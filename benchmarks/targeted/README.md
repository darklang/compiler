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
