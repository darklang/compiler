// ListHIRTests.fs - Region eligibility, ownership accounting, and storage budgets.

module ListHIRTests

let private call name args = AST.Call (name, AST.NonEmptyList.fromList args)
let private bind name value body = AST.Let (AST.LPVariable name, value, body)
let private values count = AST.ListLiteral ([1 .. count] |> List.map (int64 >> AST.Int64Literal))
let private map input = call "Stdlib.List.map_i64_i64" [input; AST.Closure ("mapCallback", [])]
let private reverse input = call "Stdlib.List.reverse_i64" [input]
let private fold input = call "Stdlib.List.fold_i64_i64" [input; AST.Int64Literal 0L; AST.Closure ("foldCallback", [])]
let private repeat = call "Stdlib.List.repeatUnsafe_i64" [AST.BigIntLiteral 3I; AST.Int64Literal 7L]
let private bytes constant : ListHIR.AllocationBytes = { ConstantBytes = constant; RuntimeBuffers = Map.empty }
let private runtimeBytes constant terms : ListHIR.AllocationBytes = { ConstantBytes = constant; RuntimeBuffers = Map.ofList terms }

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

let private checkBudget expression expected () =
    match extract expression with
    | None -> Error "Expected a closed List<Int64> region"
    | Some region ->
        ListHIR.verifyFunctional region |> Result.bind (fun () ->
            let owned = region |> ListHIR.selectStorage |> ListHIR.elaborateOwnership
            ListHIR.verify owned |> Result.bind (fun () ->
                let actual = ListHIR.allocationBudget owned
                if actual = expected then Ok () else Error $"Storage budget: expected {expected}, got {actual}"))

let private checkSummary expression expected = checkBudget expression (ListHIR.Complete expected)

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
    { Operation = ListHIR.Construct (root, ListHIR.Literal []); Releases = releases }

let private zero : ListHIR.AllocationSummary = { Allocations = 0; AllocatedBytes = bytes 0L; Copies = 0; ReusedTransforms = 0; Releases = 0 }
let private allocated = { zero with Allocations = 1; AllocatedBytes = bytes 56L }
let private choice yes no = AST.If (AST.BoolLiteral true, yes, no)
let private branchUses = bind "xs" (values 3) (choice (fold (map (AST.Var "xs"))) (fold (reverse (AST.Var "xs"))))
let private branchJoin = bind "xs" (values 3) (bind "selected" (choice (fold (reverse (AST.Var "xs"))) (AST.Int64Literal 7L)) (fold (AST.Var "xs")))
let private manyBranches count =
    bind "xs" (values 3)
        (List.foldBack (fun index body -> bind $"branch{index}" (choice (fold (AST.Var "xs")) (AST.Int64Literal 7L)) body) [1 .. count] (fold (AST.Var "xs")))
let private ownedBlock releases operations : ListHIR.OwnedBlock =
    { EntryReleases = releases
      Body = { Operations = operations; Result = { Expression = AST.Int64Literal 0L; Type = AST.TInt64 } } }
let private ownedBranch yes no : ListHIR.OwnedOperation =
    { Operation = ListHIR.Branch ("joined", { Expression = AST.BoolLiteral true; Type = AST.TBool }, yes, no); Releases = [] }

