(* convert_dialect.ml - Rewrite Dark sources from the earlier call syntax to current Dark syntax.

   convert_dialect syntax FILE...    calls, lambdas, patterns and function
                                     headers take their arguments in one paren list
   convert_dialect check BEFORE AFTER  both files parse to the same tree
   Each file is rewritten only when the new text parses as current syntax to
   the same tree as the old text did. *)
open Dark_compiler
module T = Tokenizer

let read path = In_channel.with_open_bin path In_channel.input_all
let write path text = Out_channel.with_open_bin path (fun o -> output_string o text)

(* One fingerprint per declaration, so a difference can be located. *)
let rec keys (declarations : WrittenTypes.declaration list) =
  List.concat_map
    (fun declaration ->
      match declaration with
      | WrittenTypes.DModule modul -> keys modul.WrittenTypes.declarations
      | _ ->
          [
            WrittenFormatter.syntaxKey
              { WrittenTypes.range = WrittenTypes.synthRange; declarations = [ declaration ]; exprsToEval = [] };
          ])
    declarations

let parsed text =
  Parser.parse text
  |> fun result ->
  match (result.ParserSupport.diagnostics, result.ParserSupport.parsed) with
  | [], Some (WrittenTypes.SourceFile tree) ->
      Ok (WrittenFormatter.syntaxKey tree, keys tree.WrittenTypes.declarations)
  | diagnostics, _ ->
      Error
        (String.concat "\n" (List.map (Parser.renderDiagnostic text) diagnostics))

