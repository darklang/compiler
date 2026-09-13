// ListHIR.fs - Typed, closed collection regions and storage/ownership elaboration.
//
// Region values never cross the existing List ABI. Scalar expressions retain
// their checked types; collection edges have distinct identities. The first
// storage classes use recyclable fixed blocks or independently reclaimable
// mappings. Large kernels are loops; small kernels have a bounded unroll cost.

module ListHIR

type ListId = ListId of int

type Scalar = { Expression: AST.Expr; Type: AST.Type }

type Transform =
    | Map of callback: Scalar
    | Reverse

type Operation<'transform> =
    | Construct of result: ListId * elements: Scalar list
    | Transform of result: ListId * source: ListId * operation: 'transform
    | Fold of name: string * source: ListId * initial: Scalar * callback: Scalar
    | ScalarBinding of name: string * value: Scalar

type FunctionalRegion = private FunctionalRegion of Operation<Transform> list * Scalar

type ArrayLayout = private RecycledArray of length:int | MappedArray of length:int

let capacity = function RecycledArray length | MappedArray length -> length
let elementOffset index = 24 + index * 8
let payloadSize layout = elementOffset (capacity layout)
let allocationSize layout = payloadSize layout + 8
let private unrollLimit = 28
// Offsets remain representable in the existing signed 32-bit layout metadata.
let private maxCapacity = (System.Int32.MaxValue - 40) / 8
let private requestedBytes layout = allocationSize layout + (match layout with MappedArray _ -> 8 | _ -> 0)

type StorageRegion = private StorageRegion of FunctionalRegion * Map<ListId, ArrayLayout>

/// Closed regions prove uniqueness statically. Consume transfers the final
/// reference; BorrowAndCopy preserves a source with surviving logical aliases.
/// Runtime uniqueness checks belong to the later escaping-array storage class.
type Ownership = Consume | BorrowAndCopy

type OwnedOperation = {
    Operation: Operation<Transform * Ownership>
    Releases: ListId list
}

type OwnedRegion = private OwnedRegion of OwnedOperation list * Scalar * Map<ListId, ArrayLayout>

type AllocationSummary = {
    Allocations: int
    AllocatedBytes: int
    Copies: int
    ReusedTransforms: int
    Releases: int
}

/// These are exact storage-operation counts for a straight-line region, not
/// counts of allocations performed inside scalar expressions or callbacks.
let allocationSummary (OwnedRegion (operations, _, layouts)) : AllocationSummary =
    operations
    |> List.fold (fun summary step ->
        let allocations, bytes, copies, reused =
            match step.Operation with
            | Construct (output, _)
            | Transform (output, _, (_, BorrowAndCopy)) ->
                let layout =
                    match Map.tryFind output layouts with
                    | Some layout -> layout
                    | None -> Crash.crash "List HIR: missing allocation layout"
                1, requestedBytes layout, (match step.Operation with Transform _ -> 1 | _ -> 0), 0
            | Transform (_, _, (_, Consume)) -> 0, 0, 0, 1
            | _ -> 0, 0, 0, 0
        { Allocations = summary.Allocations + allocations
          AllocatedBytes = summary.AllocatedBytes + bytes
          Copies = summary.Copies + copies
          ReusedTransforms = summary.ReusedTransforms + reused
          Releases = summary.Releases + List.length step.Releases })
        { Allocations = 0; AllocatedBytes = 0; Copies = 0; ReusedTransforms = 0; Releases = 0 }

type private Extraction = {
    Lists: Map<string, ListId>
    Types: Map<string, AST.Type>
    Operations: Operation<Transform> list
    NextId: int
}

let private immediate = function
    | AST.TInt64 | AST.TBool -> true
    | _ -> false

/// Recognition uses canonical, monomorphized identities, never suffix guesses.
let private listCall = function
    | AST.Call (name, args) -> Some (name, AST.NonEmptyList.toList args)
    | _ -> None