let tests = [
    "List HIR bounds continuation expansion", rejects (manyBranches 5)
    "List HIR accepts the bounded continuation frontier", (fun () ->
        match extract (manyBranches 4) with
        | None -> Error "Expected sixteen-path region"
        | Some region -> region |> ListHIR.selectStorage |> ListHIR.elaborateOwnership |> ListHIR.verify)
    "List HIR rejects list-valued branch joins", rejects (bind "xs" (values 3) (bind "selected" (choice (reverse (AST.Var "xs")) (AST.Var "xs")) (fold (AST.Var "selected"))))
    "List HIR rejects branch callbacks hiding aliases", rejects (bind "xs" (values 3) (choice (fold (call "Stdlib.List.map_i64_i64" [AST.Var "xs"; AST.Closure ("mapCallback", [AST.Var "xs"])])) (AST.Int64Literal 7L)))
    "List HIR consumes independently on mutually exclusive paths", checkBudget branchUses (ListHIR.Conditional (allocated, ListHIR.Complete { zero with ReusedTransforms = 1; Releases = 1 }, ListHIR.Complete { zero with ReusedTransforms = 1; Releases = 1 }, ListHIR.Complete zero))
    "List HIR preserves a source needed after the join", checkBudget branchJoin (ListHIR.Conditional (allocated, ListHIR.Complete { allocated with Copies = 1; Releases = 1 }, ListHIR.Complete zero, ListHIR.Complete { zero with Releases = 1 }))
    "List HIR releases unused inputs on the other edge", checkBudget (bind "xs" (values 3) (choice (fold (AST.Var "xs")) (AST.Int64Literal 7L))) (ListHIR.Conditional (allocated, ListHIR.Complete { zero with Releases = 1 }, ListHIR.Complete { zero with Releases = 1 }, ListHIR.Complete zero))
    "List HIR budgets branch-local constructors separately", checkBudget (bind "xs" (values 3) (choice (fold repeat) (fold (values 3)))) (ListHIR.Conditional ({ allocated with Releases = 1 }, ListHIR.Complete { zero with Allocations = 1; AllocatedBytes = runtimeBytes 0L [ListHIR.ListId 1, 1L]; Releases = 1 }, ListHIR.Complete { allocated with Releases = 1 }, ListHIR.Complete zero))
    "List HIR verifier rejects mismatched branch ownership", rejectsOwnership [construct []; ownedBranch (ownedBlock [root] []) (ownedBlock [] [])]
    "List HIR verifier rejects branch-local leaked values", rejectsOwnership [ownedBranch (ownedBlock [] [construct []]) (ownedBlock [] [])]
    "List HIR verifier rejects duplicate identities across branches", rejectsOwnership [ownedBranch (ownedBlock [] [construct [root]]) (ownedBlock [] [construct [root]])]
    "List HIR verifier rejects double edge cleanup", rejectsOwnership [construct []; ownedBranch (ownedBlock [root; root] []) (ownedBlock [root] [])]
    "List HIR consumes unique map/reverse storage", checkSummary unique { Allocations = 1; AllocatedBytes = bytes 56L; Copies = 0; ReusedTransforms = 2; Releases = 1 }
    "List HIR copies a surviving source version", checkSummary shared { Allocations = 2; AllocatedBytes = bytes 112L; Copies = 1; ReusedTransforms = 0; Releases = 2 }
    "List HIR normalizes aliases before last-use solving", checkSummary (bind "xs" (values 3) (bind "alias" (AST.Var "xs") (fold (reverse (AST.Var "alias"))))) { Allocations = 1; AllocatedBytes = bytes 56L; Copies = 0; ReusedTransforms = 1; Releases = 1 }
    "List HIR releases unused construction", checkSummary (bind "xs" (values 3) (AST.Int64Literal 1L)) { Allocations = 1; AllocatedBytes = bytes 56L; Copies = 0; ReusedTransforms = 0; Releases = 1 }
    "List HIR supports the largest recyclable array", checkSummary (fold (reverse (values 28))) { Allocations = 1; AllocatedBytes = bytes 256L; Copies = 0; ReusedTransforms = 1; Releases = 1 }
    "List HIR budgets runtime construction and consuming transforms", checkSummary (fold (reverse (map repeat))) { Allocations = 1; AllocatedBytes = runtimeBytes 0L [root, 1L]; Copies = 0; ReusedTransforms = 2; Releases = 1 }
    "List HIR budgets runtime copies through aliases", checkSummary (bind "xs" repeat (bind "alias" (AST.Var "xs") (bind "ys" (map (AST.Var "xs")) (bind "old" (fold (AST.Var "alias")) (fold (AST.Var "ys")))))) { Allocations = 2; AllocatedBytes = runtimeBytes 0L [root, 2L]; Copies = 1; ReusedTransforms = 0; Releases = 2 }
    "List HIR keeps independent runtime extents distinct", checkSummary (bind "xs" repeat (bind "xs" repeat (fold (reverse (AST.Var "xs"))))) { Allocations = 2; AllocatedBytes = runtimeBytes 0L [root, 1L; ListHIR.ListId 1, 1L]; Copies = 0; ReusedTransforms = 1; Releases = 2 }
    "List HIR retains runtime origin when copying a consumed transform", checkSummary (bind "ys" (map repeat) (bind "zs" (map (AST.Var "ys")) (bind "old" (fold (AST.Var "ys")) (fold (AST.Var "zs"))))) { Allocations = 2; AllocatedBytes = runtimeBytes 0L [root, 2L]; Copies = 1; ReusedTransforms = 1; Releases = 2 }
    "List HIR preserves native allocation budget", testLoweredBudget
    "List HIR rejects escaping lists", rejects (bind "xs" (values 3) (AST.Var "xs"))
    "List HIR reclaims arrays beyond the fixed heap classes", checkSummary (fold (reverse (values 29))) { Allocations = 1; AllocatedBytes = bytes 272L; Copies = 0; ReusedTransforms = 1; Releases = 1 }
    "List HIR rejects borrowed input lists", rejects (fold (reverse (AST.Var "external")))
    "List HIR rejects managed elements", rejects (bind "xs" (AST.ListLiteral [AST.StringLiteral "a"]) (AST.Int64Literal 0L))
    "List HIR rejects callbacks capturing region lists", rejects (bind "xs" (values 3) (fold (call "Stdlib.List.map_i64_i64" [AST.Var "xs"; AST.Closure ("mapCallback", [AST.Var "xs"])])))
    "List HIR verifier rejects duplicate release", rejectsOwnership [construct [root; root]]
    "List HIR verifier rejects leaked roots", rejectsOwnership [construct []]
    "List HIR verifier rejects reused identities", rejectsOwnership [construct [root]; construct [root]]
    "List HIR verifier rejects mutation after release", rejectsOwnership [construct [root]; { Operation = ListHIR.Transform (ListHIR.ListId 1, root, (ListHIR.Reverse, ListHIR.Consume)); Releases = [ListHIR.ListId 1] }]
]
