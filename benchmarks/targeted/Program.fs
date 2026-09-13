// Program.fs - command-line entrypoint for compiler-focused targeted benchmarks.

module TargetedBenchmarks.Program

open System
open System.IO

let private usage () : unit =
    Console.Error.WriteLine("Usage: TargetedBenchmarks <json|integer128|all> <output-path>")
    Console.Error.WriteLine("For 'all', output-path is a directory; individual suites take a JSON file path.")

[<EntryPoint>]
let main args =
    match Array.toList args with
    | ["json"; outputPath] -> JsonPerformanceBenchmarks.run outputPath
    | ["integer128"; outputPath] -> Integer128PerformanceBenchmarks.run outputPath
    | ["all"; outputDirectory] ->
        Directory.CreateDirectory(outputDirectory) |> ignore
        let jsonExitCode = JsonPerformanceBenchmarks.run (Path.Combine(outputDirectory, "json.json"))
        if jsonExitCode <> 0 then jsonExitCode
        else Integer128PerformanceBenchmarks.run (Path.Combine(outputDirectory, "integer128.json"))
    | _ ->
        usage ()
        1
