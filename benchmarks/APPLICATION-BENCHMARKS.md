# Application benchmark research

Microbenchmarks remain useful for isolating compiler regressions, but mature
language implementations also use larger programs to expose interactions among
parsing, allocation, collections, strings, dispatch, and control flow.

## Practices in other language projects

- [rustc-perf](https://github.com/rust-lang/rustc-perf) measures the Rust
  compiler on a corpus of real crates in check, debug, and optimized build
  modes. This is the strongest model once Dark can compile a representative
  Dark package graph.
- [Python's pyperformance](https://github.com/python/pyperformance) combines
  focused workloads with application-shaped programs and third-party-library
  behavior. It favors repeatable in-process workloads over external services.
- [Swift's benchmark suite](https://github.com/swiftlang/swift/tree/main/benchmark)
  distinguishes single-source tests from multi-source programs, retaining both
  diagnostic precision and whole-program coverage.
- [LLVM's test suite](https://github.com/llvm/llvm-test-suite) likewise contains
  SingleSource, MultiSource, and external application suites. External suites
  offer realism but add licensing, availability, and reproducibility costs.
- [Go's x/benchmarks repository](https://github.com/golang/benchmarks) includes
  the `sweet` application benchmarks, using pinned real packages and workloads
  to complement the standard library's package-local benchmarks.

The common pattern is a layered suite: keep small kernels, add pinned real
programs, and treat large external suites as a separate reproducibility and
licensing problem. Compiler-throughput corpora are especially valuable, but
Dark does not yet have a sufficiently large stable package ecosystem for that
to be the first application benchmark.

## Selected first application

TinyTemplate 1.2.1 is small enough to audit and port completely while still
behaving like a library used by an application. Its compiler and interpreter
exercise nested data, parsing, instruction construction, template and formatter
registries, path lookup, escaping, conditionals, iteration, scoping, and calls.
The benchmark renders an inventory report through that complete surface.

The Rust implementation is the complete published 1.2.1 crate source, not a
facsimile or a dependency on a moving release. The Dark implementation is a
functional port of the same value model, grammar, public registry operations,
rendering semantics, and error categories. Routine and quick modes invoke the
same source with profile-declared row and repetition arguments.

Future additions should prefer another pinned real codebase with a different
shape—such as a parser, serializer, or persistent-data application—before
adding more template workloads. A Dark self-hosting or multi-package compile
corpus should supersede synthetic compiler-throughput proxies once available.

## Algorithm-workload audit (2026-09-13)

Physical line counts below include comments and blank lines but exclude test,
example, and benchmark files where they can be separated. The build observations
use stable Rust 1.89.0. The benchmark runner measures the whole executable, so a
port cannot literally exclude setup from Cachegrind; each proposed workload does
setup once and scales or repeats the hot computation enough to amortize it.

### Huffman codec — A: implement

- **Source:** <https://github.com/libreamartists/huffman-codec> at
  `5bbc197d41e55d949040d8c8404f3969f8b574eb` (`Cargo.toml` version 0.1.6;
  the manifest points at the repository's former `4meta5` owner).
- **Size and dependencies:** 232 production lines before its test module, one
  library module, and no external dependencies. The crate uses `alloc` while
  remaining `no_std`.
- **Tests and benchmarks:** four library tests plus one file-based round-trip
  test cover frequency counting, missing symbols, iterator APIs, and round trips.
  Four nightly `#[bench]` functions cover small/medium encode/decode. The crate
  no longer builds unchanged: it enables the removed
  `const_in_array_repeat_expressions` feature.
- **Architecture and data:** `Codec` owns an ASCII vector/non-ASCII `BTreeMap`
  code dictionary; frequency counting uses `BTreeMap`; tree construction uses
  `BinaryHeap<Rc<Tree>>`; codes and encoded bits use `Vec<u8>`.
- **Algorithm:** frequency counting plus priority-queue tree construction and
  recursive code traversal. A standard implementation is O(n + k log k + b)
  for n symbols, k distinct symbols, and b encoded bits. The upstream heap is
  accidentally a max-heap, however, so it produces prefix codes rather than an
  optimal Huffman tree. Its published decode benchmark also passes source bytes
  rather than encoded bits.
- **Rust-specific code:** four unchecked vector/slice accesses are local
  bounds-check elisions and are not algorithmic. There is no SIMD or
  platform-specific code. `Rc`, iterators, and the split ASCII dictionary do not
  need literal Dark equivalents.
- **Dark workload:** deterministically generate a skewed 32-symbol stream from
  a fixed LCG seed, build a correct min-priority Huffman tree, encode and decode
  it, verify the decoded checksum, and print a checksum incorporating encoded
  length and contents. Quick uses 200 symbols and two round trips; routine uses
  10,000 symbols and 30 round trips. Generation and codec construction happen
  once per whole-process measurement.
- **Adaptation:** both checked-in implementations use a deterministic sorted
  priority queue, making construction O(k²); k is explicitly capped at 32, so
  scaling remains linear in n and b. Dark packs each code's bits and length into
  an `Int64`; Rust uses the same representation. This corrects the upstream
  defects while preserving the standard algorithm and comparable architecture.
  Estimated port difficulty: low.

### The Ray Tracer Challenge — A: implemented as an independent kernel

- **Source:** <https://github.com/guimauveb/the-ray-tracer-challenge> at
  `3dd64f18cee419686eeb9bffc652dd06f3918156` (unversioned application).
- **Size and dependencies:** about 3,065 core production lines, or 3,794 with
  drawing programs and the CLI; standard library only. The repository contains
  no license file, so its code must not be vendored or closely copied without
  permission.
- **Tests and benchmarks:** 191 tests in about 2,390 lines give excellent
  chapter-by-chapter coverage of tuples, matrices, rays, objects, materials,
  patterns, worlds, cameras, reflection, and refraction. There is no benchmark.
  Stable Rust cannot build the source because `main.rs` enables
  `generic_const_exprs`.
- **Architecture and data:** separate tuple, float, and renderer modules;
  fixed-size generic matrices; point/vector/color records; enum-dispatched
  shapes and patterns; vectors of intersections, objects, and pixels; recursive
  reflection/refraction capped at depth six.
- **Algorithm:** one primary ray per pixel, O(p * o log o) for p pixels and o
  object intersections as written, plus bounded recursive secondary rays.
  Matrix inversion, intersection sorting, Phong lighting, shadows, patterns,
  reflection, and refraction supply the floating-point work.
- **Rust-specific code:** const-generic matrices, operator traits, borrowing,
  and enum conversions need ordinary Dark records/functions and sum types.
  There is no unsafe, SIMD, threading, or platform-specific code.
- **Implemented workload:** the checked-in pair independently implements the
  book's well-known kernel rather than copying the unlicensed repository. It
  constructs four colored spheres and a point light in memory, casts one ray
  per pixel, selects the closest sphere intersection, traces hard shadows, and
  applies ambient/diffuse lighting. It prints an exact quantized RGB checksum;
  argv controls square image size and repetitions. The hot work is O(p * o)
  for p pixels and the fixed o objects. The implementations have identical
  arithmetic order and contain no image allocation, file output, unsafe code,
  SIMD, or external dependency.

### Dissimilar — A: implement after Huffman

- **Source:** <https://github.com/dtolnay/dissimilar> at
  `cab43ff3c3a0c3d59f743baac8a02872372776e2` (`Cargo.toml` version 1.0.11).
- **Size and dependencies:** 1,301 production lines across `lib`, `range`, and
  `find`; no runtime dependencies. Criterion 0.8 is benchmark-only and
  libFuzzer is confined to a separate fuzz workspace, so neither is needed.
- **Tests and benchmarks:** 11 unit/integration tests plus one doctest pass on
  stable Rust and cover bisect, cleanup, Unicode boundaries, formatting, and
  reconstruction invariants. The fuzz target reconstructs both inputs from the
  diff. One Criterion benchmark diffs two embedded documents.
- **Architecture and data:** `Range` views over `Vec<char>`, internal
  equal/delete/insert ranges, public borrowed string chunks, `VecDeque` cleanup
  passes, a Myers bisect, and a generic Two-Way substring search.
- **Algorithm:** Myers divide-and-conquer diff is O((N+M)D) time with linear
  auxiliary space in the intended cases and quadratic worst-case work, followed
  by boundary, semantic, and merge cleanups. Two-Way substring search is linear.
- **Rust-specific code:** borrowed output slices and mutable range views should
  become source identifiers plus offsets/lengths in Dark; vector mutation and
  deque cleanup should remain linear rather than becoming repeated string
  copying. There is no unsafe, SIMD, concurrency, or platform-specific code.
- **Implemented workload:** `myers_diff` isolates the upstream engine's central
  shortest-edit-path search rather than porting Dissimilar's borrowed output
  views and cleanup passes. It generates repeated documents with a deterministic
  middle insertion, advances Myers diagonal frontiers in hash dictionaries,
  and prints the edit distance plus a checksum of every frontier endpoint.
  Both implementations use the same average-O(1) frontier architecture and
  preserve O((N+M)D) search time. Input construction happens once before the
  repeated searches.

### Radix-2 FFT — A: implemented as an independent kernel

- **Reference:** <https://github.com/tarcieri/microfft> and the standard
  Cooley-Tukey radix-2 decomposition. No upstream source is vendored.
- **Architecture:** a local complex-number type, deterministic trigonometric
  input, recursive even/odd decomposition, and a linear twiddle-factor combine
  at each level. Both implementations intentionally use allocating collections
  rather than an in-place/SIMD specialization because Dark has no public
  mutable float array in the benchmark surface.
- **Algorithm and workload:** O(n log n) time and O(n log n) aggregate
  allocation for power-of-two argv sizes. The transform is repeated over one
  generated input and reduced to an exact quantized checksum. There are no
  dependencies, unsafe operations, SIMD intrinsics, or platform-specific code.

### regex-lite-shaped matcher — B: implemented bounded subset

- **Reference:** <https://github.com/rust-lang/regex/tree/master/regex-lite>.
  The complete crate is substantially larger than a single-file benchmark, so
  this is an independent compact subset rather than a source port.
- **Architecture:** a hand-written parser produces alternatives of atom and
  quantifier records. The matcher propagates possible byte positions instead
  of recursively backtracking, so ambiguous repetition cannot cause exponential
  behavior. Supported syntax is literals, `.`, byte ranges, top-level `|`, and
  `*`, `+`, and `?`.
- **Workload:** parse one deterministic pattern, scan a repeated mixed-match
  haystack, and print a match-count/position checksum. Compilation occurs once;
  argv repetitions exercise matching. Both sources use the same parser grammar,
  state expansion order, and allocation behavior, with no external dependency.

### Warden-shaped expression language — B: implemented with adaptation

- **Reference:** <https://github.com/Conalh/warden>. The benchmark independently
  follows its hand-written-lexer and Pratt-parser shape; it does not include
  the upstream CLI or filesystem-facing policy features.
- **Architecture:** lexing produces an indexed token arena, Pratt precedence
  parsing recursively evaluates integer variables, literals, parentheses,
  arithmetic, comparison, and semicolon-delimited statements. The benchmark
  repeats a generated in-memory program under deterministic changing bindings
  and prints a weighted result checksum.
- **Adaptation:** retaining nested recursive expression values across parser
  returns currently triggers a reproducible Dark runtime failure. The checked-in
  pair therefore performs direct evaluation while the Pratt parser unwinds.
  This preserves linear lexing/parsing and recursive precedence work, but does
  not yet benchmark retained AST allocation; it should be upgraded when that
  runtime limitation is removed.

### Satsuma — C: reject this upstream; reconsider SAT with another reference

- **Source:** <https://github.com/JulianKnodt/satsuma> at
  `b4f3c4a4759551c829ab16ad8e3eda8911d1b0e5` (unreleased 0.1.0).
- **Size and dependencies:** 1,149 core production lines, 1,186 including its
  CLI. `priority-queue`, `rustc-hash`, and `hashbrown` are central performance
  containers but replaceable by standard-library heaps/maps; optional `clap`
  is CLI-only and unnecessary for an in-memory benchmark.
- **Tests and benchmarks:** one literal-encoding unit test and no benchmarks.
  The source does not build on stable Rust 1.89 because it retains obsolete or
  removed feature gates.
- **Architecture and data:** MiniSat-shaped CDCL solver, packed literals and
  clause references, flat clause database, two-watched-literal hash maps,
  assignment/decision-level vectors, VSIDS-like priority queue, Luby restarts,
  statistics, and a DIMACS parser.
- **Algorithm:** watched-literal propagation, conflict analysis, clause
  learning, non-chronological backtracking, activity-based branching, database
  compaction, and restarts; worst-case solving remains exponential.
- **Rust-specific code:** nine unsafe sites cover unchecked indexing and raw
  slice construction over the packed database. They are performance shortcuts,
  not algorithm requirements, but the port would also need extensive mutable
  random-access state and careful watch-list invariants. There is no SIMD or
  platform-specific code.
- **Possible workload elsewhere:** generate fixed planted 3-SAT and pigeonhole
  CNFs in memory, solve satisfiable and unsatisfiable cases separately, validate
  any model against every clause, and print status/model checksums. Generation
  happens once per process. This exact project is rejected because stale nightly
  requirements, unsafe-heavy representation, and very weak tests make it a poor
  auditable baseline. Estimated difficulty: high.
