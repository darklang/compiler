// Coloring.fs - Color interference graphs and map colors to physical registers.

module RegisterColoring

open AllocationModel
open RegisterCoalescing

let private emptyColoringResult (domain: VRegDomain) : ColoringResult =
    { Domain = domain
      Colors = Array.create domain.Ids.Length None
      Spills = Bitset.empty domain.WordCount
      ChromaticNumber = 0 }

/// Build the inputs needed by greedy coloring when there are no move or phi
/// pairs to coalesce. Most small generated functions take this path, avoiding
/// construction and cloning of several full-domain bitset arrays.
let private uncoalescedColoringInputs
    (graph: InterferenceGraph)
    (precoloredPairs: (int * int) list)
    : int option array * BitSet array =
    let domain = graph.Domain
    let precolored = Array.create domain.Ids.Length None
    for (vregId, color) in precoloredPairs do
        match tryIndexOf domain vregId with
        | Some idx when Bitset.containsIndex idx graph.Vertices ->
            precolored.[idx] <- Some color
        | _ -> ()
    let emptyPreferences = Bitset.empty domain.WordCount
    let preferences = Array.create domain.Ids.Length emptyPreferences
    (precolored, preferences)

/// Greedy color in reverse PEO order with phi coalescing preferences
/// For chordal graphs, this produces an optimal coloring.
/// When preferences are provided, try to use colors that match coalesced partners.
/// Uses two-pass approach: first color vregs with no uncolored phi partners,
/// then color deferred vregs (whose partners are now colored).
let greedyColorReverse
    (graph: InterferenceGraph)
    (peo: int list)
    (precolored: int option array)
    (numColors: int)
    (preferences: BitSet array)
    : ColoringResult =
    let domain = graph.Domain
    let n = domain.Ids.Length
    let wordCount = domain.WordCount
    let colors = Array.create n None
    let spills = Bitset.empty wordCount
    let mutable maxColor = -1

    // Apply pre-colored vertices
    for idx in 0 .. n - 1 do
        match precolored.[idx] with
        | Some c ->
            colors.[idx] <- Some c
            if c > maxColor then maxColor <- c
        | None -> ()

    let inGraph = Array.create n false
    Bitset.iterIndices graph.Vertices (fun idx -> inGraph.[idx] <- true)

    let peoIndices =
        peo
        |> List.map (fun v ->
            match tryIndexOf domain v with
            | Some idx -> idx
            | None -> Crash.crash $"Greedy coloring missing vertex {v}")

    let markUsedColors (idx: int) (used: bool array) : unit =
        Bitset.iterIndices graph.Neighbors.[idx] (fun nidx ->
            if inGraph.[nidx] then
                match colors.[nidx] with
                | Some c when c >= 0 && c < numColors -> used.[c] <- true
                | _ -> ())

    let colorVertex (idx: int) : unit =
        if colors.[idx].IsNone then
            let used = Array.create numColors false
            markUsedColors idx used

            let mutable prefColor = None
            Bitset.iterIndices preferences.[idx] (fun pidx ->
                match colors.[pidx] with
                | Some c when prefColor.IsNone && c >= 0 && c < numColors && not used.[c] ->
                    prefColor <- Some c
                | _ -> ())

            let assignColor (c: int) =
                colors.[idx] <- Some c
                if c > maxColor then maxColor <- c

            match prefColor with
            | Some c -> assignColor c
            | None ->
                let mutable assigned = false
                for c in 0 .. numColors - 1 do
                    if not assigned && not used.[c] then
                        assignColor c
                        assigned <- true
                if not assigned then
                    Bitset.addIndexInPlace idx spills

    let hasUncoloredPartners (idx: int) : bool =
        let mutable found = false
        Bitset.iterIndices preferences.[idx] (fun pidx ->
            if not found && inGraph.[pidx] && colors.[pidx].IsNone then
                found <- true)
        found

    let interferes (idx1: int) (idx2: int) : bool =
        Bitset.containsIndex idx2 graph.Neighbors.[idx1]

    let colorVertexWithPartners (idx: int) : unit =
        if colors.[idx].IsNone then
            let candidates =
                let mutable acc = []
                Bitset.iterIndices preferences.[idx] (fun pidx ->
                    if inGraph.[pidx] && colors.[pidx].IsNone && not (interferes idx pidx) then
                        acc <- pidx :: acc)
                List.rev acc

            let rec filterMutuallyCompatible (acc: int list) (remaining: int list) =
                match remaining with
                | [] -> List.rev acc
                | p :: rest ->
                    let compatible = acc |> List.forall (fun a -> not (interferes p a))
                    if compatible then
                        filterMutuallyCompatible (p :: acc) rest
                    else
                        filterMutuallyCompatible acc rest

            let coalesceable = filterMutuallyCompatible [] candidates
            let allVertices = idx :: coalesceable
            let used = Array.create numColors false
            for vertex in allVertices do
                markUsedColors vertex used

            let mutable assigned = false
            for c in 0 .. numColors - 1 do
                if not assigned && not used.[c] then
                    for vertex in allVertices do
                        if colors.[vertex].IsNone then
                            colors.[vertex] <- Some c
                    if c > maxColor then maxColor <- c
                    assigned <- true

            if not assigned then
                Bitset.addIndexInPlace idx spills

    let deferred = Bitset.empty wordCount
    for idx in List.rev peoIndices do
        if colors.[idx].IsNone then
            if hasUncoloredPartners idx then
                Bitset.addIndexInPlace idx deferred
            else
                colorVertex idx

    for idx in List.rev peoIndices do
        if Bitset.containsIndex idx deferred && colors.[idx].IsNone then
            colorVertexWithPartners idx

    { Domain = domain
      Colors = colors
      Spills = spills
      ChromaticNumber = if maxColor < 0 then 0 else maxColor + 1 }

