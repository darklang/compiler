// VerifyHIR.fs - Verify normalized HIR value identities and structured control-flow edges.

module VerifyHIR

open HIR

type VerificationError =
    | UnknownValue of ValueId
    | DuplicateDefinition of ValueId
    | InconsistentValueType of ValueId
    | BindingTypeMismatch of result: ValueId
    | InvalidBranchCondition of AST.Type
    | InconsistentBranchResult of result: ValueId

type Dialect<'leaf, 'block> = {
    Body: 'block -> Block<Operation<'leaf, 'block>>
    Leaf: 'leaf -> ValueContract
}

let verify (dialect: Dialect<'leaf, 'block>) (root: 'block) =
    let require (visible: Map<ValueId, AST.Type>) (value: Value) =
        match Map.tryFind value.Id visible with
        | None -> Error (UnknownValue value.Id)
        | Some typ when typ = value.Type -> Ok ()
        | Some _ -> Error (InconsistentValueType value.Id)
    let requireMany visible (values: Value list) =
        values
        |> List.fold (fun result value -> result |> Result.bind (fun () -> require visible value)) (Ok ())
    let operand visible (value: Operand) = value.Inputs |> Map.values |> Seq.toList |> requireMany visible
    let define declared visible (value: Value) =
        if Map.containsKey value.Id declared then Error (DuplicateDefinition value.Id)
        else Ok (Map.add value.Id value.Type declared, Map.add value.Id value.Type visible)
    let defineMany declared visible (values: Value list) =
        values
        |> List.fold (fun result value -> result |> Result.bind (fun (declared, visible) -> define declared visible value)) (Ok (declared, visible))
    let rec operations declared visible = function
        | [] -> Ok (declared, visible)
        | operation :: rest ->
            let next =
                match operation with
                | Leaf leaf ->
                    let contract: ValueContract = dialect.Leaf leaf
                    requireMany visible contract.Inputs |> Result.bind (fun () ->
                        contract.Operands
                        |> List.fold (fun result value -> result |> Result.bind (fun () -> operand visible value)) (Ok ())
                        |> Result.bind (fun () -> defineMany declared visible contract.Outputs))
                | ScalarBinding (result, value) ->
                    if result.Type <> value.Type then Error (BindingTypeMismatch result.Id)
                    else operand visible value |> Result.bind (fun () -> define declared visible result)
                | Branch (result, condition, ifTrue, ifFalse) ->
                    if condition.Type <> AST.TBool then Error (InvalidBranchCondition condition.Type)
                    else
                        operand visible condition |> Result.bind (fun () ->
                            block declared visible ifTrue |> Result.bind (fun (afterTrue, (trueResult: Value)) ->
                                block afterTrue visible ifFalse |> Result.bind (fun (afterFalse, (falseResult: Value)) ->
                                    if trueResult.Type <> result.Type || falseResult.Type <> result.Type then
                                        Error (InconsistentBranchResult result.Id)
                                    else define afterFalse visible result)))
            next |> Result.bind (fun (declared, visible) -> operations declared visible rest)
    and block declared visible block =
        let body: Block<Operation<'leaf, 'block>> = dialect.Body block
        defineMany declared visible (body.Parameters |> Map.values |> Seq.toList)
        |> Result.bind (fun (declared, visible) ->
            operations declared visible body.Operations |> Result.bind (fun (declared, visible) ->
                require visible body.Result |> Result.map (fun () -> declared, body.Result)))
    block Map.empty Map.empty root |> Result.map ignore
