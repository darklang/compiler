# Current compiler port status

[How to read and update this ledger](../README.md).

Interpreter revision: `47d100c635562589a8527ed0ea5d5e67831845d7`.

The complete corpus contains 550 source files. Items are counted from the parsed sources and [their annotations](../../../porting/upstream-annotations.tsv).

A run disposition means selected, not a passing execution receipt. Replacement contracts are checked independently.

| Area | Source dispositions |
| --- | --- |
| packages | 2,608 blocked, 5 excluded, 35 replace, 2,080 run |
| stdlib | 831 blocked, 888 replace, 1,087 run |
| tests | 43 blocked, 1,717 run, 1,698 xfail |

## Expected test failures

Verified AOT rejection contracts cover programs rejected under the compiler’s static rules; they are not unfinished support.
Missing support includes compiler bugs, unavailable dependencies and test-runner gaps. Counts describe source contracts; execution receipts remain separate.

Upstream bugs record interpreter behavior or test expectations that the compiler deliberately rejects as incorrect; they are separate from missing support.

| Class | Compile | Runtime | Total |
| --- | ---: | ---: | ---: |
| `aot-rejection` | 203 | 0 | 203 |
| `missing-support` | 1,427 | 68 | 1,495 |

| Class | Reason | Tests |
| --- | --- | ---: |
| `aot-rejection` | `aot-arity-error` | 12 |
| `aot-rejection` | `aot-invalid-field` | 16 |
| `aot-rejection` | `aot-invalid-key` | 6 |
| `aot-rejection` | `aot-match-error` | 26 |
| `aot-rejection` | `aot-name-error` | 11 |
| `aot-rejection` | `aot-type-error` | 132 |
| `missing-support` | `compiler-contract` | 3 |
| `missing-support` | `compiler-gap` | 146 |
| `missing-support` | `compiler-resource` | 2 |
| `missing-support` | `interpreter-fixture` | 1 |
| `missing-support` | `test-counter` | 7 |
| `missing-support` | `upstream-dependency` | 1,336 |

### Remaining support by source

A named test remains one case even when it contains several assertions.

