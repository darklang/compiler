// ElaborateListOwnership.fs - Solve consuming uses and place branch-sensitive releases.

module ElaborateListOwnership

open HIR
open OwnedIR

open ListRegion
open ListLiveness

/// Solve backwards through explicit joins. A continuation's live values must
/// survive both paths; values needed by only one path die on the other edge.
let elaborateOwnership (StorageRegion (FunctionalRegion block, layouts)) : OwnedRegion =
    let rec elaborate (FunctionalBlock block) liveAfter : OwnedBlock * Set<ListId> =
        let operations, liveBefore =
            List.foldBack (fun operation (tail, live) ->
                let ownedOperation, releases, before =
                    match operation with
                    | Branch (name, condition, yes, no) ->
                        let yes, yesLive = elaborate yes live
                        let no, noLive = elaborate no live
                        let before = Set.union yesLive noLive
                        let edge branch required =
                            { branch with EntryReleases = Set.difference before required |> Set.toList }
                        Branch (name, condition, edge yes yesLive, edge no noLive), [], before
                    | _ ->
                        let unusedOutput = result operation |> Option.filter (fun output -> not (Set.contains output live)) |> Option.toList
                        let owned, releases =
                            match operation with
                            | Leaf (Construct (output, construction)) -> Leaf (Construct (output, construction)), []
                            | Leaf (Transform (output, input, transform)) ->
                                let ownership = if Set.contains input live then BorrowAndCopy else Consume
                                Leaf (Transform (output, input, (transform, ownership))), []
                            | Leaf (Fold (name, input, initial, callback)) ->
                                Leaf (Fold (name, input, initial, callback)), (if Set.contains input live then [] else [input])
                            | ScalarBinding (name, value) -> ScalarBinding (name, value), []
                            | Branch _ -> Crash.crash "List HIR: branch handled before leaf ownership"
                        owned, releases @ unusedOutput, ValueLiveness.liveBefore (valueContract operation) live
                { Operation = ownedOperation; Releases = releases } :: tail, before)
                block.Operations ([], liveAfter)
        { EntryReleases = []; Body = { Operations = operations; Result = block.Result } }, liveBefore
    let owned, _ = elaborate block Set.empty
    OwnedRegion (owned, layouts)
