// LIRLayoutTests.fs - Tests deterministic CFG block layout for backend fallthrough.

module LIRLayoutTests

type TestResult = Result<unit, string>

let private branchFixture () : LIR.CFG =
    let entry = LIR.Label "entry"
    let trueBlock = LIR.Label "a_true"
    let falseBlock = LIR.Label "z_false"
    let join = LIR.Label "join"
    let block label terminator : LIR.BasicBlock = {
        Label = label
        Instrs = []
        Terminator = terminator
    }
    {
        Entry = entry
        Blocks = Map.ofList [
            entry, block entry (LIR.Branch (LIR.Physical LIR.X0, trueBlock, falseBlock))
            trueBlock, block trueBlock (LIR.Jump join)
            falseBlock, block falseBlock (LIR.Jump join)
            join, block join LIR.Ret
        ]
    }

let testLayoutDefersSharedReturn () : TestResult =
    let cfg = branchFixture ()
    match LIR.layoutBlocks cfg with
    | Error e -> Error e
    | Ok blocks ->
        let labels = blocks |> List.map (fun block -> block.Label)
        let expected = [LIR.Label "entry"; LIR.Label "z_false"; LIR.Label "a_true"; LIR.Label "join"]
        let preserved =
            List.length blocks = Map.count cfg.Blocks
            && (blocks |> List.map (fun block -> block.Label, block) |> Map.ofList) = cfg.Blocks
        if not preserved then Error "Layout changed or duplicated CFG blocks"
        elif labels = expected then Ok ()
        else Error $"Expected deterministic fallthrough layout {expected}, got {labels}"

let testLayoutPreservesOtherReturnShapes () : TestResult =
    let entry = LIR.Label "entry"
    let middle = LIR.Label "z_middle"
    let last = LIR.Label "a_last"
    let block label terminator : LIR.BasicBlock = {
        Label = label
        Instrs = [LIR.Mov (LIR.Physical LIR.X0, LIR.Imm 7L)]
        Terminator = terminator
    }
    let branch yes no = LIR.Branch (LIR.Physical LIR.X0, yes, no)
    let fixtures = [
        "single entry return", [entry, LIR.Ret], [entry]
        "entry is shared return", [entry, LIR.Ret; middle, LIR.Jump entry; last, LIR.Jump entry], [entry; last; middle]
        "multiple returns", [entry, branch last middle; middle, LIR.Ret; last, LIR.Ret], [entry; middle; last]
        "no return loop", [entry, LIR.Jump middle; middle, LIR.Jump entry; last, LIR.Jump middle], [entry; middle; last]
        "unshared return chain", [entry, LIR.Jump middle; middle, LIR.Jump last; last, LIR.Ret], [entry; middle; last]
        "duplicate edges are one predecessor", [entry, branch middle middle; middle, LIR.Ret; last, LIR.Jump entry], [entry; middle; last]
    ]
    fixtures
    |> List.fold (fun result (name, terminators, expected) ->
        result |> Result.bind (fun () ->
            let cfg : LIR.CFG = {
                Entry = entry
                Blocks = terminators |> List.map (fun (label, terminator) -> label, block label terminator) |> Map.ofList
            }
            LIR.layoutBlocks cfg
            |> Result.bind (fun blocks ->
                let labels = blocks |> List.map (fun block -> block.Label)
                let preserved =
                    List.length blocks = Map.count cfg.Blocks
                    && (blocks |> List.map (fun block -> block.Label, block) |> Map.ofList) = cfg.Blocks
                if not preserved then Error $"{name}: layout changed or duplicated CFG blocks"
                elif labels = expected then Ok ()
                else Error $"{name}: expected {expected}, got {labels}"))) (Ok ())

let testLayoutLeavesMissingSuccessorValidationToConsumers () : TestResult =
    let entry = LIR.Label "entry"
    let missing = LIR.Label "missing"
    let entryBlock : LIR.BasicBlock = {
        Label = entry
        Instrs = []
        Terminator = LIR.Jump missing
    }
    let cfg : LIR.CFG = { Entry = entry; Blocks = Map.ofList [entry, entryBlock] }

    match LIR.layoutBlocks cfg with
    | Ok [block] when block.Label = entry -> Ok ()
    | Ok blocks -> Error $"Expected only the entry block, got {blocks |> List.map (fun block -> block.Label)}"
    | Error e -> Error $"Layout preempted consumer validation of a missing successor: {e}"

let testLayoutReportsMissingEntryBlock () : TestResult =
    let entry = LIR.Label "entry"
    let cfg : LIR.CFG = { Entry = entry; Blocks = Map.empty }

    match LIR.layoutBlocks cfg with
    | Error e when e.Contains "missing entry block" -> Ok ()
    | Error e -> Error $"Expected missing entry block error, got '{e}'"
    | Ok _ -> Error "Expected layout to reject a missing entry block"

let tests : (string * (unit -> TestResult)) list = [
    ("LIR layout defers shared return", testLayoutDefersSharedReturn)
    ("LIR layout preserves other return shapes", testLayoutPreservesOtherReturnShapes)
    ("LIR layout leaves missing successor validation to consumers", testLayoutLeavesMissingSuccessorValidationToConsumers)
    ("LIR layout reports missing entry block", testLayoutReportsMissingEntryBlock)
]
