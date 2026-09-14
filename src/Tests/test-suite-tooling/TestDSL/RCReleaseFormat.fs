// RCReleaseFormat.fs - Parser for semantic reference-release fixtures.
//
// Describes canonical managed object graphs without exposing their LIR layout.

module TestDSL.RCReleaseFormat

open System
open TestDSL.Common

type ManagedShape =
    | Int64Value
    | EnumValue
    | DynamicString
    | LiteralString
    | DynamicBlob
    | ListValue of ManagedShape
    | DictValue of ManagedShape * ManagedShape
    | TupleValue of ManagedShape list
    | RecordValue of ManagedShape list
    | SumValue of ManagedShape
    | ClosureValue of ManagedShape list

type PreservedRegister = {
    Register: LIR.PhysReg
    Value: int64
}

type RootPlacement =
    | CanonicalRoot
    | ExplicitRoot of LIR.PhysReg * PreservedRegister list

type RCReleaseTest = {
    Name: string
    Root: ManagedShape
    Placement: RootPlacement
    SourceFile: string
}

type private ShapeToken =
    | Identifier of string
    | LeftParen
    | RightParen
    | Comma

let private knownSections = Set.ofList [ "NAME"; "ROOT"; "ROOT-REGISTER"; "PRESERVE" ]

let private tokenizeShape (source: string) : Result<ShapeToken list, string> =
    let isIdentifierChar c = Char.IsLetterOrDigit c || c = '-' || c = '_'

    let rec readIdentifier index chars =
        match chars with
        | c :: rest when isIdentifierChar c -> readIdentifier (index + 1) rest
        | _ -> index, chars

    let rec loop offset chars tokens =
        match chars with
        | [] -> Ok (List.rev tokens)
        | c :: rest when Char.IsWhiteSpace c -> loop (offset + 1) rest tokens
        | '(' :: rest -> loop (offset + 1) rest (LeftParen :: tokens)
        | ')' :: rest -> loop (offset + 1) rest (RightParen :: tokens)
        | ',' :: rest -> loop (offset + 1) rest (Comma :: tokens)
        | c :: _ when isIdentifierChar c ->
            let length, remaining = readIdentifier 0 chars
            let identifier = source.Substring(offset, length)
            loop (offset + length) remaining (Identifier identifier :: tokens)
        | c :: _ -> Error $"Invalid ROOT character '{c}' at offset {offset}"

    source |> Seq.toList |> fun chars -> loop 0 chars []

let private parseShape source =
    let rec parseOne tokens =
        let parseArguments allowEmpty remaining =
            let rec loop parsed rest =
                match rest with
                | RightParen :: tail when allowEmpty || not (List.isEmpty parsed) ->
                    Ok (List.rev parsed, tail)
                | RightParen :: _ -> Error "Managed shape requires at least one argument"
                | _ ->
                    parseOne rest
                    |> Result.bind (fun (value, afterValue) ->
                        match afterValue with
                        | Comma :: tail -> loop (value :: parsed) tail
                        | RightParen :: tail -> Ok (List.rev (value :: parsed), tail)
                        | _ -> Error "Expected ',' or ')' in managed shape")
            loop [] remaining

        match tokens with
        | Identifier name :: rest ->
            match name.ToLowerInvariant(), rest with
            | "i64", tail -> Ok (Int64Value, tail)
            | "enum", tail -> Ok (EnumValue, tail)
            | "string", tail -> Ok (DynamicString, tail)
            | "literal-string", tail -> Ok (LiteralString, tail)
            | "blob", tail -> Ok (DynamicBlob, tail)
            | "list", LeftParen :: tail ->
                parseArguments false tail
                |> Result.bind (fun (shapes, remaining) ->
                    match shapes with
                    | [ shape ] -> Ok (ListValue shape, remaining)
                    | _ -> Error "list requires exactly one argument")
            | "dict", LeftParen :: tail ->
                parseArguments false tail
                |> Result.bind (fun (shapes, remaining) ->
                    match shapes with
                    | [ key; value ] -> Ok (DictValue (key, value), remaining)
                    | _ -> Error "dict requires exactly two arguments")
            | "tuple", LeftParen :: tail ->
                parseArguments false tail |> Result.map (fun (shapes, remaining) -> TupleValue shapes, remaining)
            | "record", LeftParen :: tail ->
                parseArguments false tail |> Result.map (fun (shapes, remaining) -> RecordValue shapes, remaining)
            | "sum", LeftParen :: tail ->
                parseArguments false tail
                |> Result.bind (fun (shapes, remaining) ->
                    match shapes with
                    | [ payload ] -> Ok (SumValue payload, remaining)
                    | _ -> Error "sum requires exactly one argument")
            | "closure", LeftParen :: tail ->
                parseArguments true tail |> Result.map (fun (shapes, remaining) -> ClosureValue shapes, remaining)
            | name, LeftParen :: _ -> Error $"Unknown managed shape '{name}'"
            | name, _ -> Error $"Unknown managed leaf shape '{name}'"
        | _ -> Error "Expected a managed shape name"

    tokenizeShape source
    |> Result.bind (fun tokens ->
        parseOne tokens
        |> Result.bind (fun (shape, remaining) ->
            match remaining with
            | [] -> Ok shape
            | _ -> Error "Unexpected tokens after ROOT managed shape"))

