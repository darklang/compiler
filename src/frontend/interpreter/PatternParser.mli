(* PatternParser.mli - Original match-pattern precedence and recovery rules. *)
val parseMatchPattern :
  ParserSupport.parserState -> int -> WrittenTypes.matchPattern * int

val parsePatternOr :
  ParserSupport.parserState -> int -> WrittenTypes.matchPattern * int

val parsePatternBase :
  ParserSupport.parserState -> int -> WrittenTypes.matchPattern * int

(* Read any `as name` after a pattern, refusing it when compiled. *)
val nestedAs :
  ParserSupport.parserState ->
  WrittenTypes.matchPattern * int ->
  WrittenTypes.matchPattern * int
