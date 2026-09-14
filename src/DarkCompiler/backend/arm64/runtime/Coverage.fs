// Coverage.fs - Generate coverage-buffer output at program termination.

module ARM64Coverage

open ARM64RuntimeImmediates
open ARM64PrintAndExit
open ARM64PrintValues
open ARM64FileMetadata
open ARM64FileRead
open ARM64FileWrite
open ARM64HostValues
open ARM64WriteFromPointer

/// Generate ARM64 instructions to flush coverage data to file
/// Writes coverage counters to /tmp/dark_cov.bin before program exit
/// coverageExprCount: number of expressions (determines bytes to write = count * 8)
///
/// Uses ADRP+ADD to get coverage data address from BSS section
/// Opens file, writes data, closes file (errors are silently ignored)
let generateCoverageFlush (target: ARM64.TargetConfig) (coverageExprCount: int) : ARM64.Instr list =
    if coverageExprCount = 0 then
        []
    else
        let os = ARM64.targetOS target
        let syscalls = ARM64.targetSyscalls target

        // O_WRONLY | O_CREAT | O_TRUNC
        let writeFlags =
            match os with
            | Platform.Linux -> 577us  // 1|64|512
            | Platform.MacOS -> 1537us  // 1|0x200|0x400

        // Path: "/tmp/dark_cov.bin" = 18 bytes + null = 19 bytes, round to 24 for alignment
        let byteCount = coverageExprCount * 8

        match os with
        | Platform.Linux ->
            [
                // Allocate stack for path (24 bytes, 8-byte aligned)
                ARM64.SUB_imm (ARM64.SP, ARM64.SP, 24us)

                // Write "/tmp/dark_cov.bin\0" to stack
                // First 8 bytes: "/tmp/dar" = 0x7261642F706D742F
                ARM64.MOVZ (ARM64.X9, 0x2F74us, 0)   // "/t"
                ARM64.MOVK (ARM64.X9, 0x6D70us, 16)  // "mp"
                ARM64.MOVK (ARM64.X9, 0x642Fus, 32)  // "/d"
                ARM64.MOVK (ARM64.X9, 0x7261us, 48)  // "ar"
                ARM64.STR (ARM64.X9, ARM64.SP, 0s)

                // Next 8 bytes: "k_cov.bi" = 0x69622E766F635F6B
                ARM64.MOVZ (ARM64.X9, 0x5F6Bus, 0)   // "k_"
                ARM64.MOVK (ARM64.X9, 0x6F63us, 16)  // "co"
                ARM64.MOVK (ARM64.X9, 0x2E76us, 32)  // "v."
                ARM64.MOVK (ARM64.X9, 0x6962us, 48)  // "bi"
                ARM64.STR (ARM64.X9, ARM64.SP, 8s)

                // Last 4 bytes: "n\0\0\0" = 0x0000006E
                ARM64.MOVZ (ARM64.X9, 0x006Eus, 0)   // "n\0"
                ARM64.STR (ARM64.X9, ARM64.SP, 16s)

                // Get coverage data address via ADRP+ADD
                ARM64.ADRP (ARM64.X10, ARM64Symbolic.coverageDataLabelName)
                ARM64.ADD_label (ARM64.X10, ARM64.X10, ARM64Symbolic.coverageDataLabelName)

                // openat(AT_FDCWD, path, flags, mode)
                ARM64.MOVZ (ARM64.X0, 100us, 0)
                ARM64.NEG (ARM64.X0, ARM64.X0)  // AT_FDCWD = -100
                ARM64.MOV_reg (ARM64.X1, ARM64.SP)  // path
                ARM64.MOVZ (ARM64.X2, writeFlags, 0)  // flags
                ARM64.MOVZ (ARM64.X3, 420us, 0)  // mode 0644
                ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Open, 0)
                ARM64.SVC syscalls.SvcImmediate

                // Check if open failed (X0 < 0)
                ARM64.TBNZ (ARM64.X0, 63, 6)  // If negative, skip to cleanup

                // Save fd
                ARM64.MOV_reg (ARM64.X11, ARM64.X0)

                // write(fd, buf, count)
                ARM64.MOV_reg (ARM64.X0, ARM64.X11)  // fd
                ARM64.MOV_reg (ARM64.X1, ARM64.X10)  // buf = coverage data
            ] @
            generateLoadNonNegativeIntImmediate ARM64.X2 byteCount @
            [
                ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Write, 0)
                ARM64.SVC syscalls.SvcImmediate

                // close(fd)
                ARM64.MOV_reg (ARM64.X0, ARM64.X11)
                ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Close, 0)
                ARM64.SVC syscalls.SvcImmediate

                // Cleanup stack
                ARM64.ADD_imm (ARM64.SP, ARM64.SP, 24us)
            ]

        | Platform.MacOS ->
            [
                // Allocate stack for path (24 bytes, 8-byte aligned)
                ARM64.SUB_imm (ARM64.SP, ARM64.SP, 24us)

                // Write "/tmp/dark_cov.bin\0" to stack (same as Linux)
                ARM64.MOVZ (ARM64.X9, 0x2F74us, 0)
                ARM64.MOVK (ARM64.X9, 0x6D70us, 16)
                ARM64.MOVK (ARM64.X9, 0x642Fus, 32)
                ARM64.MOVK (ARM64.X9, 0x7261us, 48)
                ARM64.STR (ARM64.X9, ARM64.SP, 0s)

                ARM64.MOVZ (ARM64.X9, 0x5F6Bus, 0)
                ARM64.MOVK (ARM64.X9, 0x6F63us, 16)
                ARM64.MOVK (ARM64.X9, 0x2E76us, 32)
                ARM64.MOVK (ARM64.X9, 0x6962us, 48)
                ARM64.STR (ARM64.X9, ARM64.SP, 8s)

                ARM64.MOVZ (ARM64.X9, 0x006Eus, 0)
                ARM64.STR (ARM64.X9, ARM64.SP, 16s)

                // Get coverage data address via ADRP+ADD
                ARM64.ADRP (ARM64.X10, ARM64Symbolic.coverageDataLabelName)
                ARM64.ADD_label (ARM64.X10, ARM64.X10, ARM64Symbolic.coverageDataLabelName)

                // open(path, flags, mode) - macOS uses direct open syscall
                ARM64.MOV_reg (ARM64.X0, ARM64.SP)  // path
                ARM64.MOVZ (ARM64.X1, writeFlags, 0)  // flags
                ARM64.MOVZ (ARM64.X2, 420us, 0)  // mode 0644
                ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Open, 0)
                ARM64.SVC syscalls.SvcImmediate

                // Check if open failed (X0 < 0)
                ARM64.TBNZ (ARM64.X0, 63, 6)  // If negative, skip to cleanup

                // Save fd
                ARM64.MOV_reg (ARM64.X11, ARM64.X0)

                // write(fd, buf, count)
                ARM64.MOV_reg (ARM64.X0, ARM64.X11)  // fd
                ARM64.MOV_reg (ARM64.X1, ARM64.X10)  // buf = coverage data
            ] @
            generateLoadNonNegativeIntImmediate ARM64.X2 byteCount @
            [
                ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Write, 0)
                ARM64.SVC syscalls.SvcImmediate

                // close(fd)
                ARM64.MOV_reg (ARM64.X0, ARM64.X11)
                ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Close, 0)
                ARM64.SVC syscalls.SvcImmediate

                // Cleanup stack
                ARM64.ADD_imm (ARM64.SP, ARM64.SP, 24us)
            ]