/// A failed recognition is semantic absence, not a compiler failure. The
/// original checked expression then uses the supported persistent List path.
let tryExtract
    (infer: Map<string, AST.Type> -> AST.Expr -> Result<AST.Type, string>)
    (freeVariables: AST.Expr -> Set<string>)
    (expression: AST.Expr)
    : FunctionalRegion option =
    let scalar state expr =
        let referencesList =
            freeVariables expr |> Set.exists (fun name -> Map.containsKey name state.Lists)
        if referencesList then None
        else
            match infer state.Types expr with
            | Ok typ when immediate typ -> Some { Expression = expr; Type = typ }
            | _ -> None

    let callback state expected expr =
        // A closure may not hide a region alias or an effectful destructor.
        let capturesAreImmediate =
            match expr with
            | AST.Closure (_, captures) ->
                captures |> List.forall (function
                    | AST.FuncRef _ -> true // Static code addresses, including closure comparators.
                    | capture -> Option.isSome (scalar state capture))
            | AST.FuncRef _ -> true
            | _ -> false
        if not capturesAreImmediate then None
        else
            match infer state.Types expr with
            | Ok typ when typ = expected -> Some { Expression = expr; Type = typ }
            | _ -> None

    let addList state operation =
        let id = ListId state.NextId
        id, { state with Operations = operation id :: state.Operations; NextId = state.NextId + 1 }

    let rec list state expr =
        match expr with
        | AST.Var name -> Map.tryFind name state.Lists |> Option.map (fun id -> id, state)
        | AST.ListLiteral elements when List.length elements <= maxCapacity ->
            let values = elements |> List.map (fun value -> scalar state value |> Option.filter (fun typed -> typed.Type = AST.TInt64))
            if values |> List.forall Option.isSome then
                Some (addList state (fun output -> Construct (output, List.choose id values)))
            else None
        | _ ->
            match listCall expr with
            | Some ("Stdlib.List.map_i64_i64", [input; fn]) ->
                list state input
                |> Option.bind (fun (source, next) ->
                    callback state (AST.TFunction ([AST.TInt64], AST.TInt64)) fn
                    |> Option.map (fun fn -> addList next (fun id -> Transform (id, source, Map fn))))
            | Some ("Stdlib.List.reverse_i64", [input]) ->
                list state input
                |> Option.map (fun (source, next) -> addList next (fun id -> Transform (id, source, Reverse)))
            | _ -> None

    let bindScalar state name expr =
        match listCall expr with
        | Some ("Stdlib.List.fold_i64_i64", [input; initial; fn]) ->
            list state input
            |> Option.bind (fun (source, next) ->
                match scalar state initial, callback state (AST.TFunction ([AST.TInt64; AST.TInt64], AST.TInt64)) fn with
                | Some initial, Some fn when initial.Type = AST.TInt64 ->
                    Some { next with
                             Types = Map.add name AST.TInt64 next.Types
                             Lists = Map.remove name next.Lists
                             Operations = Fold (name, source, initial, fn) :: next.Operations }
                | _ -> None)
        | _ ->
            scalar state expr
            |> Option.map (fun value ->
                { state with Types = Map.add name value.Type state.Types
                             Lists = Map.remove name state.Lists
                             Operations = ScalarBinding (name, value) :: state.Operations })

    let rec collectNames expr =
        match expr with
        | AST.Let (AST.LPVariable name, _, body) -> Set.add name (collectNames body)
        | _ -> freeVariables expression
    let rec resultName names index =
        let name = $"__list_hir_result_{index}"
        if Set.contains name names then resultName names (index + 1) else name

    let rec region finalName state expr =
        match expr with
        | AST.Let (AST.LPVariable name, value, body) ->
            match list state value with
            | Some (id, next) ->
                region finalName { next with Lists = Map.add name id next.Lists
                                             Types = Map.add name (AST.TList AST.TInt64) next.Types } body
            | None -> bindScalar state name value |> Option.bind (fun next -> region finalName next body)
        | _ ->
            bindScalar state finalName expr
            |> Option.bind (fun next ->
                if next.NextId = 0 then None
                else
                    Map.tryFind finalName next.Types
                    |> Option.map (fun typ ->
                        FunctionalRegion (List.rev next.Operations, { Expression = AST.Var finalName; Type = typ })))

    let isListOperation value =
        match listCall value with
        | Some ("Stdlib.List.map_i64_i64", _)
        | Some ("Stdlib.List.reverse_i64", _)
        | Some ("Stdlib.List.fold_i64_i64", _) -> true
        | _ -> false
    let candidate =
        match expression with
        | AST.Let (_, AST.ListLiteral _, _) -> true
        | AST.Let (_, value, _) -> isListOperation value
        | _ -> isListOperation expression
    if candidate then
        let finalName = resultName (collectNames expression) 0
        region finalName { Lists = Map.empty; Types = Map.empty; Operations = []; NextId = 0 } expression
    else None

let private lookup name key map =
    match Map.tryFind key map with
    | Some value -> value
    | None -> Crash.crash $"List HIR: missing {name} for {key}"

let private source = function
    | Transform (_, input, _) | Fold (_, input, _, _) -> Some input
    | Construct _ | ScalarBinding _ -> None

let private result = function
    | Construct (output, _) | Transform (output, _, _) -> Some output
    | Fold _ | ScalarBinding _ -> None

