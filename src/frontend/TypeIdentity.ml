(* TypeIdentity.ml - Declarations with the same content are one type, as the
   interpreter's content-addressed store has them. *)
open WrittenTypeSupport
module WT = WrittenTypes
module WS = WrittenSource
module M = StringOrder.Map
module S = StringOrder.Set

(* What the interpreter hashes for a record or enum: type parameter names,
   field names, case names, field labels and field types. *)
type content = { tag : string; parts : (string * AST.semanticType list) list }

let compilerOnly name =
  List.exists
    (fun part -> String.starts_with ~prefix:"__" part)
    (String.split_on_char '.' name)

(* Stdlib types keep their own names: a diagnostic names the type the author
   wrote, which is what tells the many one-case ParseErrors apart. *)
let keepsName name = String.starts_with ~prefix:"Darklang.Stdlib." name

let moduleOf name =
  match List.rev (String.split_on_char '.' name) with
  | _ :: path -> List.rev path
  | [] -> []

let[@warning "-4"] content allowInternal types (entry : typeEntry) =
  let resolve =
    resolveWrittenType allowInternal types entry.path (S.of_list entry.params)
  in
  let tag kind = kind ^ "<" ^ String.concat "," entry.params ^ ">" in
  match entry.definition with
  | WT.TDRecord fields ->
      Result.map
        (fun parts -> { tag = tag "record"; parts })
        (ResultList.traverse
           (fun ((field : WT.recordFieldSyntax), _) ->
             Result.map
               (fun typ -> (snd field.WT.name, [ typ ]))
               (resolve field.WT.typ))
           fields)
  | WT.TDEnum cases ->
      Result.map
        (fun parts -> { tag = tag "enum"; parts })
        (ResultList.traverse
           (fun (_, (case : WT.enumCaseSyntax)) ->
             let labels =
               List.map
                 (fun (field : WT.enumFieldSyntax) ->
                   Option.fold ~none:"_" ~some:snd field.WT.label)
                 case.WT.fields
             in
             Result.map
               (fun types ->
                 (snd case.WT.name ^ "(" ^ String.concat "," labels ^ ")", types))
               (ResultList.traverse
                  (fun (field : WT.enumFieldSyntax) -> resolve field.WT.typ)
                  case.WT.fields))
           cases)
  | WT.TDAlias _ -> Error "alias"

let rec shapeType groupOf = function
  | AST.TRecord (name, args) | AST.TSum (name, args) ->
      AST.TSum (groupOf name, List.map (shapeType groupOf) args)
  | AST.TList elem -> AST.TList (shapeType groupOf elem)
  | AST.TStream elem -> AST.TStream (shapeType groupOf elem)
  | AST.TDict (key, value) ->
      AST.TDict (shapeType groupOf key, shapeType groupOf value)
  | AST.TFunction (params, result) ->
      AST.TFunction (List.map (shapeType groupOf) params, shapeType groupOf result)
  | AST.TTuple elems -> AST.TTuple (List.map (shapeType groupOf) elems)
  | typ -> typ
[@@warning "-4"]

let shape groupOf content =
  content.tag
  ^ String.concat " "
      (List.map
         (fun (name, types) ->
           name ^ ":"
           ^ String.concat ","
               (List.map
                  (fun typ ->
                    CheckingDiagnostics.typeToString (shapeType groupOf typ))
                  types))
         content.parts)

(* Group by content, comparing referenced types by group, until stable. *)
let groups declarations =
  let rec refine groups count =
    let groupOf name =
      match M.find_opt name groups with Some group -> group | None -> name
    in
    let _, idCount, next =
      List.fold_left
        (fun (ids, idCount, next) (name, content) ->
          let key = groupOf name ^ " " ^ shape groupOf content in
          match M.find_opt key ids with
          | Some id -> (ids, idCount, M.add name id next)
          | None ->
              let id = "#" ^ string_of_int idCount in
              (M.add key id ids, idCount + 1, M.add name id next))
        (M.empty, 0, M.empty) declarations
    in
    if idCount = count then next else refine next idCount
  in
  let byGroup =
    M.fold
      (fun name group byGroup ->
        M.add group (name :: Option.value ~default:[] (M.find_opt group byGroup)) byGroup)
      (refine (M.map (fun _ -> "") (M.of_list declarations)) 1)
      M.empty
  in
  M.fold
    (fun _ members acc -> if List.length members > 1 then members :: acc else acc)
    byGroup []

let[@warning "-4"] implementedTypes allowInternal types items =
  List.fold_left
    (fun found item ->
      match item with
      | WS.Impl (path, declaration) -> (
          match
            resolveWrittenType allowInternal types path
              (S.of_list (List.map fst declaration.WT.typeParams))
              declaration.WT.forType
          with
          | Ok (AST.TRecord (name, _) | AST.TSum (name, _)) -> S.add name found
          | _ -> found)
      | _ -> found)
    S.empty items

let aliasTo canonical range params =
  let identifier name = { WT.range; name } in
  let segments = String.split_on_char '.' canonical in
  let last = List.length segments - 1 in
  WT.TDAlias
    (WT.TCustom
       {
         WT.range;
         modules =
           List.filteri (fun i _ -> i < last) segments
           |> List.map (fun name -> (identifier name, range));
         typ = identifier (List.nth segments last);
         typeArgs =
           List.map (fun name -> WT.TVariable (range, range, (range, name))) params;
       })

(* Each local declaration whose content matches an earlier one becomes an
   alias of it. A type with trait implementations or bounds keeps its own
   identity, since its impls would otherwise collide. *)
let unify allowInternal (baseTypes : typeInventory) baseImplemented
    (localTypes : typeInventory) items =
  let types = M.fold M.add localTypes baseTypes in
  let implemented = S.union baseImplemented (implementedTypes allowInternal types items) in
  let declarations =
    M.fold
      (fun name (entry : typeEntry) acc ->
        if entry.kind = AliasKind || entry.bounds <> [] || compilerOnly name
           || keepsName name || S.mem name implemented
        then acc
        else
          match content allowInternal types entry with
          | Ok content -> (name, content) :: acc
          | Error _ -> acc)
      types []
    |> List.rev
  in
  let sameAs =
    List.fold_left
      (fun sameAs members ->
        (* Two declarations in one module are kept apart, as their author wrote them. *)
        let members =
          List.filter
            (fun name ->
              List.length (List.filter (fun other -> moduleOf other = moduleOf name) members) = 1)
            (List.sort compare members)
        in
        match
          List.filter (fun name -> M.mem name baseTypes && not (M.mem name localTypes)) members
          @ members
        with
        | [] -> sameAs
        | canonical :: _ ->
            List.fold_left
              (fun sameAs name ->
                if name <> canonical && M.mem name localTypes then M.add name canonical sameAs
                else sameAs)
              sameAs members)
      M.empty (groups declarations)
  in
  if M.is_empty sameAs then (localTypes, items)
  else
    let[@warning "-4"] items, aliases =
      List.fold_right
        (fun item (items, aliases) ->
          match item with
          | WS.Type (path, declaration) -> (
              let name = String.concat "." (path @ [ declaration.WT.name.WT.name ]) in
              match M.find_opt name sameAs with
              | Some canonical ->
                  let definition =
                    aliasTo canonical declaration.WT.range
                      (List.map fst declaration.WT.typeParams)
                  in
                  ( WS.Type (path, { declaration with WT.definition }) :: items,
                    M.add name definition aliases )
              | None -> (item :: items, aliases))
          | item -> (item :: items, aliases))
        items ([], M.empty)
    in
    let localTypes =
      M.mapi
        (fun name (entry : typeEntry) ->
          match (M.find_opt name sameAs, M.find_opt name aliases) with
          | Some canonical, Some definition ->
              { entry with kind = AliasKind; definition; sameAs = Some canonical }
          | _ -> entry)
        localTypes
    in
    (localTypes, items)
