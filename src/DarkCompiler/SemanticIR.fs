// SemanticIR.fs - Typed block results and representation-independent value liveness.
// Operands retain checked source semantics until their consumer lowers them;
// this layer makes no purity claim about an opaque expression or callback.

module SemanticIR

type Operand = { Expression: AST.Expr; Type: AST.Type }

type Block<'operation> = {
    Operations: 'operation list
    Result: Operand
}

/// Explicit value edges describe reachability, not physical ownership. A later
/// storage-specific pass decides whether a last use can transfer an allocation.
type ValueContract<'id when 'id: comparison> = {
    Uses: Set<'id>
    Defines: Set<'id>
}

let liveBefore contract liveAfter =
    Set.union contract.Uses (Set.difference liveAfter contract.Defines)
