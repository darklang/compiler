(* ValueRendering.ml - Interpreter-compatible result rendering.
   Builds monomorphic Dark functions which render values at the eval boundary.
   Keeping recursion in ordinary Dark code gives tuples, lists, records, and sums
   one renderer on every native target instead of backend-specific shape switches. *)
[@@@warning "-4"]

open AST
module M = StringOrder.Map
module C = CheckedAST
module N = AST.NonEmptyList

(* Declaration metadata stays lazy at primitive rendering boundaries. *)
type renderEnv = {
  records : Types.recordTypeInfo M.t Lazy.t;
  sums : Types.sumTypeInfo M.t Lazy.t;
  sharedShapes : StringOrder.Set.t Lazy.t;
}

type renderState = { functions : C.functionDef M.t; symbols : C.symbols }

let freshBinding name state =
  let id, symbols = C.allocateBinding name state.symbols in
  (id, { state with symbols })

let constructorPattern typeName (variant : Types.sumVariantInfo) symbols fields
    =
  match C.tryFindConstructorId typeName variant.Types.name symbols with
  | Some id -> C.PConstructor (id, fields)
  | None ->
      Crash.crash
        ("Value renderer constructor was not interned: " ^ typeName ^ "."
       ^ variant.Types.name)

let args values = N.fromList values

let resolveFunction symbols name =
  match C.tryFindFunctionId name symbols with
  | Some id -> id
  | None -> Crash.crash ("Value renderer function was not interned: " ^ name)

let call symbols name values = C.Call (resolveFunction symbols name, args values)

(* `--syntax=current`: render as the current interpreter's `dark eval` prints.
   An Option or Result case is bare (`Some(41)`); a Stdlib type drops the
   `Darklang` owner (`Stdlib.Math.Overflow.Overflow`) and other package types
   keep it; a generic type shows its arguments (`Scope<Int64>.Only(443)`);
   integers other than Int64 keep their suffix (`5u8`, `1z`); DateTimes print
   as `<DateTime: ...>`; records keep declaration order; a top-level String
   prints raw and a top-level Unit prints nothing. *)
let evalDisplay = ref false

(* The interpreter's PrettyPrinter.shortenName from the root module: only the
   `Darklang` owner in front of `Stdlib` comes off, and the parser's
   scaffolding owners (`Tests`, `CliScript`) are not printed. *)
let evalTypeName name =
  match String.split_on_char '.' name with
  | "Darklang" :: ("Stdlib" :: _ as rest) -> String.concat "." rest
  | ("Tests" | "CliScript" | "") :: (_ :: _ as rest) -> String.concat "." rest
  | _ -> name

(* A type argument as the interpreter's valueType prints it: `_` where the
   type is not known, `(A * B)` for a tuple, and custom types by full name. *)
let rec evalTypeArgText typ =
  match typ with
  | TVar _ | TInferenceVar _ -> "_"
  | TInt -> "BigInt"
  | TBlob -> "Bytes"
  | TTuple elems -> "(" ^ String.concat " * " (List.map evalTypeArgText elems) ^ ")"
  | TList elem -> "List<" ^ evalTypeArgText elem ^ ">"
  | TStream elem -> "Stream<" ^ evalTypeArgText elem ^ ">"
  | TDict (key, value) ->
      "Dict<" ^ evalTypeArgText key ^ ", " ^ evalTypeArgText value ^ ">"
  | TFunction (params, result) ->
      let grouped text =
        if Text.contains text " -> " then "(" ^ text ^ ")" else text
      in
      let result = grouped (evalTypeArgText result) in
      (match params with
      | [ only ] -> grouped (evalTypeArgText only)
      | params -> "(" ^ String.concat ", " (List.map evalTypeArgText params) ^ ")")
      ^ " -> " ^ result
  | TRecord (name, []) | TSum (name, []) -> name
  | TRecord (name, args) | TSum (name, args) ->
      name ^ "<" ^ String.concat ", " (List.map evalTypeArgText args) ^ ">"
  | _ -> CheckingDiagnostics.typeToString typ

let rec typeVariables = function
  | TVar name | TInferenceVar (_, name) -> [ name ]
  | TList elem | TStream elem -> typeVariables elem
  | TDict (key, value) -> typeVariables key @ typeVariables value
  | TFunction (params, result) ->
      List.concat_map typeVariables params @ typeVariables result
  | TTuple elems -> List.concat_map typeVariables elems
  | TRecord (_, args) | TSum (_, args) -> List.concat_map typeVariables args
  | _ -> []

(* The interpreter knows a value's type arguments from what it holds, so a
   parameter that none of the shown fields mention prints as `_`:
   `MyOpt<_>.Nothing`, `MyOpt<Int64>.Just(1)`. *)
let evalTypeArgs typeParams typeArgs fieldTypes =
  if typeArgs = [] || List.length typeParams <> List.length typeArgs then ""
  else
    let used = List.concat_map typeVariables fieldTypes in
    "<"
    ^ String.concat ", "
        (List.map2
           (fun param arg ->
             if List.mem param used then evalTypeArgText arg else "_")
           typeParams typeArgs)
    ^ ">"

let displayTypeText typ =
  let text = CheckingDiagnostics.typeToString typ in
  if not !evalDisplay then text
  else
    match typ with
    | TRecord (name, _) | TSum (name, _) -> (
        match name with
        | "Darklang.Stdlib.Option.Option" | "Darklang.Stdlib.Result.Result" -> ""
        | _ -> evalTypeName name)
    | _ -> text

(* The interpreter's package types are content-addressed: declarations with
   the same type parameters and shape are one type under several names, and
   `dark eval` prints such an enum's case without a type name, since any one
   name would be a guess (`Error(BadFormat)` for every integer ParseError).
   Group the known declarations by shape, comparing referenced types by
   group, refining until the grouping is stable, and return the names that
   share a group. *)
