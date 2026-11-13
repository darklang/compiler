(* ValueRendering.mli - Interpreter-compatible result rendering.
   Builds monomorphic Dark functions for native eval and diagnostic boundaries. *)
(* `--syntax=current` sets this: render as `dark eval` prints. *)
val evalDisplay : bool ref

val rewriteProgram :
  Types.indexedTypeRegistry ->
  Types.indexedSumTypeRegistry ->
  AST.semanticType ->
  CheckedAST.program ->
  CheckedAST.program

val rewriteDiagnosticRenderers :
  Types.indexedTypeRegistry ->
  Types.indexedSumTypeRegistry ->
  CheckedAST.program ->
  CheckedAST.program
