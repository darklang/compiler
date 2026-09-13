# Darklang Compatibility

This compiler aims to match the Darklang interpreter while compiling programs
ahead of time to native code. Compiler source and E2E tests are authoritative
for implemented behavior. The revision-pinned ledgers in this directory record
compatibility boundaries and intentional extensions.

Run the compatibility validator with:

```bash
python3 scripts/validate-darklang.py --help-full
python3 scripts/validate-darklang.py
```

## Source syntax

The validator translates supported compiler spellings where the interpreter
uses different syntax.

| Area | Compiler spelling | Interpreter spelling |
|---|---|---|
| Explicit arbitrary integer | `5I` | `5` |
| Sized integers | `1y`, `1s`, `1l`, and unsigned forms | Not supported |
| Lists | `[1, 2]` | `[1L; 2L]` |
| Calls | `Mod.fn(a, b)` | `Stdlib.Mod.fn a b` |
| Generic types | `List<Int64>` | Interpreter type syntax |
| Interpolation | `$"Hello {name}"` | Not supported |

Bindings and lambdas otherwise use the shared public grammar. The detailed
language comparisons cover [bindings](language/bindings.md),
[identifiers](language/identifiers.md),
[name resolution](language/name-resolution.md),
[primitive literals](language/primitive-literals.md),
[tuples](language/tuples.md), [records](language/records.md),
[program structure](language/program-structure.md),
[conditionals and sequences](language/conditionals-and-sequences.md), and
[recursion](language/recursion.md).

## Validator skip reasons

Some native-runner observations cannot be compared through the interpreter
expression evaluator:

| Prefix | Meaning |
|---|---|
| `eval:*` | Compile errors, expected runtime errors, stdout, stderr, exit codes, or builtin test infrastructure |
| `syntax:*` | A compiler spelling that the validator cannot translate |
| `semantic:*` | A compiler operation absent from or observably different from the interpreter |
| `stdlib:*` | A standard-library operation absent from the pinned interpreter |
| `extension:*` | An explicitly supported compiler extension |
| `internal:*` | Private compiler implementation surface |

High-precision float cases use `eval:float_precision` because the compiler's
shortest-roundtrip rendering intentionally differs from the pinned
interpreter. The [float and math ledger](stdlib/floats-and-math.md) owns that
contract.

## Compiler extensions

The compiler supports integer `/` with truncation toward zero; the interpreter
uses `/` for Float and named functions for integer division. The compiler also
has bitwise operators, Boolean `!`, sized-integer literals, interpolation, and
private SkewList/HAMT helpers that are not interpreter syntax or public parity
surface.

The validator also recognizes standard-library extensions such as selected
Random functions, byte access, list/string slicing helpers, and Float
conversion helpers. The exact executable classification is in
`scripts/validate-darklang.py`; public behavior belongs in the relevant
[standard-library ledger](stdlib/).

## Intentional AOT differences

The compiler rejects some invalid programs during static type checking where
the interpreter reaches an equivalent runtime error. Individual ledgers own
those timing differences. The principal areas are conditional arm and sequence
types, mixed or nominal equality operands, invalid stream ordering, and invalid
standard-library argument types.

CLI and host behavior is documented separately in the [CLI ledger](cli.md),
[presentation ledger](cli-presentation.md), and
[process/host/input ledger](cli-process-host-input.md).

Internal functions and data structures are implementation details. An
extension must remain identified in the relevant ledger and validator rule; it
must not be presented as interpreter parity.
