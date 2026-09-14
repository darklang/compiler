// RunProcess.fs - Generate process execution with captured output.

module ARM64RunProcess

open ARM64Operands

/// Linux AArch64 argv runner. The request contains packed NUL-separated argv,
/// an optional cwd/environment overlay or a second argv for a pipeline.
/// Executables are resolved portably before this boundary, so the child can
/// call execve directly without a shell or utility process.
let internal generateLinuxCliRunProcessHelper () : ARM64Symbolic.Instr list =
    let syscall number =
        [ARM64Symbolic.MOVZ (ARM64Symbolic.X8, number, 0)
         ARM64Symbolic.SVC 0us]
    let zero reg = ARM64Symbolic.MOVZ (reg, 0us, 0)
    let pairFd slot shift =
        [ ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, slot)
          ARM64Symbolic.LSR_imm (ARM64Symbolic.X0, ARM64Symbolic.X0, shift)
          ARM64Symbolic.AND_imm (ARM64Symbolic.X0, ARM64Symbolic.X0, 0xffffffffUL) ]
    let closeFd slot shift = pairFd slot shift @ syscall 57us
    let setNonblocking slot =
        pairFd slot 0
        @ [ ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 4us, 0)
            ARM64Symbolic.MOVZ (ARM64Symbolic.X2, 2048us, 0) ]
        @ syscall 25us
    let readPipe slot buffer lengthReg nextLabel =
        pairFd slot 0
        @ [ ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, buffer, 16us)
            ARM64Symbolic.ADD_reg (ARM64Symbolic.X1, ARM64Symbolic.X1, lengthReg) ]
        @ loadImmediate ARM64Symbolic.X2 1048576L
        @ [ ARM64Symbolic.SUB_reg (ARM64Symbolic.X2, ARM64Symbolic.X2, lengthReg) ]
        @ syscall 63us
        @ [ ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
            ARM64Symbolic.B_cond_label (ARM64Symbolic.LE, nextLabel)
            ARM64Symbolic.ADD_reg (lengthReg, lengthReg, ARM64Symbolic.X0) ]
    let finalizeString buffer lengthReg =
        [ ARM64Symbolic.STR (lengthReg, buffer, 8s)
          ARM64Symbolic.MOVZ (ARM64Symbolic.X10, 1us, 0)
          ARM64Symbolic.STR (ARM64Symbolic.X10, buffer, 0s)
        ]
    [ ARM64Symbolic.Label "__dark_cli_run_process"
      ARM64Symbolic.STP_pre (ARM64Symbolic.X29, ARM64Symbolic.X30, ARM64Symbolic.SP, -16s)
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X29, ARM64Symbolic.SP)
      ARM64Symbolic.STP_pre (ARM64Symbolic.X19, ARM64Symbolic.X20, ARM64Symbolic.SP, -16s)
      ARM64Symbolic.STP_pre (ARM64Symbolic.X21, ARM64Symbolic.X22, ARM64Symbolic.SP, -16s)
      ARM64Symbolic.STP_pre (ARM64Symbolic.X23, ARM64Symbolic.X24, ARM64Symbolic.SP, -16s)
      ARM64Symbolic.STP_pre (ARM64Symbolic.X25, ARM64Symbolic.X26, ARM64Symbolic.SP, -16s)
      ARM64Symbolic.SUB_imm (ARM64Symbolic.SP, ARM64Symbolic.SP, 128us)
      // ENOENT is recoverable policy input for PATH lookup. Remember the
      // scratch-allocation boundary so a failed candidate attempt can return
      // empty strings without permanently consuming its two 1 MiB buffers.
      ARM64Symbolic.STR (ARM64Symbolic.X28, ARM64Symbolic.SP, 96s)
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X25, ARM64Symbolic.X0)
      // Copy packed argv and add the terminating NUL required by execve.
      ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.X25, 8s)
      ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.X0, 8s)
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X19, ARM64Symbolic.X28)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X0, 16us)
      zero ARM64Symbolic.X12
      ARM64Symbolic.Label "__dark_run_argv_copy"
      ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_run_argv_copied")
      ARM64Symbolic.LDRB (ARM64Symbolic.X13, ARM64Symbolic.X11, ARM64Symbolic.X12)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X12)
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X13, ARM64Symbolic.X14)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.B_label "__dark_run_argv_copy"
      ARM64Symbolic.Label "__dark_run_argv_copied"
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X10)
      zero ARM64Symbolic.X15
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X15, ARM64Symbolic.X14)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X10, 8us)
      ARM64Symbolic.LSR_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.LSL_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X11)
      // Build argv pointers from separators in the copied buffer.
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X24, ARM64Symbolic.X28)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X10, 2us)
      ARM64Symbolic.LSL_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X11)
      zero ARM64Symbolic.X12
      zero ARM64Symbolic.X13
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X14, ARM64Symbolic.X24)
      ARM64Symbolic.STR (ARM64Symbolic.X19, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.Label "__dark_run_argv_scan"
      ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_run_argv_done")
      ARM64Symbolic.LDRB (ARM64Symbolic.X15, ARM64Symbolic.X19, ARM64Symbolic.X12)
      ARM64Symbolic.CBNZ (ARM64Symbolic.X15, "__dark_run_argv_next")
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 8us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X24, ARM64Symbolic.X13)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X15, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X15, ARM64Symbolic.X19, ARM64Symbolic.X15)
      ARM64Symbolic.STR (ARM64Symbolic.X15, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.Label "__dark_run_argv_next"
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.B_label "__dark_run_argv_scan"
      ARM64Symbolic.Label "__dark_run_argv_done"
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 8us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X24, ARM64Symbolic.X13)
      zero ARM64Symbolic.X15
      ARM64Symbolic.STR (ARM64Symbolic.X15, ARM64Symbolic.X14, 0s)
      // Pipeline mode carries a second packed argv. Build its independent
      // native buffer and pointer vector before either child is created.
      ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.X25, 0s)
      ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 4us)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_pipeline_argv_done")
      ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.X25, 40s)
      ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.X0, 8s)
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X19, ARM64Symbolic.X28)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X0, 16us)
      zero ARM64Symbolic.X12
      ARM64Symbolic.Label "__dark_run_pipeline_argv_copy"
      ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_run_pipeline_argv_copied")
      ARM64Symbolic.LDRB (ARM64Symbolic.X13, ARM64Symbolic.X11, ARM64Symbolic.X12)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X12)
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X13, ARM64Symbolic.X14)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.B_label "__dark_run_pipeline_argv_copy"
      ARM64Symbolic.Label "__dark_run_pipeline_argv_copied"
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X10)
      zero ARM64Symbolic.X15
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X15, ARM64Symbolic.X14)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X10, 8us)
      ARM64Symbolic.LSR_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.LSL_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X11)
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X11, ARM64Symbolic.X28)
      ARM64Symbolic.STR (ARM64Symbolic.X11, ARM64Symbolic.SP, 104s)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X10, 2us)
      ARM64Symbolic.LSL_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 3)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X13)
      zero ARM64Symbolic.X12
      zero ARM64Symbolic.X13
      ARM64Symbolic.STR (ARM64Symbolic.X19, ARM64Symbolic.X11, 0s)
      ARM64Symbolic.Label "__dark_run_pipeline_argv_scan"
      ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_run_pipeline_argv_terminated")
      ARM64Symbolic.LDRB (ARM64Symbolic.X15, ARM64Symbolic.X19, ARM64Symbolic.X12)
      ARM64Symbolic.CBNZ (ARM64Symbolic.X15, "__dark_run_pipeline_argv_next")
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 8us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X11, ARM64Symbolic.X13)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X15, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X15, ARM64Symbolic.X19, ARM64Symbolic.X15)
      ARM64Symbolic.STR (ARM64Symbolic.X15, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.Label "__dark_run_pipeline_argv_next"
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.B_label "__dark_run_pipeline_argv_scan"
      ARM64Symbolic.Label "__dark_run_pipeline_argv_terminated"
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 8us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X11, ARM64Symbolic.X13)
      zero ARM64Symbolic.X15
      ARM64Symbolic.STR (ARM64Symbolic.X15, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.Label "__dark_run_pipeline_argv_done"
      // Locate the inherited envp from _start's root frame.
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X14, ARM64Symbolic.X29)
      ARM64Symbolic.Label "__dark_run_find_root"
      ARM64Symbolic.LDR (ARM64Symbolic.X13, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.CBZ (ARM64Symbolic.X13, "__dark_run_root_found")
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X14, ARM64Symbolic.X13)
      ARM64Symbolic.B_label "__dark_run_find_root"
      ARM64Symbolic.Label "__dark_run_root_found"
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X14, ARM64Symbolic.X14, 24us)
      ARM64Symbolic.Label "__dark_run_find_envp"
      ARM64Symbolic.LDR (ARM64Symbolic.X13, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X14, ARM64Symbolic.X14, 8us)
      ARM64Symbolic.CBNZ (ARM64Symbolic.X13, "__dark_run_find_envp")
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X26, ARM64Symbolic.X14)
      ARM64Symbolic.STR (ARM64Symbolic.X26, ARM64Symbolic.SP, 88s)
      // Prepend packed environment overrides to inherited envp. libc getenv
      // observes the first matching entry, so an override wins even when the
      // inherited vector also contains that name.
      ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.X25, 24s)
      ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.X0, 8s)
      ARM64Symbolic.CBZ (ARM64Symbolic.X10, "__dark_run_environment_done")
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X19, ARM64Symbolic.X28)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X0, 16us)
      zero ARM64Symbolic.X12
      ARM64Symbolic.Label "__dark_run_environment_copy"
      ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_run_environment_copied")
      ARM64Symbolic.LDRB (ARM64Symbolic.X13, ARM64Symbolic.X11, ARM64Symbolic.X12)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X12)
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X13, ARM64Symbolic.X14)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.B_label "__dark_run_environment_copy"
      ARM64Symbolic.Label "__dark_run_environment_copied"
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X19, ARM64Symbolic.X10)
      zero ARM64Symbolic.X15
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X15, ARM64Symbolic.X14)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X10, 8us)
      ARM64Symbolic.LSR_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.LSL_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X11)
      // Count inherited entries to reserve the complete pointer vector.
      ARM64Symbolic.LDR (ARM64Symbolic.X11, ARM64Symbolic.SP, 88s)
      zero ARM64Symbolic.X12
      ARM64Symbolic.Label "__dark_run_environment_count"
      ARM64Symbolic.LDR (ARM64Symbolic.X13, ARM64Symbolic.X11, 0s)
      ARM64Symbolic.CBZ (ARM64Symbolic.X13, "__dark_run_environment_counted")
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 8us)
      ARM64Symbolic.B_label "__dark_run_environment_count"
      ARM64Symbolic.Label "__dark_run_environment_counted"
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X26, ARM64Symbolic.X28)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X11, ARM64Symbolic.X10, ARM64Symbolic.X12)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 2us)
      ARM64Symbolic.LSL_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 3)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X11)
      // Build pointers for each packed override.
      zero ARM64Symbolic.X12
      zero ARM64Symbolic.X13
      ARM64Symbolic.STR (ARM64Symbolic.X19, ARM64Symbolic.X26, 0s)
      ARM64Symbolic.Label "__dark_run_environment_scan"
      ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_run_environment_append_inherited")
      ARM64Symbolic.LDRB (ARM64Symbolic.X15, ARM64Symbolic.X19, ARM64Symbolic.X12)
      ARM64Symbolic.CBNZ (ARM64Symbolic.X15, "__dark_run_environment_next")
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 8us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X26, ARM64Symbolic.X13)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X15, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X15, ARM64Symbolic.X19, ARM64Symbolic.X15)
      ARM64Symbolic.STR (ARM64Symbolic.X15, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.Label "__dark_run_environment_next"
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.B_label "__dark_run_environment_scan"
      ARM64Symbolic.Label "__dark_run_environment_append_inherited"
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 8us)
      ARM64Symbolic.LDR (ARM64Symbolic.X11, ARM64Symbolic.SP, 88s)
      ARM64Symbolic.Label "__dark_run_environment_append_next"
      ARM64Symbolic.LDR (ARM64Symbolic.X15, ARM64Symbolic.X11, 0s)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X14, ARM64Symbolic.X26, ARM64Symbolic.X13)
      ARM64Symbolic.STR (ARM64Symbolic.X15, ARM64Symbolic.X14, 0s)
      ARM64Symbolic.CBZ (ARM64Symbolic.X15, "__dark_run_environment_done")
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X13, 8us)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X11, ARM64Symbolic.X11, 8us)
      ARM64Symbolic.B_label "__dark_run_environment_append_next"
      ARM64Symbolic.Label "__dark_run_environment_done"
      ARM64Symbolic.LDR (ARM64Symbolic.X19, ARM64Symbolic.X24, 0s)
      // Copy cwd to a native NUL-terminated buffer.
      ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.X25, 16s)
      ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.X0, 8s)
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X11, ARM64Symbolic.X28)
      ARM64Symbolic.STR (ARM64Symbolic.X11, ARM64Symbolic.SP, 72s)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X13, ARM64Symbolic.X0, 16us)
      zero ARM64Symbolic.X12
      ARM64Symbolic.Label "__dark_run_cwd_copy"
      ARM64Symbolic.CMP_reg (ARM64Symbolic.X12, ARM64Symbolic.X10)
      ARM64Symbolic.B_cond_label (ARM64Symbolic.GE, "__dark_run_cwd_done")
      ARM64Symbolic.LDRB (ARM64Symbolic.X14, ARM64Symbolic.X13, ARM64Symbolic.X12)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X15, ARM64Symbolic.X11, ARM64Symbolic.X12)
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X14, ARM64Symbolic.X15)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X12, 1us)
      ARM64Symbolic.B_label "__dark_run_cwd_copy"
      ARM64Symbolic.Label "__dark_run_cwd_done"
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X15, ARM64Symbolic.X11, ARM64Symbolic.X10)
      zero ARM64Symbolic.X14
      ARM64Symbolic.STRB_reg (ARM64Symbolic.X14, ARM64Symbolic.X15)
      ARM64Symbolic.ADD_imm (ARM64Symbolic.X10, ARM64Symbolic.X10, 8us)
      ARM64Symbolic.LSR_imm (ARM64Symbolic.X10, ARM64Symbolic.X10, 3)
      ARM64Symbolic.LSL_imm (ARM64Symbolic.X10, ARM64Symbolic.X10, 3)
      ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X10)
      // Reserve managed output buffers.
      ARM64Symbolic.MOV_reg (ARM64Symbolic.X20, ARM64Symbolic.X28) ]
    @ loadImmediate ARM64Symbolic.X10 1048592L
    @ [ ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X10)
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X21, ARM64Symbolic.X28)
        ARM64Symbolic.ADD_reg (ARM64Symbolic.X28, ARM64Symbolic.X28, ARM64Symbolic.X10)
        zero ARM64Symbolic.X22
        zero ARM64Symbolic.X23
        zero ARM64Symbolic.X9
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 56s)
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 64s)
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 80s)
        // stdout and stderr pipes
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 0us)
        zero ARM64Symbolic.X1 ]
    @ syscall 59us
    @ [ ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_spawn_error")
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 8us)
        zero ARM64Symbolic.X1 ]
    @ syscall 59us
    @ [ ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_spawn_error_close_stdout")
        // A close-on-exec pipe reports child setup/exec errno to the parent.
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 16us) ]
    @ loadImmediate ARM64Symbolic.X1 524288L
    @ syscall 59us
    @ [ ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_spawn_error_close_output")
        // The producer and consumer share this pipe only in pipeline mode. It
        // is created unconditionally so every child/error path has one stable
        // descriptor layout.
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 112us)
        zero ARM64Symbolic.X1 ]
    @ syscall 59us
    @ [ ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_spawn_error_close_errno")
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.X25, 0s)
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 4us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_clone_consumer")
        ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 17us, 0)
        zero ARM64Symbolic.X1; zero ARM64Symbolic.X2; zero ARM64Symbolic.X3; zero ARM64Symbolic.X4 ]
    @ syscall 220us
    @ [ ARM64Symbolic.CBZ (ARM64Symbolic.X0, "__dark_run_pipeline_producer")
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_spawn_error_close_pipeline")
        ARM64Symbolic.STR (ARM64Symbolic.X0, ARM64Symbolic.SP, 120s)
        ARM64Symbolic.Label "__dark_run_clone_consumer"
        ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 17us, 0)
        zero ARM64Symbolic.X1; zero ARM64Symbolic.X2; zero ARM64Symbolic.X3; zero ARM64Symbolic.X4 ]
    @ syscall 220us
    @ [ ARM64Symbolic.CBZ (ARM64Symbolic.X0, "__dark_run_child")
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_spawn_error_close_all")
        ARM64Symbolic.STR (ARM64Symbolic.X0, ARM64Symbolic.SP, 32s)
        zero ARM64Symbolic.X9
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 24s) ]
    @ closeFd 0s 32 @ closeFd 8s 32 @ closeFd 16s 32 @ closeFd 112s 0 @ closeFd 112s 32
    @ setNonblocking 0s @ setNonblocking 8s
    @ [ ARM64Symbolic.Label "__dark_run_drain_wait" ]
    @ readPipe 0s ARM64Symbolic.X20 ARM64Symbolic.X22 "__dark_run_read_stderr"
    @ [ ARM64Symbolic.Label "__dark_run_read_stderr" ]
    @ readPipe 8s ARM64Symbolic.X21 ARM64Symbolic.X23 "__dark_run_wait"
    @ [ ARM64Symbolic.Label "__dark_run_wait"
        ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, 32s)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.SP, 24us)
        ARM64Symbolic.MOVZ (ARM64Symbolic.X2, 1us, 0)
        zero ARM64Symbolic.X3 ]
    @ syscall 260us
    @ [ ARM64Symbolic.CBNZ (ARM64Symbolic.X0, "__dark_run_finished")
        // Timeout mode decrements one millisecond per wait probe.
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.X25, 0s)
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 3us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_sleep")
        ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.SP, 56s)
        ARM64Symbolic.LDR (ARM64Symbolic.X11, ARM64Symbolic.X25, 32s)
        ARM64Symbolic.CMP_reg (ARM64Symbolic.X10, ARM64Symbolic.X11)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_timeout_next")
        ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, 32s)
        ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 9us, 0) ]
    @ syscall 129us
    @ [ ARM64Symbolic.MOVZ (ARM64Symbolic.X9, 1us, 0)
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 64s)
        ARM64Symbolic.B_label "__dark_run_blocking_wait"
        ARM64Symbolic.Label "__dark_run_timeout_next"
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X10, ARM64Symbolic.X10, 1us)
        ARM64Symbolic.STR (ARM64Symbolic.X10, ARM64Symbolic.SP, 56s)
        ARM64Symbolic.Label "__dark_run_sleep"
        zero ARM64Symbolic.X9
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 40s) ]
    @ loadImmediate ARM64Symbolic.X9 1000000L
    @ [ ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.SP, 48s)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X0, ARM64Symbolic.SP, 40us)
        zero ARM64Symbolic.X1 ]
    @ syscall 101us
    @ [ ARM64Symbolic.B_label "__dark_run_drain_wait"
        ARM64Symbolic.Label "__dark_run_blocking_wait"
        ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, 32s)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.SP, 24us)
        zero ARM64Symbolic.X2; zero ARM64Symbolic.X3 ]
    @ syscall 260us
    @ [ ARM64Symbolic.Label "__dark_run_finished"
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.X25, 0s)
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 4us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_all_children_finished")
        ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, 120s)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.SP, 80us)
        zero ARM64Symbolic.X2; zero ARM64Symbolic.X3 ]
    @ syscall 260us
    @ [ ARM64Symbolic.Label "__dark_run_all_children_finished" ]
    @ readPipe 0s ARM64Symbolic.X20 ARM64Symbolic.X22 "__dark_run_final_stderr"
    @ [ ARM64Symbolic.Label "__dark_run_final_stderr" ]
    @ readPipe 8s ARM64Symbolic.X21 ARM64Symbolic.X23 "__dark_run_final_close"
    @ [ ARM64Symbolic.Label "__dark_run_final_close" ]
    @ closeFd 0s 0 @ closeFd 8s 0
    @ pairFd 16s 0
    @ [ ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.SP, 80us)
        ARM64Symbolic.MOVZ (ARM64Symbolic.X2, 8us, 0) ]
    @ syscall 63us
    @ closeFd 16s 0
    @ [ ARM64Symbolic.LDR (ARM64Symbolic.X10, ARM64Symbolic.SP, 24s)
        ARM64Symbolic.AND_imm (ARM64Symbolic.X11, ARM64Symbolic.X10, 0x7fUL)
        ARM64Symbolic.CBNZ (ARM64Symbolic.X11, "__dark_run_signaled")
        ARM64Symbolic.LSR_imm (ARM64Symbolic.X12, ARM64Symbolic.X10, 8)
        ARM64Symbolic.B_label "__dark_run_build_result"
        ARM64Symbolic.Label "__dark_run_signaled"
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X12, ARM64Symbolic.X11, 128us)
        ARM64Symbolic.B_label "__dark_run_build_result"
        ARM64Symbolic.Label "__dark_run_spawn_error_close_all" ]
    @ closeFd 112s 0 @ closeFd 112s 32
    @ [ ARM64Symbolic.Label "__dark_run_spawn_error_close_pipeline" ]
    @ [ ARM64Symbolic.Label "__dark_run_spawn_error_close_errno" ]
    @ closeFd 16s 0 @ closeFd 16s 32
    @ [ ARM64Symbolic.Label "__dark_run_spawn_error_close_output" ]
    @ closeFd 8s 0 @ closeFd 8s 32
    @ [ ARM64Symbolic.Label "__dark_run_spawn_error_close_stdout" ]
    @ closeFd 0s 0 @ closeFd 0s 32
    @ [ ARM64Symbolic.Label "__dark_run_spawn_error"
        ARM64Symbolic.MOVZ (ARM64Symbolic.X12, 127us, 0)
        ARM64Symbolic.Label "__dark_run_build_result"
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.SP, 80s)
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 2us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_keep_capture_buffers")
        ARM64Symbolic.LDR (ARM64Symbolic.X28, ARM64Symbolic.SP, 96s)
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X20, ARM64Symbolic.X28)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X28, ARM64Symbolic.X28, 16us)
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X21, ARM64Symbolic.X28)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X28, ARM64Symbolic.X28, 16us)
        zero ARM64Symbolic.X22
        zero ARM64Symbolic.X23
        ARM64Symbolic.Label "__dark_run_keep_capture_buffers" ]
    @ finalizeString ARM64Symbolic.X20 ARM64Symbolic.X22
    @ finalizeString ARM64Symbolic.X21 ARM64Symbolic.X23
    @ [ ARM64Symbolic.MOV_reg (ARM64Symbolic.X0, ARM64Symbolic.X28)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.X28, ARM64Symbolic.X28, 48us)
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.SP, 80s)
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.X0, 0s)
        ARM64Symbolic.STR (ARM64Symbolic.X12, ARM64Symbolic.X0, 8s)
        ARM64Symbolic.STR (ARM64Symbolic.X20, ARM64Symbolic.X0, 16s)
        ARM64Symbolic.STR (ARM64Symbolic.X21, ARM64Symbolic.X0, 24s)
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.SP, 64s)
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.X0, 32s)
        ARM64Symbolic.MOVZ (ARM64Symbolic.X9, 1us, 0)
        ARM64Symbolic.STR (ARM64Symbolic.X9, ARM64Symbolic.X0, 40s)
        ARM64Symbolic.ADD_imm (ARM64Symbolic.SP, ARM64Symbolic.SP, 128us)
        ARM64Symbolic.LDP_post (ARM64Symbolic.X25, ARM64Symbolic.X26, ARM64Symbolic.SP, 16s)
        ARM64Symbolic.LDP_post (ARM64Symbolic.X23, ARM64Symbolic.X24, ARM64Symbolic.SP, 16s)
        ARM64Symbolic.LDP_post (ARM64Symbolic.X21, ARM64Symbolic.X22, ARM64Symbolic.SP, 16s)
        ARM64Symbolic.LDP_post (ARM64Symbolic.X19, ARM64Symbolic.X20, ARM64Symbolic.SP, 16s)
        ARM64Symbolic.LDP_post (ARM64Symbolic.X29, ARM64Symbolic.X30, ARM64Symbolic.SP, 16s)
        ARM64Symbolic.RET
        ARM64Symbolic.Label "__dark_run_child" ]
    @ pairFd 0s 32
    @ [ ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 1us, 0); zero ARM64Symbolic.X2 ]
    @ syscall 24us
    @ pairFd 8s 32
    @ [ ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 2us, 0); zero ARM64Symbolic.X2 ]
    @ syscall 24us
    @ [ ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.X25, 0s)
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 4us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_child_input_ready") ]
    @ pairFd 112s 0
    @ [ zero ARM64Symbolic.X1; zero ARM64Symbolic.X2 ]
    @ syscall 24us
    @ [ ARM64Symbolic.Label "__dark_run_child_input_ready" ]
    @ closeFd 0s 0 @ closeFd 0s 32 @ closeFd 8s 0 @ closeFd 8s 32 @ closeFd 16s 0 @ closeFd 112s 0 @ closeFd 112s 32
    @ [ // Apply cwd only for runIn mode.
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.X25, 0s)
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 1us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_child_exec")
        ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.SP, 72s) ]
    @ syscall 49us
    @ [ ARM64Symbolic.CMP_imm (ARM64Symbolic.X0, 0us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.LT, "__dark_run_child_fail")
        ARM64Symbolic.Label "__dark_run_child_exec"
        ARM64Symbolic.LDR (ARM64Symbolic.X9, ARM64Symbolic.X25, 0s)
        ARM64Symbolic.CMP_imm (ARM64Symbolic.X9, 4us)
        ARM64Symbolic.B_cond_label (ARM64Symbolic.NE, "__dark_run_child_first_argv")
        ARM64Symbolic.LDR (ARM64Symbolic.X1, ARM64Symbolic.SP, 104s)
        ARM64Symbolic.LDR (ARM64Symbolic.X0, ARM64Symbolic.X1, 0s)
        ARM64Symbolic.B_label "__dark_run_child_argv_ready"
        ARM64Symbolic.Label "__dark_run_child_first_argv"
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X0, ARM64Symbolic.X19)
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X1, ARM64Symbolic.X24)
        ARM64Symbolic.Label "__dark_run_child_argv_ready"
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X2, ARM64Symbolic.X26) ]
    @ syscall 221us
    @ [ ARM64Symbolic.Label "__dark_run_child_fail"
        // Linux syscalls return -errno. Preserve errno before write changes X0.
        zero ARM64Symbolic.X10
        ARM64Symbolic.SUB_reg (ARM64Symbolic.X10, ARM64Symbolic.X10, ARM64Symbolic.X0)
        ARM64Symbolic.STR (ARM64Symbolic.X10, ARM64Symbolic.SP, 80s) ]
    @ pairFd 16s 32
    @ [ ARM64Symbolic.ADD_imm (ARM64Symbolic.X1, ARM64Symbolic.SP, 80us)
        ARM64Symbolic.MOVZ (ARM64Symbolic.X2, 8us, 0) ]
    @ syscall 64us
    @ [
        ARM64Symbolic.MOVZ (ARM64Symbolic.X0, 127us, 0) ]
    @ syscall 93us
    @ [ ARM64Symbolic.Label "__dark_run_pipeline_producer" ]
    @ pairFd 112s 32
    @ [ ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 1us, 0); zero ARM64Symbolic.X2 ]
    @ syscall 24us
    @ pairFd 8s 32
    @ [ ARM64Symbolic.MOVZ (ARM64Symbolic.X1, 2us, 0); zero ARM64Symbolic.X2 ]
    @ syscall 24us
    @ closeFd 0s 0 @ closeFd 0s 32 @ closeFd 8s 0 @ closeFd 8s 32 @ closeFd 16s 0 @ closeFd 112s 0 @ closeFd 112s 32
    @ [ ARM64Symbolic.MOV_reg (ARM64Symbolic.X0, ARM64Symbolic.X19)
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X1, ARM64Symbolic.X24)
        ARM64Symbolic.MOV_reg (ARM64Symbolic.X2, ARM64Symbolic.X26) ]
    @ syscall 221us
    @ [ ARM64Symbolic.B_label "__dark_run_child_fail" ]
