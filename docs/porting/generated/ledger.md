# Current compiler port status

[How to read and update this ledger](../README.md).

Interpreter revision: `7769d0865ef17c2c9edefa04a227fa23200c3783`.

The complete corpus contains 489 source files. Items are counted directly from parsed source annotations.

A run disposition means selected, not a passing execution receipt. Replacement contracts are checked independently.

| Area | Source dispositions |
| --- | --- |
| packages | 2,278 blocked, 5 excluded, 35 replace, 1,823 run |
| stdlib | 806 blocked, 917 replace, 649 run |
| tests | 57 blocked, 1,630 run, 1,212 xfail |

## Expected test failures

Verified AOT rejection contracts cover programs rejected under the compiler’s static rules; they are not unfinished support.
Missing support includes compiler bugs, unavailable dependencies and test-runner gaps. Counts describe source contracts; execution receipts remain separate.

Upstream bugs record interpreter behavior or test expectations that the compiler deliberately rejects as incorrect; they are separate from missing support.

| Class | Compile | Runtime | Total |
| --- | ---: | ---: | ---: |
| `aot-rejection` | 222 | 0 | 222 |
| `missing-support` | 946 | 43 | 989 |
| `upstream-bug` | 0 | 1 | 1 |

| Class | Reason | Tests |
| --- | --- | ---: |
| `aot-rejection` | `aot-arity-error` | 12 |
| `aot-rejection` | `aot-invalid-field` | 16 |
| `aot-rejection` | `aot-invalid-key` | 6 |
| `aot-rejection` | `aot-match-error` | 26 |
| `aot-rejection` | `aot-name-error` | 11 |
| `aot-rejection` | `aot-type-error` | 151 |
| `missing-support` | `compiler-gap` | 126 |
| `missing-support` | `test-counter` | 7 |
| `missing-support` | `upstream-dependency` | 856 |
| `upstream-bug` | `upstream-bug` | 1 |

### Remaining support by source

Each row links to the authoritative per-test annotations. A named test remains one case even when it contains several assertions.

