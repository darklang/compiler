// ExecuteProcess.fs - Generate process replacement runtime support.

module ARM64ExecuteProcess

open ARM64HeapAllocation
open ARM64Operands

let internal generateLinuxCliExecuteHelper () : ARM64Symbolic.Instr list =
    let syscall number =
        [ARM64Symbolic.MOVZ (ARM64Symbolic.X8, number, 0)
         ARM64Symbolic.SVC 0us]
    let zero reg = ARM64Symbolic.MOVZ (reg, 0us, 0)
    let pairFd slot shift =
        [ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, slot)
         ARM64Symbolic.LSR_imm (ARM64Symbolic.X0, ARM64Symbolic.X0, shift)]
    let closeFd slot shift = pairFd slot shift @ syscall 57us
    let setNonblocking slot =
        pairFd slot 0
        @ [ARM64Symbolic.AND_imm (ARM64Symbolic.X0, ARM64Symbolic.X0, 0xffffffffUL)
           ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 4us, 0)
           ARM64Symbolic.MOVZ (ARM64Symbolic.X2, 2048us, 0)]
        @ syscall 25us
    let readPipe slot buffer lengthReg nextLabel =
        pairFd slot 0
        @ [ARM64Symbolic.AND_imm (ARM64Symbolic.X0, ARM64Symbolic.X0, 0xffffffffUL)
           ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, buffer, 16us)
           ARM64Symbolic.ADD_reg (ARM64Symbolic.X1, ARM64Symbolic.X1, lengthReg)]
        @ loadImmediate ARM64Symbolic.X2 1048576L
        @ [ARM64Symbolic.SUB_reg (ARM64Symbolic.X2, ARM64Symbolic.X2, lengthReg)]
        @ syscall 63us
        @ [ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
           ARM64Symbolic.B_cond_label (ARM64Symbolic.LE, nextLabel)
           ARM64Symbolic.ADD_reg (lengthReg, lengthReg, ARM64Symbolic.X0)]
    let finalizeString buffer lengthReg =
        [ARM64Symbolic.MOVZ (ARM64Symbolic.X10, 1us, 0)
         ARM64Symbolic.STR (ARM64Symbolic.X10, buffer, 0s)
         ARM64Symbolic.STR (lengthReg, buffer, 8s)]
    [ARM64Symbolic.Label "__dark_cli_execute"
     ARM64Symbolic.STP_pre (ARM64Symbolic.X29, ARM64Symbolic.X30, ARM64Symbolic.SP, -16s)
     ARM64Symbolic.MOV_reg (ARM64Symbolic.X29, ARM64Symbolic.SP)
     ARM64Symbolic.STP_pre (ARM64Symbolic.X19, ARM64Symbolic.X20, ARM64Symbolic.SP, -16s)
     ARM64Symbolic.STP_pre (ARM64Symbolic.X21, ARM64Symbolic.X22, ARM64Symbolic.SP, -16s)
     ARM64Symbolic.STP_pre (ARM64Symbolic.X23, ARM64Symbolic.X30, ARM64Symbolic.SP, -16s)
     ARM64Symbolic.SUB_imm (ARM64Symbolic.SP, ARM64Symbolic.SP, 96us)
     // Copy the managed command to a NUL-terminated native buffer.
     ARM64Symbolic.MOV_reg (ARM64Symbolic.X19, ARM64Symbolic.X28)
     ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.X0, 8s)
     ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X0, 16us)
     zero ARM64Symbolic.X12
     ARM64Symbolic.Label "__dark_cli_command_copy"
     ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
     ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_cli_command_copied")
     ARM64Symbolic.LDRB (ARM64Symbolic.X13, ARM64Symbolic.X11, ARM64Symbolic.X12)
     ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X12)
     ARM64Symbolic.STRB_reg (ARM64Symbolic.X13, ARM64Symbolic.X14)
     ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
     ARM64Symbolic.B_label "__dark_cli_command_copy"
     ARM64Symbolic.Label "__dark_cli_command_copied"
     ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X10)
     zero ARM64Symbolic.X15
     ARM64Symbolic.STRB_reg (ARM64Symbolic.X15, ARM64Symbolic.X14)
     ARM64Symbolic.ADD_imm (ARM64Symbolic.X10, ARM64Symbolic.X10, 8us)
     ARM64Symbolic.LSR_imm (ARM64Symbolic.X10, ARM64Symbolic.X10, 3)
     ARM64Symbolic.LSL_imm (ARM64Symbolic.X10, ARM64Symbolic.X10, 3)
     ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X10)
     // Reserve two bounded managed string blocks.
     ARM64Symbolic.MOV_reg (ARM64Symbolic.X20, ARM64Symbolic.X28)]
    @ loadImmediate ARM64Symbolic.X10 1048592L
    @ [ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X10)
       ARM64Symbolic.MOV_reg (ARM64Symbolic.X21, ARM64Symbolic.X28)
       ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X10)
       zero ARM64Symbolic.X22; zero ARM64Symbolic.X23
       // pipe2(stdout), pipe2(stderr)
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 0us); zero ARM64Symbolic.X1]
    @ syscall 59us
    @ [ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_cli_spawn_error")
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 8us); zero ARM64Symbolic.X1]
    @ syscall 59us
    @ [ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_cli_spawn_error_close_stdout")
       // clone(SIGCHLD, 0, 0, 0, 0)
       ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 17us, 0)
       zero ARM64Symbolic.X1; zero ARM64Symbolic.X2; zero ARM64Symbolic.X3; zero ARM64Symbolic.X4]
    @ syscall 220us
    @ [ARM64Symbolic.CBZ (ARM64Symbolic.X0, "__dark_cli_child")
       ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_cli_spawn_error_close_all")
       ARM64Symbolic.STR (ARM64Symbolic.X0, ARM64Symbolic.SP, 32s)
       zero ARM64Symbolic.X9
       ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 16s)]
    @ closeFd 0s 32 @ closeFd 8s 32
    @ setNonblocking 0s @ setNonblocking 8s
    @ [ARM64Symbolic.Label "__dark_cli_drain_wait"]
    @ readPipe 0s ARM64Symbolic.X20 ARM64Symbolic.X22 "__dark_cli_read_stderr"
    @ [ARM64Symbolic.Label "__dark_cli_read_stderr"]
    @ readPipe 8s ARM64Symbolic.X21 ARM64Symbolic.X23 "__dark_cli_wait"
    @ [ARM64Symbolic.Label "__dark_cli_wait"
       ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, 32s)
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.SP, 16us)
       ARM64Symbolic.MOVZ (ARM64Symbolic.X2, 1us, 0); zero ARM64Symbolic.X3]
    @ syscall 260us
    @ [ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_cli_drain_wait")
       ARM64Symbolic.CBNZ (ARM64Symbolic.X0, "__dark_cli_finished")
       zero ARM64Symbolic.X9
       ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 40s)]
    @ loadImmediate ARM64Symbolic.X9 1000000L
    @ [ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 48s)
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 40us); zero ARM64Symbolic.X1]
    @ syscall 101us
    @ [ARM64Symbolic.B_label "__dark_cli_drain_wait"
       ARM64Symbolic.Label "__dark_cli_finished"]
    @ readPipe 0s ARM64Symbolic.X20 ARM64Symbolic.X22 "__dark_cli_final_stderr"
    @ [ARM64Symbolic.Label "__dark_cli_final_stderr"]
    @ readPipe 8s ARM64Symbolic.X21 ARM64Symbolic.X23 "__dark_cli_final_close"
    @ [ARM64Symbolic.Label "__dark_cli_final_close"]
    @ closeFd 0s 0 @ closeFd 8s 0
    @ [ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.SP, 16s)
       ARM64Symbolic.AND_imm (ARM64Symbolic.X11, ARM64Symbolic.X10, 0x7fUL)
       ARM64Symbolic.CBNZ (ARM64Symbolic.X11, "__dark_cli_signaled")
       ARM64Symbolic.LSR_imm (ARM64Symbolic.X12, ARM64Symbolic.X10, 8)
       ARM64Symbolic.B_label "__dark_cli_build_result"
       ARM64Symbolic.Label "__dark_cli_signaled"
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X11, 128us)
       ARM64Symbolic.B_label "__dark_cli_build_result"
       ARM64Symbolic.Label "__dark_cli_spawn_error_close_all"]
    @ closeFd 8s 0 @ closeFd 8s 32
    @ [ARM64Symbolic.Label "__dark_cli_spawn_error_close_stdout"]
    @ closeFd 0s 0 @ closeFd 0s 32
    @ [ARM64Symbolic.Label "__dark_cli_spawn_error"
       ARM64Symbolic.MOVZ (ARM64Symbolic.X12, 127us, 0)
       ARM64Symbolic.Label "__dark_cli_build_result"]
    @ finalizeString ARM64Symbolic.X20 ARM64Symbolic.X22
    @ finalizeString ARM64Symbolic.X21 ARM64Symbolic.X23
    @ [ARM64Symbolic.MOV_reg (ARM64Symbolic.X0, ARM64Symbolic.X28)
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X28, ARM64Symbolic.X28, 32us)]
    @ [ARM64Symbolic.STR (ARM64Symbolic.X12, ARM64Symbolic.X0, 0s)
       ARM64Symbolic.STR (ARM64Symbolic.X20, ARM64Symbolic.X0, 8s)
       ARM64Symbolic.STR (ARM64Symbolic.X21, ARM64Symbolic.X0, 16s)
       ARM64Symbolic.MOVZ (ARM64Symbolic.X10, 1us, 0)
       ARM64Symbolic.STR (ARM64Symbolic.X10, ARM64Symbolic.X0, 24s)
       ARM64Symbolic.ADD_imm (ARM64Symbolic.SP, ARM64Symbolic.SP, 96us)
       ARM64Symbolic.LDP_post (ARM64Symbolic.X23, ARM64Symbolic.X30, ARM64Symbolic.SP, 16s)
       ARM64Symbolic.LDP_post (ARM64Symbolic.X21, ARM64Symbolic.X22, ARM64Symbolic.SP, 16s)
       ARM64Symbolic.LDP_post (ARM64Symbolic.X19, ARM64Symbolic.X20, ARM64Symbolic.SP, 16s)
       ARM64Symbolic.LDP_post (ARM64Symbolic.X29, ARM64Symbolic.X30, ARM64Symbolic.SP, 16s)
       ARM64Symbolic.RET
       ARM64Symbolic.Label "__dark_cli_child"]
    @ pairFd 0s 32
    @ [ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 1us, 0); zero ARM64Symbolic.X2]
    @ syscall 24us
    @ pairFd 8s 32
    @ [ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 2us, 0); zero ARM64Symbolic.X2]
    @ syscall 24us
    @ closeFd 0s 0 @ closeFd 0s 32 @ closeFd 8s 0 @ closeFd 8s 32
    @ loadStringLiteralPointer ARM64Symbolic.X0 "/bin/bash"
    @ [ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.X0, 16us)
       ARM64Symbolic.MOV_reg (ARM64Symbolic.X14, ARM64Symbolic.X29)
       ARM64Symbolic.Label "__dark_cli_find_root_for_exec"
       ARM64Symbolic.LDR (ARM64Symbolic.X13, ARM64Symbolic.X14, 0s)
       ARM64Symbolic.CBZ (ARM64Symbolic.X13, "__dark_cli_exec_root_found")
       ARM64Symbolic.MOV_reg (ARM64Symbolic.X14, ARM64Symbolic.X13)
       ARM64Symbolic.B_label "__dark_cli_find_root_for_exec"
       ARM64Symbolic.Label "__dark_cli_exec_root_found"
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X14, ARM64Symbolic.X14, 24us)
       ARM64Symbolic.Label "__dark_cli_find_envp_for_exec"
       ARM64Symbolic.LDR (ARM64Symbolic.X13, ARM64Symbolic.X14, 0s)
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X14, ARM64Symbolic.X14, 8us)
       ARM64Symbolic.CBNZ (ARM64Symbolic.X13, "__dark_cli_find_envp_for_exec")
       ARM64Symbolic.STR (ARM64Symbolic.X14, ARM64Symbolic.SP, 88s)
       ARM64Symbolic.Label "__dark_cli_find_shell"
       ARM64Symbolic.LDR (ARM64Symbolic.X13, ARM64Symbolic.X14, 0s)
       ARM64Symbolic.CBZ (ARM64Symbolic.X13, "__dark_cli_shell_found")
       ARM64Symbolic.LDRB_imm (ARM64Symbolic.X10, ARM64Symbolic.X13, 0)
       ARM64Symbolic.CMP_imm (ARM64Symbolic.X10, 83us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_cli_next_env")
       ARM64Symbolic.LDRB_imm (ARM64Symbolic.X10, ARM64Symbolic.X13, 1)
       ARM64Symbolic.CMP_imm (ARM64Symbolic.X10, 72us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_cli_next_env")
       ARM64Symbolic.LDRB_imm (ARM64Symbolic.X10, ARM64Symbolic.X13, 2)
       ARM64Symbolic.CMP_imm (ARM64Symbolic.X10, 69us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_cli_next_env")
       ARM64Symbolic.LDRB_imm (ARM64Symbolic.X10, ARM64Symbolic.X13, 3)
       ARM64Symbolic.CMP_imm (ARM64Symbolic.X10, 76us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_cli_next_env")
       ARM64Symbolic.LDRB_imm (ARM64Symbolic.X10, ARM64Symbolic.X13, 4)
       ARM64Symbolic.CMP_imm (ARM64Symbolic.X10, 76us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_cli_next_env")
       ARM64Symbolic.LDRB_imm (ARM64Symbolic.X10, ARM64Symbolic.X13, 5)
       ARM64Symbolic.CMP_imm (ARM64Symbolic.X10, 61us)
       ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_cli_next_env")
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.X13, 6us)
       ARM64Symbolic.B_label "__dark_cli_shell_found"
       ARM64Symbolic.Label "__dark_cli_next_env"
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X14, ARM64Symbolic.X14, 8us)
       ARM64Symbolic.B_label "__dark_cli_find_shell"
       ARM64Symbolic.Label "__dark_cli_shell_found"
       ARM64Symbolic.STR (ARM64Symbolic.X0, ARM64Symbolic.SP, 56s)]
    @ loadStringLiteralPointer ARM64Symbolic.X9 "-c"
    @ [ARM64Symbolic.ADD_imm (ARM64Symbolic.X9, ARM64Symbolic.X9, 16us)
       ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 64s)
       ARM64Symbolic.STR (ARM64Symbolic.X19, ARM64Symbolic.SP, 72s)
       zero ARM64Symbolic.X9
       ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 80s)
       ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.SP, 56us)
       ARM64Symbolic.LDR (ARM64Symbolic.X2, ARM64Symbolic.SP, 88s)]
    @ syscall 221us
    @ [ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 127us, 0)]
    @ syscall 93us
