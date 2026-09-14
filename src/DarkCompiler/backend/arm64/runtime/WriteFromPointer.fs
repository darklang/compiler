// WriteFromPointer.fs - Generate checked writes from native pointer ranges.

module ARM64WriteFromPointer

/// Generate code for FileWriteFromPtr: write raw bytes to a file
/// pathReg: register containing heap string pointer to file path
/// ptrReg: register containing raw pointer to bytes
/// lengthReg: register containing length in bytes
/// destReg: destination register (result = 1 on success, 0 on failure)
let generateFileWriteFromPtr (target: ARM64.TargetConfig) (destReg: ARM64.Reg) (pathReg: ARM64.Reg) (ptrReg: ARM64.Reg) (lengthReg: ARM64.Reg) : ARM64.Instr list =
    let os = ARM64.targetOS target
    let syscalls = ARM64.targetSyscalls target

    // O_WRONLY | O_CREAT | O_TRUNC
    let writeFlags =
        match os with
        | Platform.Linux -> 577us  // 1|64|512
        | Platform.MacOS -> 1537us  // 1|0x200|0x400

    match os with
    | Platform.Linux ->
        [
            // Save callee-saved registers
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.STP (ARM64.X21, ARM64.X22, ARM64.SP, -32s)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

            // Allocate stack for path buffer (256 bytes)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 1us)

            // Save registers
            ARM64.MOV_reg (ARM64.X19, pathReg)     // X19 = path heap string
            ARM64.MOV_reg (ARM64.X20, ptrReg)     // X20 = data pointer
            ARM64.MOV_reg (ARM64.X21, lengthReg)  // X21 = length

            // Copy path to stack buffer with null terminator
            ARM64.LDR (ARM64.X2, ARM64.X19, 8s)  // X2 = path length
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)  // X0 = dest buffer
            ARM64.ADD_imm (ARM64.X1, ARM64.X19, 16us)  // X1 = source data
            ARM64.MOVZ (ARM64.X4, 0us, 0)  // X4 = index

            // Copy loop
            ARM64.CBZ_offset (ARM64.X2, 7)
            ARM64.LDRB (ARM64.X3, ARM64.X1, ARM64.X4)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)
            ARM64.ADD_imm (ARM64.X0, ARM64.X0, 1us)
            ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
            ARM64.SUB_imm (ARM64.X2, ARM64.X2, 1us)
            ARM64.B (-6)

            // Store null terminator
            ARM64.MOVZ (ARM64.X3, 0us, 0)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)

            // openat(AT_FDCWD, path, flags, mode)
            ARM64.MOVZ (ARM64.X0, 100us, 0)
            ARM64.NEG (ARM64.X0, ARM64.X0)  // AT_FDCWD = -100
            ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // path
            ARM64.MOVZ (ARM64.X2, writeFlags, 0)  // flags
            ARM64.MOVZ (ARM64.X3, 420us, 0)  // mode 0644
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Open, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Check if open failed
            ARM64.MOV_reg (ARM64.X22, ARM64.X0)  // X22 = fd
            ARM64.TBNZ (ARM64.X0, 63, 11)  // If negative, branch to error path

            // write(fd, buf, count)
            ARM64.MOV_reg (ARM64.X0, ARM64.X22)  // fd
            ARM64.MOV_reg (ARM64.X1, ARM64.X20)  // buf = data pointer
            ARM64.MOV_reg (ARM64.X2, ARM64.X21)  // count = length
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Write, 0)
            ARM64.SVC syscalls.SvcImmediate

            // close(fd)
            ARM64.MOV_reg (ARM64.X0, ARM64.X22)
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Close, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Success: result = 1
            ARM64.MOVZ (ARM64.X0, 1us, 0)
            ARM64.B 2  // Skip error path

            // Error path: result = 0
            ARM64.MOVZ (ARM64.X0, 0us, 0)

            // Cleanup
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 1us)
            ARM64.LDP (ARM64.X21, ARM64.X22, ARM64.SP, 0s)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 16s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]

    | Platform.MacOS ->
        [
            // Save callee-saved registers
            ARM64.STP (ARM64.X19, ARM64.X20, ARM64.SP, -16s)
            ARM64.STP (ARM64.X21, ARM64.X22, ARM64.SP, -32s)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)

            // Allocate stack for path buffer (256 bytes)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 1us)

            // Save registers
            ARM64.MOV_reg (ARM64.X19, pathReg)     // X19 = path heap string
            ARM64.MOV_reg (ARM64.X20, ptrReg)     // X20 = data pointer
            ARM64.MOV_reg (ARM64.X21, lengthReg)  // X21 = length

            // Copy path to stack buffer with null terminator
            ARM64.LDR (ARM64.X2, ARM64.X19, 8s)  // X2 = path length
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)  // X0 = dest buffer
            ARM64.ADD_imm (ARM64.X1, ARM64.X19, 16us)  // X1 = source data
            ARM64.MOVZ (ARM64.X4, 0us, 0)  // X4 = index

            // Copy loop
            ARM64.CBZ_offset (ARM64.X2, 7)
            ARM64.LDRB (ARM64.X3, ARM64.X1, ARM64.X4)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)
            ARM64.ADD_imm (ARM64.X0, ARM64.X0, 1us)
            ARM64.ADD_imm (ARM64.X1, ARM64.X1, 1us)
            ARM64.SUB_imm (ARM64.X2, ARM64.X2, 1us)
            ARM64.B (-6)

            // Store null terminator
            ARM64.MOVZ (ARM64.X3, 0us, 0)
            ARM64.STRB (ARM64.X3, ARM64.X0, 0)

            // open(path, flags, mode) - macOS uses open, not openat
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)  // path
            ARM64.MOVZ (ARM64.X1, writeFlags, 0)  // flags
            ARM64.MOVZ (ARM64.X2, 420us, 0)  // mode 0644
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Open, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Check if open failed
            ARM64.MOV_reg (ARM64.X22, ARM64.X0)  // X22 = fd
            ARM64.TBNZ (ARM64.X0, 63, 10)  // If negative, branch to error path

            // write(fd, buf, count)
            ARM64.MOV_reg (ARM64.X0, ARM64.X22)  // fd
            ARM64.MOV_reg (ARM64.X1, ARM64.X20)  // buf = data pointer
            ARM64.MOV_reg (ARM64.X2, ARM64.X21)  // count = length
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Write, 0)
            ARM64.SVC syscalls.SvcImmediate

            // close(fd)
            ARM64.MOV_reg (ARM64.X0, ARM64.X22)
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Close, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Success: result = 1
            ARM64.MOVZ (ARM64.X0, 1us, 0)
            ARM64.B 2  // Skip error path

            // Error path: result = 0
            ARM64.MOVZ (ARM64.X0, 0us, 0)

            // Cleanup
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 255us)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 1us)
            ARM64.LDP (ARM64.X21, ARM64.X22, ARM64.SP, 0s)
            ARM64.LDP (ARM64.X19, ARM64.X20, ARM64.SP, 16s)
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]
