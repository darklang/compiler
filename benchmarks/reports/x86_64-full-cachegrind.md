# x86_64-full-cachegrind comparison

Architecture: `x86_64`. Profile: `full`.

Measurement policy: `cachegrind-ir-v1:cache-sim=no,branch-sim=no,extract=summary-I-refs`.

Current workload contract: `5547f5e73180619654c2f62a0ee6db7b48b8236b19fb16cb30836e856584dc87`.

Instruction counts include runtime and startup work; they are not elapsed-time speed ratios.

Snapshots are independent. Stale values are retained for provenance and excluded from ratios.

## Stored versions and coverage

| Language             | Version / compiler commit                | Measured                  | Current rows | Audited current rows | Build flags  | Runtime flags |
| -------------------- | ---------------------------------------- | ------------------------- | ------------ | -------------------- | ------------ | ------------- |
| Darklang             | 7608be218b01c26e89f27e4decbc2af0cdd834f4 | 2026-11-08T02:21:55+00:00 | 29/29        | 29/29                | not recorded | not recorded  |
| Rust                 | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |
| Haskell              | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |
| Python               | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |
| Node                 | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |
| Roc                  | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |
| OCaml 5              | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |
| Koka                 | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |
| Darklang interpreter | not recorded                             | not recorded              | 0/29         | 0/0                  | not recorded | not recorded  |

Audited means the implementation's algorithm and source have a current reviewed parity contract.
Output validation alone does not establish algorithm parity.

## Absolute instructions

| Benchmark       |    Darklang |         Rust |      Haskell |       Python |         Node |          Roc |      OCaml 5 |         Koka | Darklang interpreter |
| --------------- | ----------: | -----------: | -----------: | -----------: | -----------: | -----------: | -----------: | -----------: | -------------------: |
| ackermann       | 184,178,257 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| binary_trees    |  12,916,856 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| collatz         | 185,401,384 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| edigits         | 124,281,120 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| factorial       |      64,236 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| fannkuch        | 435,308,341 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| fasta           |  23,439,305 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| fft             |   2,475,741 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| fib             |   9,856,403 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| huffman         | 240,159,462 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| leibniz         |  43,010,045 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| mandelbrot      |  21,677,813 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| matmul          |  75,290,059 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| merkletrees     |   3,775,444 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| myers_diff      |   5,558,396 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| nbody           |  11,878,934 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| nqueen          |  10,714,603 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| nsieve          | 511,146,323 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| pisum           |   1,512,661 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| primes          |   4,507,275 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| quicksort       | 431,665,965 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| raytracer       |  75,098,125 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| regex_lite      |   1,344,375 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| spectral_norm   | 687,899,886 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| string_equality |   1,497,022 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| sum_to_n        |      62,145 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| tak             |  56,735,194 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| tinytemplate    | 108,091,349 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |
| warden          |     913,594 | not measured | not measured | not measured | not measured | not measured | not measured | not measured |         not measured |

## Instructions relative to Rust

Ratios for unaudited implementations are informational comparisons of validated output.

| Benchmark       |    Darklang |        Rust |     Haskell |      Python |        Node |         Roc |     OCaml 5 |        Koka | Darklang interpreter |
| --------------- | ----------: | ----------: | ----------: | ----------: | ----------: | ----------: | ----------: | ----------: | -------------------: |
| ackermann       | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| binary_trees    | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| collatz         | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| edigits         | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| factorial       | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| fannkuch        | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| fasta           | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| fft             | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| fib             | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| huffman         | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| leibniz         | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| mandelbrot      | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| matmul          | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| merkletrees     | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| myers_diff      | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| nbody           | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| nqueen          | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| nsieve          | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| pisum           | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| primes          | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| quicksort       | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| raytracer       | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| regex_lite      | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| spectral_norm   | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| string_equality | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| sum_to_n        | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| tak             | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| tinytemplate    | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |
| warden          | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable | unavailable |          unavailable |

## Aggregate comparisons

Fixed workload sets keep averages comparable as language coverage grows.
Geometric means are informational unless every included row is audited.

### Original 21 workloads

`ackermann`, `binary_trees`, `collatz`, `edigits`, `factorial`, `fannkuch`, `fasta`, `fib`, `leibniz`, `mandelbrot`, `matmul`, `merkletrees`, `nbody`, `nqueen`, `nsieve`, `pisum`, `primes`, `quicksort`, `spectral_norm`, `sum_to_n`, `tak`.

| Language             | Instructions / Rust | Total instructions | Current rows | Parity     |
| -------------------- | ------------------: | -----------------: | -----------: | ---------- |
| Darklang             |         unavailable |        unavailable |        21/21 | incomplete |
| Rust                 |         unavailable |        unavailable |         0/21 | incomplete |
| Haskell              |         unavailable |        unavailable |         0/21 | incomplete |
| Python               |         unavailable |        unavailable |         0/21 | incomplete |
| Node                 |         unavailable |        unavailable |         0/21 | incomplete |
| Roc                  |         unavailable |        unavailable |         0/21 | incomplete |
| OCaml 5              |         unavailable |        unavailable |         0/21 | incomplete |
| Koka                 |         unavailable |        unavailable |         0/21 | incomplete |
| Darklang interpreter |         unavailable |        unavailable |         0/21 | incomplete |

### All 29 workloads

`ackermann`, `binary_trees`, `collatz`, `edigits`, `factorial`, `fannkuch`, `fasta`, `fft`, `fib`, `huffman`, `leibniz`, `mandelbrot`, `matmul`, `merkletrees`, `myers_diff`, `nbody`, `nqueen`, `nsieve`, `pisum`, `primes`, `quicksort`, `raytracer`, `regex_lite`, `spectral_norm`, `string_equality`, `sum_to_n`, `tak`, `tinytemplate`, `warden`.

| Language             | Instructions / Rust | Total instructions | Current rows | Parity     |
| -------------------- | ------------------: | -----------------: | -----------: | ---------- |
| Darklang             |         unavailable |        unavailable |        29/29 | incomplete |
| Rust                 |         unavailable |        unavailable |         0/29 | incomplete |
| Haskell              |         unavailable |        unavailable |         0/29 | incomplete |
| Python               |         unavailable |        unavailable |         0/29 | incomplete |
| Node                 |         unavailable |        unavailable |         0/29 | incomplete |
| Roc                  |         unavailable |        unavailable |         0/29 | incomplete |
| OCaml 5              |         unavailable |        unavailable |         0/29 | incomplete |
| Koka                 |         unavailable |        unavailable |         0/29 | incomplete |
| Darklang interpreter |         unavailable |        unavailable |         0/29 | incomplete |
