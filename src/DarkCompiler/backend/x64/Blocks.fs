// Blocks.fs - Lower terminators and order blocks for target fallthrough.

module X64Blocks

open X64Operands
open X64CodeGenTypes
open X64FieldReferenceCounts
open X64InstructionContext
open X64Instructions

// All LIR instruction variants are handled above

/// Calculate aligned stack allocation size.
/// After CALL pushes 8-byte return address, each PUSH adds 8 bytes.
/// We need total to be 16-byte aligned for System V ABI compliance.
/// Translate a LIR terminator to x86-64 instructions
let private translateTerminator
    (comparisonContext: ComparisonContext option)
    (epilogueLabel: string)
    (nextLabel: string option)
    (term: LIR.Terminator)
    : Result<X86_64.Instr list, string> =
    match term with
    | LIR.Ret ->
        // Jump to shared epilogue at end of function
        if nextLabel = None then Ok [] else Ok [X86_64.JMP epilogueLabel]
    | LIR.Jump (LIR.Label target) ->
        if nextLabel = Some target then Ok [] else Ok [X86_64.JMP target]
    | LIR.Branch (cond, LIR.Label trueLabel, LIR.Label falseLabel) ->
        resolveReg cond
        |> Result.map (fun condReg ->
            [X86_64.TEST_reg (condReg, condReg)
             X86_64.Jcc (X86_64.NE, trueLabel)]
            @ (if nextLabel = Some falseLabel then [] else [X86_64.JMP falseLabel]))
    | LIR.BranchZero (cond, LIR.Label zeroLabel, LIR.Label nonZeroLabel) ->
        resolveReg cond
        |> Result.map (fun condReg ->
            [X86_64.TEST_reg (condReg, condReg)
             X86_64.Jcc (X86_64.EQ, zeroLabel)]
            @ (if nextLabel = Some nonZeroLabel then [] else [X86_64.JMP nonZeroLabel]))
    | LIR.CondBranch (cond, LIR.Label trueLabel, LIR.Label falseLabel) ->
        match comparisonContext with
        | None ->
            Error "x64 codegen: CondBranch without a preceding comparison in the same block"
        | Some comparisonContext ->
            if comparisonContext = FloatComparison then
                // UCOMISD sets PF for unordered operands. EQ, below, and
                // below-or-equal otherwise also match NaN and need an explicit
                // ordered guard; NE deliberately treats unordered as true.
                let branches =
                    match cond with
                    | LIR.EQ ->
                        [ X86_64.Jcc (X86_64.P, falseLabel)
                          X86_64.Jcc (X86_64.EQ, trueLabel) ]
                    | LIR.NE ->
                        [ X86_64.Jcc (X86_64.P, trueLabel)
                          X86_64.Jcc (X86_64.NE, trueLabel) ]
                    | LIR.LT | LIR.ULT ->
                        [ X86_64.Jcc (X86_64.P, falseLabel)
                          X86_64.Jcc (X86_64.B, trueLabel) ]
                    | LIR.LE | LIR.ULE ->
                        [ X86_64.Jcc (X86_64.P, falseLabel)
                          X86_64.Jcc (X86_64.BE, trueLabel) ]
                    | LIR.GT | LIR.UGT -> [X86_64.Jcc (X86_64.A, trueLabel)]
                    | LIR.GE | LIR.UGE -> [X86_64.Jcc (X86_64.AE, trueLabel)]
                Ok (branches @ [X86_64.JMP falseLabel])
            else
                let x86Cond =
                    match cond with
                    | LIR.EQ -> X86_64.EQ | LIR.NE -> X86_64.NE
                    | LIR.LT -> X86_64.LT | LIR.GT -> X86_64.GT
                    | LIR.LE -> X86_64.LE | LIR.GE -> X86_64.GE
                    | LIR.ULT -> X86_64.B | LIR.UGT -> X86_64.A
                    | LIR.ULE -> X86_64.BE | LIR.UGE -> X86_64.AE
                Ok ([X86_64.Jcc (x86Cond, trueLabel)]
                    @ (if nextLabel = Some falseLabel then [] else [X86_64.JMP falseLabel]))
    | LIR.BranchBitZero (reg, bit, LIR.Label zeroLabel, LIR.Label nonZeroLabel) ->
        resolveReg reg
        |> Result.map (fun regX86 ->
            let mask = 1L <<< bit
            loadImm64 scratch mask
            @ [X86_64.AND_reg (scratch, regX86)
               X86_64.Jcc (X86_64.EQ, zeroLabel)]
            @ (if nextLabel = Some nonZeroLabel then [] else [X86_64.JMP nonZeroLabel]))

    | LIR.BranchBitNonZero (reg, bit, LIR.Label nonZeroLabel, LIR.Label zeroLabel) ->
        resolveReg reg
        |> Result.map (fun regX86 ->
            let mask = 1L <<< bit
            loadImm64 scratch mask
            @ [X86_64.AND_reg (scratch, regX86)
               X86_64.Jcc (X86_64.NE, nonZeroLabel)]
            @ (if nextLabel = Some zeroLabel then [] else [X86_64.JMP zeroLabel]))

/// Translate a LIR basic block to x86-64 instructions
let internal translateBlock (ctx: FuncCtx) (epilogueLabel: string) (nextBlock: LIR.BasicBlock option) (block: LIR.BasicBlock) : Result<X86_64.Instr list, string> =
    let (LIR.Label labelName) = block.Label
    let labelInstr = [X86_64.Label labelName]

    let rec translateInstrs comparisonContext acc remaining =
        match remaining with
        | [] -> Ok (List.rev acc |> List.concat, comparisonContext)
        | instr :: rest ->
            match translateInstr comparisonContext ctx instr with
            | Error e -> Error e
            | Ok instrs ->
                let nextComparisonContext =
                    match instr with
                    | LIR.Cmp _ -> Some IntegerComparison
                    | LIR.FCmp _ -> Some FloatComparison
                    | _ -> comparisonContext
                translateInstrs nextComparisonContext (instrs :: acc) rest

    match translateInstrs None [] block.Instrs with
    | Error e -> Error e
    | Ok (bodyInstrs, comparisonContext) ->
        let nextLabel = nextBlock |> Option.map (fun next -> let (LIR.Label label) = next.Label in label)
        translateTerminator comparisonContext epilogueLabel nextLabel block.Terminator
        |> Result.map (fun termInstrs ->
            labelInstr @ bodyInstrs @ termInstrs)
