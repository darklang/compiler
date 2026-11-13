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

(* Write to a private temporary file, then rename, so a concurrent reader
   sees either nothing or a complete value. A failed write only loses the
   cache. *)
let write path (stdlib : CompilationContexts.stdlibResult) =
  let directory = Filename.dirname path in
  try
    let rec create path =
      if path <> "" && path <> "/" && not (Sys.file_exists path) then (
        create (Filename.dirname path);
        try Unix.mkdir path 0o755 with Unix.Unix_error (Unix.EEXIST, _, _) -> ())
    in
    create directory;
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

let buildStdlib ?directory target =
  let directory =
    match directory with Some _ -> directory | None -> defaultDirectory ()
  in
  match (directory, executableDigest ()) with
  | None, _ | _, None -> StdlibCompilation.buildStdlib target
  | Some directory, Some digest -> (
      let path = cachePath directory target digest in
      match read path with
      | Some stdlib -> Ok stdlib
      | None ->
          let built = StdlibCompilation.buildStdlib target in
          (match built with Ok stdlib -> write path stdlib | Error _ -> ());
          built)
