// HeapAllocation.fs - Generate checked heap allocation and fatal allocation paths.

module ARM64HeapAllocation

open ARM64CodeGenTypes

let internal dataLabel (name: string) : ARM64Symbolic.LabelRef =
    ARM64Symbolic.DataLabel (ARM64Symbolic.Named name)

let internal stringDataLabel (value: string) : ARM64Symbolic.LabelRef =
    ARM64Symbolic.DataLabel (ARM64Symbolic.StringLiteral value)

let internal floatDataLabel (value: float) : ARM64Symbolic.LabelRef =
    ARM64Symbolic.DataLabel (ARM64Symbolic.FloatLiteral value)

let internal codeLabel (name: string) : ARM64Symbolic.LabelRef =
    ARM64Symbolic.CodeLabel name

let internal runtimeInstrs (instrs: ARM64.Instr list) : ARM64Symbolic.Instr list =
    ARM64Symbolic.ofARM64List instrs

let internal utf8Len (value: string) : int =
    System.Text.Encoding.UTF8.GetByteCount value

let internal loadStringLiteralPointer (destReg: ARM64Symbolic.Reg) (value: string) : ARM64Symbolic.Instr list =
    let labelRef = stringDataLabel value
    [
        ARM64Symbolic.ADRP (destReg, labelRef)
        ARM64Symbolic.ADD_label (destReg, destReg, labelRef)
    ]

let private generateHeapOverflowTrapBody (_target: ARM64.TargetConfig) : ARM64Symbolic.Instr list =
    loadStringLiteralPointer ARM64Symbolic.X0 heapOutOfMemoryMessage
    @ [ ARM64Symbolic.MOVZ (ARM64Symbolic.X3, 0us, 0)
        ARM64Symbolic.B_label runtimeErrorHelperLabel ]

/// Shared non-returning error writer. X0 is a fixed-header string buffer and X3
/// selects the newline required by dynamically constructed exception text.
let internal generateRuntimeErrorHelper (target: ARM64.TargetConfig) : ARM64Symbolic.Instr list =
    let syscalls = ARM64.targetSyscalls target
    let exitLabel = $"{runtimeErrorHelperLabel}_exit"
    [ ARM64Symbolic.Label runtimeErrorHelperLabel
      ARM64Symbolic.LDR (ARM64Symbolic.X2, ARM64Symbolic.X0, 8s)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.X0, 16us)
      ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 2us, 0)
      ARM64Symbolic.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
      ARM64Symbolic.SVC syscalls.SvcImmediate
      ARM64Symbolic.CBZ (ARM64Symbolic.X3, exitLabel) ]
    @ loadStringLiteralPointer ARM64Symbolic.X1 "\n"
    @ [ ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.X1, 16us)
        ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 2us, 0)
        ARM64Symbolic.MOVZ (ARM64Symbolic.X2, 1us, 0)
        ARM64Symbolic.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Write, 0)
        ARM64Symbolic.SVC syscalls.SvcImmediate
        ARM64Symbolic.Label exitLabel
        ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 1us, 0)
        ARM64Symbolic.MOVZ (syscalls.SyscallRegister, syscalls.Numbers.Exit, 0)
        ARM64Symbolic.SVC syscalls.SvcImmediate ]

// These cold paths depend only on the target ABI. Building their immutable
// instruction lists once avoids reconstructing the same message and syscall
// sequence for every executable in a compilation session.
let private macOSHeapOverflowTrapBody =
    generateHeapOverflowTrapBody (ARM64.targetConfigFor Platform.MacOSARM64)

let private linuxHeapOverflowTrapBody =
    generateHeapOverflowTrapBody (ARM64.targetConfigFor Platform.LinuxARM64)

let internal preparedHeapOverflowTrapBody (target: ARM64.TargetConfig) =
    match ARM64.targetOS target with
    | Platform.MacOS -> macOSHeapOverflowTrapBody
    | Platform.Linux -> linuxHeapOverflowTrapBody

let internal generateHeapOverflowTrapBlock
    (body: ARM64Symbolic.Instr list)
    (label: string)
    : ARM64Symbolic.Instr list =
    ARM64Symbolic.Label label :: body

let internal withHeapBoundsCheck
    (overflowLabel: string)
    (nextPtrInstrs: ARM64Symbolic.Instr list)
    (allocInstrs: ARM64Symbolic.Instr list)
    : ARM64Symbolic.Instr list =
    [
        ARM64Symbolic.MOVZ (ARM64Symbolic.X11, heapMmapSizeMovzImm16, 16)
        ARM64Symbolic.ADD_reg (ARM64Symbolic.X11, ARM64Symbolic.X27, ARM64Symbolic.X11)
    ]
    @ nextPtrInstrs
    @ [
        ARM64Symbolic.CMP_reg (ARM64Symbolic.X14, ARM64Symbolic.X11)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.GT, overflowLabel)
    ]
    @ allocInstrs

let internal checkedBumpAllocReg
    (overflowLabel: string)
    (destReg: ARM64Symbolic.Reg)
    (sizeReg: ARM64Symbolic.Reg)
    : ARM64Symbolic.Instr list =
    withHeapBoundsCheck
        overflowLabel
        [ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X28, sizeReg)]
        [
            ARM64Symbolic.MOV_reg (destReg, ARM64Symbolic.X28)
            ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, sizeReg)
        ]
