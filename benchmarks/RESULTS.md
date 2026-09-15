# Benchmark Results

Best-known compatible full-profile Dark performance vs audited Rust references, with diagnostic reference runtimes (instruction counts).

**Snapshot timestamp:** 2026-09-15T18:11:48+00:00
**Architecture:** `arm64`
**Profile:** `full` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=no,branch-sim=no,extract=summary-I-refs`
**Workload contract:** `a3f0951d008a808f6dd71f5c8991d263f4eb1219618f6c801c33329c19cd5081`
**Compiler commit:** `13ed01b4b84e5fbcbe71f0de80bafbd12d905bc5` - Align compiler documentation with stage ownership
**Diagnostic references:** informational only; multipliers are instructions divided by Rust for the same workload.
Every displayed diagnostic row matched the profile's expected stdout.

| Benchmark | Dark (10.8x) | Rust | Darklang interpreter | Node | OCaml | Python |
|---|---:|---:|---:|---:|---:|---:|
| ackermann | 145,114,137 (1.62x) | 89,558,784 | - | - | - | - |
| binary_trees | 11,053,078 (0.17x) | 63,993,594 | - | - | - | - |
| collatz | 70,194,399 (0.91x) | 76,735,737 | - | - | - | - |
| edigits | 51,754,907 (75.1x) | 688,749 | - | - | - | - |
| factorial | 63,466 (0.07x) | 970,290 | - | - | - | - |
| fannkuch | 233,405,898 (133x) | 1,760,885 | - | - | - | - |
| fasta | 154,617,365 (225x) | 686,768 | - | - | - | - |
| fft | 407,130,735 (896x) | 454,262 | - | - | - | - |
| fib | 8,268,586 (1.37x) | 6,054,417 | - | - | - | - |
| huffman | 400,267,806 (140x) | 2,868,263 | - | - | - | - |
| leibniz | 17,006,789 (1.19x) | 14,258,894 | - | - | - | - |
| mandelbrot | 15,227,448 (1.21x) | 12,557,270 | - | - | - | - |
| matmul | 42,560,590 (65.5x) | 649,416 | - | - | - | - |
| merkletrees | 3,877,426 (1.54x) | 2,523,420 | - | - | - | - |
| myers_diff | 2,400,143,043 (3493x) | 687,142 | - | - | - | - |
| nbody | 14,537,286 (3.42x) | 4,249,498 | - | - | - | - |
| nqueen | 7,420,424 (1.25x) | 5,914,962 | - | - | - | - |
| nsieve | 131,101,393 (345x) | 380,354 | - | - | - | - |
| pisum | 812,434 (0.70x) | 1,160,413 | - | - | - | - |
| primes | 1,468,475 (1.17x) | 1,260,125 | - | - | - | - |
| quicksort | 188,808,247 (31.0x) | 6,095,209 | - | - | - | - |
| raytracer | 49,162,502 (12.1x) | 4,067,427 | - | - | - | - |
| regex_lite | 67,502,284 (165x) | 409,856 | - | - | - | - |
| spectral_norm | 221,902,869 (43.5x) | 5,106,524 | - | - | - | - |
| string_equality | 1,219,843 (0.82x) | 1,479,564 | - | - | - | - |
| sum_to_n | 62,007 (0.24x) | 260,246 | - | - | - | - |
| tak | 48,017,020 (0.12x) | 391,110,808 | - | - | - | - |
| tinytemplate | 8,606,428,391 (20474x) | 420,354 | - | - | - | - |
| warden | 44,489,020 (165x) | 270,345 | - | - | - | - |
