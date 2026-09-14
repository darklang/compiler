// DeadCode.fs - Eliminate MIR definitions unreachable from observable roots.

module MIRDeadCode

open MIR
open MIROptimizationFacts

let private buildDefUseMap
    (cfg: CFG)
    : System.Collections.Generic.Dictionary<VReg, VReg list> =
    let defUses = System.Collections.Generic.Dictionary<VReg, VReg list>()
    for KeyValue (_, block) in cfg.Blocks do
        for instr in block.Instrs do
            match getInstrDest instr with
            | Some dest ->
                defUses.[dest] <-
                    foldInstrUses (fun uses vreg -> vreg :: uses) [] instr
            | None -> ()
    defUses

/// Collect registers that are directly required by side effects and control flow.
let private collectRootUses
    (cfg: CFG)
    : System.Collections.Generic.HashSet<VReg> =
    let roots = System.Collections.Generic.HashSet<VReg>()
    let addRoot (uses: System.Collections.Generic.HashSet<VReg>) vreg =
        uses.Add vreg |> ignore
        uses
    for KeyValue (_, block) in cfg.Blocks do
        for instr in block.Instrs do
            if hasSideEffects instr then
                foldInstrUses addRoot roots instr |> ignore
        foldTerminatorUses addRoot roots block.Terminator |> ignore
    roots

/// Mark live SSA destinations by walking backwards from root uses.
let private collectLiveDestinations
    (recordTicks: (string -> int64 -> unit) option)
    (cfg: CFG)
    : System.Collections.Generic.HashSet<VReg> =
    let measure name operation =
        match recordTicks with
        | None -> operation ()
        | Some record ->
            let started = System.Diagnostics.Stopwatch.GetTimestamp()
            let result = operation ()
            record name (System.Diagnostics.Stopwatch.GetTimestamp() - started)
            result

    let defUseMap =
        measure "MIR DCE Def-Use Graph" (fun () -> buildDefUseMap cfg)
    let roots =
        measure "MIR DCE Root Collection" (fun () -> collectRootUses cfg)

    measure "MIR DCE Reachability" (fun () ->
        let work = System.Collections.Generic.Stack<VReg>(roots)
        let seen = System.Collections.Generic.HashSet<VReg>(roots)
        let live = System.Collections.Generic.HashSet<VReg>()
        while work.Count > 0 do
            let reg = work.Pop()
            match defUseMap.TryGetValue reg with
            | true, uses ->
                live.Add reg |> ignore
                for usedReg in uses do
                    if seen.Add usedReg then
                        work.Push usedReg
            | false, _ ->
                // Parameters or registers without a local definition.
                ()
        live)

/// Dead Code Elimination
/// Remove instructions whose destinations are never used (unless they have side effects)
let internal eliminateDeadCodeWithTickTrace
    (recordTicks: (string -> int64 -> unit) option)
    (cfg: CFG)
    : CFG * bool =
    let measure name operation =
        match recordTicks with
        | None -> operation ()
        | Some record ->
            let started = System.Diagnostics.Stopwatch.GetTimestamp()
            let result = operation ()
            record name (System.Diagnostics.Stopwatch.GetTimestamp() - started)
            result

    let liveDests =
        measure "MIR DCE Liveness" (fun () -> collectLiveDestinations recordTicks cfg)

    measure "MIR DCE Rewrite" (fun () ->
        let (blocks', changed) =
            cfg.Blocks
            |> Map.fold (fun (acc, ch) label block ->
                let (instrs', instrChanged) =
                    block.Instrs
                    |> List.fold (fun (acc', ch') instr ->
                        match getInstrDest instr with
                        | Some dest when not (liveDests.Contains dest) && not (hasSideEffects instr) ->
                            // Dead instruction - remove it
                            (acc', true)
                        | _ ->
                            // Keep instruction
                            (instr :: acc', ch')
                    ) ([], false)
                let instrs' = List.rev instrs'

                let block' = { block with Instrs = instrs' }
                (Map.add label block' acc, ch || instrChanged)
            ) (Map.empty, false)

        ({ cfg with Blocks = blocks' }, changed))

let eliminateDeadCode (cfg: CFG) : CFG * bool =
    eliminateDeadCodeWithTickTrace None cfg
