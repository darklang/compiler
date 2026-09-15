// VerifyOwnership.fs - Verify closed structured regions using dialect ownership contracts.

module VerifyOwnership

open OwnedIR

/// Definitions are globally fresh even across mutually exclusive branches;
/// live ownership is path-local and must agree at every shared continuation.
/// Borrowed parameters, escaping results, loops, and RC credits are not modeled
/// by this closed-region interface and require separate boundary contracts.
let verifyClosed (semantics: Semantics<'leaf, 'id>) (root: Block<'leaf, 'id>) =
    let rec release live = function
        | [] -> Ok live
        | value :: rest when Set.contains value live -> release (Set.remove value live) rest
        | value :: _ -> Error (InvalidRelease value)
    let rec define declared live = function
        | [] -> Ok (declared, live)
        | value :: _ when Set.contains value declared -> Error (DuplicateDefinition value)
        | value :: rest -> define (Set.add value declared) (Set.add value live) rest
    let require live uses =
        match Set.difference uses live |> Set.toList with
        | [] -> Ok ()
        | value :: _ -> Error (InvalidUse value)
    let scalar live operand = require live (semantics.ScalarUses operand)
    let managed value =
        match semantics.BlockArgument value with
        | Unmanaged -> None
        | Managed id -> Some id
    let requireManaged live value =
        match managed value with
        | None -> Ok ()
        | Some id -> require live (Set.singleton id)
    let leaf declared live operation =
        let contract = semantics.Leaf operation
        let uses = contract.Inputs |> List.map (function Borrowed id | Consumed id -> id) |> Set.ofList
        let consumes = contract.Inputs |> List.choose (function Consumed id -> Some id | Borrowed _ -> None)
        require live uses |> Result.bind (fun () ->
            release live consumes |> Result.bind (fun live -> define declared live contract.Outputs))
    let rec loop declared live = function
        | [] -> Ok (declared, live)
        | step :: rest ->
            let after =
                match step.Operation with
                | HIR.Leaf operation -> leaf declared live operation
                | HIR.ScalarBinding (_, value) -> scalar live value |> Result.map (fun () -> declared, live)
                | HIR.Branch (result, condition, yes, no) ->
                    scalar live condition |> Result.bind (fun () ->
                        block declared live yes |> Result.bind (fun (afterYes, yesLive, yesResult) ->
                            block afterYes live no |> Result.bind (fun (afterNo, noLive, noResult) ->
                                match managed yesResult, managed noResult, managed result with
                                | None, None, None ->
                                    if yesLive <> noLive then Error InconsistentJoin
                                    else Ok (afterNo, yesLive)
                                | Some yesId, Some noId, Some resultId ->
                                    release yesLive [yesId] |> Result.bind (fun yesRemainder ->
                                        release noLive [noId] |> Result.bind (fun noRemainder ->
                                            if yesRemainder <> noRemainder then Error InconsistentJoin
                                            else define afterNo yesRemainder [resultId]))
                                | _ -> Error InconsistentBlockArgument)))
            after |> Result.bind (fun (declared, live) ->
                release live step.Releases |> Result.bind (fun live -> loop declared live rest))
    and block declared live body =
        release live body.EntryReleases |> Result.bind (fun live ->
            loop declared live body.Body.Operations |> Result.bind (fun (declared, live) ->
                requireManaged live body.Body.Result
                |> Result.map (fun () -> declared, live, body.Body.Result)))
    block Set.empty Set.empty root |> Result.bind (fun (_, live, _) ->
        if Set.isEmpty live then Ok () else Error (UnreleasedValues live))
