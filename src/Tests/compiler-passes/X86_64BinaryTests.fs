// X86_64BinaryTests.fs - End-to-end test for x86-64 binary generation
//
// Generates a minimal x86-64 ELF binary and verifies it can be executed.
// This proves the encoder + ELF generation pipeline works end-to-end.

module X86_64BinaryTests

open X86_64
open X86_64_Encoding

/// Generate machine code for "exit(42)" on x86-64 Linux:
///   MOV RAX, 60    (syscall number for exit)
///   MOV RDI, 42    (exit code)
///   SYSCALL
let private exitProgram (exitCode: int) : byte array =
    let instructions = [
        MOV_imm32 (RAX, 60)           // sys_exit = 60
        MOV_imm32 (RDI, exitCode)     // exit code
        SYSCALL
    ]
    instructions
    |> List.map encodeInstruction
    |> Array.concat

/// Test that we can generate a valid x86-64 ELF binary
let testGenerateElf () : Result<unit, string> =
    let machineCode = exitProgram 42
    let binary =
        Binary_Generation_ELF_X86_64.createExecutableWithPools
            machineCode
            LiteralPool.emptyStringPool
            LiteralPool.emptyFloatPool
            false

    // Verify ELF magic
    if binary.[0] <> 0x7Fuy || binary.[1] <> byte 'E' || binary.[2] <> byte 'L' || binary.[3] <> byte 'F' then
        Error "Missing ELF magic bytes"
    // Verify 64-bit
    elif binary.[4] <> 2uy then
        Error "Not ELF64"
    // Verify little-endian
    elif binary.[5] <> 1uy then
        Error "Not little-endian"
    // Verify machine type is x86-64 (0x3E = 62 at offset 18-19, little-endian)
    elif binary.[18] <> 0x3Euy || binary.[19] <> 0x00uy then
        Error $"Wrong machine type: expected 0x3E 0x00, got 0x{binary.[18]:X2} 0x{binary.[19]:X2}"
    else
        Ok ()

/// Test that the generated binary executes correctly (only on x86-64 hosts)
let testExecuteElf () : Result<unit, string> =
    match Platform.detectArch () with
    | Ok Platform.X86_64 ->
        let machineCode = exitProgram 42
        let binary =
            Binary_Generation_ELF_X86_64.createExecutableWithPools
                machineCode
                LiteralPool.emptyStringPool
                LiteralPool.emptyFloatPool
                false

        // Write to temp file and execute
        let tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"))
        try
            do
                use stream = new System.IO.FileStream(tempPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None)
                stream.Write(binary, 0, binary.Length)
                stream.Flush(true)

            let permissions = System.IO.File.GetUnixFileMode(tempPath)
            System.IO.File.SetUnixFileMode(tempPath, permissions ||| System.IO.UnixFileMode.UserExecute)

            let psi = System.Diagnostics.ProcessStartInfo(tempPath)
            psi.UseShellExecute <- false
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true

            use proc = System.Diagnostics.Process.Start(psi)
            proc.WaitForExit(5000) |> ignore

            if proc.ExitCode = 42 then
                Ok ()
            else
                Error $"Expected exit code 42, got {proc.ExitCode}"
        finally
            try System.IO.File.Delete(tempPath) with _ -> ()

    | Ok Platform.ARM64 ->
        // Skip on ARM64 — the binary won't run natively (could use qemu-user-static
        // but that's tested separately via binfmt_misc in the E2E suite)
        Ok ()
    | Error err ->
        Error $"Could not detect architecture: {err}"

let tests : (string * (unit -> Result<unit, string>)) list = [
    ("Generate x86-64 ELF", testGenerateElf)
    ("Execute x86-64 ELF", testExecuteElf)
]
