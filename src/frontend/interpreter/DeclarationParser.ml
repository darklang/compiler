(* DeclarationParser.ml - Declaration annotations, parameter diagnostics, and body scopes. *)
[@@@warning "-4"]

open Tokenizer
open ParserSupport
module WT = WrittenTypes

let parseDecl parseBlock state index =
  let keyword = rng state index and nameIndex = index + 1 in
  let name =
    match tok state nameIndex with
    | TIdent name -> { WT.range = rng state nameIndex; name }
    | _ ->
        errExpected state nameIndex "a declaration name";
        { WT.range = rng state nameIndex; name = "_" }
  in
  let typeParams, bounds, next =
    TypeParameterParser.parseTypeParams state (nameIndex + 1)
  in
  if tok state next = TLParen then
    let parameters, afterParams = DeclarationSupport.parseParams state next in
    let colon, next =
      if tok state afterParams = TColon then
        (rng state afterParams, afterParams + 1)
      else begin
        errExpected state afterParams "':' before the return type";
        (zeroWidthAtEnd (rng state afterParams), afterParams)
      end
    in
    let effects, next = DeclarationSupport.parseEffectRow state next in
    let returnType, next = TypeParser.parseTypeRef state next in
    let equals, next =
      if tok state next = TEquals then (rng state next, next + 1)
      else begin
        errExpected state next "'='";
        (zeroWidthAtEnd (rng state next), next)
      end
    in
    let body, after = parseBlock state next in
    ( WT.DFunction
        {
          WT.range = span keyword (WT.exprRange body);
          name;
          typeParams;
          bounds;
          parameters;
          effects;
          returnType;
          body;
          keywordLet = keyword;
          symbolColon = colon;
          symbolEquals = equals;
          description = docOf state index;
        },
      after )
  else
    (* `val limit: Int64 = 6`: the type is optional, and the value's own type stands. *)
    let next =
      if tok state next = TColon then
        snd (TypeParser.parseTypeRef state (next + 1))
      else next
    in
    let equals, next =
      if tok state next = TEquals then (rng state next, next + 1)
      else begin
        errExpected state next "'='";
        (zeroWidthAtEnd (rng state next), next)
      end
    in
    let body, after = parseBlock state next in
    ( WT.DValue
        {
          WT.range = span keyword (WT.exprRange body);
          name;
          body;
          keywordVal = keyword;
          symbolEquals = equals;
          description = docOf state index;
        },
      after )

(* `extern name(p: T, ...): R from "library"`: a fn whose body is the native call it names.
   The symbol is the fn's name unless a second string gives it: `from "library" "symbol"`. *)
let parseExternDecl state index =
  let keyword = rng state index in
  let name =
    match tok state (index + 1) with
    | TIdent name -> { WT.range = rng state (index + 1); name }
    | _ ->
        errExpected state (index + 1) "a declaration name";
        { WT.range = rng state (index + 1); name = "_" }
  in
  let parameters, afterParams =
    DeclarationSupport.parseParams state (index + 2)
  in
  let colon, next =
    if tok state afterParams = TColon then
      (rng state afterParams, afterParams + 1)
    else begin
      errExpected state afterParams "':' before the return type";
      (zeroWidthAtEnd (rng state afterParams), afterParams)
    end
  in
  let returnType, afterReturn = TypeParser.parseTypeRef state next in
  let library, symbol, after =
    match
      ( tok state afterReturn,
        tok state (afterReturn + 1),
        tok state (afterReturn + 2) )
    with
    | TIdent "from", TStringLit library, TStringLit symbol
      when (rng state (afterReturn + 2)).start.row
           = (rng state (afterReturn + 1)).end_.row ->
        (library, symbol, afterReturn + 3)
    | TIdent "from", TStringLit library, _ ->
        (library, name.WT.name, afterReturn + 2)
    | _ ->
        errExpected state afterReturn "'from \"library\"'";
        ("", name.WT.name, afterReturn)
  in
  let last = rng state (max afterReturn (after - 1)) in
  ( WT.DFunction
      {
        WT.range = span keyword last;
        name;
        typeParams = [];
        bounds = [];
        parameters;
        effects = None;
        returnType;
        body = WT.EExtern (span (rng state afterReturn) last, library, symbol);
        keywordLet = keyword;
        symbolColon = colon;
        symbolEquals = zeroWidthAtEnd (rng state (afterReturn - 1));
        description = docOf state index;
      },
    after )
