// Liveness.fs - Solve CFG liveness and prepare caller-save requirements.

module RegisterLiveness

open AllocationModel
open RegisterFacts

/// Get successor labels for a terminator
let getSuccessors (term: LIR.Terminator) : LIR.Label list =
    match term with
    | LIR.Ret -> []
    | LIR.Branch (_, trueLabel, falseLabel) -> [trueLabel; falseLabel]
    | LIR.BranchZero (_, zeroLabel, nonZeroLabel) -> [zeroLabel; nonZeroLabel]
    | LIR.BranchBitZero (_, _, zeroLabel, nonZeroLabel) -> [zeroLabel; nonZeroLabel]
    | LIR.BranchBitNonZero (_, _, nonZeroLabel, zeroLabel) -> [nonZeroLabel; zeroLabel]
    | LIR.CondBranch (_, trueLabel, falseLabel) -> [trueLabel; falseLabel]
    | LIR.Jump label -> [label]

let private addPhiUse
    (domain: VRegDomain)
    (predIdx: int)
    (vregId: int)
    (uses: (int * BitSet) list)
    : (int * BitSet) list =
    let rec insert remaining =
        match remaining with
        | [] ->
            let bits = Bitset.empty domain.WordCount
            vregBitsAddInPlace domain vregId bits
            [ (predIdx, bits) ]
        | (idx, bits) :: rest ->
            if idx = predIdx then
                vregBitsAddInPlace domain vregId bits
                remaining
            else
                (idx, bits) :: insert rest
    insert uses

let private collectPhiUsesByPred
    (domain: VRegDomain)
    (blockIndex: BlockIndex)
    (classifiedBlocks: ClassifiedBlock array)
    : (int * BitSet) list array =
    let result = Array.init classifiedBlocks.Length (fun _ -> [])
    for blockIdx in 0 .. classifiedBlocks.Length - 1 do
        let blockFacts = classifiedBlocks.[blockIdx]
        let mutable uses = []
        for facts in blockFacts.InstrFacts do
            for (id, predLabel) in facts.IntPhiUses do
                match tryBlockIndex blockIndex predLabel with
                | Some predIdx -> uses <- addPhiUse domain predIdx id uses
                | None -> ()
        result.[blockIdx] <- uses
    result

let private collectFPhiUsesByPred
    (domain: VRegDomain)
    (blockIndex: BlockIndex)
    (classifiedBlocks: ClassifiedBlock array)
    : (int * BitSet) list array =
    let result = Array.init classifiedBlocks.Length (fun _ -> [])
    for blockIdx in 0 .. classifiedBlocks.Length - 1 do
        let blockFacts = classifiedBlocks.[blockIdx]
        let mutable uses = []
        for facts in blockFacts.InstrFacts do
            for (id, predLabel) in facts.FloatPhiUses do
                match tryBlockIndex blockIndex predLabel with
                | Some predIdx -> uses <- addPhiUse domain predIdx id uses
                | None -> ()
        result.[blockIdx] <- uses
    result

/// Compute GEN and KILL sets for a basic block
/// GEN = variables used before being defined
/// KILL = variables defined
let private computeGenKillFromFacts
    (domain: VRegDomain)
    (blockFacts: ClassifiedBlock)
    : BitSet * BitSet =
    // Process instructions in forward order
    let gen = Bitset.empty domain.WordCount
    let kill = Bitset.empty domain.WordCount

    for facts in blockFacts.InstrFacts do
        // Add to GEN if used and not already killed (defined earlier in block)
        for u in facts.IntUses do
            if not (vregBitsContains domain kill u) then
                vregBitsAddInPlace domain u gen

        // Add to KILL if defined
        match facts.IntDef with
        | Some d -> vregBitsAddInPlace domain d kill
        | None -> ()

    // Also add terminator uses to GEN
    for u in blockFacts.TerminatorUses do
        if not (vregBitsContains domain kill u) then
            vregBitsAddInPlace domain u gen

    (gen, kill)

let computeGenKill (domain: VRegDomain) (block: LIR.BasicBlock) : BitSet * BitSet =
    classifyBlocks [| block |]
    |> Array.item 0
    |> computeGenKillFromFacts domain

