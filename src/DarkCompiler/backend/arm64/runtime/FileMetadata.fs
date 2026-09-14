// FileMetadata.fs - Generate file existence, removal, and permission operations.

module ARM64FileMetadata

open ARM64RuntimeImmediates
open ARM64PrintAndExit
open ARM64PrintValues

/// Generate ARM64 instructions for Stdlib.File.exists
/// Input: pathReg contains heap string pointer (format: [refcount:8][len:8][data:N])
/// Output: destReg = 1 if file exists, 0 if not
///
/// Algorithm:
/// 1. Get data pointer from heap string (path + 16)
/// 2. Save the path string to a null-terminated temp buffer on stack
/// 3. Call access(path, F_OK) or faccessat(AT_FDCWD, path, F_OK, 0)
/// 4. If syscall returns 0 (success), set dest = 1; else dest = 0
let generateFileExists (target: ARM64.TargetConfig) (destReg: ARM64.Reg) (pathReg: ARM64.Reg) : ARM64.Instr list =
    let os = ARM64.targetOS target
    let syscalls = ARM64.targetSyscalls target

    // We need to null-terminate the string for the syscall
    // Stack layout: [saved regs:16][path:256]
    // Use simpler approach: just copy the string and add null terminator
    // Note: This is a simplification - paths > 255 chars will be truncated
    match os with
    | Platform.MacOS ->
        [
            // Save callee-saved registers we'll use
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.STP (ARM64.X21, ARM64.X22, ARM64.SP, -32s)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

            // Allocate stack space for path (256) plus caller-saved registers (64).
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 65us)

            // X19 = heap string base (pathReg), X20 = dest pointer for later
            ARM64.MOV_reg (ARM64.X19, pathReg)
            ARM64.MOV_reg (ARM64.X20, destReg)

            // Preserve live caller-saved registers across the delete syscall.
            ARM64.STR (ARM64.X1, ARM64.SP, 256s)
            ARM64.STR (ARM64.X2, ARM64.SP, 264s)
            ARM64.STR (ARM64.X3, ARM64.SP, 272s)
            ARM64.STR (ARM64.X4, ARM64.SP, 280s)
            ARM64.STR (ARM64.X5, ARM64.SP, 288s)
            ARM64.STR (ARM64.X6, ARM64.SP, 296s)
            ARM64.STR (ARM64.X7, ARM64.SP, 304s)
            ARM64.STR (ARM64.X8, ARM64.SP, 312s)

            // Use non-allocatable registers for copy loop to avoid clobbering live values
            // X10 = string length from [X19 + 8]
            ARM64.LDR (ARM64.X10, ARM64.X19, 8s)

            // X9 = dest pointer (SP)
            ARM64.MOV_reg (ARM64.X9, ARM64.SP)
            // X11 = source pointer (X19 + 16)
            ARM64.ADD_imm (ARM64.X11, ARM64.X19, 16us)

            // Copy loop: copies X10 bytes from X11 to X9
            // copy_loop (7 instructions, 0-6):
            ARM64.CBZ_offset (ARM64.X10, 7)     // 0: If length == 0, skip to null_term at inst 7
            ARM64.LDRB_imm (ARM64.X12, ARM64.X11, 0) // 1: Load byte from src
            ARM64.STRB (ARM64.X12, ARM64.X9, 0) // 2: Store byte to dest
            ARM64.ADD_imm (ARM64.X9, ARM64.X9, 1us) // 3: dest++
            ARM64.ADD_imm (ARM64.X11, ARM64.X11, 1us) // 4: src++
            ARM64.SUB_imm (ARM64.X10, ARM64.X10, 1us) // 5: len--
            ARM64.B (-6)                        // 6: Loop back to CBZ

            // null_term: Store null terminator at X9
            ARM64.MOVZ (ARM64.X12, 0us, 0)      // 7: X12 = 0
            ARM64.STRB (ARM64.X12, ARM64.X9, 0) // 8: Store 0 as null terminator

            // Call access(path, F_OK)
            // X0 = path (SP), X1 = mode (F_OK = 0)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            ARM64.MOVZ (ARM64.X1, 0us, 0)       // F_OK = 0
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Access, 0)  // access syscall
            ARM64.SVC syscalls.SvcImmediate

            // If X0 == 0, file exists; else doesn't
            // Use CMP + CSET to convert to boolean
            ARM64.CMP_imm (ARM64.X0, 0us)
            ARM64.CSET (ARM64.X0, ARM64.EQ)     // X0 = 1 if was 0, else 0

            // Cleanup - restore registers before moving result to dest
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 256us)  // Deallocate path buffer
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 0s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)
            ARM64.MOV_reg (destReg, ARM64.X0)   // Move result to dest after restoration
        ]
    | Platform.Linux ->
        [
            // Save callee-saved registers we'll use, plus extra space for caller-saved
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.STP (ARM64.X21, ARM64.X22, ARM64.SP, -32s)  // Save X21, X22 for preserving X1, X2
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

            // Allocate stack space for path (256 bytes)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 256us)

            // X19 = heap string base (pathReg)
            ARM64.MOV_reg (ARM64.X19, pathReg)

            // Save potentially live caller-saved registers (X1-X4) to callee-saved registers
            // This preserves any values that might be allocated to these registers
            ARM64.MOV_reg (ARM64.X21, ARM64.X1)  // Save X1
            ARM64.MOV_reg (ARM64.X22, ARM64.X2)  // Save X2

            // Use non-allocatable registers for copy loop to avoid clobbering live values
            // X10 = string length from [X19 + 8]
            ARM64.LDR (ARM64.X10, ARM64.X19, 8s)

            // X9 = dest pointer (SP)
            ARM64.MOV_reg (ARM64.X9, ARM64.SP)
            // X11 = source pointer (X19 + 16)
            ARM64.ADD_imm (ARM64.X11, ARM64.X19, 16us)

            // Copy loop (7 instructions, 0-6)
            ARM64.CBZ_offset (ARM64.X10, 7)     // 0: If length == 0, skip to null_term at inst 7
            ARM64.LDRB_imm (ARM64.X12, ARM64.X11, 0) // 1: Load byte from src
            ARM64.STRB (ARM64.X12, ARM64.X9, 0) // 2: Store byte to dest
            ARM64.ADD_imm (ARM64.X9, ARM64.X9, 1us) // 3: dest++
            ARM64.ADD_imm (ARM64.X11, ARM64.X11, 1us) // 4: src++
            ARM64.SUB_imm (ARM64.X10, ARM64.X10, 1us) // 5: len--
            ARM64.B (-6)                        // 6: Loop back to CBZ

            // null_term: Store null terminator
            ARM64.MOVZ (ARM64.X12, 0us, 0)      // 7: X12 = 0
            ARM64.STRB (ARM64.X12, ARM64.X9, 0) // 8: Store 0 as null terminator

            // Call faccessat(AT_FDCWD, path, F_OK, 0)
            // X0 = dirfd (AT_FDCWD = -100), X1 = path, X2 = mode (F_OK = 0), X3 = flags (0)
            ARM64.MOVZ (ARM64.X0, 100us, 0)
            ARM64.NEG (ARM64.X0, ARM64.X0)      // X0 = -100 (AT_FDCWD)
            ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // path
            ARM64.MOVZ (ARM64.X2, 0us, 0)       // F_OK = 0
            ARM64.MOVZ (ARM64.X3, 0us, 0)       // flags = 0
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Access, 0)  // faccessat syscall
            ARM64.SVC syscalls.SvcImmediate

            // If X0 == 0, file exists; else doesn't
            ARM64.CMP_imm (ARM64.X0, 0us)
            ARM64.CSET (ARM64.X0, ARM64.EQ)

            // Restore caller-saved registers
            ARM64.MOV_reg (ARM64.X1, ARM64.X21)  // Restore X1
            ARM64.MOV_reg (ARM64.X2, ARM64.X22)  // Restore X2

            // Cleanup - restore callee-saved registers
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 256us)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.LDP (ARM64.X21, ARM64.X22, ARM64.SP, 0s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 0s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)
            ARM64.MOV_reg (destReg, ARM64.X0)   // Move result to dest after restoration
        ]

