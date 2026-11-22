(*
   Program.ml - Compiler CLI Entry Point
   The main entry point for the Darklang compiler CLI.
   This module:
   - Parses command-line arguments using POSIX-style flags
   - Orchestrates the compilation pipeline through all passes
   - Handles errors and provides user feedback
   Compilation pipeline:
   1. Parser and checking: Source → parsed AST → CheckedAST
   2. AST_to_ANF: CheckedAST → ANF
   3. ANF_to_MIR: ANF → MIR
   4. MIR_to_LIR: MIR → LIR
   5. RegisterAllocation: LIR (virtual) → LIR (physical)
   6. CodeGen: LIR → target ISA instructions
   7. Encoding: target ISA instructions → machine code
   8. Binary generation: machine code → platform executable
*)
(* Program.ml - Command-line entry, batch compilation and native execution. *)
(*
   Output verbosity level
   0 = Quiet (no output)
   1 = Normal (standard output)
   2 = Verbose (show pass names)
   3 = VeryVerbose (show pass names + timing)
   4 = DumpIR (show all intermediate representations)
*)
type verbosityLevel = Quiet | Normal | Verbose | VeryVerbose | DumpIR

(*
   Select the compiler backend independently from the process architecture.
   Explicit targets are compile-only because the CLI does not emulate them.
*)
type targetSelection = HostTarget | ExplicitTarget of Platform.target

(*
   One independently linked executable in a batch compiler invocation.
*)
type batchCompileItem = {
  kind : string;
  name : string;
  sourceFile : string;
  outputFile : string;
}

type batchInput =
  | CommandLineItems of batchCompileItem * batchCompileItem list
  | ManifestFile of string

(*
   Batch mode shares immutable stdlib preparation while preserving a separate
   compilation request and executable for every source.
*)
type batchCliOptions = {
  target : targetSelection;
  verbosity : verbosityLevel;
  leakCheck : bool;
  allowInternal : bool;
  packageServer : string option;
  cacheDir : string option;
  input : batchInput;
  keepGoing : bool;
  reportPath : string option;
}

type batchReportItem = {
  kind : string;
  name : string;
  source : string;
  output : string;
  status : string;
  error : string;
  reason : string;
  milliseconds : float;
}

(*
   Parsed CLI options
   True = run, False = compile (default)
   True = expression, False = file (default)
   The positional arguments: one source, or the names --check asks about.
   Opt-in hosted package resolution. Ordinary compilation stays offline.
   Where the prepared stdlib and package answers are kept; none keeps nothing.
   The hash of the package fn an executable calls.
   Compiler-owned sources may use private runtime and HAMT helpers.
   Optimization flags
   IR dump flags
*)
type cliOptions = {
  run : bool;
  isExpression : bool;
  check : bool;
  json : bool;
  outputFile : string option;
  verbosity : verbosityLevel;
  help : bool;
  version : bool;
  inputs : string list;
  leakCheck : bool;
  target : targetSelection;
  emitResult : bool;
  packageServer : string option;
  cacheDir : string option;
  entry : string option;
  allowInternal : bool;
  disableFreeList : bool;
  disableANFOpt : bool;
  disableANFConstFolding : bool;
  disableANFConstProp : bool;
  disableANFCopyProp : bool;
  disableANFDCE : bool;
  disableANFStrengthReduction : bool;
  disableInlining : bool;
  disableTCO : bool;
  disableMIROpt : bool;
  disableMIRSCCP : bool;
  disableMIRCSE : bool;
  disableMIRDCE : bool;
  disableMIRLICM : bool;
  disableLIROpt : bool;
  disableLIRPeephole : bool;
  disableFunctionTreeShaking : bool;
  dumpANF : bool;
  dumpMIR : bool;
  dumpLIR : bool;
  dumpFunction : string option;
  dumpIRSummary : bool;
  dumpIROutput : string option;
}

(*
   Compilability analysis: whether each named package function or value
   compiles, and if not, the one-line reason. One prepared stdlib serves
   every name.
*)
type checkCliOptions = {
  checkTarget : targetSelection;
  checkPackageServer : string option;
  checkCacheDir : string option;
  checkJson : bool;
  checkNames : string list;
}

type cliCommand =
  | SingleCommand of cliOptions
  | BatchCommand of batchCliOptions
  | CheckCommand of checkCliOptions
  | PrepareCommand of targetSelection * string

module X = CompilationContexts

let ( let* ) = Result.bind

(*
   Convert VerbosityLevel to integer for library
   Library verbosity: 0=silent, 1=pass names, 2=pass names + timing, 3=dump all IRs
   No output
   CLI handles output, library silent
   Library shows pass names
   Library shows pass names + timing
   Library dumps all IRs
*)
let verbosityToInt = function
  | Quiet | Normal -> 0
  | Verbose -> 1
  | VeryVerbose -> 2
  | DumpIR -> 3

(*
   Determine whether the CLI should emit normal output for a verbosity level
*)
let shouldShowNormal = function
  | Quiet -> false
  | Normal | Verbose | VeryVerbose | DumpIR -> true

let increment = function
  | Quiet -> Normal
  | Normal -> Verbose
  | Verbose -> VeryVerbose
  | VeryVerbose | DumpIR -> DumpIR

(*
   Default empty options
*)
let defaultOptions : cliOptions =
  {
    run = false;
    isExpression = false;
    check = false;
    json = false;
    outputFile = None;
    verbosity = Normal;
    help = false;
    version = false;
    inputs = [];
    leakCheck = false;
    target = HostTarget;
    emitResult = false;
    packageServer = None;
    cacheDir = None;
    entry = None;
    allowInternal = false;
    disableFreeList = false;
    disableANFOpt = false;
    disableANFConstFolding = false;
    disableANFConstProp = false;
    disableANFCopyProp = false;
    disableANFDCE = false;
    disableANFStrengthReduction = false;
    disableInlining = false;
    disableTCO = false;
    disableMIROpt = false;
    disableMIRSCCP = false;
    disableMIRCSE = false;
    disableMIRDCE = false;
    disableMIRLICM = false;
    disableLIROpt = false;
    disableLIRPeephole = false;
    disableFunctionTreeShaking = false;
    dumpANF = false;
    dumpMIR = false;
    dumpLIR = false;
    dumpFunction = None;
    dumpIRSummary = false;
    dumpIROutput = None;
  }

let parseTargetValue value =
  if Text.lowerInvariant (Text.trim value) = "linux-x86_64" then
    Ok (ExplicitTarget Platform.LinuxX86_64)
  else Error ("Invalid target '" ^ value ^ "' (expected 'linux-x86_64')")

