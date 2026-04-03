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
    | LIR.X9  -> X86_64.R11   // Scratch (shared, used for temporaries)
    | LIR.X10 -> X86_64.R11   // Scratch (shared)
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
    | LIR.D0  -> X86_64.XMM0
    | LIR.D1  -> X86_64.XMM1
    | LIR.D2  -> X86_64.XMM2
    | LIR.D3  -> X86_64.XMM3
    | LIR.D4  -> X86_64.XMM4
    | LIR.D5  -> X86_64.XMM5
    | LIR.D6  -> X86_64.XMM6
    | LIR.D7  -> X86_64.XMM7
    | LIR.D8  -> X86_64.XMM8
    | LIR.D9  -> X86_64.XMM9
    | LIR.D10 -> X86_64.XMM10
    | LIR.D11 -> X86_64.XMM11
    | LIR.D12 -> X86_64.XMM12
    | LIR.D13 -> X86_64.XMM13
    | LIR.D14 -> X86_64.XMM14
    | LIR.D15 -> X86_64.XMM15

/// Resolve a LIR.Reg (Physical or Virtual) to x86-64 register.
/// Virtual registers should not exist after register allocation.
let resolveReg (reg: LIR.Reg) : Result<X86_64.Reg, string> =
    match reg with
    | LIR.Physical phys -> Ok (lirRegToX86 phys)
    | LIR.Virtual id -> Error $"Unresolved virtual register v{id} in x86-64 codegen"

/// Load a 64-bit immediate into a register.
/// Uses MOV_imm32 (sign-extended) when possible, MOV_imm (movabs) otherwise.
let private loadImm64 (dest: X86_64.Reg) (value: int64) : X86_64.Instr list =
    if value = 0L then
        [X86_64.XOR_reg (dest, dest)]
    elif value >= int64 System.Int32.MinValue && value <= int64 System.Int32.MaxValue then
        [X86_64.MOV_imm32 (dest, int32 value)]
    else
        [X86_64.MOV_imm (dest, value)]

/// Generate x86-64 instructions for a LIR operand → register.
/// Returns the register and any instructions needed to load the value.
let private loadOperand (dest: X86_64.Reg) (op: LIR.Operand) : Result<X86_64.Instr list * X86_64.Reg, string> =
    match op with
    | LIR.Imm value ->
        Ok (loadImm64 dest value, dest)
    | LIR.Reg reg ->
        match resolveReg reg with
        | Error e -> Error e
        | Ok srcReg ->
            if srcReg = dest then Ok ([], dest)
            else Ok ([X86_64.MOV_reg (dest, srcReg)], dest)
    | LIR.StackSlot slot ->
        // Stack slots are at [RBP - offset] or [RSP + offset]
        // For now, use RSP-relative addressing
        Ok ([X86_64.MOV_load (dest, X86_64.RSP, int32 (slot * 8))], dest)
    | _ ->
        Error $"Unsupported operand in x86-64 codegen: {op}"

/// Generate a Linux x86_64 syscall sequence.
/// Syscall number in RAX, args in RDI, RSI, RDX, R10, R8, R9.
let private genSyscall (syscallNum: uint16) : X86_64.Instr list =
    loadImm64 X86_64.RAX (int64 syscallNum) @ [X86_64.SYSCALL]
