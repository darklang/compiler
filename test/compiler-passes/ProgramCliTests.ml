(* ProgramCliTests.ml - Compiler CLI target-selection tests. *)
[@@@warning "-4-42"]

open Dark_compiler
module P = Program
module V = StructuralFormat

type testResult = (unit, string) result

let boolean value = V.Scalar (if value then "true" else "false")

let optional f = function
  | None -> V.Union ("None", [])
  | Some value -> V.Union ("Some", [ f value ])

let verbosity value =
  V.Union
    ( (match value with
      | P.Quiet -> "Quiet"
      | P.Normal -> "Normal"
      | P.Verbose -> "Verbose"
      | P.VeryVerbose -> "VeryVerbose"
      | P.DumpIR -> "DumpIR"),
      [] )

let targetValue = function
  | P.HostTarget -> V.Union ("HostTarget", [])
  | P.ExplicitTarget value ->
      V.Union
        ( "ExplicitTarget",
          [
            (match value with
            | Platform.LinuxX86_64 -> V.Union ("LinuxX86_64", [])
            | Platform.ARM64Backend Platform.LinuxARM64 ->
                V.Union ("ARM64Backend", [ V.Union ("LinuxARM64", []) ])
            | Platform.ARM64Backend Platform.MacOSARM64 ->
                V.Union ("ARM64Backend", [ V.Union ("MacOSARM64", []) ]));
          ] )

let formatTarget value = V.format (targetValue value)

let optionsValue (value : P.cliOptions) =
  V.Record
    [
      ("Run", boolean value.P.run);
      ("IsExpression", boolean value.P.isExpression);
      ("Check", boolean value.P.check);
      ("Json", boolean value.P.json);
      ("OutputFile", (optional (fun value -> V.Text value)) value.P.outputFile);
      ("Verbosity", verbosity value.P.verbosity);
      ("Help", boolean value.P.help);
      ("Version", boolean value.P.version);
      ("Inputs", V.Sequence (List.map (fun value -> V.Text value) value.P.inputs));
      ("LeakCheck", boolean value.P.leakCheck);
      ("Target", targetValue value.P.target);
      ("EmitResult", boolean value.P.emitResult);
      ( "PackageServer",
        (optional (fun value -> V.Scalar value)) value.P.packageServer );
      ("CacheDir", (optional (fun value -> V.Text value)) value.P.cacheDir);
      ("Entry", (optional (fun value -> V.Text value)) value.P.entry);
      ("AllowInternal", boolean value.P.allowInternal);
      ("DisableFreeList", boolean value.P.disableFreeList);
      ("DisableANFOpt", boolean value.P.disableANFOpt);
      ("DisableANFConstFolding", boolean value.P.disableANFConstFolding);
      ("DisableANFConstProp", boolean value.P.disableANFConstProp);
      ("DisableANFCopyProp", boolean value.P.disableANFCopyProp);
      ("DisableANFDCE", boolean value.P.disableANFDCE);
      ( "DisableANFStrengthReduction",
        boolean value.P.disableANFStrengthReduction );
      ("DisableInlining", boolean value.P.disableInlining);
      ("DisableTCO", boolean value.P.disableTCO);
      ("DisableMIROpt", boolean value.P.disableMIROpt);
      ("DisableMIRSCCP", boolean value.P.disableMIRSCCP);
      ("DisableMIRCSE", boolean value.P.disableMIRCSE);
      ("DisableMIRDCE", boolean value.P.disableMIRDCE);
      ("DisableMIRLICM", boolean value.P.disableMIRLICM);
      ("DisableLIROpt", boolean value.P.disableLIROpt);
      ("DisableLIRPeephole", boolean value.P.disableLIRPeephole);
      ("DisableFunctionTreeShaking", boolean value.P.disableFunctionTreeShaking);
      ("DumpANF", boolean value.P.dumpANF);
      ("DumpMIR", boolean value.P.dumpMIR);
      ("DumpLIR", boolean value.P.dumpLIR);
      ( "DumpFunction",
        (optional (fun value -> V.Text value)) value.P.dumpFunction );
      ("DumpIRSummary", boolean value.P.dumpIRSummary);
      ( "DumpIROutput",
        (optional (fun value -> V.Text value)) value.P.dumpIROutput );
    ]

let formatOptions value = V.format (optionsValue value)

let itemValue (value : P.batchCompileItem) =
  V.Record
    [
      ("Kind", V.Text value.P.kind);
      ("Name", V.Text value.P.name);
      ("SourceFile", V.Text value.P.sourceFile);
      ("OutputFile", V.Text value.P.outputFile);
    ]