let parsePackageServerValue value =
  let invalid () =
    Error
      ("Invalid package server '" ^ value
     ^ "' (expected an absolute HTTP(S) URL)")
  in
  try
    let uri = Uri.of_string (String.trim value) in
    match (Uri.scheme uri, Uri.host uri, Uri.port uri) with
    | Some ("http" | "https"), Some host, port
      when host <> ""
           && (not
                 (String.exists (fun c -> Char.code c <= 32 || c = '\\') host))
           (* Uri's parser can reinterpret a malformed port as a relative path. *)
           && (Uri.path uri = ""
              || String.starts_with ~prefix:"/" (Uri.path uri))
           &&
           match port with
           | None -> true
           | Some port -> port >= 0 && port <= 65535 ->
        Ok (Uri.to_string (Uri.canonicalize uri))
    | _ -> invalid ()
  with Invalid_argument _ | Failure _ -> invalid ()

(* Relative to where darkc was started, so a cache never lands in whatever
   directory a later step happens to run in. *)
let parseCacheDirValue value =
  if Text.trim value = "" then Error "--cache-dir requires a non-empty path"
  else if Filename.is_relative value then
    Ok (Filename.concat (Sys.getcwd ()) value)
  else Ok value

(* A package hash as the package server names it: hex digits only. *)
let parseEntryValue value =
  let hash = Text.trim value in
  if
    hash <> ""
    && String.for_all
         (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || c = '-')
         hash
  then Ok hash
  else Error ("Invalid entry hash '" ^ value ^ "'")

(*
   Build compiler options from CLI options
*)
let buildCompilerOptions (options : cliOptions) :
    CompilerOptions.compilerOptions =
  {
    CompilerOptions.disableFreeList = options.disableFreeList;
    disableANFOpt = options.disableANFOpt;
    disableANFConstFolding = options.disableANFConstFolding;
    disableANFConstProp = options.disableANFConstProp;
    disableANFCopyProp = options.disableANFCopyProp;
    disableANFDCE = options.disableANFDCE;
    disableANFStrengthReduction = options.disableANFStrengthReduction;
    disableInlining = options.disableInlining;
    disableTCO = options.disableTCO;
    disableMIROpt = options.disableMIROpt;
    disableMIRSCCP = options.disableMIRSCCP;
    disableMIRCSE = options.disableMIRCSE;
    disableMIRDCE = options.disableMIRDCE;
    disableMIRLICM = options.disableMIRLICM;
    disableLIROpt = options.disableLIROpt;
    disableLIRPeephole = options.disableLIRPeephole;
    disableFunctionTreeShaking = options.disableFunctionTreeShaking;
    enableCoverage = false;
    enableLeakCheck = options.leakCheck;
    nativeLayoutProbe = CompilerOptions.NoNativeLayoutProbe;
    warnings = CompilerOptions.defaultWarningSettings;
    dumpANF = options.dumpANF;
    dumpMIR = options.dumpMIR;
    dumpLIR = options.dumpLIR;
    dumpFunction = options.dumpFunction;
    dumpIRSummary = options.dumpIRSummary;
  }

(*
   Parse command-line flags into options
   Apply last verbosity setting (last one wins)
   Handle -ofile format
   Handle --output=file format
   Stack -v flags: -v = Verbose, -vv = VeryVerbose, -vvv = DumpIR
   Deliberately omitted from user-facing help. This mode exists for
   compiler-owned stdlib, regression, and benchmark sources only.
   Special case: "-" means stdin
   Handle combined short flags like -qr, -re, etc.
   -ovalue format
   Invalid flag character
   Non-flag argument - this is the filename or expression
*)
let parseArgs argv =
  let rec flags args (options : cliOptions) verbosity =
    let resume rest options = flags rest options verbosity in
    let output value rest =
      if Option.is_some options.outputFile then
        Error "Output file specified multiple times"
      else resume rest { options with outputFile = Some value }
    in
    let target value rest =
      match options.target with
      | ExplicitTarget _ -> Error "Target specified multiple times"
      | HostTarget ->
          let* target = parseTargetValue value in
          resume rest { options with target }
    in
    let server value rest =
      if Option.is_some options.packageServer then
        Error "Package server specified multiple times"
      else
        let* server = parsePackageServerValue value in
        resume rest { options with packageServer = Some server }
    in
    let cacheDir value rest =
      if Option.is_some options.cacheDir then
        Error "Cache directory specified multiple times"
      else
        let* cacheDir = parseCacheDirValue value in
        resume rest { options with cacheDir = Some cacheDir }
    in
    let entry value rest =
      if Option.is_some options.entry then Error "Entry specified multiple times"
      else
        let* hash = parseEntryValue value in
        resume rest { options with entry = Some hash }
    in
    let input value rest = resume rest { options with inputs = options.inputs @ [ value ] } in
    let dump field value rest =
      let existing, duplicate, empty =
        match field with
        | `Function ->
            ( options.dumpFunction,
              "Dump function filter specified multiple times",
              "--dump-function requires a non-empty value" )
        | `Output ->
            ( options.dumpIROutput,
              "IR dump output specified multiple times",
              "--dump-ir-output requires a non-empty value" )
      in
      if Option.is_some existing then Error duplicate
      else if Text.trim value = "" then Error empty
      else
        resume rest
          (match field with
          | `Function -> { options with dumpFunction = Some value }
          | `Output -> { options with dumpIROutput = Some value })
    in
    match args with
    | [] -> Ok { options with verbosity }
    | ("-r" | "--run") :: rest ->
        if options.run then Error "Run flag specified multiple times"
        else resume rest { options with run = true }
    | ("-e" | "--expression") :: rest ->
        if options.isExpression then
          Error "Expression flag specified multiple times"
        else resume rest { options with isExpression = true }
    | "--target" :: value :: rest -> target value rest
    | [ "--target" ] ->
        Error "Missing value for --target (expected 'linux-x86_64')"
    | "--check" :: rest ->
        if options.check then Error "Check flag specified multiple times"
        else resume rest { options with check = true }
    | "--json" :: rest -> resume rest { options with json = true }
    | "--package-server" :: value :: rest -> server value rest
    | [ "--package-server" ] -> Error "Missing value for --package-server"
    | flag :: rest when String.starts_with ~prefix:"--package-server=" flag ->
        server (String.sub flag 17 (String.length flag - 17)) rest
    | "--cache-dir" :: value :: rest -> cacheDir value rest
    | [ "--cache-dir" ] -> Error "Missing value for --cache-dir"
    | flag :: rest when String.starts_with ~prefix:"--cache-dir=" flag ->
        cacheDir (String.sub flag 12 (String.length flag - 12)) rest
    | "--entry" :: value :: rest -> entry value rest
    | [ "--entry" ] -> Error "Missing value for --entry (a package fn hash)"
    | flag :: rest when String.starts_with ~prefix:"--entry=" flag ->
        entry (String.sub flag 8 (String.length flag - 8)) rest
    | flag :: rest when String.starts_with ~prefix:"--target=" flag ->
        target (String.sub flag 9 (String.length flag - 9)) rest
    | ("-o" | "--output") :: value :: rest -> output value rest
    | flag :: rest
      when String.starts_with ~prefix:"-o" flag && String.length flag > 2 ->
        output (String.sub flag 2 (String.length flag - 2)) rest
    | flag :: rest when String.starts_with ~prefix:"--output=" flag ->
        output (String.sub flag 9 (String.length flag - 9)) rest
    | ("-q" | "--quiet") :: rest -> flags rest options Quiet
    | ("-v" | "--verbose") :: rest -> flags rest options (increment verbosity)
    | "--dump-function" :: value :: rest -> dump `Function value rest
    | [ "--dump-function" ] -> Error "Missing value for --dump-function"
    | flag :: rest when String.starts_with ~prefix:"--dump-function=" flag ->
        dump `Function (String.sub flag 16 (String.length flag - 16)) rest
    | "--dump-ir-output" :: value :: rest -> dump `Output value rest
    | [ "--dump-ir-output" ] -> Error "Missing value for --dump-ir-output"
    | flag :: rest when String.starts_with ~prefix:"--dump-ir-output=" flag ->
        dump `Output (String.sub flag 17 (String.length flag - 17)) rest
    | "--dump-anf" :: rest -> resume rest { options with dumpANF = true }
    | "--dump-mir" :: rest -> resume rest { options with dumpMIR = true }
    | "--dump-lir" :: rest -> resume rest { options with dumpLIR = true }
    | "--dump-ir-summary" :: rest ->
        resume rest { options with dumpIRSummary = true }
    | "--leak-check" :: rest -> resume rest { options with leakCheck = true }
    | "--allow-internal" :: rest ->
        resume rest { options with allowInternal = true }
    | "--emit-result" :: rest -> resume rest { options with emitResult = true }
    | ("-h" | "--help") :: rest -> resume rest { options with help = true }
    | "--version" :: rest -> resume rest { options with version = true }
    | ("--no-free-list" | "--disable-opt-freelist") :: rest ->
        resume rest { options with disableFreeList = true }
    | "--disable-opt-anf" :: rest ->
        resume rest { options with disableANFOpt = true }
    | "--disable-opt-anf-const-folding" :: rest ->
        resume rest { options with disableANFConstFolding = true }
    | "--disable-opt-anf-const-prop" :: rest ->
        resume rest { options with disableANFConstProp = true }
    | "--disable-opt-anf-copy-prop" :: rest ->
        resume rest { options with disableANFCopyProp = true }
    | "--disable-opt-anf-dce" :: rest ->
        resume rest { options with disableANFDCE = true }
    | "--disable-opt-anf-strength-reduction" :: rest ->
        resume rest { options with disableANFStrengthReduction = true }
    | "--disable-opt-inline" :: rest ->
        resume rest { options with disableInlining = true }
    | "--disable-opt-tco" :: rest ->
        resume rest { options with disableTCO = true }
    | "--disable-opt-mir" :: rest ->
        resume rest { options with disableMIROpt = true }
    | "--disable-opt-mir-sccp" :: rest ->
        resume rest { options with disableMIRSCCP = true }
    | "--disable-opt-mir-cse" :: rest ->
        resume rest { options with disableMIRCSE = true }
    | "--disable-opt-mir-dce" :: rest ->
        resume rest { options with disableMIRDCE = true }
    | "--disable-opt-mir-licm" :: rest ->
        resume rest { options with disableMIRLICM = true }
    | "--disable-opt-lir" :: rest ->
        resume rest { options with disableLIROpt = true }
    | "--disable-opt-lir-peephole" :: rest ->
        resume rest { options with disableLIRPeephole = true }
    | "--disable-opt-function-tree-shaking" :: rest ->
        resume rest { options with disableFunctionTreeShaking = true }
    | "--disable-opt-dce" :: rest ->
        resume rest { options with disableFunctionTreeShaking = true }
    | "-" :: rest -> input "-" rest
    | flag :: rest
      when String.starts_with ~prefix:"-" flag
           && (not (String.starts_with ~prefix:"--" flag))
           && String.length flag > 2 ->
        let units =
          Text.scalars (String.sub flag 1 (String.length flag - 1))
          |> Array.to_list
        in
        let rec expand units reversed =
          match units with
          | [] -> List.rev reversed
          | ((114 | 101 | 113 | 118 | 104) as code) :: rest ->
              expand rest (("-" ^ Text.ofScalars [| code |]) :: reversed)
          | 111 :: (_ :: _ as value) ->
              List.rev
                (("-o" ^ Text.ofScalars (Array.of_list value)) :: reversed)
          | code :: _ -> List.rev (("-" ^ Text.ofScalars [| code |]) :: reversed)
        in
        flags (expand units [] @ rest) options verbosity
    | argument :: rest when not (String.starts_with ~prefix:"-" argument) ->
        input argument rest
    (* `-e -- -7.5dec % 2dec`: after `--`, the one argument is the source or
       expression even when it starts with a minus. *)
    | [ "--"; argument ] -> input argument []
    | flag :: _ -> Error ("Unknown flag: " ^ flag)
  in
  flags (Array.to_list argv) defaultOptions Normal

(*
   Validate parsed options
   Help and version override everything else
   Check for required argument
   Check for conflicting options
*)
let validateOptions (options : cliOptions) =
  let only flag allowed =
    if allowed then Ok () else Error (flag ^ " can't be combined with --check")
  in
  if options.help || options.version then Ok options
  else if options.check then
    let* () = only "--run" (not options.run) in
    let* () = only "-e" (not options.isExpression) in
    let* () = only "--entry" (Option.is_none options.entry) in
    let* () = only "-o" (Option.is_none options.outputFile) in
    if options.inputs = [] then
      Error "--check requires at least one package function or value name"
    else Ok options
  else if Option.is_some options.entry then
    match options.inputs with
    | [] when options.isExpression -> Error "--entry can't be combined with -e"
    | [] when Option.is_none options.packageServer ->
        Error "--entry requires --package-server"
    | [] when options.run && Option.is_some options.outputFile ->
        Error "Cannot specify output file with run mode (-r)"
    | [] -> Ok options
    | input :: _ -> Error ("Unexpected argument with --entry: " ^ input)
  else if List.length options.inputs > 1 then
    Error ("Unexpected argument: " ^ List.nth options.inputs 1)
  else if options.inputs = [] then
    Error "Missing input (filename, expression with -e, or --entry HASH)"
  else if options.run && Option.is_some options.outputFile then
    Error "Cannot specify output file with run mode (-r)"
  else if options.run && options.target <> HostTarget then
    Error "Explicit compiler targets are compile-only; remove --run"
  else if
    (Option.is_some options.dumpFunction
    || options.dumpIRSummary
    || Option.is_some options.dumpIROutput)
    && not
         (options.dumpANF || options.dumpMIR || options.dumpLIR
        || options.verbosity = DumpIR)
  then
    Error
      "--dump-function, --dump-ir-summary, and --dump-ir-output require an IR \
       dump selection"
  else Ok options

let parseBatchArgs argv =
  let rec extract seen reversed = function
    | [] -> (seen, List.rev reversed)
    | "--" :: _ as rest -> (seen, List.rev reversed @ rest)
    | "--leak-check" :: rest -> extract true reversed rest
    | arg :: rest -> extract seen (arg :: reversed) rest
  in
  let leakCheck, args = extract false [] (Array.to_list argv) in
  let items args =
    let rec loop reversed = function
      | [] -> (
          match List.rev reversed with
          | [] ->
              Error "Batch compilation requires at least one SOURCE OUTPUT pair"
          | first :: rest -> Ok (first, rest))
      | [ _ ] ->
          Error
            "Batch compilation requires an output path after every source path"
      | source :: output :: rest ->
          if Text.trim source = "" || Text.trim output = "" then
            Error "Batch source and output paths must be non-empty"
          else
            loop
              ({
                 kind = "source";
                 name = source;
                 sourceFile = source;
                 outputFile = output;
               }
              :: reversed)
              rest
    in
    loop [] args
  in
  let rec flags target verbosity allowInternal packageServer cacheDir
      manifestPath keepGoing reportPath args =
    let resume target packageServer manifestPath keepGoing reportPath rest =
      flags target verbosity allowInternal packageServer cacheDir manifestPath
        keepGoing reportPath rest
    in
    let cacheDirValue value rest =
      if Option.is_some cacheDir then
        Error "Cache directory specified multiple times"
      else
        let* directory = parseCacheDirValue value in
        flags target verbosity allowInternal packageServer (Some directory)
          manifestPath keepGoing reportPath rest
    in
    let targetValue value rest =
      match target with
      | ExplicitTarget _ -> Error "Target specified multiple times"
      | HostTarget ->
          let* target = parseTargetValue value in
          resume target packageServer manifestPath keepGoing reportPath rest
    in
    let serverValue value rest =
      if Option.is_some packageServer then
        Error "Package server specified multiple times"
      else
        let* server = parsePackageServerValue value in
        resume target (Some server) manifestPath keepGoing reportPath rest
    in
    let pathValue manifest value rest =
      let previous, duplicate, empty =
        if manifest then
          ( manifestPath,
            "Batch manifest specified multiple times",
            "--manifest requires a non-empty path" )
        else
          ( reportPath,
            "Batch report specified multiple times",
            "--report requires a non-empty path" )
      in
      if Option.is_some previous then Error duplicate
      else if Text.trim value = "" then Error empty
      else
        resume target packageServer
          (if manifest then Some value else manifestPath)
          keepGoing
          (if manifest then reportPath else Some value)
          rest
    in
    let finish input =
      Ok
        {
          target;
          verbosity;
          leakCheck;
          allowInternal;
          packageServer;
          cacheDir;
          input;
          keepGoing;
          reportPath;
        }
    in
    match args with
    | "--" :: rest ->
        if Option.is_some manifestPath then
          Error
            "Batch compilation cannot combine --manifest with SOURCE OUTPUT \
             pairs"
        else
          let* first, rest = items rest in
          finish (CommandLineItems (first, rest))
    | ("-q" | "--quiet") :: rest ->
        flags target Quiet allowInternal packageServer cacheDir manifestPath
          keepGoing reportPath rest
    | "--allow-internal" :: rest ->
        flags target verbosity true packageServer cacheDir manifestPath
          keepGoing reportPath rest
    | "--cache-dir" :: value :: rest -> cacheDirValue value rest
    | [ "--cache-dir" ] -> Error "Missing value for --cache-dir"
    | flag :: rest when String.starts_with ~prefix:"--cache-dir=" flag ->
        cacheDirValue (String.sub flag 12 (String.length flag - 12)) rest
    | "--keep-going" :: rest ->
        if keepGoing then Error "Keep-going specified multiple times"
        else resume target packageServer manifestPath true reportPath rest
    | "--manifest" :: value :: rest -> pathValue true value rest
    | [ "--manifest" ] -> Error "Missing value for --manifest"
    | flag :: rest when String.starts_with ~prefix:"--manifest=" flag ->
        pathValue true (String.sub flag 11 (String.length flag - 11)) rest
    | "--report" :: value :: rest -> pathValue false value rest
    | [ "--report" ] -> Error "Missing value for --report"
    | flag :: rest when String.starts_with ~prefix:"--report=" flag ->
        pathValue false (String.sub flag 9 (String.length flag - 9)) rest
    | "--package-server" :: value :: rest -> serverValue value rest
    | [ "--package-server" ] -> Error "Missing value for --package-server"
    | flag :: rest when String.starts_with ~prefix:"--package-server=" flag ->
        serverValue (String.sub flag 17 (String.length flag - 17)) rest
    | "--target" :: value :: rest -> targetValue value rest
    | [ "--target" ] ->
        Error "Missing value for --target (expected 'linux-x86_64')"
    | flag :: rest when String.starts_with ~prefix:"--target=" flag ->
        targetValue (String.sub flag 9 (String.length flag - 9)) rest
    | [] -> (
        match manifestPath with
        | None ->
            Error
              "Batch compilation requires --manifest or '--' before SOURCE \
               OUTPUT pairs"
        | Some path -> finish (ManifestFile path))
    | flag :: _ -> Error ("Unknown batch flag: " ^ flag)
  in
  flags HostTarget Normal false None None None false None args

(* `prepare --cache-dir DIR [--target T]`: only the stdlib, into DIR. *)
let parsePrepareArgs args =
  let rec flags target cacheDir = function
    | [] -> (
        match cacheDir with
        | Some directory -> Ok (PrepareCommand (target, directory))
        | None -> Error "prepare requires --cache-dir")
    | "--cache-dir" :: value :: rest ->
        let* directory = parseCacheDirValue value in
        flags target (Some directory) rest
    | flag :: rest when String.starts_with ~prefix:"--cache-dir=" flag ->
        flags target cacheDir
          ("--cache-dir" :: String.sub flag 12 (String.length flag - 12) :: rest)
    | "--target" :: value :: rest ->
        let* target = parseTargetValue value in
        flags target cacheDir rest
    | argument :: _ -> Error ("Unexpected prepare argument: " ^ argument)
  in
  flags HostTarget None args

let parseCommand argv =
  match Array.to_list argv with
  | "prepare" :: rest -> parsePrepareArgs rest
  | "--batch" :: rest ->
      Result.map
        (fun value -> BatchCommand value)
        (parseBatchArgs (Array.of_list rest))
  | _ ->
      let* options = parseArgs argv in
      let* options = validateOptions options in
      if options.check && not (options.help || options.version) then
        Ok
          (CheckCommand
             {
               checkTarget = options.target;
               checkPackageServer = options.packageServer;
               checkCacheDir = options.cacheDir;
               checkJson = options.json;
               checkNames = options.inputs;
             })
      else Ok (SingleCommand options)

let sourceDescription (options : cliOptions) =
  if options.isExpression then "<expression>"
  else
    match (options.entry, options.inputs) with
    | Some hash, _ -> "<entry " ^ hash ^ ">"
    | None, value :: _ -> value
    | None, [] ->
        Crash.crash
          "sourceDescription: compile/run called without a validated input \
           source"

(*
   Redirect compiler diagnostics and IR dumps at the CLI boundary. Explicit
   dump flags do not otherwise enable pass chatter, so their files contain
   only the requested representations.
*)
let withIRDumpOutput (options : cliOptions) compile =
  match options.dumpIROutput with
  | None -> Ok (compile ())
  | Some path ->
      let writer =
        try
          Ok
            (Unix.openfile path
               [ Unix.O_WRONLY; Unix.O_CLOEXEC; Unix.O_CREAT; Unix.O_TRUNC ]
               0o666)
        with exn ->
          Error
            ("Failed to open IR dump '" ^ path ^ "': " ^ Printexc.to_string exn)
      in
      let* writer = writer in
      Fun.protect
        ~finally:(fun () -> Unix.close writer)
        (fun () ->
          flush stdout;
          let original = Unix.dup ~cloexec:true Unix.stdout in
          let result =
            Fun.protect
              ~finally:(fun () ->
                flush stdout;
                Unix.dup2 original Unix.stdout;
                Unix.close original)
              (fun () ->
                Unix.dup2 writer Unix.stdout;
                compile ())
          in
          try
            flush stdout;
            Ok result
          with exn ->
            Error
              ("Failed to write IR dump to '" ^ path ^ "': "
             ^ Printexc.to_string exn))

let selectedTarget = function
  | HostTarget -> Platform.detectHostTarget ()
  | ExplicitTarget target -> Ok target

let packageConfig (options : cliOptions) =
  Option.map
    (fun server -> PackageManager.config ~server ~cacheDirectory:options.cacheDir)
    options.packageServer

let request stdlib source verbosity (options : cliOptions) : X.compileRequest =
  {
    X.context = X.StdlibOnly stdlib;
    mode =
      (if options.isExpression || options.emitResult then
         CompilerOptions.TestExpression
       else CompilerOptions.FullProgram);
    sources =
      NonEmptyList.singleton
        {
          X.name = sourceDescription options;
          purpose = NameSyntax.SourceUnitPurpose.Executable;
          source;
        };
    allowInternal = options.allowInternal;
    verbosity = verbosityToInt verbosity;
    options = buildCompilerOptions options;
    packageValues = X.emptyPackageValueCatalog;
    packageManager = packageConfig options;
    passTimingRecorder = None;
    session = None;
  }

let compileWithStdlib stdlib source outputPath verbosity (options : cliOptions)
    =
  if shouldShowNormal verbosity then
    print_endline ("Compiling: " ^ sourceDescription options);
  let* report =
    withIRDumpOutput options (fun () ->
        CompilerLibrary.compile (request stdlib source verbosity options))
  in
  let* binary =
    Result.map_error
      (fun message -> "Compilation failed: " ^ message)
      report.CompilerOptions.result
  in
  let output =
    match report.CompilerOptions.target with
    | Platform.ARM64Backend Platform.MacOSARM64 ->
        Binary_Generation_MachO.writeToFile outputPath binary
    | Platform.ARM64Backend Platform.LinuxARM64 | Platform.LinuxX86_64 ->
        Backend_Arm64_Binary_Generation_ELF.writeToFile outputPath binary
  in
  let* () =
    Result.map_error
      (fun message -> "Failed to write binary: " ^ message)
      output
  in
  if shouldShowNormal verbosity then
    Printf.printf "Successfully wrote %d bytes to %s\n" (Bytes.length binary)
      outputPath;
  Ok ()

let printDiagnostic json (diagnostic : Diagnostic.t) =
  if json then
    prerr_endline (Yojson.Basic.to_string (Diagnostic.toJson diagnostic))
  else
    prerr_endline
      (if diagnostic.Diagnostic.reason = Diagnostic.Usage then
         "Error: " ^ diagnostic.Diagnostic.message
       else diagnostic.Diagnostic.message)

(* The chain of package items from [root] to the item [diagnostic] failed
   in, ending at what that item uses and the compiler lacks. *)
let withPath config (stdlib : X.stdlibResult) root (diagnostic : Diagnostic.t)
    =
  match (config, diagnostic.Diagnostic.location) with
  | Some config, Some location -> (
      match
        PackageManager.callPath config
          stdlib.X.context.X.typeCheckEnv.Types.resolutionEnv root
          location.Diagnostic.item
      with
      | None -> diagnostic
      | Some path ->
          let hash =
            match (location.Diagnostic.hash, List.rev path) with
            | Some hash, _ -> Some hash
            | None, (_, hash) :: _ -> Some hash
            | None, [] -> None
          in
          {
            diagnostic with
            Diagnostic.location = Some { location with Diagnostic.hash };
            path =
              List.map fst path @ Option.to_list (Diagnostic.missing diagnostic);
          })
  | _ -> diagnostic

let fail (options : cliOptions) stdlib message =
  let diagnostic = Diagnostic.ofError message in
  let diagnostic =
    match (options.json, options.entry, stdlib) with
    | true, Some hash, Some stdlib ->
        withPath (packageConfig options) stdlib (PackageManager.Hash hash)
          diagnostic
    | _ -> diagnostic
  in
  printDiagnostic options.json diagnostic;
  1

(*
   Compile source expression to executable
*)
let compile ?progress source outputPath verbosity (options : cliOptions) =
  match selectedTarget options.target with
  | Error message -> fail options None ("Target detection failed: " ^ message)
  | Ok target -> (
      match StdlibCache.buildStdlib ~directory:options.cacheDir ?progress target with
      | Error message -> fail options None ("Compilation failed: " ^ message)
      | Ok stdlib -> (
          match compileWithStdlib stdlib source outputPath verbosity options with
          | Ok () -> 0
          | Error message -> fail options (Some stdlib) message))

let fileExists = FileIO.exists
let readText = FileIO.readText

let readSourceFile path =
  if not (fileExists path) then Error ("File not found: " ^ path)
  else
    try Ok (readText path)
    with exn ->
      Error ("Failed to read file '" ^ path ^ "': " ^ Printexc.to_string exn)

let readBatchManifest path =
  let* text = readSourceFile path in
  let decode index = function
    | `Assoc fields ->
        let field name =
          (* Manifest fields are case-sensitive; the last duplicate wins. *)
          let value =
            List.fold_left
              (fun found (key, value) ->
                if key = name then Some value else found)
              None fields
          in
          match value with
          | Some (`String value) when Text.trim value <> "" -> Ok value
          | _ ->
              Error
                (Printf.sprintf
                   "Batch manifest '%s' item %d: %s must be a non-empty string"
                   path (index + 1) name)
        in
        let* kind = field "kind" in
        let* name = field "name" in
        let* sourceFile = field "source" in
        let* outputFile = field "output" in
        Ok { kind; name; sourceFile; outputFile }
    | _ ->
        Error
          (Printf.sprintf "Batch manifest '%s' item %d must be an object" path
             (index + 1))
  in
  try
    match Yojson.Safe.from_string ~fname:path text with
    | `List [] ->
        Error ("Batch manifest '" ^ path ^ "' must contain at least one item")
    | `List entries -> (
        let* items = ResultList.traverse Fun.id (List.mapi decode entries) in
        match items with
        | first :: rest -> Ok (first, rest)
        | [] -> Crash.crash "Non-empty manifest decoded to no items")
    | _ -> Error ("Batch manifest '" ^ path ^ "' must contain a JSON array")
  with Yojson.Json_error message ->
    Error ("Failed to parse batch manifest '" ^ path ^ "': " ^ message)

