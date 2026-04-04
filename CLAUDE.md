# Dark Compiler - Claude Code Instructions

## x86_64 Backend (branch: `x64`)

The compiler now supports both ARM64 and x86_64 targets. The backend is selected
automatically based on `Platform.detectArch()`. On x86_64 Linux, programs compile
to native ELF executables without QEMU.

### Architecture

Passes 1-5 (Parser → TypeCheck → ANF → MIR → LIR → RegisterAllocation) are
**fully shared** between architectures. Passes 6-8 are per-architecture:

| ARM64 | x86_64 | Purpose |
|-------|--------|---------|
| `ARM64.fs` | `X86_64.fs` | Instruction types |
| `6_CodeGen.fs` | `6_CodeGen_X86_64.fs` | LIR → ISA |
| `7_ARM64_Encoding.fs` | `7_X86_64_Encoding.fs` | Instructions → bytes |
| `7_ARM64_Resolve.fs` | `7_X86_64_Resolve.fs` | Label fixup |
| `8_Binary_Generation_ELF.fs` | `8_Binary_Generation_ELF_X86_64.fs` | ELF output |

### Critical x86_64 Codegen Pattern: Two-Operand Conflicts

x86_64 is two-operand: `dest = dest OP src`. LIR is three-operand: `dest = left OP right`.
When `dest == right`, `MOV dest, left` clobbers right before the operation.

**Every binary operation must check for this.** Fixes:
- Commutative ops (Add, Mul, And, Or, Xor, FAdd, FMul): swap operands
- Non-commutative ops (Sub, FSub, FDiv): use scratch/temp register
- Integer Mul with dest==right: use R11 (scratch) as temp
- Float non-commutative: use XMM15 as temp

### Register Mapping (LIR → x86_64)

```
X0→RAX  X1→RDI  X2→RSI  X3→RCX  X4→R8   X5→R9
X6→R10  X7→RDX  X8-X17→R11(scratch)
X19→RBX  X20→R12  X21���R13  (callee-saved, allocatable)
X22→R14  X23→R15  (RESERVED: heap ptr, free list base)
SP→RSP
```

**RDX is mapped to X7** (rarely used) to minimize IDIV clobber conflicts.
IDIV saves/restores RDX via the red zone `[RSP-8]`.

### Known Issues

1. **Float.toString multi-digit fractions**: `0.5` works, `0.14` produces null bytes.
   Root cause likely in stdlib's `__getFracDigits` recursion or `__stripZeros`.
2. **String comparison**: stdlib string ops not fully working on x86_64.
3. **GCD/modulo**: complex modulo in loops can produce wrong results.

### Running Tests

```bash
# All tests (includes E2E with stdlib)
./run-tests

# x86_64-specific unit tests only
./run-tests --filter=x86

# Quick expression test
./dark -r -e "2 + 3"
```

### Development in Docker

The devcontainer and Docker setup work on any architecture:
- ARM64 hosts: everything native
- x86_64 hosts: compiler builds natively, ARM64 test binaries run via qemu-user-static
