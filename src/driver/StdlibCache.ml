(* StdlibCache.ml - Reuse a prepared standard library across compiler processes. *)

(* The prepared stdlib is plain data, so it survives Marshal. Its key is the
   digest of the running executable: the compiler code and the embedded
   stdlib sources are both inside it, so any rebuild misses the cache, and a
   value is only ever read back by the exact code that wrote it. *)

let targetName = function
  | Platform.LinuxX86_64 -> "linux-x86_64"
  | Platform.ARM64Backend Platform.LinuxARM64 -> "linux-arm64"
  | Platform.ARM64Backend Platform.MacOSARM64 -> "macos-arm64"

let executableDigest () =
  try Some (Digest.to_hex (Digest.file Sys.executable_name))
  with Sys_error _ -> None

(* An absolute cache directory, or none: a relative path would scatter
   caches into whatever directory the compiler happens to run in. *)
let defaultDirectory () =
  let absolute = function
    | Some path when path <> "" && not (Filename.is_relative path) -> Some path
    | _ -> None
  in
  match absolute (Sys.getenv_opt "XDG_CACHE_HOME") with
  | Some path -> Some (Filename.concat path "dark-compiler")
  | None ->
      Option.map
        (fun home -> Filename.concat home ".cache/dark-compiler")
        (absolute (Sys.getenv_opt "HOME"))

let cachePath directory target digest =
  Filename.concat directory
    ("stdlib-" ^ targetName target ^ "-" ^ digest ^ ".bin")

let read path : CompilationContexts.stdlibResult option =
  try
    In_channel.with_open_bin path (fun channel ->
        Some (Marshal.from_channel channel))
  with Sys_error _ | End_of_file | Failure _ -> None

let rec createDirectory path =
  if path <> "" && path <> "/" && not (Sys.file_exists path) then (
    createDirectory (Filename.dirname path);
    try Unix.mkdir path 0o755 with Unix.Unix_error (Unix.EEXIST, _, _) -> ())

(* Write to a private temporary file, then rename, so a concurrent reader
   sees either nothing or a complete value. A failed write only loses the
   cache. *)
let write path (stdlib : CompilationContexts.stdlibResult) =
  let directory = Filename.dirname path in
  try
    let temporary, channel =
      Filename.open_temp_file ~mode:[ Open_binary ] ~temp_dir:directory
        "stdlib-" ".partial"
    in
    Fun.protect
      ~finally:(fun () ->
        close_out_noerr channel;
        if Sys.file_exists temporary then Sys.remove temporary)
      (fun () ->
        Marshal.to_channel channel stdlib [];
        close_out channel;
        Sys.rename temporary path;
        (* A stdlib written by an earlier build is never read again. *)
        let current = Filename.basename path in
        let prefix = "stdlib-" ^ String.sub current 7 (String.rindex current '-' - 6) in
        Array.iter
          (fun name ->
            if
              name <> current
              && String.starts_with ~prefix name
              && Filename.check_suffix name ".bin"
            then try Sys.remove (Filename.concat directory name) with Sys_error _ -> ())
          (Sys.readdir directory))
  with Sys_error _ | Unix.Unix_error _ | Invalid_argument _ -> ()

(* Only the lock holder writes, so any partial file left while holding it
   belongs to a writer that was killed. *)
let removePartials directory =
  try
    Array.iter
      (fun name ->
        if String.starts_with ~prefix:"stdlib-" name && Filename.check_suffix name ".partial"
        then try Sys.remove (Filename.concat directory name) with Sys_error _ -> ())
      (Sys.readdir directory)
  with Sys_error _ -> ()

type progress =
  | Waiting of { pid : int; percent : int }
  | Stage of { stage : string; percent : int }
  | Ready of { cached : bool }

