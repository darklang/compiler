# Initiatives

## Active: x86_64 backend (branch: `x64`)

Goal: reach ARM64 test parity (4486/4530 E2E tests) then merge to main.

Current: 4484/4530 (99.0%). Gap: 46 tests.

Recently fixed:
- **Lsl/Lsr dest==shift && src==RCX** — setBit computed bit<<bit instead of 1<<bit,
  corrupting all HAMT bitmaps. Fixed Dict (25 tests), nqueen (5), rotl32 (1).
- **Lsl/Lsr dest==RCX** — shift value overwrote src already in dest register.
- **Uxtw/Uxth zero-extension** — treated as no-ops but preceding 64-bit SUB left
  upper bits set. Now emits MOV_reg32/MOVZX_word. Fixed UInt negate (6 tests).
- **FileReadText/WriteText/AppendText** — implemented x86_64 syscall sequences
  using open/fstat/read/write/close. Fixed 17 File I/O tests.
- **Float test expectations** — L494/L495 had wrong expected values (copy/paste bug).

Remaining work (in priority order):
1. Fix list recursive popFront crash for 9+ elements — `match l with [h,...t] -> f(t)`
   crashes when a function recursively destructures a 9+ element list. Inline match
   works fine. The issue is NOT in FingerTree popFront itself but in how the popped
   result is passed through function calls (possibly register/stack clobbering during
   call setup with the tree structure). Blocks ~30 crypto/bytes/base64 tests that use
   Bytes.fromList (which recursively destructures).
2. Fix remaining Crypto/Base64 tests (~23 tests) — most depend on fix #1
3. Fix String.split edge cases (4 tests) — segfault with repeated single-char separator
   in test runner context (works standalone). Possibly heap exhaustion.
4. Fix other edge cases (~13 tests) — BoxEq3/4, complexSum, Result patterns, etc.

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
