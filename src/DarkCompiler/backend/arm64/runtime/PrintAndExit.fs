// PrintAndExit.fs - Generate ARM64 output and terminating entry helpers.

module ARM64PrintAndExit

open ARM64RuntimeImmediates

/// Generate ARM64 instructions to print int64 in X0 to stdout with newline
/// Then exit with code 0
///
/// Algorithm:
/// 1. Allocate 32-byte stack buffer
/// 2. Convert X0 (int64) to ASCII decimal string (work backwards from end)
/// 3. Handle negative numbers (negate value, prepend '-')
/// 4. Handle special case: zero
/// 5. Write buffer to stdout via write syscall
/// 6. Exit with code 0 via exit syscall
///
/// Register usage:
/// - X0: Input value, then stdout fd, then exit code
/// - X1: Buffer pointer (works backwards from end)
/// - X2: Working value / length / temp
/// - X3: Divisor (10) / digit / temp
/// - X4: Quotient from division
/// - X5: Remainder (digit to store)
/// - X6: Negative flag (0 = positive, 1 = negative)
/// - X16: Syscall number
///
/// Uses platform-specific syscall numbers from Platform module
let generatePrintInt64 (target: ARM64.TargetConfig) : ARM64.Instr list =
    // Platform detection for runtime support; unsupported platforms crash explicitly.
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 32 bytes on stack for buffer (plenty for 64-bit number + sign + newline)
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

        // Setup: X1 = buffer pointer (start at end, work backwards)
        // X2 = value to print (from X0)
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 31us)  // X1 = SP + 31 (end of buffer)
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)  // X2 = value to convert

        // Store newline at end of buffer
        ARM64.MOVZ (ARM64.X3, 10us, 0)  // '\n' = 10
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)  // Move pointer back

        // Instruction 6-7: Check if negative and set flag in X6
        ARM64.MOVZ (ARM64.X6, 0us, 0)  // 6: X6 = 0 (assume positive)
        ARM64.TBNZ (ARM64.X2, 63, 29)  // 7: If negative, branch forward +29 to inst 36 (handle_negative)

        // Instruction 8: Check if zero (branch forward +24 to print_zero at inst 32)
        ARM64.CBZ_offset (ARM64.X2, 24)

        // Instruction 9-17: convert_loop - Extract digits by dividing by 10
        ARM64.MOVZ (ARM64.X3, 10us, 0)  // 9: divisor = 10
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)  // 10: X4 = value / 10
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)  // 11: X5 = value % 10
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)  // 12: Convert to ASCII
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)  // 13: Store digit
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)  // 14: Move pointer back
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)  // 15: value = value / 10
        ARM64.CBZ_offset (ARM64.X2, 2)  // 16: If zero, skip (+2) to store_minus_if_needed at inst 18
        ARM64.B (-8)  // 17: Loop back (-8) to inst 9 (convert_loop start)

        // Instruction 18-21: store_minus_if_needed - If X6=1 (was negative), store '-'
        ARM64.CBZ_offset (ARM64.X6, 4)  // 18: If X6=0 (positive), skip (+4) to write_output at inst 22
        ARM64.MOVZ (ARM64.X3, 45us, 0)  // 19: '-' = ASCII 45
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)  // 20: Store '-' at X1
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)  // 21: Move pointer back

        // Instruction 22-31: write_output - Write buffer to stdout
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)  // 22: X1 was one past first char
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 32us)  // 23: End of buffer
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)  // 24: length = end - start
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // 25: stdout = 1
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)  // 26: write syscall (X16 macOS, X8 Linux)
        ARM64.SVC syscalls.SvcImmediate  // 27: call write (platform-specific)
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)  // 28: Deallocate stack
        ARM64.MOVZ (ARM64.X0, 0us, 0)  // 29: exit code = 0
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Exit, 0)  // 30: exit syscall (X16 macOS, X8 Linux)
        ARM64.SVC syscalls.SvcImmediate  // 31: call exit (platform-specific)

        // Instruction 32-35: print_zero - Special case for value 0
        ARM64.MOVZ (ARM64.X2, 48us, 0)  // 32: '0' = 48
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)  // 33: Store '0'
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)  // 34: Move pointer back
        ARM64.B (-13)  // 35: Branch back (-13) to inst 22 (write_output)

        // Instruction 36-38: handle_negative - Negate value and set flag
        ARM64.NEG (ARM64.X2, ARM64.X2)  // 36: X2 = -X2 (get absolute value)
        ARM64.MOVZ (ARM64.X6, 1us, 0)  // 37: X6 = 1 (negative flag)
        ARM64.B (-30)  // 38: Branch back (-30) to inst 8 (CBZ zero check)
    ]

