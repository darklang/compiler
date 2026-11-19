#!/usr/bin/env python3
"""A script reads as the current interpreter reads it, and results print as `dark eval` prints them."""
from pathlib import Path
import subprocess
import sys
import tempfile
import darkc_cache


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    with tempfile.TemporaryDirectory(prefix="current-syntax-") as temporary:
        program = Path(temporary) / "program"
        cache = darkc_cache.flags(compiler, temporary)

        def compile_(expression, *flags):
            return subprocess.run([compiler, *cache, *flags, "-q", "--emit-result", "-e", expression,
                "-o", str(program)], capture_output=True, text=True, timeout=300)

        def run(expression, *flags):
            result = compile_(expression, *flags)
            assert result.returncode == 0, expression + result.stdout + result.stderr
            return subprocess.check_output([str(program)], text=True, timeout=10).strip()

        # Bare integers are Int64; the pinned suffixes and BigInt's `z` mean the same either way.
        assert run("Stdlib.Int64.add(20, 22)") == "42"
        assert run("Stdlib.List.map([1, 2], fun(x) -> x * 2)") == "[2, 4]"
        assert run("Stdlib.Int64.add(20L, 22L)") == "42"
        # Results print as `dark eval` prints them.
        assert run("12345678901234567890z * 2z") == "24691357802469135780z"
        assert run("[Stdlib.Option.Option.Some(1), Stdlib.Option.Option.None]") == "[Some(1), None]"
        assert run("Stdlib.Result.Result.Ok((5u8, 3s16))") == "Ok((5u8, 3s16))"
        assert run('"a\\"b"') == 'a"b'
        assert run('["a\\"b"]') == '["a\\"b"]'
        # Stdlib calls have the interpreter's types and meaning, and dicts and
        # Uuids print as `dark eval` prints them.
        assert run('Stdlib.String.slice("hello world", 2, 7)') == "llo w"
        assert run('"hello" |> Stdlib.String.slice(1, 3)') == "el"
        assert run("Stdlib.List.length([1, 2, 3])") == "3"
        assert run('Stdlib.String.indexOf("hello", "l")') == "Some(2)"
        assert run('Stdlib.Dict.fromList([("a", 1), ("a", 2)])') == '{ "a": 2 }'
        assert run('Stdlib.Dict.fromList([("a", Stdlib.Dict.fromList([(1, 2)]))])') == '{ "a": { 1: 2 } }'
        assert run('Stdlib.Uuid.parse("6fad2a6c-a7b6-4b9e-bf01-5cb6ac1ebe62")') == \
            "Ok(<Uuid: 6fad2a6c-a7b6-4b9e-bf01-5cb6ac1ebe62>)"
        # Fns whose meaning changed on main: append adds one value, repeat answers
        # the list, padStart and padEnd pad with a Char.
        assert run("Stdlib.List.append([1, 2], 3)") == "[1, 2, 3]"
        assert run("Stdlib.List.prepend([1, 2], 3)") == "[3, 1, 2]"
        assert run("Stdlib.List.indexedMap([5, 6], fun(i, x) -> i * x)") == "[0, 6]"
        assert run("2s128 ** 10s128") == "1024s128"
        assert run("2u128 ** 100u128") == "1267650600228229401496703205376u128"
        assert run('$"a{1 + 1}b{1.5}{\"c\"}"') == "a2b1.5c"
        assert run("Stdlib.List.concat([1], [2, 3])") == "[1, 2, 3]"
        assert run('Stdlib.List.zip([1, 2], ["a", "b"])') == 'Ok([(1, "a"), (2, "b")])'
        assert run("Stdlib.List.zip([1, 2], [3])") == "Error(Stdlib.List.LengthMismatch { left: 2, right: 1 })"
        assert run("Stdlib.List.map2Shortest([1, 2, 3], [10, 20], fun(a, b) -> a + b)") == "[11, 22]"
        assert run('Stdlib.List.repeat(3, "a")') == '["a", "a", "a"]'
        assert run("Stdlib.String.padStart(\"7\", '0', 3)") == "007"
        assert run("Stdlib.String.padEnd(\"7\", '.', 3)") == "7.."
        # indexOf, lastIndexOf and contains match whole Chars and count in Chars.
        assert run('Stdlib.String.indexOf("🇵🇷🇺🇸", "🇷🇺")') == "None"
        assert run('Stdlib.String.indexOf("a🧑🏼‍💻b", "b")') == "Some(2)"
        assert run('Stdlib.String.lastIndexOf("🧑🏼‍💻🧑🏼‍💻", "🧑")') == "None"
        assert run('Stdlib.String.lastIndexOf("🧑🏼‍💻🧑🏼‍💻", "🧑🏼‍💻")') == "Some(1)"
        assert run('Stdlib.String.contains("👍🏽", "👍")') == "false"
        assert run('Stdlib.String.contains("a\\r\\nb", "\\n")') == "false"
        assert run('Stdlib.String.contains("hello", "ell")') == "true"
        # A generic type shows the type arguments its value holds, `_` for a
        # parameter none of its fields mention, and a DateTime prints in angle brackets.
        generic = ("type MyOpt<'a> =\n  | Nothing\n  | Just(a)\n"
                   "type Pair<'a, 'b> = { first: a, second: b }\n"
                   '(MyOpt.Nothing, MyOpt.Just(1), Pair { first = 1, second = "a" })')
        assert run(generic) == \
            '(\n  MyOpt<_>.Nothing,\n  MyOpt<Int64>.Just(1),\n  Pair<Int64, String> { first: 1, second: "a" }\n)'
        # Types with the same parameters and shape are one type to the interpreter, so
        # their cases print without a type name.
        same = ("type A = | Same\ntype B = | Same\ntype C = | Other(Int64)\n"
                '(A.Same, B.Same, C.Other(1), Stdlib.Int64.parse("x"))')
        assert run(same) == "(Same, Same, C.Other(1), Error(BadFormat))"
        assert run('Stdlib.DateTime.parse("2019-07-28T22:42:36Z")') == \
            "Ok(<DateTime: 2019-07-28T22:42:36Z>)"
        assert run('Stdlib.DateTime.parse("2019-07-28T22:42:36Z") |> Builtin.unwrap') == \
            "<DateTime: 2019-07-28T22:42:36Z>"
        # A value that does not fit in 80 columns breaks as dark eval breaks it:
        # each group that does not fit puts one item per line, indented by two.
        long = '"' + "a" * 41 + '"', '"' + "b" * 65 + '"'
        assert run("Stdlib.Option.Option.Some((%s, %s))" % long) == \
            "Some(\n  (\n    %s,\n    %s\n  )\n)" % long
        assert run("[[1, 2, 3], [4, 5, 6], [7, 8, 9], [10, 11, 12], [13, 14, 15], [16, 17, 18], [19, 20, 21]]") == ("[\n  [1, 2, 3],\n  [4, 5, 6],\n  [7, 8, 9],\n  [10, 11, 12],\n"
                                "  [13, 14, 15],\n  [16, 17, 18],\n  [19, 20, 21]\n]")
        assert run('Stdlib.Dict.fromList([("k", Stdlib.Dict.fromList([])), ("j", Stdlib.Dict.fromList([("x", %s)]))])'
                   % long[1]) == '{\n  "j": {\n    "x": %s\n  },\n  "k": {}\n}' % long[1]
        # Rendering a record that holds a recursive type releases it through that
        # type's own helper, which needs the helper for the match-case list inside it.
        recursive = ("type RcPattern =\n  | RcPVariable(Int64, String)\n"
                     "type RcCase =\n  { pat: RcPattern\n    whenCondition: Stdlib.Option.Option<RcExpr>\n    rhs: RcExpr }\n"
                     "type RcExpr =\n  | RcIf(Int64, RcExpr, RcExpr, Stdlib.Option.Option<RcExpr>)\n"
                     "  | RcMatch(Int64, RcExpr, List<RcCase>)\n"
                     "type RcValue = { name: String, body: RcExpr }\n"
                     "type RcClosure = { items: Dict<String, RcValue> }\n"
                     "RcClosure { items = Stdlib.Dict.empty }")
        assert run(recursive) == "RcClosure { items: {} }"
        # Bytes print as dark eval prints unsaved Bytes: the size and the first 16 in hex.
        assert run("Stdlib.Blob.fromList([171u8])") == "<Bytes: 1 byte ab>"
        assert run("(Stdlib.Blob.fromList([]), 1)") == "(<Bytes: 0 bytes>, 1)"
        assert run("Stdlib.Blob.fromList([0u8, 1u8, 2u8, 3u8, 4u8, 5u8, 6u8, 7u8, 8u8, 9u8, 10u8, 11u8, 12u8, 13u8, 14u8, 15u8, 16u8])") == \
            "<Bytes: 17 bytes 00 01 02 03 04 05 06 07 08 09 0a 0b 0c 0d 0e 0f ...>"
    print("Scripts read bare integers as Int64 and results print as dark eval prints them")


if __name__ == "__main__":
    main()
