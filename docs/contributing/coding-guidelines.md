# F# Coding Guidelines

The compiler is written in a functional subset of F#. Repository-specific
agent requirements are in [AGENTS.md](../../AGENTS.md).

## Model control flow in types

- Use `Option` only for semantic absence.
- Use `Result` for recoverable failures that need context.
- Use discriminated unions instead of Boolean flags or magic strings for
  distinct states.
- Complete migrations and remove superseded representations rather than
  maintaining hidden fallbacks.
- Use `Crash.crash` only for impossible, undocumented states.

For a sequence of fallible operations with one error type, use `Result.bind`
and `Result.mapError`:

```fsharp
lex input
|> Result.bind parse
|> Result.mapError (fun error -> $"Unable to parse input: {error}")
```

Use explicit matching when steps have different error types or successful
values need different treatment:

```fsharp
match loadSource filename with
| Error error -> Error $"Unable to load {filename}: {error}"
| Ok source ->
    match parse source with
    | Error error -> Error $"Unable to parse {filename}: {error}"
    | Ok ast -> Ok ast
```

Add context without discarding the original failure:

```fsharp
loadDarkFile filename
|> Result.mapError (fun error -> $"Unable to load {filename}: {error}")
```

Fold collections through `Result` when any element can fail:

```fsharp
bindings
|> List.fold
    (fun state binding ->
        state
        |> Result.bind (fun env -> addBinding env binding))
    (Ok Map.empty)
```

Independent results can be matched together when the error priority is
explicit. Do not encode failure as `None`, ignore a returned error, or invent a
default after a failed lookup. A throwing platform API must be contained at its
adapter boundary and exposed to compiler code as `Result`.

## Keep transformations functional

- Prefer `List.map`, `List.fold`, `Map`, and `Set` to mutable collections.
- Keep related state in one record or union so it cannot drift out of sync.
- Preserve compiler warnings as errors and make pattern matches exhaustive.
- Keep pass-local invariants explicit at the function boundary.

## Avoid hidden compatibility behavior

Do not add shims, defaults, recovery branches, or special cases merely to move
past an unsupported state. Either model the state precisely, expose a clear
failure, or document and test the behavior as a supported compatibility
contract.

Finish migrations. Do not leave old and new representations or conversion
paths in parallel after callers have moved.

## Write meaningful tests and tools

- Prefer the smallest E2E program that exposes user-visible compiler behavior.
- Do not add permanent tests solely to prove that a removed feature remains
  absent unless an active migration creates a concrete regression risk.
- Do not contort tests merely to increase a coverage count.
- Do not parse human-readable logs or compiler dumps when structured output is
  available or can reasonably be added.
- Test command-line plumbing or the harness only when the harness behavior is
  itself the subject of the change.

## Keep documentation close to evidence

Comments should explain invariants, ownership, or a non-obvious design choice
to a senior compiler engineer. Do not copy test inventories, commit history, or
temporary investigation notes into source comments. Every source file retains
its required file-purpose comment.