(* Where each stage starts, in percent of a cold preparation's time. *)
let stagePercent = function
  | "parse" -> 0
  | "type-check" -> 10
  | "convert" -> 11
  | "optimize" -> 39
  | "lower" -> 83
  | "write" -> 95
  | _ -> 0

let jsonLine started event =
  let fields =
    match event with
    | Waiting { pid; percent } ->
        [ ("stage", `String "waiting"); ("percent", `Int percent); ("pid", `Int pid) ]
    | Stage { stage; percent } -> [ ("stage", `String stage); ("percent", `Int percent) ]
    | Ready { cached } ->
        [ ("stage", `String "ready"); ("percent", `Int 100); ("cached", `Bool cached) ]
  in
  Yojson.Safe.to_string
    (`Assoc
       ((("type", `String "progress") :: ("task", `String "prepare-stdlib") :: fields)
       @ [ ("elapsedMs", `Int (int_of_float ((Unix.gettimeofday () -. started) *. 1000.))) ]))

let printJson () =
  let started = Unix.gettimeofday () in
  fun event -> prerr_endline (jsonLine started event)

(* The lock file holds "<pid> <percent>" of the process preparing, so a
   waiting one can show how far it is. *)
let announce lock percent =
  try
    ignore (Unix.lseek lock 0 Unix.SEEK_SET);
    Unix.ftruncate lock 0;
    let line = Printf.sprintf "%d %d\n" (Unix.getpid ()) percent in
    ignore (Unix.write_substring lock line 0 (String.length line))
  with Unix.Unix_error _ -> ()

let holder lock =
  try
    ignore (Unix.lseek lock 0 Unix.SEEK_SET);
    let buffer = Bytes.create 64 in
    let count = Unix.read lock buffer 0 64 in
    ignore (Unix.lseek lock 0 Unix.SEEK_SET);
    Scanf.sscanf (Bytes.sub_string buffer 0 count) "%d %d" (fun pid percent ->
        Some (Waiting { pid; percent }))
  with Unix.Unix_error _ | Scanf.Scan_failure _ | Failure _ | End_of_file -> None

(* lockf locks from the current offset, which [holder] leaves at 0. *)
let rec acquire lock report last =
  match Unix.lockf lock Unix.F_TLOCK 0 with
  | () -> true
  | exception Unix.Unix_error ((Unix.EAGAIN | Unix.EACCES), _, _) ->
      let current = holder lock in
      (match current with
      | Some event when current <> last -> report event
      | _ -> ());
      Unix.sleepf 0.5;
      acquire lock report current
  | exception Unix.Unix_error _ -> false

(* One preparation at a time per cache directory: a second process waits for
   the first one's result rather than building its own copy, which would
   double the memory. The kernel drops the lock when its holder exits, however
   it exits. *)
let withLock directory report operation =
  match
    createDirectory directory;
    Unix.openfile (Filename.concat directory "stdlib.lock")
      [ Unix.O_RDWR; Unix.O_CREAT; Unix.O_CLOEXEC ] 0o644
  with
  | exception (Sys_error _ | Unix.Unix_error _) -> None
  | lock ->
      Fun.protect
        ~finally:(fun () -> Unix.close lock)
        (fun () -> if acquire lock report None then Some (operation lock) else None)

let buildStdlib ?directory ?(progress = fun _ -> ()) target =
  let directory =
    match directory with Some _ -> directory | None -> defaultDirectory ()
  in
  let cached stdlib =
    progress (Ready { cached = true });
    Ok stdlib
  in
  let build ?lock ?path () =
    let stage name =
      let percent = stagePercent name in
      Option.iter (fun lock -> announce lock percent) lock;
      progress (Stage { stage = name; percent })
    in
    let built = StdlibCompilation.buildStdlib ~progress:stage target in
    (match built with
    | Ok stdlib ->
        Option.iter
          (fun path ->
            stage "write";
            write path stdlib)
          path
    | Error _ -> ());
    if Result.is_ok built then progress (Ready { cached = false });
    built
  in
  match (directory, executableDigest ()) with
  | None, _ | _, None -> build ()
  | Some directory, Some digest -> (
      let path = cachePath directory target digest in
      let underLock lock =
        match read path with
        | Some stdlib -> cached stdlib
        | None ->
            removePartials directory;
            build ~lock ~path ()
      in
      match read path with
      | Some stdlib -> cached stdlib
      | None -> (
          match withLock directory progress underLock with
          | Some built -> built
          | None -> build ()))