let sharedShapeNames records sums =
  let rec shapeType groupOf = function
    | TRecord (name, args) | TSum (name, args) ->
        TSum (groupOf name, List.map (shapeType groupOf) args)
    | TList elem -> TList (shapeType groupOf elem)
    | TStream elem -> TStream (shapeType groupOf elem)
    | TDict (key, value) ->
        TDict (shapeType groupOf key, shapeType groupOf value)
    | TFunction (params, result) ->
        TFunction (List.map (shapeType groupOf) params, shapeType groupOf result)
    | TTuple elems -> TTuple (List.map (shapeType groupOf) elems)
    | typ -> typ
  in
  let text groupOf typ =
    CheckingDiagnostics.typeToString (shapeType groupOf typ)
  in
  let sumShape (info : Types.sumTypeInfo) groupOf =
    String.concat " " info.Types.typeParams
    ^ " = "
    ^ String.concat " | "
        (List.map
           (fun (variant : Types.sumVariantInfo) ->
             variant.Types.name ^ "("
             ^ String.concat ", " (List.map (text groupOf) variant.Types.fields)
             ^ ")")
           (List.stable_sort
              (fun (a : Types.sumVariantInfo) b ->
                Int.compare a.Types.tag b.Types.tag)
              info.Types.variants))
  in
  let recordShape (info : Types.recordTypeInfo) groupOf =
    String.concat " " info.Types.typeParams
    ^ " = { "
    ^ String.concat ", "
        (List.map
           (fun (field, typ) -> field ^ ": " ^ text groupOf typ)
           info.Types.fields)
    ^ " }"
  in
  let isCompilerOnly name =
    List.exists
      (fun part -> String.starts_with ~prefix:"__" part)
      (String.split_on_char '.' name)
  in
  let declarations =
    M.fold
      (fun name info acc ->
        if isCompilerOnly name then acc else (name, sumShape info) :: acc)
      sums []
    |> M.fold
         (fun name info acc ->
           if M.mem name sums || isCompilerOnly name then acc
           else (name, recordShape info) :: acc)
         records
  in
  let rec refine groups count =
    let groupOf name =
      match M.find_opt name groups with Some group -> group | None -> name
    in
    let _, idCount, next =
      List.fold_left
        (fun (ids, idCount, next) (name, shape) ->
          let key = groupOf name ^ " " ^ shape groupOf in
          match M.find_opt key ids with
          | Some id -> (ids, idCount, M.add name id next)
          | None ->
              let id = "#" ^ string_of_int idCount in
              (M.add key id ids, idCount + 1, M.add name id next))
        (M.empty, 0, M.empty) declarations
    in
    if idCount = count then next else refine next idCount
  in
  let initial = M.map (fun _ -> "") (M.of_list declarations) in
  let groups = refine initial 1 in
  let sizes =
    M.fold
      (fun _ group sizes ->
        M.add group (1 + Option.value ~default:0 (M.find_opt group sizes)) sizes)
      groups M.empty
  in
  M.fold
    (fun name group shared ->
      if Option.value ~default:0 (M.find_opt group sizes) > 1 then
        StringOrder.Set.add name shared
      else shared)
    groups StringOrder.Set.empty

(* An enum's type name before its case, or nothing where `dark eval` prints the
   case alone. *)
let caseTypeText env typ =
  match typ with
  | TSum (name, _)
    when !evalDisplay && StringOrder.Set.mem name (Lazy.force env.sharedShapes)
    ->
      ""
  | _ -> displayTypeText typ

let casePrefix typeText = if typeText = "" then "" else typeText ^ "."

let concat symbols = function
  | [] -> C.StringLiteral ""
  | first :: rest ->
      (* Every renderer fragment has an ASCII delimiter at each join: quotes,
    punctuation, separators, or the edge of a canonical numeric value.
    Those boundaries cannot compose under NFC, so retain the native raw
    concat used before public StringConcat acquired normalization. *)
      List.fold_left
        (fun acc part -> call symbols "__string_concat_raw" [ acc; part ])
        first rest

let stableHash value =
  String.fold_left
    (fun hash byte ->
      Int64.mul
        (Int64.logxor hash (Int64.of_int (Char.code byte)))
        1099511628211L)
    0xcbf29ce484222325L value

(* Helpers rendered as `dark eval` prints carry their own names, so a program
   holding both kinds (inspect beside a diagnostic) keeps them apart. *)
let hashed prefix typ =
  prefix
  ^ (if !evalDisplay then "eval_" else "")
  ^ Printf.sprintf "%016Lx"
      (stableHash (CheckingDiagnostics.typeToHelperIdentityString typ))

let rendererName = hashed "__dark_render_value_"
let listItemsRendererName = hashed "__dark_render_list_items_"
let dictItemsRendererName = hashed "__dark_render_dict_items_"

let runtimeFunctionNames =
  [
    "__string_concat_raw";
    "Darklang.Stdlib.Bool.toString";
    "Darklang.Stdlib.DateTime.toString";
    "Darklang.Stdlib.Dict.__renderKey";
    "Darklang.Stdlib.Dict.toList";
    "Darklang.Stdlib.Float.toString";
    "Darklang.Stdlib.Int.toString";
    "Darklang.Stdlib.Int128.toString";
    "Darklang.Stdlib.Int16.toString";
    "Darklang.Stdlib.Int32.toString";
    "Darklang.Stdlib.Int64.toString";
    "Darklang.Stdlib.Int8.toString";
    "Darklang.Stdlib.String.length";
    "Darklang.Stdlib.String.replaceAll";
    "Darklang.Stdlib.UInt128.toString";
    "Darklang.Stdlib.UInt16.toString";
    "Darklang.Stdlib.UInt32.toString";
    "Darklang.Stdlib.UInt64.toString";
    "Darklang.Stdlib.UInt8.toString";
    "Darklang.Stdlib.Uuid.toString";
    "Darklang.Stdlib.Float32.__display";
    "Darklang.Stdlib.Blob.__evalDisplay";
    "Darklang.Stdlib.Decimal.__display";
    "Darklang.Stdlib.List.push";
    "Darklang.Stdlib.Pretty.concat";
    "Darklang.Stdlib.Pretty.render";
    "Darklang.Stdlib.Pretty.text";
    "Darklang.Stdlib.Pretty.__valueBraced";
    "Darklang.Stdlib.Pretty.__valueBracketed";
  ]

let applySubstitution subst typ =
  let rec apply = function
    | (TVar name | TInferenceVar (_, name)) as typ ->
        Option.value ~default:typ (M.find_opt name subst)
    | TList elem -> TList (apply elem)
    | TStream elem -> TStream (apply elem)
    | TDict (key, value) -> TDict (apply key, apply value)
    | TFunction (params, result) ->
        TFunction (List.map apply params, apply result)
    | TTuple elems -> TTuple (List.map apply elems)
    | TRecord (name, args) -> TRecord (name, List.map apply args)
    | TSum (name, args) -> TSum (name, List.map apply args)
    | ( TInt8 | TInt16 | TInt32 | TInt64 | TInt128 | TInt | TUInt8 | TUInt16
      | TUInt32 | TUInt64 | TUInt128 | TBool | TFloat64 | TString | TBlob
      | TChar | TDateTime | TUnit | TNever | TInternalRawPtr ) as typ ->
        typ
  in
  apply typ

