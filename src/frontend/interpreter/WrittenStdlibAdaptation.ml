(* WrittenStdlibAdaptation.ml - Interpreter-syntax stdlib calls with the interpreter's meaning. *)
[@@@warning "-4-40"]

(* Under --syntax=current a script is written against the interpreter's
   stdlib, but `Stdlib.String.slice` names the compiler's, which takes an Int
   where the interpreter takes an Int64. Each call to a fn the two disagree on
   converts its arguments and its result as a hosted call does (see
   StdlibConversions), and a renamed fn is called under the compiler's name.
   Pipe stages that need it become calls, since the conversion has to reach
   the piped value. *)

module WT = WrittenTypes
module C = StdlibConversions

let identifier range name : WT.identifier = { range; name }

let fnName range dotted =
  let segments = String.split_on_char '.' dotted in
  let rec split = function
    | [] -> ([], "")
    | [ last ] -> ([], last)
    | first :: rest ->
        let modules, fn = split rest in
        (first :: modules, fn)
  in
  let modules, fn = split segments in
  WT.EFnName
    ( range,
      {
        range;
        modules = List.map (fun name -> (identifier range name, range)) modules;
        fn = identifier range fn;
      } )

let call range dotted args = WT.EApply (range, fnName range dotted, [], args)

(* The interpreter's spelling of a qualified fn: `Stdlib.X.y` is
   `Darklang.Stdlib.X.y`. *)
let interpreterName (name : WT.qualifiedFnIdentifier) =
  let written =
    String.concat "."
      (List.map (fun ((id : WT.identifier), _) -> id.name) name.modules
      @ [ name.fn.name ])
  in
  if String.starts_with ~prefix:"Stdlib." written then "Darklang." ^ written
  else written

let withoutOwner name =
  if String.starts_with ~prefix:"Darklang." name then
    String.sub name 9 (String.length name - 9)
  else name

let rec convert range conversion expression =
  let mapThrough fn inner =
    call range fn
      [
        expression;
        WT.ELambda
          ( range,
            [ WT.LPVariable (range, "adapted'") ],
            convert range inner (WT.EVariable (range, "adapted'")),
            range,
            range );
      ]
  in
  match conversion with
  | C.Widen typ -> call range ("Stdlib.Int.from" ^ typ) [ expression ]
  | C.Narrow typ ->
      call range "Builtin.unwrap" [ call range ("Stdlib.Int.to" ^ typ) [ expression ] ]
  | C.OptionOf inner -> mapThrough "Stdlib.Option.map" inner
  | C.ResultOf inner -> mapThrough "Stdlib.Result.map" inner
  | C.ErrorOf inner -> mapThrough "Stdlib.Result.mapError" inner
  | C.Both (first, second) -> convert range second (convert range first expression)
  | C.ListOf inner -> mapThrough "Stdlib.List.map" inner
  | C.TupleOf parts -> (
      let names = List.mapi (fun index _ -> "part" ^ string_of_int index ^ "'") parts in
      let variable name = WT.EVariable (range, name) in
      let pattern name = WT.LPVariable (range, name) in
      let converted =
        List.map2
          (fun name part ->
            match part with
            | Some inner -> convert range inner (variable name)
            | None -> variable name)
          names parts
      in
      match (names, converted) with
      | first :: second :: restNames, firstValue :: secondValue :: restValues ->
          WT.ELet
            ( range,
              WT.LPTuple
                ( range,
                  pattern first,
                  range,
                  pattern second,
                  List.map (fun name -> (range, pattern name)) restNames,
                  range,
                  range ),
              expression,
              WT.ETuple
                ( range,
                  firstValue,
                  range,
                  secondValue,
                  List.map (fun value -> (range, value)) restValues,
                  range,
                  range ),
              range,
              range )
      | _ -> expression)

(* A call to <name> with <args>, converted both ways. *)
let adaptedCall range name typeArgs args =
  let conversions = C.argumentConversions name in
  let args =
    List.mapi
      (fun index arg ->
        match List.assoc_opt index conversions with
        | Some conversion -> convert range conversion arg
        | None -> arg)
      args
  in
  let applied = WT.EApply (range, fnName range (withoutOwner name), typeArgs, args) in
  match C.resultConversion name with
  | Some conversion -> convert range conversion applied
  | None -> applied

let adapts name =
  C.argumentConversions name <> []
  || Option.is_some (C.resultConversion name)

let rec expr (e : WT.expr) : WT.expr =
  match e with
  | WT.EApply (range, WT.EFnName (_, name), typeArgs, args)
    when adapts (interpreterName name) ->
      adaptedCall range (interpreterName name) typeArgs (List.map expr args)
  | WT.EUnit _ | WT.EBool _ | WT.EInt _ | WT.EInt64 _ | WT.EInt8 _ | WT.EUInt8 _
  | WT.EInt16 _ | WT.EUInt16 _ | WT.EInt32 _ | WT.EUInt32 _ | WT.EUInt64 _
  | WT.EInt128 _ | WT.EUInt128 _ | WT.EFloat _ | WT.EChar _ | WT.EVariable _
  | WT.EFnName _ | WT.EError _ ->
      e
  | WT.EString (range, prefix, segments, opening, closing) ->
      WT.EString
        ( range,
          prefix,
          List.map
            (function
              | WT.StringText _ as text -> text
              | WT.StringInterpolation (r, inner, a, b) ->
                  WT.StringInterpolation (r, expr inner, a, b))
            segments,
          opening,
          closing )
  | WT.EInfix (range, op, left, right) -> WT.EInfix (range, op, expr left, expr right)
  | WT.ELet (range, pattern, value, body, a, b) ->
      WT.ELet (range, pattern, expr value, expr body, a, b)
  | WT.EApply (range, fn, typeArgs, args) ->
      WT.EApply (range, expr fn, typeArgs, List.map expr args)
  | WT.EList (range, items, a, b) ->
      WT.EList (range, List.map (fun (item, sep) -> (expr item, sep)) items, a, b)
  | WT.ETuple (range, first, a, second, rest, b, c) ->
      WT.ETuple
        (range, expr first, a, expr second, List.map (fun (r, v) -> (r, expr v)) rest, b, c)
  | WT.EIf (range, cond, yes, no, a, b, c) ->
      WT.EIf (range, expr cond, expr yes, Option.map expr no, a, b, c)
  | WT.EUnwrap (range, inner, a) -> WT.EUnwrap (range, expr inner, a)
  | WT.ERecordFieldAccess (range, record, field, a) ->
      WT.ERecordFieldAccess (range, expr record, field, a)
  | WT.ELambda (range, patterns, body, a, b) -> WT.ELambda (range, patterns, expr body, a, b)
  | WT.ERecord (range, name, fields, a, b) ->
      WT.ERecord (range, name, List.map (fun (r, f, v) -> (r, f, expr v)) fields, a, b)
  | WT.EDict (range, entries, a, b, c) ->
      WT.EDict (range, List.map (fun (r, k, s, v) -> (r, expr k, s, expr v)) entries, a, b, c)
  | WT.ERecordUpdate (range, record, updates, a, b, c) ->
      WT.ERecordUpdate
        (range, expr record, List.map (fun (f, s, v) -> (f, s, expr v)) updates, a, b, c)
  | WT.EEnum (range, name, case, fields, dot) ->
      WT.EEnum (range, name, case, List.map expr fields, dot)
  | WT.EMatch (range, scrutinee, cases, a, b) ->
      WT.EMatch
        ( range,
          expr scrutinee,
          List.map
            (fun (case : WT.matchCase) ->
              {
                case with
                whenCondition = Option.map (fun (r, c) -> (r, expr c)) case.whenCondition;
                rhs = expr case.rhs;
              })
            cases,
          a,
          b )
  | WT.EStatement (range, first, second) -> WT.EStatement (range, expr first, expr second)
  | WT.EPipe (range, initial, stages) -> pipe range (expr initial) stages

(* A stage calling a fn that needs conversion is the call with the value so far
   as argument 0; the stages after it pipe from that call. *)
and pipe range piped stages =
  match stages with
  | [] -> piped
  | (_, WT.EPipeFnCall (callRange, name, typeArgs, args)) :: rest
    when adapts (interpreterName name) ->
      pipe range
        (adaptedCall callRange (interpreterName name) typeArgs (piped :: List.map expr args))
        rest
  | (stageRange, stage) :: rest ->
      let stage =
        match stage with
        | WT.EPipeInfix (r, op, value) -> WT.EPipeInfix (r, op, expr value)
        | WT.EPipeLambda (r, patterns, body, a, b) ->
            WT.EPipeLambda (r, patterns, expr body, a, b)
        | WT.EPipeEnum (r, name, case, fields, dot) ->
            WT.EPipeEnum (r, name, case, List.map expr fields, dot)
        | WT.EPipeFnCall (r, name, typeArgs, args) ->
            WT.EPipeFnCall (r, name, typeArgs, List.map expr args)
        | WT.EPipeVariableOrFnCall _ as stage -> stage
      in
      pipe range (WT.EPipe (range, piped, [ (stageRange, stage) ])) rest

let rec declaration (d : WT.declaration) : WT.declaration =
  match d with
  | WT.DFunction fn -> WT.DFunction { fn with body = expr fn.body }
  | WT.DValue value -> WT.DValue { value with body = expr value.body }
  | WT.DModule m -> WT.DModule { m with declarations = List.map declaration m.declarations }
  | WT.DExpr e -> WT.DExpr (expr e)
  | WT.DAssertion a ->
      WT.DAssertion
        {
          a with
          actual = expr a.actual;
          expected =
            (match a.expected with
            | WT.TEExpr e -> WT.TEExpr (expr e)
            | WT.TEError _ as error -> error);
        }
  | WT.DTest t -> WT.DTest { t with body = expr t.body }
  | WT.DType _ | WT.DTrait _ | WT.DImpl _ -> d

let sourceFile (file : WT.sourceFile) : WT.sourceFile =
  {
    file with
    declarations = List.map declaration file.declarations;
    exprsToEval = List.map expr file.exprsToEval;
  }
