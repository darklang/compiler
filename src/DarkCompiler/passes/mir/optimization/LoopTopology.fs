// LoopTopology.fs - Compute dominators and natural-loop interfaces.

module MIRLoopTopology

open MIR
open SSA_Construction

/// Get successors from a basic block terminator
let getSuccessors (block: BasicBlock) : Label list =
    match block.Terminator with
    | Ret _ -> []
    | Jump label -> [label]
    | Branch (_, trueLabel, falseLabel) -> [trueLabel; falseLabel]

/// Build successor map for the CFG
let buildSuccessors (cfg: CFG) : Map<Label, Label list> =
    cfg.Blocks |> Map.map (fun _ block -> getSuccessors block)

/// Check whether the reachable CFG contains any cycle.
let cfgHasReachableCycle (cfg: CFG) : bool =
    let succs = buildSuccessors cfg

    let rec visit (visiting: Set<Label>) (visited: Set<Label>) (label: Label) : bool * Set<Label> =
        if Set.contains label visiting then
            (true, visited)
        elif Set.contains label visited then
            (false, visited)
        else
            let visiting' = Set.add label visiting
            let successors = Map.tryFind label succs |> Option.defaultValue []
            let rec visitSuccessors remaining visitedAcc =
                match remaining with
                | [] -> (false, visitedAcc)
                | next :: rest ->
                    let (hasCycle, visited') = visit visiting' visitedAcc next
                    if hasCycle then (true, visited') else visitSuccessors rest visited'

            let (hasCycle, visited') = visitSuccessors successors visited
            (hasCycle, Set.add label visited')

    let (hasCycle, _) = visit Set.empty Set.empty cfg.Entry
    hasCycle

/// Check if dominator dominates node (using idom chain)
let dominates (entry: Label) (idoms: Dominators) (dominator: Label) (node: Label) : bool =
    if dominator = node then
        true
    elif dominator = entry then
        node = entry || Map.containsKey node idoms
    else
        let rec walk current =
            match Map.tryFind current idoms with
            | None -> false
            | Some parent ->
                if parent = dominator then true
                elif parent = entry then false
                else walk parent
        walk node

/// Identify natural loops via backedges (header dominates source), reusing a
/// predecessor map and dominators already computed for this CFG topology.
let private findNaturalLoopsWithTopology
    (cfg: CFG)
    (predecessors: Map<Label, Label list>)
    (idoms: Dominators)
    : Map<Label, Set<Label>> =
    let entry = cfg.Entry
    let successors = buildSuccessors cfg

    let backedges =
        successors
        |> Map.fold (fun acc from successorLabels ->
            successorLabels
            |> List.fold (fun acc' successor ->
                if dominates entry idoms successor from then
                    let existing =
                        Map.tryFind successor acc' |> Option.defaultValue []
                    Map.add successor (from :: existing) acc'
                else
                    acc') acc) Map.empty

    backedges
    |> Map.fold (fun loops header sources ->
        let loopBlocks =
            sources
            |> List.fold (fun acc source ->
                let initial = Set.ofList [header; source]
                let rec grow work loopSet =
                    match work with
                    | [] -> loopSet
                    | node :: rest ->
                        let nodePredecessors =
                            Map.tryFind node predecessors
                            |> Option.defaultValue []
                        let (loopSet', work') =
                            nodePredecessors
                            |> List.fold (fun (setAcc, workAcc) predecessor ->
                                if Set.contains predecessor setAcc then
                                    (setAcc, workAcc)
                                elif dominates entry idoms header predecessor then
                                    (Set.add predecessor setAcc, predecessor :: workAcc)
                                else
                                    (setAcc, workAcc)) (loopSet, rest)
                        grow work' loopSet'
                Set.union acc (grow [source] initial)) Set.empty

        if Set.isEmpty loopBlocks then loops
        else Map.add header loopBlocks loops) Map.empty

/// Immutable facts shared only while CFG blocks and edges are unchanged.
type internal LoopTopology = {
    Loops: Map<Label, Set<Label>>
    Predecessors: Map<Label, Label list>
}

type internal DominatorTopology = {
    Predecessors: Map<Label, Label list>
    ImmediateDominators: Dominators
}

let internal buildDominatorTopology (cfg: CFG) : DominatorTopology =
    let predecessors = buildPredecessors cfg
    {
        Predecessors = predecessors
        ImmediateDominators = computeDominators cfg predecessors
    }

let internal tryBuildLoopTopologyWithDominators
    (cfg: CFG)
    (dominatorTopology: DominatorTopology)
    : LoopTopology option =
    if not (cfgHasReachableCycle cfg) then
        None
    else
        Some {
            Loops =
                findNaturalLoopsWithTopology
                    cfg
                    dominatorTopology.Predecessors
                    dominatorTopology.ImmediateDominators
            Predecessors = dominatorTopology.Predecessors
        }

let internal tryBuildLoopTopology (cfg: CFG) : LoopTopology option =
    if cfgHasReachableCycle cfg then
        buildDominatorTopology cfg
        |> tryBuildLoopTopologyWithDominators cfg
    else
        None

/// Identify natural loops via backedges (header dominates source).
let findNaturalLoops (cfg: CFG) : Map<Label, Set<Label>> =
    match tryBuildLoopTopology cfg with
    | None -> Map.empty
    | Some topology -> topology.Loops
