// PrintValues.fs - Generate ARM64 nonterminating value printers.

module ARM64PrintValues

open ARM64RuntimeImmediates
open ARM64PrintAndExit

/// Generate ARM64 instructions to print int64 in X0 to stdout with newline (NO EXIT)
/// Same as generatePrintInt64 but returns instead of exiting
let generatePrintInt64NoExit (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 32 bytes on stack for buffer
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

        // Setup: X1 = buffer pointer, X2 = value
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 31us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)

        // Store newline at end of buffer
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)

        // Initialize: X6 = 0 (positive flag), X3 = 10 (divisor)
        ARM64.MOVZ (ARM64.X6, 0us, 0)
        ARM64.MOVZ (ARM64.X3, 10us, 0)

        // Check for negative: if X2 < 0, branch to handle_negative (at instruction 33)
        ARM64.CMP_imm (ARM64.X2, 0us)
        ARM64.B_cond (ARM64.LT, 25)  // 33 - 8 = 25

        // Check for zero: if X2 == 0, branch to print_zero (at instruction 29)
        ARM64.CBZ_offset (ARM64.X2, 20)  // 29 - 9 = 20

        // digit_loop: Extract digits
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)
        ARM64.CBNZ_offset (ARM64.X2, -6)

        // store_minus_if_needed
        ARM64.CBZ_offset (ARM64.X6, 4)
        ARM64.MOVZ (ARM64.X3, 45us, 0)
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // write_output
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 32us)
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)  // Deallocate stack
        ARM64.B (8)  // Skip past print_zero (4) and handle_negative (3) + 1 to exit runtime (8 instructions)

        // print_zero (at instruction 29)
        ARM64.MOVZ (ARM64.X2, 48us, 0)
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.B (-15)  // Jump to store_minus_if_needed: 17 - 32 = -15

        // handle_negative (at instruction 33)
        ARM64.NEG (ARM64.X2, ARM64.X2)
        ARM64.MOVZ (ARM64.X6, 1us, 0)
        ARM64.B (-26)  // Jump to check_zero: 9 - 35 = -26
    ]

/// Generate ARM64 instructions to print uint64 in X0 to stdout with newline (NO EXIT)
let generatePrintUInt64NoExit (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 31us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.CBZ_offset (ARM64.X2, 16)
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)
        ARM64.CBNZ_offset (ARM64.X2, -6)
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 32us)
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
        ARM64.B 5
        ARM64.MOVZ (ARM64.X2, 48us, 0)
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.B -11
    ]

/// Generate ARM64 instructions to print int64 in X0 to stderr with newline (NO EXIT)
/// Same as generatePrintInt64NoExit but writes to file descriptor 2
let generatePrintInt64ToStderrNoExit (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 32 bytes on stack for buffer
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

        // Setup: X1 = buffer pointer, X2 = value
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 31us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)

        // Store newline at end of buffer
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)

        // Initialize: X6 = 0 (positive flag), X3 = 10 (divisor)
        ARM64.MOVZ (ARM64.X6, 0us, 0)
        ARM64.MOVZ (ARM64.X3, 10us, 0)

        // Check for negative: if X2 < 0, branch to handle_negative (at instruction 33)
        ARM64.CMP_imm (ARM64.X2, 0us)
        ARM64.B_cond (ARM64.LT, 25)  // 33 - 8 = 25

        // Check for zero: if X2 == 0, branch to print_zero (at instruction 29)
        ARM64.CBZ_offset (ARM64.X2, 20)  // 29 - 9 = 20

        // digit_loop: Extract digits
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)
        ARM64.CBNZ_offset (ARM64.X2, -6)

        // store_minus_if_needed
        ARM64.CBZ_offset (ARM64.X6, 4)
        ARM64.MOVZ (ARM64.X3, 45us, 0)
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // write_output
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 32us)
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)
        ARM64.MOVZ (ARM64.X0, 2us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)  // Deallocate stack
        ARM64.B (8)  // Skip past print_zero (4) and handle_negative (3) + 1 to exit runtime (8 instructions)

        // print_zero (at instruction 29)
        ARM64.MOVZ (ARM64.X2, 48us, 0)
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.B (-15)  // Jump to store_minus_if_needed: 17 - 32 = -15

        // handle_negative (at instruction 33)
        ARM64.NEG (ARM64.X2, ARM64.X2)
        ARM64.MOVZ (ARM64.X6, 1us, 0)
        ARM64.B (-26)  // Jump to check_zero: 9 - 35 = -26
    ]

