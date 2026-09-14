// Printing.fs - Generate x64 scalar printing and heap initialization support.

module X64Printing

open X64Operands

/// Generate x86-64 instructions to print a signed 64-bit integer to stdout.
/// Value is in the given register. Includes newline. Does NOT exit.
///
/// Algorithm: itoa by repeated division by 10, writing digits backwards
/// into a stack buffer, then write(1, buf, len).
let internal genPrintInt64 (srcReg: X86_64.Reg) (addNewline: bool) : X86_64.Instr list =
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

/// Generate x86-64 instructions to print an unsigned 64-bit integer to stdout.
let internal genPrintUInt64 (srcReg: X86_64.Reg) (addNewline: bool) : X86_64.Instr list =
    let loopLabel = freshLabel "utoa_loop"
    let zeroLabel = freshLabel "utoa_zero"
    let writeLabel = freshLabel "utoa_write"

    [
        X86_64.SUB_imm (X86_64.RSP, 32)
        X86_64.LEA (X86_64.RCX, X86_64.RSP, 31)
    ]
    @ (if addNewline then [
        X86_64.MOV_imm32 (scratch, 10)
        X86_64.MOV_store_byte (X86_64.RCX, 0, scratch)
        X86_64.SUB_imm (X86_64.RCX, 1)
    ] else [])
    @ [
        X86_64.MOV_reg (X86_64.R8, srcReg)
        X86_64.TEST_reg (X86_64.R8, X86_64.R8)
        X86_64.Jcc (X86_64.EQ, zeroLabel)

        X86_64.Label loopLabel
        X86_64.MOV_reg (X86_64.RAX, X86_64.R8)
        X86_64.XOR_reg (X86_64.RDX, X86_64.RDX)
        X86_64.MOV_imm32 (X86_64.RSI, 10)
        X86_64.DIV X86_64.RSI
        X86_64.ADD_imm (X86_64.RDX, 48)
        X86_64.MOV_store_byte (X86_64.RCX, 0, X86_64.RDX)
        X86_64.SUB_imm (X86_64.RCX, 1)
        X86_64.MOV_reg (X86_64.R8, X86_64.RAX)
        X86_64.TEST_reg (X86_64.R8, X86_64.R8)
        X86_64.Jcc (X86_64.NE, loopLabel)
        X86_64.JMP writeLabel

        X86_64.Label zeroLabel
        X86_64.MOV_imm32 (scratch, 48)
        X86_64.MOV_store_byte (X86_64.RCX, 0, scratch)
        X86_64.SUB_imm (X86_64.RCX, 1)

        X86_64.Label writeLabel
        X86_64.ADD_imm (X86_64.RCX, 1)
        X86_64.LEA (X86_64.RDX, X86_64.RSP, 32)
        X86_64.SUB_reg (X86_64.RDX, X86_64.RCX)
        X86_64.MOV_imm32 (X86_64.RDI, 1)
        X86_64.MOV_reg (X86_64.RSI, X86_64.RCX)
    ]
    @ genWriteSyscall
    @ [X86_64.ADD_imm (X86_64.RSP, 32)]

/// Generate PrintInt64 + exit(0)
let private genPrintInt64AndExit (srcReg: X86_64.Reg) : X86_64.Instr list =
    genPrintInt64 srcReg true
    @ loadImm64 X86_64.RDI 0L
    @ genExitSyscall

/// Generate heap initialization via mmap (only for _start).
let internal genHeapInit () : X86_64.Instr list =
    let failLabel = freshLabel "mmap_fail"
    let okLabel = freshLabel "mmap_ok"
    // mmap(NULL, 512MB, PROT_READ|PROT_WRITE, MAP_PRIVATE|MAP_ANONYMOUS, -1, 0)
    // x86_64 Linux: rax=9, rdi=addr, rsi=length, rdx=prot, r10=flags, r8=fd, r9=offset
    loadImm64 X86_64.RDI 0L
    @ loadImm64 X86_64.RSI heapMmapSizeBytes
    @ loadImm64 X86_64.RDX 3L                         // PROT_READ | PROT_WRITE
    @ loadImm64 X86_64.R10 0x22L                       // MAP_PRIVATE | MAP_ANONYMOUS
    @ [X86_64.MOV_imm32 (X86_64.R8, -1)]              // fd = -1
    @ loadImm64 X86_64.R9 0L                           // offset = 0
    @ loadImm64 X86_64.RAX (int64 syscalls.Mmap)
    @ [X86_64.SYSCALL
       X86_64.CMP_imm (X86_64.RAX, -1)
       X86_64.Jcc (X86_64.NE, okLabel)
       X86_64.Label failLabel]
    @ loadImm64 X86_64.RDI 1L
    @ genExitSyscall
    @ [X86_64.Label okLabel
       X86_64.MOV_reg (freeListBase, X86_64.RAX)
       X86_64.LEA (heapPtr, freeListBase, int32 (freeListSize + processTableSize))]

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
