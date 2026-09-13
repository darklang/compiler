# Compiler-selected list arrays

The compiler has an initial closed-region implementation of immutable
`List<Int64>` computations using mutable array storage. No source syntax,
ownership annotation, public list type, or external calling convention changes.

## Implemented boundary

`ListHIR.fs` recognizes straight-line regions beginning with a list literal or
a supported list operation after monomorphization and lambda lifting, before
AST-to-ANF lowering destroys collection semantics. Supported operations are
`List.map<Int64, Int64>`, `List.reverse<Int64>`, and
`List.fold<Int64, Int64>`, plus runtime-sized construction with
`List.repeatUnsafe<Int64>(count: Int, value: Int64)`. Literal lengths are no
longer restricted to the small allocator's 28-element limit. Scalar bindings
and region results have type `Int64` or `Bool`; the repeat count is a
constructor-specific managed `Int` operand, not a general managed region value.

Callbacks must be known closure constructions or function references. Captures
are restricted to immediate scalar values and static code addresses. External
list parameters, escaping lists, unknown callback values, unsupported
operations and managed elements/captures retain the existing
persistent skew-list implementation. This is a supported representation
choice, not a conversion shim. There are no array/skew conversions.

The initial selection rule is an eligibility rule, not an interprocedural cost
model. Operations on literal arrays of up to 28 elements are unrolled; larger
and runtime-sized arrays use shared tail-recursive kernels in
`stdlib/__ListArray.dark`, compiled into loops.
Literal initialization still emits work proportional to the source literal.
Literal lengths must fit the existing signed 32-bit layout offsets. Runtime
lengths use checked 64-bit byte arithmetic. Runtime lengths through 28 use the
256-byte recyclable class; larger lengths use independent mappings. Growing
builders and pooled large buffers are not implemented.

## Typed stages and ownership

The region IR has three private program types:

```text
FunctionalRegion: typed scalar operands + semantic collection edges
  -> StorageRegion: explicit array layouts
  -> OwnedRegion: consume-or-copy transformations + explicit releases
  -> existing ANF primitives -> MIR -> existing native backends
```

The stages share an `Operation<'transform>` family. Only owned transforms carry
an ownership decision. Collection identities are monotonic and separate from
ANF temporary identifiers; lexical aliases resolve to the same collection
identity before use counting. Runtime extents name their originating
construction identity, not a lexical count variable; aliases, consuming
transforms and variable shadowing cannot change this origin. Lowering carries
the pointer, validated length atom and selected layout together. Scalar bindings
retain checked types.

A final use transfers the source allocation into the result. If another use
survives, the transform borrows the source and allocates/copies independent
storage. Every reader's last use releases its source, and unused results are
released. This closed grammar proves physical uniqueness statically: a buffer
has one physical ownership unit, while all logical aliases are visible in the
region graph. A borrowed parameter with RC=1 is **not** evidence of uniqueness;
borrowed external lists are ineligible.

`verifyOwnership` checks live inputs, globally unique identities, balanced
releases, and absence of leaked region roots. The stage verifier also checks
operand types, layout agreement, and allocation bounds. Construction is atomic
at the region level: element expressions run first in source order, then the
compiler allocates and initializes the buffer before exposing its identity.
Map callbacks execute in list order; fold callbacks execute in traversal order.
Repeat evaluates count and value once, in source order, even for nonpositive
counts. Negative arbitrary-precision counts normalize to zero before narrowing.
The checked constructor rejects counts above `(Int64.MaxValue - 40) / 8`
before conversion or multiplication. That rejection uses the stdlib fatal-error
mechanism (`Uncaught exception: Out of heap memory`); an OS allocation failure
uses the native allocator's fatal path.

`allocationSummary` reports exact region allocation counts/requested bytes, copies,
reused transformations, and releases, excluding work inside scalar expressions
and callbacks. Requested bytes include the mapped allocator's private prefix,
but exclude OS page rounding. Byte budgets contain a constant term and physical
runtime-buffer counts keyed by construction identities. Each runtime buffer
costs `256` for `n <= 28`, otherwise `40 + 8*n`; copies add another buffer with
the same extent. Here `n` is the validated, nonnegative array length, not the
original signed count. Distinct runtime constructors retain distinct terms.
These are piecewise budgets, not an affine approximation. Pass tests check them and
the resulting native-memory ANF operations. `--dump-anf` exposes allocations,
stores, calls, and cleanup.

## Storage contract