/// Generate ARM64 instructions to delete a file and return Result<Unit, String>
/// destReg: destination register for the Result pointer
/// pathReg: register containing heap string pointer to file path
///
/// Result memory layout: [tag:8][payload:8][refcount:8] = 24 bytes
/// - tag 0 = Ok, tag 1 = Error
/// - payload = pointer to value (0 for Unit, string pointer for Error)
let generateFileDelete (target: ARM64.TargetConfig) (destReg: ARM64.Reg) (pathReg: ARM64.Reg) : ARM64.Instr list =
    let os = ARM64.targetOS target
    let syscalls = ARM64.targetSyscalls target

    match os with
    | Platform.MacOS ->
        [
            // Save callee-saved registers we'll use
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.STP (ARM64.X21, ARM64.X22, ARM64.SP, -32s)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

            // Allocate stack space for path (256) plus caller-saved registers (64).
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 65us)

            // X19 = heap string base (pathReg), X20 = dest pointer for later
            ARM64.MOV_reg (ARM64.X19, pathReg)
            ARM64.MOV_reg (ARM64.X20, destReg)

            // Preserve live caller-saved registers across the chmod syscall.
            ARM64.STR (ARM64.X1, ARM64.SP, 256s)
            ARM64.STR (ARM64.X2, ARM64.SP, 264s)
            ARM64.STR (ARM64.X3, ARM64.SP, 272s)
            ARM64.STR (ARM64.X4, ARM64.SP, 280s)
            ARM64.STR (ARM64.X5, ARM64.SP, 288s)
            ARM64.STR (ARM64.X6, ARM64.SP, 296s)
            ARM64.STR (ARM64.X7, ARM64.SP, 304s)
            ARM64.STR (ARM64.X8, ARM64.SP, 312s)

            // X2 = string length from [X19]
            ARM64.LDR (ARM64.X2, ARM64.X19, 8s)

            // X0 = dest pointer (SP)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            // X1 = source pointer (X19 + 16)
            ARM64.ADD_imm (ARM64.X1, ARM64.X19, 16us)
            // X4 = 0 (for LDRB offset)
            ARM64.MOVZ (ARM64.X4, 0us, 0)

            // Copy loop: copies X2 bytes from X1 to X0
            // copy_loop (7 instructions, 0-6):
            ARM64.CBZ_offset (ARM64.X2, 7)      // 0: If length == 0, skip to null_term at inst 7
            ARM64.LDRB (ARM64.X3, ARM64.X1, ARM64.X4) // 1: Load byte from src [X1 + 0]
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)  // 2: Store byte to dest
            ARM64.ADD_imm (ARM64.X0, ARM64.X0, 1us) // 3: dest++
            ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us) // 4: src++
            ARM64.SUB_imm (ARM64.X2, ARM64.X2, 1us) // 5: len--
            ARM64.B (-6)                        // 6: Loop back to CBZ

            // null_term: Store null terminator at X0
            ARM64.MOVZ (ARM64.X3, 0us, 0)       // X3 = 0
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)  // Store null terminator

            // Call unlink(path)
            // X0 = path (SP)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Unlink, 0)  // unlink syscall
            ARM64.SVC syscalls.SvcImmediate

            // Save syscall result in X21
            ARM64.MOV_reg (ARM64.X21, ARM64.X0)

            // Allocate Result on heap: [tag:8][payload:8][refcount:8] = 24 bytes
            ARM64.MOV_reg (ARM64.X0, ARM64.X28)  // X0 = result pointer
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)  // bump allocator

            // Check if unlink succeeded (X21 == 0)
            ARM64.CMP_imm (ARM64.X21, 0us)
            ARM64.B_cond (ARM64.NE, 7)  // If failed, jump to error path

            // Success path: Result = Ok(())
            ARM64.MOVZ (ARM64.X1, 0us, 0)       // tag = 0 (Ok)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)  // Store tag
            ARM64.STR (ARM64.X1, ARM64.X0, 8s)  // payload = 0 (Unit)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s) // refcount = 1
            ARM64.B (27)  // Jump to cleanup (skip error path)

            // Error path: Result = Error("Error")
            // Allocate error string: "Error" (5 chars)
            // String format: [refcount:8][length:8][data:N]
            ARM64.MOV_reg (ARM64.X2, ARM64.X28)  // X2 = error string pointer  (1)
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)  // (2)
            ARM64.MOVZ (ARM64.X1, 5us, 0)       // length = 5  (3)
            ARM64.STR (ARM64.X1, ARM64.X2, 8s)  // Store length  (4)
            // Store "Error" byte by byte: E=69, r=114, r=114, o=111, r=114
            ARM64.MOVZ (ARM64.X1, 69us, 0)      // 'E'  (5)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 16us)  // (6)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (7)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'  (8)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 17us)  // (9)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (10)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'  (11)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 18us)  // (12)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (13)
            ARM64.MOVZ (ARM64.X1, 111us, 0)     // 'o'  (14)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 19us)  // (15)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (16)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'  (17)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 20us)  // (18)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (19)
            ARM64.MOVZ (ARM64.X1, 1us, 0)  // (20)
            ARM64.STR (ARM64.X1, ARM64.X2, 0s) // refcount = 1  (21)
            // Now store Result with error
            ARM64.MOVZ (ARM64.X1, 1us, 0)       // tag = 1 (Error)  (22)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)  // Store tag  (23)
            ARM64.STR (ARM64.X2, ARM64.X0, 8s)  // payload = error string pointer  (24)
            ARM64.MOVZ (ARM64.X1, 1us, 0)  // (25)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s) // refcount = 1  (26)

            // Cleanup - restore registers and move result to dest
            ARM64.LDR (ARM64.X1, ARM64.SP, 256s)
            ARM64.LDR (ARM64.X2, ARM64.SP, 264s)
            ARM64.LDR (ARM64.X3, ARM64.SP, 272s)
            ARM64.LDR (ARM64.X4, ARM64.SP, 280s)
            ARM64.LDR (ARM64.X5, ARM64.SP, 288s)
            ARM64.LDR (ARM64.X6, ARM64.SP, 296s)
            ARM64.LDR (ARM64.X7, ARM64.SP, 304s)
            ARM64.LDR (ARM64.X8, ARM64.SP, 312s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 65us)
            ARM64.LDP (ARM64.X21, ARM64.X22, ARM64.SP, 0s)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 16s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.MOV_reg (destReg, ARM64.X0)   // Move result to dest after restoration
        ]
    | Platform.Linux ->
        [
            // Save callee-saved registers we'll use
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.STP (ARM64.X21, ARM64.X22, ARM64.SP, -32s)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

            // Allocate stack space for path (256) plus caller-saved registers (64).
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 65us)

            // X19 = heap string base (pathReg)
            ARM64.MOV_reg (ARM64.X19, pathReg)

            // Preserve live caller-saved registers across the delete syscall.
            ARM64.STR (ARM64.X1, ARM64.SP, 256s)
            ARM64.STR (ARM64.X2, ARM64.SP, 264s)
            ARM64.STR (ARM64.X3, ARM64.SP, 272s)
            ARM64.STR (ARM64.X4, ARM64.SP, 280s)
            ARM64.STR (ARM64.X5, ARM64.SP, 288s)
            ARM64.STR (ARM64.X6, ARM64.SP, 296s)
            ARM64.STR (ARM64.X7, ARM64.SP, 304s)
            ARM64.STR (ARM64.X8, ARM64.SP, 312s)

            // X2 = string length from [X19]
            ARM64.LDR (ARM64.X2, ARM64.X19, 8s)

            // X0 = dest pointer (SP)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            // X1 = source pointer (X19 + 16)
            ARM64.ADD_imm (ARM64.X1, ARM64.X19, 16us)
            // X4 = 0 (for LDRB offset)
            ARM64.MOVZ (ARM64.X4, 0us, 0)

            // Copy loop (7 instructions, 0-6)
            ARM64.CBZ_offset (ARM64.X2, 7)      // 0: If length == 0, skip to null_term at inst 7
            ARM64.LDRB (ARM64.X3, ARM64.X1, ARM64.X4)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)
            ARM64.ADD_imm (ARM64.X0, ARM64.X0, 1us)
            ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
            ARM64.SUB_imm (ARM64.X2, ARM64.X2, 1us)
            ARM64.B (-6)

            // null_term: Store null terminator
            ARM64.MOVZ (ARM64.X3, 0us, 0)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)

            // Call unlinkat(AT_FDCWD, path, 0)
            // X0 = dirfd (AT_FDCWD = -100), X1 = path, X2 = flags (0)
            ARM64.MOVZ (ARM64.X0, 100us, 0)
            ARM64.NEG (ARM64.X0, ARM64.X0)      // X0 = -100 (AT_FDCWD)
            ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // path
            ARM64.MOVZ (ARM64.X2, 0us, 0)       // flags = 0
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Unlink, 0)  // unlinkat syscall
            ARM64.SVC syscalls.SvcImmediate

            // Save syscall result in X21
            ARM64.MOV_reg (ARM64.X21, ARM64.X0)

            // Allocate Result on heap: [tag:8][payload:8][refcount:8] = 24 bytes
            ARM64.MOV_reg (ARM64.X0, ARM64.X28)  // X0 = result pointer
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)  // bump allocator

            // Check if unlink succeeded (X21 == 0)
            ARM64.CMP_imm (ARM64.X21, 0us)
            ARM64.B_cond (ARM64.NE, 7)  // If failed, jump to error path

            // Success path: Result = Ok(())
            ARM64.MOVZ (ARM64.X1, 0us, 0)       // tag = 0 (Ok)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)  // Store tag
            ARM64.STR (ARM64.X1, ARM64.X0, 8s)  // payload = 0 (Unit)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s) // refcount = 1
            ARM64.B (27)  // Jump to cleanup (skip error path)

            // Error path: Result = Error("Error")
            ARM64.MOV_reg (ARM64.X2, ARM64.X28)  // X2 = error string pointer  (1)
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)  // (2)
            ARM64.MOVZ (ARM64.X1, 5us, 0)       // length = 5  (3)
            ARM64.STR (ARM64.X1, ARM64.X2, 8s)  // Store length  (4)
            // Store "Error" byte by byte: E=69, r=114, r=114, o=111, r=114
            ARM64.MOVZ (ARM64.X1, 69us, 0)      // 'E'  (5)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 16us)  // (6)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (7)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'  (8)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 17us)  // (9)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (10)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'  (11)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 18us)  // (12)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (13)
            ARM64.MOVZ (ARM64.X1, 111us, 0)     // 'o'  (14)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 19us)  // (15)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (16)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'  (17)
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 20us)  // (18)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)  // (19)
            ARM64.MOVZ (ARM64.X1, 1us, 0)  // (20)
            ARM64.STR (ARM64.X1, ARM64.X2, 0s) // refcount = 1  (21)
            ARM64.MOVZ (ARM64.X1, 1us, 0)       // tag = 1 (Error)  (22)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)  // Store tag  (23)
            ARM64.STR (ARM64.X2, ARM64.X0, 8s)  // payload = error string pointer  (24)
            ARM64.MOVZ (ARM64.X1, 1us, 0)  // (25)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s) // refcount = 1  (26)

            // Cleanup - restore registers and move result to dest
            ARM64.LDR (ARM64.X1, ARM64.SP, 256s)
            ARM64.LDR (ARM64.X2, ARM64.SP, 264s)
            ARM64.LDR (ARM64.X3, ARM64.SP, 272s)
            ARM64.LDR (ARM64.X4, ARM64.SP, 280s)
            ARM64.LDR (ARM64.X5, ARM64.SP, 288s)
            ARM64.LDR (ARM64.X6, ARM64.SP, 296s)
            ARM64.LDR (ARM64.X7, ARM64.SP, 304s)
            ARM64.LDR (ARM64.X8, ARM64.SP, 312s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 65us)
            ARM64.LDP (ARM64.X21, ARM64.X22, ARM64.SP, 0s)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 16s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.MOV_reg (destReg, ARM64.X0)   // Move result to dest after restoration
        ]

