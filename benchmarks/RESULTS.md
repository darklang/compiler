# Benchmark Results

Best-known compatible routine-profile Dark performance vs audited Rust references (instruction counts).

**Snapshot timestamp:** 2026-09-13T15:46:36+00:00
**Architecture:** `arm64`
**Profile:** `routine` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=yes,branch-sim=yes,extract=summary-I-refs`
**Workload contract:** `db2cd8833adfd66c2883d7b7f8e77c89862e90320e23b15c729115ecb5550cdf`
**Compiler commit:** `a22bd5dd1af2c35fc094ada1130f6f705af45561` - Enable ANF optimization for stdlib

| Benchmark | Dark (3.32x) | Rust |
|---|---:|---:|
| ackermann | 9,303,384,465 (1.62x) | 5,725,441,814 |
| binary_trees | 537,698,160 (0.17x) | 3,186,860,117 |
| collatz | 70,196,062 (0.91x) | 76,735,592 |
| edigits | 3,364,448,413 (237x) | 14,173,715 |
| factorial | 66,899 (0.07x) | 970,224 |
| fasta | 458,941,236 (21.4x) | 21,447,164 |
| fib | 388,196,426 (1.42x) | 272,529,537 |
| huffman | 1,958,359,613 (44.9x) | 43,624,109 |
| leibniz | 850,008,829 (1.21x) | 700,258,771 |
| mandelbrot | 15,230,475 (1.21x) | 12,557,210 |
| matmul | 931,351,499 (54.9x) | 16,951,735 |
| merkletrees | 193,343,211 (1.71x) | 113,310,447 |
| nbody | 883,009,128 (4.42x) | 199,759,342 |
| nqueen | 207,723,349 (1.31x) | 158,617,384 |
| pisum | 40,027,133 (0.88x) | 45,261,710 |
| primes | 1,470,022 (1.17x) | 1,260,022 |
| quicksort | 179,365,685 (29.3x) | 6,114,981 |
| spectral_norm | 63,964,250 (12.5x) | 5,106,472 |
| sum_to_n | 65,121 (0.25x) | 260,154 |
| tak | 48,022,824 (0.12x) | 391,109,981 |
| tinytemplate | 1,067,943,979 (1720x) | 620,727 |