/// Compute liveness using backward dataflow analysis
/// Handles SSA phi nodes: phi sources are live at predecessor exits, not at phi's block entry
/// Collect all integer VReg IDs referenced in the CFG (uses, defs, phi sources/dests, terminators)
let private collectVRegIdsFromFacts (classifiedBlocks: ClassifiedBlock array) : int list =
    classifiedBlocks
    |> Array.fold (fun acc blockFacts ->
        let acc =
            blockFacts.TerminatorUses
            |> List.fold (fun acc id -> id :: acc) acc
        blockFacts.InstrFacts
        |> Array.fold (fun acc facts ->
            let acc = facts.IntUses |> List.fold (fun acc id -> id :: acc) acc
            let acc = facts.IntPhiUses |> List.fold (fun acc (id, _) -> id :: acc) acc
            match facts.IntDef with
            | Some id -> id :: acc
            | None -> acc) acc
    ) []

/// Compute float GEN and KILL sets for a basic block
/// GEN = float variables used before being defined
/// KILL = float variables defined
let private computeFloatGenKillFromFacts
    (domain: VRegDomain)
    (blockFacts: ClassifiedBlock)
    : BitSet * BitSet =
    let gen = Bitset.empty domain.WordCount
    let kill = Bitset.empty domain.WordCount

    for facts in blockFacts.InstrFacts do
        for u in facts.FloatUses do
            if not (vregBitsContains domain kill u) then
                vregBitsAddInPlace domain u gen

        match facts.FloatDef with
        | Some d -> vregBitsAddInPlace domain d kill
        | None -> ()

    (gen, kill)

let computeFloatGenKill (domain: VRegDomain) (block: LIR.BasicBlock) : BitSet * BitSet =
    classifyBlocks [| block |]
    |> Array.item 0
    |> computeFloatGenKillFromFacts domain

/// Compute float liveness using backward dataflow analysis
/// Handles SSA FPhi nodes: phi sources are live at predecessor exits, not at phi's block entry
/// Collect all float VReg IDs referenced in the CFG (uses, defs, FPhi sources/dests)
let private collectFVRegIdsFromFacts (classifiedBlocks: ClassifiedBlock array) : int list =
    classifiedBlocks
    |> Array.fold (fun acc blockFacts ->
        blockFacts.InstrFacts
        |> Array.fold (fun acc facts ->
            let acc = facts.FloatUses |> List.fold (fun acc id -> id :: acc) acc
            let acc = facts.FloatPhiUses |> List.fold (fun acc (id, _) -> id :: acc) acc
            match facts.FloatDef with
            | Some id -> id :: acc
            | None -> acc) acc
    ) []

let private collectFVRegIds (blocks: LIR.BasicBlock array) : int list =
    blocks |> classifyBlocks |> collectFVRegIdsFromFacts

