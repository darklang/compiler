# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: 4356/4530 (96.2%). Gap: ~174 tests.

Recently fixed:
- **x86_64 spill scratch register aliasing** — X8-X17 all map to R11; the register
  allocator used X12/X13 as distinct scratch registers for loading spilled operands,
  but both are R11 on x86_64. Fixed Add/Sub/Cmp via StackSlot approach in codegen,
  and Mul/And/Or/RawGet via loadSpilledPair (loads left into dest register instead).
- HeapStore R11 conflict, RawGet/RawSet R11 conflicts
- Lsl/Lsr RCX clobbering, StringConcat register clobbering

Remaining work (in priority order):
1. Fix Bytes/Crypto operations (~55+66 tests) — Bytes.fromList segfaults in larger lists,
   Crypto depends on Bytes
2. Fix Dict operations (~48 tests) — Dict.size returns 0, likely hash function or
   comparison function issue on x86_64  
3. Fix File I/O syscalls (~17 tests) — FileReadText/WriteText/AppendText not implemented
4. Fix remaining List edge cases (~10 tests) — large list matching, spread ops
5. Fix Base64 (~10 tests) — depends on Bytes
6. Fix UInt32/UInt16 negate (6 tests) — needs 32-bit masking
7. Fix String.split edge cases (4 tests) — segfaults with repeated single-char separator

Approach: TDD — pick a failing E2E test, write the smallest fix, run full suite.
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
