# Benchmark Results

Best-known compatible routine-profile Dark performance vs audited Rust references (instruction counts).

**Snapshot timestamp:** 2026-09-13T22:31:53+00:00
**Architecture:** `arm64`
**Profile:** `routine` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=yes,branch-sim=yes,extract=summary-I-refs`
**Workload contract:** `3ec0fae5931a873cd7e23c62b9fdaccbda63b9851c381de469e16e6765b1d1f8`
**Compiler commit:** `59042c7fea57a6cd8783131ecbd53900d35ad81b` - Add five runtime application benchmarks

| Benchmark | Dark (9.52x) | Rust |
|---|---:|---:|
| ackermann | 9,303,381,642 (1.62x) | 5,725,442,001 |
| binary_trees | 537,695,282 (0.17x) | 3,186,862,486 |
| collatz | 70,194,528 (0.91x) | 76,735,859 |
| edigits | 3,334,770,978 (235x) | 14,174,361 |
| factorial | 63,745 (0.07x) | 972,619 |
| fannkuch | 23,681,380,693 (184x) | 128,622,475 |
| fasta | 454,539,838 (21.2x) | 21,447,429 |
| fft | 4,578,662,331 (2148x) | 2,131,524 |
| fib | 388,194,915 (1.42x) | 272,529,831 |
| huffman | 1,919,516,505 (44.0x) | 43,624,468 |
| leibniz | 850,007,272 (1.21x) | 700,259,051 |
| mandelbrot | 15,227,643 (1.21x) | 12,557,401 |
| matmul | 931,349,942 (54.9x) | 16,952,036 |
| merkletrees | 193,340,287 (1.71x) | 113,308,410 |
| myers_diff | 5,100,640,118 (4429x) | 1,151,630 |
| nbody | 726,507,592 (3.64x) | 199,759,607 |
| nqueen | 207,721,884 (1.31x) | 158,617,689 |
| nsieve | 557,295,690 (2.15x) | 259,624,566 |
| pisum | 40,024,117 (0.88x) | 45,262,023 |
| primes | 1,468,580 (1.17x) | 1,260,314 |
| quicksort | 179,287,842 (29.3x) | 6,115,178 |
| raytracer | 2,496,915,202 (13.0x) | 192,123,145 |
| regex_lite | 4,399,477,343 (1397x) | 3,149,570 |
| spectral_norm | 63,961,303 (12.5x) | 5,106,615 |
| string_equality | 58,060,641 (0.96x) | 60,769,660 |
| sum_to_n | 62,220 (0.24x) | 260,513 |
| tak | 48,017,359 (0.12x) | 391,111,104 |
| tinytemplate | 1,021,215,184 (1645x) | 620,847 |
| warden | 2,098,952,483 (6041x) | 347,458 |