let jsonString value = Yojson.Basic.to_string (`String value)

let roundMilliseconds value =
  let scaled = value *. 1000. in
  let lower = Float.floor scaled in
  let fraction = scaled -. lower in
  (if fraction > 0.5 || (fraction = 0.5 && mod_float lower 2. <> 0.) then
     lower +. 1.
   else lower)
  /. 1000.

let reportJson (value : batchReportItem) =
  let fields =
    [
      ("kind", jsonString value.kind);
      ("name", jsonString value.name);
      ("source", jsonString value.source);
      ("output", jsonString value.output);
      ("status", jsonString value.status);
      ("error", jsonString value.error);
      ("reason", jsonString value.reason);
      ("milliseconds", FloatFormat.roundTrip value.milliseconds);
    ]
  in
  "{"
  ^ String.concat ","
      (List.map (fun (key, value) -> jsonString key ^ ":" ^ value) fields)
  ^ "}"

let rec createDirectory path =
  if path <> "" && path <> "." && not (Sys.file_exists path) then (
    createDirectory (Filename.dirname path);
    Unix.mkdir path 0o777)

let writeBatchReport path reports =
  try
    createDirectory (Filename.dirname path);
    Out_channel.with_open_bin path (fun channel ->
        List.iter
          (fun value ->
            output_string channel (reportJson value);
            output_char channel '\n')
          reports);
    Ok ()
  with exn ->
    Error
      ("Failed to write batch report '" ^ path ^ "': " ^ Printexc.to_string exn)

let compileBatch ?progress (options : batchCliOptions) =
  let sources =
    let* first, rest =
      match options.input with
      | CommandLineItems (first, rest) -> Ok (first, rest)
      | ManifestFile path -> readBatchManifest path
    in
    ResultList.traverse
      (fun (item : batchCompileItem) ->
        Result.map
          (fun source -> (item, source))
          (readSourceFile item.sourceFile))
      (first :: rest)
  in
  match (selectedTarget options.target, sources) with
  | Error message, _ ->
      prerr_endline ("Target detection failed: " ^ message);
      1
  | _, Error message ->
      prerr_endline message;
      1
  | Ok target, Ok sources -> (
      match StdlibCache.buildStdlib ~directory:options.cacheDir ?progress target with
      | Error message ->
          prerr_endline ("Compilation failed: " ^ message);
          1
      | Ok stdlib -> (
          let rec loop failed reports = function
            | [] -> (failed, List.rev reports)
            | ((item : batchCompileItem), source) :: rest -> (
                let start = Int64.to_float (Mtime_clock.elapsed_ns ()) /. 1e6 in
                let cli =
                  {
                    defaultOptions with
                    inputs = [ item.sourceFile ];
                    outputFile = Some item.outputFile;
                    verbosity = options.verbosity;
                    leakCheck = options.leakCheck;
                    target = options.target;
                    allowInternal = options.allowInternal;
                    packageServer = options.packageServer;
                    cacheDir = options.cacheDir;
                  }
                in
                let compiled =
                  compileWithStdlib stdlib source item.outputFile
                    options.verbosity cli
                in
                let milliseconds =
                  roundMilliseconds
                    ((Int64.to_float (Mtime_clock.elapsed_ns ()) /. 1e6)
                    -. start)
                in
                let report status error =
                  {
                    kind = item.kind;
                    name = item.name;
                    source = item.sourceFile;
                    output = item.outputFile;
                    status;
                    error;
                    reason =
                      (if error = "" then ""
                       else Diagnostic.reasonText (Diagnostic.ofError error));
                    milliseconds;
                  }
                in
                match compiled with
                | Ok () -> loop failed (report "compiled" "" :: reports) rest
                | Error error ->
                    prerr_endline
                      ("Compilation failed for " ^ item.kind ^ " " ^ item.name
                     ^ ": " ^ error);
                    let reports = report "failed" error :: reports in
                    if options.keepGoing then loop true reports rest
                    else (true, List.rev reports))
          in
          let failed, reports = loop false [] sources in
          let written =
            match options.reportPath with
            | None -> Ok ()
            | Some path -> writeBatchReport path reports
          in
          match written with
          | Error message ->
              prerr_endline message;
              1
          | Ok () -> if failed then 1 else 0))

(* The probe references the item as a value, so the compiler must resolve,
   check and lower it and everything it calls, without needing arguments. *)
(* An interpreter helper (`__name`) is declared under its hosted `pkg'__`
   name, so the probe names it that way. *)
