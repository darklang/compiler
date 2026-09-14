// LIRExecutionTestRunner.fs - Compiles and executes single-block LIR programs.
//
// Checks typed x64 failures and provides shared backend executable support.

module TestDSL.LIRExecutionTestRunner

open System
open System.IO
open System.Diagnostics
open TestDSL.Common
open TestDSL.LIRExecutionFormat

let private executableProgram program =
    match program with
    | LIR.Program (functions, variants, records) ->
        let entryFunctions, otherFunctions =
            functions |> List.partition (fun func -> func.Name = "_start")
        match entryFunctions with
        | [ func ] ->
            match func.CFG.Blocks |> Map.toList with
            | [ (_, body) ] ->
                let entryLabel = LIR.Label "_start_entry"
                let bodyLabel = LIR.Label "_start_body"
                let entryBlock: LIR.BasicBlock =
                    { Label = entryLabel
                      Instrs = []
                      Terminator = LIR.Jump bodyLabel }
                let bodyBlock = { body with Label = bodyLabel }
                let executableFunction =
                    { func with
                        Name = "_start"
                        CFG =
                            { Entry = entryLabel
                              Blocks = Map.ofList [ (entryLabel, entryBlock); (bodyLabel, bodyBlock) ] } }
                Ok (LIR.Program (executableFunction :: otherFunctions, variants, records))
            | blocks -> Error $"Executable LIR fixture requires one input block, got {List.length blocks}"
        | entries -> Error $"Executable LIR fixture requires one _start function, got {List.length entries}"

let private patchDeferredLabels
    (stringPool: LiteralPool.StringPool)
    (resolveResult: X86_64_Resolve.ResolveResult) =
    if List.isEmpty resolveResult.DeferredFixups then Ok resolveResult
    else
        let codeFileOffset = 64 + 56
        let codeSize = resolveResult.MachineCode.Length
        let dataLabels =
            X86_64_Resolve.dataLabelOffsets codeFileOffset codeSize stringPool
        X86_64_Resolve.patchDataLabels resolveResult dataLabels codeFileOffset

let private writeAndRun target binary =
    let tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    try
        do
            use stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)
            stream.Write(binary, 0, binary.Length)
            stream.Flush(true)

        let permissions = File.GetUnixFileMode(tempPath)
        File.SetUnixFileMode(tempPath, permissions ||| UnixFileMode.UserExecute)
        let startInfo =
            match target, Platform.detectArch () with
            | Platform.LinuxX86_64, Ok Platform.X86_64
            | Platform.ARM64Backend _, Ok Platform.ARM64 ->
                ProcessStartInfo(tempPath)
            | Platform.LinuxX86_64, _ ->
                ProcessStartInfo("/opt/dcb/qemu/qemu-x86_64", tempPath)
            | Platform.ARM64Backend _, _ ->
                ProcessStartInfo("/opt/dcb/qemu/qemu-aarch64", tempPath)
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        use proc = Process.Start(startInfo)
        let stdoutTask = proc.StandardOutput.ReadToEndAsync()
        let stderrTask = proc.StandardError.ReadToEndAsync()

        if proc.WaitForExit(10000) then
            Ok (proc.ExitCode, stdoutTask.Result, stderrTask.Result)
        else
            try proc.Kill(true) with _ -> ()
            Error "Execution timed out after 10000ms"
    with ex ->
        Error $"Execution failed: {ex.Message}"
    |> fun result ->
        try File.Delete(tempPath) with _ -> ()
        result

let private translate program leakCheck =
    let enableLeakCheck = leakCheck = LeakCheckEnabled
    executableProgram program
    |> Result.bind (fun executable ->
        CodeGen_X86_64.translateProgram executable enableLeakCheck)

let private executeX64Program program leakCheck =
    let enableLeakCheck = leakCheck = LeakCheckEnabled
    translate program leakCheck
    |> Result.mapError (fun msg -> $"Codegen error: {msg}")
    |> Result.bind (fun instructions ->
        let stringPool = X86_64_Resolve.collectStringPool instructions
        X86_64_Resolve.resolveAndEncode instructions
        |> Result.mapError (fun msg -> $"Resolve error: {msg}")
        |> Result.bind (patchDeferredLabels stringPool)
        |> Result.map (fun resolved -> (resolved, stringPool)))
    |> Result.bind (fun (resolved, stringPool) ->
        Binary_Generation_ELF_X86_64.createExecutableWithPools
            resolved.MachineCode
            stringPool
            LiteralPool.emptyFloatPool
            enableLeakCheck
            0
        |> writeAndRun Platform.LinuxX86_64)