/// Generate ARM64 instructions to print boolean in X0 to stdout with newline
/// Then exit with code 0
///
/// Algorithm:
/// 1. Check if X0 == 0 (false) or != 0 (true)
/// 2. Store "true\n" or "false\n" on stack
/// 3. Write to stdout via write syscall
/// 4. Exit with code 0
///
/// Register usage:
/// - X0: Input boolean value (0=false, non-zero=true)
/// - X1: Buffer pointer
/// - X2: Length
/// - X3: Temp for storing chars
/// - X16/X8: Syscall number
let generatePrintBool (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 16 bytes on stack for buffer (16-byte aligned)
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 16us)

        // Check if value is false (X0 == 0)
        ARM64.CBZ_offset (ARM64.X0, 17)  // If false, branch to print_false (+17 instructions)

        // print_true: Store "true\n" on stack (5 bytes)
        // 't' = 116, 'r' = 114, 'u' = 117, 'e' = 101, '\n' = 10
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

        // Write and exit
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // buffer
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // stdout
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate
        ARM64.B (16)  // Jump to cleanup_and_exit (+16 instructions)

        // print_false: Store "false\n" on stack (6 bytes)
        // 'f' = 102, 'a' = 97, 'l' = 108, 's' = 115, 'e' = 101, '\n' = 10
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
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // buffer
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // stdout
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // cleanup_and_exit:
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)  // Deallocate stack
        ARM64.MOVZ (ARM64.X0, 0us, 0)  // exit code = 0
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Exit, 0)
        ARM64.SVC syscalls.SvcImmediate
    ]

/// Generate ARM64 instructions to print string to stdout with newline, then exit
///
/// Assumes:
/// - X0 = string address (set up by caller via ADRP + ADD_label)
/// - stringLen = length of string to print (not including any newline)
///
/// Algorithm:
/// 1. Save string address
/// 2. Print string via write syscall
/// 3. Print newline via write syscall
/// 4. Exit with code 0
///
/// Register usage:
/// - X0: string address, then stdout fd
/// - X1: buffer pointer
/// - X2: length
/// - X16/X8: Syscall number (platform-specific)
let generatePrintString (target: ARM64.TargetConfig) (stringLen: int) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // X0 points to [refcount][length][data].
        ARM64.ADD_imm (ARM64.X1, ARM64.X0, 16us)
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // X0 = stdout fd (1)

        // Load string length into X2 and call write syscall
    ] @
    generateLoadNonNegativeIntImmediate ARM64.X2 stringLen @
    [
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // Now print newline: allocate 1 byte on stack
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 16us)  // 16-byte aligned
        ARM64.MOVZ (ARM64.X3, 10us, 0)  // '\n' = 10
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)  // Store newline at SP

        // Write newline
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // stdout
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // buffer = SP
        ARM64.MOVZ (ARM64.X2, 1us, 0)  // length = 1
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // Cleanup stack
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 16us)

        // Exit with code 0
        ARM64.MOVZ (ARM64.X0, 0us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Exit, 0)
        ARM64.SVC syscalls.SvcImmediate
    ]

