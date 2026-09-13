// ListHIR.fs - Typed, closed collection regions and storage/ownership elaboration.
//
// Region values never cross the existing List ABI. Scalar expressions retain
// their checked types; collection edges have distinct identities. The first
// storage classes use recyclable fixed blocks or independently reclaimable
// mappings. Large kernels are loops; small kernels have a bounded unroll cost.

module ListHIR

type ListId = ListId of int

type Scalar = SemanticIR.Operand

type Transform =
    | Map of callback: Scalar
    | Reverse

type Construction =
    | Literal of elements: Scalar list
    | Repeat of count: Scalar * value: Scalar

type Operation<'transform, 'block> =
    | Construct of result: ListId * construction: Construction
    | Transform of result: ListId * source: ListId * operation: 'transform
    | Fold of name: string * source: ListId * initial: Scalar * callback: Scalar
    | ScalarBinding of name: string * value: Scalar
    | Branch of name: string * condition: Scalar * ifTrue: 'block * ifFalse: 'block

type FunctionalBlock = private FunctionalBlock of SemanticIR.Block<Operation<Transform, FunctionalBlock>>
type FunctionalRegion = private FunctionalRegion of FunctionalBlock

/// Runtime extent identity survives aliases and consuming transformations.
/// It names a construction, never a source variable that could be rebound.
type ArrayExtent = ConstantLength of int | RuntimeLength of ListId

type ArrayLayout = private RecycledArray of length:int | MappedArray of length:int | RuntimeArray of origin:ListId

let extent = function
    | RecycledArray length | MappedArray length -> ConstantLength length
    | RuntimeArray origin -> RuntimeLength origin
let elementOffset index = 24 + index * 8
let payloadSize length = elementOffset length
let allocationSize length = payloadSize length + 8
let private recycledCapacityLimit = 28
// Literal offsets remain representable in signed 32-bit layout metadata.
// Runtime extents instead use checked 64-bit arithmetic in the constructor.
let private maxCapacity = (System.Int32.MaxValue - 40) / 8

/// Exact piecewise requested-byte budget, excluding OS page rounding.
/// Each runtime buffer costs 256 bytes for n <= 28, otherwise 40 + 8*n.
/// Terms count physical buffers with the same validated construction extent.
type AllocationBytes = {
    ConstantBytes: int64
    RuntimeBuffers: Map<ListId, int64>
}

let private constantBytes value = { ConstantBytes = value; RuntimeBuffers = Map.empty }
let private requestedBytes = function
    | RecycledArray length -> constantBytes (int64 (allocationSize length))
    | MappedArray length -> constantBytes (int64 (allocationSize length) + 8L)
    | RuntimeArray origin -> { ConstantBytes = 0L; RuntimeBuffers = Map.ofList [origin, 1L] }

let private addBytes left right =
    let coefficients =
        Map.fold (fun terms origin coefficient ->
            Map.change origin (fun current -> Some (coefficient + Option.defaultValue 0L current)) terms)
            left.RuntimeBuffers right.RuntimeBuffers
    { ConstantBytes = left.ConstantBytes + right.ConstantBytes; RuntimeBuffers = coefficients }

type StorageRegion = private StorageRegion of FunctionalRegion * Map<ListId, ArrayLayout>

/// Closed regions prove uniqueness statically. Consume transfers the final
/// reference; BorrowAndCopy preserves a source with surviving logical aliases.
/// Runtime uniqueness checks belong to the later escaping-array storage class.
type Ownership = Consume | BorrowAndCopy

type OwnedOperation = {
    Operation: Operation<Transform * Ownership, OwnedBlock>
    Releases: ListId list
}
and OwnedBlock = {
    EntryReleases: ListId list
    Body: SemanticIR.Block<OwnedOperation>
}

type OwnedRegion = private OwnedRegion of OwnedBlock * Map<ListId, ArrayLayout>

type AllocationSummary = {
    Allocations: int
    AllocatedBytes: AllocationBytes
    Copies: int
    ReusedTransforms: int
    Releases: int
}

/// Preserve alternatives and their continuation without enumerating paths or
/// adding mutually exclusive costs. Callback/scalar allocations are excluded.
type AllocationBudget =
    | Complete of AllocationSummary
    | Conditional of prefix: AllocationSummary * ifTrue: AllocationBudget * ifFalse: AllocationBudget * continuation: AllocationBudget