/// Compute integer and float liveness in one backward CFG fixed point.
/// The two domains remain distinct, so allocation receives the exact same live
/// sets as independent solvers, while CFG successors and edge lookups are shared.
let internal computeCombinedLivenessBitsFromFacts
    (blockIndex: BlockIndex)
    (classifiedBlocks: ClassifiedBlock array)
    (intExtraIds: int list)
    (floatExtraIds: int list)
    : VRegDomain * BlockLiveness array * VRegDomain * BlockLiveness array =
    let intDomain = buildVRegDomain (collectVRegIdsFromFacts classifiedBlocks @ intExtraIds)
    let floatDomain = buildVRegDomain (collectFVRegIdsFromFacts classifiedBlocks @ floatExtraIds)
    let emptyIntBits = Bitset.empty intDomain.WordCount
    let emptyFloatBits = Bitset.empty floatDomain.WordCount
    let intGenKillBits =
        Array.init classifiedBlocks.Length (fun idx ->
            computeGenKillFromFacts intDomain classifiedBlocks.[idx])
    let floatGenKillBits =
        Array.init classifiedBlocks.Length (fun idx ->
            computeFloatGenKillFromFacts floatDomain classifiedBlocks.[idx])
    let intPhiUsesBits = collectPhiUsesByPred intDomain blockIndex classifiedBlocks
    let floatPhiUsesBits = collectFPhiUsesByPred floatDomain blockIndex classifiedBlocks
    let intLiveness = Array.init classifiedBlocks.Length (fun _ -> { LiveIn = emptyIntBits; LiveOut = emptyIntBits })
    let floatLiveness = Array.init classifiedBlocks.Length (fun _ -> { LiveIn = emptyFloatBits; LiveOut = emptyFloatBits })
    let successorIndices =
        Array.init classifiedBlocks.Length (fun blockIdx ->
            classifiedBlocks.[blockIdx].Block.Terminator
            |> getSuccessors
            |> List.choose (tryBlockIndex blockIndex)
            |> List.toArray)
    let predecessorIndices =
        Array.init classifiedBlocks.Length (fun _ -> ResizeArray<int>())
    for predIdx in 0 .. successorIndices.Length - 1 do
        for succIdx in successorIndices.[predIdx] do
            predecessorIndices.[succIdx].Add predIdx
    // Liveness flows from successors to predecessors. Visiting a CFG in
    // postorder therefore settles acyclic regions in one sweep; the fixed
    // point below only has to revisit loop backedges. Successor and predecessor
    // indices are retained so the solver only revisits blocks affected by a
    // changed successor instead of rescanning the entire CFG.
    let backwardDataflowOrder =
        let roots =
            blockIndex.EntryIndex
            :: [0 .. classifiedBlocks.Length - 1]
        let rec visit
            (work: (int * bool) list)
            (visited: Set<int>)
            (postorderRev: int list)
            : int list =
            match work with
            | [] -> List.rev postorderRev
            | (blockIdx, expanded) :: remaining ->
                if expanded then
                    visit remaining visited (blockIdx :: postorderRev)
                elif Set.contains blockIdx visited then
                    visit remaining visited postorderRev
                else
                    let successors =
                        successorIndices.[blockIdx]
                        |> Array.toList
                        |> List.map (fun successorIdx -> (successorIdx, false))
                    visit
                        (successors @ ((blockIdx, true) :: remaining))
                        (Set.add blockIdx visited)
                        postorderRev
        visit (roots |> List.map (fun blockIdx -> (blockIdx, false))) Set.empty []
    let phiUsesForEdge (phiUses: (int * BitSet) list array) (emptyBits: BitSet) (succIdx: int) (predIdx: int) : BitSet =
        match phiUses.[succIdx] |> List.tryFind (fun (idx, _) -> idx = predIdx) with
        | Some (_, bits) -> bits
        | None -> emptyBits
    let work = System.Collections.Generic.Queue<int>()
    let queued = Array.create classifiedBlocks.Length false
    for blockIdx in backwardDataflowOrder do
        work.Enqueue blockIdx
        queued.[blockIdx] <- true
    while work.Count > 0 do
        let blockIdx = work.Dequeue()
        queued.[blockIdx] <- false
        let mutable intLiveOutAccumulator = NoUnionBits
        let mutable floatLiveOutAccumulator = NoUnionBits
        for succIdx in successorIndices.[blockIdx] do
            intLiveOutAccumulator <- bitsetAccumulateUnion intLiveOutAccumulator intLiveness.[succIdx].LiveIn
            intLiveOutAccumulator <- bitsetAccumulateUnion intLiveOutAccumulator (phiUsesForEdge intPhiUsesBits emptyIntBits succIdx blockIdx)
            floatLiveOutAccumulator <- bitsetAccumulateUnion floatLiveOutAccumulator floatLiveness.[succIdx].LiveIn
            floatLiveOutAccumulator <- bitsetAccumulateUnion floatLiveOutAccumulator (phiUsesForEdge floatPhiUsesBits emptyFloatBits succIdx blockIdx)
        let (intGen, intKill) = intGenKillBits.[blockIdx]
        let oldIntLiveness = intLiveness.[blockIdx]
        let newIntLiveOut = bitsetFinishUnion emptyIntBits intLiveOutAccumulator
        let newIntLiveIn = Bitset.clone newIntLiveOut
        Bitset.diffInPlace newIntLiveIn intKill
        Bitset.unionInPlace newIntLiveIn intGen
        let intLiveInChanged = not (Bitset.equal newIntLiveIn oldIntLiveness.LiveIn)
        if intLiveInChanged || not (Bitset.equal newIntLiveOut oldIntLiveness.LiveOut) then
            intLiveness.[blockIdx] <- { LiveIn = newIntLiveIn; LiveOut = newIntLiveOut }
        let (floatGen, floatKill) = floatGenKillBits.[blockIdx]
        let oldFloatLiveness = floatLiveness.[blockIdx]
        let newFloatLiveOut = bitsetFinishUnion emptyFloatBits floatLiveOutAccumulator
        let newFloatLiveIn = Bitset.clone newFloatLiveOut
        Bitset.diffInPlace newFloatLiveIn floatKill
        Bitset.unionInPlace newFloatLiveIn floatGen
        let floatLiveInChanged = not (Bitset.equal newFloatLiveIn oldFloatLiveness.LiveIn)
        if floatLiveInChanged || not (Bitset.equal newFloatLiveOut oldFloatLiveness.LiveOut) then
            floatLiveness.[blockIdx] <- { LiveIn = newFloatLiveIn; LiveOut = newFloatLiveOut }
        if intLiveInChanged || floatLiveInChanged then
            for predIdx in predecessorIndices.[blockIdx] do
                if not queued.[predIdx] then
                    work.Enqueue predIdx
                    queued.[predIdx] <- true

    (intDomain, intLiveness, floatDomain, floatLiveness)

