// VerifyListOwnership.fs - Independently verify region ownership, layouts, and typed edges.

module VerifyListOwnership

open ListRegion

/// Independent accounting check: each operation requires a live input, consumes
/// or borrows its unit, creates exactly one result unit, and releases live units.
let verifyOwnership (operations: OwnedOperation list) : Result<unit, string> =
    let rec release live = function
        | [] -> Ok live
        | value :: rest when Set.contains value live -> release (Set.remove value live) rest
        | _ -> Error "List HIR: duplicate or invalid release"
    let rec loop declared live rest =
        match rest with
        | [] -> Ok (declared, live)
        | { Operation = Branch (_, _, yes, no); Releases = releases } :: tail ->
            checkBlock declared live yes |> Result.bind (fun (afterYes, yesLive) ->
                checkBlock afterYes live no |> Result.bind (fun (afterNo, noLive) ->
                    if yesLive <> noLive then Error "List HIR: inconsistent ownership at branch join"
                    else release yesLive releases |> Result.bind (fun live -> loop afterNo live tail)))
        | step :: tail ->
            let inputValid = source step.Operation |> Option.forall (fun input -> Set.contains input live)
            let outputFresh = result step.Operation |> Option.forall (fun output -> not (Set.contains output declared))
            if not inputValid || not outputFresh then Error "List HIR: invalid value lifetime"
            else
                let afterInput =
                    match step.Operation with
                    | Transform (_, input, (_, Consume)) -> Set.remove input live
                    | _ -> live
                let afterOutput, declared =
                    match result step.Operation with
                    | Some output -> Set.add output afterInput, Set.add output declared
                    | None -> afterInput, declared
                release afterOutput step.Releases |> Result.bind (fun live -> loop declared live tail)
    and checkBlock declared live block =
        release live block.EntryReleases |> Result.bind (fun live -> loop declared live block.Body.Operations)
    loop Set.empty Set.empty operations
    |> Result.bind (fun (_, live) -> if Set.isEmpty live then Ok () else Error "List HIR: unreleased region values")

let verify (OwnedRegion (block, layouts)) : Result<unit, string> =
    let rec blockValid block = immediate block.Body.Result.Type && List.forall typesValid block.Body.Operations
    and typesValid step =
        match step.Operation with
        | Construct (output, Literal elements) ->
            elements |> List.forall (fun element -> element.Type = AST.TInt64)
            && extent (lookup "construction layout" output layouts) = ConstantLength (List.length elements)
        | Construct (output, Repeat (count, value)) ->
            count.Type = AST.TInt && value.Type = AST.TInt64
            && extent (lookup "repeat layout" output layouts) = RuntimeLength output
        | Transform (output, input, (operation, _)) ->
            lookup "output layout" output layouts = lookup "input layout" input layouts
            && (match operation with
                | Map callback -> callback.Type = AST.TFunction ([AST.TInt64], AST.TInt64)
                | Reverse -> true)
        | Fold (_, _, initial, callback) ->
            initial.Type = AST.TInt64 && callback.Type = AST.TFunction ([AST.TInt64; AST.TInt64], AST.TInt64)
        | ScalarBinding (_, value) -> immediate value.Type
        | Branch (_, condition, yes, no) ->
            condition.Type = AST.TBool && yes.Body.Result.Type = no.Body.Result.Type
            && blockValid yes && blockValid no
    if layouts |> Map.exists (fun _ layout ->
        match layout with
        | RecycledArray length -> length < 0 || length > recycledCapacityLimit
        | MappedArray length -> length <= recycledCapacityLimit || length > maxCapacity
        | RuntimeArray _ -> false) then
        Error "List HIR: unsupported allocation size"
    elif not (blockValid block) then
        Error "List HIR: invalid storage operand types"
    elif not (List.isEmpty block.EntryReleases) then Error "List HIR: root cannot release incoming values"
    else verifyOwnership block.Body.Operations
