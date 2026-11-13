(* StdlibCache.mli - Reuse a prepared standard library across compiler processes. *)

(* $XDG_CACHE_HOME/dark-compiler, else ~/.cache/dark-compiler; none when
   neither is an absolute path. *)
val defaultDirectory : unit -> string option

(* The prepared stdlib for [target], read from [directory] (default
   [defaultDirectory ()]) when this exact executable stored it there before,
   otherwise built and stored. A cache that cannot be read or written only
   costs the rebuild; with no directory nothing is cached. *)
val buildStdlib :
  ?directory:string ->
  Platform.target ->
  (CompilationContexts.stdlibResult, string) result
