#!/usr/bin/env python3
"""A prepared stdlib is reused across processes, and a damaged cache only costs a rebuild."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    with tempfile.TemporaryDirectory(prefix="stdlib-cache-") as temporary:
        root = Path(temporary)
        env = dict(os.environ, XDG_CACHE_HOME=str(root / "cache"), XDG_DATA_HOME=str(root / "data"))
        program = root / "program"

        def compile_():
            start = time.monotonic()
            result = subprocess.run([compiler, "-q", "--emit-result", "-e", "(2L ** 10L) + 1L",
                "-o", str(program)], env=env, capture_output=True, text=True, timeout=300, cwd=root)
            assert result.returncode == 0, result.stdout + result.stderr
            assert json.loads(subprocess.check_output([str(program)], text=True, timeout=10)) == 1025
            return time.monotonic() - start

        cold = compile_()
        caches = list((root / "cache" / "dark-compiler").glob("stdlib-*.bin"))
        assert len(caches) == 1, caches
        warm = compile_()
        # Generous: the cold build is about a minute, a cache hit a second or two.
        assert warm < cold / 4, (cold, warm)
        caches[0].write_bytes(b"not a marshalled stdlib")
        compile_()
        assert caches[0].stat().st_size > 1000, "a damaged cache is rebuilt and replaced"
        assert not any(path.name.startswith("dark-compiler") for path in root.iterdir() if path.name != "cache")
    print(f"Prepared stdlib is cached across processes ({cold:.1f}s cold, {warm:.1f}s warm)")


if __name__ == "__main__":
    main()
