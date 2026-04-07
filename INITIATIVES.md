# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: **4503/4530 (99.4%)**. Already exceeds ARM64 baseline (4486). **27 failures remain.**

Recently fixed:
- **HeapStore register clobbering** — `HeapStore(addr, offset, StringSymbol)` clobbered
  the addr register (R11 or RCX) during inline string allocation. The string init uses
  R11 for the allocation pointer and RCX for temporary values, so if the addr register was
  either of these, the final store wrote to the wrong address. Also fixed FuncAddr and
  FloatSymbol variants for R11 clobbering. Fixed equality, list, result, and dict tests.
- **Heap bounds checking** (commit e0069b1) — HeapAlloc/RawAlloc now check against
  512MB mmap limit and exit(1) with "Out of heap memory" instead of SIGSEGV.
- **StringConcat left operand in R8/R9** (commit 73be36a) — `loadInfo right` clobbered
  R8/R9 before `loadInfo left` could read from them.
- **INT64_MIN / -1 SIGFPE** — detect overflow before IDIV.
- **Lsl/Lsr dest==shift && src==RCX** — setBit computed bit<<bit instead of 1<<bit.
- **Lsl/Lsr dest==RCX** — shift value overwrote src already in dest register.
- **Uxtw/Uxth zero-extension** — preceding 64-bit SUB left upper bits set.
- **FileReadText/WriteText/AppendText** — implemented x86_64 syscall sequences.

### Remaining 27 failures (grouped by root cause)

#### 1. Heap exhaustion in test suites (~20 tests)

Tests that pass standalone but crash (exit 139) when run as part of their suite because
earlier tests have consumed the heap and refcounting/GC is not implemented.

**Affected:** elet.dark L51/L55/L60/L65/L69, string.e2e L384/L392/L393/L394,
crypto.e2e L12/L167/L170/L211/L214/L293/L296/L299/L302, benchmarks.e2e L475

**Fix:** Implement refcounting, or reset heap between tests in the test runner.

#### 2. Crypto hash wrong values (5 tests)

SHA1 for short input (3 bytes) produces wrong hash. MD5 for short input is correct.
SHA256/SHA384 have similar issues. These are pure-Dark implementations.

**Affected tests:** crypto.e2e L26 (sha1), L39 (sha256), L49/L52 (sha384)

#### 3. Dict.fromList with list values (1 test)

`Dict.fromList([(1, [10]), (2, [20, 30])])` crashes standalone. Works with 1-2 entries
but crashes with 3+. Likely a FingerTree operation bug when handling complex nested values.

**Affected:** dict.e2e L307

#### 4. Partial application / multi-arg lambda (1 test)

`List.map (fun x y -> x)` then `List.map (fun l -> l 1L)` crashes. Involves partial
application creating closures from multi-arg lambdas.

**Affected:** eapply.dark L10

#### 5. Refcounting-dependent (2 tests)

- **refcount leak_check** (refcounting.e2e L5): output mismatch (refcounting not implemented on x86)
- **memReclaimBurn** (benchmarks.e2e L234): memory reclamation stress test, needs refcounting

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
