# Benchmark Results

Best-known compatible routine-profile Dark performance vs audited Rust references (instruction counts).

**Snapshot timestamp:** 2026-09-13T17:30:22+00:00
**Architecture:** `arm64`
**Profile:** `routine` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=yes,branch-sim=yes,extract=summary-I-refs`
**Workload contract:** `2f4e8d149d0eb7a5bbbbea471086afae5bb34d6a687e7d102256a0cde0fcfcc8`
**Compiler commit:** `24e4e7c8e4cb7b457af2b7674e823f7869d94409` - Inline trivial stdlib helpers

| Benchmark | Dark (3.12x) | Rust |
|---|---:|---:|
| ackermann | 9,303,382,264 (1.62x) | 5,725,442,084 |
| binary_trees | 537,695,950 (0.17x) | 3,186,862,490 |
| collatz | 70,194,957 (0.91x) | 76,735,880 |
| edigits | 3,364,446,203 (237x) | 14,174,258 |
| factorial | 64,689 (0.07x) | 970,494 |
| fasta | 458,940,131 (21.4x) | 21,447,439 |
| fib | 388,195,321 (1.42x) | 272,529,792 |
| huffman | 1,922,458,560 (44.1x) | 43,624,456 |
| leibniz | 850,007,724 (1.21x) | 700,259,083 |
| mandelbrot | 15,228,265 (1.21x) | 12,557,502 |
| matmul | 931,350,394 (54.9x) | 16,952,044 |
| merkletrees | 193,341,001 (1.71x) | 113,308,384 |
| nbody | 883,008,023 (4.42x) | 199,759,617 |
| nqueen | 207,722,244 (1.31x) | 158,617,689 |
| pisum | 40,024,923 (0.88x) | 45,261,987 |
| primes | 1,468,917 (1.17x) | 1,260,320 |
| quicksort | 179,363,475 (29.3x) | 6,115,251 |
| spectral_norm | 63,962,040 (12.5x) | 5,106,614 |
| string_equality | 58,061,865 (0.96x) | 60,769,629 |
| sum_to_n | 62,911 (0.24x) | 260,484 |
| tak | 48,018,413 (0.12x) | 391,110,892 |
| tinytemplate | 1,022,685,005 (1647x) | 620,865 |
