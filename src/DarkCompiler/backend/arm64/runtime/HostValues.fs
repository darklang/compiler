// HostValues.fs - Generate host clock and randomness operations.

module ARM64HostValues

open ARM64RuntimeImmediates
open ARM64PrintAndExit
open ARM64PrintValues
open ARM64FileMetadata
open ARM64FileRead
open ARM64FileWrite

/// Generate ARM64 instructions to get 8 random bytes as Int64
/// destReg: destination register for the random Int64
/// Uses getrandom (Linux) or getentropy (macOS) syscall
/// Note: This function saves/restores caller-saved registers X1, X2, X8
/// that may contain live values, since the syscall clobbers them.
let generateRandomInt64 (target: ARM64.TargetConfig) (destReg: ARM64.Reg) : ARM64.Instr list =
    let os = ARM64.targetOS target
    let syscalls = ARM64.targetSyscalls target

    match os with
    | Platform.MacOS ->
        [
            // Save X1 (caller-saved, may contain live value)
            // Allocate 32 bytes: 8 for X1, 8 for buffer, 16 for alignment
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 32us)
            ARM64.STR (ARM64.X1, ARM64.SP, 24s)  // Save X1 at SP+24

            // Call getentropy(buffer, 8)
            // X0 = buffer pointer (SP), X1 = length (8)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            ARM64.MOVZ (ARM64.X1, 8us, 0)
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Getrandom, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Load 8 bytes from buffer into X0
            ARM64.LDR (ARM64.X0, ARM64.SP, 0s)

            // Restore X1
            ARM64.LDR (ARM64.X1, ARM64.SP, 24s)

            // Cleanup stack
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 32us)

            // Move result to destination
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]
    | Platform.Linux ->
        [
            // Save X1, X2, X8 (caller-saved, may contain live values)
            // Allocate 48 bytes: 8 for buffer, 8 each for X1/X2/X8 = 32, 16 for alignment
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 48us)
            ARM64.STR (ARM64.X1, ARM64.SP, 40s)  // Save X1 at SP+40
            ARM64.STR (ARM64.X2, ARM64.SP, 32s)  // Save X2 at SP+32
            ARM64.STR (ARM64.X8, ARM64.SP, 24s)  // Save X8 at SP+24

            // Call getrandom(buffer, 8, flags=0)
            // X0 = buffer pointer (SP), X1 = length (8), X2 = flags (0)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            ARM64.MOVZ (ARM64.X1, 8us, 0)
            ARM64.MOVZ (ARM64.X2, 0us, 0)
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Getrandom, 0)
            ARM64.SVC syscalls.SvcImmediate

            // Load 8 bytes from buffer into X0
            ARM64.LDR (ARM64.X0, ARM64.SP, 0s)

            // Restore X1, X2, X8
            ARM64.LDR (ARM64.X1, ARM64.SP, 40s)
            ARM64.LDR (ARM64.X2, ARM64.SP, 32s)
            ARM64.LDR (ARM64.X8, ARM64.SP, 24s)

            // Cleanup stack
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 48us)

            // Move result to destination
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]

/// Generate ARM64 instructions to get the current UTC instant as 100ns Unix ticks.
/// destReg: destination register for the timestamp
/// Uses gettimeofday (macOS) or clock_gettime (Linux) syscall
/// Note: This function saves/restores caller-saved registers that may contain live values.
let generateDateTimeNow (target: ARM64.TargetConfig) (destReg: ARM64.Reg) : ARM64.Instr list =
    let os = ARM64.targetOS target
    let syscalls = ARM64.targetSyscalls target

    match os with
    | Platform.MacOS ->
        [
            // Preserve the scratch registers around the syscall and conversion.
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 48us)
            ARM64.STR (ARM64.X1, ARM64.SP, 40s)
            ARM64.STR (ARM64.X2, ARM64.SP, 32s)

            // Call gettimeofday(tv, NULL)
            // X0 = timeval pointer (SP), X1 = timezone (NULL)
            ARM64.MOV_reg (ARM64.X0, ARM64.SP)
            ARM64.MOVZ (ARM64.X1, 0us, 0)  // NULL timezone
            ARM64.MOVZ (ARM64.X16, syscalls.Numbers.Gettimeofday, 0)
            ARM64.SVC syscalls.SvcImmediate

            // ticks = tv_sec * 10_000_000 + tv_usec * 10
            ARM64.LDR (ARM64.X0, ARM64.SP, 0s)
            ARM64.LDR (ARM64.X1, ARM64.SP, 8s)
        ] @ generateLoadUInt64Immediate ARM64.X2 10000000UL @ [
            ARM64.MUL (ARM64.X0, ARM64.X0, ARM64.X2)
            ARM64.MOVZ (ARM64.X2, 10us, 0)
            ARM64.MUL (ARM64.X1, ARM64.X1, ARM64.X2)
            ARM64.ADD_reg (ARM64.X0, ARM64.X0, ARM64.X1)

            ARM64.LDR (ARM64.X1, ARM64.SP, 40s)
            ARM64.LDR (ARM64.X2, ARM64.SP, 32s)

            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 48us)

            // Move result to destination
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]
    | Platform.Linux ->
        [
            // Save X1, X2, X8 (caller-saved, may contain live values)
            // Allocate 48 bytes: 16 for timespec struct (tv_sec, tv_nsec), 8 each for X1/X2/X8 = 24, 8 for alignment
            ARM64.SUB_imm (ARM64.SP, ARM64.SP, 48us)
            ARM64.STR (ARM64.X1, ARM64.SP, 40s)  // Save X1 at SP+40
            ARM64.STR (ARM64.X2, ARM64.SP, 32s)  // Save X2 at SP+32
            ARM64.STR (ARM64.X8, ARM64.SP, 24s)  // Save X8 at SP+24

            // Call clock_gettime(CLOCK_REALTIME, ts)
            // X0 = clock_id (0 = CLOCK_REALTIME), X1 = timespec pointer (SP)
            ARM64.MOVZ (ARM64.X0, 0us, 0)  // CLOCK_REALTIME = 0
            ARM64.MOV_reg (ARM64.X1, ARM64.SP)
            ARM64.MOVZ (ARM64.X8, syscalls.Numbers.Gettimeofday, 0)
            ARM64.SVC syscalls.SvcImmediate

            // ticks = tv_sec * 10_000_000 + tv_nsec / 100
            ARM64.LDR (ARM64.X0, ARM64.SP, 0s)
            ARM64.LDR (ARM64.X1, ARM64.SP, 8s)
        ] @ generateLoadUInt64Immediate ARM64.X2 10000000UL @ [
            ARM64.MUL (ARM64.X0, ARM64.X0, ARM64.X2)
            ARM64.MOVZ (ARM64.X2, 100us, 0)
            ARM64.UDIV (ARM64.X1, ARM64.X1, ARM64.X2)
            ARM64.ADD_reg (ARM64.X0, ARM64.X0, ARM64.X1)

            // Restore X1, X2, X8
            ARM64.LDR (ARM64.X1, ARM64.SP, 40s)
            ARM64.LDR (ARM64.X2, ARM64.SP, 32s)
            ARM64.LDR (ARM64.X8, ARM64.SP, 24s)

            // Cleanup stack
            ARM64.ADD_imm (ARM64.SP, ARM64.SP, 48us)

            // Move result to destination
            ARM64.MOV_reg (destReg, ARM64.X0)
        ]
