# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: **4529/4530 (99.98%)**. Already exceeds ARM64 baseline (4486). **1 failure remains.**

Recently fixed:
- **Leak check + string refcounting (1 test)** — Implemented leak counter via data label
  with RIP-relative addressing, string RefCountInc/Dec with sentinel detection, and
  leak report at _start exit. Fixed Print instructions to not bypass epilogue.
- **RawSet register aliasing (25 tests)** — X12/X13/X14 all map to R11 on x86_64
  but register allocator loaded spilled RawSet operands into them as if distinct.
  When both ptr and value were spilled, loading both into R11 clobbered the ptr,
  causing stores to wrong addresses (corrupt FingerTree nodes, null pointers).
  Fix: save/restore X3 (RCX) via push/pop and use as non-R11 temp for ptr.
  Fixed all elet shadowing, String.split, Dict, partial_application, crypto, benchmarks.
- **HeapStore register clobbering (7 tests)** — `HeapStore(addr, offset, StringSymbol)` clobbered
  the addr register (R11 or RCX) during inline string allocation.
- **Heap bounds checking** (commit e0069b1) — HeapAlloc/RawAlloc now check against
  512MB mmap limit and exit(1) with "Out of heap memory" instead of SIGSEGV.
- **StringConcat left operand in R8/R9** (commit 73be36a) — `loadInfo right` clobbered
  R8/R9 before `loadInfo left` could read from them.
- **INT64_MIN / -1 SIGFPE** — detect overflow before IDIV.
- **Lsl/Lsr dest==shift && src==RCX** — setBit computed bit<<bit instead of 1<<bit.
- **Lsl/Lsr dest==RCX** — shift value overwrote src already in dest register.
- **Uxtw/Uxth zero-extension** — preceding 64-bit SUB left upper bits set.
- **FileReadText/WriteText/AppendText** — implemented x86_64 syscall sequences.

### Remaining 1 failure: memReclaimBurn (list.e2e L234)

Creates 10,000×400-element lists. Each iteration builds a list, calls `List.length`,
and the list goes out of scope. Without memory reclamation, the 512MB heap exhausts
around iteration ~3,000. Fixing this requires TWO things working together:

#### 1. TaggedList RefCountDec — recursive FingerTree traversal

When a list variable goes out of scope, `RefCountDec(addr, 24, TaggedList)` is emitted.
The addr is a **tagged pointer** (tag in low 3 bits). The helper must:

1. Untag the pointer (`AND addr, ~7`)
2. Determine node type from tag → payload size → free list bucket index
3. Decrement refcount at `[untagged + payloadSize]`
4. If refcount > 0: done (other references exist)
5. If refcount == 0: free node to free list, then collect all children and recurse

**Tag → layout mapping:**

| Tag | Type   | Payload | Children (offsets)                                    |
|-----|--------|---------|-------------------------------------------------------|
| 1   | SINGLE | 8       | [0]: one child                                        |
| 2   | DEEP   | 96      | [16..40]: prefix[0..3], [48]: middle, [64..88]: suffix[0..3] |
| 3   | NODE2  | 24      | [0]: child0, [8]: child1                              |
| 4   | NODE3  | 32      | [0]: child0, [8]: child1, [16]: child2                |
| 5   | LEAF   | 8       | (no children)                                         |

DEEP nodes also have prefix_count at offset 8 and suffix_count at offset 56.

**Implementation approach:** Iterative DFS using the machine stack as a work stack.
The ARM64 version (`6_CodeGen.fs` lines 173-413) does this with ~240 instructions.
The x86_64 port should use `PUSH`/`POP` for the work stack and `CALL`/`RET` for
the function boundary. **Critical:** save/restore ALL caller-saved registers around
the CALL since the helper clobbers RAX, RCX, RDX, RDI, RSI.

**Failed attempt notes:** A first attempt at the helper (in this session) caused 400
test regressions. The crash was in PrintHeapString's byte copy loop, suggesting the
helper corrupted registers or the stack. Possible causes:
- The helper's work stack (via SUB RSP) may interfere with the callee-saved register
  saves (PUSH RBP/RBX/RDI/RSI in the helper prologue). If items are left on the work
  stack at return, the POPs read wrong values.