let batchValue (value : P.batchCliOptions) =
  V.Record
    [
      ("Target", targetValue value.P.target);
      ("Verbosity", verbosity value.P.verbosity);
      ("LeakCheck", boolean value.P.leakCheck);
      ("AllowInternal", boolean value.P.allowInternal);
      ( "PackageServer",
        optional (fun value -> V.Scalar value) value.P.packageServer );
      ( "Input",
        match value.P.input with
        | P.ManifestFile path -> V.Union ("ManifestFile", [ V.Text path ])
        | P.CommandLineItems (first, rest) ->
            V.Union
              ( "CommandLineItems",
                [
                  V.Tuple
                    [ itemValue first; V.Sequence (List.map itemValue rest) ];
                ] ) );
      ("KeepGoing", boolean value.P.keepGoing);
      ("ReportPath", optional (fun value -> V.Text value) value.P.reportPath);
    ]

let formatCommand = function
  | P.PrepareCommand (target, directory) ->
      V.format (V.Union ("PrepareCommand", [ targetValue target; V.Text directory ]))
  | P.SingleCommand value ->
      V.format (V.Union ("SingleCommand", [ optionsValue value ]))
  | P.BatchCommand value ->
      V.format (V.Union ("BatchCommand", [ batchValue value ]))
  | P.CheckCommand value ->
      V.format
        (V.Union
           ( "CheckCommand",
             [
               boolean value.P.checkJson;
               optional (fun value -> V.Scalar value) value.P.checkPackageServer;
               optional (fun value -> V.Text value) value.P.checkCacheDir;
               V.Sequence (List.map (fun name -> V.Scalar name) value.P.checkNames);
             ] ))

let testExplicitLinuxX86_64Target () =
  match P.parseArgs [| "--target=linux-x86_64"; "program.dark" |] with
  | Ok options when options.P.target = P.ExplicitTarget Platform.LinuxX86_64 ->
      Ok ()
  | Ok options ->
      Error
        ("Expected explicit Linux x86_64 target, got "
        ^ formatTarget options.P.target)
  | Error error -> Error ("Expected target parsing to succeed, got: " ^ error)

let testUnknownTargetRejected () =
  match P.parseArgs [| "--target=windows-x86_64"; "program.dark" |] with
  | Error error when Text.contains error "linux-x86_64" -> Ok ()
  | Error error -> Error ("Expected supported-target guidance, got: " ^ error)
  | Ok _ -> Error "Expected unknown target to be rejected"

let testCrossTargetRunRejected () =
  match
    Result.bind
      (P.parseArgs [| "--run"; "--target=linux-x86_64"; "program.dark" |])
      P.validateOptions
  with
  | Error error when Text.contains error "compile-only" -> Ok ()
  | Error error -> Error ("Expected compile-only guidance, got: " ^ error)
  | Ok _ -> Error "Expected cross-target run mode to be rejected"

let testEmitResultModeIsExplicit () =
  match P.parseArgs [| "--emit-result"; "program.dark" |] with
  | Ok options when options.P.emitResult -> Ok ()
  | Ok _ ->
      Error
        "Expected --emit-result to select observable file-result compilation"
  | Error error ->
      Error ("Expected --emit-result parsing to succeed, got: " ^ error)

let testExpressionAfterDoubleDash () =
  match P.parseArgs [| "-e"; "--"; "-7.5dec % 2dec" |] with
  | Ok options
    when options.P.isExpression && options.P.inputs = [ "-7.5dec % 2dec" ] ->
      Ok ()
  | Ok options -> Error ("Unexpected expression options: " ^ formatOptions options)
  | Error error ->
      Error ("Expected an expression after -- to parse, got: " ^ error)

let testPackageServerIsExplicit () =
  match
    P.parseArgs [| "--package-server=http://127.0.0.1:9090"; "program.dark" |]
  with
  | Ok options when options.P.packageServer = Some "http://127.0.0.1:9090/" ->
      Ok ()
  | Ok options ->
      Error ("Unexpected package server options: " ^ formatOptions options)
  | Error error ->
      Error ("Expected package server parsing to succeed, got: " ^ error)

let testPackageServerRejectsNonHttpUrl () =
  match
    P.parseArgs [| "--package-server=file:///tmp/packages"; "program.dark" |]
  with
  | Error error when Text.contains error "HTTP(S)" -> Ok ()
  | Error error ->
      Error ("Expected HTTP(S) package server guidance, got: " ^ error)
  | Ok _ -> Error "Expected a non-HTTP package server URL to be rejected"

