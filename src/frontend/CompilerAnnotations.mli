(* CompilerAnnotations.mli - Per-item compiler decisions, kept beside imported sources or in fixture comments. *)
type phase = Compile | Runtime
type failureClass = AotRejection | MissingSupport | UpstreamBug

type disposition =
  | Run
  | ErrorMessage of { phase : phase; diagnostic : string; reason : string }
  | Blocked of { reason : string; diagnostic : string option }
  | Excluded of string
  | Replace of { target : string; reason : string }
  | Xfail of {
      phase : phase;
      diagnostic : string;
      reason : string;
      classification : failureClass;
    }

type item = { position : Tokenizer.pos; isTest : bool }
type annotation = { position : Tokenizer.pos; disposition : disposition }

val read :
  reasons:string list ->
  items:item list ->
  string ->
  (annotation list, string) result

val at : annotation list -> Tokenizer.pos -> disposition
val parseReasons : string -> (string list, string) result
val readReasons : string -> (string list, string) result
val parse : string list -> string -> (disposition, string) result
val check : isTest:bool -> disposition -> (disposition, string) result

type sidecar

val parseSidecar : string -> (sidecar, string) result
val readSidecar : string -> (sidecar, string) result
val sidecarPaths : sidecar -> string list
val forSource : sidecar -> string -> (string * string) list
