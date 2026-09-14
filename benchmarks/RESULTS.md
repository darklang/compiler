# Benchmark Results

Best-known compatible full-profile Dark performance vs audited Rust references (instruction counts).

**Snapshot timestamp:** 2026-09-14T21:31:26+00:00
**Architecture:** `arm64`
**Profile:** `full` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=no,branch-sim=no,extract=summary-I-refs`
**Workload contract:** `7a187054fa812f1f89d35f3dd3176db95059371d4c096ff03227313ac338038d`
**Compiler commit:** `750de683dc06d9df4dd5b83d550fd55210bcc7b3` - Count benchmark instructions without cache simulation

| Benchmark | Dark (10.9x) | Rust |
|---|---:|---:|
| ackermann | 145,114,326 (1.62x) | 89,558,784 |
| binary_trees | 11,053,279 (0.17x) | 63,993,594 |
| collatz | 70,194,528 (0.91x) | 76,735,737 |
| edigits | 52,884,224 (76.8x) | 688,749 |
| factorial | 63,745 (0.07x) | 970,290 |
| fannkuch | 239,915,558 (136x) | 1,760,885 |
| fasta | 157,197,960 (229x) | 686,768 |
| fft | 413,128,617 (909x) | 454,262 |
| fib | 8,268,703 (1.37x) | 6,054,417 |
| huffman | 408,375,089 (142x) | 2,868,263 |
| leibniz | 17,006,924 (1.19x) | 14,258,894 |
| mandelbrot | 15,227,643 (1.21x) | 12,557,270 |
| matmul | 43,156,675 (66.5x) | 649,416 |
| merkletrees | 3,877,645 (1.54x) | 2,523,420 |
| myers_diff | 2,480,164,309 (3609x) | 687,142 |
| nbody | 14,537,406 (3.42x) | 4,249,498 |
| nqueen | 7,420,529 (1.25x) | 5,914,962 |
| nsieve | 131,518,105 (346x) | 380,354 |
| pisum | 812,677 (0.70x) | 1,160,413 |
| primes | 1,468,580 (1.17x) | 1,260,125 |
| quicksort | 192,723,619 (31.6x) | 6,095,209 |
| raytracer | 49,546,721 (12.2x) | 4,067,427 |
| regex_lite | 69,462,277 (169x) | 409,856 |
| spectral_norm | 229,725,034 (45.0x) | 5,106,524 |
| string_equality | 1,220,167 (0.82x) | 1,479,564 |
| sum_to_n | 62,220 (0.24x) | 260,246 |
| tak | 48,017,359 (0.12x) | 391,110,808 |
| tinytemplate | 8,881,984,620 (21130x) | 420,354 |
| warden | 45,956,223 (170x) | 270,345 |