let private toSectionMap sections =
    match sections |> List.tryFind (fun (name, _) -> not (Set.contains name knownSections)) with
    | Some (name, _) -> Error $"Unknown reference-release section: {name}"
    | None ->
        match sections |> List.countBy fst |> List.tryFind (fun (_, count) -> count > 1) with
        | Some (name, _) -> Error $"Duplicate reference-release section: {name}"
        | None -> Ok (Map.ofList sections)

let private required name (sections: Map<string, string>) =
    match Map.tryFind name sections with
    | Some value when not (String.IsNullOrWhiteSpace value) -> Ok (value.Trim())
    | Some _ -> Error $"Reference-release section {name} cannot be empty"
    | None -> Error $"Missing required reference-release section: {name}"

let private parsePreservedRegisters (source: string) =
    let parseLine (lineNumber, line: string) =
        match line.Split('=', StringSplitOptions.TrimEntries) |> Array.toList with
        | [ register; value ] ->
            match TestDSL.LIRParser.parsePhysReg register, Int64.TryParse value with
            | Ok parsedRegister, (true, parsedValue) ->
                Ok { Register = parsedRegister; Value = parsedValue }
            | Error msg, _ -> Error $"PRESERVE line {lineNumber}: {msg}"
            | _, _ -> Error $"PRESERVE line {lineNumber}: invalid Int64 value '{value}'"
        | _ -> Error $"PRESERVE line {lineNumber}: expected REGISTER = VALUE"

    source.Split('\n')
    |> Array.mapi (fun index line -> index + 1, line.Trim())
    |> Array.filter (fun (_, line) -> line <> "")
    |> Array.toList
    |> List.fold
        (fun result line ->
            result
            |> Result.bind (fun parsed ->
                parseLine line |> Result.map (fun value -> value :: parsed)))
        (Ok [])
    |> Result.map List.rev

let private parsePlacement sections =
    match Map.tryFind "ROOT-REGISTER" sections, Map.tryFind "PRESERVE" sections with
    | None, None -> Ok CanonicalRoot
    | None, Some _ -> Error "PRESERVE requires ROOT-REGISTER"
    | Some register, preserved ->
        TestDSL.LIRParser.parsePhysReg register
        |> Result.bind (fun rootRegister ->
            match preserved with
            | None -> Ok (ExplicitRoot (rootRegister, []))
            | Some source ->
                parsePreservedRegisters source
                |> Result.bind (fun values ->
                    if values |> List.exists (fun value -> value.Register = rootRegister) then
                        Error "PRESERVE register cannot also be ROOT-REGISTER"
                    elif values |> List.map _.Register |> List.distinct |> List.length <> List.length values then
                        Error "PRESERVE registers must be unique"
                    else
                        Ok (ExplicitRoot (rootRegister, values))))

let private parseCase path sections =
    toSectionMap sections
    |> Result.bind (fun values ->
        match required "NAME" values, required "ROOT" values, parsePlacement values with
        | Error msg, _, _
        | _, Error msg, _
        | _, _, Error msg -> Error msg
        | Ok name, Ok root, Ok placement ->
            parseShape root
            |> Result.map (fun shape ->
                { Name = name
                  Root = shape
                  Placement = placement
                  SourceFile = path }))

let private groupCases sections =
    let rec loop completed current remaining =
        match remaining with
        | [] ->
            match current with
            | [] -> Ok (List.rev completed)
            | _ -> Ok (List.rev (List.rev current :: completed))
        | (("NAME", _) as section) :: rest ->
            match current with
            | [] -> loop completed [ section ] rest
            | _ -> loop (List.rev current :: completed) [ section ] rest
        | section :: rest ->
            match current with
            | [] -> Error $"Reference-release case must start with NAME, found {fst section}"
            | _ -> loop completed (section :: current) rest
    loop [] [] sections

let parseRCReleaseFileContent path content =
    let sections = parseSections (normalizeLineEndings content)
    if List.isEmpty sections then Error "Reference-release fixture contains no sections"
    else
        groupCases sections
        |> Result.bind (fun cases ->
            cases
            |> List.fold
                (fun result sections ->
                    result
                    |> Result.bind (fun parsed ->
                        parseCase path sections |> Result.map (fun test -> test :: parsed)))
                (Ok [])
            |> Result.map List.rev)
