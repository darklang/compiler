// SemanticIR.fs - Typed block results and representation-independent value liveness.
// Operands retain checked source semantics until their consumer lowers them;
// this layer makes no purity claim about an opaque expression or callback.

module SemanticIR

type Operand = { Expression: AST.Expr; Type: AST.Type }

type Block<'operation> = {
    Operations: 'operation list
    Result: Operand
}

/// Explicit value edges describe reachability, not physical ownership. A later
/// storage-specific pass decides whether a last use can transfer an allocation.
type ValueContract<'id when 'id: comparison> = {
    Uses: Set<'id>
    Defines: Set<'id>
}

let liveBefore contract liveAfter =
    Set.union contract.Uses (Set.difference liveAfter contract.Defines)

/// Structural proof that releasing a value cannot invoke user code. This is
/// not an effect/purity claim about evaluating it. Nominal payloads and opaque
/// closures require layout/capture evidence unavailable from the type alone.
let rec hasInertDestruction = function
    | AST.TInt8 | AST.TInt16 | AST.TInt32 | AST.TInt64
    | AST.TUInt8 | AST.TUInt16 | AST.TUInt32 | AST.TUInt64
    | AST.TInt | AST.TInt128 | AST.TUInt128
    | AST.TBool | AST.TFloat64 | AST.TString | AST.TChar
    | AST.TBlob | AST.TUnit | AST.TDateTime | AST.TRawPtr | AST.TRuntimeError -> true
    | AST.TList element -> hasInertDestruction element
    | AST.TTuple elements -> List.forall hasInertDestruction elements
    | AST.TDict (key, value) -> hasInertDestruction key && hasInertDestruction value
    | _ -> false

type ScopeDestruction = InertScope | UnprovenScope

type FunctionScopeContract = {
    LocalDestruction: ScopeDestruction
    Calls: Set<string>
}

/// These compiler primitives can perform effects, but neither owns a value
/// whose destruction invokes user code. Unknown external calls remain unproven.
let private inertPrimitives = Set.ofList ["Builtin.printLine"; "Builtin.print"]

/// Reject callers transitively from locally unproven scopes and unavailable
/// callees. Safe recursive components are accepted without unfolding paths.
let inertFunctionScopes (contracts: Map<string, FunctionScopeContract>) =
    let names = contracts |> Map.keys |> Set.ofSeq
    let primitives = Set.difference inertPrimitives names
    let unavailable calls = not (Set.isSubset calls (Set.union names primitives))
    let unproven =
        contracts |> Map.toSeq |> Seq.choose (fun (name, contract) ->
            match contract.LocalDestruction with
            | UnprovenScope -> Some name
            | InertScope when unavailable contract.Calls -> Some name
            | InertScope -> None) |> Set.ofSeq
    let callers =
        contracts |> Map.fold (fun callers name contract ->
            contract.Calls |> Set.fold (fun callers target ->
                Map.change target (fun previous -> Some (Set.add name (Option.defaultValue Set.empty previous))) callers) callers) Map.empty
    let rejected = CallGraphReachability.findReachable callers unproven
    Set.union primitives (Set.difference names rejected)
