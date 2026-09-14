// Execution.fs - Run generated binaries through the host process boundary.

module CompilerExecution

open System
open System.IO
open System.Diagnostics
open System.Reflection
open System.Collections.Generic
open Output
open CompilerOptions
open CompilationSession
open SourcePreparation
/// Execute a compiled binary with positional arguments and finite stdin while
/// capturing both output streams.
let executeCapturedWithArgumentsAndEnvironment
    (target: Platform.Target)
    (verbosity: int)
    (arguments: string list)
    (environment: (string * string) list)
    (input: ExecutionInput)
    (binary: byte array)
    : ExecutionOutput =
    let sw = Stopwatch.StartNew()
    let finish (exitCode: int) (stdout: string) (stderr: string) : ExecutionOutput =
        sw.Stop()
        { ExitCode = exitCode
          Stdout = stdout
          Stderr = stderr
          RuntimeTime = sw.Elapsed }

    if verbosity >= 1 then println ""
    if verbosity >= 1 then println "  Execution:"

    // Write binary to temp file
    if verbosity >= 1 then println "    • Writing binary to temp file..."
    let tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))

    // Write and flush to disk to minimize (but not eliminate) "Text file busy" race
    do
        use stream = new IO.FileStream(tempPath, IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None)
        stream.Write(binary, 0, binary.Length)
        stream.Flush(true)  // Flush both stream and OS buffers to disk

    let writeTime = sw.Elapsed.TotalMilliseconds
    if verbosity >= 2 then println $"      {System.Math.Round(writeTime, 1)}ms"

    let result =
        try
            // Make executable using Unix file mode
            if verbosity >= 1 then println "    • Setting executable permissions..."
            let permissions = File.GetUnixFileMode(tempPath)
            File.SetUnixFileMode(tempPath, permissions ||| IO.UnixFileMode.UserExecute)
            let chmodTime = sw.Elapsed.TotalMilliseconds - writeTime
            if verbosity >= 2 then println $"      {System.Math.Round(chmodTime, 1)}ms"

            // Code sign with adhoc signature (required for macOS only)
            let codesignResult =
                if Platform.requiresCodeSigning (Platform.osFor target) then
                    if verbosity >= 1 then println "    • Code signing (adhoc)..."
                    let codesignStart = sw.Elapsed.TotalMilliseconds
                    let codesignInfo = ProcessStartInfo("codesign")
                    codesignInfo.Arguments <- $"-s - \"{tempPath}\""
                    codesignInfo.UseShellExecute <- false
                    codesignInfo.RedirectStandardOutput <- true
                    codesignInfo.RedirectStandardError <- true
                    let codesignProc = Process.Start(codesignInfo)
                    codesignProc.WaitForExit()

                    if codesignProc.ExitCode <> 0 then
                        let stderr = codesignProc.StandardError.ReadToEnd()
                        Some $"Code signing failed: {stderr}"
                    else
                        let codesignTime = sw.Elapsed.TotalMilliseconds - codesignStart
                        if verbosity >= 2 then println $"      {System.Math.Round(codesignTime, 1)}ms"
                        None
                else
                    if verbosity >= 1 then println "    • Code signing skipped (not required on Linux)"
                    None

            match codesignResult with
            | Some errorMsg ->
                // Code signing or platform detection failed - return error
                finish -1 "" errorMsg
            | None ->
                // Execute (with retry for "Text file busy" race condition)
                // Even with flush, kernel may not have fully synced file/permissions in fast test runs
                if verbosity >= 1 then println "    • Running binary..."
                let execStart = sw.Elapsed.TotalMilliseconds
                let execInfo = ProcessStartInfo(tempPath)
                execInfo.RedirectStandardOutput <- true
                execInfo.RedirectStandardError <- true
                execInfo.RedirectStandardInput <- true
                execInfo.UseShellExecute <- false
                arguments |> List.iter execInfo.ArgumentList.Add
                environment
                |> List.iter (fun (name, value) -> execInfo.Environment.[name] <- value)

                // Retry up to 3 times with small delay if we get "Text file busy"
                let rec startWithRetry attempts =
                    match tryStartProcess execInfo with
                    | Ok proc -> Ok proc
                    | Error msg when msg.Contains("Text file busy") && attempts > 0 ->
                        Threading.Thread.Sleep(10)  // Wait 10ms before retry
                        startWithRetry (attempts - 1)
                    | Error msg -> Error msg

                match startWithRetry 3 with
                | Error msg ->
                    finish -1 "" $"Failed to start process: {msg}"
                | Ok execProc ->
                    use proc = execProc
                    match input with
                    | Closed -> proc.StandardInput.Close()
                    | Bytes bytes ->
                        proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length)
                        proc.StandardInput.Close()
                    // Start async reads immediately to avoid blocking
                    let stdoutTask = proc.StandardOutput.ReadToEndAsync()
                    let stderrTask = proc.StandardError.ReadToEndAsync()

                    // Wait for process to complete
                    proc.WaitForExit()

                    // Now wait for output to be fully read
                    let stdout = stdoutTask.Result
                    let stderr = stderrTask.Result

                    let execTime = sw.Elapsed.TotalMilliseconds - execStart
                    if verbosity >= 2 then println $"      {System.Math.Round(execTime, 1)}ms"

                    if verbosity >= 1 then
                        println $"  ✓ Execution complete ({System.Math.Round(sw.Elapsed.TotalMilliseconds, 1)}ms)"

                    finish proc.ExitCode stdout stderr
        finally
            // Cleanup - ignore deletion errors
            tryDeleteFile tempPath
    result

