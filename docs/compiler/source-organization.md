# Compiler source organization

Source files are named for responsibilities, not execution positions. F# project
compilation order expresses dependencies; typed driver functions express pass
execution order. Diagnostic stage names remain stable when passes are inserted.

## Boundaries

- `frontend/`: parsing, resolution, checking, and generated source helpers.
- `ir/`: representation definitions and their local operations.
- `memory/`: representation layouts, destruction shapes, and release plans,
  independent of ANF.
- `analysis/`: semantic and cross-function analysis.
- `passes/`: transformations grouped by the representation they operate on.
- `backend/`: target instruction selection, runtime instruction generators,
  resolution, encoding, and binary output.
- `driver/`: typed pipeline orchestration, compilation sessions and caches,
  stdlib preparation, and process execution.

Keep cohesive recursive definitions together. Extract types and leaf algorithms
before splitting recursive dispatchers; use narrow typed callbacks for recursive
expression handlers. Avoid generic helper collections and cross-file recursive
module dependencies. File size is a review signal, not a partitioning rule.

ANF expression lowering has typed recursion callbacks in
`passes/anf/lowering/LoweringCallbacks.fs`. `Expressions.fs` ties the recursive
entry points together; atom, ordinary-expression, and pattern handlers cannot
depend on that driver. Pattern matching retains its cohesive recursive pattern
compiler and source-order failure rendering in one larger module.

## Migration sequence

1. Remove numeric filenames, group passes, and name driver diagnostics without
   changing execution order.
2. Extract memory contracts from ANF and separate list-region and RC concerns.
3. Separate specialization, closure preparation, and expression lowering.
4. Separate backend allocation, destruction, instruction selection, and emission.
5. Separate driver, checking, register allocation, optimization, and test concerns.
6. Extend semantic HIR and ownership interfaces, with explicit verification and
   separate behavior-changing commits.

The intended general pipeline is typed AST → semantic HIR → storage IR → owned
IR → ANF → MIR → LIR → target instructions. The first three stages currently
cover only closed list regions; their generalization is implementation work,
not accomplished by moving files. Do not create empty future stages or duplicate
ownership authorities. Generated printing must precede general ownership
elaboration. Changes to aliasing and lifetime after elaboration require proof
preservation and verification.

Each refactoring chunk preserves algorithms, evaluation order, and emitted-code
behavior. Semantic migrations require focused failing E2E coverage before their
implementation. Verification requirements remain in
[verification.md](../contributing/verification.md).
