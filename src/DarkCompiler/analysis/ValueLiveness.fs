// ValueLiveness.fs - Backward value-edge transfer independent of physical storage.

module ValueLiveness

/// Reachability is not permission to reuse storage: aliases, destruction, and
/// the storage-specific ownership contract must be resolved independently.
type Contract<'id when 'id: comparison> = {
    Uses: Set<'id>
    Defines: Set<'id>
}

let liveBefore contract liveAfter =
    Set.union contract.Uses (Set.difference liveAfter contract.Defines)
