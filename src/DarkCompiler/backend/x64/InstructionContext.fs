// InstructionContext.fs - Shared operand context for x64 instruction-family lowering.

module X64InstructionContext

open X64CodeGenTypes
open X64FieldReferenceCounts

/// Adjust a stack slot offset to account for callee-saved registers pushed after RBP.
/// LIR stack slots are byte offsets from FP (e.g., -8, -16), but callee-saved pushes
/// occupy [RBP-8] through [RBP-N*8], so spill slots must be shifted past them.
let internal adjustStackOffset (ctx: FuncCtx) (offset: int) : int =
    offset - (List.length ctx.UsedCalleeSaved * 8)

/// The comparison whose flags condition consumers read within a basic block.
/// UCOMISD sets CF/ZF differently from CMP, which sets SF/OF/ZF.
type internal ComparisonContext =
    | IntegerComparison
    | FloatComparison

/// Translate a single LIR instruction to x86-64 instructions
