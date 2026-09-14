// Accumulators.fs - Lower eligible recursive scalar computations through accumulators.

module ANFAccumulatorOptimization

open ANF
open ANFExpressionOptimization

type private SiblingAddition = {
    FirstCallId: TempId
    FirstArgs: Atom list
    SecondCallId: TempId
    SecondArgs: Atom list
    ResultId: TempId
}

/// A direct recursive call whose Int64 result is immediately multiplied by an
/// Int64 parameter or literal. The factor restriction makes moving the wrapped
/// multiply ahead of the call observably safe without effect analysis.
type private WrappedMultiplication = {
    CallId: TempId
    CallArgs: Atom list
    Factor: Atom
    ResultId: TempId
}

let private tryLinearBindings (expr: AExpr) : ((TempId * CExpr) list * Atom) option =
    let rec collect reversedBindings remaining =
        match remaining with
        | Let (tempId, cexpr, body) ->
            collect ((tempId, cexpr) :: reversedBindings) body
        | Return atom ->
            Some (List.rev reversedBindings, atom)
        | Jump _ | Join _ | If _ ->
            None
    collect [] expr

/// Recognize a complete linear sibling-recursion arm. Requiring exactly two
/// self calls and a final addition keeps effect order and the rewrite boundary
/// explicit; the function-level gate rejects any recursion outside this shape.
let private trySiblingAddition (funcName: string) (expr: AExpr) : SiblingAddition option =
    match tryLinearBindings expr with
    | Some (bindings, Var returnedId) ->
        match List.rev bindings with
        | (resultId, Prim (Add, Var leftId, Var rightId)) :: _ when resultId = returnedId ->
            let selfCalls =
                bindings
                |> List.choose (fun (tempId, cexpr) ->
                    match cexpr with
                    | Call (target, args) when target = funcName -> Some (tempId, args)
                    | _ -> None)
            match selfCalls with
            | [(firstCallId, firstArgs); (secondCallId, secondArgs)]
                when (leftId = firstCallId && rightId = secondCallId)
                     || (leftId = secondCallId && rightId = firstCallId) ->
                Some {
                    FirstCallId = firstCallId
                    FirstArgs = firstArgs
                    SecondCallId = secondCallId
                    SecondArgs = secondArgs
                    ResultId = resultId
                }
            | _ -> None
        | _ -> None
    | _ -> None

let private isInt64ParameterOrLiteral (int64Params: Set<TempId>) (atom: Atom) : bool =
    match atom with
    | IntLiteral (Int64 _) -> true
    | Var tempId -> Set.contains tempId int64Params
    | _ -> false

/// Recognize one direct self call wrapped by a final Int64 multiplication.
/// Every preceding binding must be pure and first-order, which rejects managed
/// allocations, effects, indirect calls, and unmodelled control-flow values.
let private tryWrappedMultiplication
    (funcName: string)
    (int64Params: Set<TempId>)
    (expr: AExpr)
    : WrappedMultiplication option =
    match tryLinearBindings expr with
    | Some (bindings, Var returnedId) ->
        let isAllowedBinding binding =
            match binding with
            | _, Atom _
            | _, TypedAtom _
            | _, Prim _
            | _, UnaryPrim _ -> true
            | _, Call (target, _) when target = funcName -> true
            | _ -> false
        match List.rev bindings with
        | (resultId, Prim (Mul, left, right)) :: _ when resultId = returnedId ->
            let selfCalls =
                bindings
                |> List.choose (fun (tempId, cexpr) ->
                    match cexpr with
                    | Call (target, args) when target = funcName -> Some (tempId, args)
                    | _ -> None)
            let noOtherCalls = bindings |> List.forall isAllowedBinding
            match selfCalls with
            | [(callId, callArgs)] when noOtherCalls ->
                match left, right with
                | Var resultCallId, factor when resultCallId = callId && isInt64ParameterOrLiteral int64Params factor ->
                    Some { CallId = callId; CallArgs = callArgs; Factor = factor; ResultId = resultId }
                | factor, Var resultCallId when resultCallId = callId && isInt64ParameterOrLiteral int64Params factor ->
                    Some { CallId = callId; CallArgs = callArgs; Factor = factor; ResultId = resultId }
                | _ -> None
            | _ -> None
        | _ -> None
    | _ -> None

