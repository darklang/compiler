// Model.fs - Represent register domains, liveness sets, and interference graphs.

module AllocationModel

type LiveInterval = {
    VRegId: int
    Start: int
    End: int
}

/// Bitset of VRegDomain indices
type BitSet = Bitset.Bitset

/// Dense domain for VReg IDs used by bitsets
type VRegDomain = {
    Ids: int array
    IndexOf: int array
    IndexOffset: int
    WordCount: int
}

/// Dense domain for basic block labels
type BlockIndex = {
    Labels: LIR.Label array
    EntryIndex: int
}

/// Result of register allocation
type AllocationResult = {
    Domain: VRegDomain
    Allocations: Allocation option array
    StackSize: int
    UsedCalleeSaved: LIR.PhysReg list
}

/// Allocation target for a virtual register
and Allocation =
    | PhysReg of LIR.PhysReg
    | StackSlot of int

/// Liveness information for a basic block
type BlockLiveness = {
    LiveIn: BitSet
    LiveOut: BitSet
}

/// Timing information for register allocation phases
type RegisterAllocationTiming = {
    Phase: string
    ElapsedMs: float
}

type internal ChordalColoringTiming = {
    CoalesceMs: float
    McsMs: float
    GreedyMs: float
    ExpandMs: float
}

/// Register facts shared by domain construction, liveness, and interference.
/// Phi uses remain separate because they are live on predecessor edges rather
/// than at the instruction's position in its block.
type internal InstrRegisterFacts = {
    Instr: LIR.Instr
    IntUses: int list
    IntDef: int option
    IntPhiUses: (int * LIR.Label) list
    FloatUses: int list
    FloatDef: int option
    FloatPhiUses: (int * LIR.Label) list
}

type internal ClassifiedBlock = {
    Block: LIR.BasicBlock
    InstrFacts: InstrRegisterFacts array
    TerminatorUses: int list
    HasPhiNodes: bool
}

// ============================================================================
// Bitset Utilities (used to speed up register allocation)
// ============================================================================

let internal buildVRegDomain (ids: int list) : VRegDomain =
    let sorted = List.sort ids
    let rec unique (last: int option) (remaining: int list) (acc: int list) : int list =
        match remaining with
        | [] -> List.rev acc
        | head :: tail ->
            match last with
            | Some value when value = head -> unique last tail acc
            | _ -> unique (Some head) tail (head :: acc)
    let ordered = unique None sorted []
    let idsArray = ordered |> List.toArray
    let wordCount = Bitset.wordCount idsArray.Length
    match ordered with
    | [] ->
        { Ids = idsArray; IndexOf = [||]; IndexOffset = 0; WordCount = 0 }
    | minId :: _ ->
        let rec lastId (current: int) (remaining: int list) : int =
            match remaining with
            | [] -> current
            | head :: tail -> lastId head tail
        let maxId = lastId minId ordered
        let size = maxId - minId + 1
        let indexOf = Array.create size -1
        ordered |> List.iteri (fun idx id -> indexOf.[id - minId] <- idx)
        { Ids = idsArray; IndexOf = indexOf; IndexOffset = minId; WordCount = wordCount }

let internal tryIndexOf (domain: VRegDomain) (value: int) : int option =
    if domain.IndexOf.Length = 0 then
        None
    else
        let idx = value - domain.IndexOffset
        if idx < 0 || idx >= domain.IndexOf.Length then
            None
        else
            let mapped = domain.IndexOf.[idx]
            if mapped >= 0 then Some mapped else None

let vregBitsContains (domain: VRegDomain) (bits: BitSet) (value: int) : bool =
    match tryIndexOf domain value with
    | Some idx -> Bitset.containsIndex idx bits
    | None -> false

let internal vregBitsAddInPlace (domain: VRegDomain) (value: int) (bits: BitSet) : unit =
    match tryIndexOf domain value with
    | Some idx -> Bitset.addIndexInPlace idx bits
    | None -> Crash.crash $"RegisterAllocation: missing vreg {value} in bitset domain"

let internal vregBitsRemoveInPlace (domain: VRegDomain) (value: int) (bits: BitSet) : unit =
    match tryIndexOf domain value with
    | Some idx -> Bitset.removeIndexInPlace idx bits
    | None -> Crash.crash $"RegisterAllocation: missing vreg {value} in bitset domain"

type internal BitSetUnionAccumulator =
    | NoUnionBits
    | BorrowedUnionBits of BitSet
    | OwnedUnionBits of BitSet

/// Accumulate unions without allocating for zero or one non-empty input and without
/// replacing the owned result after the second non-empty input.
let internal bitsetAccumulateUnion
    (accumulator: BitSetUnionAccumulator)
    (bits: BitSet)
    : BitSetUnionAccumulator =
    if Bitset.isEmpty bits then
        accumulator
    else
        match accumulator with
        | NoUnionBits -> BorrowedUnionBits bits
        | BorrowedUnionBits existing -> OwnedUnionBits (Bitset.union existing bits)
        | OwnedUnionBits result ->
            Bitset.unionInPlace result bits
            accumulator

let internal bitsetFinishUnion
    (emptyBits: BitSet)
    (accumulator: BitSetUnionAccumulator)
    : BitSet =
    match accumulator with
    | NoUnionBits -> emptyBits
    | BorrowedUnionBits bits
    | OwnedUnionBits bits -> bits

