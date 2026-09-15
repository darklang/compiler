// ListLiveness.fs - Representation-independent collection liveness and entry verification.

module ListLiveness

open HIR

open ListRegion

/// Region aliases are canonical identities. Opaque scalar evaluations cannot
/// access them; callbacks cannot capture them. These are value-edge contracts,
/// not permission to reorder scalar effects or to mutate a borrowed parameter.
let rec internal valueContract operation : ValueLiveness.Contract<ListId> =
    let uses =
        match operation with
        | Branch (_, _, yes, no) -> Set.union (entryLive yes Set.empty) (entryLive no Set.empty)
        | _ -> source operation |> Option.toList |> Set.ofList
    { Uses = uses; Defines = result operation |> Option.toList |> Set.ofList }
and private entryLive (FunctionalBlock block) liveAfter =
    List.foldBack (fun operation live -> ValueLiveness.liveBefore (valueContract operation) live) block.Operations liveAfter

let private hirContract operation : HIR.ValueContract =
    match operation with
    | Construct (output, Literal elements) ->
        { Inputs = []; Operands = elements; Outputs = [output] }
    | Construct (output, Repeat (count, value)) ->
        { Inputs = []; Operands = [count; value]; Outputs = [output] }
    | Transform (output, input, Map callback) ->
        { Inputs = [input]; Operands = [callback]; Outputs = [output] }
    | Transform (output, input, Reverse) ->
        { Inputs = [input]; Operands = []; Outputs = [output] }
    | Fold (output, input, initial, callback) ->
        { Inputs = [input]; Operands = [initial; callback]; Outputs = [output] }

/// No physical ownership is needed to check the region's incoming value
/// interface. The current representation permits no external collection roots.
let verifyFunctional (FunctionalRegion block) : Result<unit, string> =
    let dialect : VerifyHIR.Dialect<Operation<Transform>, FunctionalBlock> = {
        Body = fun (FunctionalBlock block) -> block
        Leaf = hirContract
    }
    VerifyHIR.verify dialect block
    |> Result.mapError (fun error -> $"List HIR: {error}")
    |> Result.bind (fun () ->
        if Set.isEmpty (entryLive block Set.empty) then Ok ()
        else Error "List HIR: external collection roots in a closed region")
