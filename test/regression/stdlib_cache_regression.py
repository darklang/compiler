#!/usr/bin/env python3
"""A prepared stdlib is reused across processes through --cache-dir, two cold runs prepare it
once, a damaged cache or a killed write only costs a rebuild, and without --cache-dir darkc keeps
nothing: it reads no HOME or XDG directory."""
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
        home, xdg = root / "home", root / "xdg"
        home.mkdir()
        xdg.mkdir()
        env = dict(os.environ, HOME=str(home), XDG_CACHE_HOME=str(xdg), XDG_DATA_HOME=str(xdg))
        program = root / "program"
        cache = root / "cache"
        cached = ("--cache-dir", str(cache))

        def start(output, *flags):
            return subprocess.Popen([compiler, *flags, "--verbose=json", "-q", "--emit-result", "-e",
                "(2L ** 10L) + 1L", "-o", str(output)], env=env, stdout=subprocess.PIPE,
                stderr=subprocess.PIPE, text=True, cwd=root)

        def finish(process, output):
            stdout, stderr = process.communicate(timeout=300)
            assert process.returncode == 0, stdout + stderr
            assert json.loads(subprocess.check_output([str(output)], text=True, timeout=10)) == 1025
            return [json.loads(line) for line in stderr.splitlines()]

        def compile_(*flags):
            begin = time.monotonic()
            finish(start(program, *flags), program)
            return time.monotonic() - begin

        # Two cold runs at once: one prepares, the other waits and reads its result.
        begin = time.monotonic()
        first, second = start(program, *cached), start(root / "second", *cached)
        reports = [finish(first, program), finish(second, root / "second")]
        cold = time.monotonic() - begin
        assert sorted(lines[-1]["cached"] for lines in reports) == [False, True], reports
        assert any(line["stage"] == "waiting" for lines in reports for line in lines), reports
        caches = list(cache.glob("stdlib-*.bin"))
        assert len(caches) == 1, caches
        warm = compile_(*cached)
        # Generous: the cold build is about a minute, a cache hit a second or two.
        assert warm < cold / 4, (cold, warm)
        caches[0].write_bytes(b"not a marshalled stdlib")
        partial = cache / "stdlib-killed.partial"
        partial.write_bytes(b"left by a killed write")
        compile_(*cached)
        assert not partial.exists(), "a killed write's partial file is removed"
        assert caches[0].stat().st_size > 1000, "a damaged cache is rebuilt and replaced"
        compile_()
        assert list(home.iterdir()) == [] and list(xdg.iterdir()) == [], (
            list(home.rglob("*")), list(xdg.rglob("*")))
        assert sorted(path.name for path in root.iterdir()) == ["cache", "home", "program", "second", "xdg"]
    print(f"Prepared stdlib is cached in --cache-dir ({cold:.1f}s cold, {warm:.1f}s warm), and nowhere without it")


if __name__ == "__main__":
    main()
