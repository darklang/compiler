// FloatFormatting.fs - Generate native floating-point formatting.

module ARM64FloatFormatting

/// Generate ARM64 instructions to convert a float to a heap string
/// destReg: destination register for the heap string pointer
/// valueReg: FP register containing the float value
/// The string format is "-123.45" (integer part + "." + 1-2 decimal digits)
/// Heap string layout: [8 bytes refcount][8 bytes length][N bytes data]
let generateFloatToString (destReg: ARM64.Reg) (valueReg: ARM64.FReg) : ARM64.Instr list =
    // This function:
    // 1. Converts float to characters in a buffer on stack
    // 2. Allocates heap string using bump allocator (X28)
    // 3. Copies characters to heap and sets length
    // 4. Returns heap pointer in destReg
    //
    // Uses: X0-X7 (scratch), D0-D1 (float), X19 (heap ptr), X20 (sign flag), X21 (saved length)
    // Stack layout (64 bytes):
    //   [SP+0..31]: character buffer (32 bytes)
    //   [SP+32..39]: saved D0
    //   [SP+40..47]: saved X19
    //   [SP+48..55]: saved X20
    //   [SP+56..63]: saved X21
    [
        // Save callee-saved registers and allocate stack
        ARM64.SUB_imm (ARM64.SP, ARM64.SP, 64us)
        ARM64.STR (ARM64.X19, ARM64.SP, 40s)
        ARM64.STR (ARM64.X20, ARM64.SP, 48s)
        ARM64.STR (ARM64.X21, ARM64.SP, 56s)

        // Move input to D0
        ARM64.FMOV_reg (ARM64.D0, valueReg)
        ARM64.STR_fp (ARM64.D0, ARM64.SP, 32s)  // Save original value

        // Check if negative using sign bit
        ARM64.MOVZ (ARM64.X20, 0us, 0)  // X20 = 0 (assume positive)
        ARM64.FMOV_to_gp (ARM64.X0, ARM64.D0)  // Get bit pattern
        ARM64.TBNZ (ARM64.X0, 63, 2)  // If sign bit set, X20 = 1
        ARM64.B (2)  // Skip setting X20
        ARM64.MOVZ (ARM64.X20, 1us, 0)  // X20 = 1 (negative)

        // X1 = buffer pointer (start at end of buffer, work backwards)
        ARM64.ADD_imm (ARM64.X1, ARM64.SP, 31us)  // X1 = SP + 31 (end of buffer)

        // Extract integer part
        ARM64.FCVTZS (ARM64.X0, ARM64.D0)  // X0 = integer part (signed)
        // Make X2 = absolute value of X0
        ARM64.TBNZ (ARM64.X0, 63, 3)  // If negative, skip to NEG
        ARM64.MOV_reg (ARM64.X2, ARM64.X0)  // X2 = X0 (positive)
        ARM64.B (2)  // Skip NEG
        ARM64.NEG (ARM64.X2, ARM64.X0)  // X2 = -X0 (make positive)

        // === Build fractional part first (1-2 digits) ===
        ARM64.LDR_fp (ARM64.D0, ARM64.SP, 32s)
        ARM64.FCVTZS (ARM64.X0, ARM64.D0)
        ARM64.SCVTF (ARM64.D1, ARM64.X0)
        ARM64.FSUB (ARM64.D0, ARM64.D0, ARM64.D1)  // D0 = fractional part

        // Multiply by 100 to get 2 digits
        ARM64.MOVZ (ARM64.X0, 100us, 0)
        ARM64.SCVTF (ARM64.D1, ARM64.X0)
        ARM64.FMUL (ARM64.D0, ARM64.D0, ARM64.D1)
        ARM64.FCVTZS (ARM64.X7, ARM64.D0)  // X7 = fractional digits (may be negative)

        // Take absolute value of X7
        ARM64.TBNZ (ARM64.X7, 63, 2)
        ARM64.B (2)
        ARM64.NEG (ARM64.X7, ARM64.X7)

        // Extract both fractional digits
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.UDIV (ARM64.X4, ARM64.X7, ARM64.X3)  // X4 = tens digit
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X7)  // X5 = ones digit

        // Store fractional digits (backwards): ones first, then tens
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)  // ASCII
        ARM64.MOV_reg (ARM64.X7, ARM64.X5)  // Keep ones digit for length trim
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X4, ARM64.X4, 48us)  // ASCII
        ARM64.STRB (ARM64.X4, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // Store decimal point
        ARM64.MOVZ (ARM64.X3, 46us, 0)  // '.'
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // === Build integer part ===
        // X2 still has absolute value of integer part
        ARM64.CBZ_offset (ARM64.X2, 11)  // If zero, print just "0"

        // Convert integer digits (loop)
        ARM64.MOVZ (ARM64.X3, 10us, 0)
        ARM64.UDIV (ARM64.X4, ARM64.X2, ARM64.X3)
        ARM64.MSUB (ARM64.X5, ARM64.X4, ARM64.X3, ARM64.X2)
        ARM64.ADD_imm (ARM64.X5, ARM64.X5, 48us)
        ARM64.STRB (ARM64.X5, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.MOV_reg (ARM64.X2, ARM64.X4)
        ARM64.CBZ_offset (ARM64.X2, 2)
        ARM64.B (-8)
        ARM64.B (4)  // Skip zero case

        // Zero case: just store '0'
        ARM64.MOVZ (ARM64.X3, 48us, 0)  // '0'
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // Add minus sign if negative
        ARM64.CBZ_offset (ARM64.X20, 4)
        ARM64.MOVZ (ARM64.X3, 45us, 0)  // '-'
        ARM64.STRB (ARM64.X3, ARM64.X1, 0)
        ARM64.SUB_imm (ARM64.X1, ARM64.X1, 1us)

        // X1 now points one before first char
        // Adjust X1 to point to first char, calculate length (trim trailing zero)
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)  // X1 = start of string
        ARM64.ADD_imm (ARM64.X3, ARM64.SP, 32us)  // X3 = end+1 of buffer (SP+32)
        ARM64.SUBS_imm (ARM64.X7, ARM64.X7, 48us)  // Set flags if ones digit was '0'
        ARM64.CSET (ARM64.X4, ARM64.EQ)  // X4 = 1 when ones digit == 0
        ARM64.SUB_reg (ARM64.X3, ARM64.X3, ARM64.X4)  // Drop ones digit if needed
        ARM64.SUB_reg (ARM64.X21, ARM64.X3, ARM64.X1)  // X21 = length (save for later)

        // Allocate heap string using bump allocator (X28)
        // Total size = 8 (refcount) + 8 (length) + string data, aligned to 8
        ARM64.ADD_imm (ARM64.X0, ARM64.X21, 23us)  // X0 = len + 16 + 7 (for alignment)
        ARM64.AND_imm (ARM64.X0, ARM64.X0, 0xFFFFFFFFFFFFFFF8UL)  // Round down to 8-byte alignment
        ARM64.MOV_reg (ARM64.X19, ARM64.X28)  // X19 = current heap pointer (our allocation)
        ARM64.ADD_reg (ARM64.X28, ARM64.X28, ARM64.X0)  // Bump allocator

        ARM64.MOVZ (ARM64.X4, 1us, 0)  // X4 = 1
        ARM64.STR (ARM64.X4, ARM64.X19, 0s)
        ARM64.STR (ARM64.X21, ARM64.X19, 8s)

        // Copy string from stack buffer to heap
        // X1 = source (stack buffer start), X19+16 = dest, X21 = length
        ARM64.ADD_imm (ARM64.X3, ARM64.X19, 16us)  // X3 = dest
        ARM64.MOV_reg (ARM64.X2, ARM64.X21)  // X2 = length (counter)

        // Copy loop (byte by byte)
        ARM64.CBZ_offset (ARM64.X2, 6)  // If length 0, skip
        ARM64.LDRB_imm (ARM64.X4, ARM64.X1, 0)
        ARM64.STRB (ARM64.X4, ARM64.X3, 0)
        ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
        ARM64.ADD_imm (ARM64.X3, ARM64.X3, 1us)
        ARM64.SUB_imm (ARM64.X2, ARM64.X2, 1us)
        ARM64.CBNZ_offset (ARM64.X2, -5)

        // Move result to dest register
        ARM64.MOV_reg (destReg, ARM64.X19)

        // Restore callee-saved registers
        ARM64.LDR (ARM64.X19, ARM64.SP, 40s)
        ARM64.LDR (ARM64.X20, ARM64.SP, 48s)
        ARM64.LDR (ARM64.X21, ARM64.SP, 56s)
        ARM64.ADD_imm (ARM64.SP, ARM64.SP, 64us)
    ]
