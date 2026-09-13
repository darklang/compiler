"""Compare closed-list workloads using built compilers and pinned QEMU counts."""

import argparse
import hashlib
import json
from pathlib import Path
import platform
import re
import subprocess
import tempfile
import time


CASES = {"main": "8205000\n", "shared": "360000\n", "captured-list": "9\n"}


def checked(command, cwd):
    result = subprocess.run(command, cwd=cwd, text=True, capture_output=True, timeout=180)
    if result.returncode:
        raise RuntimeError(f"{command} exited {result.returncode}: {result.stdout}{result.stderr}")
    return result


def measure(repository, source, expected, target, output):
    compiler = repository / "dark"
    # The CLI supports an explicit x86_64 target; ARM64 uses the host target.
    if target == "arm64" and (platform.system() != "Linux" or platform.machine() not in ("aarch64", "arm64")):
        raise RuntimeError("ARM64 measurements require a Linux ARM64 host")
    target_args = ["--target=linux-x86_64"] if target == "x86_64" else []
    command = [str(compiler), "--emit-result", *target_args, str(source), "-q", "-o", str(output)]
    start = time.monotonic()
    checked(command, repository)
    compile_ms = (time.monotonic() - start) * 1000
    counter = repository / "benchmarks/infrastructure/qemu_instruction_count.sh"
    execution = checked([str(counter), target, str(output)], repository)
    if execution.stdout != expected:
        raise RuntimeError(f"{source.name}: expected {expected!r}, got {execution.stdout!r}")
    counts = re.findall(r"total insns: (\d+)", execution.stderr)
    if len(counts) != 1 or int(counts[0]) <= 0:
        raise RuntimeError(f"Expected one positive instruction count: {execution.stderr}")
    binary_bytes = output.stat().st_size
    checked(command + ["--leak-check"], repository)
    leak = checked([str(counter), target, str(output)], repository)
    if leak.stdout != expected or "leaks:" in leak.stderr:
        raise RuntimeError(f"Leak-check failed for {source.name}: {leak.stdout}{leak.stderr}")
    return {"instructions": int(counts[0]), "compile_ms": compile_ms, "binary_bytes": binary_bytes, "leak_check_passed": True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--target", choices=["arm64", "x86_64"], required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    roots = {"baseline": args.baseline.resolve(), "candidate": args.candidate.resolve()}
    sources = Path(__file__).resolve().parent
    report = {"target": args.target, "compilers": {}, "workloads": {}}
    for label, repository in roots.items():
        report["compilers"][label] = {
            "commit": checked(["git", "rev-parse", "HEAD"], repository).stdout.strip(),
            "dirty": bool(checked(["git", "status", "--porcelain"], repository).stdout),
            "assembly_sha256": hashlib.sha256((repository / "bin/DarkCompiler/Debug/net10.0/DarkCompiler.dll").read_bytes()).hexdigest(),
        }
    with tempfile.TemporaryDirectory(prefix="list-array-bench-") as temporary:
        for name, expected in CASES.items():
            source = sources / f"{name}.dark"
            measurements = {
                label: measure(repository, source, expected, args.target, Path(temporary) / f"{label}-{name}")
                for label, repository in roots.items()
            }
            measurements["source_sha256"] = hashlib.sha256(source.read_bytes()).hexdigest()
            measurements["instruction_ratio"] = measurements["candidate"]["instructions"] / measurements["baseline"]["instructions"]
            report["workloads"][name] = measurements
            print(f"{name}: ratio={measurements['instruction_ratio']:.6f}", flush=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