let private executeARM64Program armTarget program leakCheck =
    let enableLeakCheck = leakCheck = LeakCheckEnabled
    let target = ARM64.targetConfigFor armTarget
    let options = { ARM64CodeGenTypes.defaultOptions with EnableLeakCheck = enableLeakCheck }

    executableProgram program
    |> Result.map ARM64PrepareFunctions.prepareARM64Program
    |> Result.bind (CodeGen.generateARM64WithOptions target options)
    |> Result.mapError (fun msg -> $"Codegen error: {msg}")
    |> Result.bind (fun generated ->
        let instructions = CodeGen.generatedProgramInstructions generated
        let labels =
            instructions
            |> List.choose (function | ARM64Symbolic.Label label -> Some label | _ -> None)
            |> Set.ofList
        match
            instructions
            |> List.tryPick (function
                | ARM64Symbolic.BL label when not (Set.contains label labels) -> Some label
                | _ -> None)
        with
        | Some label -> Error $"Codegen error: ARM64 call target '{label}' was not generated"
        | None ->
            Ok
                (ARM64_Emit.emitBinary
                    generated
                    (ARM64.targetOS target)
                    enableLeakCheck
                    None
                    None
                    None))
    |> Result.bind (fun emitted -> writeAndRun (Platform.ARM64Backend armTarget) emitted.Binary)

let internal executeProgram target program leakCheck =
    match target with
    | Platform.LinuxX86_64 -> executeX64Program program leakCheck
    | Platform.ARM64Backend armTarget -> executeARM64Program armTarget program leakCheck

let private checkExpectation (exitCode: int, stdout: string, stderr: string) expectation =
    match expectation with
    | ExpectedExitCode expected when exitCode = expected -> Ok ()
    | ExpectedExitCode expected -> Error $"Expected exit code {expected}, got {exitCode}"
    | ExpectedStdout expected when stdout.Trim() = expected -> Ok ()
    | ExpectedStdout expected -> Error $"Expected stdout '{expected}', got '{stdout.Trim()}'"
    | ExpectedStderr expected when stderr.Trim() = expected -> Ok ()
    | ExpectedStderr expected -> Error $"Expected stderr '{expected}', got '{stderr.Trim()}'"

let private checkProcessExpectations test expectations =
    executeProgram Platform.LinuxX86_64 test.Program test.LeakCheck
    |> Result.bind (fun actual ->
        let rec loop remaining =
            match remaining with
            | [] -> Ok ()
            | expectation :: rest ->
                checkExpectation actual expectation |> Result.bind (fun () -> loop rest)
        loop expectations)

let private checkCodegenError test (expected: string) =
    match translate test.Program test.LeakCheck with
    | Error msg when msg.Contains expected -> Ok ()
    | Error msg -> Error $"Expected codegen error containing '{expected}', got '{msg}'"
    | Ok _ -> Error $"Expected codegen error containing '{expected}', but translation succeeded"

let runLIRExecutionTest test =
    match test.Expectation with
    | ExpectedProcessResult expectations -> checkProcessExpectations test expectations
    | ExpectedCodegenError expected -> checkCodegenError test expected

let loadLIRExecutionTests path =
    if not (File.Exists path) then Error $"LIR-execution test file not found: {path}"
    else
        try File.ReadAllText path |> parseLIRExecutionFileContent path
        with ex -> Error $"Failed to read LIR-execution test file {path}: {ex.Message}"

let tests (testFiles: string array) : (string * (unit -> Result<unit, string>)) list =
    let testsForFile path =
        match loadLIRExecutionTests path with
        | Error msg -> [ ($"parse {Path.GetFileName path}", fun () -> Error msg) ]
        | Ok cases -> cases |> List.map (fun test -> test.Name, fun () -> runLIRExecutionTest test)

    testFiles |> Array.sort |> Array.toList |> List.collect testsForFile
