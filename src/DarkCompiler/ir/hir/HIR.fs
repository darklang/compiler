// HIR.fs - Normalized values and typed structured control flow shared by semantic dialects.
// Source expressions remain opaque evaluation payloads. Their lexical inputs
// are explicit value identities, but they carry no purity or aliasing claim.

module HIR

type ValueId = ValueId of int

type Value = {
    Id: ValueId
    Type: AST.Type
}

type Operand = {
    Expression: AST.Expr
    Type: AST.Type
    Inputs: Map<string, Value>
}

type Operation<'leaf, 'block> =
    | Leaf of 'leaf
    | ScalarBinding of result: Value * value: Operand
    | Branch of result: Value * condition: Operand * ifTrue: 'block * ifFalse: 'block

/// Each branch produces the binding consumed by the enclosing continuation.
/// Keeping the continuation in this sequence avoids duplicating it per path.
type Block<'operation> = {
    Parameters: Map<string, Value>
    Operations: 'operation list
    Result: Value
}

/// A leaf interface exposes normalized value edges and opaque scalar operands
/// without assigning ownership or effects to either category.
type ValueContract = {
    Inputs: Value list
    Operands: Operand list
    Outputs: Value list
}