let checkProbe name =
  "let packageProbe = " ^ PackageManager.publicName name ^ " in 0L\n"

(* A generic fn referenced without a call has no type arguments, so its probe
   says nothing. It is probed again specialized to each type argument being
   Int64, then String, at each arity up to four: the arity is whichever does
   not fail with a count mismatch. *)
let genericProbeTypes = [ "Int64"; "String" ]
let genericProbeArities = [ 1; 2; 3; 4 ]

let typeArgumentText typ arity =
  "<" ^ String.concat ", " (List.init arity (fun _ -> typ)) ^ ">"

let typeArgumentCountMismatch error =
  Text.contains error "type argument count mismatch"

let checkReportJson name specialized (diagnostic : Diagnostic.t option) =
  let status =
    match diagnostic with
    | None -> "compiles"
    | Some diagnostic when Diagnostic.undetermined diagnostic -> "undetermined"
    | Some _ -> "failed"
  in
  Yojson.Basic.to_string
    (`Assoc
       [
         ("name", `String name);
         ("status", `String status);
         ( "typeArguments",
           match specialized with None -> `Null | Some args -> `String args );
         ( "reason",
           `String
             (Option.fold ~none:""
                ~some:(fun d -> Diagnostic.kind d.Diagnostic.reason)
                diagnostic) );
         ( "error",
           `String
             (Option.fold ~none:"" ~some:(fun d -> d.Diagnostic.message) diagnostic)
         );
         ( "diagnostic",
           match diagnostic with
           | None -> `Null
           | Some diagnostic -> Diagnostic.toJson diagnostic );
       ])