let allocationBudget (OwnedRegion (block, layouts)) : AllocationBudget =
    let empty = { Allocations = 0; AllocatedBytes = constantBytes 0L; Copies = 0; ReusedTransforms = 0; Releases = 0 }
    let rec budget block =
        loop { empty with Releases = List.length block.EntryReleases } block.Body.Operations
    and loop summary operations =
        match operations with
        | [] -> Complete summary
        | { Operation = Branch (_, _, yes, no); Releases = releases } :: rest ->
            Conditional (summary, budget yes, budget no, loop { empty with Releases = List.length releases } rest)
        | step :: rest ->
            let allocations, bytes, copies, reused =
                match step.Operation with
                | Construct (output, _)
                | Transform (output, _, (_, BorrowAndCopy)) ->
                    let layout =
                        match Map.tryFind output layouts with
                        | Some layout -> layout
                        | None -> Crash.crash "List HIR: missing allocation layout"
                    1, requestedBytes layout, (match step.Operation with Transform _ -> 1 | _ -> 0), 0
                | Transform (_, _, (_, Consume)) -> 0, constantBytes 0L, 0, 1
                | _ -> 0, constantBytes 0L, 0, 0
            loop { Allocations = summary.Allocations + allocations
                   AllocatedBytes = addBytes summary.AllocatedBytes bytes
                   Copies = summary.Copies + copies
                   ReusedTransforms = summary.ReusedTransforms + reused
                   Releases = summary.Releases + List.length step.Releases } rest
    budget block

type private Extraction = {
    Lists: Map<string, ListId>
    Types: Map<string, AST.Type>
    Operations: Operation<Transform, FunctionalBlock> list
    NextId: int
    Paths: int
}

// ANF is still a continuation tree. Bound duplication until explicit join
// blocks reach the downstream IR; this limit is on expanded paths, not depth.
let private maxLoweredPaths = 16

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
    let operand state accepts expr : Scalar option =
        let referencesList =
            freeVariables expr |> Set.exists (fun name -> Map.containsKey name state.Lists)
        if referencesList then None
        else
            match infer state.Types expr with
            | Ok typ when accepts typ -> Some { Expression = expr; Type = typ }
            | _ -> None

    let scalar state expr = operand state immediate expr

    let callback state expected expr : Scalar option =
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
                Some (addList state (fun output -> Construct (output, Literal (List.choose id values))))
            else None
        | _ ->
            match listCall expr with
            | Some ("Stdlib.List.repeatUnsafe_i64", [count; value]) ->
                match operand state ((=) AST.TInt) count, operand state ((=) AST.TInt64) value with
                | Some count, Some value -> Some (addList state (fun output -> Construct (output, Repeat (count, value))))
                | _ -> None
            | Some ("Stdlib.List.map_i64_i64", [input; fn]) ->
                list state input
                |> Option.bind (fun (source, next) ->
                    callback state (AST.TFunction ([AST.TInt64], AST.TInt64)) fn
                    |> Option.map (fun fn -> addList next (fun id -> Transform (id, source, Map fn))))
            | Some ("Stdlib.List.reverse_i64", [input]) ->
                list state input
                |> Option.map (fun (source, next) -> addList next (fun id -> Transform (id, source, Reverse)))
            | _ -> None

    let rec bindScalar state name expr =
        match expr with
        | AST.If (condition, yes, no) ->
            operand state ((=) AST.TBool) condition
            |> Option.bind (fun condition ->
                region name { state with Operations = []; Paths = 1 } yes
                |> Option.bind (fun (FunctionalBlock yes as yesBlock, afterYes, yesPaths) ->
                    region name { state with Operations = []; NextId = afterYes; Paths = 1 } no
                    |> Option.bind (fun (FunctionalBlock no as noBlock, afterNo, noPaths) ->
                        let paths = state.Paths * (yesPaths + noPaths)
                        if yes.Result.Type <> no.Result.Type || paths > maxLoweredPaths then None
                        else Some { state with
                                      NextId = afterNo
                                      Paths = paths
                                      Types = Map.add name yes.Result.Type state.Types
                                      Lists = Map.remove name state.Lists
                                      Operations = Branch (name, condition, yesBlock, noBlock) :: state.Operations })))
        | _ -> bindSimpleScalar state name expr

    and bindSimpleScalar state name expr =
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

    and region finalName state expr =
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
                Map.tryFind finalName next.Types
                |> Option.map (fun typ ->
                    FunctionalBlock { Operations = List.rev next.Operations
                                      Result = { Expression = AST.Var finalName; Type = typ } }, next.NextId, next.Paths))

    let rec collectNames expr =
        match expr with
        | AST.Let (AST.LPVariable name, value, body) -> Set.add name (Set.union (collectNames value) (collectNames body))
        | AST.If (condition, yes, no) -> Set.unionMany [collectNames condition; collectNames yes; collectNames no]
        | _ -> freeVariables expr
    let rec resultName names index =
        let name = $"__list_hir_result_{index}"
        if Set.contains name names then resultName names (index + 1) else name

    let isListOperation value =
        match listCall value with
        | Some ("Stdlib.List.map_i64_i64", _)
        | Some ("Stdlib.List.reverse_i64", _)
        | Some ("Stdlib.List.repeatUnsafe_i64", _)
        | Some ("Stdlib.List.fold_i64_i64", _) -> true
        | _ -> false
    let candidate =
        match expression with
        | AST.Let (_, AST.ListLiteral _, _) -> true
        | AST.Let (_, value, _) -> isListOperation value
        | _ -> isListOperation expression
    if candidate then
        let finalName = resultName (collectNames expression) 0
        region finalName { Lists = Map.empty; Types = Map.empty; Operations = []; NextId = 0; Paths = 1 } expression
        |> Option.bind (fun (block, nextId, _) -> if nextId = 0 then None else Some (FunctionalRegion block))
    else None