let typeSubstitution params arguments =
  if List.length params = List.length arguments then
    M.of_list (List.combine params arguments)
  else
    Crash.crash
      ("Value renderer type argument mismatch: params="
      ^ string_of_int (List.length params)
      ^ ", args="
      ^ string_of_int (List.length arguments))

let escapedString symbols quote value =
  let replace oldValue newValue input =
    call symbols "Darklang.Stdlib.String.replaceAll"
      [ input; C.StringLiteral oldValue; C.StringLiteral newValue ]
  in
  let escaped =
    value |> replace "\\" "\\\\" |> replace "\n" "\\n" |> replace "\r" "\\r"
    |> replace "\t" "\\t"
    |> replace quote ("\\" ^ quote)
  in
  concat symbols [ C.StringLiteral quote; escaped; C.StringLiteral quote ]

let makeCase pattern body =
  { C.patterns = N.singleton pattern; guard = None; body }

let rec canonicalRenderType env typ =
  let canonical = canonicalRenderType env in
  match typ with
  | TRecord (name, args) when M.mem name (Lazy.force env.sums) ->
      TSum (name, List.map canonical args)
  | TRecord (name, args) -> TRecord (name, List.map canonical args)
  | TSum (name, args) -> TSum (name, List.map canonical args)
  | TTuple elems -> TTuple (List.map canonical elems)
  | TList elem -> TList (canonical elem)
  | TDict (key, value) -> TDict (canonical key, canonical value)
  | TFunction (params, result) ->
      TFunction (List.map canonical params, canonical result)
  | _ -> typ

let functionDef id name parameter typ =
  {
    C.id;
    name;
    typeParams = [];
    params = N.singleton (parameter, C.checkedType typ);
    returnType = C.checkedType TString;
    bodyType = C.checkedType TString;
    body = C.StringLiteral "";
    recursion = None;
  }

let rec ensureRenderer env typ (state : renderState) =
  let typ = canonicalRenderType env typ in
  let name = rendererName typ in
  match M.find_opt name state.functions with
  | Some _ -> (name, state)
  | None ->
      let functionId, symbols = C.internFunction name state.symbols in
      let state = { state with symbols } in
      let valueId, state = freshBinding "__value" state in
      (* Reserve the name before descending so recursive sum types terminate. *)
      let reservedDefinition = functionDef functionId name valueId typ in
      let reserved =
        { state with functions = M.add name reservedDefinition state.functions }
      in
      let body, (withDependencies : renderState) =
        renderBody env typ (C.Local valueId) reserved
      in
      let completed = { reservedDefinition with C.body } in
      ( name,
        {
          withDependencies with
          functions = M.add name completed withDependencies.functions;
        } )

and renderCall env typ value state =
  match typ with
  | TVar _ | TInferenceVar _ ->
      (* Empty collections and inactive enum payloads can remain unconstrained.
         Their payload renderer is unreachable: do not invent a representation
         or require a concrete layout for a value that cannot be constructed. *)
      (C.RuntimeError "Cannot render an uninhabited type parameter", state)
  | _ ->
      let name, nextState = ensureRenderer env typ state in
      (call nextState.symbols name [ value ], nextState)

and renderDelimited env items state =
  let rec loop remaining current acc =
    match remaining with
    | [] -> (List.rev acc, current)
    | (typ, expr) :: rest ->
        let rendered, next = renderCall env typ expr current in
        loop rest next (rendered :: acc)
  in
  loop items state []

and ensureListItemsRenderer env elemType (state : renderState) =
  let listType = TList elemType in
  let name = listItemsRendererName listType in
  match M.find_opt name state.functions with
  | Some _ -> (name, state)
  | None ->
      let functionId, symbols = C.internFunction name state.symbols in
      let state = { state with symbols } in
      let itemsId, state = freshBinding "__items" state in
      let headId, state = freshBinding "__head" state in
      let tailId, state = freshBinding "__tail" state in
      let reservedDefinition = functionDef functionId name itemsId listType in
      let reserved =
        { state with functions = M.add name reservedDefinition state.functions }
      in
      let renderedHead, withElemRenderer =
        renderCall env elemType (C.Local headId) reserved
      in
      let tailBody =
        C.Match
          ( C.Local tailId,
            args
              [
                makeCase (C.PList []) (C.StringLiteral "");
                makeCase C.PWildcard
                  (concat reserved.symbols
                     [
                       C.StringLiteral ", ";
                       call reserved.symbols name [ C.Local tailId ];
                     ]);
              ] )
      in
      let body =
        C.Match
          ( C.Local itemsId,
            args
              [
                makeCase (C.PList []) (C.StringLiteral "");
                makeCase
                  (C.PListCons ([ C.PVariable headId ], C.PVariable tailId))
                  (concat withElemRenderer.symbols [ renderedHead; tailBody ]);
              ] )
      in
      ( name,
        {
          withElemRenderer with
          functions =
            M.add name
              { reservedDefinition with C.body }
              withElemRenderer.functions;
        } )

and ensureDictItemsRenderer env keyType valueType (state : renderState) =
  let listType = TList (TTuple [ keyType; valueType ]) in
  let name = dictItemsRendererName (TDict (keyType, valueType)) in
  match M.find_opt name state.functions with
  | Some _ -> (name, state)
  | None ->
      let functionId, symbols = C.internFunction name state.symbols in
      let state = { state with symbols } in
      let entriesId, state = freshBinding "__entries" state in
      let entryId, state = freshBinding "__entry" state in
      let tailId, state = freshBinding "__tail" state in
      let reservedDefinition = functionDef functionId name entriesId listType in
      let reserved =
        { state with functions = M.add name reservedDefinition state.functions }
      in
      let entryKey = C.TupleAccess (C.Local entryId, 0) in
      let entryValue = C.TupleAccess (C.Local entryId, 1) in
      (* `dark eval` shows every key as the value it is: { "a": 1 }. *)
      let renderedKey, withKeyRenderer =
        match keyType with
        | TString when not !evalDisplay ->
            ( call reserved.symbols "Darklang.Stdlib.Dict.__renderKey"
                [ entryKey ],
              reserved )
        | _ -> renderCall env keyType entryKey reserved
      in
      let separator =
        if keyType = TString && not !evalDisplay then " = " else ": "
      in
      let renderedValue, withValueRenderer =
        renderCall env valueType entryValue withKeyRenderer
      in
      let renderedEntry =
        concat withValueRenderer.symbols
          [ renderedKey; C.StringLiteral separator; renderedValue ]
      in
      let tailBody =
        C.Match
          ( C.Local tailId,
            args
              [
                makeCase (C.PList []) (C.StringLiteral "");
                makeCase C.PWildcard
                  (concat withValueRenderer.symbols
                     [
                       C.StringLiteral ", ";
                       call withValueRenderer.symbols name [ C.Local tailId ];
                     ]);
              ] )
      in
      let body =
        C.Match
          ( C.Local entriesId,
            args
              [
                makeCase (C.PList []) (C.StringLiteral "");
                makeCase
                  (C.PListCons ([ C.PVariable entryId ], C.PVariable tailId))
                  (concat withValueRenderer.symbols [ renderedEntry; tailBody ]);
              ] )
      in
      ( name,
        {
          withValueRenderer with
          functions =
            M.add name
              { reservedDefinition with C.body }
              withValueRenderer.functions;
        } )