let testScopedIRDumpOptions () =
  match
    Result.bind
      (P.parseArgs
         [|
           "--dump-anf";
           "--dump-function=map";
           "--dump-ir-summary";
           "--dump-ir-output=artifacts/map.ir";
           "program.dark";
         |])
      P.validateOptions
  with
  | Ok options
    when options.P.dumpANF
         && options.P.dumpFunction = Some "map"
         && options.P.dumpIRSummary
         && options.P.dumpIROutput = Some "artifacts/map.ir" ->
      Ok ()
  | Ok options ->
      Error ("Unexpected scoped IR dump options: " ^ formatOptions options)
  | Error error ->
      Error ("Expected scoped IR dump options to parse, got: " ^ error)

let testIRDumpModifiersRequireDumpSelection () =
  match
    Result.bind
      (P.parseArgs [| "--dump-function=map"; "program.dark" |])
      P.validateOptions
  with
  | Error error when Text.contains error "require an IR dump" -> Ok ()
  | Error error -> Error ("Expected IR dump selection guidance, got: " ^ error)
  | Ok _ ->
      Error "Expected a dump function filter without an IR selection to fail"

let testEmptyIRDumpValuesRejected () =
  match P.parseArgs [| "--dump-anf"; "--dump-function="; "program.dark" |] with
  | Error error when Text.contains error "non-empty" -> Ok ()
  | Error error ->
      Error ("Expected non-empty dump filter guidance, got: " ^ error)
  | Ok _ -> Error "Expected an empty dump function filter to fail"

let testBatchCompileParsesIndependentOutputs () =
  match
    P.parseCommand
      [|
        "--batch";
        "--quiet";
        "--package-server=http://127.0.0.1:9090";
        "--";
        "first.dark";
        "first.out";
        "second.dark";
        "second.out";
      |]
  with
  | Ok (P.BatchCommand options) -> (
      match options.P.input with
      | P.CommandLineItems (first, rest) -> (
          match first :: rest with
          | [ first; second ]
            when options.P.verbosity = P.Quiet
                 && options.P.packageServer = Some "http://127.0.0.1:9090/"
                 && first.P.sourceFile = "first.dark"
                 && first.P.outputFile = "first.out"
                 && second.P.sourceFile = "second.dark"
                 && second.P.outputFile = "second.out" ->
              Ok ()
          | items ->
              Error
                ("Unexpected batch compile items: "
                ^ V.format (V.Sequence (List.map itemValue items))))
      | P.ManifestFile path ->
          Error ("Expected command-line items, got manifest " ^ path))
  | Ok command -> Error ("Expected batch command, got: " ^ formatCommand command)
  | Error error -> Error ("Expected batch command to parse, got: " ^ error)

let testBatchCompileRejectsMissingOutput () =
  match P.parseCommand [| "--batch"; "--"; "only-source.dark" |] with
  | Error error when Text.contains error "output path" -> Ok ()
  | Error error -> Error ("Expected missing-output guidance, got: " ^ error)
  | Ok _ -> Error "Expected an unmatched batch source to be rejected"

let testBatchManifestKeepGoingParses () =
  match
    P.parseCommand
      [|
        "--batch";
        "--package-server=http://127.0.0.1:9090";
        "--manifest";
        "package-probes.json";
        "--keep-going";
        "--report";
        "package-report.jsonl";
      |]
  with
  | Ok (P.BatchCommand options) -> (
      match (options.P.input, options.P.reportPath) with
      | P.ManifestFile "package-probes.json", Some "package-report.jsonl"
        when options.P.keepGoing
             && options.P.packageServer = Some "http://127.0.0.1:9090/" ->
          Ok ()
      | _ ->
          Error
            ("Unexpected manifest batch options: "
            ^ V.format (batchValue options)))
  | Ok command ->
      Error ("Expected manifest batch command, got: " ^ formatCommand command)
  | Error error ->
      Error ("Expected manifest batch compilation to parse, got: " ^ error)

let testBatchCompileAllowsCompilerOwnedSources () =
  match
    P.parseCommand
      [|
        "--batch"; "--allow-internal"; "--"; "benchmark.dark"; "benchmark.out";
      |]
  with
  | Ok (P.BatchCommand options) when options.P.allowInternal -> Ok ()
  | Ok command ->
      Error
        ("Expected internal batch compilation, got: " ^ formatCommand command)
  | Error error ->
      Error ("Expected --allow-internal to parse in batch mode, got: " ^ error)

