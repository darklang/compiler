# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: 3311/4530 (73%). Gap: ~1175 tests.

Remaining work (in priority order):
1. Fix Float.toString for multi-digit fractions (~170 tests) — null bytes in
   `__getFracDigits` recursion, likely another two-operand conflict in deeper
   register allocation pattern or in `__stripZeros` string manipulation
2. Fix stdlib string operations (~90 tests) — comparison, substring, indexOf
3. Fix dict operations (~60 tests) — depends on string ops
4. Fix remaining list operations (~60 tests) — list printing, comparison
5. Fix remaining segfaults (~97 tests) — various causes
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
