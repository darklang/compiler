// Blocks.fs - Lower terminators and order blocks for target fallthrough.

module ARM64Blocks

open ARM64CodeGenTypes
open ARM64Operands
open ARM64Instructions

/// Convert LIR terminator to ARM64 instructions
/// epilogueLabel: the label to jump to for function return (handles stack cleanup)
let convertTerminator (epilogueLabel: string) (nextLabel: string option) (terminator: LIR.Terminator) : Result<ARM64Symbolic.Instr list, string> =
    match terminator with
    | LIR.Ret ->
        // Jump to function epilogue (handles stack cleanup and RET)
        if nextLabel = None then Ok [] else Ok [ARM64Symbolic.B_label epilogueLabel]

    | LIR.Branch (condReg, trueLabel, falseLabel) ->
        // Branch if register is non-zero (true), otherwise fall through to else
        // Use CBNZ (compare and branch if not zero) to true label
        // Then unconditional branch to false label
        lirRegToARM64Reg condReg
        |> Result.map (fun arm64Reg ->
            let (LIR.Label trueLbl) = trueLabel
            let (LIR.Label falseLbl) = falseLabel
            [ARM64Symbolic.CBNZ (arm64Reg, trueLbl)]
            @ (if nextLabel = Some falseLbl then [] else [ARM64Symbolic.B_label falseLbl]))

    | LIR.BranchZero (condReg, zeroLabel, nonZeroLabel) ->
        // Branch if register is zero, otherwise fall through to non-zero case
        // Use CBZ (compare and branch if zero) to zero label
        // Then unconditional branch to non-zero label
        lirRegToARM64Reg condReg
        |> Result.map (fun arm64Reg ->
            let (LIR.Label zeroLbl) = zeroLabel
            let (LIR.Label nonZeroLbl) = nonZeroLabel
            [ARM64Symbolic.CBZ (arm64Reg, zeroLbl)]
            @ (if nextLabel = Some nonZeroLbl then [] else [ARM64Symbolic.B_label nonZeroLbl]))

    | LIR.BranchBitZero (condReg, bit, zeroLabel, nonZeroLabel) ->
        // Branch if specified bit is zero, otherwise fall through to non-zero case
        // Use TBZ (test bit and branch if zero) to zero label
        // Then unconditional branch to non-zero label
        lirRegToARM64Reg condReg
        |> Result.map (fun arm64Reg ->
            let (LIR.Label zeroLbl) = zeroLabel
            let (LIR.Label nonZeroLbl) = nonZeroLabel
            [ARM64Symbolic.TBZ_label (arm64Reg, bit, zeroLbl)]
            @ (if nextLabel = Some nonZeroLbl then [] else [ARM64Symbolic.B_label nonZeroLbl]))

    | LIR.BranchBitNonZero (condReg, bit, nonZeroLabel, zeroLabel) ->
        // Branch if specified bit is non-zero, otherwise fall through to zero case
        // Use TBNZ (test bit and branch if not zero) to non-zero label
        // Then unconditional branch to zero label
        lirRegToARM64Reg condReg
        |> Result.map (fun arm64Reg ->
            let (LIR.Label nonZeroLbl) = nonZeroLabel
            let (LIR.Label zeroLbl) = zeroLabel
            [ARM64Symbolic.TBNZ_label (arm64Reg, bit, nonZeroLbl)]
            @ (if nextLabel = Some zeroLbl then [] else [ARM64Symbolic.B_label zeroLbl]))

    | LIR.Jump label ->
        let (LIR.Label lbl) = label
        if nextLabel = Some lbl then Ok [] else Ok [ARM64Symbolic.B_label lbl]

    | LIR.CondBranch (cond, trueLabel, falseLabel) ->
        // Branch based on condition flags (set by previous CMP)
        // Use B.cond to true label, then unconditional branch to false label
        let (LIR.Label trueLbl) = trueLabel
        let (LIR.Label falseLbl) = falseLabel
        let arm64Cond =
            match cond with
            | LIR.EQ -> ARM64Symbolic.EQ
            | LIR.NE -> ARM64Symbolic.NE
            | LIR.LT -> ARM64Symbolic.LT
            | LIR.GT -> ARM64Symbolic.GT
            | LIR.LE -> ARM64Symbolic.LE
            | LIR.GE -> ARM64Symbolic.GE
            | LIR.ULT -> ARM64Symbolic.LO
            | LIR.UGT -> ARM64Symbolic.HI
            | LIR.ULE -> ARM64Symbolic.LS
            | LIR.UGE -> ARM64Symbolic.HS
        Ok ([ARM64Symbolic.B_cond_label (arm64Cond, trueLbl)]
            @ (if nextLabel = Some falseLbl then [] else [ARM64Symbolic.B_label falseLbl]))

