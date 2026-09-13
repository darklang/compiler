# Benchmark Results

Best-known compatible routine-profile Dark performance vs audited Rust references (instruction counts).

**Snapshot timestamp:** 2026-09-13T19:20:36+00:00
**Architecture:** `arm64`
**Profile:** `routine` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=yes,branch-sim=yes,extract=summary-I-refs`
**Workload contract:** `218650a6c491f640af51e280af9f08cfd9dedff693cf1f3bfeb8cd6e1c418cff`
**Compiler commit:** `b2d48df5331c9b2a3a77cd3f5172ee8683d7cb6c` - Run full fannkuch and include nsieve

| Benchmark | Dark (3.64x) | Rust |
|---|---:|---:|
| ackermann | 9,303,382,264 (1.62x) | 5,725,442,001 |
| binary_trees | 537,695,950 (0.17x) | 3,186,862,486 |
| collatz | 70,194,957 (0.91x) | 76,735,859 |
| edigits | 3,334,771,646 (235x) | 14,174,361 |
| factorial | 64,689 (0.07x) | 972,619 |
| fannkuch | 23,727,526,579 (184x) | 128,622,475 |
| fasta | 454,540,290 (21.2x) | 21,447,429 |
| fib | 388,195,321 (1.42x) | 272,529,831 |
| huffman | 1,922,458,544 (44.1x) | 43,624,468 |
| leibniz | 850,007,724 (1.21x) | 700,259,051 |
| mandelbrot | 15,228,265 (1.21x) | 12,557,401 |
| matmul | 931,350,394 (54.9x) | 16,952,036 |
| merkletrees | 193,341,001 (1.71x) | 113,308,410 |
| nbody | 883,008,023 (4.42x) | 199,759,607 |
| nqueen | 207,722,244 (1.31x) | 158,617,689 |
| nsieve | 557,297,189 (2.15x) | 259,624,566 |
| pisum | 40,024,923 (0.88x) | 45,262,023 |
| primes | 1,468,917 (1.17x) | 1,260,314 |
| quicksort | 179,288,556 (29.3x) | 6,115,178 |
| spectral_norm | 63,962,040 (12.5x) | 5,106,615 |
| string_equality | 58,061,865 (0.96x) | 60,769,660 |
| sum_to_n | 62,911 (0.24x) | 260,513 |
| tak | 48,018,413 (0.12x) | 391,111,104 |
| tinytemplate | 1,022,670,136 (1647x) | 620,847 |
