(* StdlibConversions.ml - Where the interpreter's stdlib and the compiler's differ. *)

(* Stdlib fns the compiler has under the interpreter's name with an older
   meaning: the interpreter's Dict.fromList keeps the last of a repeated key,
   where the compiler's answers None. A hosted call reaches the compiler fn
   with the interpreter's meaning. *)
let hostedRenames =
  [
    ("Darklang.Stdlib.Dict.fromList", "Darklang.Stdlib.Dict.fromListOverwritingDuplicates");
    (* List.append adds one value at the end; the compiler's joins two lists. *)
    ("Darklang.Stdlib.List.append", "Darklang.Stdlib.List.appendOne");
    (* The compiler's names for List.prepend and List.concat. *)
    ("Darklang.Stdlib.List.prepend", "Darklang.Stdlib.List.push");
    (* zip and map2 answer a Result with both lengths; the compiler's an Option. *)
    ("Darklang.Stdlib.List.zip", "Darklang.Stdlib.List.zipChecked");
    ("Darklang.Stdlib.List.map2", "Darklang.Stdlib.List.map2Checked");
    ("Darklang.Stdlib.List.map2Shortest", "Darklang.Stdlib.List.map2shortest");
    ("Darklang.Stdlib.List.concat", "Darklang.Stdlib.List.append");
    (* List.repeat answers the List; the compiler's a Result. *)
    ("Darklang.Stdlib.List.repeat", "Darklang.Stdlib.List.repeatUnsafe");
    (* padStart and padEnd pad with a Char to an Int64; the compiler's with a
       String to an Int, answering a Result. *)
    ("Darklang.Stdlib.String.padStart", "Darklang.Stdlib.String.padStartChar");
    ("Darklang.Stdlib.String.padEnd", "Darklang.Stdlib.String.padEndChar");
  ]

let hostedName name =
  Option.value (List.assoc_opt name hostedRenames) ~default:name

(* Where the interpreter's stdlib and the compiler's disagree on a fn's
   numeric types (an Int64 index where the compiler takes an Int, a UInt8 where
   it answers an Int), a hosted call converts the arguments it passes and the
   result it gets back, so the code around the call keeps the types it was
   written for. Widen takes an interpreter value to an Int, Narrow an Int back
   to the interpreter's type, through Options, Results, Lists and tuples. The
   table is the difference between the two stdlibs' signatures; the re-pin
   removes it. *)
type conversion =
  | Widen of string
  | Narrow of string
  | OptionOf of conversion
  | ResultOf of conversion
  | ListOf of conversion
  | TupleOf of conversion option list

let stdlibConversions =
  [
    ("Darklang.Stdlib.Char.fromCodepoint", [ (0, Widen "UInt32") ], None);
    ("Darklang.Stdlib.Char.toAsciiCode", [  ], Some (OptionOf (Narrow "UInt8")));
    ("Darklang.Stdlib.Char.toCodepoint", [  ], Some (OptionOf (Narrow "UInt32")));
    ("Darklang.Stdlib.Cli.Dir.size", [  ], Some (ResultOf (Narrow "Int64")));
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
    ("Darklang.Stdlib.Cli.Process.runInteractive", [  ], Some (ResultOf (Narrow "Int64")));
    ("Darklang.Stdlib.Cli.Process.runSilent", [  ], Some (ResultOf (Narrow "Int64")));
    ("Darklang.Stdlib.Cli.Process.runWithTimeout", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.Process.spawnAndWait", [ (2, OptionOf (Widen "Int64")) ], Some (ResultOf (TupleOf [Some (Narrow "Int64"); None; None])));
    ("Darklang.Stdlib.Cli.Tui.Text.clipMarked", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.Tui.Text.clipToWidth", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Cli.Tui.Text.styledWidth", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Cli.UI.Progress.bar", [ (0, Widen "Int64"); (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.DateTime.addMilliseconds", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.DateTime.addSeconds", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.DateTime.day", [  ], Some (Narrow "UInt8"));
    ("Darklang.Stdlib.DateTime.fromMilliseconds", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.DateTime.fromSeconds", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.DateTime.hour", [  ], Some (Narrow "UInt8"));
    ("Darklang.Stdlib.DateTime.millisecond", [  ], Some (Narrow "UInt16"));
    ("Darklang.Stdlib.DateTime.minute", [  ], Some (Narrow "UInt8"));
    ("Darklang.Stdlib.DateTime.second", [  ], Some (Narrow "UInt8"));
    ("Darklang.Stdlib.DateTime.subtractMilliseconds", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.DateTime.subtractMs", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.DateTime.subtractSeconds", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.DateTime.toMilliseconds", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.DateTime.toSeconds", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.DateTime.year", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Float.ceiling", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Float.floor", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Float.round", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Float.roundDown", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Float.roundTowardsZero", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Float.roundUp", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Float.truncate", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.List.chunkBySize", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.List.drop", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.List.findFirstIndex", [  ], Some (OptionOf (Narrow "Int64")));
    ("Darklang.Stdlib.List.range", [ (0, Widen "Int64"); (1, Widen "Int64") ], Some (ListOf (Narrow "Int64")));
    ("Darklang.Stdlib.List.setAt", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.Pretty.hang", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.Pretty.nest", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.Pretty.render", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.charAt", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.codepointLength", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.String.displayWidth", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.String.dropFirst", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.dropLast", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.first", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.getByteAt", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.indexOf", [  ], Some (OptionOf (Narrow "Int64")));
    ("Darklang.Stdlib.String.last", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.lastIndexOf", [  ], Some (OptionOf (Narrow "Int64")));
    ("Darklang.Stdlib.String.length", [  ], Some (Narrow "Int64"));
    ("Darklang.Stdlib.String.random", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.repeat", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.slice", [ (1, Widen "Int64"); (2, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.splitN", [ (2, Widen "Int64") ], None);
    ("Darklang.Stdlib.String.toCodepoints", [  ], Some (ListOf (Narrow "UInt32")));
    ("Darklang.Stdlib.Blob.length", [], Some (Narrow "Int64"));
    ("Darklang.Stdlib.Blob.slice", [ (1, Widen "Int64"); (2, Widen "Int64") ], None);
    ("Darklang.Stdlib.List.length", [], Some (Narrow "Int64"));
    ("Darklang.Stdlib.List.repeat", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.List.repeatUnsafe", [ (0, Widen "Int64") ], None);
    ("Darklang.Stdlib.Dict.size", [], Some (Narrow "Int64"));
    ("Darklang.Stdlib.List.getAt", [ (1, Widen "Int64") ], None);
    ("Darklang.Stdlib.List.take", [ (1, Widen "Int64") ], None);
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

