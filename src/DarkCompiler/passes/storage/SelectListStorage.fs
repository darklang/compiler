// SelectListStorage.fs - Choose fixed-block or mapped storage for closed list regions.

module SelectListStorage

open ListRegion

/// Small fixed blocks use the recycling heap; larger arrays own a mapping.
let selectStorage (FunctionalRegion block as region) : StorageRegion =
    let rec select layouts (FunctionalBlock block) =
        block.Operations
        |> List.fold (fun layouts operation ->
            match operation with
            | Construct (id, Literal elements) ->
                let length = List.length elements
                Map.add id (if length <= recycledCapacityLimit then RecycledArray length else MappedArray length) layouts
            | Construct (id, Repeat _) -> Map.add id (RuntimeArray id) layouts
            | Transform (id, input, _) -> Map.add id (lookup "layout" input layouts) layouts
            | Branch (_, _, yes, no) -> select (select layouts yes) no
            | Fold _ | ScalarBinding _ -> layouts) layouts
    StorageRegion (region, select Map.empty block)