/// Generate ARM64 instructions to print boolean in X0 to stdout with newline (NO EXIT)
/// Same as generatePrintBool but returns instead of exiting
let generatePrintBoolNoExit (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 16 bytes on stack for buffer
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 16us)

        // Check if false (X0 == 0), branch to print_false (+17 instructions)
        ARM64.CBZ_offset (ARM64.X0, 17)

        // print_true: Store "true\n" on stack (5 bytes)
        ARM64.MOVZ (ARM64.X3, 116us, 0)  // 't'
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)
        ARM64.MOVZ (ARM64.X3, 114us, 0)  // 'r'
        ARM64.STRB (ARM64.X3, ARM64.SP, 1)
        ARM64.MOVZ (ARM64.X3, 117us, 0)  // 'u'
        ARM64.STRB (ARM64.X3, ARM64.SP, 2)
        ARM64.MOVZ (ARM64.X3, 101us, 0)  // 'e'
        ARM64.STRB (ARM64.X3, ARM64.SP, 3)
        ARM64.MOVZ (ARM64.X3, 10us, 0)   // '\n'
        ARM64.STRB (ARM64.X3, ARM64.SP, 4)
        ARM64.MOVZ (ARM64.X2, 5us, 0)    // length = 5

        // Write and cleanup (no exit)
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.B (18)  // Jump to cleanup (+18 instructions to skip false branch)

        // print_false: Store "false\n" on stack (6 bytes)
        ARM64.MOVZ (ARM64.X3, 102us, 0)  // 'f'
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)
        ARM64.MOVZ (ARM64.X3, 97us, 0)   // 'a'
        ARM64.STRB (ARM64.X3, ARM64.SP, 1)
        ARM64.MOVZ (ARM64.X3, 108us, 0)  // 'l'
        ARM64.STRB (ARM64.X3, ARM64.SP, 2)
        ARM64.MOVZ (ARM64.X3, 115us, 0)  // 's'
        ARM64.STRB (ARM64.X3, ARM64.SP, 3)
        ARM64.MOVZ (ARM64.X3, 101us, 0)  // 'e'
        ARM64.STRB (ARM64.X3, ARM64.SP, 4)
        ARM64.MOVZ (ARM64.X3, 10us, 0)   // '\n'
        ARM64.STRB (ARM64.X3, ARM64.SP, 5)
        ARM64.MOVZ (ARM64.X2, 6us, 0)    // length = 6

        // Write
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // cleanup (no exit):
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)
    ]

/// Generate ARM64 instructions to print int64 in X0 to stdout WITHOUT newline
/// For use in tuple/list element printing
let generatePrintInt64NoNewline (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 32 bytes on stack for buffer
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

        // Setup: X1 = buffer pointer (start at end-1, no newline), X2 = value
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 30us)  // One less than with newline
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)

        // Initialize: X6 = 0 (positive flag), X3 = 10 (divisor)
        ARM64.MOVZ (ARM64.X6, 0us, 0)
        ARM64.MOVZ (ARM64.X3, 10us, 0)

        // Check for negative: if X2 < 0, branch to handle_negative (at index 31)
        ARM64.CMP_imm (ARM64.X2, 0us)
        ARM64.B_cond (ARM64.LT, 25)  // 31 - 6 = 25

        // Check for zero: if X2 == 0, branch to print_zero (at index 27)
        ARM64.CBZ_offset (ARM64.X2, 20)  // 27 - 7 = 20

        // digit_loop: Extract digits
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)
        ARM64.CBNZ_offset (ARM64.X2, -6)

        // store_minus_if_needed
        ARM64.CBZ_offset (ARM64.X6, 4)
        ARM64.MOVZ (ARM64.X3, 45us, 0)
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // write_output
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 31us)  // End of buffer area
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)  // Deallocate stack
        ARM64.B (8)  // Skip past print_zero (4) and handle_negative (3) + 1 to exit

        // print_zero (at index 27)
        ARM64.MOVZ (ARM64.X2, 48us, 0)
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.B (-15)  // Jump to store_minus_if_needed at index 15: 15 - 30 = -15

        // handle_negative (at index 31)
        ARM64.NEG (ARM64.X2, ARM64.X2)
        ARM64.MOVZ (ARM64.X6, 1us, 0)
        ARM64.B (-25)  // Jump to digit_loop at index 8: 8 - 33 = -25
    ]

/// Generate ARM64 instructions to print uint64 in X0 to stdout WITHOUT newline
let generatePrintUInt64NoNewline (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 30us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.CBZ_offset (ARM64.X2, 16)
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)
        ARM64.CBNZ_offset (ARM64.X2, -6)
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 31us)
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
        ARM64.B 5
        ARM64.MOVZ (ARM64.X2, 48us, 0)
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.B -11
    ]

