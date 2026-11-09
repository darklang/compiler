#!/usr/bin/env python3
"""--check probes a generic fn at concrete type arguments, so it gets an answer."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    with tempfile.TemporaryDirectory(prefix="check-generic-") as temporary:
        env = dict(os.environ, XDG_DATA_HOME=temporary)
        result = subprocess.run([compiler, "--check", "Stdlib.List.map", "Stdlib.List.sort",
                                 "Stdlib.String.length"],
                                env=env, capture_output=True, text=True, timeout=600)
        assert result.stdout.splitlines() == [
            "Stdlib.List.map: compiles at <Int64, Int64>",
            "Stdlib.List.sort: compiles at <Int64>",
            "Stdlib.String.length: compiles",
        ], result.stdout + result.stderr
    print("--check compiles a generic fn at concrete type arguments and names them")


if __name__ == "__main__":
    main()
