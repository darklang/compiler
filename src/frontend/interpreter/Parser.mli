(* Parser.mli - Recoverable syntax, validated execution input, and diagnostic rendering. *)
val parseExpr : ParserSupport.parserState -> int -> WrittenTypes.expr * int

val parseTokensAt :
  int ->
  ParserSupport.ItemScope.t ->
  Lexer.spannedToken array ->
  ParserSupport.parseResult

val parseTokens : Lexer.spannedToken array -> ParserSupport.parseResult
val parse : string -> ParserSupport.parseResult

(* Read the sources parsed by [f] as interpreter sources at the pin: a bare
   integer is an Int64. *)
val withInterpreterSource : bool -> (unit -> 'a) -> 'a

val parseFor :
  ?adaptStdlib:bool ->
  Validation.mode ->
  string ->
  (Validation.validatedSourceFile, ParserSupport.diagnostic list) result

val renderDiagnostic : string -> ParserSupport.diagnostic -> string