/// Main chordal graph coloring function with phi coalescing preferences
let chordalGraphColor
    (graph: InterferenceGraph)
    (precoloredPairs: (int * int) list)
    (numColors: int)
    (preferencePairs: (int * int) list)
    (movePairs: (int * int) list)
    : ColoringResult =
    if Bitset.isEmpty graph.Vertices then
        emptyColoringResult graph.Domain
    elif List.isEmpty movePairs && List.isEmpty preferencePairs then
        let (precolored, preferences) =
            uncoalescedColoringInputs graph precoloredPairs
        let peo = maximumCardinalitySearch graph
        greedyColorReverse graph peo precolored numColors preferences
    else
        let coalesced = coalesceGraphFast graph precoloredPairs movePairs preferencePairs
        let peo = maximumCardinalitySearch coalesced.Graph
        let result = greedyColorReverse coalesced.Graph peo coalesced.Precolored numColors coalesced.Preferences
        expandColoring result coalesced.RepMembers

let internal chordalGraphColorWithTiming
    (sw: System.Diagnostics.Stopwatch)
    (graph: InterferenceGraph)
    (precoloredPairs: (int * int) list)
    (numColors: int)
    (preferencePairs: (int * int) list)
    (movePairs: (int * int) list)
    : ColoringResult * ChordalColoringTiming =
    if Bitset.isEmpty graph.Vertices then
        (emptyColoringResult graph.Domain,
         { CoalesceMs = 0.0
           McsMs = 0.0
           GreedyMs = 0.0
           ExpandMs = 0.0 })
    elif List.isEmpty movePairs && List.isEmpty preferencePairs then
        let start = sw.Elapsed.TotalMilliseconds
        let (precolored, preferences) =
            uncoalescedColoringInputs graph precoloredPairs
        let prepMs = sw.Elapsed.TotalMilliseconds - start
        let mcsStart = sw.Elapsed.TotalMilliseconds
        let peo = maximumCardinalitySearch graph
        let mcsMs = sw.Elapsed.TotalMilliseconds - mcsStart
        let greedyStart = sw.Elapsed.TotalMilliseconds
        let result = greedyColorReverse graph peo precolored numColors preferences
        let greedyMs = sw.Elapsed.TotalMilliseconds - greedyStart
        (result,
         { CoalesceMs = prepMs
           McsMs = mcsMs
           GreedyMs = greedyMs
           ExpandMs = 0.0 })
    else
        let timePhase (f: unit -> 'a) : 'a * float =
            let start = sw.Elapsed.TotalMilliseconds
            let result = f ()
            let elapsedMs = sw.Elapsed.TotalMilliseconds - start
            (result, elapsedMs)

        let (coalesced, coalesceMs) =
            timePhase (fun () -> coalesceGraphFast graph precoloredPairs movePairs preferencePairs)
        let (peo, mcsMs) =
            timePhase (fun () -> maximumCardinalitySearch coalesced.Graph)
        let (result, greedyMs) =
            timePhase (fun () ->
                greedyColorReverse coalesced.Graph peo coalesced.Precolored numColors coalesced.Preferences)
        let (expanded, expandMs) =
            timePhase (fun () -> expandColoring result coalesced.RepMembers)

        let timing = {
            CoalesceMs = coalesceMs
            McsMs = mcsMs
            GreedyMs = greedyMs
            ExpandMs = expandMs
        }
        (expanded, timing)

/// Convert chordal graph coloring result to allocation result
/// Colors map to physical registers, spills map to stack slots
let coloringToAllocation (colorResult: ColoringResult) (registers: LIR.PhysReg list) : AllocationResult =
    let domain = colorResult.Domain
    let n = domain.Ids.Length
    let allocations = Array.create n None
    let mutable nextStackSlot = -8
    let mutable usedCalleeSaved : LIR.PhysReg list = []

    // Map colored vertices to physical registers
    for idx in 0 .. n - 1 do
        match colorResult.Colors.[idx] with
        | Some color ->
            if color < List.length registers then
                let reg = List.item color registers
                allocations.[idx] <- Some (PhysReg reg)
                // Track callee-saved register usage
                if List.contains reg [LIR.X19; LIR.X20; LIR.X21; LIR.X22; LIR.X23; LIR.X24; LIR.X25; LIR.X26] then
                    if not (List.contains reg usedCalleeSaved) then
                        usedCalleeSaved <- reg :: usedCalleeSaved
            else
                // Color out of range - treat as spill
                allocations.[idx] <- Some (StackSlot nextStackSlot)
                nextStackSlot <- nextStackSlot - 8
        | None -> ()

    // Map spilled vertices to stack slots
    Bitset.iterIndices colorResult.Spills (fun idx ->
        if allocations.[idx].IsNone then
            allocations.[idx] <- Some (StackSlot nextStackSlot)
            nextStackSlot <- nextStackSlot - 8)

    // Compute 16-byte aligned stack size
    let stackSize =
        if nextStackSlot = -8 then 0
        else ((abs nextStackSlot + 15) / 16) * 16

    { Domain = domain
      Allocations = allocations
      StackSize = stackSize
      UsedCalleeSaved = usedCalleeSaved |> List.sort }