and suffixed state name suffix value =
  let text = call state.symbols name [ value ] in
  if !evalDisplay then concat state.symbols [ text; C.StringLiteral suffix ]
  else text

and renderBody env typ value state =
  match typ with
  | TUnit -> (C.StringLiteral "()", state)
  | TBool ->
      (call state.symbols "Darklang.Stdlib.Bool.toString" [ value ], state)
  | TInt8 -> (suffixed state "Darklang.Stdlib.Int8.toString" "s8" value, state)
  | TInt16 -> (suffixed state "Darklang.Stdlib.Int16.toString" "s16" value, state)
  | TInt32 -> (suffixed state "Darklang.Stdlib.Int32.toString" "s32" value, state)
  | TInt64 ->
      (call state.symbols "Darklang.Stdlib.Int64.toString" [ value ], state)
  | TInt -> (suffixed state "Darklang.Stdlib.Int.toString" "z" value, state)
  | TUInt8 -> (suffixed state "Darklang.Stdlib.UInt8.toString" "u8" value, state)
  | TUInt16 -> (suffixed state "Darklang.Stdlib.UInt16.toString" "u16" value, state)
  | TUInt32 -> (suffixed state "Darklang.Stdlib.UInt32.toString" "u32" value, state)
  | TUInt64 -> (suffixed state "Darklang.Stdlib.UInt64.toString" "u64" value, state)
  (* Fixed-block 128-bit values cross the textual boundary through their
    limb-based decimal formatters. *)
  | TInt128 -> (suffixed state "Darklang.Stdlib.Int128.toString" "s128" value, state)
  | TUInt128 -> (suffixed state "Darklang.Stdlib.UInt128.toString" "u128" value, state)
  | TFloat64 ->
      (call state.symbols "Darklang.Stdlib.Float.toString" [ value ], state)
  | TString -> (escapedString state.symbols "\"" value, state)
  | TChar -> (escapedString state.symbols "'" value, state)
  | TDateTime ->
      let text = call state.symbols "Darklang.Stdlib.DateTime.toString" [ value ] in
      ( (if !evalDisplay then
           concat state.symbols
             [ C.StringLiteral "<DateTime: "; text; C.StringLiteral ">" ]
         else text),
        state )
  | TTuple elems ->
      let items =
        List.mapi (fun index typ -> (typ, C.TupleAccess (value, index))) elems
      in
      let rendered, next = renderDelimited env items state in
      let separated =
        List.concat
          (List.mapi
             (fun index expr ->
               if index = 0 then [ expr ] else [ C.StringLiteral ", "; expr ])
             rendered)
      in
      ( concat next.symbols
          ((C.StringLiteral "(" :: separated) @ [ C.StringLiteral ")" ]),
        next )
  | TList elem ->
      let itemsName, next = ensureListItemsRenderer env elem state in
      let typeName = CheckingDiagnostics.typeToString typ in
      ( C.Match
          ( value,
            args
              [
                makeCase (C.PList []) (C.StringLiteral (typeName ^ " []"));
                makeCase C.PWildcard
                  (concat next.symbols
                     [
                       C.StringLiteral "[";
                       call next.symbols itemsName [ value ];
                       C.StringLiteral "]";
                     ]);
              ] ),
        next )
  | TStream _ -> (C.StringLiteral "<stream>", state)
  (* An unconstrained Dict value can only be the polymorphic empty literal;
    no value renderer is needed because there are no entries to inspect. *)
  | TDict ((TVar _ | TInferenceVar _), (TVar _ | TInferenceVar _)) ->
      (C.StringLiteral (if !evalDisplay then "{}" else "Dict { }"), state)
  | TDict (key, valueType) ->
      let itemsName, next = ensureDictItemsRenderer env key valueType state in
      let entriesId, next = freshBinding "__dict_entries" next in
      let entries =
        C.TypeApp
          ( resolveFunction next.symbols "Darklang.Stdlib.Dict.toList",
            C.checkedTypeArgs [ key; valueType ],
            N.singleton value )
      in
      ( C.Let
          ( C.LPVariable entriesId,
            entries,
            C.Match
              ( C.Local entriesId,
                args
                  [
                    makeCase (C.PList [])
                      (C.StringLiteral (if !evalDisplay then "{}" else "Dict { }"));
                    makeCase C.PWildcard
                      (concat next.symbols
                         [
                           C.StringLiteral (if !evalDisplay then "{ " else "Dict { ");
                           call next.symbols itemsName [ C.Local entriesId ];
                           C.StringLiteral " }";
                         ]);
                  ] ) ),
        next )
  | TRecord (typeName, typeArgs) -> (
      match M.find_opt typeName (Lazy.force env.records) with
      | None ->
          Crash.crash ("Missing record metadata for value renderer: " ^ typeName)
      | Some recordInfo ->
          let rec collect = function
            | TVar name | TInferenceVar (_, name) -> [ name ]
            | TList elem -> collect elem
            | TDict (key, value) -> collect key @ collect value
            | TFunction (params, result) ->
                List.concat_map collect params @ collect result
            | TTuple elems -> List.concat_map collect elems
            | TRecord (_, args) | TSum (_, args) -> List.concat_map collect args
            | _ -> []
          in
          let fallbackTypeParams =
            List.concat_map
              (fun (_, typ) -> collect typ)
              recordInfo.Types.fields
            |> List.fold_left
                 (fun names name ->
                   if List.mem name names then names else names @ [ name ])
                 []
          in
          let typeParams =
            if recordInfo.Types.typeParams = [] then fallbackTypeParams
            else recordInfo.Types.typeParams
          in
          let subst = typeSubstitution typeParams typeArgs in
          let sortedFields =
            List.mapi
              (fun index (name, typ) -> (index, name, typ))
              recordInfo.Types.fields
            |> fun fields ->
            if !evalDisplay then fields
            else
              List.stable_sort
                (fun (_, a, _) (_, b, _) -> StringOrder.compare a b)
                fields
          in
          let rec renderFields remaining current acc =
            match remaining with
            | [] -> (List.rev acc, current)
            | (index, name, typ) :: rest ->
                let concrete = applySubstitution subst typ in
                let fieldId, symbols =
                  C.internField typeName name index current.symbols
                in
                let rendered, next =
                  renderCall env concrete
                    (C.RecordAccess (value, fieldId))
                    { current with symbols }
                in
                renderFields rest next ((name, rendered) :: acc)
          in
          let renderedFields, next = renderFields sortedFields state [] in
          let parts separator =
            List.concat
              (List.mapi
                 (fun index (name, rendered) ->
                   [
                     C.StringLiteral
                       ((if index = 0 then "" else separator) ^ name ^ ": ");
                     rendered;
                   ])
                 renderedFields)
          in
          let typeText =
            if !evalDisplay then
              displayTypeText typ
              ^ evalTypeArgs typeParams typeArgs
                  (List.map snd recordInfo.Types.fields)
            else displayTypeText typ
          in
          let short =
            concat next.symbols
              ((C.StringLiteral (typeText ^ " { ") :: parts ", ")
              @ [ C.StringLiteral " }" ])
          in
          let long =
            concat next.symbols
              ((C.StringLiteral (typeText ^ " {\n  ") :: parts ",\n  ")
              @ [ C.StringLiteral "\n}" ])
          in
          let shortId, next = freshBinding "__record_short" next in
          ( C.Let
              ( C.LPVariable shortId,
                short,
                C.If
                  ( C.BinOp
                      ( Lte,
                        call next.symbols "Darklang.Stdlib.String.length"
                          [ C.Local shortId ],
                        C.Int64Literal 80L ),
                    C.Local shortId,
                    long ) ),
            next ))
  | TSum ("Uuid", []) ->
      let text = call state.symbols "Darklang.Stdlib.Uuid.toString" [ value ] in
      ( (if !evalDisplay then
           concat state.symbols
             [ C.StringLiteral "<Uuid: "; text; C.StringLiteral ">" ]
         else text),
        state )
  (* `dark eval` shows these as they are written: 1.5f32 and 1.10dec. *)
  | TSum ("Float32", []) ->
      (call state.symbols "Darklang.Stdlib.Float32.__display" [ value ], state)
  | TSum ("Decimal", []) ->
      (call state.symbols "Darklang.Stdlib.Decimal.__display" [ value ], state)
  | TSum (typeName, typeArgs) -> (
      match M.find_opt typeName (Lazy.force env.sums) with
      | None ->
          Crash.crash ("Missing sum metadata for value renderer: " ^ typeName)
      | Some sumInfo ->
          let subst = typeSubstitution sumInfo.Types.typeParams typeArgs in
          let typeText = caseTypeText env typ in
          let casePrefix (variant : Types.sumVariantInfo) =
            if !evalDisplay && typeText <> "" then
              casePrefix
                (typeText
                ^ evalTypeArgs sumInfo.Types.typeParams typeArgs
                    variant.Types.fields)
            else casePrefix typeText
          in
          let rec buildCases remaining current acc =
            match remaining with
            | [] -> (List.rev acc, current)
            | (variant : Types.sumVariantInfo) :: rest -> (
                match variant.Types.fields with
                | [] ->
                    let case =
                      makeCase
                        (constructorPattern typeName variant current.symbols [])
                        (C.StringLiteral (casePrefix variant ^ variant.Types.name))
                    in
                    buildCases rest current (case :: acc)
                | fieldTypes ->
                    let concreteTypes =
                      List.map (applySubstitution subst) fieldTypes
                    in
                    let fieldNames =
                      List.mapi
                        (fun index _ ->
                          "__field_"
                          ^ string_of_int variant.Types.tag
                          ^ "_" ^ string_of_int index)
                        fieldTypes
                    in
                    let reversed, current =
                      List.fold_left
                        (fun (ids, state) name ->
                          let id, next = freshBinding name state in
                          (id :: ids, next))
                        ([], current) fieldNames
                    in
                    let fieldIds = List.rev reversed in
                    let items =
                      List.map2
                        (fun typ id -> (typ, C.Local id))
                        concreteTypes fieldIds
                    in
                    let rendered, next = renderDelimited env items current in
                    let separated =
                      List.concat
                        (List.mapi
                           (fun index expr ->
                             if index = 0 then [ expr ]
                             else [ C.StringLiteral ", "; expr ])
                           rendered)
                    in
                    let body =
                      concat next.symbols
                        (C.StringLiteral
                           (casePrefix variant ^ variant.Types.name ^ "(")
                         :: separated
                        @ [ C.StringLiteral ")" ])
                    in
                    let case =
                      makeCase
                        (constructorPattern typeName variant next.symbols
                           (List.map (fun id -> C.PVariable id) fieldIds))
                        body
                    in
                    buildCases rest next (case :: acc))
          in
          let cases, next =
            buildCases
              (List.stable_sort
                 (fun (a : Types.sumVariantInfo) (b : Types.sumVariantInfo) ->
                   Int.compare a.Types.tag b.Types.tag)
                 sumInfo.Types.variants)
              state []
          in
          (C.Match (value, args cases), next))
  | TFunction _ -> (C.StringLiteral "(lambda)", state)
  | TBlob when !evalDisplay ->
      (* As the current interpreter prints unsaved Bytes: the length and the
     first 16 bytes in hex. *)
      (call state.symbols "Darklang.Stdlib.Blob.__evalDisplay" [ value ], state)
  | TBlob ->
      (* The interpreter deliberately does not expose ephemeral Blob payloads
     or process-local identities through value rendering. *)
      (C.StringLiteral "<Blob: ephemeral>", state)
  | TInternalRawPtr ->
      (call state.symbols "Darklang.Stdlib.Int64.toString" [ value ], state)
  | TNever -> (C.StringLiteral "()", state)
  | TVar name ->
      Crash.crash ("Unresolved type variable in value renderer: " ^ name)
  | TInferenceVar (displayName, _) ->
      Crash.crash
        ("Unresolved inference variable in value renderer: " ^ displayName)

