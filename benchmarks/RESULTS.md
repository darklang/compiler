# Benchmark Results

Best-known compatible routine-profile Dark performance vs audited Rust references (instruction counts).

**Snapshot timestamp:** 2026-09-13T16:53:34+00:00
**Architecture:** `arm64`
**Profile:** `routine` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=yes,branch-sim=yes,extract=summary-I-refs`
**Workload contract:** `2f4e8d149d0eb7a5bbbbea471086afae5bb34d6a687e7d102256a0cde0fcfcc8`
**Compiler commit:** `7b5d4ce0990c44bfafc3e19fd8542f7b75d52332` - Lower canonical string equality directly

| Benchmark | Dark (3.13x) | Rust |
|---|---:|---:|
| ackermann | 9,303,383,850 (1.62x) | 5,725,442,084 |
| binary_trees | 537,697,536 (0.17x) | 3,186,862,490 |
| collatz | 70,195,750 (0.91x) | 76,735,880 |
| edigits | 3,364,447,789 (237x) | 14,174,258 |
| factorial | 66,275 (0.07x) | 970,494 |
| fasta | 458,940,924 (21.4x) | 21,447,439 |
| fib | 388,196,114 (1.42x) | 272,529,792 |
| huffman | 1,922,782,076 (44.1x) | 43,624,456 |
| leibniz | 850,008,517 (1.21x) | 700,259,083 |
| mandelbrot | 15,229,851 (1.21x) | 12,557,502 |
| matmul | 931,351,187 (54.9x) | 16,952,044 |
| merkletrees | 193,342,587 (1.71x) | 113,308,384 |
| nbody | 883,008,816 (4.42x) | 199,759,617 |
| nqueen | 207,723,037 (1.31x) | 158,617,689 |
| pisum | 40,026,509 (0.88x) | 45,261,987 |
| primes | 1,469,710 (1.17x) | 1,260,320 |
| quicksort | 179,365,061 (29.3x) | 6,115,251 |
| spectral_norm | 63,963,626 (12.5x) | 5,106,614 |
| string_equality | 58,063,451 (0.96x) | 60,769,629 |
| sum_to_n | 64,497 (0.25x) | 260,484 |
| tak | 48,021,585 (0.12x) | 391,110,892 |
| tinytemplate | 1,066,875,529 (1718x) | 620,865 |
