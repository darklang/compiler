// Peephole.fs - Optimize symbolic target instructions with register-lifetime checks.

module ARM64Peephole

open ARM64ListReferenceCounts

type private RegisterLifetimeStep =
    | Unrelated
    | Overwritten
    | ReadOrControlFlow

/// Classify one instruction while proving that a register value is dead.
/// Control flow is a conservative barrier because this local peephole does not
/// construct a CFG for the final symbolic instruction stream.
let private registerLifetimeStep
    (target: ARM64Symbolic.Reg)
    (instr: ARM64Symbolic.Instr)
    : RegisterLifetimeStep =
    let classify (reads: ARM64Symbolic.Reg list) (writes: ARM64Symbolic.Reg list) =
        if List.contains target reads then ReadOrControlFlow
        elif List.contains target writes then Overwritten
        else Unrelated

    match instr with
    | ARM64Symbolic.MOVZ (dest, _, _)
    | ARM64Symbolic.MOVN (dest, _, _)
    | ARM64Symbolic.CSET (dest, _)
    | ARM64Symbolic.ADRP (dest, _)
    | ARM64Symbolic.ADR (dest, _)
    | ARM64Symbolic.FMOV_to_gp (dest, _)
    | ARM64Symbolic.UMOV_byte (dest, _)
    | ARM64Symbolic.FCVTZS (dest, _) ->
        classify [] [dest]
    | ARM64Symbolic.MOVK (dest, _, _) ->
        classify [dest] [dest]
    | ARM64Symbolic.ADD_imm (dest, src, _)
    | ARM64Symbolic.SUB_imm (dest, src, _)
    | ARM64Symbolic.SUB_imm12 (dest, src, _)
    | ARM64Symbolic.SUBS_imm (dest, src, _)
    | ARM64Symbolic.AND_imm (dest, src, _)
    | ARM64Symbolic.LSL_imm (dest, src, _)
    | ARM64Symbolic.LSR_imm (dest, src, _)
    | ARM64Symbolic.ASR_imm (dest, src, _)
    | ARM64Symbolic.ADD_label (dest, src, _) ->
        classify [src] [dest]
    | ARM64Symbolic.MVN (dest, src)
    | ARM64Symbolic.MOV_reg (dest, src)
    | ARM64Symbolic.NEG (dest, src)
    | ARM64Symbolic.SXTB (dest, src)
    | ARM64Symbolic.SXTH (dest, src)
    | ARM64Symbolic.SXTW (dest, src)
    | ARM64Symbolic.UXTB (dest, src)
    | ARM64Symbolic.UXTH (dest, src)
    | ARM64Symbolic.UXTW (dest, src) ->
        classify [src] [dest]
    | ARM64Symbolic.ADD_reg (dest, src1, src2)
    | ARM64Symbolic.SUB_reg (dest, src1, src2)
    | ARM64Symbolic.MUL (dest, src1, src2)
    | ARM64Symbolic.SDIV (dest, src1, src2)
    | ARM64Symbolic.UDIV (dest, src1, src2)
    | ARM64Symbolic.AND_reg (dest, src1, src2)
    | ARM64Symbolic.BIC_reg (dest, src1, src2)
    | ARM64Symbolic.ORR_reg (dest, src1, src2)
    | ARM64Symbolic.EOR_reg (dest, src1, src2)
    | ARM64Symbolic.LSL_reg (dest, src1, src2)
    | ARM64Symbolic.LSR_reg (dest, src1, src2)
    | ARM64Symbolic.ASR_reg (dest, src1, src2) ->
        classify [src1; src2] [dest]
    | ARM64Symbolic.ADD_shifted (dest, src1, src2, _)
    | ARM64Symbolic.SUB_shifted (dest, src1, src2, _) ->
        classify [src1; src2] [dest]
    | ARM64Symbolic.MSUB (dest, src1, src2, src3)
    | ARM64Symbolic.MADD (dest, src1, src2, src3) ->
        classify [src1; src2; src3] [dest]
    | ARM64Symbolic.CMP_imm (src, _) ->
        classify [src] []
    | ARM64Symbolic.CMP_reg (src1, src2) ->
        classify [src1; src2] []
    | ARM64Symbolic.STRB (src, addr, _)
    | ARM64Symbolic.STR (src, addr, _)
    | ARM64Symbolic.STUR (src, addr, _) ->
        classify [src; addr] []
    | ARM64Symbolic.STRB_reg (src, addr) ->
        classify [src; addr] []
    | ARM64Symbolic.LDRB (dest, addr, index) ->
        classify [addr; index] [dest]
    | ARM64Symbolic.LDRB_imm (dest, addr, _)
    | ARM64Symbolic.LDR (dest, addr, _)
    | ARM64Symbolic.LDUR (dest, addr, _) ->
        classify [addr] [dest]
    | ARM64Symbolic.STP (reg1, reg2, addr, _) ->
        classify [reg1; reg2; addr] []
    | ARM64Symbolic.STP_pre (reg1, reg2, addr, _) ->
        classify [reg1; reg2; addr] [addr]
    | ARM64Symbolic.LDP (reg1, reg2, addr, _) ->
        classify [addr] [reg1; reg2]
    | ARM64Symbolic.LDP_post (reg1, reg2, addr, _) ->
        classify [addr] [reg1; reg2; addr]
    | ARM64Symbolic.LDR_fp (_, addr, _)
    | ARM64Symbolic.STR_fp (_, addr, _)
    | ARM64Symbolic.STP_fp (_, _, addr, _)
    | ARM64Symbolic.LDP_fp (_, _, addr, _) ->
        classify [addr] []
    | ARM64Symbolic.FMOV_from_gp (_, src)
    | ARM64Symbolic.SCVTF (_, src) ->
        classify [src] []
    | ARM64Symbolic.FADD _
    | ARM64Symbolic.FSUB _
    | ARM64Symbolic.FMUL _
    | ARM64Symbolic.FDIV _
    | ARM64Symbolic.FNEG _
    | ARM64Symbolic.FABS _
    | ARM64Symbolic.FSQRT _
    | ARM64Symbolic.FCMP _
    | ARM64Symbolic.FMOV_reg _
    | ARM64Symbolic.FMOV_zero _
    | ARM64Symbolic.FMOV_imm _
    | ARM64Symbolic.CNT_8B _
    | ARM64Symbolic.ADDV_8B _ ->
        Unrelated
    | ARM64Symbolic.BL _
    | ARM64Symbolic.BLR _
    | ARM64Symbolic.BR _
    | ARM64Symbolic.CBZ _
    | ARM64Symbolic.CBNZ _
    | ARM64Symbolic.B_label _
    | ARM64Symbolic.B_cond_label _
    | ARM64Symbolic.CBZ_offset _
    | ARM64Symbolic.CBNZ_offset _
    | ARM64Symbolic.TBZ _
    | ARM64Symbolic.TBNZ _
    | ARM64Symbolic.TBZ_label _
    | ARM64Symbolic.TBNZ_label _
    | ARM64Symbolic.B _
    | ARM64Symbolic.B_cond _
    | ARM64Symbolic.RET
    | ARM64Symbolic.SVC _
    | ARM64Symbolic.Label _ ->
        ReadOrControlFlow

