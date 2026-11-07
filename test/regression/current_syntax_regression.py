#!/usr/bin/env python3
"""`--syntax=current` reads a script's bare integers as Int64, as the current interpreter does."""
import json
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
            return json.loads(subprocess.check_output([str(program)], text=True, timeout=10))

        current = "--syntax=current"
        assert run("Stdlib.Int64.add(20, 22)", current) == 42
        assert run("Stdlib.List.map([1, 2], fun(x) -> x * 2)", current) == [2, 4]
        # The pinned suffixes and BigInt's `z` mean the same under either reading.
        assert run("Stdlib.Int64.add(20L, 22L)", current) == 42
        assert str(run("12345678901234567890z * 2z", current)) == "24691357802469135780"
        # Without the flag a bare integer is still an Int.
        pinned = compile_("Stdlib.Int64.add(20, 22)")
        assert pinned.returncode != 0, pinned.stdout + pinned.stderr
        assert str(run("12345678901234567890 * 2")) == "24691357802469135780"
    print("--syntax=current reads bare integers as Int64; the pinned reading is unchanged")


if __name__ == "__main__":
    main()
