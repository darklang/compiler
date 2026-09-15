#!/usr/bin/env python3
"""Render merge-train state with repository integration and benchmark history."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path
from typing import Any


def git(repo: Path, *args: str) -> str:
    completed = subprocess.run(
        ["git", "-C", str(repo), *args],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    return completed.stdout.rstrip("\n")


def active_jobs(payload: dict[str, Any]) -> list[dict[str, Any]]:
    by_id = {
        int(job["id"]): job
        for job in [*payload.get("recent_jobs", []), *payload.get("attention_jobs", [])]
        if job.get("state") in {"waiting", "running", "ready", "attention"}
    }
    return [by_id[job_id] for job_id in sorted(by_id)]


def benchmark_changes(repo: Path, limit: int = 3) -> list[str]:
    history = git(
        repo,
        "log",
        f"-{limit}",
        "--format=%H%x09%h%x09%cI%x09%s",
        "--",
        "benchmarks/RESULTS.md",
    )
    if not history:
        return []

    def render(line: str) -> str:
        commit, short_commit, date, subject = line.split("\t", 3)
        numstat = git(
            repo,
            "show",
            "--format=",
            "--numstat",
            commit,
            "--",
            "benchmarks/RESULTS.md",
        ).splitlines()
        additions, deletions, _path = numstat[0].split("\t", 2)
        return f"{short_commit} {date} {subject} (+{additions}/-{deletions})"

    return [render(line) for line in history.splitlines()]


def render(payload: dict[str, Any], repo: Path) -> str:
    if payload.get("contract_version") != 4:
        raise ValueError(
            f"unsupported mergetrain contract version: {payload.get('contract_version')}"
        )

    action = payload["next_action"]
    next_action = action.get("command") or str(action["code"]).replace("_", " ")
    lines = [
        f"health: {payload['health']}",
        f"{str(payload['state']).upper()}: {payload['summary']}",
        f"next: {next_action}",
    ]
    if action.get("requires_approval") != "none":
        lines.append(f"approval: {action['requires_approval']}")
    lines.extend(
        f"warning {warning['code']}: {warning['summary']}"
        for warning in payload.get("warnings", [])
    )

    lines.append("in train:")
    jobs = active_jobs(payload)
    if jobs:
        for job in jobs:
            line = f"  #{job['id']} {job['state']} {job['task']} [{job['branch']}]"
            if job.get("reason"):
                line += f" — {job['reason']}"
            lines.append(line)
    else:
        lines.append("  (empty)")

    recent_merge = git(
        repo,
        "log",
        "-1",
        "--first-parent",
        "--merges",
        "--format=%h %cI %s",
        "--",
    )
    lines.extend(["recent merge:", f"  {recent_merge or '(none)'}"])

    changes = benchmark_changes(repo)
    lines.append("recent benchmarks/RESULTS.md changes:")
    lines.extend([f"  {change}" for change in changes] or ["  (none)"])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True, type=Path)
    args = parser.parse_args()
    try:
        payload = json.load(sys.stdin)
        print(render(payload, args.repo.resolve()))
    except (
        json.JSONDecodeError,
        KeyError,
        OSError,
        subprocess.CalledProcessError,
        ValueError,
    ) as exc:
        print(f"Unable to render merge-train status: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
