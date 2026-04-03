// 6_CodeGen_X86_64.fs - x86-64 Code Generation (Pass 6)
//
// Transforms LIR into x86-64 instructions.
//
// Maps LIR physical registers to x86-64 registers using ArchConfig.x86_64:
//   X0→RAX, X1→RDI, X2→RSI, X3→RDX, X4→RCX, X5→R8, X6→R9,
//   X7→R10, X8→R11, X9→scratch, X10→scratch,
//   X19→RBX, X20→R12, X21→R13, X22→R14, X23→R15
//
// Key x86_64 differences from ARM64:
// - CISC: most instructions modify destination in-place (dest = dest OP src)
// - Division uses fixed registers (RDX:RAX)
// - SYSCALL instruction instead of SVC; syscall number in RAX
// - No link register; CALL pushes return address on stack
// - 6 integer arg registers (vs ARM64's 8)

module CodeGen_X86_64

let private syscalls = Platform.linuxX86_64SyscallNumbers

/// Map LIR.PhysReg to x86-64 register
let lirRegToX86 (reg: LIR.PhysReg) : X86_64.Reg =
    match reg with
    | LIR.X0  -> X86_64.RAX   // Return value
    | LIR.X1  -> X86_64.RDI   // Arg 1
    | LIR.X2  -> X86_64.RSI   // Arg 2
    | LIR.X3  -> X86_64.RDX   // Arg 3
    | LIR.X4  -> X86_64.RCX   // Arg 4
    | LIR.X5  -> X86_64.R8    // Arg 5
    | LIR.X6  -> X86_64.R9    // Arg 6
    | LIR.X7  -> X86_64.R10   // Caller-saved
    | LIR.X8  -> X86_64.R11   // Scratch
    | LIR.X9  -> X86_64.R11   // Scratch (shared)
    | LIR.X10 -> X86_64.R11
    | LIR.X11 -> X86_64.R11
    | LIR.X12 -> X86_64.R11
    | LIR.X13 -> X86_64.R11
    | LIR.X14 -> X86_64.R11
    | LIR.X15 -> X86_64.R11
    | LIR.X16 -> X86_64.R11
    | LIR.X17 -> X86_64.R11
    | LIR.X19 -> X86_64.RBX   // Callee-saved 1
    | LIR.X20 -> X86_64.R12   // Callee-saved 2
    | LIR.X21 -> X86_64.R13   // Callee-saved 3
    | LIR.X22 -> X86_64.R14   // Callee-saved 4
    | LIR.X23 -> X86_64.R15   // Callee-saved 5
    | LIR.X24 -> X86_64.R15   // Overflow (shouldn't be allocated on x86_64)
    | LIR.X25 -> X86_64.R15
    | LIR.X26 -> X86_64.R15
    | LIR.X27 -> X86_64.RBP   // Reserved (free list / heap)
    | LIR.X29 -> X86_64.RBP   // Frame pointer
    | LIR.X30 -> X86_64.RAX   // Link register (not applicable on x86_64)
    | LIR.SP  -> X86_64.RSP

/// Map LIR.FReg to x86-64 XMM register
let lirFRegToX86 (freg: LIR.PhysFPReg) : X86_64.FReg =
    match freg with
    | LIR.D0  -> X86_64.XMM0  | LIR.D1  -> X86_64.XMM1
    | LIR.D2  -> X86_64.XMM2  | LIR.D3  -> X86_64.XMM3
    | LIR.D4  -> X86_64.XMM4  | LIR.D5  -> X86_64.XMM5
    | LIR.D6  -> X86_64.XMM6  | LIR.D7  -> X86_64.XMM7
    | LIR.D8  -> X86_64.XMM8  | LIR.D9  -> X86_64.XMM9
    | LIR.D10 -> X86_64.XMM10 | LIR.D11 -> X86_64.XMM11
    | LIR.D12 -> X86_64.XMM12 | LIR.D13 -> X86_64.XMM13
    | LIR.D14 -> X86_64.XMM14 | LIR.D15 -> X86_64.XMM15