let internal computeLivenessBitsFromFacts blockIndex classifiedBlocks extraIds =
    let (domain, liveness, _, _) = computeCombinedLivenessBitsFromFacts blockIndex classifiedBlocks extraIds []
    (domain, liveness)

let internal computeFloatLivenessBitsFromFacts blockIndex classifiedBlocks extraIds =
    let (_, _, domain, liveness) = computeCombinedLivenessBitsFromFacts blockIndex classifiedBlocks [] extraIds
    (domain, liveness)

let private computeLivenessBitsRaw blockIndex blocks extraIds =
    computeLivenessBitsFromFacts blockIndex (classifyBlocks blocks) extraIds

/// Compute liveness using bitsets for the dataflow fixed point.
let computeLivenessBits (cfg: LIR.CFG) : VRegDomain * BlockIndex * BlockLiveness array =
    let (blockIndex, blocks) = buildBlockIndex cfg
    let (domain, liveness) = computeLivenessBitsRaw blockIndex blocks []
    (domain, blockIndex, liveness)

let private computeFloatLivenessBitsRaw
    (blockIndex: BlockIndex)
    (blocks: LIR.BasicBlock array)
    (extraIds: int list)
    : VRegDomain * BlockLiveness array =
    computeFloatLivenessBitsFromFacts blockIndex (classifyBlocks blocks) extraIds

/// Compute float liveness using bitsets for the dataflow fixed point
let computeFloatLivenessBits (cfg: LIR.CFG) : VRegDomain * BlockIndex * BlockLiveness array =
    let (blockIndex, blocks) = buildBlockIndex cfg
    let (domain, liveness) = computeFloatLivenessBitsRaw blockIndex blocks []
    (domain, blockIndex, liveness)