let private selfCallCount (funcName: string) (expr: AExpr) : int =
    let rec count expr =
        match expr with
        | Jump _ | Return _ -> 0
        | Let (_, cexpr, body) ->
            let current =
                match cexpr with
                | Call (target, _) when target = funcName -> 1
                | _ -> 0
            current + count body
        | Join (_, continuation, entry) -> count continuation + count entry
        | If (_, thenBranch, elseBranch) ->
            count thenBranch + count elseBranch
    count expr

let private siblingAdditionCount (funcName: string) (expr: AExpr) : int =
    let rec count expr =
        match trySiblingAddition funcName expr with
        | Some _ -> 1
        | None ->
            match expr with
            | Jump _ | Return _ -> 0
            | Let (_, _, body) -> count body
            | Join (_, continuation, entry) -> count continuation + count entry
            | If (_, thenBranch, elseBranch) -> count thenBranch + count elseBranch
    count expr

let private wrappedMultiplicationCount
    (funcName: string)
    (int64Params: Set<TempId>)
    (expr: AExpr)
    : int =
    let rec count current =
        match tryWrappedMultiplication funcName int64Params current with
        | Some _ -> 1
        | None ->
            match current with
            | Jump _ | Return _ -> 0
            | Let (_, _, body) -> count body
            | Join (_, continuation, entry) -> count continuation + count entry
            | If (_, thenBranch, elseBranch) -> count thenBranch + count elseBranch
    count expr

let private int64Zero = IntLiteral (Int64 0L)

let private rebuildBindings (bindings: (TempId * CExpr) list) (body: AExpr) : AExpr =
    List.foldBack (fun (tempId, cexpr) acc -> Let (tempId, cexpr, acc)) bindings body

