// RegionContractTests.fs - Unit ownership laws independent of collection layout and codegen.

module RegionContractTests

open OwnedIR

let private identity = function
    | "a" -> HIR.ValueId 0
    | "b" -> HIR.ValueId 1
    | "c" -> HIR.ValueId 2
    | name -> Crash.crash $"Unsupported ownership fixture identity {name}"
let private value name : HIR.Value = { Id = identity name; Type = AST.TInt64 }
let private unitValue : HIR.Value = { Id = HIR.ValueId 100; Type = AST.TUnit }
let private reference name : HIR.Operand =
    { Expression = AST.Var name; Type = AST.TInt64; Inputs = Map.ofList [name, value name] }
let private condition : HIR.Operand = { Expression = AST.BoolLiteral true; Type = AST.TBool; Inputs = Map.empty }

let private semantics : Semantics<Contract<string>, string> = {
    Leaf = id
    ScalarUses = fun value ->
        value.Inputs |> Map.keys |> Set.ofSeq
    ValueUses = fun value ->
        match value.Id with
        | HIR.ValueId 0 -> Set.singleton "a"
        | HIR.ValueId 1 -> Set.singleton "b"
        | HIR.ValueId 2 -> Set.singleton "c"
        | _ -> Set.empty
}

let private step inputs outputs releases : Step<Contract<string>, string> =
    { Operation = HIR.Leaf { Inputs = inputs; Outputs = outputs }; Releases = releases }
let private block entry operations : Block<Contract<string>, string> =
    { EntryReleases = entry; Body = { Parameters = Map.empty; Operations = operations; Result = unitValue } }
let private branch predicate yes no : Step<Contract<string>, string> =
    { Operation = HIR.Branch ({ Id = HIR.ValueId 101; Type = AST.TUnit }, predicate, yes, no); Releases = [] }
let private read name releases : Step<Contract<string>, string> =
    { Operation = HIR.ScalarBinding ({ Id = HIR.ValueId 102; Type = AST.TInt64 }, reference name); Releases = releases }
let private check expected region () =
    let actual = VerifyOwnership.verifyClosed semantics region
    if actual = expected then Ok () else Error $"Expected {expected}, got {actual}"

let tests = [
    "Ownership contracts borrow before consuming multiple inputs", check (Ok ())
        (block [] [step [] ["a"; "b"] []; step [Borrowed "a"; Consumed "a"; Consumed "b"] ["c"] ["c"]])
    "Ownership contracts reject duplicate consumed units", check (Error (InvalidRelease "a"))
        (block [] [step [] ["a"] []; step [Consumed "a"; Consumed "a"] [] []])
    "Ownership contracts reject duplicate result identities", check (Error (DuplicateDefinition "a"))
        (block [] [step [] ["a"; "a"] []])
    "Ownership contracts reject unknown borrows", check (Error (InvalidUse "a"))
        (block [] [step [Borrowed "a"] [] []])
    "Ownership contracts reject release after consume", check (Error (InvalidRelease "a"))
        (block [] [step [] ["a"] []; step [Consumed "a"] [] ["a"]])
    "Ownership contracts account for scalar operand reads", check (Ok ())
        (block [] [step [] ["a"] []; read "a" ["a"]])
    "Ownership contracts reject scalar use after release", check (Error (InvalidUse "a"))
        (block [] [step [] ["a"] ["a"]; read "a" []])
    "Ownership contracts check branch conditions", check (Error (InvalidUse "a"))
        (block [] [branch (reference "a") (block [] []) (block [] [])])
    "Ownership contracts check block results", check (Error (InvalidUse "a"))
        { EntryReleases = []; Body = { Parameters = Map.empty; Operations = [step [] ["a"] ["a"]]; Result = value "a" } }
    "Ownership contracts allow consumption on exclusive paths", check (Ok ())
        (block [] [step [] ["a"] []; branch condition
            (block [] [step [Consumed "a"] [] []])
            (block [] [step [Consumed "a"] [] []])])
    "Ownership contracts preserve values through nested joins", check (Ok ())
        (block [] [step [] ["a"] []; branch condition
            (block [] [branch condition (block [] [read "a" []]) (block [] [])])
            (block [] [read "a" []]); step [Consumed "a"] [] []])
    "Ownership contracts reject inconsistent joins", check (Error InconsistentJoin)
        (block [] [step [] ["a"] []; branch condition (block ["a"] []) (block [] [])])
    "Ownership contracts enforce global branch identity freshness", check (Error (DuplicateDefinition "a"))
        (block [] [branch condition (block [] [step [] ["a"] ["a"]]) (block [] [step [] ["a"] ["a"]])])
    "Ownership contracts reject double edge release", check (Error (InvalidRelease "a"))
        (block [] [step [] ["a"] []; branch condition (block ["a"; "a"] []) (block ["a"] [])])
    "Ownership contracts reject incoming roots in closed regions", check (Error (InvalidRelease "a"))
        (block ["a"] [])
    "Ownership contracts reject leaked units", check (Error (UnreleasedValues (Set.singleton "a")))
        (block [] [step [] ["a"] []])
]
