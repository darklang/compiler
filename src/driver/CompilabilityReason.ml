(* CompilabilityReason.ml - One-line "can't compile: uses X" reasons for batch reports. *)

(* Names the interpreter renamed after the compiler's pinned revision. A call
   to the new spelling reaches the compiler as an unknown name; the reason
   names the spelling the compiler still has, so an audit separates a stale
   pin from a missing implementation. Exact names only: a module prefix would
   claim renames for functions that never had a pinned counterpart. *)
let upstreamRenames =
  [
    ("Stdlib.Dict.containsKey", "Stdlib.Dict.member");
    ("Stdlib.Dict.toEntries", "Stdlib.Dict.toList");
    ("Stdlib.List.concat", "Stdlib.List.append");
    ("Stdlib.List.prepend", "Stdlib.List.push");
    ("Stdlib.Base16.encode", "Stdlib.Blob.toHex");
    ("Stdlib.Base16.decode", "Stdlib.Blob.fromHex");
    ("Stdlib.Utf8.encode", "Stdlib.String.toBlob");
    ("Stdlib.Crypto.digestText", "Stdlib.String.digest");
  ]

let withoutOwner name =
  if String.starts_with ~prefix:"Darklang." name then
    String.sub name 9 (String.length name - 9)
  else name

(* The text between [prefix] and the next [stop] character, if [prefix]
   occurs in [text]. *)
let after prefix stop text =
  let prefixLength = String.length prefix in
  let textLength = String.length text in
  let rec find index =
    if index + prefixLength > textLength then None
    else if String.sub text index prefixLength = prefix then
      let start = index + prefixLength in
      match String.index_from_opt text start stop with
      | Some finish -> Some (String.sub text start (finish - start))
      | None -> Some (String.sub text start (textLength - start))
    else find (index + 1)
  in
  find 0

let firstLine text =
  match String.index_opt text '\n' with
  | Some index -> String.sub text 0 index
  | None -> text

let missingName kind name =
  let bare = withoutOwner name in
  let renamed =
    match List.assoc_opt bare upstreamRenames with
    | Some pinned -> "; the compiler's pinned name is " ^ pinned
    | None -> ""
  in
  "uses " ^ kind ^ " " ^ bare ^ ", which the compiler does not define" ^ renamed

let genericValue = "Cannot infer specialization for function value '"

(* A generic function referenced without a call has no type arguments to
   specialize with, so its probe says nothing about whether it compiles. *)
let undetermined error = Option.is_some (after genericValue '\'' error)

let classify error =
  let error = String.trim error in
  if error = "" then ""
  else
    match after "can't compile: " '\n' error with
    | Some reason -> reason
    | None -> (
        match after "Unknown function or value '" '\'' error with
        | Some name -> missingName "function or value" name
        | None -> (
            match after "Unknown type '" '\'' error with
            | Some name -> missingName "type" name
            | None -> (
                match after genericValue '\'' error with
                | Some name ->
                    withoutOwner name
                    ^ " is generic; compiling it needs a call with concrete \
                       type arguments"
                | None -> "compiler error: " ^ firstLine error)))