/// Generate ARM64 instructions to print float in D0 to stdout with newline
/// Then exit with code 0
///
/// Algorithm:
/// 1. Extract integer part: FCVTZS X0, D0
/// 2. Check if negative and handle sign
/// 3. Print integer part (absolute value)
/// 4. Print decimal point '.'
/// 5. Extract fractional part: frac = abs(D0) - floor(abs(D0))
/// 6. Multiply by 10^2 to get 2 decimal digits
/// 7. Print fractional digits, trimming a trailing zero
/// 8. Print newline and exit
///
/// Register usage:
/// - D0: Input float value
/// - D1-D3: Working FP registers
/// - X0-X6: Working integer registers (same as PrintInt64)
/// - X7: Loop counter for fractional digits
/// - X16/X8: Syscall number (platform-specific)
let generatePrintFloat (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        // Allocate 48 bytes on stack for buffer (room for sign, digits, decimal, digits, newline)
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 48us)

        // Save D0 in case we need the original value
        // Store it on stack at [SP+32] (8 bytes for double)
        ARM64.STR_fp (ARM64.D0, ARM64.SP, 32s)

        // Check if negative using D0's sign bit (before FCVTZS, which loses sign for -0.x values)
        ARM64.MOVZ (ARM64.X6, 0us, 0)  // X6 = 0 (assume positive)
        ARM64.FMOV_to_gp (ARM64.X0, ARM64.D0)  // Get bit pattern of D0
        ARM64.TBNZ (ARM64.X0, 63, 2)  // If sign bit set, set X6 = 1
        ARM64.B (2)  // Skip setting X6
        ARM64.MOVZ (ARM64.X6, 1us, 0)  // X6 = 1 (negative)

        // Setup: X1 = buffer pointer (start at end, work backwards)
        // Use SP+31 as the end of buffer (we'll build digits backwards from here)
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 31us)  // X1 = SP + 31 (end of buffer)

        // Extract integer part using FCVTZS (truncate toward zero)
        ARM64.FCVTZS (ARM64.X0, ARM64.D0)  // X0 = integer part (may be negative)
        // Make X2 = absolute value of X0
        ARM64.TBNZ (ARM64.X0, 63, 3)  // If negative, skip the MOV and go to NEG
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)  // X2 = X0 (positive)
        ARM64.B (2)  // Skip NEG
        ARM64.NEG (ARM64.X2, ARM64.X0)  // X2 = -X0 (make positive)

        // === Print integer part (positive value in X2) ===
        // Label: after_sign_check
        // Instruction offset: 14

        // Check if integer part is zero (branch to print_zero_int)
        ARM64.CBZ_offset (ARM64.X2, 64)  // If zero, branch to print_zero_int (inst 77)

        // convert_int_loop - Extract digits by dividing by 10
        ARM64.MOVZ (ARM64.X3, 10us, 0)  // divisor = 10
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)  // X4 = value / 10
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)  // X5 = value % 10
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)  // Convert to ASCII
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)  // Store digit
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)  // Move pointer back
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)  // value = value / 10
        ARM64.CBZ_offset (ARM64.X2, 2)  // If zero, done with integer
        ARM64.B (-8)  // Loop back

        // store_minus_if_needed - If X6=1 (was negative), store '-'
        ARM64.CBZ_offset (ARM64.X6, 4)  // If X6=0 (positive), skip
        ARM64.MOVZ (ARM64.X3, 45us, 0)  // '-' = ASCII 45
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)  // Store '-'
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)  // Move pointer back

        // print_integer_part - Write integer digits to stdout
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)  // X1 was one past first char
        ARM64.ADD_imm (ARM64.X2, ARM64.SP, 32us)  // End of int buffer
        ARM64.SUB_reg (ARM64.X2, ARM64.X2, ARM64.X1)  // length = end - start
        // Save X6 to stack at [SP+40] before syscalls (syscalls may clobber X0-X7)
        ARM64.STR (ARM64.X6, ARM64.SP, 40s)
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // stdout = 1
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // print_decimal_point - Print '.'
        ARM64.MOVZ (ARM64.X3, 46us, 0)  // '.' = 46
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)  // Store at [SP]
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // stdout
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // buffer = SP
        ARM64.MOVZ (ARM64.X2, 1us, 0)  // length = 1
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // === Extract and print fractional part (1 or 2 decimal digits) ===
        // Reload original float value
        ARM64.LDR_fp (ARM64.D0, ARM64.SP, 32s)  // Reload original value

        // Get integer part again and compute fractional part
        ARM64.FCVTZS (ARM64.X0, ARM64.D0)  // X0 = integer part (signed)
        ARM64.SCVTF (ARM64.D1, ARM64.X0)  // D1 = (double)integer (signed)
        ARM64.FSUB (ARM64.D0, ARM64.D0, ARM64.D1)  // D0 = fractional part (signed, same sign as original)

        // Multiply fractional part by 100 to get 2 digits
        ARM64.MOVZ (ARM64.X0, 100us, 0)  // 100
        ARM64.SCVTF (ARM64.D1, ARM64.X0)  // D1 = 100.0
        ARM64.FMUL (ARM64.D0, ARM64.D0, ARM64.D1)  // D0 = frac * 100 (might be negative)
        ARM64.FCVTZS (ARM64.X7, ARM64.D0)  // X7 = fractional digits as integer (might be negative)

        // Take absolute value of X7 if negative
        ARM64.TBNZ (ARM64.X7, 63, 2)  // If sign bit set, skip the B
        ARM64.B (2)  // Skip NEG if positive
        ARM64.NEG (ARM64.X7, ARM64.X7)  // Negate to make positive

        // Extract both digits BEFORE any syscalls (syscalls clobber X0-X7)
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.UDIV (ARM64.X4, ARM64.X7, ARM64.X3)  // X4 = X7 / 10 (tens digit)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X7)  // X5 = X7 % 10 (ones digit)
        // Store digits at [SP+1] and [SP+2] for later
        ARM64.ADD_imm (ARM64.X4, ARM64.X4, 48us)  // Convert tens to ASCII
        ARM64.STRB (ARM64.X4, ARM64.SP, 1)  // Store tens digit at [SP+1]
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)  // Convert ones to ASCII
        ARM64.STRB (ARM64.X5, ARM64.SP, 2)  // Store ones digit at [SP+2]

        // Print one or two digits in one syscall (trim trailing zero)
        ARM64.MOVZ (ARM64.X0, 1us, 0)  // stdout
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 1us)  // buffer at [SP+1]
        ARM64.SUBS_imm (ARM64.X5, ARM64.X5, 48us)  // Set flags if ones digit was '0'
        ARM64.CSET (ARM64.X2, ARM64.NE)  // X2 = 1 when ones digit != 0
        ARM64.ADD_imm (ARM64.X2, ARM64.X2, 1us)  // length = 1 or 2
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // print_newline_and_exit
        ARM64.MOVZ (ARM64.X3, 10us, 0)  // '\n'
        ARM64.STRB (ARM64.X3, ARM64.SP, 0)
        ARM64.MOVZ (ARM64.X0, 1us, 0)
        ARM64.MOV_reg (ARM64.X1, ARM64.SP)
        ARM64.MOVZ (ARM64.X2, 1us, 0)
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64.SVC syscalls.SvcImmediate

        // Cleanup and exit
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 48us)  // Deallocate stack
        ARM64.MOVZ (ARM64.X0, 0us, 0)  // exit code = 0
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Exit, 0)
        ARM64.SVC syscalls.SvcImmediate

        // print_zero_int - Integer part is zero, just store '0' (instruction 75)
        ARM64.MOVZ (ARM64.X2, 48us, 0)  // '0' = 48
        ARM64.STRB (ARM64.X2, ARM64.X1, 0)  // Store '0'
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)  // Move pointer back
        ARM64.B (-57)  // Branch back to store_minus_if_needed (inst 23)
    ]

/// Generate ARM64 instructions to exit with code 0
let generateExit (target: ARM64.TargetConfig) : ARM64.Instr list =
    let syscalls = ARM64.targetSyscalls target
    [
        ARM64.MOVZ (ARM64.X0, 0us, 0)  // exit code = 0
        ARM64.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Exit, 0)
        ARM64.SVC syscalls.SvcImmediate
    ]