| Source | Tests |
| --- | ---: |
| [packages/darklang/tests/cli/bashrc.dark](../../../upstream/packages/darklang/tests/cli/bashrc.dark) | 2 |
| [packages/darklang/tests/cli/builtinGroups.dark](../../../upstream/packages/darklang/tests/cli/builtinGroups.dark) | 4 |
| [packages/darklang/tests/cli/commandCompletions.dark](../../../upstream/packages/darklang/tests/cli/commandCompletions.dark) | 9 |
| [packages/darklang/tests/cli/commandGroups.dark](../../../upstream/packages/darklang/tests/cli/commandGroups.dark) | 3 |
| [packages/darklang/tests/cli/docsForAI.dark](../../../upstream/packages/darklang/tests/cli/docsForAI.dark) | 5 |
| [packages/darklang/tests/cli/evalAndRun.dark](../../../upstream/packages/darklang/tests/cli/evalAndRun.dark) | 17 |
| [packages/darklang/tests/cli/flags.dark](../../../upstream/packages/darklang/tests/cli/flags.dark) | 3 |
| [packages/darklang/tests/cli/golden.dark](../../../upstream/packages/darklang/tests/cli/golden.dark) | 2 |
| [packages/darklang/tests/cli/instance-tests.dark](../../../upstream/packages/darklang/tests/cli/instance-tests.dark) | 5 |
| [packages/darklang/tests/cli/interactiveTests.dark](../../../upstream/packages/darklang/tests/cli/interactiveTests.dark) | 4 |
| [packages/darklang/tests/cli/isolation.dark](../../../upstream/packages/darklang/tests/cli/isolation.dark) | 13 |
| [packages/darklang/tests/cli/jsonShapes.dark](../../../upstream/packages/darklang/tests/cli/jsonShapes.dark) | 9 |
| [packages/darklang/tests/cli/permissionsGrammar.dark](../../../upstream/packages/darklang/tests/cli/permissionsGrammar.dark) | 13 |
| [packages/darklang/tests/cli/propagate.dark](../../../upstream/packages/darklang/tests/cli/propagate.dark) | 1 |
| [packages/darklang/tests/cli/scripts.dark](../../../upstream/packages/darklang/tests/cli/scripts.dark) | 13 |
| [packages/darklang/tests/cli/searchExactSeveral.dark](../../../upstream/packages/darklang/tests/cli/searchExactSeveral.dark) | 1 |
| [packages/darklang/tests/cli/searchRanking.dark](../../../upstream/packages/darklang/tests/cli/searchRanking.dark) | 9 |
| [packages/darklang/tests/cli/searchShallow.dark](../../../upstream/packages/darklang/tests/cli/searchShallow.dark) | 2 |
| [packages/darklang/tests/cli/signaturesSeveral.dark](../../../upstream/packages/darklang/tests/cli/signaturesSeveral.dark) | 2 |
| [packages/darklang/tests/cli/signaturesTraitCalls.dark](../../../upstream/packages/darklang/tests/cli/signaturesTraitCalls.dark) | 2 |
| [packages/darklang/tests/cli/telemetry.dark](../../../upstream/packages/darklang/tests/cli/telemetry.dark) | 1 |
| [packages/darklang/tests/cli/tests-tui.dark](../../../upstream/packages/darklang/tests/cli/tests-tui.dark) | 24 |
| [packages/darklang/tests/cli/tests-workbench.dark](../../../upstream/packages/darklang/tests/cli/tests-workbench.dark) | 4 |
| [packages/darklang/tests/cli/tests.dark](../../../upstream/packages/darklang/tests/cli/tests.dark) | 10 |
| [packages/darklang/tests/cli/traceVerbs.dark](../../../upstream/packages/darklang/tests/cli/traceVerbs.dark) | 18 |
| [packages/darklang/tests/cli/traces.dark](../../../upstream/packages/darklang/tests/cli/traces.dark) | 29 |
| [packages/darklang/tests/cli/tracesRendering.dark](../../../upstream/packages/darklang/tests/cli/tracesRendering.dark) | 12 |
| [packages/darklang/tests/cli/viewSeveral.dark](../../../upstream/packages/darklang/tests/cli/viewSeveral.dark) | 3 |
| [packages/darklang/tests/cli/zshrc.dark](../../../upstream/packages/darklang/tests/cli/zshrc.dark) | 2 |
| [packages/darklang/tests/language/aliases.dark](../../../upstream/packages/darklang/tests/language/aliases.dark) | 2 |
| [packages/darklang/tests/language/apply.dark](../../../upstream/packages/darklang/tests/language/apply.dark) | 6 |
| [packages/darklang/tests/language/archivedSelf.dark](../../../upstream/packages/darklang/tests/language/archivedSelf.dark) | 1 |
| [packages/darklang/tests/language/basic.dark](../../../upstream/packages/darklang/tests/language/basic.dark) | 4 |
| [packages/darklang/tests/language/big.dark](../../../upstream/packages/darklang/tests/language/big.dark) | 1 |
| [packages/darklang/tests/language/collections.dark](../../../upstream/packages/darklang/tests/language/collections.dark) | 1 |
| [packages/darklang/tests/language/equality.dark](../../../upstream/packages/darklang/tests/language/equality.dark) | 2 |
| [packages/darklang/tests/language/errorTypeNames.dark](../../../upstream/packages/darklang/tests/language/errorTypeNames.dark) | 8 |
| [packages/darklang/tests/language/eself.dark](../../../upstream/packages/darklang/tests/language/eself.dark) | 2 |
| [packages/darklang/tests/language/fastOps.dark](../../../upstream/packages/darklang/tests/language/fastOps.dark) | 2 |
| [packages/darklang/tests/language/lambdas.dark](../../../upstream/packages/darklang/tests/language/lambdas.dark) | 3 |
| [packages/darklang/tests/language/letBindings.dark](../../../upstream/packages/darklang/tests/language/letBindings.dark) | 1 |
| [packages/darklang/tests/language/matchExpressions.dark](../../../upstream/packages/darklang/tests/language/matchExpressions.dark) | 5 |
| [packages/darklang/tests/language/operators.dark](../../../upstream/packages/darklang/tests/language/operators.dark) | 1 |
| [packages/darklang/tests/language/records.dark](../../../upstream/packages/darklang/tests/language/records.dark) | 7 |
| [packages/darklang/tests/language/runtimeToProgramTypes.dark](../../../upstream/packages/darklang/tests/language/runtimeToProgramTypes.dark) | 6 |
| [packages/darklang/tests/language/tailCalls.dark](../../../upstream/packages/darklang/tests/language/tailCalls.dark) | 2 |
| [packages/darklang/tests/language/traits.dark](../../../upstream/packages/darklang/tests/language/traits.dark) | 9 |
| [packages/darklang/tests/language/tupleLiterals.dark](../../../upstream/packages/darklang/tests/language/tupleLiterals.dark) | 2 |
| [packages/darklang/tests/language/unwrap.dark](../../../upstream/packages/darklang/tests/language/unwrap.dark) | 6 |
| [packages/darklang/tests/languageTools/atRestTypeChecker.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker.dark) | 186 |
| [packages/darklang/tests/languageTools/builtinIntrospection.dark](../../../upstream/packages/darklang/tests/languageTools/builtinIntrospection.dark) | 3 |
| [packages/darklang/tests/languageTools/live.dark](../../../upstream/packages/darklang/tests/languageTools/live.dark) | 6 |
| [packages/darklang/tests/languageTools/opWalks.dark](../../../upstream/packages/darklang/tests/languageTools/opWalks.dark) | 4 |
| [packages/darklang/tests/languageTools/parsedFileShape.dark](../../../upstream/packages/darklang/tests/languageTools/parsedFileShape.dark) | 4 |
| [packages/darklang/tests/languageTools/parser/diagnostics.dark](../../../upstream/packages/darklang/tests/languageTools/parser/diagnostics.dark) | 19 |
| [packages/darklang/tests/languageTools/parser/trees.dark](../../../upstream/packages/darklang/tests/languageTools/parser/trees.dark) | 22 |
| [packages/darklang/tests/languageTools/perf.dark](../../../upstream/packages/darklang/tests/languageTools/perf.dark) | 7 |
| [packages/darklang/tests/languageTools/pickLocation.dark](../../../upstream/packages/darklang/tests/languageTools/pickLocation.dark) | 8 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripExprs.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripExprs.dark) | 28 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripFunctionDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripFunctionDeclarations.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripModuleDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripModuleDeclarations.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripSourceFiles.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripSourceFiles.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripTestDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripTestDeclarations.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripTypeDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripTypeDeclarations.dark) | 2 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripTypeReferences.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripTypeReferences.dark) | 3 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripValueDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripValueDeclarations.dark) | 3 |
| [packages/darklang/tests/languageTools/semanticTokenization.dark](../../../upstream/packages/darklang/tests/languageTools/semanticTokenization.dark) | 9 |
| [packages/darklang/tests/languageTools/valueSearch.dark](../../../upstream/packages/darklang/tests/languageTools/valueSearch.dark) | 1 |
| [packages/darklang/tests/prettyPrinter/runtimeTypes.dark](../../../upstream/packages/darklang/tests/prettyPrinter/runtimeTypes.dark) | 14 |
| [packages/darklang/tests/scm/branchIdentity.dark](../../../upstream/packages/darklang/tests/scm/branchIdentity.dark) | 4 |
| [packages/darklang/tests/scm/branches.dark](../../../upstream/packages/darklang/tests/scm/branches.dark) | 10 |
| [packages/darklang/tests/scm/cli/branch.dark](../../../upstream/packages/darklang/tests/scm/cli/branch.dark) | 7 |
| [packages/darklang/tests/scm/cli/commit.dark](../../../upstream/packages/darklang/tests/scm/cli/commit.dark) | 6 |
| [packages/darklang/tests/scm/cli/conflicts.dark](../../../upstream/packages/darklang/tests/scm/cli/conflicts.dark) | 9 |
| [packages/darklang/tests/scm/cli/discard.dark](../../../upstream/packages/darklang/tests/scm/cli/discard.dark) | 5 |
| [packages/darklang/tests/scm/cli/history.dark](../../../upstream/packages/darklang/tests/scm/cli/history.dark) | 3 |
| [packages/darklang/tests/scm/cli/merge.dark](../../../upstream/packages/darklang/tests/scm/cli/merge.dark) | 7 |
| [packages/darklang/tests/scm/cli/propagation.dark](../../../upstream/packages/darklang/tests/scm/cli/propagation.dark) | 5 |
| [packages/darklang/tests/scm/cli/rebase.dark](../../../upstream/packages/darklang/tests/scm/cli/rebase.dark) | 6 |
| [packages/darklang/tests/scm/conflicts.dark](../../../upstream/packages/darklang/tests/scm/conflicts.dark) | 21 |
| [packages/darklang/tests/scm/draft.dark](../../../upstream/packages/darklang/tests/scm/draft.dark) | 6 |
| [packages/darklang/tests/scm/lww.dark](../../../upstream/packages/darklang/tests/scm/lww.dark) | 6 |
| [packages/darklang/tests/scm/propagation.dark](../../../upstream/packages/darklang/tests/scm/propagation.dark) | 16 |
| [packages/darklang/tests/scm/referenceNames.dark](../../../upstream/packages/darklang/tests/scm/referenceNames.dark) | 2 |
| [packages/darklang/tests/scm/removalConflicts.dark](../../../upstream/packages/darklang/tests/scm/removalConflicts.dark) | 5 |
| [packages/darklang/tests/stdlib/altJson.dark](../../../upstream/packages/darklang/tests/stdlib/altJson.dark) | 6 |
| [packages/darklang/tests/stdlib/base16.dark](../../../upstream/packages/darklang/tests/stdlib/base16.dark) | 5 |
| [packages/darklang/tests/stdlib/base64.dark](../../../upstream/packages/darklang/tests/stdlib/base64.dark) | 2 |
| [packages/darklang/tests/stdlib/bytes.dark](../../../upstream/packages/darklang/tests/stdlib/bytes.dark) | 32 |
| [packages/darklang/tests/stdlib/char.dark](../../../upstream/packages/darklang/tests/stdlib/char.dark) | 2 |
| [packages/darklang/tests/stdlib/cli/file.dark](../../../upstream/packages/darklang/tests/stdlib/cli/file.dark) | 10 |
| [packages/darklang/tests/stdlib/cli/posix.dark](../../../upstream/packages/darklang/tests/stdlib/cli/posix.dark) | 12 |
| [packages/darklang/tests/stdlib/cli/process.dark](../../../upstream/packages/darklang/tests/stdlib/cli/process.dark) | 9 |
| [packages/darklang/tests/stdlib/cli/sgr.dark](../../../upstream/packages/darklang/tests/stdlib/cli/sgr.dark) | 3 |
| [packages/darklang/tests/stdlib/cli/sys.dark](../../../upstream/packages/darklang/tests/stdlib/cli/sys.dark) | 2 |
| [packages/darklang/tests/stdlib/cli/widgets.dark](../../../upstream/packages/darklang/tests/stdlib/cli/widgets.dark) | 6 |
| [packages/darklang/tests/stdlib/crypto.dark](../../../upstream/packages/darklang/tests/stdlib/crypto.dark) | 10 |
| [packages/darklang/tests/stdlib/dateTime.dark](../../../upstream/packages/darklang/tests/stdlib/dateTime.dark) | 4 |
| [packages/darklang/tests/stdlib/decimal.dark](../../../upstream/packages/darklang/tests/stdlib/decimal.dark) | 3 |
| [packages/darklang/tests/stdlib/dict.dark](../../../upstream/packages/darklang/tests/stdlib/dict.dark) | 32 |
| [packages/darklang/tests/stdlib/exec.dark](../../../upstream/packages/darklang/tests/stdlib/exec.dark) | 3 |
| [packages/darklang/tests/stdlib/float32.dark](../../../upstream/packages/darklang/tests/stdlib/float32.dark) | 3 |
| [packages/darklang/tests/stdlib/http.dark](../../../upstream/packages/darklang/tests/stdlib/http.dark) | 5 |
| [packages/darklang/tests/stdlib/httpclient.dark](../../../upstream/packages/darklang/tests/stdlib/httpclient.dark) | 7 |
| [packages/darklang/tests/stdlib/httpserver.dark](../../../upstream/packages/darklang/tests/stdlib/httpserver.dark) | 3 |
| [packages/darklang/tests/stdlib/ints/int128.dark](../../../upstream/packages/darklang/tests/stdlib/ints/int128.dark) | 1 |
| [packages/darklang/tests/stdlib/ints/int64.dark](../../../upstream/packages/darklang/tests/stdlib/ints/int64.dark) | 2 |
| [packages/darklang/tests/stdlib/ints/uint128.dark](../../../upstream/packages/darklang/tests/stdlib/ints/uint128.dark) | 1 |
| [packages/darklang/tests/stdlib/ints/uint8.dark](../../../upstream/packages/darklang/tests/stdlib/ints/uint8.dark) | 1 |
| [packages/darklang/tests/stdlib/json.dark](../../../upstream/packages/darklang/tests/stdlib/json.dark) | 13 |
| [packages/darklang/tests/stdlib/list.dark](../../../upstream/packages/darklang/tests/stdlib/list.dark) | 20 |
| [packages/darklang/tests/stdlib/net.dark](../../../upstream/packages/darklang/tests/stdlib/net.dark) | 2 |
| [packages/darklang/tests/stdlib/regex.dark](../../../upstream/packages/darklang/tests/stdlib/regex.dark) | 15 |
| [packages/darklang/tests/stdlib/result.dark](../../../upstream/packages/darklang/tests/stdlib/result.dark) | 1 |
| [packages/darklang/tests/stdlib/sse.dark](../../../upstream/packages/darklang/tests/stdlib/sse.dark) | 8 |
| [packages/darklang/tests/stdlib/stream.dark](../../../upstream/packages/darklang/tests/stdlib/stream.dark) | 2 |
| [packages/darklang/tests/stdlib/string.dark](../../../upstream/packages/darklang/tests/stdlib/string.dark) | 17 |
| [packages/darklang/tests/stdlib/url.dark](../../../upstream/packages/darklang/tests/stdlib/url.dark) | 11 |
| [packages/darklang/tests/stdlib/utf8.dark](../../../upstream/packages/darklang/tests/stdlib/utf8.dark) | 2 |