/// Convert LIR basic block to ARM64 instructions (with label)
/// epilogueLabel: passed through to terminator for Ret handling
let private lirInstructionCaseNames =
    Microsoft.FSharp.Reflection.FSharpType.GetUnionCases(typeof<LIR.Instr>)
    |> Array.map (fun case -> case.Name)

let private readLirInstructionTag =
    Microsoft.FSharp.Reflection.FSharpValue.PreComputeUnionTagReader(typeof<LIR.Instr>)

let private lirInstructionOpcode (instr: LIR.Instr) : string =
    let tag = readLirInstructionTag instr
    if tag < 0 || tag >= lirInstructionCaseNames.Length then
        Crash.crash $"ARM64 LIR profiling received invalid instruction tag {tag}"
    else
        lirInstructionCaseNames.[tag]

let private lirInstructionProfileDetail (instr: LIR.Instr) : string =
    match instr with
    | LIR.RefCountInc (_, payloadSize, kind, metadata)
    | LIR.RefCountDec (_, payloadSize, kind, metadata) ->
        let sourceType =
            metadata
            |> Option.bind (fun value -> value.SourceType)
            |> Option.map CheckingDiagnostics.typeToString
            |> Option.defaultValue "unknown"
        $"{kind}:{payloadSize}:{sourceType}"
    | _ -> ""

let convertBlock (ctx: CodeGenContext) (epilogueLabel: string) (nextBlock: LIR.BasicBlock option) (block: LIR.BasicBlock) : Result<ARM64Symbolic.Instr list, string> =
    // Emit label for this block
    let (LIR.Label lbl) = block.Label
    let labelInstr = ARM64Symbolic.Label lbl

    block.Instrs
    |> List.mapi (fun index instr ->
        let instructionCtx = {
            ctx with InstructionSite = $"{lbl}_{index}"
        }
        match ctx.RecordLirOpExpansion with
        | None -> convertInstr instructionCtx instr
        | Some record ->
            let started = System.Diagnostics.Stopwatch.GetTimestamp()
            convertInstr instructionCtx instr
            |> Result.map (fun instructions ->
                let elapsedTicks =
                    System.Diagnostics.Stopwatch.GetTimestamp() - started
                record
                    ctx.FunctionName
                    (lirInstructionOpcode instr)
                    (lirInstructionProfileDetail instr)
                    instructions.Length
                    elapsedTicks
                instructions))
    |> ResultList.collectResults id
    |> Result.bind (fun instrs ->
        let nextLabel = nextBlock |> Option.map (fun next -> let (LIR.Label label) = next.Label in label)
        convertTerminator epilogueLabel nextLabel block.Terminator
        |> Result.map (fun termInstrs ->
            labelInstr :: (instrs @ termInstrs)))

/// Convert LIR CFG to ARM64 instructions
/// epilogueLabel: passed through to blocks for Ret handling
let convertCFG (ctx: CodeGenContext) (epilogueLabel: string) (cfg: LIR.CFG) : Result<ARM64Symbolic.Instr list, string> =
    LIR.layoutBlocks cfg
    |> Result.mapError (fun e -> $"ARM64 codegen: function {ctx.FunctionName}: {e}")
    |> Result.bind (fun blocks ->
        blocks
        |> List.mapi (fun index block -> convertBlock ctx epilogueLabel (List.tryItem (index + 1) blocks) block)
        |> ResultList.collectResults id)