/// Small fixed blocks use the recycling heap; larger arrays own a mapping.
let selectStorage (FunctionalRegion (operations, _) as region) : StorageRegion =
    let layouts =
        operations
        |> List.fold (fun layouts operation ->
            match operation with
            | Construct (id, elements) ->
                let length = List.length elements
                Map.add id (if length <= unrollLimit then RecycledArray length else MappedArray length) layouts
            | Transform (id, input, _) -> Map.add id (lookup "layout" input layouts) layouts
            | Fold _ | ScalarBinding _ -> layouts) Map.empty
    StorageRegion (region, layouts)

let private useCounts operations =
    operations
    |> List.choose source
    |> List.countBy id
    |> Map.ofList

/// Last-use ownership is exact because all region aliases are canonical ListIds
/// and no list reference can escape into scalar expressions or callbacks.
let elaborateOwnership (StorageRegion (FunctionalRegion (operations, finalValue), layouts)) : OwnedRegion =
    let counts = useCounts operations
    let owned, _ =
        operations
        |> List.mapFold (fun remaining operation ->
            let ownership, releases, next =
                match source operation with
                | None -> Consume, [], remaining
                | Some input ->
                    let count = lookup "use count" input remaining
                    let next = Map.add input (count - 1) remaining
                    match operation with
                    | Transform _ -> (if count = 1 then Consume else BorrowAndCopy), [], next
                    | _ -> Consume, (if count = 1 then [input] else []), next
            let unusedOutput =
                result operation
                |> Option.filter (fun output -> not (Map.containsKey output counts))
                |> Option.toList
            let ownedOperation =
                match operation with
                | Construct (output, elements) -> Construct (output, elements)
                | Transform (output, input, transform) -> Transform (output, input, (transform, ownership))
                | Fold (name, input, initial, callback) -> Fold (name, input, initial, callback)
                | ScalarBinding (name, value) -> ScalarBinding (name, value)
            { Operation = ownedOperation; Releases = releases @ unusedOutput }, next) counts
    OwnedRegion (owned, finalValue, layouts)

/// Independent accounting check: each operation requires a live input, consumes
/// or borrows its unit, creates exactly one result unit, and releases live units.
let verifyOwnership (operations: OwnedOperation list) : Result<unit, string> =
    let rec loop declared live rest =
        match rest with
        | [] -> if Set.isEmpty live then Ok () else Error "List HIR: unreleased region values"
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
                let rec release live releases =
                    match releases with
                    | [] -> loop declared live tail
                    | value :: rest when Set.contains value live -> release (Set.remove value live) rest
                    | _ -> Error "List HIR: duplicate or invalid release"
                release afterOutput step.Releases
    loop Set.empty Set.empty operations

let verify (OwnedRegion (operations, finalValue, layouts)) : Result<unit, string> =
    let typesValid step =
        match step.Operation with
        | Construct (output, elements) ->
            elements |> List.forall (fun element -> element.Type = AST.TInt64)
            && capacity (lookup "construction layout" output layouts) = List.length elements
        | Transform (output, input, (operation, _)) ->
            lookup "output layout" output layouts = lookup "input layout" input layouts
            && (match operation with
                | Map callback -> callback.Type = AST.TFunction ([AST.TInt64], AST.TInt64)
                | Reverse -> true)
        | Fold (_, _, initial, callback) ->
            initial.Type = AST.TInt64 && callback.Type = AST.TFunction ([AST.TInt64; AST.TInt64], AST.TInt64)
        | ScalarBinding (_, value) -> immediate value.Type
    if layouts |> Map.exists (fun _ layout ->
        match layout with
        | RecycledArray length -> length < 0 || length > unrollLimit
        | MappedArray length -> length <= unrollLimit || length > maxCapacity) then
        Error "List HIR: unsupported allocation size"
    elif not (immediate finalValue.Type) || not (List.forall typesValid operations) then
        Error "List HIR: invalid storage operand types"
    else verifyOwnership operations

type LowerScalar = AST.Expr -> ANF.VarGen -> Map<string, ANF.TempId * AST.Type> -> Result<ANF.AExpr * ANF.VarGen, string>

let private word value = ANF.IntLiteral (ANF.Int64 (int64 value))

let rec private bindReturns expression continuation =
    match expression with
    | ANF.Return atom -> continuation atom
    | ANF.Let (id, value, body) -> ANF.Let (id, value, bindReturns body continuation)
    | ANF.If (condition, yes, no) -> ANF.If (condition, bindReturns yes continuation, bindReturns no continuation)

