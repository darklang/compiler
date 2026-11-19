(* StdlibCache.mli - Reuse a prepared standard library across compiler processes. *)

(* $XDG_CACHE_HOME/dark-compiler, else ~/.cache/dark-compiler; none when
   neither is an absolute path. *)
val defaultDirectory : unit -> string option

(* How far a preparation is: waiting for process [pid] that is preparing the
   same cache, at a stage of a cold build, or done. *)
type progress =
  | Waiting of { pid : int; percent : int }
  | Stage of { stage : string; percent : int }
  | Ready of { cached : bool }

(* A reporter that prints each event as one JSON line on stderr
   (docs/compiler/progress.md). *)
val printJson : unit -> progress -> unit

(* The prepared stdlib for [target], read from [directory] (default
   [defaultDirectory ()]) when this exact executable stored it there before,
   otherwise built and stored. A cache that cannot be read or written only
   costs the rebuild; with no directory nothing is cached. One process at a
   time prepares into a directory; the others wait for its result. *)
val buildStdlib :
  ?directory:string ->
  ?progress:(progress -> unit) ->
  Platform.target ->
  (CompilationContexts.stdlibResult, string) result