/// Resolve a LIR.Reg (Physical or Virtual) to x86-64 register.
let resolveReg (reg: LIR.Reg) : Result<X86_64.Reg, string> =
    match reg with
    | LIR.Physical phys -> Ok (lirRegToX86 phys)
    | LIR.Virtual id -> Error $"Unresolved virtual register v{id} in x86-64 codegen"

/// Load a 64-bit immediate into a register.
let private loadImm64 (dest: X86_64.Reg) (value: int64) : X86_64.Instr list =
    if value = 0L then
        [X86_64.XOR_reg (dest, dest)]
    elif value >= int64 System.Int32.MinValue && value <= int64 System.Int32.MaxValue then
        [X86_64.MOV_imm32 (dest, int32 value)]
    else
        [X86_64.MOV_imm (dest, value)]

/// Scratch register for temporaries in codegen
let private scratch = X86_64.R11

/// Generate x86-64 write(fd, buf, len) syscall
let private genWriteSyscall : X86_64.Instr list =
    loadImm64 X86_64.RAX (int64 syscalls.Write) @ [X86_64.SYSCALL]

/// Generate x86-64 exit(code) syscall.
/// Exit code must already be in RDI.
let private genExitSyscall : X86_64.Instr list =
    loadImm64 X86_64.RAX (int64 syscalls.Exit) @ [X86_64.SYSCALL]

/// Mutable counter for generating unique labels within a compilation
let mutable private labelCounter = 0
let private freshLabel (prefix: string) : string =
    labelCounter <- labelCounter + 1
    $"__{prefix}_{labelCounter}"

/// Generate x86-64 instructions to print a signed 64-bit integer to stdout.
/// Value is in the given register. Includes newline. Does NOT exit.
///
/// Algorithm: itoa by repeated division by 10, writing digits backwards
/// into a stack buffer, then write(1, buf, len).
let private genPrintInt64 (srcReg: X86_64.Reg) (addNewline: bool) : X86_64.Instr list =
    let loopLabel = freshLabel "itoa_loop"
    let doneLabel = freshLabel "itoa_done"
    let zeroLabel = freshLabel "itoa_zero"
    let negLabel = freshLabel "itoa_neg"
    let writeLabel = freshLabel "itoa_write"
    let skipMinusLabel = freshLabel "itoa_skipminus"

    [
        // Save value, allocate 32-byte buffer on stack
        X86_64.SUB_imm (X86_64.RSP, 32)
        // RCX = write pointer (end of buffer, work backwards)
        X86_64.LEA (X86_64.RCX, X86_64.RSP, 31)
    ]
    @ (if addNewline then [
        // Store newline at end
        X86_64.MOV_imm32 (scratch, 10)
        X86_64.MOV_store_byte (X86_64.RCX, 0, scratch)
        X86_64.SUB_imm (X86_64.RCX, 1)
    ] else [])
    @ [
        // R8 = value to print; R9 = negative flag
        X86_64.MOV_reg (X86_64.R8, srcReg)
        X86_64.XOR_reg (X86_64.R9, X86_64.R9)  // R9 = 0 (positive)

        // Check if negative
        X86_64.TEST_reg (X86_64.R8, X86_64.R8)
        X86_64.Jcc (X86_64.LT, negLabel)

        // Check if zero
        X86_64.TEST_reg (X86_64.R8, X86_64.R8)
        X86_64.Jcc (X86_64.EQ, zeroLabel)

        // convert_loop: extract digits by dividing by 10
        X86_64.Label loopLabel
        X86_64.MOV_reg (X86_64.RAX, X86_64.R8)  // RAX = value
        X86_64.XOR_reg (X86_64.RDX, X86_64.RDX) // Clear RDX for unsigned div
        // But we made it positive, so use unsigned division
        X86_64.MOV_imm32 (X86_64.RSI, 10)
        X86_64.DIV X86_64.RSI  // RAX = quotient, RDX = remainder
        X86_64.ADD_imm (X86_64.RDX, 48)  // Convert remainder to ASCII
        X86_64.MOV_store_byte (X86_64.RCX, 0, X86_64.RDX)  // Store digit
        X86_64.SUB_imm (X86_64.RCX, 1)  // Move pointer back
        X86_64.MOV_reg (X86_64.R8, X86_64.RAX)  // value = quotient
        X86_64.TEST_reg (X86_64.R8, X86_64.R8)
        X86_64.Jcc (X86_64.NE, loopLabel)  // Loop if not zero

        // store_minus_if_needed
        X86_64.TEST_reg (X86_64.R9, X86_64.R9)
        X86_64.Jcc (X86_64.EQ, skipMinusLabel)
        X86_64.MOV_imm32 (scratch, 45)  // '-' = 45
        X86_64.MOV_store_byte (X86_64.RCX, 0, scratch)
        X86_64.SUB_imm (X86_64.RCX, 1)
        X86_64.Label skipMinusLabel

        // write_output
        X86_64.JMP writeLabel

        // print_zero: special case
        X86_64.Label zeroLabel
        X86_64.MOV_imm32 (scratch, 48)  // '0' = 48
        X86_64.MOV_store_byte (X86_64.RCX, 0, scratch)
        X86_64.SUB_imm (X86_64.RCX, 1)
        X86_64.JMP writeLabel

        // handle_negative: negate and set flag
        X86_64.Label negLabel
        X86_64.NEG X86_64.R8
        X86_64.MOV_imm32 (X86_64.R9, 1)  // negative flag
        X86_64.TEST_reg (X86_64.R8, X86_64.R8)
        X86_64.Jcc (X86_64.EQ, zeroLabel)
        X86_64.JMP loopLabel

        // write_output
        X86_64.Label writeLabel
        X86_64.ADD_imm (X86_64.RCX, 1)  // RCX was one past first char
        // length = (RSP + 32) - RCX
        X86_64.LEA (X86_64.RDX, X86_64.RSP, 32)
        X86_64.SUB_reg (X86_64.RDX, X86_64.RCX)  // RDX = length
        X86_64.MOV_imm32 (X86_64.RDI, 1)  // fd = stdout
        X86_64.MOV_reg (X86_64.RSI, X86_64.RCX)  // buf
    ]
    @ genWriteSyscall
    @ [
        X86_64.ADD_imm (X86_64.RSP, 32)  // Deallocate buffer
        X86_64.Label doneLabel
    ]

