// Files.fs - Emit arm64 instructions for files operations.

module ARM64EmitFiles

open ARM64CodeGenTypes
open ARM64HeapAllocation
open ARM64LeakAccounting
open ARM64Operands

let internal emitFileReadText (ctx: CodeGenContext) (dest: LIR.Reg) (path: LIR.Operand) : Result<ARM64Symbolic.Instr list, string> =
    // File reading: generates syscall sequence to read file contents
    // Returns Result<String, String>
    lirRegToARM64Reg dest
    |> Result.bind (fun destReg ->
        match path with
        | LIR.Reg pathReg ->
            // Already a heap string pointer
            lirRegToARM64Reg pathReg
            |> Result.map (fun pathARM64 ->
                runtimeInstrs (ARM64FileRead.generateFileReadText ctx.Target destReg pathARM64)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterInc ctx)
        | LIR.StringSymbol value ->
            Ok (
                loadStringLiteralPointer ARM64Symbolic.X15 value
                @ runtimeInstrs (ARM64FileRead.generateFileReadText ctx.Target destReg ARM64Symbolic.X15)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterInc ctx)
        | LIR.StackSlot offset ->
            loadStackSlot ARM64Symbolic.X15 offset
            |> Result.map (fun loadInstrs ->
                loadInstrs
                @ runtimeInstrs (ARM64FileRead.generateFileReadText ctx.Target destReg ARM64Symbolic.X15)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterInc ctx)
        | _ -> Error "FileReadText requires string operand")

let internal emitFileExists (ctx: CodeGenContext) (dest: LIR.Reg) (path: LIR.Operand) : Result<ARM64Symbolic.Instr list, string> =
    // File exists check: generates syscall sequence to check file accessibility
    // Uses access/faccessat syscall to check if path exists
    // Path can be either a Reg (heap string pointer) or StringSymbol (literal string)
    lirRegToARM64Reg dest
    |> Result.bind (fun destReg ->
        match path with
        | LIR.Reg pathReg ->
            // Already a heap string pointer
            lirRegToARM64Reg pathReg
            |> Result.map (fun pathARM64 ->
                runtimeInstrs (ARM64FileMetadata.generateFileExists ctx.Target destReg pathARM64))
        | LIR.StringSymbol value ->
            Ok (loadStringLiteralPointer ARM64Symbolic.X15 value @ runtimeInstrs (ARM64FileMetadata.generateFileExists ctx.Target destReg ARM64Symbolic.X15))
        | LIR.StackSlot offset ->
            // Load heap string from stack slot
            loadStackSlot ARM64Symbolic.X15 offset
            |> Result.map (fun loadInstrs ->
                loadInstrs @ runtimeInstrs (ARM64FileMetadata.generateFileExists ctx.Target destReg ARM64Symbolic.X15))
        | _ -> Error "FileExists requires string operand")

let internal emitFileWriteText (ctx: CodeGenContext) (dest: LIR.Reg) (path: LIR.Operand) (content: LIR.Operand) : Result<ARM64Symbolic.Instr list, string> =
    // File write: writes content string to file at path
    // Returns Result<Unit, String>
    lirRegToARM64Reg dest
    |> Result.bind (fun destReg ->
        // Helper to get operand into a register
        let getOperandReg operand tempReg =
            match operand with
            | LIR.Reg reg ->
                lirRegToARM64Reg reg |> Result.map (fun r -> ([], r))
            | LIR.StringSymbol value ->
                Ok (loadStringLiteralPointer tempReg value, tempReg)
            | LIR.StackSlot offset ->
                loadStackSlot tempReg offset |> Result.map (fun instrs -> (instrs, tempReg))
            | _ -> Error "FileWriteText requires string operands"

        getOperandReg path ARM64Symbolic.X15
        |> Result.bind (fun (pathInstrs, pathReg) ->
            getOperandReg content ARM64Symbolic.X14
            |> Result.map (fun (contentInstrs, contentReg) ->
                pathInstrs
                @ contentInstrs
                @ runtimeInstrs (ARM64FileWrite.generateFileWriteText ctx.Target destReg pathReg contentReg false)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)))

let internal emitFileAppendText (ctx: CodeGenContext) (dest: LIR.Reg) (path: LIR.Operand) (content: LIR.Operand) : Result<ARM64Symbolic.Instr list, string> =
    // File append: appends content string to file at path
    // Returns Result<Unit, String>
    lirRegToARM64Reg dest
    |> Result.bind (fun destReg ->
        // Same helper as FileWriteText
        let getOperandReg operand tempReg =
            match operand with
            | LIR.Reg reg ->
                lirRegToARM64Reg reg |> Result.map (fun r -> ([], r))
            | LIR.StringSymbol value ->
                Ok (loadStringLiteralPointer tempReg value, tempReg)
            | LIR.StackSlot offset ->
                loadStackSlot tempReg offset |> Result.map (fun instrs -> (instrs, tempReg))
            | _ -> Error "FileAppendText requires string operands"

        getOperandReg path ARM64Symbolic.X15
        |> Result.bind (fun (pathInstrs, pathReg) ->
            getOperandReg content ARM64Symbolic.X14
            |> Result.map (fun (contentInstrs, contentReg) ->
                pathInstrs
                @ contentInstrs
                @ runtimeInstrs (ARM64FileWrite.generateFileWriteText ctx.Target destReg pathReg contentReg true)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)))