let private metadata layout : ANF.RcMetadata option =
    Some { ReleasePlanCacheKey = None; SourceType = None
           ReleasePlan = Some (ANF.RootRelease (payloadSize layout, ANF.GenericHeap, ANF.NoPayloadRelease)) }

let private emit value vg =
    let id, next = ANF.freshVar vg
    ANF.Var id, [(id, value)], next

let private write pointer offset value vg = emit (ANF.RawWriteWord (pointer, word offset, value)) vg

let private allocate layout vg =
    let size = word (allocationSize layout)
    let operation = match layout with RecycledArray _ -> ANF.RawAlloc size | MappedArray _ -> ANF.MappedAlloc size
    let pointer, allocation, next = emit operation vg
    let header = [0, capacity layout; 8, capacity layout; 16, 0; payloadSize layout, 1]
    let writes, final = header |> List.mapFold (fun state (offset, value) -> let _, bindings, next = write pointer offset (word value) state in bindings, next) next
    pointer, allocation @ List.concat writes, final

let private wrap bindings body = List.foldBack (fun (id, value) tail -> ANF.Let (id, value, tail)) bindings body

/// Lower verified storage operations to existing raw memory and RC primitives.
/// The raw pointer is never tagged as a source List or assigned a fake Blob type.
let lower (lowerScalar: LowerScalar) env vg (OwnedRegion (operations, finalValue, layouts) as region) =
    let lowerValue env vg value =
        lowerScalar value.Expression vg env
        |> Result.map (fun (expr, next) ->
            let id, final = ANF.freshVar next
            bindReturns expr (fun atom -> ANF.Let (id, ANF.TypedAtom (atom, value.Type), ANF.Return (ANF.Var id))), ANF.Var id, final)

    let release pointers values vg =
        values |> List.mapFold (fun state value ->
            let layout = lookup "release layout" value layouts
            let pointer = lookup "release pointer" value pointers
            let operation =
                match layout with
                | RecycledArray _ -> ANF.RefCountDec (pointer, payloadSize layout, ANF.GenericHeap, metadata layout)
                | MappedArray _ -> ANF.MappedFree pointer
            let _, bindings, next = emit operation state
            bindings, next) vg
        |> fun (bindings, next) -> List.concat bindings, next

    let prepareMutation pointer layout ownership vg =
        match ownership with
        | Consume -> ANF.Return pointer, vg
        | BorrowAndCopy ->
            let copy, allocations, afterAllocation = allocate layout vg
            let copied, afterCopy =
                if capacity layout > unrollLimit then
                    let _, bindings, next = emit (ANF.Call ("Stdlib.Internal.ListArray.copy", [pointer; copy; word 0; word (capacity layout)])) afterAllocation
                    [bindings], next
                else
                    [0 .. capacity layout - 1] |> List.mapFold (fun state index ->
                        let value, loads, next = emit (ANF.RawGet (pointer, word (elementOffset index), Some AST.TInt64)) state
                        let _, stores, final = write copy (elementOffset index) value next
                        loads @ stores, final) afterAllocation
            let _, initialized, final = write copy 16 (word (capacity layout)) afterCopy
            wrap (allocations @ List.concat copied @ initialized) (ANF.Return copy), final

    let rec loop env pointers vg (steps: OwnedOperation list) =
        match steps with
        | [] -> lowerScalar finalValue.Expression vg env
        | step :: rest ->
            let lowerRest env pointers vg =
                let releases, next = release pointers step.Releases vg
                loop env pointers next rest |> Result.map (fun (body, final) -> wrap releases body, final)
            match step.Operation with
            | ScalarBinding (name, value) ->
                lowerValue env vg value
                |> Result.bind (fun (expr, atom, next) ->
                    match atom with
                    | ANF.Var id ->
                        lowerRest (Map.add name (id, value.Type) env) pointers next
                        |> Result.map (fun (body, final) -> bindReturns expr (fun _ -> body), final)
                    | _ -> Crash.crash "List HIR: scalar lowering must bind its result")
            | Construct (output, elements) ->
                let rec evaluate vg expressions values =
                    match expressions with
                    | [] -> Ok (ANF.Return ANF.UnitLiteral, List.rev values, vg)
                    | value :: tail ->
                        lowerValue env vg value |> Result.bind (fun (expr, atom, next) ->
                            evaluate next tail (atom :: values)
                            |> Result.map (fun (body, atoms, final) -> bindReturns expr (fun _ -> body), atoms, final))
                evaluate vg elements [] |> Result.bind (fun (evaluation, atoms, next) ->
                    let layout = lookup "construction layout" output layouts
                    let pointer, allocation, afterAllocation = allocate layout next
                    let writes, afterWrites = atoms |> List.mapi (fun index atom -> index, atom) |> List.mapFold (fun state (index, atom) -> let _, bindings, next = write pointer (elementOffset index) atom state in bindings, next) afterAllocation
                    let _, initialized, afterInit = write pointer 16 (word (capacity layout)) afterWrites
                    lowerRest env (Map.add output pointer pointers) afterInit
                    |> Result.map (fun (body, final) -> bindReturns evaluation (fun _ -> wrap (allocation @ List.concat writes @ initialized) body), final))
            | Transform (output, input, (operation, ownership)) ->
                let layout = lookup "transform layout" input layouts
                let pointer = lookup "transform pointer" input pointers
                let callback =
                    match operation with
                    | Reverse -> Ok (ANF.Return ANF.UnitLiteral, ANF.UnitLiteral, vg)
                    | Map fn -> lowerValue env vg fn
                callback |> Result.bind (fun (evaluation, fn, next) ->
                    let preparation, afterPreparation = prepareMutation pointer layout ownership next
                    let destination, afterDestination = ANF.freshVar afterPreparation
                    let target = ANF.Var destination
                    let mutations, afterMutation =
                        match operation with
                        | Map _ when capacity layout > unrollLimit ->
                            let _, bindings, next = emit (ANF.Call ("Stdlib.Internal.ListArray.map", [target; word 0; word (capacity layout); fn])) afterDestination
                            [bindings], next
                        | Reverse when capacity layout > unrollLimit ->
                            let _, bindings, next = emit (ANF.Call ("Stdlib.Internal.ListArray.reverse", [target; word 0; word (capacity layout - 1)])) afterDestination
                            [bindings], next
                        | Map _ ->
                            [0 .. capacity layout - 1] |> List.mapFold (fun state index ->
                                let value, load, next = emit (ANF.RawGet (target, word (elementOffset index), Some AST.TInt64)) state
                                let mapped, call, afterCall = emit (ANF.ClosureCall (fn, [value])) next
                                let _, store, final = write target (elementOffset index) mapped afterCall
                                load @ call @ store, final) afterDestination
                        | Reverse ->
                            [0 .. capacity layout / 2 - 1] |> List.mapFold (fun state index ->
                                let other = capacity layout - 1 - index
                                let left, leftLoad, next = emit (ANF.RawGet (target, word (elementOffset index), Some AST.TInt64)) state
                                let right, rightLoad, afterRight = emit (ANF.RawGet (target, word (elementOffset other), Some AST.TInt64)) next
                                let _, leftWrite, afterLeft = write target (elementOffset index) right afterRight
                                let _, rightWrite, final = write target (elementOffset other) left afterLeft
                                leftLoad @ rightLoad @ leftWrite @ rightWrite, final) afterDestination
                    lowerRest env (Map.add output target pointers) afterMutation
                    |> Result.map (fun (body, final) ->
                        bindReturns evaluation (fun _ ->
                            bindReturns preparation (fun selected ->
                                ANF.Let (destination, ANF.TypedAtom (selected, AST.TRawPtr), wrap (List.concat mutations) body))), final))
            | Fold (name, input, initial, fn) ->
                lowerValue env vg initial |> Result.bind (fun (initialExpr, accumulator, next) ->
                    lowerValue env next fn |> Result.bind (fun (callbackExpr, callback, afterCallback) ->
                        let layout = lookup "fold layout" input layouts
                        let pointer = lookup "fold pointer" input pointers
                        let bindings, (value, afterFold) =
                            if capacity layout > unrollLimit then
                                let result, calls, next = emit (ANF.Call ("Stdlib.Internal.ListArray.fold", [pointer; word 0; word (capacity layout); accumulator; callback])) afterCallback
                                [calls], (result, next)
                            else
                                [0 .. capacity layout - 1] |> List.mapFold (fun (acc, state) index ->
                                    let element, loads, next = emit (ANF.RawGet (pointer, word (elementOffset index), Some AST.TInt64)) state
                                    let result, calls, final = emit (ANF.ClosureCall (callback, [acc; element])) next
                                    loads @ calls, (result, final)) (accumulator, afterCallback)
                        let id, afterId = ANF.freshVar afterFold
                        lowerRest (Map.add name (id, AST.TInt64) env) pointers afterId
                        |> Result.map (fun (body, final) ->
                            bindReturns initialExpr (fun _ -> bindReturns callbackExpr (fun _ ->
                                wrap (List.concat bindings) (ANF.Let (id, ANF.TypedAtom (value, AST.TInt64), body)))), final)))

    verify region |> Result.bind (fun () -> loop env Map.empty vg operations)