(* A batch report names what a failed item uses that the compiler lacks. *)
let testBatchReasonNamesUnsupportedFeature () =
  let expect error reason =
    let actual = Diagnostic.reasonText (Diagnostic.ofError error) in
    if actual = reason then Ok ()
    else Error ("Expected reason '" ^ reason ^ "', got '" ^ actual ^ "'")
  in
  let ( let* ) = Result.bind in
  let* () =
    expect
      "Package Hosted.f could not be loaded: can't compile: uses a record \
       pattern, which this compiler does not support\nmore"
      "uses a record pattern, which this compiler does not support"
  in
  let* () =
    expect "<entry>: Unknown function or value 'Darklang.Stdlib.Dict.containsKey'"
      "uses function or value Stdlib.Dict.containsKey, which the compiler does \
       not define"
  in
  let* () =
    expect "x.dark: Unknown type 'Stdlib.Decimal.Decimal'"
      "uses type Stdlib.Decimal.Decimal, which the compiler does not define"
  in
  let* () =
    expect
      "Compilation failed: ANF conversion error: Cannot infer specialization \
       for function value 'Darklang.Stdlib.List.map'"
      "Stdlib.List.map is generic; compiling it needs a call with concrete \
       type arguments"
  in
  let* () =
    expect "Type mismatch: expected Int64\nat line 2"
      "compiler error: Type mismatch: expected Int64"
  in
  Ok ()

let testCheckParsesNamesInOrder () =
  match
    P.parseCommand
      [|
        "--check"; "Hosted.first"; "--json";
        "--package-server=http://127.0.0.1:9090"; "Hosted.second";
      |]
  with
  | Ok (P.CheckCommand options)
    when options.P.checkJson
         && options.P.checkNames = [ "Hosted.first"; "Hosted.second" ]
         && options.P.checkPackageServer = Some "http://127.0.0.1:9090/" ->
      Ok ()
  | Ok command -> Error ("Expected check command, got: " ^ formatCommand command)
  | Error error -> Error ("Expected check command to parse, got: " ^ error)

let testCheckRequiresAName () =
  match P.parseCommand [| "--check"; "--json" |] with
  | Error error when Text.contains error "at least one" -> Ok ()
  | Error error -> Error ("Expected missing-name guidance, got: " ^ error)
  | Ok command -> Error ("Expected a nameless check to be rejected, got: " ^ formatCommand command)

(* --check is a flag like any other: it may follow the shared ones. *)
let testCheckAfterSharedFlags () =
  match
    P.parseCommand
      [|
        "--package-server"; "http://127.0.0.1:9090"; "--cache-dir"; "/tmp/c";
        "--check"; "Hosted.first";
      |]
  with
  | Ok (P.CheckCommand options)
    when options.P.checkNames = [ "Hosted.first" ]
         && options.P.checkCacheDir = Some "/tmp/c"
         && options.P.checkPackageServer = Some "http://127.0.0.1:9090/" ->
      Ok ()
  | Ok command -> Error ("Expected check command, got: " ^ formatCommand command)
  | Error error -> Error ("Expected check command to parse, got: " ^ error)

let testCheckRejectsCompileFlags () =
  match P.parseCommand [| "--check"; "-o"; "out"; "Hosted.first" |] with
  | Error error when Text.contains error "can't be combined with --check" -> Ok ()
  | Error error -> Error ("Expected a conflict, got: " ^ error)
  | Ok command -> Error ("Expected -o with --check to fail, got: " ^ formatCommand command)

let testEntryParses () =
  match
    P.parseCommand
      [|
        "--entry"; "0f11ab"; "--package-server=http://127.0.0.1:9090";
        "--cache-dir=relative"; "-o"; "wc";
      |]
  with
  | Ok (P.SingleCommand options)
    when options.P.entry = Some "0f11ab"
         && options.P.inputs = []
         && options.P.outputFile = Some "wc"
         && options.P.cacheDir
            = Some (Filename.concat (Sys.getcwd ()) "relative") ->
      Ok ()
  | Ok command -> Error ("Expected an entry compile, got: " ^ formatCommand command)
  | Error error -> Error ("Expected --entry to parse, got: " ^ error)

