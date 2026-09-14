// Immediates.fs - Materialize integer constants for ARM64 runtime instruction generators.

module ARM64RuntimeImmediates

let internal generateLoadUInt64Immediate (dest: ARM64.Reg) (value: uint64) : ARM64.Instr list =
    let chunk shift =
        uint16 ((value >>> shift) &&& 0xFFFFUL)

    let chunks = [
        (chunk 0, 0)
        (chunk 16, 16)
        (chunk 32, 32)
        (chunk 48, 48)
    ]

    match chunks |> List.tryFind (fun (value, _) -> value <> 0us) with
    | None ->
        [ARM64.MOVZ (dest, 0us, 0)]
    | Some (firstValue, firstShift) ->
        let movkInstrs =
            chunks
            |> List.choose (fun (value, shift) ->
                if shift = firstShift || value = 0us then
                    None
                else
                    Some (ARM64.MOVK (dest, value, shift)))

        ARM64.MOVZ (dest, firstValue, firstShift) :: movkInstrs

let internal generateLoadNonNegativeIntImmediate (dest: ARM64.Reg) (value: int) : ARM64.Instr list =
    if value < 0 then
        Crash.crash $"Runtime: cannot load negative unsigned immediate {value}"
    else
        generateLoadUInt64Immediate dest (uint64 value)
