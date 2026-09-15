// HIRVerificationTests.fs - Normalized value identity and structured-edge verifier laws.

module HIRVerificationTests

type private TestBlock = TestBlock of HIR.Block<HIR.Operation<HIR.ValueContract, TestBlock>>

let private value id typ : HIR.Value = { Id = HIR.ValueId id; Type = typ }
let private literal typ expression : HIR.Operand =
    { Expression = expression; Type = typ; Inputs = Map.empty }
let private reference name input typ : HIR.Operand =
    { Expression = AST.Var name; Type = typ; Inputs = Map.ofList [name, input] }
let private block parameters operations result =
    TestBlock { Parameters = parameters; Operations = operations; Result = result }
let private leaf inputs operands outputs =
    let contract: HIR.ValueContract = { Inputs = inputs; Operands = operands; Outputs = outputs }
    HIR.Leaf contract
let private verify root =
    let dialect: VerifyHIR.Dialect<HIR.ValueContract, TestBlock> = {
        Body = fun (TestBlock body) -> body
        Leaf = id
    }
    VerifyHIR.verify dialect root
let private check expected root () =
    let actual = verify root
    if actual = expected then Ok () else Error $"Expected {expected}, got {actual}"

let tests = [
    let parameter = value 0 AST.TInt64
    let result = value 1 AST.TInt64
    let condition = literal AST.TBool (AST.BoolLiteral true)
    let branchResult = value 2 AST.TInt64
    let branchLocal = value 3 AST.TInt64

    "HIR accepts normalized parameter and operand identities", check (Ok ())
        (block (Map.ofList ["input", parameter])
            [HIR.ScalarBinding (result, reference "input" parameter AST.TInt64)] result)
    "HIR rejects an operand with an unknown identity", check (Error (VerifyHIR.UnknownValue parameter.Id))
        (block Map.empty [HIR.ScalarBinding (result, reference "input" parameter AST.TInt64)] result)
    "HIR rejects sibling definitions with the same identity", check (Error (VerifyHIR.DuplicateDefinition branchLocal.Id))
        (block Map.empty
            [HIR.Branch (branchResult, condition,
                block Map.empty [leaf [] [] [branchLocal]] branchLocal,
                block Map.empty [leaf [] [] [branchLocal]] branchLocal)]
            branchResult)
    "HIR rejects a branch result whose type disagrees with its target", check (Error (VerifyHIR.InconsistentBranchResult branchResult.Id))
        (block Map.empty
            [HIR.Branch (branchResult, condition,
                block Map.empty [leaf [] [] [branchLocal]] branchLocal,
                let boolean = value 4 AST.TBool
                block Map.empty [leaf [] [] [boolean]] boolean)]
            branchResult)
]