let internal emitFileDelete (ctx: CodeGenContext) (dest: LIR.Reg) (path: LIR.Operand) : Result<ARM64Symbolic.Instr list, string> =
    // File delete: deletes file at path
    // Uses unlink syscall to remove file
    // Returns Result<Unit, String>
    lirRegToARM64Reg dest
    |> Result.bind (fun destReg ->
        match path with
        | LIR.Reg pathReg ->
            // Already a heap string pointer
            lirRegToARM64Reg pathReg
            |> Result.map (fun pathARM64 ->
                runtimeInstrs (ARM64FileMetadata.generateFileDelete ctx.Target destReg pathARM64)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)
        | LIR.StringSymbol value ->
            Ok (
                loadStringLiteralPointer ARM64Symbolic.X15 value
                @ runtimeInstrs (ARM64FileMetadata.generateFileDelete ctx.Target destReg ARM64Symbolic.X15)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)
        | LIR.StackSlot offset ->
            // Load heap string from stack slot
            loadStackSlot ARM64Symbolic.X15 offset
            |> Result.map (fun loadInstrs ->
                loadInstrs
                @ runtimeInstrs (ARM64FileMetadata.generateFileDelete ctx.Target destReg ARM64Symbolic.X15)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)
        | _ -> Error "FileDelete requires string operand")

let internal emitFileSetExecutable (ctx: CodeGenContext) (dest: LIR.Reg) (path: LIR.Operand) : Result<ARM64Symbolic.Instr list, string> =
    // File set executable: sets executable bit on file at path
    // Uses chmod syscall with executable permission
    // Returns Result<Unit, String>
    lirRegToARM64Reg dest
    |> Result.bind (fun destReg ->
        match path with
        | LIR.Reg pathReg ->
            // Already a heap string pointer
            lirRegToARM64Reg pathReg
            |> Result.map (fun pathARM64 ->
                runtimeInstrs (ARM64FileMetadata.generateFileSetExecutable ctx.Target destReg pathARM64)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)
        | LIR.StringSymbol value ->
            Ok (
                loadStringLiteralPointer ARM64Symbolic.X15 value
                @ runtimeInstrs (ARM64FileMetadata.generateFileSetExecutable ctx.Target destReg ARM64Symbolic.X15)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)
        | LIR.StackSlot offset ->
            loadStackSlot ARM64Symbolic.X15 offset
            |> Result.map (fun loadInstrs ->
                loadInstrs
                @ runtimeInstrs (ARM64FileMetadata.generateFileSetExecutable ctx.Target destReg ARM64Symbolic.X15)
                @ generateLeakCounterInc ctx
                @ generateLeakCounterIncIfResultError ctx destReg)
        | _ -> Error "FileSetExecutable requires string operand")

let internal emitFileWriteFromPtr (ctx: CodeGenContext) (dest: LIR.Reg) (path: LIR.Operand) (ptr: LIR.Reg) (length: LIR.Reg) : Result<ARM64Symbolic.Instr list, string> =
    // Write raw bytes from ptr to file at path
    // Returns 1 on success, 0 on failure
    lirRegToARM64Reg dest
    |> Result.bind (fun destReg ->
        lirRegToARM64Reg ptr
        |> Result.bind (fun ptrARM64 ->
            lirRegToARM64Reg length
            |> Result.bind (fun lengthARM64 ->
                match path with
                | LIR.Reg pathReg ->
                    // Already a heap string pointer
                    lirRegToARM64Reg pathReg
                    |> Result.map (fun pathARM64 ->
                        runtimeInstrs (ARM64WriteFromPointer.generateFileWriteFromPtr ctx.Target destReg pathARM64 ptrARM64 lengthARM64)
                        @ generateLeakCounterIncIfResultError ctx destReg)
                | LIR.StringSymbol value ->
                    Ok (
                        loadStringLiteralPointer ARM64Symbolic.X15 value
                        @ runtimeInstrs (ARM64WriteFromPointer.generateFileWriteFromPtr ctx.Target destReg ARM64Symbolic.X15 ptrARM64 lengthARM64)
                        @ generateLeakCounterIncIfResultError ctx destReg)
                | LIR.StackSlot offset ->
                    loadStackSlot ARM64Symbolic.X15 offset
                    |> Result.map (fun loadInstrs ->
                        loadInstrs
                        @ runtimeInstrs (ARM64WriteFromPointer.generateFileWriteFromPtr ctx.Target destReg ARM64Symbolic.X15 ptrARM64 lengthARM64)
                        @ generateLeakCounterIncIfResultError ctx destReg)
                | _ -> Error "FileWriteFromPtr requires string path operand")))
