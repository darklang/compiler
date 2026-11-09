(* Parser.mli - Recoverable syntax, validated execution input, and diagnostic rendering. *)
val parseExpr : ParserSupport.parserState -> int -> WrittenTypes.expr * int

val parseTokensAt :
  int ->
  ParserSupport.ItemScope.t ->
  Lexer.spannedToken array ->
  ParserSupport.parseResult

val parseTokens : Lexer.spannedToken array -> ParserSupport.parseResult
val parse : string -> ParserSupport.parseResult

(* `--syntax=current`: scripts and tests read a bare integer as Int64, as the
   current interpreter does; the embedded library keeps the pinned rules. *)
val currentSyntax : bool ref

val parseFor :
  ?adaptStdlib:bool ->
  Validation.mode ->
  string ->
  (Validation.validatedSourceFile, ParserSupport.diagnostic list) result

val renderDiagnostic : string -> ParserSupport.diagnostic -> string
