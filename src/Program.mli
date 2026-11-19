(* Program.mli - Command-line entry, batch compilation and native execution. *)
type verbosityLevel = Quiet | Normal | Verbose | VeryVerbose | DumpIR
type targetSelection = HostTarget | ExplicitTarget of Platform.target

type batchCompileItem = {
  kind : string;
  name : string;
  sourceFile : string;
  outputFile : string;
}

type batchInput =
  | CommandLineItems of batchCompileItem * batchCompileItem list
  | ManifestFile of string

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

val verbosityToInt : verbosityLevel -> int
val shouldShowNormal : verbosityLevel -> bool
val defaultOptions : cliOptions
val buildCompilerOptions : cliOptions -> CompilerOptions.compilerOptions
val parseArgs : string array -> (cliOptions, string) result
val validateOptions : cliOptions -> (cliOptions, string) result
val parseBatchArgs : string array -> (batchCliOptions, string) result
val parseCommand : string array -> (cliCommand, string) result
val compile :
  ?progress:(StdlibCache.progress -> unit) ->
  string -> string -> verbosityLevel -> cliOptions -> int
val compileBatch :
  ?progress:(StdlibCache.progress -> unit) ->
  batchCliOptions -> int
val checkItems :
  ?progress:(StdlibCache.progress -> unit) ->
  checkCliOptions -> int
val run :
  ?progress:(StdlibCache.progress -> unit) ->
  string -> verbosityLevel -> cliOptions -> int
val versionLines : unit -> string list
val printVersion : unit -> unit
val printUsage : unit -> unit
val main : string array -> int