/// Generate ARM64 instructions to print boolean in X0 to stdout WITHOUT newline
/// For use in tuple/list element printing
let generatePrintBoolNoNewline (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 16 bytes on stack for buffer
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 16us)

        // Check if false (X0 == 0), branch to print_false at index 16
        ARM64.CBZ_offset (ARM64.X0, 15)  // 16 - 1 = 15

        // print_true: Store "true" on stack (4 bytes, no newline)
        ARM64.MOVZ (ARM64.X3, 116us, 0)  // 't'
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)
        ARM64.MOVZ (ARM64.X3, 114us, 0)  // 'r'
        ARM64.STRB (ARM64.X3, ARM64.SP, 1)
        ARM64.MOVZ (ARM64.X3, 117us, 0)  // 'u'
        ARM64.STRB (ARM64.X3, ARM64.SP, 2)
        ARM64.MOVZ (ARM64.X3, 101us, 0)  // 'e'
        ARM64.STRB (ARM64.X3, ARM64.SP, 3)
        ARM64.MOVZ (ARM64.X2, 4us, 0)    // length = 4 (no newline)

        // Write and cleanup
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.B (16)  // Jump to cleanup at index 31: 31 - 15 = 16

        // print_false: Store "false" on stack (5 bytes, no newline)
        ARM64.MOVZ (ARM64.X3, 102us, 0)  // 'f'
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)
        ARM64.MOVZ (ARM64.X3, 97us, 0)   // 'a'
        ARM64.STRB (ARM64.X3, ARM64.SP, 1)
        ARM64.MOVZ (ARM64.X3, 108us, 0)  // 'l'
        ARM64.STRB (ARM64.X3, ARM64.SP, 2)
        ARM64.MOVZ (ARM64.X3, 115us, 0)  // 's'
        ARM64.STRB (ARM64.X3, ARM64.SP, 3)
        ARM64.MOVZ (ARM64.X3, 101us, 0)  // 'e'
        ARM64.STRB (ARM64.X3, ARM64.SP, 4)
        ARM64.MOVZ (ARM64.X2, 5us, 0)    // length = 5 (no newline)

        // Write
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // cleanup:
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)
    ]

/// Generate ARM64 instructions to print float in D0 to stdout WITHOUT newline
/// For use in tuple/list element printing
/// Similar to generatePrintFloat but doesn't add newline or exit
let generatePrintFloatNoNewline (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 48 bytes on stack for buffer
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 48us)

        // Save D0 at [SP+32]
        ARM64.STR_fp (ARM64.D0, ARM64.SP, 32s)

        // Check if negative using D0's sign bit
        ARM64.MOVZ (ARM64.X6, 0us, 0)  // X6 = 0 (assume positive)
        ARM64.FMOV_to_gp (ARM64.X0, ARM64.D0)  // Get bit pattern
        ARM64.TBNZ (ARM64.X0, 63, 2)  // If sign bit set, set X6 = 1
        ARM64.B (2)  // Skip setting X6
        ARM64.MOVZ (ARM64.X6, 1us, 0)  // X6 = 1 (negative)

        // Setup: X1 = buffer pointer (start at end)
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 31us)

        // Extract integer part
        ARM64.FCVTZS (ARM64.X0, ARM64.D0)
        ARM64.TBNZ (ARM64.X0, 63, 3)
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)
        ARM64.B (2)
        ARM64.NEG (ARM64.X2, ARM64.X0)

        // Check if integer part is zero
        ARM64.CBZ_offset (ARM64.X2, 55)  // Branch to print_zero_int (instruction 69)

        // convert_int_loop
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)
        ARM64.CBZ_offset (ARM64.X2, 2)
        ARM64.B (-8)

        // store_minus_if_needed
        ARM64.CBZ_offset (ARM64.X6, 4)
        ARM64.MOVZ (ARM64.X3, 45us, 0)
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // print_integer_part
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 32us)
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)
        ARM64.STR (ARM64.X6, ARM64.SP, 40s)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // print_decimal_point
        ARM64.MOVZ (ARM64.X3, 46us, 0)
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)
        ARM64.MOVZ (ARM64.X2, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // Extract and print fractional part (1 or 2 decimal digits)
        ARM64.LDR_fp (ARM64.D0, ARM64.SP, 32s)
        ARM64.FCVTZS (ARM64.X0, ARM64.D0)
        ARM64.SCVTF (ARM64.D1, ARM64.X0)
        ARM64.FSUB (ARM64.D0, ARM64.D0, ARM64.D1)
        ARM64.MOVZ (ARM64.X0, 100us, 0)
        ARM64.SCVTF (ARM64.D1, ARM64.X0)
        ARM64.FMUL (ARM64.D0, ARM64.D0, ARM64.D1)
        ARM64.FCVTZS (ARM64.X7, ARM64.D0)

        // Take absolute value of X7
        ARM64.TBNZ (ARM64.X7, 63, 2)
        ARM64.B (2)
        ARM64.NEG (ARM64.X7, ARM64.X7)

        // Extract digits
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.UDIV (ARM64.X4, ARM64.X7, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X7)
        ARM64.ADD_imm (ARM64.X4, ARM64.X4, 48us)
        ARM64.STRB (ARM64.X4, ARM64.SP, 1)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.SP, 2)

        // Print one or two digits (trim trailing zero)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 1us)
        ARM64.SUBS_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.CSET (ARM64.X2, ARM64.NE)
        ARM64.ADD_imm (ARM64.X2, ARM64.X2, 1us)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // Cleanup (no newline, no exit)
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 48us)
        ARM64.B (5)  // Skip past print_zero_int (4 instructions + 1 to land after)

        // print_zero_int (instruction 69)
        ARM64.MOVZ (ARM64.X2, 48us, 0)
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.B (-48)  // Branch back to store_minus_if_needed (instruction 24)
    ]

