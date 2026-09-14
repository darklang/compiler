// VerifyListOwnership.fs - Independently verify region ownership, layouts, and typed edges.

module VerifyListOwnership

open HIR
open OwnedIR
open ListRegion

/// Extraction proves scalars and callbacks cannot access the canonical list
/// identities. List-specific storage facts remain here; accounting is shared.
let private semantics : Semantics<Operation<Transform * Ownership>, ListId> = {
    Leaf = function
        | Construct (output, _) -> { Inputs = []; Outputs = [output] }
        | Transform (output, input, (_, ownership)) ->
            let useMode = match ownership with Consume -> Consumed input | BorrowAndCopy -> Borrowed input
            { Inputs = [useMode]; Outputs = [output] }
        | Fold (_, input, _, _) -> { Inputs = [Borrowed input]; Outputs = [] }
    ScalarUses = fun _ -> Set.empty
}

let verifyBlockOwnership (block: OwnedBlock) : Result<unit, string> =
    VerifyOwnership.verifyClosed semantics block
    |> Result.mapError (fun error -> $"List HIR: {error}")

let verify (OwnedRegion (block, layouts)) : Result<unit, string> =
    let rec blockValid block = immediate block.Body.Result.Type && List.forall typesValid block.Body.Operations
    and typesValid step =
        match step.Operation with
        | Leaf (Construct (output, Literal elements)) ->
            elements |> List.forall (fun element -> element.Type = AST.TInt64)
            && extent (lookup "construction layout" output layouts) = ConstantLength (List.length elements)
        | Leaf (Construct (output, Repeat (count, value))) ->
            count.Type = AST.TInt && value.Type = AST.TInt64
            && extent (lookup "repeat layout" output layouts) = RuntimeLength output
        | Leaf (Transform (output, input, (operation, _))) ->
            lookup "output layout" output layouts = lookup "input layout" input layouts
            && (match operation with
                | Map callback -> callback.Type = AST.TFunction ([AST.TInt64], AST.TInt64)
                | Reverse -> true)
        | Leaf (Fold (_, _, initial, callback)) ->
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
    else verifyBlockOwnership block
