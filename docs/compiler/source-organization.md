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

Backend `Instructions.fs` files retain exhaustive LIR dispatch. Their
`instructions/` children receive typed operands, not arbitrary instructions to
redispatch. Recursive ARM64 expansion receives an explicit lowering callback.
Target runtime generators live under each backend's `runtime/`; shared memory
plans contain no ISA instructions. The former root `Runtime.fs` was ARM64 code,
not a target-independent runtime layer.

## Finding an owner

| Change | Start here |
|---|---|
| Source type rules or diagnostics | `frontend/checking/` |
| Generic identity or closure preparation | `passes/preparation/` |
| Collection recognition and array selection | `passes/hir/`, `passes/storage/` |
| Region liveness, reuse, or verification | `passes/ownership/` |
| Existing ANF lifetime insertion | `passes/anf/ownership/` |
| Shared destruction shapes and release plans | `memory/` |
| Expression, atom, or pattern lowering | `passes/anf/lowering/` |
| ANF or MIR optimization | `passes/anf/optimization/`, `passes/mir/optimization/` |
| Liveness, spilling, coloring, phi edges | `passes/lir/allocation/` |
| Native operation expansion | `backend/<target>/instructions/` |
| Native allocation and destruction helpers | `backend/<target>/runtime/` |
| Pipeline scheduling or cache identity | `driver/` |
| IR definitions and scoped printers | `ir/<representation>/` |

Ownership and ARM64 test registries retain their existing suite entry points;
their cases are grouped under `src/Tests/compiler-passes/ownership/` and
`src/Tests/compiler-passes/arm64/`. Test selection and observable assertions are
unchanged by that grouping.

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
