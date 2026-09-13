// ListHIRTests.fs - Region eligibility, ownership accounting, and storage budgets.

module ListHIRTests

let private call name args = AST.Call (name, AST.NonEmptyList.fromList args)
let private bind name value body = AST.Let (AST.LPVariable name, value, body)
let private values count = AST.ListLiteral ([1 .. count] |> List.map (int64 >> AST.Int64Literal))
let private map input = call "Stdlib.List.map_i64_i64" [input; AST.Closure ("mapCallback", [])]
let private reverse input = call "Stdlib.List.reverse_i64" [input]
let private fold input = call "Stdlib.List.fold_i64_i64" [input; AST.Int64Literal 0L; AST.Closure ("foldCallback", [])]

let private functions : AST_to_ANF.FunctionRegistry =
    Map.ofList [
        "mapCallback", AST.TFunction ([AST.TRawPtr; AST.TInt64], AST.TInt64)
        "foldCallback", AST.TFunction ([AST.TRawPtr; AST.TInt64; AST.TInt64], AST.TInt64)
        "Stdlib.List.map_i64_i64", AST.TFunction ([AST.TList AST.TInt64; AST.TFunction ([AST.TInt64], AST.TInt64)], AST.TList AST.TInt64)
        "Stdlib.List.reverse_i64", AST.TFunction ([AST.TList AST.TInt64], AST.TList AST.TInt64)
        "Stdlib.List.fold_i64_i64", AST.TFunction ([AST.TList AST.TInt64; AST.TInt64; AST.TFunction ([AST.TInt64; AST.TInt64], AST.TInt64)], AST.TInt64)
    ]

let private extract expression =
    let infer types expr = AST_to_ANF.inferTypeCore Set.empty expr types Map.empty Map.empty functions Map.empty
    ListHIR.tryExtract infer (fun expr -> AST_to_ANF.freeVars expr Set.empty) expression

let private checkSummary expression expected () =
    match extract expression with
    | None -> Error "Expected a closed List<Int64> region"
    | Some region ->
        let owned = region |> ListHIR.selectStorage |> ListHIR.elaborateOwnership
        ListHIR.verify owned |> Result.bind (fun () ->
            let actual = ListHIR.allocationSummary owned
            if actual = expected then Ok () else Error $"Storage budget: expected {expected}, got {actual}")

let private unique = bind "xs" (values 3) (bind "ys" (map (AST.Var "xs")) (fold (reverse (AST.Var "ys"))))
let private shared =
    bind "xs" (values 3)
        (bind "ys" (map (AST.Var "xs"))
            (bind "old" (fold (AST.Var "xs")) (fold (AST.Var "ys"))))

let private rejects expression () =
    match extract expression with
    | None -> Ok ()
    | Some _ -> Error "An unsupported or escaping list entered array storage"

let private testLoweredBudget () =
    AST_to_ANF.toANF unique ANF.initialVarGen Map.empty Map.empty Map.empty functions Map.empty
    |> Result.bind (fun (body, _) ->
        let rec counts expression =
            match expression with
            | ANF.Return _ -> 0, 0
            | ANF.If (_, yes, no) -> let a, b = counts yes in let c, d = counts no in a + c, b + d
            | ANF.Let (_, operation, tail) ->
                let allocations, releases = counts tail
                match operation with
                | ANF.RawAlloc _ -> allocations + 1, releases
                | ANF.RefCountDec _ -> allocations, releases + 1
                | _ -> allocations, releases
        if counts body = (1, 1) then Ok ()
        else Error $"Expected one native array allocation and release, got {counts body}")

let private rejectsOwnership operations () =
    match ListHIR.verifyOwnership operations with
    | Error _ -> Ok ()
    | Ok () -> Error "Ownership verifier accepted an invalid lifetime"

let private root = ListHIR.ListId 0
let private construct releases : ListHIR.OwnedOperation =
    { Operation = ListHIR.Construct (root, []); Releases = releases }

let tests = [
    "List HIR consumes unique map/reverse storage", checkSummary unique { Allocations = 1; AllocatedBytes = 56; Copies = 0; ReusedTransforms = 2; Releases = 1 }
    "List HIR copies a surviving source version", checkSummary shared { Allocations = 2; AllocatedBytes = 112; Copies = 1; ReusedTransforms = 0; Releases = 2 }
    "List HIR normalizes aliases before last-use solving", checkSummary (bind "xs" (values 3) (bind "alias" (AST.Var "xs") (fold (reverse (AST.Var "alias"))))) { Allocations = 1; AllocatedBytes = 56; Copies = 0; ReusedTransforms = 1; Releases = 1 }
    "List HIR releases unused construction", checkSummary (bind "xs" (values 3) (AST.Int64Literal 1L)) { Allocations = 1; AllocatedBytes = 56; Copies = 0; ReusedTransforms = 0; Releases = 1 }
    "List HIR supports the largest recyclable array", checkSummary (fold (reverse (values 28))) { Allocations = 1; AllocatedBytes = 256; Copies = 0; ReusedTransforms = 1; Releases = 1 }
    "List HIR preserves native allocation budget", testLoweredBudget
    "List HIR rejects escaping lists", rejects (bind "xs" (values 3) (AST.Var "xs"))
    "List HIR rejects oversized arrays", rejects (fold (reverse (values 29)))
    "List HIR rejects borrowed input lists", rejects (fold (reverse (AST.Var "external")))
    "List HIR rejects managed elements", rejects (bind "xs" (AST.ListLiteral [AST.StringLiteral "a"]) (AST.Int64Literal 0L))
    "List HIR rejects callbacks capturing region lists", rejects (bind "xs" (values 3) (fold (call "Stdlib.List.map_i64_i64" [AST.Var "xs"; AST.Closure ("mapCallback", [AST.Var "xs"])])))
    "List HIR verifier rejects duplicate release", rejectsOwnership [construct [root; root]]
    "List HIR verifier rejects leaked roots", rejectsOwnership [construct []]
    "List HIR verifier rejects reused identities", rejectsOwnership [construct [root]; construct [root]]
    "List HIR verifier rejects mutation after release", rejectsOwnership [construct [root]; { Operation = ListHIR.Transform (ListHIR.ListId 1, root, (ListHIR.Reverse, ListHIR.Consume)); Releases = [ListHIR.ListId 1] }]
]