let private lookup name key map =
    match Map.tryFind key map with
    | Some value -> value
    | None -> Crash.crash $"List HIR: missing {name} for {key}"

let private source = function
    | Transform (_, input, _) | Fold (_, input, _, _) -> Some input
    | Construct _ | ScalarBinding _ | Branch _ -> None

let private result = function
    | Construct (output, _) | Transform (output, _, _) -> Some output
    | Fold _ | ScalarBinding _ | Branch _ -> None

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

/// Region aliases are canonical identities. Opaque scalar evaluations cannot
/// access them; callbacks cannot capture them. These are value-edge contracts,
/// not permission to reorder scalar effects or to mutate a borrowed parameter.
let rec private valueContract operation : SemanticIR.ValueContract<ListId> =
    let uses =
        match operation with
        | Branch (_, _, yes, no) -> Set.union (entryLive yes Set.empty) (entryLive no Set.empty)
        | _ -> source operation |> Option.toList |> Set.ofList
    { Uses = uses; Defines = result operation |> Option.toList |> Set.ofList }
and private entryLive (FunctionalBlock block) liveAfter =
    List.foldBack (fun operation live -> SemanticIR.liveBefore (valueContract operation) live) block.Operations liveAfter

/// No physical ownership is needed to check the region's incoming value
/// interface. The current representation permits no external collection roots.
let verifyFunctional (FunctionalRegion block) : Result<unit, string> =
    if Set.isEmpty (entryLive block Set.empty) then Ok ()
    else Error "List HIR: external collection roots in a closed region"

/// Solve backwards through explicit joins. A continuation's live values must
/// survive both paths; values needed by only one path die on the other edge.
let elaborateOwnership (StorageRegion (FunctionalRegion block, layouts)) : OwnedRegion =
    let rec elaborate (FunctionalBlock block) liveAfter =
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
                            | Construct (output, construction) -> Construct (output, construction), []
                            | Transform (output, input, transform) ->
                                let ownership = if Set.contains input live then BorrowAndCopy else Consume
                                Transform (output, input, (transform, ownership)), []
                            | Fold (name, input, initial, callback) ->
                                Fold (name, input, initial, callback), (if Set.contains input live then [] else [input])
                            | ScalarBinding (name, value) -> ScalarBinding (name, value), []
                            | Branch _ -> Crash.crash "List HIR: branch handled before leaf ownership"
                        owned, releases @ unusedOutput, SemanticIR.liveBefore (valueContract operation) live
                { Operation = ownedOperation; Releases = releases } :: tail, before)
                block.Operations ([], liveAfter)
        { EntryReleases = []; Body = { Operations = operations; Result = block.Result } }, liveBefore
    let owned, _ = elaborate block Set.empty
    OwnedRegion (owned, layouts)

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