| Source | Tests |
| --- | ---: |
| [packages/darklang/tests/cli/authoring.dark](../../../upstream/packages/darklang/tests/cli/authoring.dark) | 2 |
| [packages/darklang/tests/cli/bashrc.dark](../../../upstream/packages/darklang/tests/cli/bashrc.dark) | 2 |
| [packages/darklang/tests/cli/builtinGroups.dark](../../../upstream/packages/darklang/tests/cli/builtinGroups.dark) | 4 |
| [packages/darklang/tests/cli/commandCompletions.dark](../../../upstream/packages/darklang/tests/cli/commandCompletions.dark) | 9 |
| [packages/darklang/tests/cli/commandGroups.dark](../../../upstream/packages/darklang/tests/cli/commandGroups.dark) | 3 |
| [packages/darklang/tests/cli/commandHelp.dark](../../../upstream/packages/darklang/tests/cli/commandHelp.dark) | 3 |
| [packages/darklang/tests/cli/commandSweeps.dark](../../../upstream/packages/darklang/tests/cli/commandSweeps.dark) | 9 |
| [packages/darklang/tests/cli/config.dark](../../../upstream/packages/darklang/tests/cli/config.dark) | 2 |
| [packages/darklang/tests/cli/configList.dark](../../../upstream/packages/darklang/tests/cli/configList.dark) | 2 |
| [packages/darklang/tests/cli/dashDash.dark](../../../upstream/packages/darklang/tests/cli/dashDash.dark) | 3 |
| [packages/darklang/tests/cli/deleteModule.dark](../../../upstream/packages/darklang/tests/cli/deleteModule.dark) | 13 |
| [packages/darklang/tests/cli/deprecate.dark](../../../upstream/packages/darklang/tests/cli/deprecate.dark) | 6 |
| [packages/darklang/tests/cli/deprecateShortNames.dark](../../../upstream/packages/darklang/tests/cli/deprecateShortNames.dark) | 2 |
| [packages/darklang/tests/cli/deprecationKinds.dark](../../../upstream/packages/darklang/tests/cli/deprecationKinds.dark) | 4 |
| [packages/darklang/tests/cli/descriptionFlag.dark](../../../upstream/packages/darklang/tests/cli/descriptionFlag.dark) | 5 |
| [packages/darklang/tests/cli/didYouMean.dark](../../../upstream/packages/darklang/tests/cli/didYouMean.dark) | 11 |
| [packages/darklang/tests/cli/docs.dark](../../../upstream/packages/darklang/tests/cli/docs.dark) | 2 |
| [packages/darklang/tests/cli/docsExamples.dark](../../../upstream/packages/darklang/tests/cli/docsExamples.dark) | 2 |
| [packages/darklang/tests/cli/docsForAI.dark](../../../upstream/packages/darklang/tests/cli/docsForAI.dark) | 7 |
| [packages/darklang/tests/cli/edit.dark](../../../upstream/packages/darklang/tests/cli/edit.dark) | 9 |
| [packages/darklang/tests/cli/editCommand.dark](../../../upstream/packages/darklang/tests/cli/editCommand.dark) | 4 |
| [packages/darklang/tests/cli/effectsAtSave.dark](../../../upstream/packages/darklang/tests/cli/effectsAtSave.dark) | 6 |
| [packages/darklang/tests/cli/evalAndRun.dark](../../../upstream/packages/darklang/tests/cli/evalAndRun.dark) | 25 |
| [packages/darklang/tests/cli/exitCodes.dark](../../../upstream/packages/darklang/tests/cli/exitCodes.dark) | 3 |
| [packages/darklang/tests/cli/file.dark](../../../upstream/packages/darklang/tests/cli/file.dark) | 6 |
| [packages/darklang/tests/cli/flags.dark](../../../upstream/packages/darklang/tests/cli/flags.dark) | 3 |
| [packages/darklang/tests/cli/fnSaveIssues.dark](../../../upstream/packages/darklang/tests/cli/fnSaveIssues.dark) | 1 |
| [packages/darklang/tests/cli/fnSaveLines.dark](../../../upstream/packages/darklang/tests/cli/fnSaveLines.dark) | 3 |
| [packages/darklang/tests/cli/format.dark](../../../upstream/packages/darklang/tests/cli/format.dark) | 6 |
| [packages/darklang/tests/cli/globalFlags.dark](../../../upstream/packages/darklang/tests/cli/globalFlags.dark) | 1 |
| [packages/darklang/tests/cli/golden.dark](../../../upstream/packages/darklang/tests/cli/golden.dark) | 2 |
| [packages/darklang/tests/cli/instanceTests.dark](../../../upstream/packages/darklang/tests/cli/instanceTests.dark) | 5 |
| [packages/darklang/tests/cli/interactiveTests.dark](../../../upstream/packages/darklang/tests/cli/interactiveTests.dark) | 4 |
| [packages/darklang/tests/cli/isolation.dark](../../../upstream/packages/darklang/tests/cli/isolation.dark) | 12 |
| [packages/darklang/tests/cli/jsonShapes.dark](../../../upstream/packages/darklang/tests/cli/jsonShapes.dark) | 15 |
| [packages/darklang/tests/cli/listing.dark](../../../upstream/packages/darklang/tests/cli/listing.dark) | 5 |
| [packages/darklang/tests/cli/moduleParseErrors.dark](../../../upstream/packages/darklang/tests/cli/moduleParseErrors.dark) | 11 |
| [packages/darklang/tests/cli/moduleSaveLines.dark](../../../upstream/packages/darklang/tests/cli/moduleSaveLines.dark) | 7 |
| [packages/darklang/tests/cli/nearbyNames.dark](../../../upstream/packages/darklang/tests/cli/nearbyNames.dark) | 1 |
| [packages/darklang/tests/cli/permissionsDisplay.dark](../../../upstream/packages/darklang/tests/cli/permissionsDisplay.dark) | 3 |
| [packages/darklang/tests/cli/permissionsGrammar.dark](../../../upstream/packages/darklang/tests/cli/permissionsGrammar.dark) | 27 |
| [packages/darklang/tests/cli/plainOutput.dark](../../../upstream/packages/darklang/tests/cli/plainOutput.dark) | 4 |
| [packages/darklang/tests/cli/propagate.dark](../../../upstream/packages/darklang/tests/cli/propagate.dark) | 1 |
| [packages/darklang/tests/cli/refusals.dark](../../../upstream/packages/darklang/tests/cli/refusals.dark) | 4 |
| [packages/darklang/tests/cli/releaseAssets.dark](../../../upstream/packages/darklang/tests/cli/releaseAssets.dark) | 3 |
| [packages/darklang/tests/cli/save.dark](../../../upstream/packages/darklang/tests/cli/save.dark) | 9 |
| [packages/darklang/tests/cli/saveReformatted.dark](../../../upstream/packages/darklang/tests/cli/saveReformatted.dark) | 3 |
| [packages/darklang/tests/cli/scripts.dark](../../../upstream/packages/darklang/tests/cli/scripts.dark) | 16 |
| [packages/darklang/tests/cli/search.dark](../../../upstream/packages/darklang/tests/cli/search.dark) | 8 |
| [packages/darklang/tests/cli/searchExactSeveral.dark](../../../upstream/packages/darklang/tests/cli/searchExactSeveral.dark) | 1 |
| [packages/darklang/tests/cli/searchLimits.dark](../../../upstream/packages/darklang/tests/cli/searchLimits.dark) | 2 |
| [packages/darklang/tests/cli/searchRanking.dark](../../../upstream/packages/darklang/tests/cli/searchRanking.dark) | 13 |
| [packages/darklang/tests/cli/searchShallow.dark](../../../upstream/packages/darklang/tests/cli/searchShallow.dark) | 2 |
| [packages/darklang/tests/cli/signaturesSeveral.dark](../../../upstream/packages/darklang/tests/cli/signaturesSeveral.dark) | 4 |
| [packages/darklang/tests/cli/signaturesTraitCalls.dark](../../../upstream/packages/darklang/tests/cli/signaturesTraitCalls.dark) | 2 |
| [packages/darklang/tests/cli/stdlibTypoRefused.dark](../../../upstream/packages/darklang/tests/cli/stdlibTypoRefused.dark) | 2 |
| [packages/darklang/tests/cli/streams.dark](../../../upstream/packages/darklang/tests/cli/streams.dark) | 4 |
| [packages/darklang/tests/cli/telemetry.dark](../../../upstream/packages/darklang/tests/cli/telemetry.dark) | 2 |
| [packages/darklang/tests/cli/terminalEscapes.dark](../../../upstream/packages/darklang/tests/cli/terminalEscapes.dark) | 4 |
| [packages/darklang/tests/cli/testCount.dark](../../../upstream/packages/darklang/tests/cli/testCount.dark) | 5 |
| [packages/darklang/tests/cli/testRunEnvironment.dark](../../../upstream/packages/darklang/tests/cli/testRunEnvironment.dark) | 1 |
| [packages/darklang/tests/cli/testSelection.dark](../../../upstream/packages/darklang/tests/cli/testSelection.dark) | 2 |
| [packages/darklang/tests/cli/traceVerbs.dark](../../../upstream/packages/darklang/tests/cli/traceVerbs.dark) | 20 |
| [packages/darklang/tests/cli/traces.dark](../../../upstream/packages/darklang/tests/cli/traces.dark) | 32 |
| [packages/darklang/tests/cli/tracesRendering.dark](../../../upstream/packages/darklang/tests/cli/tracesRendering.dark) | 12 |
| [packages/darklang/tests/cli/tui.dark](../../../upstream/packages/darklang/tests/cli/tui.dark) | 9 |
| [packages/darklang/tests/cli/unknownCommand.dark](../../../upstream/packages/darklang/tests/cli/unknownCommand.dark) | 3 |
| [packages/darklang/tests/cli/view.dark](../../../upstream/packages/darklang/tests/cli/view.dark) | 15 |
| [packages/darklang/tests/cli/viewRawModule.dark](../../../upstream/packages/darklang/tests/cli/viewRawModule.dark) | 6 |
| [packages/darklang/tests/cli/viewSeveral.dark](../../../upstream/packages/darklang/tests/cli/viewSeveral.dark) | 3 |
| [packages/darklang/tests/cli/workbench.dark](../../../upstream/packages/darklang/tests/cli/workbench.dark) | 33 |
| [packages/darklang/tests/cli/workbenchRepl.dark](../../../upstream/packages/darklang/tests/cli/workbenchRepl.dark) | 1 |
| [packages/darklang/tests/cli/zshrc.dark](../../../upstream/packages/darklang/tests/cli/zshrc.dark) | 2 |
| [packages/darklang/tests/language/aliases.dark](../../../upstream/packages/darklang/tests/language/aliases.dark) | 2 |
| [packages/darklang/tests/language/apply.dark](../../../upstream/packages/darklang/tests/language/apply.dark) | 4 |
| [packages/darklang/tests/language/basic.dark](../../../upstream/packages/darklang/tests/language/basic.dark) | 9 |
| [packages/darklang/tests/language/big.dark](../../../upstream/packages/darklang/tests/language/big.dark) | 1 |
| [packages/darklang/tests/language/collections.dark](../../../upstream/packages/darklang/tests/language/collections.dark) | 7 |
| [packages/darklang/tests/language/conditionals.dark](../../../upstream/packages/darklang/tests/language/conditionals.dark) | 5 |
| [packages/darklang/tests/language/enums.dark](../../../upstream/packages/darklang/tests/language/enums.dark) | 5 |
| [packages/darklang/tests/language/equality.dark](../../../upstream/packages/darklang/tests/language/equality.dark) | 3 |
| [packages/darklang/tests/language/errorPropagation.dark](../../../upstream/packages/darklang/tests/language/errorPropagation.dark) | 14 |
| [packages/darklang/tests/language/errorTypeNames.dark](../../../upstream/packages/darklang/tests/language/errorTypeNames.dark) | 11 |
| [packages/darklang/tests/language/eself.dark](../../../upstream/packages/darklang/tests/language/eself.dark) | 2 |
| [packages/darklang/tests/language/fastOps.dark](../../../upstream/packages/darklang/tests/language/fastOps.dark) | 3 |
| [packages/darklang/tests/language/lambdas.dark](../../../upstream/packages/darklang/tests/language/lambdas.dark) | 10 |
| [packages/darklang/tests/language/letBindings.dark](../../../upstream/packages/darklang/tests/language/letBindings.dark) | 6 |
| [packages/darklang/tests/language/matchExpressions.dark](../../../upstream/packages/darklang/tests/language/matchExpressions.dark) | 12 |
| [packages/darklang/tests/language/operators.dark](../../../upstream/packages/darklang/tests/language/operators.dark) | 5 |
| [packages/darklang/tests/language/pipe.dark](../../../upstream/packages/darklang/tests/language/pipe.dark) | 1 |
| [packages/darklang/tests/language/records.dark](../../../upstream/packages/darklang/tests/language/records.dark) | 9 |
| [packages/darklang/tests/language/runtimeToProgramTypes.dark](../../../upstream/packages/darklang/tests/language/runtimeToProgramTypes.dark) | 6 |
| [packages/darklang/tests/language/tailCalls.dark](../../../upstream/packages/darklang/tests/language/tailCalls.dark) | 2 |
| [packages/darklang/tests/language/traits.dark](../../../upstream/packages/darklang/tests/language/traits.dark) | 9 |
| [packages/darklang/tests/language/tupleLiterals.dark](../../../upstream/packages/darklang/tests/language/tupleLiterals.dark) | 4 |
| [packages/darklang/tests/language/unwrap.dark](../../../upstream/packages/darklang/tests/language/unwrap.dark) | 6 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/basics.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/basics.dark) | 34 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/batches.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/batches.dark) | 30 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/declarations.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/declarations.dark) | 14 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/dictKeys.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/dictKeys.dark) | 12 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/lets.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/lets.dark) | 26 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/namesAndPatterns.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/namesAndPatterns.dark) | 32 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/operators.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/operators.dark) | 23 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/packageTests.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/packageTests.dark) | 12 |
| [packages/darklang/tests/languageTools/atRestTypeChecker/unwrapAndTuples.dark](../../../upstream/packages/darklang/tests/languageTools/atRestTypeChecker/unwrapAndTuples.dark) | 27 |
| [packages/darklang/tests/languageTools/builtinIntrospection.dark](../../../upstream/packages/darklang/tests/languageTools/builtinIntrospection.dark) | 4 |
| [packages/darklang/tests/languageTools/formatter.dark](../../../upstream/packages/darklang/tests/languageTools/formatter.dark) | 15 |
| [packages/darklang/tests/languageTools/live.dark](../../../upstream/packages/darklang/tests/languageTools/live.dark) | 9 |
| [packages/darklang/tests/languageTools/opWalks.dark](../../../upstream/packages/darklang/tests/languageTools/opWalks.dark) | 4 |
| [packages/darklang/tests/languageTools/parsedFileShape.dark](../../../upstream/packages/darklang/tests/languageTools/parsedFileShape.dark) | 4 |
| [packages/darklang/tests/languageTools/parser/diagnostics.dark](../../../upstream/packages/darklang/tests/languageTools/parser/diagnostics.dark) | 25 |
| [packages/darklang/tests/languageTools/parser/trees.dark](../../../upstream/packages/darklang/tests/languageTools/parser/trees.dark) | 22 |
| [packages/darklang/tests/languageTools/perf.dark](../../../upstream/packages/darklang/tests/languageTools/perf.dark) | 7 |
| [packages/darklang/tests/languageTools/pickLocation.dark](../../../upstream/packages/darklang/tests/languageTools/pickLocation.dark) | 8 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripExprs.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripExprs.dark) | 29 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripFunctionDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripFunctionDeclarations.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripModuleDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripModuleDeclarations.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripSourceFiles.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripSourceFiles.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripTestDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripTestDeclarations.dark) | 1 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripTypeDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripTypeDeclarations.dark) | 2 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripTypeReferences.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripTypeReferences.dark) | 3 |
| [packages/darklang/tests/languageTools/roundTrip/roundTripValueDeclarations.dark](../../../upstream/packages/darklang/tests/languageTools/roundTrip/roundTripValueDeclarations.dark) | 3 |
| [packages/darklang/tests/languageTools/semanticTokenization.dark](../../../upstream/packages/darklang/tests/languageTools/semanticTokenization.dark) | 9 |
| [packages/darklang/tests/languageTools/valueSearch.dark](../../../upstream/packages/darklang/tests/languageTools/valueSearch.dark) | 1 |
| [packages/darklang/tests/prettyPrinter/runtimeTypes.dark](../../../upstream/packages/darklang/tests/prettyPrinter/runtimeTypes.dark) | 15 |
| [packages/darklang/tests/scm/branchIdentity.dark](../../../upstream/packages/darklang/tests/scm/branchIdentity.dark) | 4 |
| [packages/darklang/tests/scm/branches.dark](../../../upstream/packages/darklang/tests/scm/branches.dark) | 11 |
| [packages/darklang/tests/scm/cli/branch.dark](../../../upstream/packages/darklang/tests/scm/cli/branch.dark) | 20 |
| [packages/darklang/tests/scm/cli/commit.dark](../../../upstream/packages/darklang/tests/scm/cli/commit.dark) | 12 |
| [packages/darklang/tests/scm/cli/conflicts.dark](../../../upstream/packages/darklang/tests/scm/cli/conflicts.dark) | 13 |
| [packages/darklang/tests/scm/cli/discard.dark](../../../upstream/packages/darklang/tests/scm/cli/discard.dark) | 8 |
| [packages/darklang/tests/scm/cli/history.dark](../../../upstream/packages/darklang/tests/scm/cli/history.dark) | 6 |
| [packages/darklang/tests/scm/cli/include.dark](../../../upstream/packages/darklang/tests/scm/cli/include.dark) | 8 |
| [packages/darklang/tests/scm/cli/merge.dark](../../../upstream/packages/darklang/tests/scm/cli/merge.dark) | 13 |
| [packages/darklang/tests/scm/cli/propagation.dark](../../../upstream/packages/darklang/tests/scm/cli/propagation.dark) | 13 |
| [packages/darklang/tests/scm/cli/rebase.dark](../../../upstream/packages/darklang/tests/scm/cli/rebase.dark) | 7 |
| [packages/darklang/tests/scm/cli/rename.dark](../../../upstream/packages/darklang/tests/scm/cli/rename.dark) | 22 |
| [packages/darklang/tests/scm/cli/undo.dark](../../../upstream/packages/darklang/tests/scm/cli/undo.dark) | 9 |
| [packages/darklang/tests/scm/conflicts.dark](../../../upstream/packages/darklang/tests/scm/conflicts.dark) | 21 |
| [packages/darklang/tests/scm/draft.dark](../../../upstream/packages/darklang/tests/scm/draft.dark) | 6 |
| [packages/darklang/tests/scm/lww.dark](../../../upstream/packages/darklang/tests/scm/lww.dark) | 6 |
| [packages/darklang/tests/scm/propagation.dark](../../../upstream/packages/darklang/tests/scm/propagation.dark) | 17 |
| [packages/darklang/tests/scm/referenceNames.dark](../../../upstream/packages/darklang/tests/scm/referenceNames.dark) | 2 |
| [packages/darklang/tests/scm/removalConflicts.dark](../../../upstream/packages/darklang/tests/scm/removalConflicts.dark) | 5 |
| [packages/darklang/tests/stdlib/altJson.dark](../../../upstream/packages/darklang/tests/stdlib/altJson.dark) | 7 |
| [packages/darklang/tests/stdlib/base16.dark](../../../upstream/packages/darklang/tests/stdlib/base16.dark) | 5 |
| [packages/darklang/tests/stdlib/bytes.dark](../../../upstream/packages/darklang/tests/stdlib/bytes.dark) | 21 |
| [packages/darklang/tests/stdlib/char.dark](../../../upstream/packages/darklang/tests/stdlib/char.dark) | 2 |
| [packages/darklang/tests/stdlib/cli/dir.dark](../../../upstream/packages/darklang/tests/stdlib/cli/dir.dark) | 1 |
| [packages/darklang/tests/stdlib/cli/file.dark](../../../upstream/packages/darklang/tests/stdlib/cli/file.dark) | 8 |
| [packages/darklang/tests/stdlib/cli/posix.dark](../../../upstream/packages/darklang/tests/stdlib/cli/posix.dark) | 11 |
| [packages/darklang/tests/stdlib/cli/process.dark](../../../upstream/packages/darklang/tests/stdlib/cli/process.dark) | 9 |
| [packages/darklang/tests/stdlib/cli/sgr.dark](../../../upstream/packages/darklang/tests/stdlib/cli/sgr.dark) | 4 |
| [packages/darklang/tests/stdlib/cli/sys.dark](../../../upstream/packages/darklang/tests/stdlib/cli/sys.dark) | 2 |
| [packages/darklang/tests/stdlib/cli/widgets.dark](../../../upstream/packages/darklang/tests/stdlib/cli/widgets.dark) | 6 |
| [packages/darklang/tests/stdlib/crypto.dark](../../../upstream/packages/darklang/tests/stdlib/crypto.dark) | 10 |
| [packages/darklang/tests/stdlib/dateTime.dark](../../../upstream/packages/darklang/tests/stdlib/dateTime.dark) | 34 |
| [packages/darklang/tests/stdlib/decimal.dark](../../../upstream/packages/darklang/tests/stdlib/decimal.dark) | 6 |
| [packages/darklang/tests/stdlib/dict.dark](../../../upstream/packages/darklang/tests/stdlib/dict.dark) | 33 |
| [packages/darklang/tests/stdlib/exec.dark](../../../upstream/packages/darklang/tests/stdlib/exec.dark) | 3 |
| [packages/darklang/tests/stdlib/float32.dark](../../../upstream/packages/darklang/tests/stdlib/float32.dark) | 6 |
| [packages/darklang/tests/stdlib/float64.dark](../../../upstream/packages/darklang/tests/stdlib/float64.dark) | 17 |
| [packages/darklang/tests/stdlib/http.dark](../../../upstream/packages/darklang/tests/stdlib/http.dark) | 4 |
| [packages/darklang/tests/stdlib/httpclient.dark](../../../upstream/packages/darklang/tests/stdlib/httpclient.dark) | 4 |
| [packages/darklang/tests/stdlib/httpserver.dark](../../../upstream/packages/darklang/tests/stdlib/httpserver.dark) | 2 |
| [packages/darklang/tests/stdlib/ints/bigInt.dark](../../../upstream/packages/darklang/tests/stdlib/ints/bigInt.dark) | 5 |
| [packages/darklang/tests/stdlib/ints/int128.dark](../../../upstream/packages/darklang/tests/stdlib/ints/int128.dark) | 1 |
| [packages/darklang/tests/stdlib/ints/int64.dark](../../../upstream/packages/darklang/tests/stdlib/ints/int64.dark) | 2 |
| [packages/darklang/tests/stdlib/ints/uint128.dark](../../../upstream/packages/darklang/tests/stdlib/ints/uint128.dark) | 1 |
| [packages/darklang/tests/stdlib/ints/uint8.dark](../../../upstream/packages/darklang/tests/stdlib/ints/uint8.dark) | 1 |
| [packages/darklang/tests/stdlib/json.dark](../../../upstream/packages/darklang/tests/stdlib/json.dark) | 7 |
| [packages/darklang/tests/stdlib/json/collections.dark](../../../upstream/packages/darklang/tests/stdlib/json/collections.dark) | 4 |
| [packages/darklang/tests/stdlib/json/userTypes.dark](../../../upstream/packages/darklang/tests/stdlib/json/userTypes.dark) | 6 |
| [packages/darklang/tests/stdlib/list.dark](../../../upstream/packages/darklang/tests/stdlib/list.dark) | 23 |
| [packages/darklang/tests/stdlib/math.dark](../../../upstream/packages/darklang/tests/stdlib/math.dark) | 7 |
| [packages/darklang/tests/stdlib/net.dark](../../../upstream/packages/darklang/tests/stdlib/net.dark) | 2 |
| [packages/darklang/tests/stdlib/regex.dark](../../../upstream/packages/darklang/tests/stdlib/regex.dark) | 6 |
| [packages/darklang/tests/stdlib/result.dark](../../../upstream/packages/darklang/tests/stdlib/result.dark) | 1 |
| [packages/darklang/tests/stdlib/sse.dark](../../../upstream/packages/darklang/tests/stdlib/sse.dark) | 10 |
| [packages/darklang/tests/stdlib/stream.dark](../../../upstream/packages/darklang/tests/stdlib/stream.dark) | 4 |
| [packages/darklang/tests/stdlib/string.dark](../../../upstream/packages/darklang/tests/stdlib/string.dark) | 8 |
| [packages/darklang/tests/stdlib/test.dark](../../../upstream/packages/darklang/tests/stdlib/test.dark) | 6 |
| [packages/darklang/tests/stdlib/url.dark](../../../upstream/packages/darklang/tests/stdlib/url.dark) | 13 |
| [packages/darklang/tests/stdlib/utf8.dark](../../../upstream/packages/darklang/tests/stdlib/utf8.dark) | 1 |
| [packages/darklang/tests/stdlib/uuid.dark](../../../upstream/packages/darklang/tests/stdlib/uuid.dark) | 1 |