(* Scalar offsets of each line start, matching the lexer's columns. *)
let offsets scalars =
  Array.to_list scalars
  |> List.mapi (fun index scalar -> (index, scalar))
  |> List.filter_map (fun (index, scalar) -> if scalar = 10 then Some (index + 1) else None)
  |> fun starts -> Array.of_list (0 :: starts)

let index lines (pos : T.pos) = lines.(pos.T.row) + pos.T.column

(* Replace [start, stop) spans, given in source order, by new text. *)
let splice scalars edits =
  let rec go at edits acc =
    match edits with
    | [] -> List.rev (Text.ofScalars (Array.sub scalars at (Array.length scalars - at)) :: acc)
    | (start, stop, text) :: rest ->
        go stop rest (text :: Text.ofScalars (Array.sub scalars at (start - at)) :: acc)
  in
  String.concat "" (go 0 edits [])

(* ---- call syntax: `f a b` -> `f(a, b)`, `fun a b ->` -> `fun(a, b) ->`,
   `let f (a: A) (b: B) : R =` -> `let f(a: A, b: B): R =` ---- *)
module W = WrittenTypes

exception Manual of string * T.pos

type ctx = {
  scalars : int array;
  lines : int array;
  tokens : Lexer.spannedToken array;
}

let at ctx pos = index ctx.lines pos
let startOf ctx i = at ctx ctx.tokens.(i).Lexer.range.T.start
let endOf ctx i = at ctx ctx.tokens.(i).Lexer.range.T.end_
let tokenIs ctx i token = i >= 0 && i < Array.length ctx.tokens && ctx.tokens.(i).Lexer.token = token

(* first token starting at or after offset [i] *)
let nextTok ctx i =
  let rec go lo hi = if lo >= hi then lo else
    let mid = (lo + hi) / 2 in
    if startOf ctx mid < i then go (mid + 1) hi else go lo mid in
  go 0 (Array.length ctx.tokens)

(* last token starting before offset [i] *)
let prevTok ctx i = nextTok ctx i - 1

let span ctx (range : T.tokenRange) = (at ctx range.T.start, at ctx range.T.end_)

let manual ctx offset message =
  let row = ref 0 in
  Array.iteri (fun r start -> if start <= offset then row := r) ctx.lines;
  raise (Manual (message, { T.row = !row; column = offset - ctx.lines.(!row) }))

(* Grouping parentheses around an argument. *)
let rec outer ctx (s, e) =
  let p = prevTok ctx s and n = nextTok ctx e in
  if tokenIs ctx p T.TLParen && tokenIs ctx n T.TRParen then outer ctx (startOf ctx p, endOf ctx n)
  else (s, e)

let slice ctx a b =
  if b < a then manual ctx b "an argument out of source order"
  else Text.ofScalars (Array.sub ctx.scalars a (b - a))

let gapOnly ctx a b allowed =
  for i = a to b - 1 do
    let c = ctx.scalars.(i) in
    if not (c = 32 || c = 10 || List.mem c allowed) then manual ctx a "a comment or other text between arguments"
  done

let newlineSuffix ctx a b =
  let text = slice ctx a b in
  match String.rindex_opt text '\n' with
  | None -> None
  | Some i ->
      Some (String.sub text i (String.length text - i)
            |> String.to_seq |> Seq.filter (fun c -> c <> '(' && c <> ')') |> String.of_seq)

let paren = Char.code '(' and close = Char.code ')'

(* Edits for one application with arguments (span, keeps its parentheses when not last, is unit). *)
let call ctx args =
  match args with
  | [] -> []
  | ((s1, e1), _, _) :: _ ->
      let n = List.length args in
      let (_, en), _, _ = List.nth args (n - 1) in
      let p = prevTok ctx s1 in
      if n >= 2 && tokenIs ctx p T.TLParen && tokenIs ctx (nextTok ctx e1) T.TComma then begin
        (* already a paren list; a spaced one loses its space *)
        if not (tokenIs ctx (nextTok ctx en) T.TRParen) then manual ctx s1 "a paren list followed by more arguments";
        let before = endOf ctx (p - 1) and opening = startOf ctx p in
        if before < opening then (gapOnly ctx before opening []; [ (before, opening, "") ]) else []
      end
      else
        let outers = List.map (fun (inner, _, _) -> outer ctx inner) args in
        let contents =
          List.mapi
            (fun k ((inner, keep, _), o) -> if keep && k < n - 1 then o else inner)
            (List.combine args outers)
        in
        let o1s, _ = List.hd outers in
        let head = endOf ctx (prevTok ctx o1s) in
        match args with
        | [ (_, _, true) ] ->
            let _, oe = List.hd outers in
            gapOnly ctx head (fst (List.hd contents)) [ paren ];
            [ (head, oe, "()") ]
        | _ ->
            let c1s, _ = List.hd contents in
            gapOnly ctx head c1s [ paren ];
            let first = (head, c1s, "(" ^ Option.value ~default:"" (newlineSuffix ctx head c1s)) in
            let rec between = function
              | (_, ae) :: (((bs, _) :: _) as rest) ->
                  gapOnly ctx ae bs [ paren; close ];
                  (ae, bs, "," ^ Option.value ~default:" " (newlineSuffix ctx ae bs)) :: between rest
              | _ -> []
            in
            let _, cne = List.nth contents (n - 1) and _, one = List.nth outers (n - 1) in
            gapOnly ctx cne one [ close ];
            (first :: between contents) @ [ (cne, one, ")") ]

let keepsParens = function
  | W.ELambda _ | W.EIf _ | W.EMatch _ | W.ELet _ | W.EStatement _ | W.EPipe _ -> true
  | _ -> false

let isUnit = function W.EUnit _ -> true | _ -> false

let subexprs = function
  | W.EInfix (_, _, a, b) | W.EStatement (_, a, b) | W.ELet (_, _, a, b, _, _) -> [ a; b ]
  | W.EApply (_, fn, _, args) -> fn :: args
  | W.EList (_, items, _, _) -> List.map fst items
  | W.ETuple (_, a, _, b, rest, _, _) -> a :: b :: List.map snd rest
  | W.EIf (_, c, t, f, _, _, _) -> c :: t :: Option.to_list f
  | W.EUnwrap (_, e, _) | W.ERecordFieldAccess (_, e, _, _) | W.ELambda (_, _, e, _, _) -> [ e ]
  | W.ERecord (_, _, fields, _, _) -> List.map (fun (_, _, e) -> e) fields
  | W.EDict (_, entries, _, _, _) -> List.concat_map (fun (_, k, _, v) -> [ k; v ]) entries
  | W.ERecordUpdate (_, e, fields, _, _, _) -> e :: List.map (fun (_, _, e) -> e) fields
  | W.EEnum (_, _, _, fields, _) -> fields
  | W.EMatch (_, e, cases, _, _) ->
      e
      :: List.concat_map
           (fun (case : W.matchCase) ->
             Option.to_list (Option.map snd case.W.whenCondition) @ [ case.W.rhs ])
           cases
  | W.EPipe (_, e, pipes) ->
      e
      :: List.concat_map
           (fun (_, p) ->
             match p with
             | W.EPipeInfix (_, _, e) | W.EPipeLambda (_, _, e, _, _) -> [ e ]
             | W.EPipeEnum (_, _, _, es, _) | W.EPipeFnCall (_, _, _, es) -> es
             | W.EPipeVariableOrFnCall _ -> [])
           pipes
  | _ -> []

(* A node's range stops at its last child's range, inside any parentheses
   that group that child; the text the node covers includes them. *)
(* ... and a paren list's closing paren after its last element. *)
let balanced ctx (s, e) =
  let rec opened i depth =
    if i >= Array.length ctx.tokens || startOf ctx i >= e then depth
    else
      opened (i + 1)
        (match ctx.tokens.(i).Lexer.token with
         | T.TLParen -> depth + 1
         | T.TRParen -> depth - 1
         | _ -> depth)
  in
  let rec close e depth =
    if depth <= 0 then e
    else
      let n = nextTok ctx e in
      if tokenIs ctx n T.TRParen then close (endOf ctx n) (depth - 1)
      else manual ctx e "an unbalanced argument"
  in
  (s, close e (opened (nextTok ctx s) 0))

let rec extent ctx e =
  List.fold_left
    (fun (s, e) child ->
      let cs, ce = outer ctx (extent ctx child) in
      (min s cs, max e ce))
    (span ctx (W.exprRange e)) (subexprs e)
  |> balanced ctx

let subpatterns = function
  | W.MPEnum (_, _, ps) | W.MPOr (_, ps) -> ps
  | W.MPTuple (_, a, _, b, rest, _, _) -> a :: b :: List.map snd rest
  | W.MPList (_, items, _, _) -> List.map fst items
  | W.MPListCons (_, a, b, _) -> [ a; b ]
  | _ -> []

let rec patternExtent ctx p =
  List.fold_left
    (fun (s, e) child ->
      let cs, ce = outer ctx (patternExtent ctx child) in
      (min s cs, max e ce))
    (span ctx (W.mpRange p)) (subpatterns p)
  |> balanced ctx

let exprArg ctx e = (extent ctx e, keepsParens e, isUnit e)

(* A call written by the user, not one the parser made for `-x`, `a @ b` or a
   spread, to a callee the paren-list syntax accepts: a field such as
   `socket.send peer` keeps its spaced arguments. *)
let written ctx = function
  | W.EFnName (range, name) ->
      let a, b = span ctx range in
      String.ends_with ~suffix:name.W.fn.W.name (slice ctx a b)
  | W.EVariable _ | W.ELambda _ -> true
  | _ -> false

let lambda ctx keyword arrow patterns patternRange =
  let k = nextTok ctx (at ctx keyword.T.start) in
  if not (tokenIs ctx k T.TFun) then []
  else if tokenIs ctx (k + 1) T.TLParen && startOf ctx (k + 1) = endOf ctx k then []
  else
    match patterns with
    | [] | [ _ ] -> []
    | _ ->
        let a = endOf ctx k and b = at ctx arrow.T.start in
        let texts = List.map (fun p -> let s, e = span ctx (patternRange p) in slice ctx s e) patterns in
        String.iter (fun c -> if c = '/' then manual ctx a "a comment in lambda parameters") (slice ctx a b);
        [ (a, b, "(" ^ String.concat ", " texts ^ ") ") ]

let letPatternRange = function
  | W.LPUnit r | W.LPVariable (r, _) | W.LPWildcard r | W.LPTuple (r, _, _, _, _, _, _) -> r

(* Parameter groups from the token [first] (a `(`) up to the return colon or `=`. *)
let header ctx first =
  let rec matching i depth =
    if i >= Array.length ctx.tokens then manual ctx (startOf ctx first) "unclosed parameter list"
    else match ctx.tokens.(i).Lexer.token with
      | T.TLParen -> matching (i + 1) (depth + 1)
      | T.TRParen -> if depth = 1 then i else matching (i + 1) (depth - 1)
      | _ -> matching (i + 1) depth
  in
  let rec groups i acc =
    if tokenIs ctx i T.TLParen then
      let j = matching i 0 in
      groups (j + 1) ((i, j) :: acc)
    else (List.rev acc, i)
  in
  let found, after = groups first [] in
  let adjacent = startOf ctx first = endOf ctx (first - 1) in
  match found with
  | [ _ ] when adjacent && not (tokenIs ctx after T.TColon && startOf ctx after > endOf ctx (after - 1)) -> []
  | _ ->
      let colon = tokenIs ctx after T.TColon in
      let stop = if colon then startOf ctx (after + 1) else startOf ctx after in
      let last = if colon then after + 1 else after in
      for i = first to last do
        let t = ctx.tokens.(i) in
        if t.Lexer.leadingTrivia <> [] || t.Lexer.docComment <> None then
          manual ctx (startOf ctx i) "a comment inside a function header"
      done;
      let inner (i, j) = String.trim (slice ctx (endOf ctx i) (startOf ctx j)) in
      let texts = List.filter (fun t -> t <> "") (List.map inner found) in
      if List.length texts > 0 && List.length texts < List.length found then
        manual ctx (startOf ctx first) "unit and named parameters together";
      [ (endOf ctx (first - 1), stop, "(" ^ String.concat ", " texts ^ ")" ^ if colon then ": " else " ") ]

let rec expr ctx e =
  let go = expr ctx in
  match e with
  | W.EUnit _ | W.EBool _ | W.EInt _ | W.EInt64 _ | W.EInt8 _ | W.EUInt8 _ | W.EInt16 _
  | W.EUInt16 _ | W.EInt32 _ | W.EUInt32 _ | W.EUInt64 _ | W.EInt128 _ | W.EUInt128 _
  | W.EFloat _ | W.EChar _ | W.EVariable _ | W.EFnName _ | W.EError _ -> []
  | W.EString _ -> []
  | W.EInfix (_, _, a, b) | W.EStatement (_, a, b) -> go a @ go b
  | W.ELet (_, W.LPVariable _, W.ELambda (lambdaRange, _, body, keyword, _), rest, _, _)
    when keyword.T.start = keyword.T.end_ ->
      header ctx (nextTok ctx (at ctx lambdaRange.T.start)) @ go body @ go rest
  | W.ELet (_, _, value, body, _, _) -> go value @ go body
  | W.EApply (_, fn, _, args) ->
      go fn @ List.concat_map go args
      @ if written ctx fn then call ctx (List.map (exprArg ctx) args) else []
  | W.EList (_, items, _, _) -> List.concat_map (fun (e, _) -> go e) items
  | W.ETuple (_, a, _, b, rest, _, _) -> go a @ go b @ List.concat_map (fun (_, e) -> go e) rest
  | W.EIf (_, c, t, f, _, _, _) -> go c @ go t @ (match f with Some f -> go f | None -> [])
  | W.EUnwrap (_, e, _) | W.ERecordFieldAccess (_, e, _, _) -> go e
  | W.ELambda (_, patterns, body, keyword, arrow) ->
      lambda ctx keyword arrow patterns letPatternRange @ go body
  | W.ERecord (_, _, fields, _, _) -> List.concat_map (fun (_, _, e) -> go e) fields
  | W.EDict (_, entries, _, _, _) -> List.concat_map (fun (_, k, _, v) -> go k @ go v) entries
  | W.ERecordUpdate (_, e, fields, _, _, _) -> go e @ List.concat_map (fun (_, _, e) -> go e) fields
  | W.EEnum (_, _, _, fields, _) -> List.concat_map go fields @ call ctx (List.map (exprArg ctx) fields)
  | W.EMatch (_, e, cases, _, _) ->
      go e
      @ List.concat_map
          (fun (case : W.matchCase) ->
            pattern ctx case.W.pat
            @ (match case.W.whenCondition with Some (_, c) -> go c | None -> [])
            @ go case.W.rhs)
          cases
  | W.EPipe (_, e, pipes) -> go e @ List.concat_map (fun (_, p) -> pipe ctx p) pipes

and pipe ctx p =
  let go = expr ctx in
  match p with
  | W.EPipeInfix (_, _, e) -> go e
  | W.EPipeLambda (_, patterns, body, keyword, arrow) ->
      lambda ctx keyword arrow patterns letPatternRange @ go body
  | W.EPipeEnum (_, _, _, fields, _) -> List.concat_map go fields @ call ctx (List.map (exprArg ctx) fields)
  | W.EPipeFnCall (_, _, _, args) -> List.concat_map go args @ call ctx (List.map (exprArg ctx) args)
  | W.EPipeVariableOrFnCall _ -> []

and pattern ctx p =
  match p with
  | W.MPEnum (_, _, patterns) ->
      List.concat_map (pattern ctx) patterns
      @ call ctx (List.map (fun p -> (patternExtent ctx p, false, (match p with W.MPUnit _ -> true | _ -> false))) patterns)
  | W.MPTuple (_, a, _, b, rest, _, _) ->
      pattern ctx a @ pattern ctx b @ List.concat_map (fun (_, p) -> pattern ctx p) rest
  | W.MPList (_, items, _, _) -> List.concat_map (fun (p, _) -> pattern ctx p) items
  | W.MPListCons (_, a, b, _) -> pattern ctx a @ pattern ctx b
  | W.MPOr (_, patterns) -> List.concat_map (pattern ctx) patterns
  | _ -> []

let firstParam ctx = function
  | W.FPUnit range :: _ -> nextTok ctx (at ctx range.T.start)
  | W.FPNormal (_, _, _, opening, _, _, _) :: _ -> nextTok ctx (at ctx opening.T.start)
  | [] -> -1

let fn ctx (decl : W.fnDecl) = header ctx (firstParam ctx decl.W.parameters) @ expr ctx decl.W.body

let rec declaration ctx = function
  | W.DFunction decl -> fn ctx decl
  | W.DValue value -> expr ctx value.W.body
  | W.DModule modul -> List.concat_map (declaration ctx) modul.W.declarations
  | W.DType _ -> []
  | W.DTrait trait_ ->
      List.concat_map
        (fun (m : W.traitMethodDecl) ->
          header ctx (firstParam ctx m.W.parameters)
          @ match m.W.body with Some body -> expr ctx body | None -> [])
        trait_.W.methods
  | W.DImpl impl ->
      List.concat_map
        (function W.IMethod decl -> fn ctx decl | W.IAlias value -> expr ctx value.W.body)
        impl.W.members
  | W.DExpr e -> expr ctx e
  | W.DAssertion a -> expr ctx a.W.actual @ (match a.W.expected with W.TEExpr e -> expr ctx e | W.TEError _ -> [])
  | W.DTest test -> expr ctx test.W.body

let syntax text =
  match Lexer.tokenize text with
  | Error message -> Error message
  | Ok (tokens, _) -> (
      match (Parser.parse text).ParserSupport.parsed with
      | None -> Error "no syntax tree"
      | Some (W.SourceFile tree) -> (
          let scalars = Text.scalars text in
          let ctx = { scalars; lines = offsets scalars; tokens = Array.of_list tokens } in
          try
            let edits =
              List.concat_map (declaration ctx) tree.W.declarations
              @ List.concat_map (expr ctx) tree.W.exprsToEval
              |> List.filter (fun (a, b, t) -> slice ctx a b <> t)
              |> List.stable_sort (fun (a, b, _) (c, d, _) -> compare (a, b) (c, d))
            in
            let rec overlapping = function
              | (_, b, _) :: ((c, _, _) :: _ as rest) -> b > c || overlapping rest
              | _ -> false
            in
            if overlapping edits then Error "overlapping edits"
            else Ok (splice scalars edits, List.length edits)
          with Manual (message, pos) ->
            Error (Printf.sprintf "line %d: %s" (pos.T.row + 1) message)))

let convert pass path =
  let text = read path in
  match parsed text with
  | Error message -> Error (path ^ ": does not parse in the earlier dialect\n" ^ message)
  | Ok before -> (
      match pass text with
      | Error message -> Error (path ^ ": " ^ message)
      | Ok (fresh, count) -> (
          match parsed fresh with
          | Ok (after, _) when after = fst before ->
              if fresh <> text then write path fresh;
              Ok count
          | result ->
              if Sys.getenv_opt "DIALECT_KEEP" <> None then write (path ^ ".new") fresh;
              Error
                (path ^ ": the rewritten file "
                ^ match result with
                  | Ok (_, keys) ->
                      let rec first i = function
                        | a :: rest, b :: rest' -> if a = b then first (i + 1) (rest, rest') else i
                        | _ -> i
                      in
                      Printf.sprintf "parses to a different tree from declaration %d"
                        (first 1 (snd before, keys))
                  | Error message -> "does not parse\n" ^ message)))

let () =
  match Array.to_list Sys.argv with
  | _ :: ("syntax" as pass) :: paths ->
      let results = List.map (convert syntax) paths in
      let failures = List.filter_map (function Error m -> Some m | Ok _ -> None) results in
      let count = List.fold_left (fun n -> function Ok c -> n + c | Error _ -> n) 0 results in
      List.iter prerr_endline failures;
      Printf.printf "%d %s edits in %d files; %d files left as they were\n" count pass
        (List.length paths - List.length failures) (List.length failures);
      if failures <> [] then exit 1
  | [ _; "check"; before; after ] -> (
      match (parsed (read before), parsed (read after)) with
      | Ok (a, _), Ok (b, _) when a = b -> ()
      | Ok _, Ok _ -> prerr_endline (after ^ ": parses to a different tree"); exit 1
      | _, Error message | Error message, _ -> prerr_endline message; exit 1)
  | _ ->
      prerr_endline "Usage: convert_dialect syntax FILE... | check BEFORE AFTER";
      exit 2