(* `--syntax=current` lays a result out as `dark eval` does: each tuple, list,
   dict, record and case payload is a Stdlib.Pretty group that stays on one
   line when it fits in 80 columns and otherwise puts one item per line,
   indented by two. Scalars are text from the renderers above. *)
let docType = TSum ("Darklang.Stdlib.Pretty.Doc", [])
let docRendererName = hashed "__dark_render_doc_"
let docItemsRendererName = hashed "__dark_render_doc_items_"
let docEntriesRendererName = hashed "__dark_render_doc_entries_"
let evalRendererName = hashed "__dark_render_eval_"

let docFunctionDef id name parameter parameterType returnType =
  {
    (functionDef id name parameter parameterType) with
    C.returnType = C.checkedType returnType;
    bodyType = C.checkedType returnType;
  }

let docText symbols expr = call symbols "Darklang.Stdlib.Pretty.text" [ expr ]

let docConcat symbols left right =
  call symbols "Darklang.Stdlib.Pretty.concat" [ left; right ]

let docBracketed symbols opening closing parts =
  call symbols "Darklang.Stdlib.Pretty.__valueBracketed"
    [ C.StringLiteral opening; C.StringLiteral closing; parts ]

let docBraced symbols opening closing parts =
  call symbols "Darklang.Stdlib.Pretty.__valueBraced"
    [ C.StringLiteral opening; C.StringLiteral closing; parts ]

