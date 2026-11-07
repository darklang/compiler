(* CompilabilityReason.mli - One-line "can't compile: uses X" reasons for batch reports. *)

(* Pairs of (current interpreter spelling, compiler's pinned spelling). *)
val upstreamRenames : (string * string) list

(* The reason a failed compilation reports, from its diagnostic text: the
   feature or name it uses that the compiler lacks, or the first line of any
   other compiler error. Empty for an empty diagnostic. *)
val classify : string -> string
