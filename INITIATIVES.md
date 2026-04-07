# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: **4495/4530 (99.2%)**. Already exceeds ARM64 baseline (4486). **35 failures remain.**

Recently fixed:
- **StringConcat left operand in R8/R9** (commit 73be36a) — `loadInfo right` clobbered
  R8/R9 before `loadInfo left` could read from them. Fix: save left to scratch (R11)
  before loading right. Fixed all 10 Base64 tests.
- **INT64_MIN / -1 SIGFPE** — detect overflow before IDIV.
- **Lsl/Lsr dest==shift && src==RCX** — setBit computed bit<<bit instead of 1<<bit.
- **Lsl/Lsr dest==RCX** — shift value overwrote src already in dest register.
- **Uxtw/Uxth zero-extension** — preceding 64-bit SUB left upper bits set.
- **FileReadText/WriteText/AppendText** — implemented x86_64 syscall sequences.

### Remaining 35 failures (grouped by root cause)

#### 1. FingerTree `__rebuildFrom` crash (THE BIG ONE — blocks ~20 tests)

**Symptom:** Iterating a 9+ element LITERAL list via recursive `match [b,...rest] -> f(rest)`
crashes with SIGSEGV. The crash produces a garbage pointer with valid heap offset in low
bits but corrupted high bits (e.g., `0xC00175_XX_XX_00140`).

**What works:** getAt, head, pushBack, non-recursive iteration, cons-built lists of ANY size.
Only LITERAL lists (compiled to direct FingerTree construction) of 9+ elements trigger the
bug when iterated in a self-recursive function.

**Root cause investigation so far:**
- The crash happens inside `__rebuildFrom_i64` which is called from `tail_i64` when the
  middle tree is a DEEP node (line 507-514 of `__FingerTree.dark`)
- `__rebuildFrom` iterates via getAt + pushBack. Both work correctly individually.
- Valgrind shows NO prior memory writes to invalid addresses — the garbage pointer is
  **computed**, not read from corrupted memory
- Hardware watchpoints on callee-saved register save locations ([RBP-16], etc.) show the
  saved values are NOT overwritten on the stack
- The corrupted return value has pattern: valid heap address in low ~36 bits, garbage in
  upper bits. This strongly suggests a **64-bit arithmetic bug** — possibly an IMUL that
  produces extra high bits, or a register that isn't properly zero-extended after a 32-bit op.
- Attempted fix: replace `__rebuildFrom` with proper FingerTree node manipulation. This
  avoids the crash but regresses 42 other tests (the replacement had its own bugs). Reverted.

**Suggested next steps:**
1. **Use `--dump-lir` on `__rebuildFrom_i64`** and trace the exact register allocation for
   the recursive call's ArgMoves. Check for parallel-move conflicts where a source register
   is overwritten before being read.
2. **Compare the IMUL results** in the crashing binary vs a working binary. The crash
   value's high bits may come from a multiplication that should have been masked to 32 bits
   (FingerTree uses `index * 8` for offsets — if `index` is in a register that has garbage
   in its upper 32 bits, `imul r11, rcx` would amplify the garbage).
3. **Check if `And_imm` or `Orr` operations properly clear upper bits.** On x86_64, 32-bit
   operations zero-extend, but 64-bit `AND` does not clear bits above the result.

**Affected tests:** lists.e2e L295/L297, benchmarks.e2e L354, equality.e2e L125/L126,
dict.e2e L307, crypto.e2e L12/L293/L296/L299/L302 + most other crypto tests (which
internally use Bytes.fromList which does recursive list iteration),
benchmarks.e2e L475 (complexSum).

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

#### 5. Independent small issues (4 tests)

- **rawptr OOM** (rawptr.e2e L15): `__raw_alloc(600MB)` should exit(1) not SIGSEGV
- **refcount leak_check** (refcounting.e2e L5): output mismatch (refcounting not implemented on x86)
- **memReclaimBurn** (benchmarks.e2e L234): memory reclamation stress test, 1.59s runtime
- **Dict string keys** (dict.e2e L87): Dict.fromList with string keys fails

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