let checkItems ?progress (options : checkCliOptions) =
  match selectedTarget options.checkTarget with
  | Error message ->
      prerr_endline ("Target detection failed: " ^ message);
      1
  | Ok target -> (
      match StdlibCache.buildStdlib ~directory:options.checkCacheDir ?progress target with
      | Error message ->
          prerr_endline ("Compilation failed: " ^ message);
          1
      | Ok stdlib ->
          let config =
            Option.map
              (fun server ->
                PackageManager.config ~server
                  ~cacheDirectory:options.checkCacheDir)
              options.checkPackageServer
          in
          let output = Filename.temp_file "dark-check-" ".out" in
          Fun.protect
            ~finally:(fun () ->
              if Sys.file_exists output then Sys.remove output)
            (fun () ->
              List.fold_left
                (fun failed name ->
                  let cli =
                    {
                      defaultOptions with
                      (* As `dark compile -e` compiles it: the probe is an
                         expression, and only what it reaches is lowered. *)
                      isExpression = true;
                      inputs = [ "<check " ^ name ^ ">" ];
                      outputFile = Some output;
                      verbosity = Quiet;
                      target = options.checkTarget;
                      packageServer = options.checkPackageServer;
                      cacheDir = options.checkCacheDir;
                    }
                  in
                  let attempt probe =
                    match compileWithStdlib stdlib probe output Quiet cli with
                    | Ok () -> ""
                    | Error error -> error
                  in
                  let error = attempt (checkProbe name) in
                  (* The first specialization that compiles, or else the first
                     error that is not about the number of type arguments. *)
                  (* A trait-bounded generic (`min<'a: Compare>`) leaves its
                     obligation unresolved until it has type arguments. *)
                  let needsTypes =
                    (error <> "" && Diagnostic.undetermined (Diagnostic.ofError error))
                    || Text.contains error "Unresolved trait obligation '"
                  in
                  let error, specialized =
                    if not needsTypes then (error, None)
                    else
                      let rec probe typeFailure = function
                        | [] -> (Option.value typeFailure ~default:error, None)
                        | (typ, arity) :: rest -> (
                            let args = typeArgumentText typ arity in
                            match attempt (checkProbe (name ^ args)) with
                            | "" -> ("", Some args)
                            | failure
                              when typeArgumentCountMismatch failure
                                   || Option.is_some typeFailure ->
                                probe typeFailure rest
                            | failure -> probe (Some failure) rest)
                      in
                      probe None
                        (List.concat_map
                           (fun typ ->
                             List.map (fun arity -> (typ, arity)) genericProbeArities)
                           genericProbeTypes)
                  in
                  let diagnostic =
                    if error = "" then None
                    else Some (Diagnostic.ofError error)
                  in
                  (match diagnostic with
                  | _ when options.checkJson ->
                      print_endline
                        (checkReportJson name specialized
                           (Option.map
                              (withPath config stdlib (PackageManager.Name name))
                              diagnostic))
                  | None ->
                      print_endline
                        (name ^ ": compiles"
                        ^ Option.fold ~none:"" ~some:(fun args -> " at " ^ args)
                            specialized)
                  | Some diagnostic when Diagnostic.undetermined diagnostic ->
                      print_endline
                        (name ^ ": can't tell: " ^ Diagnostic.reasonText diagnostic)
                  | Some diagnostic ->
                      print_endline
                        (name ^ ": can't compile: "
                        ^ Diagnostic.reasonText diagnostic));
                  failed || error <> "")
                false options.checkNames
              |> fun failed -> if failed then 1 else 0))

