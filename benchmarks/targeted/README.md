# Targeted Compiler Benchmarks

These diagnostic suites isolate compiler/runtime subsystems. They are separate
from the audited application workloads in `../problems`, do not participate in
the canonical `routine` profile, and never update its baselines.

Run one suite and write its measurements to a JSON file:

```bash
./benchmarks/targeted/run.sh json /tmp/json-benchmark.json
./benchmarks/targeted/run.sh integer128 /tmp/integer128-benchmark.json
```

Run every targeted suite into one directory:

```bash
./benchmarks/targeted/run.sh all /tmp/dark-targeted-benchmarks
```

Each case is compiled once for measurement, checked for its exact expected
output, sampled seven times, and compiled separately with leak checking. Results
include compilation time, executable size, median runtime, normalized operation
cost where applicable, and leak-check status.

The `integer128` suite covers signed and unsigned arithmetic, comparisons and
bitwise operations, decimal parsing/formatting, UUID parsing/formatting,
generation and equality, and collection storage/copying. It is intended to
capture the current decimal-buffer implementation before migrating `Int128` and
`UInt128` to direct 128-bit values.

## Closed-list array diagnostics

The independent `list-array` comparison uses built Debug compilers from two
worktrees and pinned QEMU instruction counts. It checks unique reuse, surviving
old versions, and a captured-list persistent fallback. Both compilers receive
the exact same source files. Each case also runs with leak checking. Reports
include source/assembly hashes, commit and dirty-state attribution, instruction
counts, compile times, and executable sizes. They do not update canonical
snapshots; single compile-time samples are diagnostic, not a timing gate.

```bash
python3 benchmarks/targeted/list-array/compare.py \
  --baseline=/path/to/baseline-worktree --candidate=/path/to/candidate-worktree \
  --target=arm64 --output=/tmp/list-array-arm64.json
# Repeat with --target=x86_64 and a different output file.
```

The separate allocator probe must print `434` with no leak report on a compiler
supporting this storage class. It deliberately inspects allocator contents and
is not a source-semantics benchmark. On the persistent baseline it prints `1431`.

```bash
./dark --allow-internal --emit-result --leak-check --disable-opt-inline \
  benchmarks/targeted/list-array/storage-probe.dark -o /tmp/list-array-probe
/tmp/list-array-probe
```