/// Generate ARM64 instructions to set executable permission on a file
/// destReg: destination register for the Result pointer
/// pathReg: register containing heap string pointer to file path
///
/// Result memory layout: [tag:8][payload:8][refcount:8] = 24 bytes
/// - tag 0 = Ok, tag 1 = Error
/// - payload = pointer to value (0 for Unit, string pointer for Error)
let generateFileSetExecutable (target: ARM64.TargetConfig) (destReg: ARM64.Reg) (pathReg: ARM64.Reg) : ARM64.Instr list =
    let os = ARM64.targetOS target
    let syscalls = ARM64.targetSyscalls target

    match os with
    | Platform.MacOS ->
        [
            // Save callee-saved registers we'll use
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 16us)

            // Allocate stack space for path (256 bytes, 16-byte aligned)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 256us)

            // X19 = heap string base (pathReg), X20 = dest pointer for later
            ARM64.MOV_reg (ARM64.X19, pathReg)
            ARM64.MOV_reg (ARM64.X20, destReg)

            // X2 = string length from [X19]
            ARM64.LDR (ARM64.X2, ARM64.X19, 8s)

            // X0 = dest pointer (SP)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            // X1 = source pointer (X19 + 16)
            ARM64.ADD_imm (ARM64.X1, ARM64.X19, 16us)
            // X4 = 0 (for LDRB offset)
            ARM64.MOVZ (ARM64.X4, 0us, 0)

            // Copy loop: copies X2 bytes from X1 to X0
            ARM64.CBZ_offset (ARM64.X2, 7)
            ARM64.LDRB (ARM64.X3, ARM64.X1, ARM64.X4)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)
            ARM64.ADD_imm (ARM64.X0, ARM64.X0, 1us)
            ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
            ARM64.SUB_imm (ARM64.X2, ARM64.X2, 1us)
            ARM64.B (-6)

            // null_term: Store null terminator at X0
            ARM64.MOVZ (ARM64.X3, 0us, 0)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)

            // Call chmod(path, 0755)
            // X0 = path (SP), X1 = mode (0755 = 493 in decimal)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            ARM64.MOVZ (ARM64.X1, 493us, 0)   // 0755 = 493
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Chmod, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Save syscall result in X21
            ARM64.MOV_reg (ARM64.X21, ARM64.X0)

            // Allocate Result on heap: [tag:8][payload:8][refcount:8] = 24 bytes
            ARM64.MOV_reg (ARM64.X0, ARM64.X28)
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)

            // Check if chmod succeeded (X21 == 0)
            ARM64.CMP_imm (ARM64.X21, 0us)
            ARM64.B_cond (ARM64.NE, 7)

            // Success path: Result = Ok(())
            ARM64.MOVZ (ARM64.X1, 0us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)
            ARM64.STR (ARM64.X1, ARM64.X0, 8s)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s)
            ARM64.B (27)

            // Error path: Result = Error("Error")
            ARM64.MOV_reg (ARM64.X2, ARM64.X28)
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)
            ARM64.MOVZ (ARM64.X1, 5us, 0)
            ARM64.STR (ARM64.X1, ARM64.X2, 8s)
            ARM64.MOVZ (ARM64.X1, 69us, 0)      // 'E'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 16us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 17us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 18us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 111us, 0)     // 'o'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 19us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 20us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X2, 0s)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)
            ARM64.STR (ARM64.X2, ARM64.X0, 8s)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s)

            // Cleanup
            ARM64.LDR (ARM64.X1, ARM64.SP, 256s)
            ARM64.LDR (ARM64.X2, ARM64.SP, 264s)
            ARM64.LDR (ARM64.X3, ARM64.SP, 272s)
            ARM64.LDR (ARM64.X4, ARM64.SP, 280s)
            ARM64.LDR (ARM64.X5, ARM64.SP, 288s)
            ARM64.LDR (ARM64.X6, ARM64.SP, 296s)
            ARM64.LDR (ARM64.X7, ARM64.SP, 304s)
            ARM64.LDR (ARM64.X8, ARM64.SP, 312s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 65us)
            ARM64.LDP (ARM64.X21, ARM64.X22, ARM64.SP, 0s)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 16s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]
    | Platform.Linux ->
        [
            // Save callee-saved registers we'll use
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.STP (ARM64.X21, ARM64.X22, ARM64.SP, -32s)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

            // Allocate stack space for path (256) plus caller-saved registers (64).
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 65us)

            // X19 = heap string base (pathReg)
            ARM64.MOV_reg (ARM64.X19, pathReg)

            // Preserve live caller-saved registers across the chmod syscall.
            ARM64.STR (ARM64.X1, ARM64.SP, 256s)
            ARM64.STR (ARM64.X2, ARM64.SP, 264s)
            ARM64.STR (ARM64.X3, ARM64.SP, 272s)
            ARM64.STR (ARM64.X4, ARM64.SP, 280s)
            ARM64.STR (ARM64.X5, ARM64.SP, 288s)
            ARM64.STR (ARM64.X6, ARM64.SP, 296s)
            ARM64.STR (ARM64.X7, ARM64.SP, 304s)
            ARM64.STR (ARM64.X8, ARM64.SP, 312s)

            // X2 = string length from [X19]
            ARM64.LDR (ARM64.X2, ARM64.X19, 8s)

            // X0 = dest pointer (SP)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            // X1 = source pointer (X19 + 16)
            ARM64.ADD_imm (ARM64.X1, ARM64.X19, 16us)
            // X4 = 0 (for LDRB offset)
            ARM64.MOVZ (ARM64.X4, 0us, 0)

            // Copy loop (7 instructions, 0-6)
            ARM64.CBZ_offset (ARM64.X2, 7)
            ARM64.LDRB (ARM64.X3, ARM64.X1, ARM64.X4)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)
            ARM64.ADD_imm (ARM64.X0, ARM64.X0, 1us)
            ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
            ARM64.SUB_imm (ARM64.X2, ARM64.X2, 1us)
            ARM64.B (-6)

            // null_term: Store null terminator
            ARM64.MOVZ (ARM64.X3, 0us, 0)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)

            // Call fchmodat(AT_FDCWD, path, 0755, 0)
            // X0 = dirfd (AT_FDCWD = -100), X1 = path, X2 = mode (0755 = 493), X3 = flags (0)
            ARM64.MOVZ (ARM64.X0, 100us, 0)
            ARM64.NEG (ARM64.X0, ARM64.X0)
            ARM64.MOV_reg (ARM64.X1, ARM64.SP)
            ARM64.MOVZ (ARM64.X2, 493us, 0)   // 0755 = 493
            ARM64.MOVZ (ARM64.X3, 0us, 0)
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Chmod, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Save syscall result in X21
            ARM64.MOV_reg (ARM64.X21, ARM64.X0)

            // Allocate Result on heap
            ARM64.MOV_reg (ARM64.X0, ARM64.X28)
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)

            // Check if chmod succeeded (X21 == 0)
            ARM64.CMP_imm (ARM64.X21, 0us)
            ARM64.B_cond (ARM64.NE, 7)

            // Success path: Result = Ok(())
            ARM64.MOVZ (ARM64.X1, 0us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)
            ARM64.STR (ARM64.X1, ARM64.X0, 8s)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s)
            ARM64.B (27)

            // Error path: Result = Error("Error")
            ARM64.MOV_reg (ARM64.X2, ARM64.X28)
            ARM64.ADD_imm (ARM64.X28, ARM64.X28, 24us)
            ARM64.MOVZ (ARM64.X1, 5us, 0)
            ARM64.STR (ARM64.X1, ARM64.X2, 8s)
            ARM64.MOVZ (ARM64.X1, 69us, 0)      // 'E'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 16us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 17us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 18us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 111us, 0)     // 'o'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 19us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 114us, 0)     // 'r'
            ARM64.ADD_imm (ARM64.X3, ARM64.X2, 20us)
            ARM64.STRB_reg (ARM64.X1, ARM64.X3)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X2, 0s)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 0s)
            ARM64.STR (ARM64.X2, ARM64.X0, 8s)
            ARM64.MOVZ (ARM64.X1, 1us, 0)
            ARM64.STR (ARM64.X1, ARM64.X0, 16s)

            // Cleanup
            ARM64.LDR (ARM64.X1, ARM64.SP, 256s)
            ARM64.LDR (ARM64.X2, ARM64.SP, 264s)
            ARM64.LDR (ARM64.X3, ARM64.SP, 272s)
            ARM64.LDR (ARM64.X4, ARM64.SP, 280s)
            ARM64.LDR (ARM64.X5, ARM64.SP, 288s)
            ARM64.LDR (ARM64.X6, ARM64.SP, 296s)
            ARM64.LDR (ARM64.X7, ARM64.SP, 304s)
            ARM64.LDR (ARM64.X8, ARM64.SP, 312s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 65us)
            ARM64.LDP (ARM64.X21, ARM64.X22, ARM64.SP, 0s)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 16s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]