/// Generate ARM64 instructions to print heap string WITHOUT newline
/// Expects: X9 = data address, X10 = length
/// For use in tuple/list element printing
let generatePrintStringNoNewline (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Write string to stdout
        ARM64.MOVZ (ARM64.X0, 1us, 0)        // fd = stdout
        ARM64.MOV_reg (ARM64.X1, ARM64.X9)   // buffer = X9
        ARM64.MOV_reg (ARM64.X2, ARM64.X10)  // length = X10
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
    ]

/// Generate ARM64 instructions to print a sequence of literal characters
/// Used for printing delimiters like "(", ")", "[", "]", ", " etc.
///
/// Algorithm:
/// 1. Allocate aligned stack buffer
/// 2. Store each byte on stack
/// 3. Write to stdout via syscall
/// 4. Deallocate stack
///
/// No newline is added - caller controls newlines
let generatePrintChars (target: ARM64.TargetConfig) (chars: byte list) : ARM64.Instr list =
    if List.isEmpty chars then [] else
    let syscalls = ARM64.targetSyscalls target
    let len = List.length chars
    // Stack allocation must be 16-byte aligned
    let stackSize = max 16 ((len + 15) / 16 * 16)
    [
        // Allocate stack buffer
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, uint16 stackSize)
    ]
    @ (chars |> List.mapi (fun i b ->
        [
            ARM64.MOVZ (ARM64.X3, uint16 b, 0)
            ARM64.STRB (ARM64.X3, ARM64.SP, i)
        ]) |> List.concat)
    @ [
        // Write to stdout
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)          // buffer
    ]
    @ generateLoadNonNegativeIntImmediate ARM64.X2 len
    @ [
        ARM64.MOVZ (ARM64.X0, 1us, 0)               // stdout = 1
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        // Deallocate stack
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, uint16 stackSize)
    ]

/// Generate ARM64 instructions to print literal characters to stderr
let generatePrintCharsToStderr (target: ARM64.TargetConfig) (chars: byte list) : ARM64.Instr list =
    if List.isEmpty chars then [] else
    let syscalls = ARM64.targetSyscalls target
    let len = List.length chars
    let stackSize = max 16 ((len + 15) / 16 * 16)
    [
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, uint16 stackSize)
    ]
    @ (chars |> List.mapi (fun i b ->
        [
            ARM64.MOVZ (ARM64.X3, uint16 b, 0)
            ARM64.STRB (ARM64.X3, ARM64.SP, i)
        ]) |> List.concat)
    @ [
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)
    ]
    @ generateLoadNonNegativeIntImmediate ARM64.X2 len
    @ [
        ARM64.MOVZ (ARM64.X0, 2us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, uint16 stackSize)
    ]

/// Generate ARM64 instructions for the interpreter-compatible ephemeral Blob
/// rendering. Blob contents and process-local identity stay opaque.
let generatePrintBlob (target: ARM64.TargetConfig) : ARM64.Instr list =
    generatePrintChars
        target
        [ byte '<'
          byte 'B'
          byte 'l'
          byte 'o'
          byte 'b'
          byte ':'
          byte ' '
          byte 'e'
          byte 'p'
          byte 'h'
          byte 'e'
          byte 'm'
          byte 'e'
          byte 'r'
          byte 'a'
          byte 'l'
          byte '>'
          10uy ]

/// Generate ARM64 instructions to perform write syscall only
///
/// Assumes caller has set up:
/// - X0 = file descriptor (usually 1 for stdout)
/// - X1 = buffer pointer
/// - X2 = length
///
/// Does NOT print newline or exit - caller handles those if needed
let generateWriteSyscall (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
    ]
