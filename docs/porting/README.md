# Understanding the compiler port

The [current status](generated/ledger.md) counts declarations and tests directly
from the complete imported interpreter corpus in [`upstream/`](../../upstream/).
Every `.dark` file under interpreter `packages/` and
`backend/testfiles/execution/` is included at the revision in
[`sources.yaml`](../../porting/sources.yaml).

`upstream/` is byte-identical to darklang/dark at that revision, so a re-pin is
`python3 scripts/sync-upstream.py /path/to/dark REVISION`. Compiler decisions
live in [`upstream-annotations.tsv`](../../porting/upstream-annotations.tsv), one
line per item: the path below `upstream/`, the item's qualified name and the
decision, separated by tabs. Assertions and top-level expressions are named by
line (`Module.assertion@12`). There are no file-level exclusions:

- No annotation selects the original declaration or test.
- `error-message compile: "diagnostic" reason=diagnostic-difference`
  selects a runnable negative test with an accepted compiler diagnostic and
  required phase (`compile` or `runtime`), preserving the upstream expectation.
- `blocked: tag` retains an item awaiting compiler support.
- `excluded: tag` records an intentional compiler exclusion.
- `replace: packages/path.dark#Qualified.name reason=tag` selects
  a separately maintained compiler implementation with a checked public contract.
- `xfail compile: "diagnostic" class=missing-support reason=tag` runs a test that must
  currently fail compilation with the specified diagnostic. `runtime` requires
  a runtime failure instead. Unexpected passes, wrong phases, unrelated output
  and abnormal process termination fail the check.

Every expected failure requires `class=aot-rejection`, `class=missing-support`
or `class=upstream-bug`, independently of its compile/runtime phase and reason.
AOT rejection contracts exercise intended static validation and must fail at
compilation. They are regression coverage, not unfinished support. Missing
support includes compiler/runtime bugs, unavailable declarations and test-runner
limitations; it can fail at either phase.

Upstream bugs record interpreter behavior or expectations we consider incorrect.
They keep strict phase and diagnostic matching and are counted separately from
compiler support gaps. Signed-zero patterns are one example: the compiler
distinguishes `0.0` and `-0.0` patterns even though numeric equality equates them.

The generated report separates these classes, their phases and reasons, and
lists remaining support by source. A named test with several assertions stays
one case: if an invalid assertion prevents compilation, the valid assertions in
that test have not thereby acquired passing execution coverage.

Reasons are short tags from the [glossary](../../porting/compiler-reasons.json).
The [import guide](upstream-import.md) describes source selection and validation.
An annotation naming an item its source does not have is an error, so a re-pin
reports every decision that needs moving. Named tests containing multiple
assertions remain one case. Test discovery and counting do not require successful compilation.

Existing adapted fixtures remain compiler regression tests. They are not used
to infer counterparts, partial test coverage or ambiguous source equivalence.
Execution receipts from `--results-json=...` belong to their individual runs;
source dispositions alone never claim successful execution. Receipts include
`expected_failure_class` (`aot-rejection`, `missing-support`, `upstream-bug`, or `none`) so a
passing rejection contract is distinguishable from an ordinary passing program.

## Update and check

Build the native adapters with `./build --ai`, then run:

```bash
python3 scripts/update-ledger.py
python3 scripts/update-ledger.py --check
```

Use `--build-dir=...` for a separate Dune directory. Every run checks that
`upstream/packages` has the Git tree id recorded as `upstream_packages_tree` in
`sources.yaml`. To also compare every imported source byte for byte with the pin:

```bash
python3 scripts/update-ledger.py --check --upstream /path/to/interpreter
```

The check reads committed Git blobs, ignoring dirty and untracked interpreter
files, and verifies the complete source scope. `scripts/check-porting` runs the
helper tests and obtains an isolated pinned checkout when none is supplied.
The only generated source ledger artifacts are aggregate counts and the status
page; names, compiler targets and per-item dispositions remain in the annotation file.

Language and parser capability assessments remain in
[`language-features.yaml`](../../porting/language-features.yaml) and
[`parser.yaml`](../../porting/parser.yaml). Partial, missing and excluded
capabilities require explanatory notes. Interpreter host builtin assessments
and their explicit binding locations remain separately maintained in
[`builtins.yaml`](../../porting/builtins.yaml); host-language F# tests are outside
this Dark source import.

The native `check_sources` tool validates the selected original bodies and all
replacement contracts together; it runs as part of ledger updates and Dune
regression checks. `probe_helpers` can check blocked test functions, types and
values against the current library to identify portable candidates. A candidate
must also pass the complete selection check before its annotation is removed:
constructor identities and other constraints can conflict across files.

When `--trial-upstream` and `--results-json=PATH` are supplied together, the
runner runs only pending imported tests, and flushes each completed outcome to `PATH.jsonl` and logs the current
test before compiling it. These are execution receipts for investigation, not a
maintained ledger. They preserve completed results if an unsupported test hangs
or terminates the compiler process.
