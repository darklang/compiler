(* Diagnostic.ml - Why darkc refused something, as data: the reason, the item it failed in, and the call path there. *)

type reason =
  | MissingFunction of string
  | MissingType of string
  | UnsupportedFeature of string
  | NeedsTypeArguments of string option
  | NotAnEntry of { parameters : string list; returnType : string }
  | EntryNotFound of string
  | Usage
  | CompilerError of string

type location = {
  item : string;
  hash : string option;
  line : int option;
  column : int option;
}

type t = {
  reason : reason;
  location : location option;
  path : string list;
  message : string;
}

let withoutOwner name =
  if String.starts_with ~prefix:"Darklang." name then
    String.sub name 9 (String.length name - 9)
  else name

let find text pattern =
  let length = String.length pattern in
  let rec loop index =
    if index + length > String.length text then None
    else if String.sub text index length = pattern then Some index
    else loop (index + 1)
  in
  loop 0

(* The text between [prefix] and the next [stop] character, if [prefix]
   occurs in [text]. *)
let after prefix stop text =
  Option.map
    (fun index ->
      let start = index + String.length prefix in
      match String.index_from_opt text start stop with
      | Some finish -> String.sub text start (finish - start)
      | None -> String.sub text start (String.length text - start))
    (find text prefix)

let firstLine text =
  match String.index_opt text '\n' with
  | Some index -> String.sub text 0 index
  | None -> text

let genericValue = "Cannot infer specialization for function value '"
let unsupported = ", which this compiler does not support"

let reasonOf error =
  let error = String.trim error in
  match after "can't compile: uses " '\n' error with
  | Some text when String.ends_with ~suffix:unsupported text ->
      UnsupportedFeature
        (String.sub text 0 (String.length text - String.length unsupported))
  | _ -> (
      match after "Unknown function or value '" '\'' error with
      | Some name -> MissingFunction (withoutOwner name)
      | None -> (
          match after "Unknown type '" '\'' error with
          | Some name -> MissingType (withoutOwner name)
          | None -> (
              match after genericValue '\'' error with
              | Some name -> NeedsTypeArguments (Some (withoutOwner name))
              | None
                when Option.is_some
                       (find error "remained polymorphic after monomorphization")
                ->
                  NeedsTypeArguments None
              | None -> CompilerError (firstLine error))))

(* The number starting at [index], and where it ends. *)
let number text index =
  let rec span i =
    if i < String.length text && text.[i] >= '0' && text.[i] <= '9' then
      span (i + 1)
    else i
  in
  let finish = span index in
  if finish = index then None
  else Some (int_of_string (String.sub text index (finish - index)), finish)

(* "error[...] at 2:1: ..." names a line and column in the unit. *)
let lineAndColumn text =
  let rec scan from =
    match find (String.sub text from (String.length text - from)) " at " with
    | None -> (None, None)
    | Some offset -> (
        let start = from + offset + 4 in
        match number text start with
        | Some (line, finish)
          when finish < String.length text && text.[finish] = ':' -> (
            match number text (finish + 1) with
            | Some (column, _) -> (Some line, Some column)
            | None -> scan start)
        | _ -> scan start)
  in
  scan 0

let stages =
  [ "Compilation failed: "; "Parse error: "; "ANF conversion error: " ]

let rec withoutStages text =
  match
    List.find_opt (fun prefix -> String.starts_with ~prefix text) stages
  with
  | Some prefix ->
      withoutStages
        (String.sub text (String.length prefix)
           (String.length text - String.length prefix))
  | None -> text

(* The item an error is reported in: a package unit (`package:A.b@hash: `)
   or a declaration the checker names (`A.b: `). *)
