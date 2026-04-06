# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: 4279/4530 (94.5%). Gap: ~251 tests.

Recently fixed:
- **x86_64 spill scratch register aliasing** — X8-X17 all map to R11; the register
  allocator used X12/X13 as distinct scratch registers for loading spilled operands,
  but both are R11 on x86_64. Fixed Add/Sub/Cmp via StackSlot approach in codegen,
  and Mul/And/Or/RawGet via loadSpilledPair (loads left into dest register instead).
- HeapStore R11 conflict, RawGet/RawSet R11 conflicts
- Lsl/Lsr RCX clobbering, StringConcat register clobbering

Remaining work (in priority order):
1. Fix remaining stdlib segfaults (~148 tests) — likely from register mapping issues
   in complex stdlib functions (String.prepend, Bytes.fromList, Crypto, etc.)
   Root cause: bad pointer values in byte copy loops, possibly from incorrect
   argument passing or spill-related corruption
2. Fix File I/O syscalls (~22 tests) — FileExists etc. are stubbed to return 0
3. Fix remaining Dict operations (~48 tests) — depends on list/fingertree fixes
4. Fix UInt32/UInt16 negate (6 tests) — needs 32-bit masking
5. Fix remaining floats (~3 tests) — edge cases

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
