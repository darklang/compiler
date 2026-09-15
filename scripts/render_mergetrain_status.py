#!/usr/bin/env python3
"""Render merge-train state with repository integration and benchmark history."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


RESET = "\033[0m"
BOLD = "\033[1m"
DIM = "\033[2m"
RED = "\033[31m"
GREEN = "\033[32m"
YELLOW = "\033[33m"
CYAN = "\033[36m"


def styled(value: object, style: str, color: bool) -> str:
    text = str(value)
    return f"{style}{text}{RESET}" if color else text


def state_style(state: str) -> str:
    return {
        "attention": RED,
        "running": CYAN,
        "ready": GREEN,
        "waiting": YELLOW,
        "idle": DIM,
    }.get(state, RED)


def human_age(timestamp: str | datetime, *, now: datetime | None = None) -> str:
    instant = datetime.fromisoformat(timestamp) if isinstance(timestamp, str) else timestamp
    reference = now or datetime.now(timezone.utc)
    seconds = max(0, int((reference - instant).total_seconds()))
    units = (
        (365 * 24 * 60 * 60, "y"),
        (30 * 24 * 60 * 60, "mo"),
        (7 * 24 * 60 * 60, "w"),
        (24 * 60 * 60, "d"),
        (60 * 60, "h"),
        (60, "m"),
    )
    for duration, suffix in units:
        if seconds >= duration:
            return f"{seconds // duration}{suffix} ago"
    return f"{seconds}s ago"


def history_line(
    line: str,
    color: bool,
    *,
    now: datetime,
) -> str:
    parts = line.split(" ", 2)
    if len(parts) < 3:
        return line
    commit, timestamp, description = parts
    return (
        f"{styled(commit, CYAN, color)} "
        f"{styled(human_age(timestamp, now=now), DIM, color)} {description}"
    )


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


def benchmark_ratio(repo: Path) -> str | None:
    header_prefix = "| Benchmark | Dark ("
    results_path = repo / "benchmarks" / "RESULTS.md"
    if not results_path.is_file():
        return None
    for line in results_path.read_text(encoding="utf-8").splitlines():
        if line.startswith(header_prefix) and ") |" in line:
            return line[len(header_prefix) :].split(") |", 1)[0]
    return None


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


def render(payload: dict[str, Any], repo: Path, *, color: bool) -> str:
    if payload.get("contract_version") != 4:
        raise ValueError(
            f"unsupported mergetrain contract version: {payload.get('contract_version')}"
        )

    action = payload["next_action"]
    next_action = action.get("command") or str(action["code"]).replace("_", " ")
    health = str(payload["health"])
    health_style = GREEN if health == "healthy" else YELLOW
    state = str(payload["state"])
    lines = [
        f"health: {styled(health, health_style, color)}",
        f"{styled(state.upper(), state_style(state), color)}: {payload['summary']}",
        f"next: {styled(next_action, CYAN, color)}",
    ]
    if action.get("requires_approval") != "none":
        lines.append(f"approval: {action['requires_approval']}")
    lines.extend(
        styled(f"warning {warning['code']}: {warning['summary']}", YELLOW, color)
        for warning in payload.get("warnings", [])
    )

    lines.append(styled("in train:", BOLD, color))
    jobs = active_jobs(payload)
    if jobs:
        for job in jobs:
            job_state = str(job["state"])
            line = (
                f"  #{job['id']} {styled(job_state, state_style(job_state), color)} "
                f"{job['task']} [{job['branch']}]"
            )
            if job.get("reason"):
                line += f" — {job['reason']}"
            lines.append(line)
    else:
        lines.append("  (empty)")

    now = datetime.now(timezone.utc)
    recent_merges = git(
        repo,
        "log",
        "-5",
        "--first-parent",
        "--merges",
        "--format=%h %cI %s",
        "--",
    ).splitlines()
    lines.append(styled("recent merges:", BOLD, color))
    lines.extend(
        [f"  {history_line(merge, color, now=now)}" for merge in recent_merges]
        or ["  (none)"]
    )

    changes = benchmark_changes(repo)
    ratio = benchmark_ratio(repo)
    lines.append(f"benchmark ratio: {styled(ratio or 'unavailable', CYAN, color)}")
    lines.append(styled("recent benchmarks/RESULTS.md changes:", BOLD, color))
    lines.extend(
        [f"  {history_line(change, color, now=now)}" for change in changes]
        or ["  (none)"]
    )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True, type=Path)
    parser.add_argument("--color", action="store_true")
    args = parser.parse_args()
    try:
        payload = json.load(sys.stdin)
        print(render(payload, args.repo.resolve(), color=args.color))
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
