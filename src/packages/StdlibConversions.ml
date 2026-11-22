(* StdlibConversions.ml - Where the interpreter's stdlib and the compiler's differ. *)

(* Where the interpreter's stdlib and the compiler's still disagree on a fn's
   numeric types, a hosted call converts the arguments it passes and the
   result it gets back, so the code around the call keeps the types it was
   written for. Widen takes an interpreter value to an Int, Narrow an Int back
   to the interpreter's type, through Options, Results, Lists and tuples. Only
   Blob, which the interpreter replaced by Bytes, still differs: its length and
   slice take Int. *)
type conversion =
  | Widen of string
  | Narrow of string
  | OptionOf of conversion
  | ResultOf of conversion
  | ErrorOf of conversion
  | Both of conversion * conversion
  | ListOf of conversion
  | TupleOf of conversion option list

let stdlibConversions =
  [
    ("Darklang.Stdlib.Blob.length", [], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Blob.slice", [ (1, Widen "Int64"); (2, Widen "Int64") ], None);
  ]

(* A builtin renders as the compiler's fn (`Stdlib.Blob.length`), a hosted
   call as the interpreter's (`Darklang.Stdlib.String.length`). *)
let conversionsOf name =
  List.find_opt
    (fun (candidate, _, _) -> candidate = name || candidate = "Darklang." ^ name)
    stdlibConversions

let argumentConversions name =
  match conversionsOf name with
  | Some (_, arguments, _) -> arguments
  | None -> []

let resultConversion name =
  match conversionsOf name with
  | Some (_, _, result) -> result
  | None -> None

