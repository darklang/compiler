(* WrittenParsing.mli - Executable source parsing with rendered diagnostics. *)
val parse :
  ?adaptStdlib:bool ->
  Validation.mode -> string -> (Validation.validatedSourceFile, string) result
