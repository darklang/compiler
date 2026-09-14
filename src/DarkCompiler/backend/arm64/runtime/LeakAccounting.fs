// LeakAccounting.fs - Generate allocation accounting and leak reports.

module ARM64LeakAccounting

open ARM64CodeGenTypes
open ARM64HeapAllocation

let generateLeakCounterInc (ctx: CodeGenContext) : ARM64Symbolic.Instr list =
    if ctx.Options.EnableLeakCheck then
        let labelRef = dataLabel leakCounterLabel
        [
            ARM64Symbolic.ADRP (ARM64Symbolic.X17, labelRef)
            ARM64Symbolic.ADD_label (ARM64Symbolic.X17, ARM64Symbolic.X17, labelRef)
            ARM64Symbolic.LDR (ARM64Symbolic.X16, ARM64Symbolic.X17, 0s)
            ARM64Symbolic.ADD_imm (ARM64Symbolic.X16, ARM64Symbolic.X16, 1us)
            ARM64Symbolic.STR (ARM64Symbolic.X16, ARM64Symbolic.X17, 0s)
        ]
    else
        []

let generateLeakCounterDec (ctx: CodeGenContext) : ARM64Symbolic.Instr list =
    if ctx.Options.EnableLeakCheck then
        let labelRef = dataLabel leakCounterLabel
        [
            ARM64Symbolic.ADRP (ARM64Symbolic.X17, labelRef)
            ARM64Symbolic.ADD_label (ARM64Symbolic.X17, ARM64Symbolic.X17, labelRef)
            ARM64Symbolic.LDR (ARM64Symbolic.X16, ARM64Symbolic.X17, 0s)
            ARM64Symbolic.SUB_imm (ARM64Symbolic.X16, ARM64Symbolic.X16, 1us)
            ARM64Symbolic.STR (ARM64Symbolic.X16, ARM64Symbolic.X17, 0s)
        ]
    else
        []

let generateLeakCounterIncIfResultError (ctx: CodeGenContext) (resultReg: ARM64Symbolic.Reg) : ARM64Symbolic.Instr list =
    let leakInc = generateLeakCounterInc ctx
    if List.isEmpty leakInc then
        []
    else
        [
            ARM64Symbolic.LDR (ARM64Symbolic.X15, resultReg, 0s)
            ARM64Symbolic.CBZ_offset (ARM64Symbolic.X15, List.length leakInc + 1)
        ] @ leakInc

let generateLeakCheckReport (ctx: CodeGenContext) : ARM64Symbolic.Instr list =
    if ctx.Options.EnableLeakCheck then
        let prefix = ARM64PrintValues.generatePrintCharsToStderr ctx.Target [byte 'l'; byte 'e'; byte 'a'; byte 'k'; byte 's'; byte ':'; byte ' '] |> runtimeInstrs
        let printCount = ARM64PrintValues.generatePrintInt64ToStderrNoExit ctx.Target |> runtimeInstrs
        let skipOffset = List.length prefix + 1 + List.length printCount + 1
        let labelRef = dataLabel leakCounterLabel
        [
            ARM64Symbolic.ADRP (ARM64Symbolic.X17, labelRef)
            ARM64Symbolic.ADD_label (ARM64Symbolic.X17, ARM64Symbolic.X17, labelRef)
            ARM64Symbolic.LDR (ARM64Symbolic.X16, ARM64Symbolic.X17, 0s)
            ARM64Symbolic.CBZ_offset (ARM64Symbolic.X16, skipOffset)
        ]
        @ prefix
        @ [ARM64Symbolic.MOV_reg (ARM64Symbolic.X0, ARM64Symbolic.X16)]
        @ printCount
    else
        []