let private overwrittenBeforeReadOrEnd
    (target: ARM64Symbolic.Reg)
    (instrs: ARM64Symbolic.Instr list)
    : bool =
    let rec check remaining =
        match remaining with
        | [] -> true
        | instr :: rest ->
            match registerLifetimeStep target instr with
            | Unrelated -> check rest
            | Overwritten -> true
            | ReadOrControlFlow -> false

    check instrs

/// Return the condition that selects the complementary control-flow edge.
let private invertCondition (condition: ARM64.Condition) : ARM64.Condition =
    match condition with
    | ARM64.EQ -> ARM64.NE
    | ARM64.NE -> ARM64.EQ
    | ARM64.LT -> ARM64.GE
    | ARM64.GT -> ARM64.LE
    | ARM64.LE -> ARM64.GT
    | ARM64.GE -> ARM64.LT
    | ARM64.LO -> ARM64.HS
    | ARM64.HI -> ARM64.LS
    | ARM64.LS -> ARM64.HI
    | ARM64.HS -> ARM64.LO

/// Peephole optimization pass
/// Patterns:
/// 1. SUB_imm + CMP #0 → SUBS (fuse subtract and compare)
/// 2. MOV Xn, Xn → remove (redundant self-move)
/// 3. FMOV Dn, Dn → remove (redundant FP self-move)
/// 4. ADD Xn, Xn, #0 → remove (add zero)
/// 5. SUB Xn, Xn, #0 → remove (subtract zero)
/// 6. B_label X + Label X → remove branch (branch to next instruction)
/// 7. CMP #0 + B.EQ → CBZ (compare zero and branch equal)
/// 8. CMP #0 + B.NE → CBNZ (compare zero and branch not equal)
/// 9. AND Xn, Xn, Xn → MOV (AND with self is identity)
/// 10. ORR Xn, Xn, Xn → MOV (OR with self is identity)
/// 11. MOVN #0 + EOR + AND → BIC (bit clear when the inverted temporary is overwritten)
/// 12. B.cond true + B false + true: → B.!cond false + true: (fall through)
let peepholeOptimize (instrs: ARM64Symbolic.Instr list) : ARM64Symbolic.Instr list =
    let rec optimize acc remaining =
        match remaining with
        | [] -> List.rev acc
        // Fuse SUB + CMP #0 into SUBS
        | ARM64Symbolic.SUB_imm (dest, src, imm) :: ARM64Symbolic.CMP_imm (cmpReg, 0us) :: rest when dest = cmpReg ->
            optimize (ARM64Symbolic.SUBS_imm (dest, src, imm) :: acc) rest
        // Fuse CMP #0 + B.EQ into CBZ
        | ARM64Symbolic.CMP_imm (reg, 0us) :: ARM64Symbolic.B_cond_label (ARM64.EQ, label) :: rest ->
            optimize (ARM64Symbolic.CBZ (reg, label) :: acc) rest
        // Fuse CMP #0 + B.NE into CBNZ
        | ARM64Symbolic.CMP_imm (reg, 0us) :: ARM64Symbolic.B_cond_label (ARM64.NE, label) :: rest ->
            optimize (ARM64Symbolic.CBNZ (reg, label) :: acc) rest
        // Fuse x & (y EOR -1) into BIC x, y. Requiring AND to overwrite the
        // EOR destination proves that the inverted temporary is dead here.
        | ARM64Symbolic.MOVN (allOnes, 0us, 0)
          :: ARM64Symbolic.EOR_reg (inverted, value, eorMask)
          :: ARM64Symbolic.AND_reg (dest, left, andRight)
          :: rest
            when eorMask = allOnes
                 && andRight = inverted
                 && dest = inverted
                 && allOnes <> inverted
                 && allOnes <> left
                 && allOnes <> value
                 && inverted <> left
                 && overwrittenBeforeReadOrEnd allOnes rest ->
            optimize (ARM64Symbolic.BIC_reg (dest, left, value) :: acc) rest
        | ARM64Symbolic.MOVN (allOnes, 0us, 0)
          :: ARM64Symbolic.EOR_reg (inverted, eorMask, value)
          :: ARM64Symbolic.AND_reg (dest, left, andRight)
          :: rest
            when eorMask = allOnes
                 && andRight = inverted
                 && dest = inverted
                 && allOnes <> inverted
                 && allOnes <> left
                 && allOnes <> value
                 && inverted <> left
                 && overwrittenBeforeReadOrEnd allOnes rest ->
            optimize (ARM64Symbolic.BIC_reg (dest, left, value) :: acc) rest
        // Remove redundant self-move (integer)
        | ARM64Symbolic.MOV_reg (dest, src) :: rest when dest = src ->
            optimize acc rest
        // Remove redundant self-move (FP)
        | ARM64Symbolic.FMOV_reg (dest, src) :: rest when dest = src ->
            optimize acc rest
        // Remove add zero
        | ARM64Symbolic.ADD_imm (dest, src, 0us) :: rest when dest = src ->
            optimize acc rest
        // Remove subtract zero
        | ARM64Symbolic.SUB_imm (dest, src, 0us) :: rest when dest = src ->
            optimize acc rest
        // Make an immediately following true target the fallthrough edge.
        | ARM64Symbolic.B_cond_label (condition, trueTarget)
          :: ARM64Symbolic.B_label falseTarget
          :: ARM64Symbolic.Label label
          :: rest
            when trueTarget = label ->
            let branch =
                ARM64Symbolic.B_cond_label (invertCondition condition, falseTarget)
            optimize (ARM64Symbolic.Label label :: branch :: acc) rest
        // AND with self is identity - simplify to MOV if dest differs from operand
        | ARM64Symbolic.AND_reg (dest, src1, src2) :: rest when src1 = src2 ->
            if dest = src1 then
                optimize acc rest  // dest = src AND src = src, remove entirely
            else
                optimize (ARM64Symbolic.MOV_reg (dest, src1) :: acc) rest
        // OR with self is identity - simplify to MOV if dest differs from operand
        | ARM64Symbolic.ORR_reg (dest, src1, src2) :: rest when src1 = src2 ->
            if dest = src1 then
                optimize acc rest  // dest = src OR src = src, remove entirely
            else
                optimize (ARM64Symbolic.MOV_reg (dest, src1) :: acc) rest
        // Remove branch to next instruction
        | ARM64Symbolic.B_label target :: ARM64Symbolic.Label lbl :: rest when target = lbl ->
            optimize (ARM64Symbolic.Label lbl :: acc) rest
        // Fuse LSL_imm + ADD_reg into ADD_shifted: dest = src1 + (src2 << shift)
        // Pattern: LSL_imm temp, x, shift; ADD_reg dest, x, temp → ADD_shifted dest, x, x, shift
        | ARM64Symbolic.LSL_imm (lslDest, lslSrc, shift) :: ARM64Symbolic.ADD_reg (addDest, addSrc1, addSrc2) :: rest
            when lslDest = addSrc2 && lslSrc = addSrc1 ->
            optimize (ARM64Symbolic.ADD_shifted (addDest, addSrc1, lslSrc, shift) :: acc) rest
        // Fuse LSL_imm + ADD_reg (commutative): ADD_reg dest, temp, x → ADD_shifted dest, x, x, shift
        | ARM64Symbolic.LSL_imm (lslDest, lslSrc, shift) :: ARM64Symbolic.ADD_reg (addDest, addSrc1, addSrc2) :: rest
            when lslDest = addSrc1 && lslSrc = addSrc2 ->
            optimize (ARM64Symbolic.ADD_shifted (addDest, addSrc2, lslSrc, shift) :: acc) rest
        // Fuse LSL_imm + SUB_reg into SUB_shifted: dest = shifted - src
        // Pattern: LSL_imm temp, x, shift; SUB_reg dest, temp, x → SUB_shifted dest, temp, x, 0 then adjust
        // Actually for n = 2^k - 1: x * n = (x << k) - x, so SUB dest, shifted, x
        // We need: SUB_shifted dest, (x << shift), x, 0 but that's not quite right...
        // For x * 7 = (x << 3) - x: LSL temp, x, 3; SUB dest, temp, x
        // This becomes: dest = temp - x = (x << 3) - x
        // ARM64 SUB_shifted is: dest = src1 - (src2 << shift)
        // So we need: dest = (x << 3) - x which is dest = (x << 3) - (x << 0)
        // That's not directly expressible with SUB_shifted... but we can use:
        // SUB dest, temp, x where temp = x << 3, which is two instructions
        // Actually let's skip SUB fusion for now since it doesn't map cleanly to SUB_shifted
        | instr :: rest ->
            optimize (instr :: acc) rest
    optimize [] instrs
