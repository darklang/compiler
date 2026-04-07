# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: 4430/4530 (97.8%). Gap: 100 tests.

Recently fixed:
- **x86_64 spill scratch register aliasing** — X8-X17 all map to R11; the register
  allocator used X12/X13 as distinct scratch registers for loading spilled operands,
  but both are R11 on x86_64. Fixed Add/Sub/Cmp via StackSlot approach in codegen,
  and Mul/And/Or/RawGet via loadSpilledPair (loads left into dest register instead).
- HeapStore R11 conflict, RawGet/RawSet R11 conflicts
- Lsl/Lsr RCX clobbering, StringConcat register clobbering

Remaining work (in priority order):
1. Fix FingerTree tail for 9+ element lists — tail(popFront) on trees with 
   non-empty middle crashes. Likely a register conflict in explodeNodeToFront
   or rebuildFrom. Blocks ~30 crypto/bytes tests.
2. Fix Dict operations with String keys (~25 tests) — Int64 keys work, String keys
   don't. Second set causes first entry to vanish. Hash function issue?
3. Fix File I/O syscalls (~17 tests) — FileReadText/WriteText/AppendText not implemented
4. Fix Base64 (~10 tests) — depends on Bytes
5. Fix UInt negate printing (6 tests) — computation correct, display as signed not unsigned
6. Fix other edge cases (~12 tests)

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