let internal vregBitsFromList (domain: VRegDomain) (values: int list) : BitSet =
    if List.isEmpty values then
        Bitset.empty domain.WordCount
    else
        let bits = Bitset.empty domain.WordCount
        for value in values do
            match tryIndexOf domain value with
            | Some idx ->
                Bitset.addIndexInPlace idx bits
            | None ->
                Crash.crash $"BitSet: Missing vreg {value} in domain"
        bits

let private tryLabelIndex (labels: LIR.Label array) (label: LIR.Label) : int option =
    if labels.Length = 0 then
        None
    else
        let rec search low high =
            if low > high then
                None
            else
                let mid = (low + high) / 2
                let cmp = compare labels.[mid] label
                if cmp = 0 then
                    Some mid
                else if cmp < 0 then
                    search (mid + 1) high
                else
                    search low (mid - 1)
        search 0 (labels.Length - 1)

let internal buildBlockIndex (cfg: LIR.CFG) : BlockIndex * LIR.BasicBlock array =
    let entries = cfg.Blocks |> Seq.toArray
    let labels = entries |> Array.map (fun kvp -> kvp.Key)
    let blocks = entries |> Array.map (fun kvp -> kvp.Value)
    let entryIndex =
        match tryLabelIndex labels cfg.Entry with
        | Some idx -> idx
        | None -> Crash.crash $"BlockIndex: Missing entry label {cfg.Entry}"
    ({ Labels = labels; EntryIndex = entryIndex }, blocks)

let internal tryBlockIndex (index: BlockIndex) (label: LIR.Label) : int option =
    tryLabelIndex index.Labels label

let blockIndexOfLabel (index: BlockIndex) (label: LIR.Label) : int option =
    tryBlockIndex index label

let blockLivenessForLabel
    (index: BlockIndex)
    (liveness: BlockLiveness array)
    (label: LIR.Label)
    : BlockLiveness option =
    match tryBlockIndex index label with
    | Some idx -> Some liveness.[idx]
    | None -> None

let internal blocksToMap (index: BlockIndex) (blocks: LIR.BasicBlock array) : Map<LIR.Label, LIR.BasicBlock> =
    Array.zip index.Labels blocks |> Map.ofArray

// ============================================================================
// Chordal Graph Coloring Types
// ============================================================================

/// Interference graph for register allocation
/// In SSA form, this graph is guaranteed to be chordal
type InterferenceGraph = {
    Domain: VRegDomain
    Vertices: BitSet                // Domain indices present in the graph
    Neighbors: BitSet array         // Adjacency bitsets per domain index
}

/// Result of graph coloring
type ColoringResult = {
    Domain: VRegDomain
    Colors: int option array        // Domain index → color (0..k-1)
    Spills: BitSet                  // Domain indices that must be spilled
    ChromaticNumber: int            // Max color used + 1
}

/// Profiling data for Maximum Cardinality Search
type McsProfile = {
    VertexCount: int
    SelectionChecks: int
    WeightUpdates: int
    BucketSkips: int
}

/// Build an interference graph from an explicit vertex list and edge list.
let buildInterferenceGraphFromEdges (vertices: int list) (edges: (int * int) list) : InterferenceGraph =
    let domain = buildVRegDomain vertices
    let n = domain.Ids.Length
    let wordCount = domain.WordCount
    let neighbors = Array.init n (fun _ -> Bitset.empty wordCount)
    let present = Bitset.empty wordCount

    for v in vertices do
        match tryIndexOf domain v with
        | Some idx -> Bitset.addIndexInPlace idx present
        | None -> Crash.crash $"Interference graph missing vertex {v}"

    for (u, v) in edges do
        if u <> v then
            match tryIndexOf domain u, tryIndexOf domain v with
            | Some idxU, Some idxV ->
                Bitset.addIndexInPlace idxU present
                Bitset.addIndexInPlace idxV present
                Bitset.addIndexInPlace idxV neighbors.[idxU]
                Bitset.addIndexInPlace idxU neighbors.[idxV]
            | _ ->
                Crash.crash $"Interference graph missing edge endpoint {u} or {v}"

    { Domain = domain; Vertices = present; Neighbors = neighbors }

/// Check if a graph contains a vertex.
let graphHasVertex (graph: InterferenceGraph) (vregId: int) : bool =
    vregBitsContains graph.Domain graph.Vertices vregId

/// Get neighbors of a vertex in the interference graph.
let graphNeighbors (graph: InterferenceGraph) (vregId: int) : int list =
    match tryIndexOf graph.Domain vregId with
    | None -> []
    | Some idx ->
        if not (Bitset.containsIndex idx graph.Vertices) then
            []
        else
            graph.Neighbors.[idx]
            |> Bitset.indicesToList
            |> List.choose (fun nidx ->
                if Bitset.containsIndex nidx graph.Vertices then
                    Some graph.Domain.Ids.[nidx]
                else
                    None)

/// Get the assigned color of a vertex.
let colorOf (result: ColoringResult) (vregId: int) : int option =
    match tryIndexOf result.Domain vregId with
    | Some idx -> result.Colors.[idx]
    | None -> None

/// Check if a vertex was spilled.
let isSpill (result: ColoringResult) (vregId: int) : bool =
    match tryIndexOf result.Domain vregId with
    | Some idx -> Bitset.containsIndex idx result.Spills
    | None -> false

/// Count spilled vertices.
let spillCount (result: ColoringResult) : int =
    Bitset.count result.Spills

/// Count colored vertices.
let coloredCount (result: ColoringResult) : int =
    result.Colors
    |> Array.fold (fun acc color -> if color.IsSome then acc + 1 else acc) 0