let locationOf error =
  let text = withoutStages (String.trim error) in
  let line, column = lineAndColumn text in
  if String.starts_with ~prefix:"package:" text then
    match (String.index_opt text '@', find text ": ") with
    | Some at, Some colon when at < colon ->
        Some
          {
            item = String.sub text 8 (at - 8);
            hash = Some (String.sub text (at + 1) (colon - at - 1));
            line;
            column;
          }
    | _ -> None
  else
    match find text ": " with
    | Some colon ->
        let item = String.sub text 0 colon in
        if
          String.contains item '.'
          && not (String.exists (fun c -> c = ' ' || c = '\'') item)
        then Some { item; hash = None; line; column }
        else None
    | None -> None

let ofError error =
  {
    reason = reasonOf error;
    location = locationOf error;
    path = [];
    message = String.trim error;
  }

let usage message = { reason = Usage; location = None; path = []; message }

(* What the failing item uses and the compiler lacks, if that is the reason. *)
let missing diagnostic =
  match diagnostic.reason with
  | MissingFunction name | MissingType name -> Some name
  | UnsupportedFeature _ | NeedsTypeArguments _ | NotAnEntry _ | EntryNotFound _
  | Usage | CompilerError _ ->
      None

let undetermined diagnostic =
  match diagnostic.reason with
  | NeedsTypeArguments _ -> true
  | MissingFunction _ | MissingType _ | UnsupportedFeature _ | NotAnEntry _
  | EntryNotFound _ | Usage | CompilerError _ ->
      false

(* One line, as `--check` prints it after "can't compile: ". *)
let reasonText diagnostic =
  match diagnostic.reason with
  | MissingFunction name ->
      "uses function or value " ^ name ^ ", which the compiler does not define"
  | MissingType name ->
      "uses type " ^ name ^ ", which the compiler does not define"
  | UnsupportedFeature feature -> "uses " ^ feature ^ unsupported
  | NeedsTypeArguments (Some name) ->
      name
      ^ " is generic; compiling it needs a call with concrete type arguments"
  | NeedsTypeArguments None ->
      "it is generic; compiling it needs a call with concrete type arguments"
  | NotAnEntry { parameters; returnType } ->
      "an executable's fn takes List<String> or nothing and returns Unit or \
       Int64; this one takes ("
      ^ String.concat ", " parameters
      ^ ") and returns " ^ returnType
  | EntryNotFound hash -> "no package fn has the hash " ^ hash
  | Usage -> diagnostic.message
  | CompilerError line -> "compiler error: " ^ line

let optional f = function None -> `Null | Some value -> f value

let reasonJson = function
  | MissingFunction name ->
      `Assoc
        [
          ( "kind",
            `String
              (if String.starts_with ~prefix:"Builtin." name then
                 "missing-builtin"
               else "missing-function") );
          ("name", `String name);
        ]
  | MissingType name ->
      `Assoc [ ("kind", `String "missing-type"); ("name", `String name) ]
  | UnsupportedFeature feature ->
      `Assoc
        [
          ("kind", `String "unsupported-feature"); ("feature", `String feature);
        ]
  | NeedsTypeArguments name ->
      `Assoc
        [
          ("kind", `String "needs-type-arguments");
          ("name", optional (fun name -> `String name) name);
        ]
  | NotAnEntry { parameters; returnType } ->
      `Assoc
        [
          ("kind", `String "not-an-entry");
          ("parameters", `List (List.map (fun p -> `String p) parameters));
          ("returnType", `String returnType);
        ]
  | EntryNotFound hash ->
      `Assoc [ ("kind", `String "entry-not-found"); ("hash", `String hash) ]
  | Usage -> `Assoc [ ("kind", `String "usage") ]
  | CompilerError _ -> `Assoc [ ("kind", `String "compiler-error") ]

let toJson diagnostic : Yojson.Basic.t =
  let int = optional (fun value -> `Int value) in
  `Assoc
    [
      ("reason", reasonJson diagnostic.reason);
      ( "location",
        optional
          (fun location ->
            `Assoc
              [
                ("item", `String location.item);
                ("hash", optional (fun hash -> `String hash) location.hash);
                ("line", int location.line);
                ("column", int location.column);
              ])
          diagnostic.location );
      ("path", `List (List.map (fun name -> `String name) diagnostic.path));
      ("message", `String diagnostic.message);
    ]