/// Generate PrintInt64 + exit(0)
let private genPrintInt64AndExit (srcReg: X86_64.Reg) : X86_64.Instr list =
    genPrintInt64 srcReg true
    @ loadImm64 X86_64.RDI 0L
    @ genExitSyscall

/// Generate PrintBool + exit(0)
let private genPrintBoolAndExit (srcReg: X86_64.Reg) : X86_64.Instr list =
    let trueLabel = freshLabel "bool_true"
    let writeLabel = freshLabel "bool_write"
    [
        X86_64.TEST_reg (srcReg, srcReg)
        X86_64.Jcc (X86_64.NE, trueLabel)
        // false\n = 6 bytes
        X86_64.SUB_imm (X86_64.RSP, 8)
    ]
    @ loadImm64 scratch 0x0A65736C6166L  // "false\n" in little-endian (6 bytes)
    @ [
        X86_64.MOV_store (X86_64.RSP, 0, scratch)
        X86_64.MOV_reg (X86_64.RSI, X86_64.RSP)
        X86_64.MOV_imm32 (X86_64.RDX, 6)
        X86_64.JMP writeLabel

        X86_64.Label trueLabel
        X86_64.SUB_imm (X86_64.RSP, 8)
    ]
    @ loadImm64 scratch 0x0A65757274L  // "true\n" in little-endian (5 bytes)
    @ [
        X86_64.MOV_store (X86_64.RSP, 0, scratch)
        X86_64.MOV_reg (X86_64.RSI, X86_64.RSP)
        X86_64.MOV_imm32 (X86_64.RDX, 5)

        X86_64.Label writeLabel
        X86_64.MOV_imm32 (X86_64.RDI, 1)  // fd = stdout
    ]
    @ genWriteSyscall
    @ [X86_64.ADD_imm (X86_64.RSP, 8)]
    @ loadImm64 X86_64.RDI 0L
    @ genExitSyscall

