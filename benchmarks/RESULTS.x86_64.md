# x86_64 QEMU Benchmark Results

Canonical quick-profile guest instruction counts under pinned QEMU.

**Compiler:** `b25efe54556edbd12c839f2bbef1c98b1288ba7e` - Complete point-in-time x64 parity audit
**Generated:** 2026-09-14T02:49:16.367224+00:00
**Track:** `x86_64-quick-qemu`
**Measurement policy:** `qemu-tcg-plugin-guest-insns-v1:qemu-11.1.1:rustc-1.89.0`
**Overall Dark/Rust:** `4.553932×`

| Benchmark | Dark instructions | Rust instructions | Dark/Rust |
| --- | ---: | ---: | ---: |
| ackermann | 2,849,810 | 1,751,854 | 1.627× |
| binary_trees | 1,278,194 | 4,497,159 | 0.284× |
| collatz | 926,709 | 840,256 | 1.103× |
| edigits | 6,698,480 | 318,276 | 21.046× |
| factorial | 17,787 | 297,458 | 0.060× |
| fannkuch | 845,201 | 293,133 | 2.883× |
| fasta | 11,912,187 | 319,976 | 37.228× |
| fft | 528,598,657 | 492,957 | 1072.302× |
| fib | 346,092 | 510,019 | 0.679× |
| huffman | 23,161,076 | 333,601 | 69.427× |
| leibniz | 2,158,649 | 988,518 | 2.184× |
| mandelbrot | 1,378,291 | 1,252,738 | 1.100× |
| matmul | 74,981 | 297,209 | 0.252× |
| merkletrees | 602,837 | 647,831 | 0.931× |
| myers_diff | 2,204,891,807 | 528,834 | 4169.346× |
| nbody | 225,893 | 350,096 | 0.645× |
| nqueen | 533,649 | 689,923 | 0.773× |
| nsieve | 1,155,278 | 291,805 | 3.959× |
| pisum | 166,006 | 415,114 | 0.400× |
| primes | 147,690 | 326,061 | 0.453× |
| quicksort | 697,448 | 307,761 | 2.266× |
| raytracer | 2,783,847 | 488,449 | 5.699× |
| regex_lite | 26,407,486 | 341,526 | 77.322× |
| spectral_norm | 776,631 | 298,184 | 2.605× |
| string_equality | 810,743 | 743,734 | 1.090× |
| sum_to_n | 64,787 | 290,077 | 0.223× |
| tak | 54,879,270 | 42,360,536 | 1.296× |
| tinytemplate | 47,135,884,452 | 670,619 | 70287.129× |
| warden | 19,554,113 | 266,488 | 73.377× |
