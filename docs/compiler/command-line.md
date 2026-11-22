# darkc's command line, as `dark compile` calls it

`dark compile` (in darklang/dark) drives darkc. This page is the contract between
them: the flags Dark passes, what darkc prints, and the exit codes. Dark works
out what a program uses and whether it may compile it; darkc only translates.
darkc reads no environment variables to decide where things go and has no
default package server.

## Building an executable from a package fn

```bash
darkc --entry HASH --package-server URL --cache-dir DIR -q -o OUTPUT [--json]
```

- `--entry HASH` is the hash of the package fn the executable calls. darkc reads
  it from `URL/function/get/with-location/HASH`.
- The fn takes `List<String>` (the command-line words after the program name)
  or nothing (`()`), and returns `Unit` (the executable exits 0) or `Int64` (the
  executable exits with that code). These are `dark run`'s rules, including
  its range: a result outside 0-255 prints
  `Script returned N, but an exit code must be 0-255.` on stderr and exits 1. Any other
  signature is refused with a `not-an-entry` diagnostic before compiling.
- darkc writes the argv and exit wiring itself; Dark sends no source text.
- `-r` instead of `-o OUTPUT` compiles to a temporary file and runs it.

## Checking whether package items compile

```bash
darkc --check --package-server URL --cache-dir DIR [--json] NAME...
```

One line per name on stdout, in the order given. Exit 1 if any name doesn't
compile.

Text:

```text
Darklang.Me.Tools.count: compiles
Stdlib.List.map: compiles at <Int64, Int64>
Darklang.Me.Tools.report: can't compile: uses function or value Builtin.pmLocations, which the compiler does not define
```

`--json`, one object per line:

```json
{"name":"Darklang.Me.Tools.report","status":"failed","typeArguments":null,"reason":"missing-builtin","error":"Compilation failed: ...","diagnostic":{...}}
```

`status` is `compiles`, `failed`, or `undetermined` (a generic fn that no
probed type arguments settled). `typeArguments` names the type arguments a
generic fn compiled at, or is null. `reason` is the diagnostic's
`reason.kind` and `error` its `message`, both empty when it compiles.
`diagnostic` is null when it compiles.

## Diagnostics

Errors go to stderr. With `--json`, each error is one JSON object on one line:

```json
{
  "reason": {"kind": "missing-builtin", "name": "Builtin.pmLocations"},
  "location": {"item": "Darklang.Me.Tools.total", "hash": "total-hash", "line": null, "column": null},
  "path": ["Darklang.Me.Tools.report", "Darklang.Me.Tools.total", "Builtin.pmLocations"],
  "message": "Compilation failed: Darklang.Me.Tools.total: Unknown function or value 'Builtin.pmLocations'"
}
```

- `reason.kind` is one of:
  - `missing-builtin` (`name`): a builtin compiled code doesn't have;
  - `missing-function` (`name`): a fn or value the compiler can't find;
  - `missing-type` (`name`);
  - `unsupported-feature` (`feature`): a language feature with no lowering;
  - `needs-type-arguments` (`name` or null);
  - `not-an-entry` (`parameters`, `returnType`): the `--entry` fn's signature;
  - `entry-not-found` (`hash`);
  - `usage`: a bad command line;
  - `compiler-error`: anything else. `message` has the text.
- `location` is the item the failure is in, with its hash when it's a package
  item, and the line and column when the error names them. It's null when the
  failure isn't inside a known item.
- `path` runs from the root (the `--entry` fn or the `--check` name) through
  package references to the failing item, then to what it uses. It's empty
  when there's no package path.
- `message` is darkc's own text, for debugging. Dark renders the rest.

Without `--json`, darkc prints `message` (or `Error: …` for a bad command line).

## Caching

`--cache-dir DIR` is where darkc keeps the prepared stdlib
(`stdlib-<target>-<darkc digest>.bin`, about 150 MB) and the package answers
(`packages.sqlite3`). A relative `DIR` is taken from the current directory.
One process at a time prepares into a directory (`stdlib.lock`); the others
wait for its result. A preparation that was killed leaves a
`stdlib-*.partial`, which the next one removes. Without `--cache-dir`, darkc
keeps nothing between runs and prepares the stdlib
in memory each time (about 80 s).

## Preparing the stdlib ahead of time

```bash
darkc prepare --cache-dir DIR [--target linux-x86_64]
```

Prepares the stdlib into `DIR` and exits: 0 on success, 1 with the error on
stderr. It prints nothing else. A compile that finds the prepared stdlib in
the same `--cache-dir` starts in seconds.

With `--verbose=json`, any command reports the stages of a preparation as JSON
lines on stderr; [progress.md](progress.md) has the format.

## Exit codes

- 0: success, or for `-r`, the program's own exit code.
- 1: a compile or check failure, or a bad command line.

## Other modes

These are for development and the test suite, not for Dark:

- `darkc FILE`, `darkc -e EXPRESSION` (`-` reads stdin), with `-r` to run.
- `--batch`, `--allow-internal`, `--emit-result`, `--target`, the `--dump-*` and
  `--disable-opt-*` flags.

`darkc --help` lists everything.