let docPush symbols list value =
  C.TypeApp
    ( resolveFunction symbols "Darklang.Stdlib.List.push",
      C.checkedTypeArgs [ docType ],
      args [ list; value ] )

(* Reserve a generated function, build its body, and record it. *)
let generated name parameterType returnType (state : renderState) build =
  match M.find_opt name state.functions with
  | Some _ -> (name, state)
  | None ->
      let functionId, symbols = C.internFunction name state.symbols in
      let state = { state with symbols } in
      let parameter, state = freshBinding "__value" state in
      let reservedDefinition =
        docFunctionDef functionId name parameter parameterType returnType
      in
      let reserved =
        { state with functions = M.add name reservedDefinition state.functions }
      in
      let body, (built : renderState) = build (C.Local parameter) reserved in
      ( name,
        {
          built with
          functions =
            M.add name { reservedDefinition with C.body } built.functions;
        } )

let rec ensureDocRenderer env typ state =
  let typ = canonicalRenderType env typ in
  generated (docRendererName typ) typ docType state (fun value state ->
      docBody env typ value state)

and docCall env typ value state =
  match typ with
  | TVar _ | TInferenceVar _ ->
      (C.RuntimeError "Cannot render an uninhabited type parameter", state)
  | _ ->
      let name, next = ensureDocRenderer env typ state in
      (call next.symbols name [ value ], next)

and docList env items state =
  let rec loop remaining current acc =
    match remaining with
    | [] -> (C.ListLiteral (List.rev acc), current)
    | (typ, expr) :: rest ->
        let doc, next = docCall env typ expr current in
        loop rest next (doc :: acc)
  in
  loop items state []

(* A List<T> as the List<Doc> of its items, in order. *)
and ensureDocItemsRenderer env elemType state =
  let listType = TList elemType in
  generated (docItemsRendererName listType) listType (TList docType) state
    (fun items state ->
      let name = docItemsRendererName listType in
      let headId, state = freshBinding "__head" state in
      let tailId, state = freshBinding "__tail" state in
      let head, state = docCall env elemType (C.Local headId) state in
      let rest = call state.symbols name [ C.Local tailId ] in
      ( C.Match
          ( items,
            args
              [
                makeCase (C.PList []) (C.ListLiteral []);
                makeCase
                  (C.PListCons ([ C.PVariable headId ], C.PVariable tailId))
                  (docPush state.symbols rest head);
              ] ),
        state ))

(* Dict entries as `key: value` docs, in key order. *)
and ensureDocEntriesRenderer env keyType valueType state =
  let listType = TList (TTuple [ keyType; valueType ]) in
  let name = docEntriesRendererName (TDict (keyType, valueType)) in
  generated name listType (TList docType) state (fun entries state ->
      let entryId, state = freshBinding "__entry" state in
      let tailId, state = freshBinding "__tail" state in
      let key, state =
        renderCall env keyType (C.TupleAccess (C.Local entryId, 0)) state
      in
      let value, state =
        docCall env valueType (C.TupleAccess (C.Local entryId, 1)) state
      in
      let entry =
        docConcat state.symbols
          (docText state.symbols
             (concat state.symbols [ key; C.StringLiteral ": " ]))
          value
      in
      let rest = call state.symbols name [ C.Local tailId ] in
      ( C.Match
          ( entries,
            args
              [
                makeCase (C.PList []) (C.ListLiteral []);
                makeCase
                  (C.PListCons ([ C.PVariable entryId ], C.PVariable tailId))
                  (docPush state.symbols rest entry);
              ] ),
        state ))