(*
   Run an expression (compile to temp and execute)
   Use library for compile and run
*)
let run ?progress source verbosity (options : cliOptions) =
  let show = shouldShowNormal verbosity in
  if show then (
    print_endline ("Compiling and running: " ^ sourceDescription options);
    print_endline "---");
  let result =
    match Platform.detectHostTarget () with
    | Error message -> Error (None, "Target detection failed: " ^ message)
    | Ok target -> (
        match StdlibCache.buildStdlib ~directory:options.cacheDir ?progress target with
        | Error message -> Error (None, message)
        | Ok stdlib -> (
            let compiled =
              let* report =
                withIRDumpOutput options (fun () ->
                    CompilerLibrary.compile
                      (request stdlib source verbosity options))
              in
              let* binary = report.CompilerOptions.result in
              Ok
                (CompilerExecution.executeAttached
                   report.CompilerOptions.target (verbosityToInt verbosity)
                   binary)
            in
            match compiled with
            | Ok output -> Ok output
            | Error message -> Error (Some stdlib, message)))
  in
  let exitCode =
    match result with
    | Ok output -> output.CompilerOptions.exitCode
    | Error _ -> 1
  in
  if show then (
    print_endline "---";
    Printf.printf "Exit code: %d\n" exitCode);
  match result with
  | Ok output ->
      if output.CompilerOptions.stderr <> "" then
        prerr_endline output.CompilerOptions.stderr;
      exitCode
  | Error (stdlib, message) -> fail options stdlib message

