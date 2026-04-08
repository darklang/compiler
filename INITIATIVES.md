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

### Remaining 1 failure

- **memReclaimBurn** (list.e2e L234): creates 10,000×400-element lists, exhausts 512MB heap.
  Needs recursive FingerTree RefCountDec (a ~240 instruction helper that traverses tagged
  tree nodes and frees each to the appropriate free list size class) PLUS free list reuse
  in RawAlloc. The ARM64 backend has this (`__dark_list_refcount_dec_helper`); needs porting.

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
