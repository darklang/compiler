(* StdlibConversions.ml - Where the interpreter's stdlib and the compiler's differ. *)

(* Where the interpreter's stdlib and the compiler's disagree on a fn's
   numeric types (an Int64 index where the compiler takes an Int, a UInt8 where
   it answers an Int), a hosted call converts the arguments it passes and the
   result it gets back, so the code around the call keeps the types it was
   written for. Widen takes an interpreter value to an Int, Narrow an Int back
   to the interpreter's type, through Options, Results, Lists and tuples. What
   remains differs still: the CLI file, process and terminal fns the compiler
   implements natively keep Int counts and sizes, and Blob, which the
   interpreter replaced by Bytes, keeps Int lengths. *)
type conversion =
  | Widen of string
  | Narrow of string
  | OptionOf of conversion
  | ResultOf of conversion
  | ListOf of conversion
  | TupleOf of conversion option list

let stdlibConversions =
  [
    ("Darklang.Stdlib.Cli.File.getModifiedTime", [  ], Some (ResultOf (Narrow "Int64")));
    ("Darklang.Stdlib.Cli.File.readFully", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.File.readLastLines", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.File.setPermissions", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.File.size", [  ], Some (ResultOf (Narrow "Int64")));
    ("Darklang.Stdlib.Cli.Posix.chmod", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.Posix.fdRead", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.Posix.fdSeekFromStart", [ (1, Widen "Int64") ], Some (ResultOf (Narrow "Int64")));
    ("Darklang.Stdlib.Cli.Posix.fdWrite", [  ], Some (ResultOf (Narrow "Int64")));
    ("Darklang.Stdlib.Cli.Posix.mkdir", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.Posix.openFile", [ (1, Widen "Int64"); (2, Widen "Int64") ], None);
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

