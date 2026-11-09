#!/usr/bin/env python3
"""`--syntax=current` reads a script as the current interpreter does, and prints results as `dark eval` does."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    with tempfile.TemporaryDirectory(prefix="current-syntax-") as temporary:
        program = Path(temporary) / "program"

        def compile_(expression, *flags):
            return subprocess.run([compiler, *flags, "-q", "--emit-result", "-e", expression,
                "-o", str(program)], capture_output=True, text=True, timeout=300,
                env=dict(os.environ, XDG_DATA_HOME=temporary))

        def run(expression, *flags):
            result = compile_(expression, *flags)
            assert result.returncode == 0, expression + result.stdout + result.stderr
            return subprocess.check_output([str(program)], text=True, timeout=10).strip()

        current = "--syntax=current"
        # Bare integers are Int64; the pinned suffixes and BigInt's `z` mean the same either way.
        assert run("Stdlib.Int64.add(20, 22)", current) == "42"
        assert run("Stdlib.List.map([1, 2], fun(x) -> x * 2)", current) == "[2, 4]"
        assert run("Stdlib.Int64.add(20L, 22L)", current) == "42"
        # Results print as `dark eval` prints them.
        assert run("12345678901234567890z * 2z", current) == "24691357802469135780z"
        assert run("[Stdlib.Option.Option.Some(1), Stdlib.Option.Option.None]", current) == "[Some(1), None]"
        assert run("Stdlib.Result.Result.Ok((5u8, 3s16))", current) == "Ok((5u8, 3s16))"
        assert run('"a\\"b"', current) == 'a"b'
        assert run('["a\\"b"]', current) == '["a\\"b"]'
        # Stdlib calls have the interpreter's types and meaning, and dicts and
        # Uuids print as `dark eval` prints them.
        assert run('Stdlib.String.slice("hello world", 2, 7)', current) == "llo w"
        assert run('"hello" |> Stdlib.String.slice(1, 3)', current) == "el"
        assert run("Stdlib.List.length([1, 2, 3])", current) == "3"
        assert run('Stdlib.String.indexOf("hello", "l")', current) == "Some(2)"
        assert run('Stdlib.Dict.fromList([("a", 1), ("a", 2)])', current) == '{ "a": 2 }'
        assert run('Stdlib.Dict.fromList([("a", Stdlib.Dict.fromList([(1, 2)]))])', current) == '{ "a": { 1: 2 } }'
        assert run('Stdlib.Uuid.parse("6fad2a6c-a7b6-4b9e-bf01-5cb6ac1ebe62")', current) == \
            "Ok(<Uuid: 6fad2a6c-a7b6-4b9e-bf01-5cb6ac1ebe62>)"
        # Without the flag a bare integer is still an Int, printed as before.
        pinned = compile_("Stdlib.Int64.add(20, 22)")
        assert pinned.returncode != 0, pinned.stdout + pinned.stderr
        assert run("12345678901234567890 * 2") == "24691357802469135780"
        assert run("Stdlib.Option.Option.Some 1L") == "Darklang.Stdlib.Option.Option<Int64>.Some(1)"
    print("--syntax=current reads bare integers as Int64 and prints as dark eval; the pinned behaviour is unchanged")


if __name__ == "__main__":
    main()