(*
   Print version information
*)
let versionLines () =
  [
    "Dark Compiler v0.1.0";
    "Darklang native compiler for macOS and Linux";
    "Build: " ^ BuildInfo.hash;
  ]

let printVersion () = List.iter print_endline (versionLines ())

(*
   Print usage information
*)
let usageLines =
  [
    "Dark Compiler v0.1.0";
    "";
    "Usage:";
    "  darkc FILE [-o OUTPUT]               Compile a file to an executable";
    "  darkc -e EXPRESSION [-o OUTPUT]      Compile an expression; the executable prints its value";
    "  darkc --entry HASH --package-server URL [-o OUTPUT]";
    "                                       Compile the package fn with this hash as an executable";
    "  darkc -r FILE | -r -e EXPRESSION | -r --entry HASH";
    "                                       Compile and run";
    "  darkc --check [--json] --package-server URL NAME...";
    "                                       Report whether each package fn or value compiles, and why not";
    "  darkc prepare --cache-dir DIR        Prepare the stdlib in DIR and nothing else";
    "  darkc --batch [OPTIONS] -- SOURCE OUTPUT [SOURCE OUTPUT ...]";
    "  darkc --batch [OPTIONS] --manifest FILE";
    "";
    "Flags:";
    "  -r, --run              Run instead of compile (shows exit code)";
    "  -e, --expression       Treat the argument as an expression (- reads stdin)";
    "  --entry HASH           Compile the package fn HASH: it takes List<String> (the";
    "                         command-line words) or nothing, and returns Unit (exit 0)";
    "                         or Int64 (the exit code)";
    "  --check                Report whether each NAME compiles, and why not";
    "  --json                 Errors on stderr, and --check results, as JSON lines";
    "  --package-server URL   Resolve referenced hosted packages from URL";
    "  --cache-dir DIR        Keep the prepared stdlib and package answers in DIR";
    "                         (without it, nothing is kept between runs)";
    "  --target TARGET        Compile for linux-x86_64 instead of the host";
    "  -o, --output FILE      Output file (default: dark.out)";
    "  --emit-result          Print a file's final expression result when executed";
    "  -q, --quiet            Suppress compilation output";
    "  --progress=json        Report stdlib preparation as JSON lines on stderr";
    "  -v, --verbose          Show compilation pass names (-vv adds timing, -vvv dumps IRs)";
    "  --leak-check           Enable leak checking, including in batch mode (debug builds only)";
    "  -h, --help             Show this help message";
    "  --version              Show version information";
    "";
    "Batch flags:";
    "  --manifest FILE        Read labeled batch inputs from a JSON manifest";
    "  --keep-going           Continue batch compilation after individual failures";
    "  --report FILE          Write one JSON object per batch result";
    "";
    "IR dumps (for debugging):";
    "  --dump-anf, --dump-mir, --dump-lir   Dump that representation";
    "  --dump-function TEXT   Restrict IR dumps to matching function names";
    "  --dump-ir-summary      Emit function/block/instruction counts instead of full IR";
    "  --dump-ir-output FILE  Write compiler IR output to FILE instead of stdout";
    "";
    "Optimization flags (for debugging):";
    "  --disable-opt-anf      Disable ANF-level optimizations";
    "  --disable-opt-inline   Disable function inlining";
    "  --disable-opt-tco      Disable tail call optimization";
    "  --disable-opt-mir      Disable MIR-level optimizations";
    "  --disable-opt-lir      Disable LIR-level optimizations";
    "  --disable-opt-function-tree-shaking  Disable function tree shaking (alias --disable-opt-dce)";
    "  --disable-opt-freelist Disable free list memory reuse";
    "";
    "Flags can appear in any order, and short flags combine (-qr, -re, -vvre).";
    "";
    "Examples:";
    "  darkc prog.dark -o prog            Compile a file to 'prog'";
    "  darkc -r -e \"2 + 3\"               Run an expression";
    "  darkc -r -e - < input.txt          Run an expression read from stdin";
    "  darkc --entry 3f9a... --package-server http://127.0.0.1:9098 --cache-dir ~/.cache/darkc -o wc";
    "  darkc --check --json --package-server http://127.0.0.1:9098 Stdlib.English.toOrdinal";
    "  darkc --batch -q -- a.dark a.out b.dark b.out";
    "";
    "Generated executables may need code signing to run on macOS.";
  ]