and docBody env typ value state =
  let leaf () =
    let rendered, next = renderCall env typ value state in
    (docText next.symbols rendered, next)
  in
  match typ with
  | TTuple elems ->
      let parts, next =
        docList env
          (List.mapi (fun index typ -> (typ, C.TupleAccess (value, index))) elems)
          state
      in
      (docBracketed next.symbols "(" ")" parts, next)
  | TList elem ->
      let empty, next = renderCall env typ value state in
      let itemsName, next = ensureDocItemsRenderer env elem next in
      ( C.Match
          ( value,
            args
              [
                makeCase (C.PList []) (docText next.symbols empty);
                makeCase C.PWildcard
                  (docBracketed next.symbols "[" "]"
                     (call next.symbols itemsName [ value ]));
              ] ),
        next )
  | TDict ((TVar _ | TInferenceVar _), (TVar _ | TInferenceVar _)) -> leaf ()
  | TDict (key, valueType) ->
      let entriesName, next =
        ensureDocEntriesRenderer env key valueType state
      in
      let entriesId, next = freshBinding "__dict_entries" next in
      let entries =
        C.TypeApp
          ( resolveFunction next.symbols "Darklang.Stdlib.Dict.toList",
            C.checkedTypeArgs [ key; valueType ],
            N.singleton value )
      in
      ( C.Let
          ( C.LPVariable entriesId,
            entries,
            C.Match
              ( C.Local entriesId,
                args
                  [
                    makeCase (C.PList [])
                      (docText next.symbols (C.StringLiteral "{}"));
                    makeCase C.PWildcard
                      (docBraced next.symbols "{" "}"
                         (call next.symbols entriesName [ C.Local entriesId ]));
                  ] ) ),
        next )
  | TRecord (typeName, typeArgs) -> (
      match M.find_opt typeName (Lazy.force env.records) with
      | None -> leaf ()
      | Some recordInfo ->
          let rec collect = function
            | TVar name | TInferenceVar (_, name) -> [ name ]
            | TList elem -> collect elem
            | TDict (key, value) -> collect key @ collect value
            | TFunction (params, result) ->
                List.concat_map collect params @ collect result
            | TTuple elems -> List.concat_map collect elems
            | TRecord (_, args) | TSum (_, args) -> List.concat_map collect args
            | _ -> []
          in
          let typeParams =
            if recordInfo.Types.typeParams <> [] then
              recordInfo.Types.typeParams
            else
              List.concat_map (fun (_, typ) -> collect typ) recordInfo.Types.fields
              |> List.fold_left
                   (fun names name ->
                     if List.mem name names then names else names @ [ name ])
                   []
          in
          let subst = typeSubstitution typeParams typeArgs in
          let rec fields remaining index current acc =
            match remaining with
            | [] -> (C.ListLiteral (List.rev acc), current)
            | (name, typ) :: rest ->
                let fieldId, symbols =
                  C.internField typeName name index current.symbols
                in
                let doc, next =
                  docCall env (applySubstitution subst typ)
                    (C.RecordAccess (value, fieldId))
                    { current with symbols }
                in
                let field =
                  docConcat next.symbols
                    (docText next.symbols (C.StringLiteral (name ^ ": ")))
                    doc
                in
                fields rest (index + 1) next (field :: acc)
          in
          let parts, next = fields recordInfo.Types.fields 0 state [] in
          ( docConcat next.symbols
              (docText next.symbols
                 (C.StringLiteral
                    (displayTypeText typ
                    ^ evalTypeArgs typeParams typeArgs
                        (List.map snd recordInfo.Types.fields)
                    ^ " ")))
              (docBraced next.symbols "{" "}" parts),
            next ))
  | TSum (("Uuid" | "Float32" | "Decimal"), []) -> leaf ()
  | TSum (typeName, typeArgs) -> (
      match M.find_opt typeName (Lazy.force env.sums) with
      | None -> leaf ()
      | Some sumInfo ->
          let subst = typeSubstitution sumInfo.Types.typeParams typeArgs in
          let typeText = caseTypeText env typ in
          let prefix (variant : Types.sumVariantInfo) =
            if typeText = "" then ""
            else
              casePrefix
                (typeText
                ^ evalTypeArgs sumInfo.Types.typeParams typeArgs
                    variant.Types.fields)
          in
          let rec cases remaining current acc =
            match remaining with
            | [] -> (List.rev acc, current)
            | (variant : Types.sumVariantInfo) :: rest -> (
                let caseText =
                  C.StringLiteral (prefix variant ^ variant.Types.name)
                in
                match variant.Types.fields with
                | [] ->
                    let case =
                      makeCase
                        (constructorPattern typeName variant current.symbols [])
                        (docText current.symbols caseText)
                    in
                    cases rest current (case :: acc)
                | fieldTypes ->
                    let reversed, current =
                      List.fold_left
                        (fun (ids, state) index ->
                          let id, next =
                            freshBinding
                              ("__field_"
                              ^ string_of_int variant.Types.tag
                              ^ "_" ^ string_of_int index)
                              state
                          in
                          (id :: ids, next))
                        ([], current)
                        (List.mapi (fun index _ -> index) fieldTypes)
                    in
                    let fieldIds = List.rev reversed in
                    let parts, next =
                      docList env
                        (List.map2
                           (fun typ id -> (applySubstitution subst typ, C.Local id))
                           fieldTypes fieldIds)
                        current
                    in
                    let case =
                      makeCase
                        (constructorPattern typeName variant next.symbols
                           (List.map (fun id -> C.PVariable id) fieldIds))
                        (docConcat next.symbols
                           (docText next.symbols caseText)
                           (docBracketed next.symbols "(" ")" parts))
                    in
                    cases rest next (case :: acc))
          in
          let cases, next =
            cases
              (List.stable_sort
                 (fun (a : Types.sumVariantInfo) (b : Types.sumVariantInfo) ->
                   Int.compare a.Types.tag b.Types.tag)
                 sumInfo.Types.variants)
              state []
          in
          (C.Match (value, args cases), next))
  | _ -> leaf ()

(* The boundary renderer under `--syntax=current`: the value's document laid
   out in 80 columns, as `dark eval` prints it. *)
let ensureEvalRenderer env typ state =
  let typ = canonicalRenderType env typ in
  generated (evalRendererName typ) typ TString state (fun value state ->
      let doc, next = docCall env typ value state in
      ( call next.symbols "Darklang.Stdlib.Pretty.render"
          [ C.Int64Literal 80L; doc ],
        next ))

let starts name prefix = Text.startsWith name prefix

let renderEnv recordMetadata sumMetadata =
  {
    records = lazy recordMetadata;
    sums = lazy sumMetadata;
    sharedShapes = lazy (sharedShapeNames recordMetadata sumMetadata);
  }

let includeRuntimeFunctions symbols =
  List.fold_left
    (fun symbols name -> snd (C.internFunction name symbols))
    symbols runtimeFunctionNames

let existingRenderers topLevels =
  List.filter_map
    (function
      | C.FunctionDef definition when starts definition.C.name "__dark_render_"
        ->
          Some (definition.C.name, definition)
      | _ -> None)
    topLevels
  |> M.of_list