// ============================================================================
// LIR Instruction Translation
// ============================================================================

/// Translate a single LIR instruction to x86-64 instructions
let private translateInstr (instr: LIR.Instr) : Result<X86_64.Instr list, string> =
    match instr with

    | LIR.Mov (dest, src) ->
        resolveReg dest
        |> Result.bind (fun destReg ->
            match src with
            | LIR.Imm value ->
                Ok (loadImm64 destReg value)
            | LIR.Reg srcReg ->
                resolveReg srcReg
                |> Result.map (fun srcX86 ->
                    if destReg = srcX86 then []
                    else [X86_64.MOV_reg (destReg, srcX86)])
            | LIR.StackSlot offset ->
                Ok [X86_64.MOV_load (destReg, X86_64.RSP, int32 (offset * 8))]
            | _ ->
                Error $"Unsupported Mov source in x86-64 codegen: {src}")

    | LIR.Store (stackSlot, src) ->
        resolveReg src
        |> Result.map (fun srcReg ->
            [X86_64.MOV_store (X86_64.RSP, int32 (stackSlot * 8), srcReg)])

    | LIR.Add (dest, left, right) ->
        resolveReg dest
        |> Result.bind (fun destReg ->
            resolveReg left
            |> Result.bind (fun leftReg ->
                match right with
                | LIR.Imm value when value >= int64 System.Int32.MinValue && value <= int64 System.Int32.MaxValue ->
                    // x86_64: dest = left + imm32
                    let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                    Ok (setup @ [X86_64.ADD_imm (destReg, int32 value)])
                | LIR.Imm value ->
                    let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                    Ok (setup @ loadImm64 scratch value @ [X86_64.ADD_reg (destReg, scratch)])
                | LIR.Reg rightReg ->
                    resolveReg rightReg
                    |> Result.map (fun rightX86 ->
                        let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                        setup @ [X86_64.ADD_reg (destReg, rightX86)])
                | LIR.StackSlot offset ->
                    let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                    Ok (setup @ [X86_64.MOV_load (scratch, X86_64.RSP, int32 (offset * 8)); X86_64.ADD_reg (destReg, scratch)])
                | _ -> Error $"Unsupported Add right operand: {right}"))

    | LIR.Sub (dest, left, right) ->
        resolveReg dest
        |> Result.bind (fun destReg ->
            resolveReg left
            |> Result.bind (fun leftReg ->
                match right with
                | LIR.Imm value when value >= int64 System.Int32.MinValue && value <= int64 System.Int32.MaxValue ->
                    let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                    Ok (setup @ [X86_64.SUB_imm (destReg, int32 value)])
                | LIR.Imm value ->
                    let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                    Ok (setup @ loadImm64 scratch value @ [X86_64.SUB_reg (destReg, scratch)])
                | LIR.Reg rightReg ->
                    resolveReg rightReg
                    |> Result.map (fun rightX86 ->
                        let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                        setup @ [X86_64.SUB_reg (destReg, rightX86)])
                | _ -> Error $"Unsupported Sub right operand: {right}"))

    | LIR.Mul (dest, left, right) ->
        // x86_64 IMUL r64, r/m64 — dest = dest * src
        resolveReg dest
        |> Result.bind (fun destReg ->
            resolveReg left
            |> Result.bind (fun leftReg ->
                resolveReg right
                |> Result.map (fun rightReg ->
                    let setup = if destReg <> leftReg then [X86_64.MOV_reg (destReg, leftReg)] else []
                    setup @ [X86_64.IMUL_reg (destReg, rightReg)])))

    | LIR.Sdiv (dest, left, right) ->
        // x86_64 IDIV: RDX:RAX / src → RAX=quotient, RDX=remainder
        // Need to: MOV RAX, left; CQO; IDIV right; MOV dest, RAX
        resolveReg dest
        |> Result.bind (fun destReg ->
            resolveReg left
            |> Result.bind (fun leftReg ->
                resolveReg right
                |> Result.map (fun rightReg ->
                    let rightSrc =
                        if rightReg = X86_64.RAX || rightReg = X86_64.RDX then
                            // Divisor can't be in RAX or RDX (we clobber them)
                            [X86_64.MOV_reg (scratch, rightReg)]
                        else []
                    let divisor = if rightReg = X86_64.RAX || rightReg = X86_64.RDX then scratch else rightReg
                    (if leftReg <> X86_64.RAX then [X86_64.MOV_reg (X86_64.RAX, leftReg)] else [])
                    @ rightSrc
                    @ [X86_64.CQO; X86_64.IDIV divisor]
                    @ (if destReg <> X86_64.RAX then [X86_64.MOV_reg (destReg, X86_64.RAX)] else []))))

    | LIR.Msub (dest, mulLeft, mulRight, sub) ->
        // dest = sub - mulLeft * mulRight
        // No fused instruction on x86_64: IMUL tmp, mulLeft, mulRight; MOV dest, sub; SUB dest, tmp
        resolveReg dest
        |> Result.bind (fun destReg ->
            resolveReg mulLeft
            |> Result.bind (fun mlReg ->
                resolveReg mulRight
                |> Result.bind (fun mrReg ->
                    resolveReg sub
                    |> Result.map (fun subReg ->
                        [X86_64.MOV_reg (scratch, mlReg); X86_64.IMUL_reg (scratch, mrReg)]
                        @ (if destReg <> subReg then [X86_64.MOV_reg (destReg, subReg)] else [])
                        @ [X86_64.SUB_reg (destReg, scratch)]))))

    | LIR.Cmp (left, right) ->
        resolveReg left
        |> Result.bind (fun leftReg ->
            match right with
            | LIR.Imm value when value >= int64 System.Int32.MinValue && value <= int64 System.Int32.MaxValue ->
                Ok [X86_64.CMP_imm (leftReg, int32 value)]
            | LIR.Imm value ->
                Ok (loadImm64 scratch value @ [X86_64.CMP_reg (leftReg, scratch)])
            | LIR.Reg rightReg ->
                resolveReg rightReg
                |> Result.map (fun rightX86 -> [X86_64.CMP_reg (leftReg, rightX86)])
            | _ -> Error $"Unsupported Cmp right operand: {right}")

    | LIR.Cset (dest, cond) ->
        resolveReg dest
        |> Result.map (fun destReg ->
            let x86Cond =
                match cond with
                | LIR.EQ -> X86_64.EQ | LIR.NE -> X86_64.NE
                | LIR.LT -> X86_64.LT | LIR.GT -> X86_64.GT
                | LIR.LE -> X86_64.LE | LIR.GE -> X86_64.GE
            // SETcc sets low byte; MOVZX clears upper bits
            [X86_64.SETcc (x86Cond, destReg); X86_64.MOVZX_byte (destReg, destReg)])

    | LIR.And (dest, left, right) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg left |> Result.bind (fun l -> resolveReg right |> Result.map (fun r ->
            (if d <> l then [X86_64.MOV_reg (d, l)] else []) @ [X86_64.AND_reg (d, r)])))

    | LIR.And_imm (dest, src, imm) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            (if d <> s then [X86_64.MOV_reg (d, s)] else [])
            @ [X86_64.AND_imm (d, int32 imm)]))

    | LIR.Orr (dest, left, right) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg left |> Result.bind (fun l -> resolveReg right |> Result.map (fun r ->
            (if d <> l then [X86_64.MOV_reg (d, l)] else []) @ [X86_64.OR_reg (d, r)])))

    | LIR.Eor (dest, left, right) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg left |> Result.bind (fun l -> resolveReg right |> Result.map (fun r ->
            (if d <> l then [X86_64.MOV_reg (d, l)] else []) @ [X86_64.XOR_reg (d, r)])))

    | LIR.Lsl_imm (dest, src, shift) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            (if d <> s then [X86_64.MOV_reg (d, s)] else []) @ [X86_64.SHL_imm (d, shift)]))

    | LIR.Lsr_imm (dest, src, shift) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            (if d <> s then [X86_64.MOV_reg (d, s)] else []) @ [X86_64.SHR_imm (d, shift)]))

    | LIR.Mvn (dest, src) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            (if d <> s then [X86_64.MOV_reg (d, s)] else []) @ [X86_64.NOT d]))

    | LIR.Sxtb (dest, src) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            [X86_64.MOVSX_byte (d, s)]))

    | LIR.Sxth (dest, src) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            [X86_64.MOVSX_word (d, s)]))

    | LIR.Sxtw (dest, src) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            [X86_64.MOVSXD (d, s)]))

    | LIR.Uxtb (dest, src) ->
        resolveReg dest |> Result.bind (fun d -> resolveReg src |> Result.map (fun s ->
            [X86_64.MOVZX_byte (d, s)]))

    | LIR.Exit ->
        // RDI should already contain the exit code
        Ok genExitSyscall

    | LIR.PrintChars bytes ->
        // Write literal bytes to stdout: write(1, buf, len)
        // Push bytes onto stack, write from RSP, then pop
        let len = List.length bytes
        let padded = ((len + 7) / 8) * 8  // 8-byte aligned
        let paddedBytes = bytes @ List.replicate (padded - len) 0uy
        // Push bytes in reverse 8-byte chunks
        let pushInstrs =
            paddedBytes
            |> List.chunkBySize 8
            |> List.rev
            |> List.collect (fun chunk ->
                let value =
                    chunk |> List.mapi (fun i b -> int64 b <<< (i * 8)) |> List.fold (|||) 0L
                loadImm64 scratch value @ [X86_64.PUSH scratch])
        let writeInstrs =
            [X86_64.MOV_imm32 (X86_64.RDI, 1)]  // fd = stdout
            @ [X86_64.MOV_reg (X86_64.RSI, X86_64.RSP)]  // buf = stack
            @ loadImm64 X86_64.RDX (int64 len)  // len
            @ genWriteSyscall
            @ [X86_64.ADD_imm (X86_64.RSP, int32 padded)]  // pop
        Ok (pushInstrs @ writeInstrs)

    | LIR.PrintInt64 reg ->
        resolveReg reg
        |> Result.map (fun srcReg -> genPrintInt64AndExit srcReg)

    | LIR.PrintInt64NoNewline reg ->
        resolveReg reg
        |> Result.map (fun srcReg -> genPrintInt64 srcReg false)

    | LIR.PrintBool reg ->
        resolveReg reg
        |> Result.map (fun srcReg -> genPrintBoolAndExit srcReg)

    | LIR.PrintBoolNoNewline reg ->
        resolveReg reg
        |> Result.map (fun srcReg ->
            // TODO: implement bool printing without exit
            [])

    | LIR.Call (dest, funcName, _args) ->
        // TODO: implement argument passing
        resolveReg dest
        |> Result.map (fun destReg ->
            [X86_64.CALL funcName]
            @ (if destReg <> X86_64.RAX then [X86_64.MOV_reg (destReg, X86_64.RAX)] else []))

    | LIR.TailCall (funcName, _args) ->
        // TODO: implement argument passing
        Ok [X86_64.JMP funcName]

    | _ ->
        Error $"Unsupported LIR instruction in x86-64 codegen: {instr}"

