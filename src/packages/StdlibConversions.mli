(* StdlibConversions.mli - Where the interpreter's stdlib and the compiler's differ. *)

(* How a value crosses from the interpreter's numeric type to the compiler's
   (Widen) or back (Narrow), through containers. *)
type conversion =
  | Widen of string
  | Narrow of string
  | OptionOf of conversion
  | ResultOf of conversion
  | ErrorOf of conversion
  | Both of conversion * conversion
  | ListOf of conversion
  | TupleOf of conversion option list

val stdlibConversions : (string * (int * conversion) list * conversion option) list
val argumentConversions : string -> (int * conversion) list
val resultConversion : string -> conversion option
