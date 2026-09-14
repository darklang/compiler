// HIR.fs - Typed structured control flow shared by semantic operation dialects.
// Opaque operands preserve source evaluation order; they are not purity proofs
// or normalized value identities. A dialect must account for their value uses.

module HIR

type Operand = { Expression: AST.Expr; Type: AST.Type }

type Operation<'leaf, 'block> =
    | Leaf of 'leaf
    | ScalarBinding of name: string * value: Operand
    | Branch of name: string * condition: Operand * ifTrue: 'block * ifFalse: 'block

/// Each branch produces the binding consumed by the enclosing continuation.
/// Keeping the continuation in this sequence avoids duplicating it per path.
type Block<'operation> = {
    Operations: 'operation list
    Result: Operand
}
