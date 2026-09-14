# Benchmark Results

Best-known compatible full-profile Dark performance vs audited Rust references (instruction counts).

**Snapshot timestamp:** 2026-09-14T00:25:36+00:00
**Architecture:** `arm64`
**Profile:** `full` (schema 2)
**Measurement policy:** `cachegrind-ir-v1:cache-sim=yes,branch-sim=yes,extract=summary-I-refs`
**Workload contract:** `192b215fe4cb83b04d99c505183d561edf9e0ef149a430e4153b3e9ff95d8c16`
**Compiler commit:** `e6db8ae33491b8749fa655c186076b5b1bf46ba6` - Remove legacy operator syntax and conversion tooling

| Benchmark | Dark (17.4x) | Rust |
|---|---:|---:|
| ackermann | 9,303,381,642 (1.62x) | 5,725,442,001 |
| binary_trees | 537,695,282 (0.17x) | 3,186,862,486 |
| collatz | 70,194,528 (0.91x) | 76,735,859 |
| edigits | 1,724,752,465 (122x) | 14,174,361 |
| factorial | 63,745 (0.07x) | 972,619 |
| fannkuch | 23,918,821,535 (186x) | 128,622,475 |
| fasta | 7,719,486,910 (360x) | 21,447,429 |
| fft | 4,578,662,331 (2148x) | 2,131,783 |
| fib | 388,194,915 (1.42x) | 272,529,831 |
| huffman | 6,442,997,915 (148x) | 43,624,468 |
| leibniz | 850,007,272 (1.21x) | 700,259,051 |
| mandelbrot | 15,227,643 (1.21x) | 12,557,401 |
| matmul | 2,130,138,761 (126x) | 16,952,036 |
| merkletrees | 193,340,287 (1.71x) | 113,308,410 |
| myers_diff | 30,802,188,851 (26745x) | 1,151,692 |
| nbody | 726,507,592 (3.64x) | 199,759,607 |
| nqueen | 207,721,884 (1.31x) | 158,617,689 |
| nsieve | 4,305,616,573 (1509x) | 2,852,993 |
| pisum | 40,024,117 (0.88x) | 45,262,023 |
| primes | 1,468,580 (1.17x) | 1,260,314 |
| quicksort | 192,723,619 (31.5x) | 6,115,178 |
| raytracer | 2,496,915,202 (13.0x) | 192,123,273 |
| regex_lite | 4,408,024,171 (1400x) | 3,147,470 |
| spectral_norm | 229,725,034 (45.0x) | 5,106,615 |
| string_equality | 58,060,641 (0.96x) | 60,769,660 |
| sum_to_n | 62,220 (0.24x) | 260,513 |
| tak | 48,017,359 (0.12x) | 391,111,104 |
| tinytemplate | 34,888,976,276 (56194x) | 620,867 |
| warden | 2,105,453,216 (6056x) | 347,689 |
