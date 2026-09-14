# Verification Policy

This policy applies to all agents when they verify a proposed commit, change, fix, workflow update, or integration step.

Verification means both, for the active development target:

- All tests pass.
- Benchmarks do not regress.

The active target is the host unless the work explicitly declares another
target. For ordinary host-target compiler changes, the default verification
commands are:

```bash
./run-tests --ai
./benchmarks/run_benchmarks.sh --verify routine
```

Routine verification keeps terminal output concise so automated callers do not
consume context on repeated per-workload details. Full build and measurement
logs, the markdown report, and decision JSON remain in the reported results
directory. Use `--verbose` when interactive diagnosis needs streamed details.

The E2E runner compiles up to 8192 compatible value-equality checks together by
default, enough for every compatible contiguous group in the current corpus.
Each check remains a separately compiled function while the caller and
executable are shared. Use `--e2e-batch-size=1` for a singular diagnostic
baseline, or another value through 8192 for batch-size experiments. Timing JSON
records the configured size, logical/eligible test counts, physical executions,
batch count, batched logical tests, and largest observed batch so batch-size
comparisons do not confuse logical coverage with compiler invocations.

Agents may run narrower checks while developing a change, but a change is not verified until the full verification policy has passed or the agent explicitly reports why full verification could not be completed.

Target support is intentionally allowed to advance independently. A feature
developed for one target may land after that target's applicable tests and
benchmarks pass; an architecture outside the declared scope is not an
integration blocker. Cross-target parity work must name every target in scope
and is a dated audit of those targets at that revision, not a permanent
requirement that future changes validate every architecture.

On an ARM64 host, Linux x86_64 tests are explicit and execute generated ELF
binaries through the pinned QEMU installation:

```bash
./run-tests --ai --target=linux-x86_64
```

Omitting `--target` validates ARM64 only. Conversely, an x86_64-target change
must pass the x86_64 suite and its relevant x86_64 benchmark gate; a host ARM64
run is required only when ARM64 is also declared in scope.

Verification mode compares the complete routine run with the compatible
architecture-specific canonical Dark snapshot, not `RESULTS.md`. The decision is
the exact comparison of the products of every positive instruction count; the
reported equal-weight geometric `current/baseline` ratio is below 1 for an
improvement and above 1 for a regression. Individual losses may be compensated
by larger gains. Equal and improved aggregate runs pass ordinary read-only
verification, regressions fail, and no tracked benchmark file is modified.

When a compiler change improves aggregate routine performance, run
`./benchmarks/run_benchmarks.sh routine` in recording mode and commit the updated
Dark snapshot and generated `benchmarks/RESULTS.md`; commit
`benchmarks/BASELINES.md` only for an audited Rust refresh. Recording advances
only on improvement and leaves the stronger snapshot/results on regression.
Integration uses `--verify-fresh` and stops if a known improvement has not been
recorded. An incompatible or missing snapshot requires
one complete successful `--reset-dark-baseline` routine run; partial, targeted,
`all`, hyperfine, and failed runs cannot reset it. Audited Rust refreshes remain
separate via `--refresh-baseline=rust`.

When reporting verification, include the exact commands run, whether they passed or failed, and any residual risk.

For Linux x86_64 benchmark validation on an ARM64 worker, use the
canonical `benchmarks/x86_64_check.py` quick track. DCB measures the exact base
with Dark and audited Rust, measures the candidate with Dark, and retains the
structured comparison outside either worktree. A `partial-*` decision is useful
diagnostic evidence but is never a verified win. Run both the host routine gate
and the x86_64 QEMU gate only when both targets were explicitly included in the
change; each declared target must pass its own gate.
