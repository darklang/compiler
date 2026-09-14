// Interference.fs - Build register-interference graphs from solved liveness.

module RegisterInterference

open AllocationModel
open RegisterFacts
open RegisterLiveness

// ============================================================================
// Chordal Graph Coloring Register Allocation
// ============================================================================

let private buildInterferenceGraphBitsetFastWithLivenessInternal
    (blockIndex: BlockIndex)
    (classifiedBlocks: ClassifiedBlock array)
    (domain: VRegDomain)
    (liveness: BlockLiveness array)
    (entryDefs: BitSet)
    : InterferenceGraph =
    let n = domain.Ids.Length
    let wordCount = domain.WordCount
    let adjacency = Array.init n (fun _ -> Bitset.empty wordCount)
    let present = Bitset.empty wordCount
    let markPresentIdx (idx: int) =
        if idx >= 0 && idx < n then
            Bitset.addIndexInPlace idx present

    let markPresentValue (value: int) =
        match tryIndexOf domain value with
        | Some idx -> markPresentIdx idx
        | None -> ()

    let addEdgesToLive (defIdx: int) (live: BitSet) =
        Bitset.unionInPlace adjacency.[defIdx] live
        Bitset.removeIndexInPlace defIdx adjacency.[defIdx]
        Bitset.iterIndices live (fun idx ->
            if idx <> defIdx then
                Bitset.addIndexInPlace defIdx adjacency.[idx])

    for blockIdx in 0 .. classifiedBlocks.Length - 1 do
        let blockFacts = classifiedBlocks.[blockIdx]
        let blockLiveness = liveness.[blockIdx]
        let mutable live = Bitset.clone blockLiveness.LiveOut

        for v in blockFacts.TerminatorUses do
            vregBitsAddInPlace domain v live

        Bitset.iterIndices live markPresentIdx

        for instrIdx in blockFacts.InstrFacts.Length - 1 .. -1 .. 0 do
            let facts = blockFacts.InstrFacts.[instrIdx]

            for u in facts.IntUses do
                markPresentValue u

            match facts.IntDef with
            | Some d ->
                markPresentValue d
                match tryIndexOf domain d with
                | Some defIdx -> addEdgesToLive defIdx live
                | None -> ()
                vregBitsRemoveInPlace domain d live
            | None -> ()

            for u in facts.IntUses do
                vregBitsAddInPlace domain u live

        if blockIdx = blockIndex.EntryIndex then
            Bitset.iterIndices entryDefs (fun defIdx ->
                if Bitset.containsIndex defIdx live then
                    markPresentIdx defIdx
                    addEdgesToLive defIdx live)

    { Domain = domain; Vertices = present; Neighbors = adjacency }

/// Build interference graph from CFG using bitset liveness
let internal buildInterferenceGraphBitsetWithLiveness
    (blockIndex: BlockIndex)
    (classifiedBlocks: ClassifiedBlock array)
    (domain: VRegDomain)
    (liveness: BlockLiveness array)
    (entryDefs: BitSet)
    : InterferenceGraph =
    buildInterferenceGraphBitsetFastWithLivenessInternal blockIndex classifiedBlocks domain liveness entryDefs

/// Build interference graph from CFG using bitset liveness
let buildInterferenceGraphBitsetFast
    (cfg: LIR.CFG)
    (entryDefs: int list)
    : InterferenceGraph =
    let (blockIndex, blocks) = buildBlockIndex cfg
    let classifiedBlocks = classifyBlocks blocks
    let (domain, liveness) = computeLivenessBitsFromFacts blockIndex classifiedBlocks entryDefs
    let entryBits = vregBitsFromList domain entryDefs
    buildInterferenceGraphBitsetWithLiveness blockIndex classifiedBlocks domain liveness entryBits

/// Build interference graph from CFG using bitset liveness
let buildInterferenceGraphBitset
    (cfg: LIR.CFG)
    (entryDefs: int list)
    : InterferenceGraph =
    buildInterferenceGraphBitsetFast cfg entryDefs

/// Build float interference graph from CFG using bitset liveness
let internal buildFloatInterferenceGraphBitsetWithLiveness
    (blockIndex: BlockIndex)
    (classifiedBlocks: ClassifiedBlock array)
    (domain: VRegDomain)
    (liveness: BlockLiveness array)
    (entryDefs: BitSet)
    : InterferenceGraph =
    let ids = domain.Ids
    let n = ids.Length
    let wordCount = domain.WordCount
    let adjacency = Array.init n (fun _ -> Bitset.empty wordCount)
    let present = Bitset.empty wordCount

    let markPresentIdx (idx: int) =
        if idx >= 0 && idx < n then
            Bitset.addIndexInPlace idx present

    let markPresentValue (value: int) =
        match tryIndexOf domain value with
        | Some idx -> markPresentIdx idx
        | None -> ()

    let addEdgesToLive (defIdx: int) (live: BitSet) =
        Bitset.unionInPlace adjacency.[defIdx] live
        Bitset.removeIndexInPlace defIdx adjacency.[defIdx]
        Bitset.iterIndices live (fun idx ->
            if idx <> defIdx then
                Bitset.addIndexInPlace defIdx adjacency.[idx])

    for blockIdx in 0 .. classifiedBlocks.Length - 1 do
        let blockFacts = classifiedBlocks.[blockIdx]
        let blockLiveness = liveness.[blockIdx]
        let mutable live = Bitset.clone blockLiveness.LiveOut
        Bitset.iterIndices live markPresentIdx

        for instrIdx in blockFacts.InstrFacts.Length - 1 .. -1 .. 0 do
            let facts = blockFacts.InstrFacts.[instrIdx]

            for u in facts.FloatUses do
                markPresentValue u

            match facts.FloatDef with
            | Some d ->
                markPresentValue d
                match tryIndexOf domain d with
                | Some defIdx -> addEdgesToLive defIdx live
                | None -> ()
                vregBitsRemoveInPlace domain d live
            | None -> ()

            for u in facts.FloatUses do
                vregBitsAddInPlace domain u live

        if blockIdx = blockIndex.EntryIndex then
            Bitset.iterIndices entryDefs (fun defIdx ->
                if Bitset.containsIndex defIdx live then
                    markPresentIdx defIdx
                    addEdgesToLive defIdx live)

    { Domain = domain; Vertices = present; Neighbors = adjacency }