let printUsage () = List.iter print_endline usageLines

let readSource (options : cliOptions) =
  match options.inputs with
  | [ "-" ] -> (
      try
        let source = Utf8.utf8 (In_channel.input_all stdin) in
        if Text.trim source = "" then Error "No input provided on stdin"
        else Ok source
      with exn -> Error ("Failed to read from stdin: " ^ Printexc.to_string exn))
  | [ value ] when options.isExpression -> Ok value
  | [ path ] -> (
      if not (fileExists path) then Error ("File not found: " ^ path)
      else
        try Ok (readText path)
        with exn -> Error ("Failed to read file: " ^ Printexc.to_string exn))
  | _ -> Error "No source provided"

(* What an executable of the package fn [hash] runs: the fn, called with the
   command-line words if it takes List<String>, or with nothing. An Int64
   result is the exit code; as `dark run` has it, one outside 0-255 is
   reported and exits 1, since the OS keeps only the low 8 bits. *)
let entrySource (options : cliOptions) hash =
  let refused reason message location =
    Error { Diagnostic.reason; location; path = []; message }
  in
  match packageConfig options with
  | None -> Error (Diagnostic.usage "--entry requires --package-server")
  | Some config -> (
      match PackageManager.entryFunction config hash with
      | Error message -> Error (Diagnostic.ofError message)
      | Ok None ->
          refused (Diagnostic.EntryNotFound hash)
            ("No package fn has the hash " ^ hash)
            None
      | Ok (Some (name, parameters, returnType)) -> (
          let arguments =
            String.concat "\n"
              [
                "let darkcArguments(i: Int64, acc: List<String>): List<String> =";
                "  match Stdlib.Cli.__argv(i) with";
                "  | Some(a) -> darkcArguments(i + 1, Stdlib.List.prepend(acc, a))";
                "  | None -> Stdlib.List.reverse(acc)";
                "";
                "let darkcExit(code: Int64): Unit =";
                "  if code >= 0 && code <= 255 then Stdlib.Cli.__exit(code)";
                "  else";
                "    let _ = Stdlib.printErrorLine($\"Script returned {Stdlib.Int64.toString(code)}, but an exit code must be 0-255.\")";
                "    Stdlib.Cli.__exit(1)";
                "";
                "";
              ]
          in
          let call =
            match parameters with
            | [] | [ "Unit" ] -> Some (name ^ "()")
            | [ "List<String>" ] -> Some (name ^ "(darkcArguments(0, []))")
            | _ -> None
          in
          match (call, returnType) with
          | Some call, "Unit" -> Ok (arguments ^ call ^ "\n")
          | Some call, "Int64" ->
              Ok (arguments ^ "darkcExit(" ^ call ^ ")\n")
          | _ ->
              let location =
                { Diagnostic.item = name; hash = Some hash; line = None; column = None }
              in
              let diagnostic =
                {
                  Diagnostic.reason =
                    Diagnostic.NotAnEntry { parameters; returnType };
                  location = Some location;
                  path = [ name ];
                  message = "";
                }
              in
              Error
                {
                  diagnostic with
                  Diagnostic.message =
                    name ^ " can't be an executable: "
                    ^ Diagnostic.reasonText diagnostic;
                }))

(* --progress=json applies to every command, so it comes out before parsing. *)
let progressFirst argv =
  let rec split json reversed = function
    | "--progress=json" :: rest -> split true reversed rest
    | "--" :: _ as rest -> (json, List.rev_append reversed rest)
    | arg :: rest -> split json (arg :: reversed) rest
    | [] -> (json, List.rev reversed)
  in
  let json, args = split false [] (Array.to_list argv) in
  ((if json then Some (StdlibCache.printJson ()) else None), Array.of_list args)

let main argv =
  let progress, argv = progressFirst argv in
  try
    match parseCommand argv with
    | Error message ->
        if Array.mem "--json" argv then
          printDiagnostic true (Diagnostic.usage message)
        else (
          prerr_endline ("Error: " ^ message);
          prerr_endline "Run 'darkc --help' for usage.");
        1
    | Ok (SingleCommand options) when options.help ->
        printUsage ();
        0
    | Ok (SingleCommand options) when options.version ->
        printVersion ();
        0
    | Ok (BatchCommand options) -> compileBatch ?progress options
    | Ok (CheckCommand options) -> checkItems ?progress options
    | Ok (PrepareCommand (target, directory)) -> (
        let prepared =
          let* target = selectedTarget target in
          StdlibCache.buildStdlib ~directory:(Some directory) ?progress target
        in
        match prepared with
        | Ok _ -> 0
        | Error message ->
            prerr_endline ("Error: " ^ message);
            1)
    | Ok (SingleCommand options) -> (
        let source =
          match options.entry with
          | Some hash -> entrySource options hash
          | None -> Result.map_error Diagnostic.usage (readSource options)
        in
        match source with
        | Error diagnostic ->
            printDiagnostic options.json diagnostic;
            1
        | Ok source ->
            (* The entry's own wrapper reads argv and exits through the
               compiler's private helpers. *)
            let options =
              if Option.is_some options.entry then
                { options with allowInternal = true }
              else options
            in
            if options.run then run ?progress source options.verbosity options
            else
              compile ?progress source
                (Option.value ~default:"dark.out" options.outputFile)
                options.verbosity options)
  with exn ->
    prerr_endline ("Error: " ^ Printexc.to_string exn);
    prerr_endline (Printexc.get_backtrace ());
    1
