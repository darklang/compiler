// Frames.fs - Generate aligned frames and callee-saved register handling.

module X64Frames

open X64Operands
open X64Printing

// ============================================================================
// LIR Instruction Translation
// ============================================================================

let private alignedStackSize (stackSlots: int) (numCalleeSaved: int) : int =
    let returnAddr = 8
    let pushes = numCalleeSaved * 8
    let total = returnAddr + pushes + stackSlots  // StackSize is already in bytes from regalloc
    let aligned = ((total + 15) / 16) * 16
    aligned - returnAddr - pushes

let internal genPrologue (stackSize: int) (usedCalleeSaved: LIR.PhysReg list) : X86_64.Instr list =
    // Push RBP and set up frame pointer for stack slot access.
    // Stack slots use [RBP - offset] which is stable across SaveRegs PUSHes.
    let setupFP = [X86_64.PUSH X86_64.RBP; X86_64.MOV_reg (X86_64.RBP, X86_64.RSP)]
    let saves = usedCalleeSaved |> List.map (fun reg -> X86_64.PUSH (lirRegToX86 reg))
    let alignedSize = alignedStackSize stackSize (List.length usedCalleeSaved + 1)  // +1 for RBP push
    let stackAlloc =
        if alignedSize > 0 then [X86_64.SUB_imm (X86_64.RSP, int32 alignedSize)]
        else []
    setupFP @ saves @ stackAlloc

let internal genEpilogue (stackSize: int) (usedCalleeSaved: LIR.PhysReg list) : X86_64.Instr list =
    let alignedSize = alignedStackSize stackSize (List.length usedCalleeSaved + 1)
    let stackDealloc =
        if alignedSize > 0 then [X86_64.ADD_imm (X86_64.RSP, int32 alignedSize)]
        else []
    let restores = usedCalleeSaved |> List.rev |> List.map (fun reg -> X86_64.POP (lirRegToX86 reg))
    let restoreFP = [X86_64.POP X86_64.RBP]
    stackDealloc @ restores @ restoreFP
