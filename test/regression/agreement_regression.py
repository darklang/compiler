#!/usr/bin/env python3
"""Calls that compiled and printed exactly what `dark eval` printed in the last
agreement run must keep doing so.

agreement_regression.py COMPILER [BASELINE]          check every call in the baseline
agreement_regression.py COMPILER --select IN OUT     keep the calls of IN that agree now

Each line of the baseline is {"id", "expr", "eval"}: an expression that needs only
the embedded stdlib, and what `dark eval` printed for it under --syntax=current."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

HERE = Path(__file__).resolve().parent
# Nothing listens here, so a call that needs the package store fails rather than reaching one.
OFFLINE = "http://127.0.0.1:9"


def outcomes(compiler, calls):
    with tempfile.TemporaryDirectory(prefix="agreement-") as temporary:
        env = dict(os.environ, XDG_DATA_HOME=temporary)

        def outcome(indexed):
            index, call = indexed
            program = Path(temporary) / f"{index}.out"
            built = subprocess.run([compiler, "--syntax=current", "--package-server", OFFLINE, "-q",
                "--emit-result", "-e", call["expr"], "-o", str(program)],
                capture_output=True, text=True, timeout=600, env=env, cwd=temporary)
            if built.returncode != 0:
                return (call, None, (built.stdout + built.stderr).strip()[:300])
            try:
                ran = subprocess.run([str(program)], capture_output=True, text=True, timeout=20,
                    stdin=subprocess.DEVNULL, env=env, cwd=temporary)
                return (call, (ran.stdout + ran.stderr).strip(), None)
            except subprocess.TimeoutExpired:
                return (call, "TIMEOUT", None)

        # The first compile builds the stdlib cache the others read.
        first = [outcome((0, calls[0]))] if calls else []
        with ThreadPoolExecutor(4) as pool:
            return first + list(pool.map(outcome, list(enumerate(calls))[1:]))


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    if len(sys.argv) > 2 and sys.argv[2] == "--select":
        calls = [json.loads(line) for line in open(sys.argv[3])]
        kept = [call for call, printed, _ in outcomes(compiler, calls) if printed == call["eval"]]
        with open(sys.argv[4], "w") as out:
            for call in kept:
                out.write(json.dumps(call, ensure_ascii=False) + "\n")
        print(f"{len(kept)} of {len(calls)} calls agree")
        return
    baseline = Path(sys.argv[2]) if len(sys.argv) > 2 else HERE / "agreement-baseline.jsonl"
    calls = [json.loads(line) for line in open(baseline)]
    failures = [(call, printed, error) for call, printed, error in outcomes(compiler, calls)
                if printed != call["eval"]]
    for call, printed, error in failures:
        print(f"{call['id']}\n  {call['expr']}\n  eval:     {call['eval']}\n  compiled: {error or printed}")
    if failures:
        sys.exit(f"{len(failures)} of {len(calls)} calls no longer print what dark eval prints")
    print(f"All {len(calls)} calls still print what dark eval prints")


main()
