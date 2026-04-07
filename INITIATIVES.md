# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: **4496/4530 (99.3%)**. Already exceeds ARM64 baseline (4486). **34 failures remain.**

Recently fixed:
- **Heap bounds checking** (commit e0069b1) — HeapAlloc/RawAlloc now check against
  512MB mmap limit and exit(1) with "Out of heap memory" instead of SIGSEGV.
  Shared OOM handler avoids code bloat. Fixes rawptr.e2e L15.
- **StringConcat left operand in R8/R9** (commit 73be36a) — `loadInfo right` clobbered
  R8/R9 before `loadInfo left` could read from them. Fix: save left to scratch (R11)
  before loading right. Fixed all 10 Base64 tests.
- **INT64_MIN / -1 SIGFPE** — detect overflow before IDIV.
- **Lsl/Lsr dest==shift && src==RCX** — setBit computed bit<<bit instead of 1<<bit.
- **Lsl/Lsr dest==RCX** — shift value overwrote src already in dest register.
- **Uxtw/Uxth zero-extension** — preceding 64-bit SUB left upper bits set.
- **FileReadText/WriteText/AppendText** — implemented x86_64 syscall sequences.

### Remaining 34 failures (grouped by root cause)

#### 1. Callee-saved register corruption (THE BIG ONE — blocks ~25 tests)

**Symptom:** Complex FingerTree operations (list equality, tail on two different lists,
recursive list iteration) crash with SIGSEGV or produce wrong results. Callee-saved
registers RBX (X19) and R12 (X20) get corrupted to small values like 1.

**Root cause (narrowed down):** MIR copy propagation changes register allocation in a way
that exposes a latent x86_64 codegen bug. Disabling `--disable-opt-mir-copy-prop` makes
the FingerTree crashes deterministically disappear (0/30 crashes) but causes 8 float
tailcall test regressions, so it's not a viable workaround.

**Key evidence:**
- Crash is **non-deterministic** (ASLR-dependent): sometimes works, sometimes SIGSEGV
- Valgrind shows NO memory errors (address mapping neutralizes the bug)
- Copy-prop reduces `tail_i64`'s spill slots from 400 to 48 bytes (different register
  assignments expose the bug)
- Stack padding doesn't fix it — corruption is in registers, not stack overflow
- Both RBX and R12 get corrupted to value 1 (TAG_SINGLE, refcount, or element value)
- The MIR for `tail_i64` is **identical** with/without copy-prop; only the pre-regalloc
  LIR differs (fewer intermediate copies → different register allocation)

**Suggested next steps:**
1. **Binary diff**: disassemble `__FingerTree.tail_i64` with and without copy-prop,
   diff the x86 instructions to find the exact instruction that corrupts RBX/R12
2. **GDB hardware watchpoint**: break at tail_i64 entry, set hw watchpoint on the
   stack location where R12 is saved ([RBP-16]), continue to find what overwrites it
3. **Check TailCall + epilogue interaction**: tail_i64 has multiple TailCall paths
   — verify each one correctly restores callee-saved registers before JMP

**Affected tests:** lists.e2e L295/L297, benchmarks.e2e L354/L475,
equality.e2e L125/L126, dict.e2e L87/L307, elet.dark L51/L55/L60/L65/L69,
crypto.e2e L12/L164/L167/L170/L211/L214/L293/L296/L299/L302,
string.e2e L384/L392/L393/L394 (crash in test runner context)

#### 2. Crypto hash wrong values (3-5 tests, independent of #1)

SHA1 for short input (3 bytes) produces wrong hash. MD5 for short input is correct.
SHA256/SHA384 may have similar issues. These are pure-Dark implementations.

**Affected tests:** crypto.e2e L26 (sha1), L39 (sha256), L49/L52 (sha384)

#### 3. String.split in test runner (4 tests)

Works standalone but fails in test runner with exit code 139. Likely heap exhaustion when
many tests share a heap, or a test-runner-specific codepath.

**Affected tests:** string.e2e L384, L392, L393, L394

#### 4. Result/match pattern crashes (5 tests)

Segfault in List.map with match/Result patterns. Likely same root cause as #1 (recursive
list iteration) since List.map iterates.

**Affected:** result.e2e — various tests with `List.map_v0 [...] (fun x -> match ...)`

#### 5. Independent small issues (3 tests)

- **refcount leak_check** (refcounting.e2e L5): output mismatch (refcounting not implemented on x86)
- **memReclaimBurn** (benchmarks.e2e L234): memory reclamation stress test, needs refcounting
- **Dict string keys** (dict.e2e L87): Dict.fromList with string keys crashes (likely #1)

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