/// Translate a LIR terminator to x86-64 instructions
let private translateTerminator (term: LIR.Terminator) : Result<X86_64.Instr list, string> =
    match term with
    | LIR.Ret ->
        Ok [X86_64.RET]
    | LIR.Jump (LIR.Label target) ->
        Ok [X86_64.JMP target]
    | LIR.Branch (cond, LIR.Label trueLabel, LIR.Label falseLabel) ->
        resolveReg cond
        |> Result.map (fun condReg ->
            [X86_64.TEST_reg (condReg, condReg)
             X86_64.Jcc (X86_64.NE, trueLabel)
             X86_64.JMP falseLabel])
    | LIR.BranchZero (cond, LIR.Label zeroLabel, LIR.Label nonZeroLabel) ->
        resolveReg cond
        |> Result.map (fun condReg ->
            [X86_64.TEST_reg (condReg, condReg)
             X86_64.Jcc (X86_64.EQ, zeroLabel)
             X86_64.JMP nonZeroLabel])
    | LIR.CondBranch (cond, LIR.Label trueLabel, LIR.Label falseLabel) ->
        let x86Cond =
            match cond with
            | LIR.EQ -> X86_64.EQ | LIR.NE -> X86_64.NE
            | LIR.LT -> X86_64.LT | LIR.GT -> X86_64.GT
            | LIR.LE -> X86_64.LE | LIR.GE -> X86_64.GE
        Ok [X86_64.Jcc (x86Cond, trueLabel)
            X86_64.JMP falseLabel]
    | _ ->
        Error $"Unsupported LIR terminator in x86-64 codegen: {term}"

