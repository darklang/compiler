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
  | ErrorOf of conversion
  | Both of conversion * conversion
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
    (* The runtime builtins hosted process and clock helpers call: the
       compiler's take and give Int errnos, indexes and counts where the
       interpreter's use Int64. Child handles and descriptors are BigInts in
       both. *)
    ("Builtin.runtimeChildStart", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.runtimeChildDescriptors", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.runtimeChildPoll", [  ], Some (Both (ResultOf (OptionOf (Narrow "Int64")), ErrorOf (Narrow "Int64"))));
    ("Builtin.runtimeChildWait", [  ], Some (Both (ResultOf (Narrow "Int64"), ErrorOf (Narrow "Int64"))));
    ("Builtin.runtimeChildKill", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.runtimeChildClose", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.runtimeChildClosePort", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.runtimeChildBufferGet", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.runtimeChildBufferSet", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.runtimeMonotonicCounter", [  ], Some (TupleOf [ Some (Narrow "Int64"); Some (Narrow "Int64") ]));
    ("Builtin.runtimeDelayTicks", [ (0, Widen "Int64") ], None);
    ("Builtin.syscallNanosleep", [ (0, Widen "Int64"); (1, Widen "Int64") ], Some (TupleOf [ Some (Narrow "Int64"); None ]));
    ("Builtin.syscallClockGettime", [ (0, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallMachAbsoluteTime", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallMachTimebaseInfo", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallStatAt", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallGetdents", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallGetcwd", [ (0, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallChdir", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallMkdir", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallUnlinkAt", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallRename", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallChmod", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallSymlink", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallReadlinkAt", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallRead", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallLseek", [ (1, Widen "Int64"); (2, Widen "Int64") ], Some (Both (ResultOf (Narrow "Int64"), ErrorOf (Narrow "Int64"))));
    ("Builtin.syscallWrite", [  ], Some (Both (ResultOf (Narrow "Int64"), ErrorOf (Narrow "Int64"))));
    ("Builtin.syscallClose", [  ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallOpen", [ (1, Widen "Int64"); (2, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallGetpid", [  ], Some (Narrow "Int64"));
    ("Builtin.syscallGetuid", [  ], Some (Narrow "Int64"));
    ("Builtin.syscallFlock", [ (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
    ("Builtin.syscallKill", [ (0, Widen "Int64"); (1, Widen "Int64") ], Some (ErrorOf (Narrow "Int64")));
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

