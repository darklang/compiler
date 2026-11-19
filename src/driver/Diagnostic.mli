(* Diagnostic.mli - Why darkc refused something, as data: the reason, the item it failed in, and the call path there. *)

type reason =
  | MissingFunction of string
  | MissingType of string
  | UnsupportedFeature of string
  | NeedsTypeArguments of string option
  | NotAnEntry of { parameters : string list; returnType : string }
  | EntryNotFound of string
  | Usage
  | CompilerError of string

(* A package unit's item and hash, or a declaration the checker names; the
   line and column when the error gives them. *)
type location = {
  item : string;
  hash : string option;
  line : int option;
  column : int option;
}

(* [path] runs from the root (the checked name or the entry fn) to what the
   failing item uses, when both are package items. *)
type t = {
  reason : reason;
  location : location option;
  path : string list;
  message : string;
}

val ofError : string -> t
val usage : string -> t
val missing : t -> string option
val undetermined : t -> bool
val reasonText : t -> string
val toJson : t -> Yojson.Basic.t