type LowerScalar = AST.Expr -> ANF.VarGen -> Map<string, ANF.TempId * AST.Type> -> Result<ANF.AExpr * ANF.VarGen, string>

let private word value = ANF.IntLiteral (ANF.Int64 (int64 value))

let rec private bindReturns expression continuation =
    match expression with
    | ANF.Return atom -> continuation atom
    | ANF.Let (id, value, body) -> ANF.Let (id, value, bindReturns body continuation)
    | ANF.If (condition, yes, no) -> ANF.If (condition, bindReturns yes continuation, bindReturns no continuation)

let private metadata length : ANF.RcMetadata option =
    Some { ReleasePlanCacheKey = None; SourceType = None
           ReleasePlan = Some (ANF.RootRelease (payloadSize length, ANF.GenericHeap, ANF.NoPayloadRelease)) }

/// The runtime allocator's small branch reserves the entire 256-byte class;
/// its RC word follows capacity, not the runtime logical length.
let releaseRuntimeSmall pointer =
    ANF.RefCountDec (pointer, payloadSize recycledCapacityLimit, ANF.GenericHeap, metadata recycledCapacityLimit)

let private emit value vg =
    let id, next = ANF.freshVar vg
    ANF.Var id, [(id, value)], next

let private write pointer offset value vg = emit (ANF.RawWriteWord (pointer, word offset, value)) vg

type private Buffer = { Pointer: ANF.Atom; Length: ANF.Atom; Layout: ArrayLayout }

let private allocate layout length vg =
    let allocateConstant count primitive =
        let pointer, allocation, next = emit (primitive (word (allocationSize count))) vg
        let header = [0, count; 8, count; 16, 0; payloadSize count, 1]
        let writes, final = header |> List.mapFold (fun state (offset, value) -> let _, bindings, next = write pointer offset (word value) state in bindings, next) next
        pointer, allocation @ List.concat writes, final
    match layout with
    | RuntimeArray _ -> emit (ANF.Call ("Stdlib.List.__arrayAllocate", [length])) vg
    | RecycledArray count -> allocateConstant count ANF.RawAlloc
    | MappedArray count -> allocateConstant count ANF.MappedAlloc

let private wrap bindings body = List.foldBack (fun (id, value) tail -> ANF.Let (id, value, tail)) bindings body