The internal layout is `[length][capacity][initialized count][Int64 elements][RC]`,
with 8-byte words. Capacity equals the length except for small runtime buffers,
whose capacity is 28. Allocation size is `32 + 8 * capacity`, including the
refcount word. Empty literals use 32 bytes; empty runtime buffers use the same
256-byte recyclable class as other small runtime buffers. There is no special
null-array representation.

Storage selection produces `RecycledArray`, `MappedArray`, or `RuntimeArray`. Statically
sized allocations through 256 bytes use the existing allocator's recyclable
size classes on both native backends. Their cleanup uses the fixed-block release
plan with no child destructors. Runtime allocation and release dispatch on
the validated length. The small branch has RC at offset 248 and uses the same
fixed-block release plan, exposed through an internal array-release intrinsic;
it never disguises the buffer as a source-level managed value. A shared release
helper keeps conditional cleanup out of initial region continuations.
Larger arrays own an independent mapping and explicitly unmap it at their verified final release.
The common array header and RC word remain
uniform; mapped-region lifetime is controlled by the ownership plan, not RC.
The compiler never tags array storage as a source-level list, reinterprets it
as a Blob/String, or uses the 8-byte-only `RawFree` primitive to reclaim it.

`MappedAlloc` and `MappedFree` remain distinct effects through ANF, MIR, and
LIR. The allocator uses `mmap`/`munmap`, with an 8-byte private mapping-length
prefix before the returned word-aligned pointer. Size checks reject negatives
and prefix-addition overflow before entering the kernel. Zero requested bytes
still produce a releasable allocation. Syscall failure uses the existing fatal
allocation-error path. Syscall operands and live caller registers are protected
by ordinary LIR caller-save boundaries; an allocation result is moved out of
the result register only after restoration. Leak accounting counts mappings
only after successful allocation and decrements only after successful release.

These mappings favor simple, auditable reclamation over a pooled large-buffer
allocator: every new large physical buffer incurs a mapping syscall, and every
release incurs an unmapping syscall. Consumed transformations reuse the mapping
without either syscall. Copying preserves the source and allocates one new
mapping. Native wall-time evidence is therefore required alongside instruction
counts when evaluating these workloads. Growing, pooled, and escaped buffers
remain separate future work.

The internal allocator test seeds an exact-size block, runs the list pipeline,
reacquires a block, and observes transformed contents. It also checks leak
accounting. Inlining is disabled for that probe to preserve the seed function's
release boundary. Ordinary semantic tests run with default optimizations.

Repeated native execution also validates the instrumentation itself. Both x86
allocation paths (raw and fixed-block) count a recycled block as live again.
ELF leak counters are placed on a separate 64 KiB boundary, shared by relocation
and image construction, so their writes cannot repeatedly invalidate hot code
pages under QEMU. This padding is confined to leak-check builds; ordinary
binaries retain their previous layout.

## Further architecture work

This is the first end-to-end region slice, not a replacement for the entire
ANF pipeline or a complete Perceus implementation. The next boundaries are:

1. Further runtime-sized constructors (including Result-wrapped `List.repeat`),
   builders, a growth policy, and profitable pooling for large runtime buffers.
   Checked repeat construction, independent variable-byte mappings and
   loop-based kernels provide the reclamation/execution foundation.
2. A general semantic HIR and primitive effect/alias/ownership contracts;
   layout/destruction metadata independent of ANF; stage verifiers throughout
   the pipeline. Generated printing must precede general ownership elaboration.
3. Function ownership and representation interfaces, bounded specialization,
   explicit conversion profitability, recursive solving, and cache identities.
4. Runtime uniqueness tests for consumed arrays whose sharing is not statically
   known; surviving borrowed aliases must remain protected.
5. Managed elements and destruction-effect propagation. Stream finalizers are
   observable, including through containers, so general last-use release cannot
   move them arbitrarily. This slice excludes managed elements and captures.
6. Drop specialization, constructor reset/reuse tokens, safe child-edge
   dismantling, and later suitable tail-recursion-modulo-constructor lowering.

Roc's ownership solver/emitter/certifier separation and array uniqueness
mechanics inform these later stages. Koka's Perceus implementation supplies the
model for precise RC and constructor reuse. Representation selection between
Dark's skew lists and arrays remains a separate compiler analysis. Opportunistic
reuse does not constitute a general fully-in-place guarantee.

Validation commands and integration requirements remain owned by
[verification.md](../../contributing/verification.md). Collection-specific diagnostics are
documented in [the targeted benchmark guide](../../../benchmarks/targeted/README.md).