/// Compute the data needed to populate empty SaveRegs/RestoreRegs placeholders.
/// A single backward walk captures continuation liveness and, on ARM64, the
/// caller-saved sources needed to preserve parallel argument moves.
let internal computeSaveRegsPreparation
    (trackArgMoveBacking: bool)
    (intDomain: VRegDomain)
    (floatDomain: VRegDomain)
    (mapping: AllocationResult)
    (block: LIR.BasicBlock)
    (instrFacts: InstrRegisterFacts array)
    (intLiveOut: BitSet)
    (floatLiveOut: BitSet)
    : (BitSet * BitSet) list * LIR.PhysReg list list =
    let intLive = Bitset.clone intLiveOut
    let floatLive = Bitset.clone floatLiveOut

    getTerminatorUsedVRegs block.Terminator
    |> List.iter (fun id -> vregBitsAddInPlace intDomain id intLive)

    let sourcePhysReg (operand: LIR.Operand) : LIR.PhysReg option =
        match operand with
        | LIR.Reg (LIR.Physical reg) -> Some reg
        | LIR.Reg (LIR.Virtual id) ->
            match tryIndexOf mapping.Domain id with
            | Some idx ->
                match mapping.Allocations.[idx] with
                | Some (PhysReg reg) -> Some reg
                | Some (StackSlot _)
                | None -> None
            | None -> None
        | _ -> None

    let callerSavedArgIndex (reg: LIR.PhysReg) : int option =
        match reg with
        | LIR.X1 -> Some 0
        | LIR.X2 -> Some 1
        | LIR.X3 -> Some 2
        | LIR.X4 -> Some 3
        | LIR.X5 -> Some 4
        | LIR.X6 -> Some 5
        | LIR.X7 -> Some 6
        | _ -> None

    let mergeBackingForMoves
        (backing: bool array)
        (moves: (LIR.PhysReg * LIR.Operand) list)
        : unit =
        let destinations = Array.create 7 false
        for (dest, _) in moves do
            match callerSavedArgIndex dest with
            | Some idx -> destinations.[idx] <- true
            | None -> ()
        for (dest, source) in moves do
            match sourcePhysReg source with
            | Some sourceReg when sourceReg <> dest ->
                match callerSavedArgIndex sourceReg with
                | Some idx when destinations.[idx] -> backing.[idx] <- true
                | _ -> ()
            | _ -> ()

    let finishBacking (backing: bool array) : LIR.PhysReg list =
        [ LIR.X1; LIR.X2; LIR.X3; LIR.X4; LIR.X5; LIR.X6; LIR.X7 ]
        |> List.mapi (fun idx reg -> (idx, reg))
        |> List.choose (fun (idx, reg) -> if backing.[idx] then Some reg else None)

    let rec walkBackwards
        (instrIdx: int)
        (pendingRestores: ((BitSet * BitSet) * bool array) list)
        (snapshots: (BitSet * BitSet) list)
        (backingRegs: LIR.PhysReg list list)
        : (BitSet * BitSet) list * LIR.PhysReg list list =
        if instrIdx < 0 then
            if List.isEmpty pendingRestores then
                (snapshots, backingRegs)
            else
                Crash.crash "Unmatched RestoreRegs while computing caller-save liveness"
        else
            let facts = instrFacts.[instrIdx]
            let instr = facts.Instr
            let (pendingRestores, snapshots, backingRegs) =
                match instr with
                | LIR.RestoreRegs ([], []) ->
                    if trackArgMoveBacking && not (List.isEmpty pendingRestores) then
                        Crash.crash "Nested SaveRegs while computing argument-move backing"
                    let snapshot = (Bitset.clone intLive, Bitset.clone floatLive)
                    ((snapshot, Array.create 7 false) :: pendingRestores, snapshots, backingRegs)
                | LIR.ArgMoves moves when trackArgMoveBacking ->
                    match pendingRestores with
                    | (snapshot, backing) :: rest ->
                        mergeBackingForMoves backing moves
                        ((snapshot, backing) :: rest, snapshots, backingRegs)
                    | [] -> (pendingRestores, snapshots, backingRegs)
                | LIR.SaveRegs ([], []) ->
                    match pendingRestores with
                    | (snapshot, backing) :: pendingRestores ->
                        (pendingRestores, snapshot :: snapshots, finishBacking backing :: backingRegs)
                    | [] ->
                        Crash.crash "Unmatched SaveRegs while computing caller-save liveness"
                | _ -> (pendingRestores, snapshots, backingRegs)

            match facts.IntDef with
            | Some id -> vregBitsRemoveInPlace intDomain id intLive
            | None -> ()
            facts.IntUses
            |> List.iter (fun id -> vregBitsAddInPlace intDomain id intLive)

            match facts.FloatDef with
            | Some id -> vregBitsRemoveInPlace floatDomain id floatLive
            | None -> ()
            facts.FloatUses
            |> List.iter (fun id -> vregBitsAddInPlace floatDomain id floatLive)

            walkBackwards (instrIdx - 1) pendingRestores snapshots backingRegs

    walkBackwards (instrFacts.Length - 1) [] [] []

let internal isEmptySaveRegs (instr: LIR.Instr) : bool =
    match instr with
    | LIR.SaveRegs ([], []) -> true
    | _ -> false