let testEntryNeedsAPackageServer () =
  match P.parseCommand [| "--entry"; "0f11ab" |] with
  | Error error when Text.contains error "--package-server" -> Ok ()
  | Error error -> Error ("Expected package-server guidance, got: " ^ error)
  | Ok command -> Error ("Expected --entry alone to fail, got: " ^ formatCommand command)

let testEntryTakesNoSource () =
  match
    P.parseCommand
      [| "--entry"; "0f11ab"; "--package-server=http://127.0.0.1:1"; "x.dark" |]
  with
  | Error error when Text.contains error "x.dark" -> Ok ()
  | Error error -> Error ("Expected the extra source to be named, got: " ^ error)
  | Ok command -> Error ("Expected a source with --entry to fail, got: " ^ formatCommand command)

let testDiagnosticNamesItemAndReason () =
  let diagnostic =
    Diagnostic.ofError
      "Compilation failed: Darklang.Me.Tools.total: Unknown function or value \
       'Builtin.pmLocations'"
  in
  let json = Yojson.Basic.to_string (Diagnostic.toJson diagnostic) in
  let expected =
    {|{"reason":{"kind":"missing-builtin","name":"Builtin.pmLocations"},"location":{"item":"Darklang.Me.Tools.total","hash":null,"line":null,"column":null},"path":[],"message":"Compilation failed: Darklang.Me.Tools.total: Unknown function or value 'Builtin.pmLocations'"}|}
  in
  if json = expected then Ok () else Error ("Unexpected diagnostic: " ^ json)

let testDiagnosticReadsPackageUnitPosition () =
  let diagnostic =
    Diagnostic.ofError
      "Compilation failed: Parse error: package:Darklang.Me.Tools.hello@abc12: \
       error[PARSE-UNEXPECTED] at 2:7: unexpected token"
  in
  match diagnostic.Diagnostic.location with
  | Some
      {
        Diagnostic.item = "Darklang.Me.Tools.hello";
        hash = Some "abc12";
        line = Some 2;
        column = Some 7;
      } ->
      Ok ()
  | _ ->
      Error
        ("Unexpected location: "
        ^ Yojson.Basic.to_string (Diagnostic.toJson diagnostic))

let testPrepareNeedsACacheDir () =
  match (P.parseCommand [| "prepare" |], P.parseCommand [| "prepare"; "--cache-dir"; "/tmp/c" |]) with
  | Error error, Ok (P.PrepareCommand (P.HostTarget, "/tmp/c"))
    when Text.contains error "--cache-dir" ->
      Ok ()
  | _ -> Error "Expected prepare to need, and take, --cache-dir"

let tests =
  [
    ("prepare takes --cache-dir", testPrepareNeedsACacheDir);
    ("parse --check after shared flags", testCheckAfterSharedFlags);
    ("reject compile flags with --check", testCheckRejectsCompileFlags);
    ("parse an entry compile", testEntryParses);
    ("require a package server for --entry", testEntryNeedsAPackageServer);
    ("reject a source with --entry", testEntryTakesNoSource);
    ("diagnostic names the item and reason", testDiagnosticNamesItemAndReason);
    ("diagnostic reads a package unit position", testDiagnosticReadsPackageUnitPosition);
    ("parse check names in order", testCheckParsesNamesInOrder);
    ("reject check without a name", testCheckRequiresAName);
    ( "name what a failed batch item uses",
      testBatchReasonNamesUnsupportedFeature );
    ("parse explicit Linux x86_64 target", testExplicitLinuxX86_64Target);
    ("reject unknown compiler target", testUnknownTargetRejected);
    ("reject cross-target run mode", testCrossTargetRunRejected);
    ("parse explicit file-result mode", testEmitResultModeIsExplicit);
    ("parse an expression after --", testExpressionAfterDoubleDash);
    ("parse explicit package server", testPackageServerIsExplicit);
    ("reject non-HTTP package server", testPackageServerRejectsNonHttpUrl);
    ("parse scoped IR dump options", testScopedIRDumpOptions);
    ( "require an IR selection for dump modifiers",
      testIRDumpModifiersRequireDumpSelection );
    ("reject empty IR dump values", testEmptyIRDumpValuesRejected);
    ( "parse independent batch compile outputs",
      testBatchCompileParsesIndependentOutputs );
    ("reject batch source without output", testBatchCompileRejectsMissingOutput);
    ("parse keep-going manifest batch", testBatchManifestKeepGoingParses);
    ( "allow compiler-owned sources in batch mode",
      testBatchCompileAllowsCompilerOwnedSources );
  ]