| Kind | Items |
| --- | --- |
| function | 5,869 |
| implementation | 272 |
| named-test | 3,458 |
| trait | 24 |
| trait-method | 28 |
| type | 919 |
| val | 422 |

## Reasons

Tags are defined in [the glossary](../../../porting/compiler-reasons.json).

| Area | Tag | Items |
| --- | --- | --- |
| packages | `aot-type-error` | 10 |
| packages | `compiler-contract` | 15 |
| packages | `compiler-gap` | 23 |
| packages | `compiler-implementation` | 34 |
| packages | `constructor-identity` | 18 |
| packages | `container-adaptation` | 3 |
| packages | `runtime-reflection` | 5 |
| packages | `test-counter` | 1 |
| packages | `test-diagnostics` | 1 |
| packages | `upstream-dependency` | 2,538 |
| stdlib | `compiler-contract` | 126 |
| stdlib | `compiler-gap` | 6 |
| stdlib | `compiler-implementation` | 886 |
| stdlib | `compiler-timeout` | 1 |
| stdlib | `constructor-identity` | 2 |
| stdlib | `container-adaptation` | 490 |
| stdlib | `test-counter` | 2 |
| stdlib | `test-diagnostics` | 2 |
| stdlib | `upstream-dependency` | 204 |
| tests | `aot-arity-error` | 12 |
| tests | `aot-invalid-field` | 16 |
| tests | `aot-invalid-key` | 6 |
| tests | `aot-match-error` | 26 |
| tests | `aot-name-error` | 11 |
| tests | `aot-type-error` | 132 |
| tests | `compiler-contract` | 3 |
| tests | `compiler-crash` | 2 |
| tests | `compiler-gap` | 175 |
| tests | `compiler-leak` | 2 |
| tests | `compiler-resource` | 2 |
| tests | `compiler-timeout` | 2 |
| tests | `diagnostic-difference` | 1 |
| tests | `interpreter-fixture` | 3 |
| tests | `test-counter` | 7 |
| tests | `upstream-dependency` | 1,342 |

## Separate capability assessments

| Area | Assessments |
| --- | --- |
| features | 6 excluded, 9 missing, 1 partial, 199 supported |
| parser | 1 partial, 30 supported |
| builtins | 88 missing, 48 present, 233 unassessed |
