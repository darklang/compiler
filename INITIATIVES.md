# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: 4145/4530 (91.5%). Gap: ~341 tests.

Recently fixed:
- ArgMoves parallel move conflicts (red zone save for clobbered sources)
- Float.toString now works for ALL fractions (0.14, 3.14, etc.)
- String.take/slice work correctly
- PrintFloat calls Stdlib.Float.toString instead of being a stub

Remaining work (in priority order):
1. Fix stack slot addressing for functions with spilled values + negative offsets
   — Positive offsets (local spills) use `[RBP + offset*8]`
   — Negative offsets (Stack -8, -16) are incoming stack args, need `[RBP + offset]` (raw bytes)
   — Need to distinguish the two cases; currently `* 8` is applied to all
   — This causes ~100 segfaults in complex stdlib functions (fingertree, List.push, etc.)
2. Fix remaining dict operations (~47 tests) — depends on fingertree fixes
3. Fix remaining list operations (~52 tests) — most depend on fingertree
4. Fix remaining floats (~21 tests) — edge cases
5. Fix remaining tailcall (~10 tests)
6. Fix 128-bit integer types (~13 tests)
7. Implement file I/O syscalls

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
