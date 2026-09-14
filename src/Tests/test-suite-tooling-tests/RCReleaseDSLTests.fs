// RCReleaseDSLTests.fs - Tests for semantic reference-release fixture parsing.
//
// Covers typed shape parsing, invalid placement, and executable release behavior.

module RCReleaseDSLTests

open TestDSL.RCReleaseFormat
open TestDSL.RCReleaseTestRunner

type TestResult = Result<unit, string>

let testParsesNestedManagedShape () : TestResult =
    let content =
        """---NAME---
nested graph
---ROOT---
tuple(string, list(i64), dict(string, record(blob)))
"""

    match parseRCReleaseFileContent "nested.rcrelease" content with
    | Ok [{ Root = TupleValue [ DynamicString; ListValue Int64Value; DictValue (DynamicString, RecordValue [ DynamicBlob ]) ] }] -> Ok ()
    | Ok tests -> Error $"Expected one nested release shape, got {tests}"
    | Error msg -> Error $"Expected nested release shape to parse: {msg}"

let testRejectsPreserveWithoutRootRegister () : TestResult =
    let content =
        """---NAME---
invalid placement
---ROOT---
tuple(string)
---PRESERVE---
X0 = 42
"""

    match parseRCReleaseFileContent "invalid.rcrelease" content with
    | Error msg when msg.Contains "requires ROOT-REGISTER" -> Ok ()
    | Error msg -> Error $"Expected placement error, got: {msg}"
    | Ok _ -> Error "Expected PRESERVE without ROOT-REGISTER to fail"

let testRejectsInvalidShapeArity () : TestResult =
    let content =
        """---NAME---
invalid dict
---ROOT---
dict(string)
"""

    match parseRCReleaseFileContent "invalid.rcrelease" content with
    | Error msg when msg.Contains "exactly two" -> Ok ()
    | Error msg -> Error $"Expected dict arity error, got: {msg}"
    | Ok _ -> Error "Expected one-argument dict shape to fail"

let testRunsNestedReleaseCase target () : TestResult =
    let content =
        """---NAME---
release nested graph
---ROOT---
tuple(string, list(i64), dict(i64, string))
"""

    match parseRCReleaseFileContent "release.rcrelease" content with
    | Ok [ test ] -> runRCReleaseTest target test
    | Ok tests -> Error $"Expected one release case, got {List.length tests}"
    | Error msg -> Error $"Expected release case to parse: {msg}"

let tests target = [
    ("Reference-release DSL parses nested managed shapes", testParsesNestedManagedShape)
    ("Reference-release DSL rejects preservation without root placement", testRejectsPreserveWithoutRootRegister)
    ("Reference-release DSL rejects invalid shape arity", testRejectsInvalidShapeArity)
    ("Reference-release DSL executes nested release", testRunsNestedReleaseCase target)
]
