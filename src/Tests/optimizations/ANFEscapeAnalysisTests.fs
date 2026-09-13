// ANFEscapeAnalysisTests.fs - Tests local scalar aggregate replacement.
//
// These fixtures pin both allocation removal and the conservative escape and
// ownership boundaries of the first escape-analysis implementation.

module ANFEscapeAnalysisTests

open ANF

type TestResult = Result<unit, string>

let private pointDescriptor fieldType =
    { SourceTypeName = "Point"
      RuntimeTypeName = "Point"
      TypeArgs = []
      Fields = ["x", fieldType; "y", fieldType] }

let rec private containsAggregateAllocation (expr: AExpr) : bool =
    match expr with
    | Return _ -> false
    | Let (_, cexpr, body) ->
        match cexpr with
        | TupleAlloc _
        | RecordAlloc _
        | RecordClone _ -> true
        | _ -> containsAggregateAllocation body
    | If (_, thenBranch, elseBranch) ->
        containsAggregateAllocation thenBranch
        || containsAggregateAllocation elseBranch

let private optimizeBody (body: AExpr) : AExpr =
    let func =
        { Name = "fixture"
          TypedParams = []
          ReturnType = AST.TInt64
          ReturnOwnership = OwnedReturn
          Body = body }
    let (Program (functions, _)) =
        ANF_EscapeAnalysis.scalarReplaceProgram
            (Program ([func], Return UnitLiteral))
    match functions with
    | [optimized] -> optimized.Body
    | _ -> Crash.crash "ANFEscapeAnalysisTests: fixture function disappeared"

let testScalarRecordProjectionRemovesAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TInt64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [IntLiteral (Int64 10L); IntLiteral (Int64 20L)]),
            Let (TempId 1, RecordGet (descriptor, Var (TempId 0), 1), Return (Var (TempId 1)))
        )
        |> optimizeBody
    match body with
    | Let (TempId 1, Atom (IntLiteral (Int64 20L)), Return (Var (TempId 1))) -> Ok ()
    | _ -> Error $"Expected scalar record projection without allocation, got {body}"

let testAggregateAliasRemovesAllocation () : TestResult =
    let body =
        Let (
            TempId 0,
            TupleAlloc [IntLiteral (Int64 10L); IntLiteral (Int64 20L)],
            Let (
                TempId 1,
                Atom (Var (TempId 0)),
                Let (TempId 2, TupleGet (Var (TempId 1), 0), Return (Var (TempId 2)))
            )
        )
        |> optimizeBody
    if containsAggregateAllocation body then
        Error "Expected projection-only tuple alias to be scalar-replaced"
    else
        match body with
        | Let (TempId 2, Atom (IntLiteral (Int64 10L)), Return (Var (TempId 2))) -> Ok ()
        | _ -> Error $"Expected tuple alias and projection to become a scalar binding, got {body}"

let testScalarRecordCloneChainRemovesAllocations () : TestResult =
    let descriptor = pointDescriptor AST.TInt64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [IntLiteral (Int64 1L); IntLiteral (Int64 2L)]),
            Let (
                TempId 1,
                RecordClone (descriptor, Var (TempId 0), [IntLiteral (Int64 3L); IntLiteral (Int64 2L)]),
                Let (TempId 2, RecordGet (descriptor, Var (TempId 1), 0), Return (Var (TempId 2)))
            )
        )
        |> optimizeBody
    if containsAggregateAllocation body then
        Error "Expected scalar record clone chain to be scalar-replaced"
    else Ok ()

let testEscapingCloneRetainsOnlyCloneAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TInt64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [IntLiteral (Int64 1L); IntLiteral (Int64 2L)]),
            Let (
                TempId 1,
                RecordClone (descriptor, Var (TempId 0), [IntLiteral (Int64 3L); IntLiteral (Int64 2L)]),
                Return (Var (TempId 1))
            )
        )
        |> optimizeBody
    match body with
    | Let (TempId 1, RecordAlloc (_, _), Return (Var (TempId 1))) -> Ok ()
    | _ -> Error $"Expected an escaping clone to retain only its own allocation, got {body}"

let testReturnedRecordPreservesAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TInt64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [IntLiteral (Int64 1L); IntLiteral (Int64 2L)]),
            Return (Var (TempId 0))
        )
        |> optimizeBody
    if containsAggregateAllocation body then Ok ()
    else Error "Expected returned record allocation to be preserved"

let testRecordPassedToCallPreservesAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TInt64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [IntLiteral (Int64 1L); IntLiteral (Int64 2L)]),
            Let (TempId 1, Call ("consume", [Var (TempId 0)]), Return (Var (TempId 1)))
        )
        |> optimizeBody
    if containsAggregateAllocation body then Ok ()
    else Error "Expected record passed to a call to preserve its allocation"

let testRecordCapturedByClosurePreservesAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TInt64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [IntLiteral (Int64 1L); IntLiteral (Int64 2L)]),
            Let (TempId 1, ClosureAlloc ("capture", [Var (TempId 0)]), Return (IntLiteral (Int64 0L)))
        )
        |> optimizeBody
    if containsAggregateAllocation body then Ok ()
    else Error "Expected closure-captured record to preserve its allocation"

let testBranchLocalProjectionsRemoveAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TInt64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [IntLiteral (Int64 1L); IntLiteral (Int64 2L)]),
            If (
                BoolLiteral true,
                Let (TempId 1, RecordGet (descriptor, Var (TempId 0), 0), Return (Var (TempId 1))),
                Let (TempId 2, RecordGet (descriptor, Var (TempId 0), 1), Return (Var (TempId 2)))
            )
        )
        |> optimizeBody
    if containsAggregateAllocation body then
        Error "Expected projection-only branch uses to be scalar-replaced"
    else Ok ()

let testManagedRecordPreservesAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TString
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [StringLiteral "left"; StringLiteral "right"]),
            Let (TempId 1, RecordGet (descriptor, Var (TempId 0), 0), Return (Var (TempId 1)))
        )
        |> optimizeBody
    if containsAggregateAllocation body then Ok ()
    else Error "Expected record with managed fields to be preserved"

let testFloatRecordPreservesAllocation () : TestResult =
    let descriptor = pointDescriptor AST.TFloat64
    let body =
        Let (
            TempId 0,
            RecordAlloc (descriptor, [FloatLiteral 1.0; FloatLiteral 2.0]),
            Let (TempId 1, RecordGet (descriptor, Var (TempId 0), 0), Return (Var (TempId 1)))
        )
        |> optimizeBody
    if containsAggregateAllocation body then Ok ()
    else Error "Expected Float record to remain allocated until Float spilling is supported"

let tests =
    [ ("Scalar record projection removes allocation", testScalarRecordProjectionRemovesAllocation)
      ("Projection-only aggregate alias removes allocation", testAggregateAliasRemovesAllocation)
      ("Scalar record clone chain removes allocations", testScalarRecordCloneChainRemovesAllocations)
      ("Escaping clone retains only clone allocation", testEscapingCloneRetainsOnlyCloneAllocation)
      ("Returned record preserves allocation", testReturnedRecordPreservesAllocation)
      ("Record passed to call preserves allocation", testRecordPassedToCallPreservesAllocation)
      ("Closure-captured record preserves allocation", testRecordCapturedByClosurePreservesAllocation)
      ("Branch-local record projections remove allocation", testBranchLocalProjectionsRemoveAllocation)
      ("Managed record preserves allocation", testManagedRecordPreservesAllocation)
      ("Float record preserves allocation", testFloatRecordPreservesAllocation) ]