/// Lower verified storage operations to existing raw memory and RC primitives.
/// The raw pointer is never tagged as a source List or assigned a fake Blob type.
let lower (lowerScalar: LowerScalar) env vg (OwnedRegion (block, layouts) as region) =
    let lowerValue env vg (value: Scalar) =
        lowerScalar value.Expression vg env
        |> Result.map (fun (expr, next) ->
            let id, final = ANF.freshVar next
            bindReturns expr (fun atom -> ANF.Let (id, ANF.TypedAtom (atom, value.Type), ANF.Return (ANF.Var id))), ANF.Var id, final)

    let release buffers values vg =
        values |> List.mapFold (fun state value ->
            let buffer = lookup "release buffer" value buffers
            let operation =
                match buffer.Layout with
                | RecycledArray length -> ANF.RefCountDec (buffer.Pointer, payloadSize length, ANF.GenericHeap, metadata length)
                | MappedArray _ -> ANF.MappedFree buffer.Pointer
                | RuntimeArray _ -> ANF.Call ("Stdlib.List.__arrayRelease", [buffer.Pointer])
            let _, bindings, next = emit operation state
            bindings, next) vg
        |> fun (bindings, next) -> List.concat bindings, next

    let prepareMutation buffer ownership vg =
        match ownership with
        | Consume -> ANF.Return buffer.Pointer, vg
        | BorrowAndCopy ->
            let copy, allocations, afterAllocation = allocate buffer.Layout buffer.Length vg
            let copied, afterCopy =
                match buffer.Layout with
                | MappedArray _ | RuntimeArray _ ->
                    let _, bindings, next = emit (ANF.Call ("Stdlib.List.__arrayCopy", [buffer.Pointer; copy; word 0; buffer.Length])) afterAllocation
                    [bindings], next
                | RecycledArray length ->
                    [0 .. length - 1] |> List.mapFold (fun state index ->
                        let value, loads, next = emit (ANF.RawGet (buffer.Pointer, word (elementOffset index), Some AST.TInt64)) state
                        let _, stores, final = write copy (elementOffset index) value next
                        loads @ stores, final) afterAllocation
            let _, initialized, final = write copy 16 buffer.Length afterCopy
            wrap (allocations @ List.concat copied @ initialized) (ANF.Return copy), final

    let rec lowerBlock env buffers vg block =
        let releases, next = release buffers block.EntryReleases vg
        loop block.Body.Result env buffers next block.Body.Operations
        |> Result.map (fun (body, final) -> wrap releases body, final)
    and loop finalValue env buffers vg (steps: OwnedOperation list) =
        match steps with
        | [] -> lowerScalar finalValue.Expression vg env
        | step :: rest ->
            let lowerRest env buffers vg =
                let releases, next = release buffers step.Releases vg
                loop finalValue env buffers next rest |> Result.map (fun (body, final) -> wrap releases body, final)
            match step.Operation with
            | Branch (name, condition, yes, no) ->
                lowerValue env vg condition |> Result.bind (fun (evaluation, condition, next) ->
                    lowerBlock env buffers next yes |> Result.bind (fun (yesExpr, afterYes) ->
                        lowerBlock env buffers afterYes no |> Result.bind (fun (noExpr, afterNo) ->
                            let joined, afterJoin = ANF.freshVar afterNo
                            let typ = yes.Body.Result.Type
                            lowerRest (Map.add name (joined, typ) env) buffers afterJoin
                            |> Result.map (fun (body, final) ->
                                let join atom = ANF.Let (joined, ANF.TypedAtom (atom, typ), body)
                                bindReturns evaluation (fun _ -> ANF.If (condition, bindReturns yesExpr join, bindReturns noExpr join)), final))))
            | ScalarBinding (name, value) ->
                lowerValue env vg value
                |> Result.bind (fun (expr, atom, next) ->
                    match atom with
                    | ANF.Var id ->
                        lowerRest (Map.add name (id, value.Type) env) buffers next
                        |> Result.map (fun (body, final) -> bindReturns expr (fun _ -> body), final)
                    | _ -> Crash.crash "List HIR: scalar lowering must bind its result")
            | Construct (output, Repeat (count, value)) ->
                lowerValue env vg count |> Result.bind (fun (countExpr, countAtom, afterCount) ->
                    lowerValue env afterCount value |> Result.bind (fun (valueExpr, valueAtom, afterValue) ->
                        let pointer, allocation, afterAllocation = emit (ANF.Call ("Stdlib.List.__arrayRepeat", [countAtom; valueAtom])) afterValue
                        let length, load, afterLoad = emit (ANF.RawGet (pointer, word 0, Some AST.TInt64)) afterAllocation
                        let buffer = { Pointer = pointer; Length = length; Layout = lookup "construction layout" output layouts }
                        lowerRest env (Map.add output buffer buffers) afterLoad
                        |> Result.map (fun (body, final) ->
                            bindReturns countExpr (fun _ -> bindReturns valueExpr (fun _ -> wrap (allocation @ load) body)), final)))
            | Construct (output, Literal elements) ->
                let rec evaluate vg expressions values =
                    match expressions with
                    | [] -> Ok (ANF.Return ANF.UnitLiteral, List.rev values, vg)
                    | value :: tail ->
                        lowerValue env vg value |> Result.bind (fun (expr, atom, next) ->
                            evaluate next tail (atom :: values)
                            |> Result.map (fun (body, atoms, final) -> bindReturns expr (fun _ -> body), atoms, final))
                evaluate vg elements [] |> Result.bind (fun (evaluation, atoms, next) ->
                    let layout = lookup "construction layout" output layouts
                    let length = word (List.length elements)
                    let pointer, allocation, afterAllocation = allocate layout length next
                    let writes, afterWrites = atoms |> List.mapi (fun index atom -> index, atom) |> List.mapFold (fun state (index, atom) -> let _, bindings, next = write pointer (elementOffset index) atom state in bindings, next) afterAllocation
                    let _, initialized, afterInit = write pointer 16 length afterWrites
                    let buffer = { Pointer = pointer; Length = length; Layout = layout }
                    lowerRest env (Map.add output buffer buffers) afterInit
                    |> Result.map (fun (body, final) -> bindReturns evaluation (fun _ -> wrap (allocation @ List.concat writes @ initialized) body), final))
            | Transform (output, input, (operation, ownership)) ->
                let buffer = lookup "transform buffer" input buffers
                let callback =
                    match operation with
                    | Reverse -> Ok (ANF.Return ANF.UnitLiteral, ANF.UnitLiteral, vg)
                    | Map fn -> lowerValue env vg fn
                callback |> Result.bind (fun (evaluation, fn, next) ->
                    let preparation, afterPreparation = prepareMutation buffer ownership next
                    let destination, afterDestination = ANF.freshVar afterPreparation
                    let target = ANF.Var destination
                    let mutations, afterMutation =
                        match operation, buffer.Layout with
                        | Map _, (MappedArray _ | RuntimeArray _) ->
                            let _, bindings, next = emit (ANF.Call ("Stdlib.List.__arrayMap", [target; word 0; buffer.Length; fn])) afterDestination
                            [bindings], next
                        | Reverse, (MappedArray _ | RuntimeArray _) ->
                            let last, subtraction, afterLast = emit (ANF.Prim (ANF.Sub, buffer.Length, word 1)) afterDestination
                            let _, bindings, next = emit (ANF.Call ("Stdlib.List.__arrayReverse", [target; word 0; last])) afterLast
                            [subtraction @ bindings], next
                        | Map _, RecycledArray length ->
                            [0 .. length - 1] |> List.mapFold (fun state index ->
                                let value, load, next = emit (ANF.RawGet (target, word (elementOffset index), Some AST.TInt64)) state
                                let mapped, call, afterCall = emit (ANF.ClosureCall (fn, [value])) next
                                let _, store, final = write target (elementOffset index) mapped afterCall
                                load @ call @ store, final) afterDestination
                        | Reverse, RecycledArray length ->
                            [0 .. length / 2 - 1] |> List.mapFold (fun state index ->
                                let other = length - 1 - index
                                let left, leftLoad, next = emit (ANF.RawGet (target, word (elementOffset index), Some AST.TInt64)) state
                                let right, rightLoad, afterRight = emit (ANF.RawGet (target, word (elementOffset other), Some AST.TInt64)) next
                                let _, leftWrite, afterLeft = write target (elementOffset index) right afterRight
                                let _, rightWrite, final = write target (elementOffset other) left afterLeft
                                leftLoad @ rightLoad @ leftWrite @ rightWrite, final) afterDestination
                    lowerRest env (Map.add output { buffer with Pointer = target } buffers) afterMutation
                    |> Result.map (fun (body, final) ->
                        bindReturns evaluation (fun _ ->
                            bindReturns preparation (fun selected ->
                                ANF.Let (destination, ANF.TypedAtom (selected, AST.TRawPtr), wrap (List.concat mutations) body))), final))
            | Fold (name, input, initial, fn) ->
                lowerValue env vg initial |> Result.bind (fun (initialExpr, accumulator, next) ->
                    lowerValue env next fn |> Result.bind (fun (callbackExpr, callback, afterCallback) ->
                        let buffer = lookup "fold buffer" input buffers
                        let bindings, (value, afterFold) =
                            match buffer.Layout with
                            | MappedArray _ | RuntimeArray _ ->
                                let result, calls, next = emit (ANF.Call ("Stdlib.List.__arrayFold", [buffer.Pointer; word 0; buffer.Length; accumulator; callback])) afterCallback
                                [calls], (result, next)
                            | RecycledArray length ->
                                [0 .. length - 1] |> List.mapFold (fun (acc, state) index ->
                                    let element, loads, next = emit (ANF.RawGet (buffer.Pointer, word (elementOffset index), Some AST.TInt64)) state
                                    let result, calls, final = emit (ANF.ClosureCall (callback, [acc; element])) next
                                    loads @ calls, (result, final)) (accumulator, afterCallback)
                        let id, afterId = ANF.freshVar afterFold
                        lowerRest (Map.add name (id, AST.TInt64) env) buffers afterId
                        |> Result.map (fun (body, final) ->
                            bindReturns initialExpr (fun _ -> bindReturns callbackExpr (fun _ ->
                                wrap (List.concat bindings) (ANF.Let (id, ANF.TypedAtom (value, AST.TInt64), body)))), final)))

    verify region |> Result.bind (fun () -> lowerBlock env Map.empty vg block)