let private transformSiblingAddition
    (helperName: string)
    (accumulatorId: TempId)
    (varGen: VarGen)
    (sibling: SiblingAddition)
    (bindings: (TempId * CExpr) list)
    : AExpr * VarGen =
    let (nextAccumulatorId, varGen') = freshVar varGen
    let rec rewrite remaining =
        match remaining with
        | [] -> Return (Var sibling.SecondCallId)
        | (tempId, _) :: rest when tempId = sibling.ResultId ->
            rewrite rest
        | (tempId, Call (_, _)) :: rest when tempId = sibling.FirstCallId ->
            Let (
                tempId,
                Call (helperName, sibling.FirstArgs @ [int64Zero]),
                rewrite rest
            )
        | (tempId, Call (_, _)) :: rest when tempId = sibling.SecondCallId ->
            Let (
                nextAccumulatorId,
                Prim (Add, Var accumulatorId, Var sibling.FirstCallId),
                Let (
                    tempId,
                    Call (helperName, sibling.SecondArgs @ [Var nextAccumulatorId]),
                    rewrite rest
                )
            )
        | binding :: rest ->
            rebuildBindings [binding] (rewrite rest)
    (rewrite bindings, varGen')

let rec private transformAccumulatorBody
    (funcName: string)
    (helperName: string)
    (accumulatorId: TempId)
    (varGen: VarGen)
    (expr: AExpr)
    : AExpr * VarGen =
    match trySiblingAddition funcName expr, tryLinearBindings expr with
    | Some sibling, Some (bindings, _) ->
        transformSiblingAddition helperName accumulatorId varGen sibling bindings
    | _ ->
        match expr with
        | Jump _ -> (expr, varGen)
        | Join (parameter, continuation, entry) ->
            let body, next = transformAccumulatorBody funcName helperName accumulatorId varGen continuation
            let entry', final = transformAccumulatorBody funcName helperName accumulatorId next entry
            (Join (parameter, body, entry'), final)
        | Return atom ->
            let (resultId, varGen') = freshVar varGen
            (Let (resultId, Prim (Add, Var accumulatorId, atom), Return (Var resultId)), varGen')
        | Let (tempId, cexpr, body) ->
            let (body', varGen') =
                transformAccumulatorBody funcName helperName accumulatorId varGen body
            (Let (tempId, cexpr, body'), varGen')
        | If (cond, thenBranch, elseBranch) ->
            let (thenBranch', varGenAfterThen) =
                transformAccumulatorBody funcName helperName accumulatorId varGen thenBranch
            let (elseBranch', varGenAfterElse) =
                transformAccumulatorBody funcName helperName accumulatorId varGenAfterThen elseBranch
            (If (cond, thenBranch', elseBranch'), varGenAfterElse)

let private transformWrappedMultiplication
    (helperName: string)
    (accumulatorId: TempId)
    (varGen: VarGen)
    (wrapped: WrappedMultiplication)
    (bindings: (TempId * CExpr) list)
    : AExpr * VarGen =
    let (nextAccumulatorId, varGen') = freshVar varGen
    let rec rewrite remaining =
        match remaining with
        | [] -> Return (Var wrapped.CallId)
        | (tempId, _) :: rest when tempId = wrapped.ResultId -> rewrite rest
        | (tempId, Call (_, _)) :: rest when tempId = wrapped.CallId ->
            Let (
                nextAccumulatorId,
                Prim (Mul, Var accumulatorId, wrapped.Factor),
                Let (tempId, Call (helperName, wrapped.CallArgs @ [Var nextAccumulatorId]), rewrite rest)
            )
        | binding :: rest -> rebuildBindings [binding] (rewrite rest)
    (rewrite bindings, varGen')

let rec private transformMultiplicationAccumulatorBody
    (funcName: string)
    (int64Params: Set<TempId>)
    (helperName: string)
    (accumulatorId: TempId)
    (varGen: VarGen)
    (expr: AExpr)
    : AExpr * VarGen =
    match tryWrappedMultiplication funcName int64Params expr, tryLinearBindings expr with
    | Some wrapped, Some (bindings, _) ->
        transformWrappedMultiplication helperName accumulatorId varGen wrapped bindings
    | _ ->
        match expr with
        | Jump _ -> (expr, varGen)
        | Join (parameter, continuation, entry) ->
            let body, next = transformMultiplicationAccumulatorBody funcName int64Params helperName accumulatorId varGen continuation
            let entry', final = transformMultiplicationAccumulatorBody funcName int64Params helperName accumulatorId next entry
            (Join (parameter, body, entry'), final)
        | Return atom ->
            let (resultId, varGen') = freshVar varGen
            (Let (resultId, Prim (Mul, Var accumulatorId, atom), Return (Var resultId)), varGen')
        | Let (tempId, cexpr, body) ->
            let (body', varGen') =
                transformMultiplicationAccumulatorBody funcName int64Params helperName accumulatorId varGen body
            (Let (tempId, cexpr, body'), varGen')
        | If (cond, thenBranch, elseBranch) ->
            let (thenBranch', varGenAfterThen) =
                transformMultiplicationAccumulatorBody funcName int64Params helperName accumulatorId varGen thenBranch
            let (elseBranch', varGenAfterElse) =
                transformMultiplicationAccumulatorBody funcName int64Params helperName accumulatorId varGenAfterThen elseBranch
            (If (cond, thenBranch', elseBranch'), varGenAfterElse)

let private freshHelperName (usedNames: Set<string>) (funcName: string) : string =
    let rec choose suffix =
        let candidate =
            if suffix = 0 then $"{funcName}$trmo"
            else $"{funcName}$trmo{suffix}"
        if Set.contains candidate usedNames then choose (suffix + 1) else candidate
    choose 0

let internal transformTailRecursionModuloAddition (program: Program) : Program =
    let (Program (functions, mainExpr)) = program
    let initialNames = functions |> List.map (fun func -> func.Name) |> Set.ofList
    let initialVarGen = freshVarGenForProgram program
    let (functionsReversed, _, _) =
        functions
        |> List.fold
            (fun (rewritten, usedNames, varGen) func ->
                let pairs = siblingAdditionCount func.Name func.Body
                let recursiveCalls = selfCallCount func.Name func.Body
                let eligible =
                    func.ReturnType = AST.TInt64
                    && pairs > 0
                    && recursiveCalls = pairs * 2
                if not eligible then
                    (func :: rewritten, Set.add func.Name usedNames, varGen)
                else
                    let helperName = freshHelperName usedNames func.Name
                    let (accumulatorId, varGenAfterAccumulator) = freshVar varGen
                    let (helperBody, varGenAfterHelper) =
                        transformAccumulatorBody
                            func.Name
                            helperName
                            accumulatorId
                            varGenAfterAccumulator
                            func.Body
                    let (wrapperResultId, varGenAfterWrapper) = freshVar varGenAfterHelper
                    let helper = {
                        func with
                            Name = helperName
                            TypedParams =
                                func.TypedParams @ [{ Id = accumulatorId; Type = AST.TInt64 }]
                            Body = helperBody
                    }
                    let wrapper = {
                        func with
                            Body =
                                Let (
                                    wrapperResultId,
                                    Call (
                                        helperName,
                                        (func.TypedParams |> List.map (fun param -> Var param.Id))
                                        @ [int64Zero]
                                    ),
                                    Return (Var wrapperResultId)
                                )
                    }
                    (helper :: wrapper :: rewritten, Set.add helperName usedNames, varGenAfterWrapper))
            ([], initialNames, initialVarGen)
    Program (List.rev functionsReversed, mainExpr)

/// Turn a direct recursive Int64 multiplication with a pure parameter/literal
/// factor into an accumulator helper. Wrapping signed multiplication is
/// associative modulo 2^64, so the helper preserves overflow behavior.
let internal transformTailRecursionModuloMultiplication (program: Program) : Program =
    let (Program (functions, mainExpr)) = program
    let initialNames = functions |> List.map (fun func -> func.Name) |> Set.ofList
    let initialVarGen = freshVarGenForProgram program
    let (functionsReversed, _, _) =
        functions
        |> List.fold
            (fun (rewritten, usedNames, varGen) func ->
                let int64Params =
                    func.TypedParams
                    |> List.choose (fun param -> if param.Type = AST.TInt64 then Some param.Id else None)
                    |> Set.ofList
                let wrappedCalls = wrappedMultiplicationCount func.Name int64Params func.Body
                let recursiveCalls = selfCallCount func.Name func.Body
                let eligible =
                    func.ReturnType = AST.TInt64
                    && wrappedCalls > 0
                    && recursiveCalls = wrappedCalls
                if not eligible then
                    (func :: rewritten, Set.add func.Name usedNames, varGen)
                else
                    let helperName = freshHelperName usedNames func.Name
                    let (accumulatorId, varGenAfterAccumulator) = freshVar varGen
                    let (helperBody, varGenAfterHelper) =
                        transformMultiplicationAccumulatorBody
                            func.Name int64Params helperName accumulatorId varGenAfterAccumulator func.Body
                    let (wrapperResultId, varGenAfterWrapper) = freshVar varGenAfterHelper
                    let helper = {
                        func with
                            Name = helperName
                            TypedParams = func.TypedParams @ [{ Id = accumulatorId; Type = AST.TInt64 }]
                            Body = helperBody
                    }
                    let wrapper = {
                        func with
                            Body =
                                Let (
                                    wrapperResultId,
                                    Call (helperName, (func.TypedParams |> List.map (fun param -> Var param.Id)) @ [IntLiteral (Int64 1L)]),
                                    Return (Var wrapperResultId)
                                )
                    }
                    (helper :: wrapper :: rewritten, Set.add helperName usedNames, varGenAfterWrapper))
            ([], initialNames, initialVarGen)
    Program (List.rev functionsReversed, mainExpr)