/// Translate a LIR basic block to x86-64 instructions
let private translateBlock (block: LIR.BasicBlock) : Result<X86_64.Instr list, string> =
    let (LIR.Label labelName) = block.Label
    let labelInstr = [X86_64.Label labelName]

    let rec translateInstrs acc remaining =
        match remaining with
        | [] -> Ok (List.rev acc |> List.concat)
        | instr :: rest ->
            match translateInstr instr with
            | Error e -> Error e
            | Ok instrs -> translateInstrs (instrs :: acc) rest

    match translateInstrs [] block.Instrs with
    | Error e -> Error e
    | Ok bodyInstrs ->
        translateTerminator block.Terminator
        |> Result.map (fun termInstrs ->
            labelInstr @ bodyInstrs @ termInstrs)

/// Generate function prologue (stack frame setup, callee-saved register saves)
let private genPrologue (stackSize: int) (usedCalleeSaved: LIR.PhysReg list) : X86_64.Instr list =
    let saves =
        usedCalleeSaved
        |> List.map (fun reg -> X86_64.PUSH (lirRegToX86 reg))
    let stackAlloc =
        if stackSize > 0 then [X86_64.SUB_imm (X86_64.RSP, int32 (stackSize * 8))]
        else []
    saves @ stackAlloc

/// Generate function epilogue (stack frame teardown, callee-saved register restores)
let private genEpilogue (stackSize: int) (usedCalleeSaved: LIR.PhysReg list) : X86_64.Instr list =
    let stackDealloc =
        if stackSize > 0 then [X86_64.ADD_imm (X86_64.RSP, int32 (stackSize * 8))]
        else []
    let restores =
        usedCalleeSaved
        |> List.rev
        |> List.map (fun reg -> X86_64.POP (lirRegToX86 reg))
    stackDealloc @ restores

