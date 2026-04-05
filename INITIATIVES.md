# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: 3986/4530 (88%). Gap: ~544 tests.

Recently fixed:
- ArgMoves parallel move conflicts (red zone save for clobbered sources)
- Float.toString now works for ALL fractions (0.14, 3.14, etc.)
- String.take/slice work correctly
- PrintFloat calls Stdlib.Float.toString instead of being a stub

Remaining work (in priority order):
1. Investigate why float E2E tests still fail despite correct output (~179 tests)
   — `./dark -r -e "1.0"` prints "1.0" correctly but E2E runner says "Value mismatch"
   — May be output format, newline, or E2E runner capture issue
2. Fix remaining string operations (~92 tests) — comparison, indexOf
3. Fix remaining segfaults (~146 tests) — various causes
4. Fix dict operations (~63 tests) — depends on string ops
5. Fix remaining list operations (~58 tests) — list printing, comparison
6. Fix remaining closures (~19 tests) — edge cases with captures
7. Fix 128-bit integer types (~13 tests)
8. Implement file I/O syscalls

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