let rewriteProgram recordMetadata sumMetadata programType program =
  let symbols, topLevels = C.viewProgram program in
  let symbols = includeRuntimeFunctions symbols in
  (* Type checking already built and overlaid these immutable indexes. Keep
    them lazy so primitive renderers do not inspect declaration metadata. *)
  let env = renderEnv recordMetadata sumMetadata in
  let existing = existingRenderers topLevels in
  let renderName, state =
    match programType with
    | TDateTime when not !evalDisplay -> (None, { functions = existing; symbols })
    | _ ->
        let name, state =
          if !evalDisplay then
            ensureEvalRenderer env programType { functions = existing; symbols }
          else ensureRenderer env programType { functions = existing; symbols }
        in
        (Some name, state)
  in
  let rec tryNamedPartialName = function
    | C.Let (C.LPVariable capture, _, body)
      when Option.fold ~none:false
             ~some:(fun name -> starts name "__partial_capture_")
             (C.bindingName capture symbols) ->
        tryNamedPartialName body
    | C.Lambda (parameters, _returnAnnotation, body) -> (
        let parameterIds =
          N.toList parameters
          |> List.filter_map (fun (parameter : C.lambdaParameter) ->
              match parameter.C.pattern with
              | C.LPVariable id -> Some id
              | _ -> None)
        in
        let generatedPartial =
          List.length parameterIds = N.length parameters
          && List.for_all
               (fun id ->
                 Option.fold ~none:false
                   ~some:(fun name -> starts name "__partial_")
                   (C.bindingName id symbols))
               parameterIds
        in
        let callNameAndArgs =
          match body with
          | C.Call (name, callArgs) | C.TypeApp (name, _, callArgs) ->
              Some (name, N.toList callArgs)
          | _ -> None
        in
        match (generatedPartial, callNameAndArgs) with
        | true, Some (name, callArgs)
          when List.length callArgs > List.length parameterIds ->
            let trailing =
              List.filteri
                (fun index _ ->
                  index >= List.length callArgs - List.length parameterIds)
                callArgs
            in
            if
              List.for_all2
                (fun arg id -> arg = C.Local id)
                trailing parameterIds
            then Some name
            else None
        | _ -> None)
    | _ -> None
  in
  let rewriteExpression state expr =
    let rendered, next =
      match (programType, expr, tryNamedPartialName expr) with
      | TString, _, _ when !evalDisplay -> (expr, state)
      | TUnit, _, _ when !evalDisplay ->
          let id, next = freshBinding "__rendered_unit" state in
          (C.Let (C.LPVariable id, expr, C.StringLiteral ""), next)
      | TDateTime, _, _ when not !evalDisplay ->
          ( C.BoundaryRender
              ( resolveFunction state.symbols "Darklang.Stdlib.DateTime.toString",
                expr ),
            state )
      | TFunction _, _, Some functionId ->
          let id, next = freshBinding "__rendered_named_partial" state in
          let name =
            match C.functionName functionId state.symbols with
            | Some name -> name
            | None ->
                Crash.crash
                  "Named partial function identity is absent from symbols"
          in
          (C.Let (C.LPVariable id, expr, C.StringLiteral name), next)
      | TFunction _, C.FuncRef functionId, _ ->
          let id, next = freshBinding "__rendered_named_function" state in
          let name =
            match C.functionName functionId state.symbols with
            | Some name -> name
            | None -> Crash.crash "Function identity is absent from symbols"
          in
          (C.Let (C.LPVariable id, expr, C.StringLiteral name), next)
      | TFunction _, C.Lambda _, _ ->
          let id, next = freshBinding "__rendered_lambda" state in
          (C.Let (C.LPVariable id, expr, C.StringLiteral "(lambda)"), next)
      | _ ->
          let renderer =
            match renderName with
            | Some name -> resolveFunction state.symbols name
            | None -> Crash.crash "Missing boundary value renderer"
          in
          (C.BoundaryRender (renderer, expr), state)
    in
    (C.Expression rendered, next)
  in
  let reversed, final =
    List.fold_left
      (fun (tops, state) top ->
        let top, next =
          match top with
          | C.Expression expr -> rewriteExpression state expr
          | _ -> (top, state)
        in
        (top :: tops, next))
      ([], state) topLevels
  in
  let generated =
    M.bindings
      (M.filter (fun name _ -> not (M.mem name existing)) state.functions)
    |> List.map (fun (_, func) -> C.FunctionDef func)
  in
  C.programFromCheckedParts (final.symbols, generated @ List.rev reversed)

(* A call to the impl of <method> for <valueType>, when the type has one. *)
let traitCall state methodName valueType value =
  let catalog = C.traitCatalog state.symbols in
  match M.find_opt methodName catalog.TraitCatalog.dispatches with
  | None -> None
  | Some dispatch -> (
      match TraitResolution.select catalog dispatch [ valueType ] with
      | Error _ -> None
      | Ok (target, parameters) ->
          let target = resolveFunction state.symbols target in
          let args = N.singleton value in
          Some
            (if parameters = [] then C.Call (target, args)
             else C.TypeApp (target, C.checkedTypeArgs parameters, args)))

(* Stdlib.inspect: the type's Inspect impl when it has one, otherwise the
   value renderer. *)
let inspection env valueType value state =
  match traitCall state "Darklang.Stdlib.Inspect.inspect" valueType value with
  | Some call -> (call, state)
  | None ->
      (* Stdlib.inspect is the interpreter's developer form, `Some(3)`, in
         either output mode. *)
      let previous = !evalDisplay in
      evalDisplay := true;
      let rendered = renderCall env valueType value state in
      evalDisplay := previous;
      rendered

(* `$"{value}"` shows a String as it is, a value whose type has a Display
   impl through that impl, and any other value as Stdlib.inspect does. *)
let interpolation env valueType value state =
  match valueType with
  | TString -> (value, state)
  | _ -> (
      match
        traitCall state "Darklang.Stdlib.Display.display" valueType value
      with
      | Some call -> (call, state)
      | None -> inspection env valueType value state)

(* Replace concrete diagnostic renderer specializations with the same
   monomorphic renderer used for eval results. *)
let rewriteDiagnosticRenderers recordMetadata sumMetadata program =
  let symbols, topLevels = C.viewProgram program in
  let symbols = includeRuntimeFunctions symbols in
  let env = renderEnv recordMetadata sumMetadata in
  let existing = existingRenderers topLevels in
  let initial = { functions = existing; symbols } in
  let reversed, final =
    List.fold_left
      (fun (tops, state) top ->
        let top, next =
          match top with
          | C.FunctionDef definition
            when List.exists (starts definition.C.name)
                   [
                     "Darklang.Stdlib.Dict.__renderGenericKey_";
                     "Darklang.Stdlib.Test.__renderValue_";
                     "Darklang.Stdlib.inspect_";
                     "Darklang.Stdlib.__interpolate_";
                   ] -> (
              match N.toList definition.C.params with
              | [ (parameter, valueType) ] ->
                  let valueType = C.semanticType valueType in
                  let value = C.Local parameter in
                  let body, next =
                    if starts definition.C.name "Darklang.Stdlib.__interpolate_"
                    then interpolation env valueType value state
                    else if starts definition.C.name "Darklang.Stdlib.inspect_"
                    then inspection env valueType value state
                    else renderCall env valueType value state
                  in
                  (C.FunctionDef { definition with C.body }, next)
              | _ ->
                  Crash.crash
                    "Diagnostic value renderer has an invalid parameter list")
          | _ -> (top, state)
        in
        (top :: tops, next))
      ([], initial) topLevels
  in
  let generated =
    M.bindings
      (M.filter (fun name _ -> not (M.mem name existing)) final.functions)
    |> List.map (fun (_, func) -> C.FunctionDef func)
  in
  C.programFromCheckedParts (final.symbols, generated @ List.rev reversed)
