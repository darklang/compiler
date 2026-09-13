# Roadmap

This file contains concrete work that is not yet represented by a focused
failing test or external issue. Compatibility gaps are tracked in the
[compatibility documentation](../compatibility/overview.md); reproducible
compiler bugs belong in [known issues](known-issues.md).

## Current work

- Complete the dual-backend memory-management matrix described in the
  [x86-64 backend status](../compiler/backend/x64.md), including the disabled
  `memReclaimBurn` coverage and shared raw-memory policy.
- Expand byte-level x86-64 instruction-encoding coverage and replace the
  hand-maintained coverage count with a test-derived report.
- Finish upstream-test enablement. `TestRunner.fs` is the source of truth:
  `eapply.dark`, `aliases.dark`, `epipe.dark`, and `derror.dark` remain
  line-allowlisted, while `elambda.dark` is still excluded from the default
  upstream set. Do not preserve dated failure counts here.
- Validate the compiler against the existing package repository and turn each
  discovered incompatibility into a focused test or compatibility-ledger item.
- Establish or reject the reported high-register-pressure spill risk. The old
  claim had no minimal reproduction; retain it as an investigation target, not
  as a confirmed compiler bug.

## Longer-term direction

- Continue toward full language, standard-library, and upstream-test parity,
  with the compatibility ledgers defining concrete slices.
- Unify managed-value ownership around typed shapes and release plans instead
  of ad hoc RawPtr, heap-primitive, and backend-specific paths.
- Treat self-hosting the compiler and test suite in Darklang as a design goal
  that requires a scoped proposal before implementation.

Rejected optimization trials are recorded in the
[optimization catalog](../compiler/optimizations/catalog.md) so they are not
repeated without new evidence.
