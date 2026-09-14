// ListRegion.fs - Typed closed-list region stages, identities, and array layouts.

module ListRegion

type ListId = ListId of int

type Scalar = SemanticIR.Operand

type Transform =
    | Map of callback: Scalar
    | Reverse

type Construction =
    | Literal of elements: Scalar list
    | Repeat of count: Scalar * value: Scalar

type Operation<'transform, 'block> =
    | Construct of result: ListId * construction: Construction
    | Transform of result: ListId * source: ListId * operation: 'transform
    | Fold of name: string * source: ListId * initial: Scalar * callback: Scalar
    | ScalarBinding of name: string * value: Scalar
    | Branch of name: string * condition: Scalar * ifTrue: 'block * ifFalse: 'block

type FunctionalBlock = internal FunctionalBlock of SemanticIR.Block<Operation<Transform, FunctionalBlock>>
type FunctionalRegion = internal FunctionalRegion of FunctionalBlock

/// Runtime extent identity survives aliases and consuming transformations.
/// It names a construction, never a source variable that could be rebound.
type ArrayExtent = ConstantLength of int | RuntimeLength of ListId

type ArrayLayout = internal RecycledArray of length:int | MappedArray of length:int | RuntimeArray of origin:ListId

let extent = function
    | RecycledArray length | MappedArray length -> ConstantLength length
    | RuntimeArray origin -> RuntimeLength origin
let elementOffset index = 24 + index * 8
let payloadSize length = elementOffset length
let allocationSize length = payloadSize length + 8
let internal recycledCapacityLimit = 28
// Literal offsets remain representable in signed 32-bit layout metadata.
// Runtime extents instead use checked 64-bit arithmetic in the constructor.
let internal maxCapacity = (System.Int32.MaxValue - 40) / 8

/// Exact piecewise requested-byte budget, excluding OS page rounding.
/// Each runtime buffer costs 256 bytes for n <= 28, otherwise 40 + 8*n.
/// Terms count physical buffers with the same validated construction extent.
type AllocationBytes = {
    ConstantBytes: int64
    RuntimeBuffers: Map<ListId, int64>
}

let internal constantBytes value = { ConstantBytes = value; RuntimeBuffers = Map.empty }
let internal requestedBytes = function
    | RecycledArray length -> constantBytes (int64 (allocationSize length))
    | MappedArray length -> constantBytes (int64 (allocationSize length) + 8L)
    | RuntimeArray origin -> { ConstantBytes = 0L; RuntimeBuffers = Map.ofList [origin, 1L] }

let internal addBytes left right =
    let coefficients =
        Map.fold (fun terms origin coefficient ->
            Map.change origin (fun current -> Some (coefficient + Option.defaultValue 0L current)) terms)
            left.RuntimeBuffers right.RuntimeBuffers
    { ConstantBytes = left.ConstantBytes + right.ConstantBytes; RuntimeBuffers = coefficients }

type StorageRegion = internal StorageRegion of FunctionalRegion * Map<ListId, ArrayLayout>

/// Closed regions prove uniqueness statically. Consume transfers the final
/// reference; BorrowAndCopy preserves a source with surviving logical aliases.
/// Runtime uniqueness checks belong to the later escaping-array storage class.
type Ownership = Consume | BorrowAndCopy

type OwnedOperation = {
    Operation: Operation<Transform * Ownership, OwnedBlock>
    Releases: ListId list
}
and OwnedBlock = {
    EntryReleases: ListId list
    Body: SemanticIR.Block<OwnedOperation>
}

type OwnedRegion = internal OwnedRegion of OwnedBlock * Map<ListId, ArrayLayout>

type AllocationSummary = {
    Allocations: int
    AllocatedBytes: AllocationBytes
    Copies: int
    ReusedTransforms: int
    Releases: int
}

/// Preserve alternatives and their continuation without enumerating paths or
/// adding mutually exclusive costs. Callback/scalar allocations are excluded.
type AllocationBudget =
    | Complete of AllocationSummary
    | Conditional of prefix: AllocationSummary * ifTrue: AllocationBudget * ifFalse: AllocationBudget * continuation: AllocationBudget

let internal lookup name key map =
    match Map.tryFind key map with
    | Some value -> value
    | None -> Crash.crash $"List HIR: missing {name} for {key}"

let internal source = function
    | Transform (_, input, _) | Fold (_, input, _, _) -> Some input
    | Construct _ | ScalarBinding _ | Branch _ -> None

let internal result = function
    | Construct (output, _) | Transform (output, _, _) -> Some output
    | Fold _ | ScalarBinding _ | Branch _ -> None

let internal immediate = function
    | AST.TInt64 | AST.TBool -> true
    | _ -> false

/// Recognition uses canonical, monomorphized identities, never suffix guesses.