- Free list corruption: when a node is freed (`[node].next = head; head = node`), if
  the node is still reachable through another path, the next-pointer overwrites data.
- The helper uses RBX for pending count but RBX is callee-saved. If the caller relies
  on RBX after the CALL, it must be properly saved/restored.

**Recommended approach for next attempt:**
- Start with the simplest case: SINGLE and LEAF only (no DEEP/NODE2/NODE3)
- Test with `[1]` (SINGLE(LEAF)) — this should work before adding complexity
- Add NODE2/NODE3, test with `[1,2]` and `[1,2,3]`
- Add DEEP last (most complex child collection)
- Use GDB breakpoints on the helper entry to verify register state

#### 2. RawAlloc free list reuse

FingerTree nodes are allocated via `RawAlloc` (variable-size bump allocation), NOT
`HeapAlloc` (fixed-size). The ARM64 RawAlloc (`6_CodeGen.fs` lines 3103-3146) checks
the free list before bump-allocating:

```
aligned_size = (numBytes + 7) & ~7
payload_class = aligned_size - 8     // subtract the refcount word
if payload_class in [0, 248]:
    head = freeList[payload_class]
    if head != null:
        dest = head
        freeList[payload_class] = head.next
        return
// fall through to bump allocation
```

**Key insight:** The free list index for RawAlloc is `aligned_size - 8` (payload class),
which matches the `payloadSize` parameter in RefCountDec. This is because the refcount
is always the LAST 8 bytes of the allocation.

**Failed attempt notes:** Adding free list to RawAlloc caused 157 regressions (even
without RefCountDec enabled). The RawAlloc free list code used PUSH/POP of RCX/RDX
as temps, but the issue was likely that `destReg` could be RCX or RDX, or that the
`sizeReg` was clobbered by the free list check. Need to be more careful about which
registers are used as temps vs which are operands.

**Recommended approach:** Use a register that's NOT destReg and NOT sizeReg for the
free list check. The ARM64 version uses X12-X15 (scratch registers). On x86_64,
use PUSH/POP to save a known-safe register, or check if destReg/sizeReg conflict
with the temp registers before choosing the code path.

#### Generic RefCountDec (non-list types)

Generic RefCountDec for tuples, closures, etc. also caused regressions (220 failures)
when enabled. Root cause unclear but likely:
- The refcount field offset (`payloadSize`) may not match actual struct layout for
  some object types
- Objects allocated via HeapAlloc use `sizeBytes` as the free list index, but
  RefCountDec uses `payloadSize`. These may not match: HeapAlloc(24) → index 24,
  but RefCountDec(addr, 16, Generic) → index 16. Need to verify the mapping.
- Some heap objects may not have their refcount field initialized to 1

### Diagnostic tools

Three shell scripts in `scripts/`:
- **`debug-x86-crash.sh`** — Compile & run with GDB crash analysis, callee-saved watchpoints,
  or valgrind. Usage: `./scripts/debug-x86-crash.sh "expr" [--watch-func ADDR] [--trace-calls]`
- **`dump-lir-func.sh`** — Dump pre/post-regalloc LIR for a specific function.
  Usage: `./scripts/dump-lir-func.sh "expr" function_name`
- **`disasm-func.sh`** — Disassemble a function from a compiled binary.
  Usage: `./scripts/disasm-func.sh /path/to/bin [hex_addr]`

### Approach

TDD — pick a failing E2E test, write the smallest fix, run full suite.
See CLAUDE.md for x86_64 architecture decisions and known patterns.

## Long term

- mutmut testing
- matching darklang language
- increasing code coverage
- completing benchmarks
- expanding to support full language
- support full darklang stdlib
- Json stdlib module (parsing/serialization)
- support full darklang test suite
- reimplement darklang compiler in Darklang
- reimplement test suite in Darklang
- complete Unicode string support
- add optimizations
- remove crashes
- end-to-end SSA
- SSA-based HIR (sub ANF?)
- SCCP-based HIR, MIR, and LIR optimizations
- remove non-functional idioms
- unify memory RawPtr, heap primitives, reference counting. Ensure everything is reference counted.

# Short term

- int64 assumptions
- fix indentation to not nest so deeply
- add values
