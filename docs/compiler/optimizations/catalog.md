# Optimization Catalog

This is a compact map of retained optimization behavior. Source and focused
before/after fixtures are authoritative; generated benchmark documents own
current performance measurements. Historical trial ratios and sandbox-session
notes are intentionally omitted because they become stale and are recoverable
from Git history.

## Retained transformation index

- **Constant folding:** Float negation, absolute value, square root,
  Int64-to-Float, Float-to-Int64, Float-to-bits, Float comparisons, string
  concatenation, UInt64 comparisons, typed canonical-buffer equality, Int64
  shifts, Int64 bitwise operations, UInt64 bitwise operations, and UInt64
  arithmetic.
- **Algebraic simplification:** signed and unsigned division/modulo identities;
  integer, Float, Boolean, and string self-comparisons; bitwise and Boolean
  idempotence, absorption, complement, zero, and all-ones rules; empty string
  concatenation; double not/negation; Float absolute-value rules; identical and
  Boolean-literal branches; negated branch/comparison folding; shift identities;
  subtraction/addition identities; integer reassociation, cancellation, and
  common-factor combination; and safe Float/UInt64 multiplicative identities.
- **Strength reduction:** integer self-addition, Float multiplication by two,
  and signed/unsigned multiplication, division, and modulo by powers of two.
- **Common-subexpression elimination:** dominator-scoped MIR scalar reuse,
  effect-free direct-call reuse, barrier-aware scalar heap-load reuse through
  supported pure scalar operations, and ANF pure-value reuse with commutative
  and reversed-relational canonicalization.
- **Interprocedural and aggregate work:** uniform literal direct-parameter
  propagation, bounded scalar-literal cloning, ownership-safe tuple projection
  forwarding, projection-only scalar tuple and record replacement, and unused
  ANF binding elimination.
- **Loops and control flow:** bounded recursive-loop unrolling, tail recursion
  modulo wrapping addition or multiplication, effect-free call and Float-load
  hoisting, affine induction reduction, factor-two counted-loop unrolling,
  same-target and redundant-successor branches, fallthrough block placement,
  and linear block merging.
- **Instruction selection and allocation:** ARM64 bit-clear fusion, addition
  with a single-use negation, dead multiply-subtract and Float-copy fusion,
  ARM64 entry-parameter copy elimination, and floating-point phi coalescing.
- **Code motion and closures:** shared leading conditional-binding hoisting and
  capture-free local closure devirtualization.

## ANF simplification

`passes/2.3_ANF_Optimize.fs` and `src/Tests/optimization/anf.opt` own:

- literal folding for float negation, absolute value, square root,
  Int64/Float conversions, Float bit conversion and comparison, string
  concatenation, typed `CanonicalBufferEq`, and UInt64 arithmetic, comparison,
  shifts, and bitwise work;
- integer, UInt64, Float, Boolean, bitwise, shift, negation, comparison, and
  empty-string identities;
- conditional simplification, negated-condition folding, integer
  reassociation/cancellation/factorization, and safe multiplication/division
  strength reduction;
- common-subexpression reuse through a dedicated, exhaustive value key for
  conditional values, tuple projections, non-owning non-Float scalar record
  projections, scalar conversions, Float unary operations, commutative
  operations, and reversed relational comparisons;
- ownership-safe local tuple projection forwarding and unused-binding
  elimination; and
- shared leading conditional binding hoisting and capture-free local closure
  devirtualization.

Floating-point rewrites retain NaN, signed-zero, rounding, overflow, and
evaluation-order restrictions. Managed-value forwarding retains ownership
restrictions. Allocations, mutable-memory observations, managed and Float
record projections, calls, and ownership operations are not merged. Focused
negative fixtures are part of each transformation's contract.

## Direct-call specialization

`passes/2.4.5_ANF_DirectCallSpecialization.fs` specializes internal direct
calls when callee identity and scalar literal arguments are statically known.
It supports uniform literal parameter removal and bounded scalar-literal
cloning while retaining fallbacks and excluding address-taken, closure, and
managed-string cases. The focused specialization tests own caps, recursive
signatures, float-bit identity, indirect-use exclusions, and ownership rules.

## Escape analysis and scalar replacement

`passes/2.4.6_ANF_EscapeAnalysis.fs` removes fixed-layout tuple and record
allocations whose fields are non-floating immediate scalar values and whose
complete lexical use set consists only of projections, local aliases, and
representation-only record-clone sources. Escaping clones retain their own
allocation even when an eligible source allocation is removed.

Returns, calls, closure capture, storage, raw operations, managed fields,
floating-point fields, and unknown uses preserve allocation. Float aggregates
remain excluded because extending their field live ranges can exceed the
current non-spilling Float register allocator. Focused tests cover the accepted
projection, alias, clone, and branch shapes plus each conservative boundary.

## MIR optimization

`passes/3.5_MIR_Optimize.fs` and `src/Tests/optimization/mir.opt` own:

- dominator-scoped scalar and effect-free-call common-subexpression reuse;
- barrier-aware exact scalar heap-load reuse through `FloatSqrt`, `FloatAbs`,
  `Int64ToFloat`, `FloatToInt64`, and `FloatToBits` locally, without exporting
  availability into dominated blocks;
- bounded recursive-loop unrolling, tail recursion modulo wrapping Int64
  addition or multiplication, effect-free call hoisting, affine induction
  reduction, and narrow counted-loop unrolling;
- same-target and redundant-successor branch elimination; and
- linear basic-block merging with typed phi repair.

Memory, allocation, ownership, unknown-call, managed-result, and aliasing
barriers remain conservative unless a focused proof says otherwise. `FloatNeg`
remains a load-availability boundary because extending loads across long
negated reductions exceeds the current non-spilling Float register allocator.

## LIR, allocation, and backend optimization

`passes/4.5_LIR_Peephole.fs`, `passes/5_RegisterAllocation.fs`, and the native
backends own:

- floating constant-load motion;
- dead multiply/subtract and floating arithmetic-copy fusion;
- ARM64 bit-clear fusion;
- floating-point phi coalescing;
- authoritative entry-parameter placement; and
- CFG block placement that exposes native fallthroughs.

The LIR fixtures, allocation tests, and target-specific generated-code tests
own register-liveness, interference, flags, and instruction-encoding safety.

## Rejected trials

| Trial | Why it is not retained | Revisit condition |
|---|---|---|
| Dead direct-parameter elimination | Added whole-program signature machinery but produced no measured workload improvement | A representative workload contains hot provably dead direct parameters |
| General captured-closure scalarization | A local one-scalar-capture prototype did not improve a real workload | A known higher-order chain, such as List filtering, demonstrates benefit while preserving capture ownership |

Do not treat a rejected trial as a permanent prohibition. Require a new
workload or correctness argument before rebuilding the discarded machinery.
