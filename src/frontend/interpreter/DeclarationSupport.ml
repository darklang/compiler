(* DeclarationSupport.ml - Preserve parameter docs, delimiter ranges, and effect diagnostics. *)
[@@@warning "-4"]

open Tokenizer
open ParserSupport
module WT = WrittenTypes

(*
   a function parameter `(name: Type)` or `()`
   `_` names a parameter you don't intend to use. It's also what `()` is stored as, so accepting it
   here is what makes `(_: Unit)` and `()` both parse to the same thing and either form round-trip.
   A `///` for a parameter attaches to whichever token follows it: the `(` when the comment is
   written above the whole parameter, the NAME when it is written just inside the paren. Both
   spellings occur, so both are read.
*)
let parseParamName state index =
  match tok state index with
  | TIdent name -> { WT.range = rng state index; name }
  | TUnderscore -> { WT.range = rng state index; name = "_" }
  | _ ->
      errExpected state index "a parameter name";
      { WT.range = rng state index; name = "_" }

let parseParamColon state index =
  if tok state index = TColon then (rng state index, index + 1)
  else begin
    errExpected state index "':'";
    (zeroWidthAtEnd (rng state index), index)
  end

(*
   Since darklang/dark@74dca94 one paren may list every parameter,
   `(a: Int64, b: Int64)`, as a call lists its arguments. The earlier
   one-paren-per-parameter form, `(a: Int64) (b: Int64)`, still parses. Each
   parameter of a shared paren keeps the paren as its delimiters.
*)
let parseParam state index =
  let opening = rng state index in
  if tok state (index + 1) = TRParen then
    ([ WT.FPUnit (span opening (rng state (index + 1))) ], index + 2)
  else
    let atParen = docOf state index in
    let one nameIndex =
      let name = parseParamName state nameIndex in
      let colon, next = parseParamColon state (nameIndex + 1) in
      let typ, next = TypeParser.parseTypeRef state next in
      let description =
        if atParen <> "" && nameIndex = index + 1 then atParen
        else docOf state nameIndex
      in
      ((name, typ, colon, description), next)
    in
    let first, next = one (index + 1) in
    let rest = RevBuffer.create () and stop = ref next in
    while tok state !stop = TComma && tok state (!stop + 1) <> TRParen do
      let parameter, after = one (!stop + 1) in
      RevBuffer.add rest parameter;
      stop := after
    done;
    if tok state !stop = TComma then incr stop;
    let closing, after =
      if tok state !stop = TRParen then (rng state !stop, !stop + 1)
      else begin
        errUnclosed state !stop ")" "(" opening;
        (zeroWidthAtEnd (rng state !stop), !stop)
      end
    in
    let parameters =
      List.map
        (fun (name, typ, colon, description) ->
          WT.FPNormal
            ( span opening closing,
              name,
              typ,
              opening,
              colon,
              closing,
              description ))
        (first :: RevBuffer.toList rest)
    in
    (parameters, after)

(* Shared declaration parameter grammar and blank-name validation. *)
let parseParams state index =
  let parameters = RevBuffer.create ()
  and stop = ref index
  and more = ref true in
  while !more && tok state !stop = TLParen do
    let group, after = parseParam state !stop in
    List.iter (RevBuffer.add parameters) group;
    if after = !stop then more := false else stop := after
  done;
  List.iter
    (function
      | WT.FPNormal (_, name, _, _, _, _, _) when name.WT.name = "" ->
          state.diagnostics :=
            {
              code = DiagnosticCode.pattern;
              severity = DiagError;
              range = name.WT.range;
              message =
                "Blank parameter '___' is not allowed in a package function";
              related = [];
              hint =
                Some "use () for a unit parameter or give the parameter a name";
            }
            :: !(state.diagnostics)
      | _ -> ())
    (RevBuffer.toList parameters);
  (RevBuffer.toList parameters, !stop)

(*
   A declaration-scope function (`let f (p: T) … : R = body`) or value
   (`val x = body`). Legacy module-level `let x = body` also comes through here
   to retain a recovery DValue beside its focused diagnostic.
   `:{Http, Clock}` immediately after a declaration's return colon. Absent
   means no ceiling; `{}` means effect-free. Unknown names are diagnostics,
   not wildcards: the row fails closed.
*)
let parseEffectRow state index =
  if tok state index <> TLBrace then (None, index)
  else
    let names = RevBuffer.create () and stop = ref (index + 1) in
    let more = ref (tok state !stop <> TRBrace) in
    while !more do
      (match tok state !stop with
      | TIdent name ->
          if not (List.mem name effectCaseNames) then
            err state DiagnosticCode.effect_ !stop
              ("unknown effect '" ^ name ^ "'; effects are "
              ^ String.concat ", " effectCaseNames);
          RevBuffer.add names { WT.range = rng state !stop; name };
          incr stop;
          if tok state !stop = TComma then incr stop
          else if tok state !stop <> TRBrace then begin
            errExpected state !stop "',' or '}' in the effect row";
            more := false
          end
      | _ ->
          errExpected state !stop "an effect name";
          more := false);
      if tok state !stop = TRBrace then more := false
    done;
    ( Some (RevBuffer.toList names),
      if tok state !stop = TRBrace then !stop + 1 else !stop )
