"""darkc's --cache-dir for a test: its own package answers, beside the prepared stdlib that
every test shares, so no test pays the minute-long stdlib preparation again."""
import hashlib
import os
from pathlib import Path
import subprocess
import tempfile

SHARED = Path.home() / ".cache" / "darkc-tests"


def flags(compiler, directory=None):
    """The shared cache, or `directory`'s own one linked to the shared stdlib."""
    SHARED.mkdir(parents=True, exist_ok=True)
    # darkc keys a prepared stdlib by the MD5 of its own executable.
    digest = hashlib.md5(Path(compiler).read_bytes()).hexdigest()
    if not list(SHARED.glob(f"stdlib-*-{digest}.bin")):
        with tempfile.TemporaryDirectory(prefix="darkc-warm-") as warm:
            subprocess.run([compiler, "--cache-dir", str(SHARED), "-q", "-e", "0L",
                            "-o", os.path.join(warm, "program")],
                           check=True, capture_output=True, timeout=900)
    if directory is None:
        return ["--cache-dir", str(SHARED)]
    own = Path(directory) / "darkc-cache"
    own.mkdir(exist_ok=True)
    for stdlib in SHARED.glob(f"stdlib-*-{digest}.bin"):
        if not (own / stdlib.name).exists():
            (own / stdlib.name).symlink_to(stdlib)
    return ["--cache-dir", str(own)]