let executeCapturedWithArguments
    (target: Platform.Target)
    (verbosity: int)
    (arguments: string list)
    (input: ExecutionInput)
    (binary: byte array)
    : ExecutionOutput =
    executeCapturedWithArgumentsAndEnvironment target verbosity arguments [] input binary

/// Execute a compiled binary with finite stdin while capturing both output streams.
let executeCaptured
    (target: Platform.Target)
    (verbosity: int)
    (input: ExecutionInput)
    (binary: byte array)
    : ExecutionOutput =
    executeCapturedWithArguments target verbosity [] input binary

/// Backward-compatible captured execution with an already-closed stdin stream.
let execute (target: Platform.Target) (verbosity: int) (binary: byte array) : ExecutionOutput =
    executeCaptured target verbosity Closed binary

/// Execute a compiled binary with stdin/stdout/stderr inherited from this process.
/// This is the interactive run path: presentation bytes are visible immediately
/// and the OS remains responsible for terminal and signal behavior.
let executeAttached
    (target: Platform.Target)
    (verbosity: int)
    (binary: byte array)
    : ExecutionOutput =
    let sw = Stopwatch.StartNew()
    let finish (exitCode: int) (stderr: string) : ExecutionOutput =
        sw.Stop()
        { ExitCode = exitCode
          Stdout = ""
          Stderr = stderr
          RuntimeTime = sw.Elapsed }

    if verbosity >= 1 then println ""
    if verbosity >= 1 then println "  Execution:"
    if verbosity >= 1 then println "    • Writing binary to temp file..."

    let tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    do
        use stream = new IO.FileStream(tempPath, IO.FileMode.Create, IO.FileAccess.Write, IO.FileShare.None)
        stream.Write(binary, 0, binary.Length)
        stream.Flush(true)

    let writeTime = sw.Elapsed.TotalMilliseconds
    if verbosity >= 2 then println $"      {System.Math.Round(writeTime, 1)}ms"

    let result =
        try
            if verbosity >= 1 then println "    • Setting executable permissions..."
            let permissions = File.GetUnixFileMode(tempPath)
            File.SetUnixFileMode(tempPath, permissions ||| IO.UnixFileMode.UserExecute)
            let chmodTime = sw.Elapsed.TotalMilliseconds - writeTime
            if verbosity >= 2 then println $"      {System.Math.Round(chmodTime, 1)}ms"

            let codesignResult =
                if Platform.requiresCodeSigning (Platform.osFor target) then
                    if verbosity >= 1 then println "    • Code signing (adhoc)..."
                    let codesignStart = sw.Elapsed.TotalMilliseconds
                    let codesignInfo = ProcessStartInfo("codesign")
                    codesignInfo.Arguments <- $"-s - \"{tempPath}\""
                    codesignInfo.UseShellExecute <- false
                    codesignInfo.RedirectStandardOutput <- true
                    codesignInfo.RedirectStandardError <- true
                    let codesignProc = Process.Start(codesignInfo)
                    codesignProc.WaitForExit()
                    if codesignProc.ExitCode <> 0 then
                        Some $"Code signing failed: {codesignProc.StandardError.ReadToEnd()}"
                    else
                        let codesignTime = sw.Elapsed.TotalMilliseconds - codesignStart
                        if verbosity >= 2 then println $"      {System.Math.Round(codesignTime, 1)}ms"
                        None
                else
                    if verbosity >= 1 then println "    • Code signing skipped (not required on Linux)"
                    None

            match codesignResult with
            | Some errorMsg -> finish -1 errorMsg
            | None ->
                if verbosity >= 1 then println "    • Running binary..."
                let execStart = sw.Elapsed.TotalMilliseconds
                let execInfo = ProcessStartInfo(tempPath)
                execInfo.UseShellExecute <- false

                let rec startWithRetry attempts =
                    match tryStartProcess execInfo with
                    | Ok proc -> Ok proc
                    | Error msg when msg.Contains("Text file busy") && attempts > 0 ->
                        Threading.Thread.Sleep(10)
                        startWithRetry (attempts - 1)
                    | Error msg -> Error msg

                match startWithRetry 3 with
                | Error msg -> finish -1 $"Failed to start process: {msg}"
                | Ok execProc ->
                    use proc = execProc
                    proc.WaitForExit()
                    let execTime = sw.Elapsed.TotalMilliseconds - execStart
                    if verbosity >= 2 then println $"      {System.Math.Round(execTime, 1)}ms"
                    if verbosity >= 1 then
                        println $"  ✓ Execution complete ({System.Math.Round(sw.Elapsed.TotalMilliseconds, 1)}ms)"
                    finish proc.ExitCode ""
        finally
            tryDeleteFile tempPath
    result
