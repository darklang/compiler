// OwnedIR.fs - Structured region ownership and explicit unit-transfer contracts.

module OwnedIR

type Input<'id> = Borrowed of 'id | Consumed of 'id

/// Lists retain multiplicity: consuming one unit twice or defining a duplicate
/// identity is invalid. This is unit ownership, not general RC credit arithmetic.
type Contract<'id> = {
    Inputs: Input<'id> list
    Outputs: 'id list
}

type Step<'leaf, 'id> = {
    Operation: HIR.Operation<'leaf, Block<'leaf, 'id>>
    Releases: 'id list
}
and Block<'leaf, 'id> = {
    EntryReleases: 'id list
    Body: HIR.Block<Step<'leaf, 'id>>
}

/// A dialect must describe every access, including opaque scalar operands and
/// block results. No default marks unknown expressions as storage-independent.
type Semantics<'leaf, 'id when 'id: comparison> = {
    Leaf: 'leaf -> Contract<'id>
    ScalarUses: HIR.Operand -> Set<'id>
    ValueUses: HIR.Value -> Set<'id>
}

type VerificationError<'id when 'id: comparison> =
    | InvalidUse of 'id
    | InvalidRelease of 'id
    | DuplicateDefinition of 'id
    | InconsistentJoin
    | UnreleasedValues of Set<'id>