| Kind | Items |
| --- | --- |
| function | 5,089 |
| implementation | 249 |
| named-test | 2,899 |
| trait | 22 |
| trait-method | 26 |
| type | 808 |
| val | 319 |

## Reasons

Tags are defined in [the glossary](../../../porting/compiler-reasons.json).

| Area | Tag | Items |
| --- | --- | --- |
| packages | `aot-type-error` | 10 |
| packages | `compiler-contract` | 14 |
| packages | `compiler-gap` | 18 |
| packages | `compiler-implementation` | 34 |
| packages | `constructor-identity` | 18 |
| packages | `container-adaptation` | 3 |
| packages | `runtime-reflection` | 5 |
| packages | `test-counter` | 1 |
| packages | `test-diagnostics` | 1 |
| packages | `upstream-dependency` | 2,214 |
| stdlib | `compiler-contract` | 122 |
| stdlib | `compiler-implementation` | 914 |
| stdlib | `compiler-timeout` | 1 |
| stdlib | `constructor-identity` | 2 |
| stdlib | `container-adaptation` | 490 |
| stdlib | `interpreter-intrinsic` | 1 |
| stdlib | `test-counter` | 2 |
| stdlib | `test-diagnostics` | 2 |
| stdlib | `upstream-dependency` | 189 |
| tests | `aot-arity-error` | 12 |
| tests | `aot-invalid-field` | 16 |
| tests | `aot-invalid-key` | 6 |
| tests | `aot-match-error` | 26 |
| tests | `aot-name-error` | 11 |
| tests | `aot-type-error` | 151 |
| tests | `compiler-crash` | 2 |
| tests | `compiler-gap` | 131 |
| tests | `compiler-leak` | 2 |
| tests | `compiler-timeout` | 1 |
| tests | `diagnostic-difference` | 2 |
| tests | `interpreter-fixture` | 47 |
| tests | `test-counter` | 7 |
| tests | `upstream-bug` | 1 |
| tests | `upstream-dependency` | 856 |

## Separate capability assessments

| Area | Assessments |
| --- | --- |
| features | 6 excluded, 9 missing, 1 partial, 199 supported |
| parser | 1 partial, 30 supported |
| builtins | 170 missing, 77 present, 361 unassessed |
