# Compiler-selected list arrays

The compiler has an initial closed-region implementation of immutable
`List<Int64>` computations using mutable array storage. No source syntax,
ownership annotation, public list type, or external calling convention changes.

## Implemented boundary

`ListHIR.fs` recognizes straight-line regions beginning with a list literal or
a supported list operation after monomorphization and lambda lifting, before
AST-to-ANF lowering destroys collection semantics. Supported operations are
`List.map<Int64, Int64>`, `List.reverse<Int64>`, and
`List.fold<Int64, Int64>`. Literals contain at most 28 elements. Scalar bindings
and region results have type `Int64` or `Bool`.

Callbacks must be known closure constructions or function references. Captures
are restricted to immediate scalar values and static code addresses. External
list parameters, escaping lists, unknown callback values, unsupported
operations, managed elements/captures, and larger literals retain the existing
persistent skew-list implementation. This is a supported representation
choice, not a conversion shim. There are no array/skew conversions.

The initial selection rule is an eligibility rule, not an interprocedural cost
model. Operations are unrolled within the fixed 28-element bound. This favors
small local computations; it does not yet optimize large collections or growing
builders.

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
identity before use counting. Scalar bindings retain checked types.

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

`allocationSummary` reports exact region allocation counts/bytes, copies,
reused transformations, and releases, excluding work inside scalar expressions
and callbacks. Pass tests check these budgets and the resulting native-memory
ANF operations. `--dump-anf` exposes allocations, stores, calls, and cleanup.

## Storage contract

The internal layout is `[length][capacity][initialized count][Int64 elements][RC]`,
with 8-byte words. Capacity equals the statically known length. Allocation size
is `32 + 8 * length`, including the refcount word. Empty regions use a 32-byte
allocation; there is no special null-array representation.

The largest supported allocation is 256 bytes, whose 248-byte payload is in
the existing allocator's recyclable size classes on both native backends.
Cleanup uses the existing fixed-block release plan with no child destructors.
The compiler never tags array storage as a source-level list, reinterprets it
as a Blob/String, or uses the 8-byte-only `RawFree` primitive to reclaim it.

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

1. A reclaiming allocator for variable/larger buffers, growth and overflow
   policy, and loop-based array kernels rather than fixed-size unrolling.
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