/// Translate a LIR function to x86-64 instructions
let translateFunction (func: LIR.Function) : Result<X86_64.Instr list, string> =
    let prologue = genPrologue func.StackSize func.UsedCalleeSaved

    // Translate all blocks in order (entry first)
    let (LIR.Label entryName) = func.CFG.Entry
    let entryBlock = Map.find func.CFG.Entry func.CFG.Blocks
    let otherBlocks =
        func.CFG.Blocks
        |> Map.toList
        |> List.filter (fun (label, _) -> label <> func.CFG.Entry)
        |> List.map snd

    let allBlocks = entryBlock :: otherBlocks

    let rec translateBlocks acc remaining =
        match remaining with
        | [] -> Ok (List.rev acc |> List.concat)
        | block :: rest ->
            match translateBlock block with
            | Error e -> Error e
            | Ok instrs -> translateBlocks (instrs :: acc) rest

    match translateBlocks [] allBlocks with
    | Error e -> Error e
    | Ok blockInstrs ->
        // Insert prologue after the entry label
        let funcLabel = [X86_64.Label func.Name]
        let entryLabel = [X86_64.Label entryName]
        Ok (funcLabel @ entryLabel @ prologue @ blockInstrs)

/// Translate a complete LIR program to x86-64 instructions
let translateProgram (LIR.Program functions) : Result<X86_64.Instr list, string> =
    let rec translateFuncs acc remaining =
        match remaining with
        | [] -> Ok (List.rev acc |> List.concat)
        | func :: rest ->
            match translateFunction func with
            | Error e -> Error e
            | Ok instrs -> translateFuncs (instrs :: acc) rest

    translateFuncs [] functions
