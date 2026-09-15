# x86_64 QEMU Benchmark Results

Canonical quick-profile guest instruction counts under pinned QEMU.

**Compiler:** `e7954cd317a4e9df891d8ccf93a03e47c243766c` - Optimize public Int64 float conversion
**Generated:** 2026-09-15T20:45:50.212199+00:00
**Track:** `x86_64-quick-qemu`
**Measurement policy:** `qemu-tcg-plugin-guest-insns-v1:qemu-11.1.1:rustc-1.89.0`
**Overall Dark/Rust:** `4.632612×`

| Benchmark | Dark instructions | Rust instructions | Dark/Rust |
| --- | ---: | ---: | ---: |
| ackermann | 2,849,810 | 1,751,820 | 1.627× |
| binary_trees | 1,278,194 | 4,497,789 | 0.284× |
| collatz | 926,709 | 840,205 | 1.103× |
| edigits | 6,698,480 | 318,369 | 21.040× |
| factorial | 17,787 | 298,180 | 0.060× |
| fannkuch | 845,201 | 237,180 | 3.564× |
| fasta | 11,912,187 | 319,196 | 37.319× |
| fft | 528,590,727 | 492,853 | 1072.512× |
| fib | 346,092 | 510,153 | 0.678× |
| huffman | 27,214,753 | 390,542 | 69.685× |
| leibniz | 2,158,704 | 988,845 | 2.183× |
| mandelbrot | 1,378,291 | 1,253,128 | 1.100× |
| matmul | 74,981 | 297,381 | 0.252× |
| merkletrees | 602,837 | 648,332 | 0.930× |
| myers_diff | 3,792,789,291 | 529,042 | 7169.165× |
| nbody | 225,954 | 350,109 | 0.645× |
| nqueen | 533,649 | 690,214 | 0.773× |
| nsieve | 1,155,278 | 291,688 | 3.961× |
| pisum | 166,064 | 414,545 | 0.401× |
| primes | 174,137 | 382,856 | 0.455× |
| quicksort | 697,448 | 307,853 | 2.266× |
| raytracer | 2,903,079 | 432,190 | 6.717× |
| regex_lite | 31,947,900 | 285,453 | 111.920× |
| spectral_norm | 1,693,382 | 298,183 | 5.679× |
| string_equality | 810,743 | 743,341 | 1.091× |
| sum_to_n | 64,787 | 290,038 | 0.223× |
| tak | 54,879,270 | 42,395,241 | 1.294× |
| tinytemplate | 10,486,538,239 | 670,683 | 15635.611× |
| warden | 20,514,739 | 301,241 | 68.101× |
