(* convert_dialect.ml - Rewrite compiler packages from the earlier dialect to current Dark syntax.

   convert_dialect literals FILE...  bare integers become `z`, `L` goes, other
                                     widths take their bit-count suffix
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

let parsed ~current text =
  Parser.withInterpreterSource current (fun () -> Parser.parse text)
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

let literal (previous : Lexer.spannedToken option) (token : Lexer.spannedToken) =
  let text = token.Lexer.text in
  let ends suffix = String.ends_with ~suffix text in
  let swap old fresh = Some (String.sub text 0 (String.length text - String.length old) ^ fresh) in
  let fieldAccess =
    match previous with
    | Some { Lexer.token = T.TDot; range; _ } -> range.T.end_ = token.Lexer.range.T.start
    | _ -> false
  in
  match token.Lexer.token with
  | _ when fieldAccess -> None
  | T.TInt _ when not (ends "z") -> Some (text ^ "z")
  | T.TInt64 _ when ends "L" -> swap "L" ""
  | T.TInt8 _ when ends "y" -> swap "y" "s8"
  | T.TUInt8 _ when ends "uy" -> swap "uy" "u8"
  | T.TInt16 _ when ends "s" -> swap "s" "s16"
  | T.TUInt16 _ when ends "us" -> swap "us" "u16"
  | T.TInt32 _ when ends "l" -> swap "l" "s32"
  | T.TUInt32 _ when ends "ul" -> swap "ul" "u32"
  | T.TUInt64 _ when ends "UL" -> swap "UL" "u64"
  | T.TInt128 _ when ends "Q" -> swap "Q" "s128"
  | T.TUInt128 _ when ends "Z" -> swap "Z" "u128"
  | _ -> None

let literals text =
  match Lexer.tokenize text with
  | Error message -> Error message
  | Ok (tokens, _) ->
      let scalars = Text.scalars text in
      let lines = offsets scalars in
      let edits =
        List.fold_left
          (fun (previous, edits) (token : Lexer.spannedToken) ->
            ( Some token,
              match literal previous token with
              | Some fresh ->
                  (index lines token.Lexer.range.T.start, index lines token.Lexer.range.T.end_, fresh)
                  :: edits
              | None -> edits ))
          (None, []) tokens
        |> snd |> List.rev
      in
      Ok (splice scalars edits, List.length edits)


let convert ~before pass path =
  let text = read path in
  match parsed ~current:before text with
  | Error message -> Error (path ^ ": does not parse in the earlier dialect\n" ^ message)
  | Ok before -> (
      match pass text with
      | Error message -> Error (path ^ ": " ^ message)
      | Ok (fresh, count) -> (
          match parsed ~current:true fresh with
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
  | _ :: ("literals" as pass) :: paths ->
      let results = List.map (convert ~before:false literals) paths in
      let failures = List.filter_map (function Error m -> Some m | Ok _ -> None) results in
      let count = List.fold_left (fun n -> function Ok c -> n + c | Error _ -> n) 0 results in
      List.iter prerr_endline failures;
      Printf.printf "%d %s edits in %d files; %d files left as they were\n" count pass
        (List.length paths - List.length failures) (List.length failures);
      if failures <> [] then exit 1
  | _ ->
      prerr_endline "Usage: convert_dialect literals FILE...";
      exit 2
