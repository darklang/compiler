"""test_mergetrain_status.py - E2E tests for the repository status summary."""

import os
import subprocess
import tempfile
import unittest
from pathlib import Path


class MergetrainStatusTests(unittest.TestCase):
    def test_shows_active_train_recent_merge_and_benchmark_changes(self) -> None:
        source_root = Path(__file__).resolve().parent.parent

        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            repo = root / "repo"
            fake_bin = root / "bin"
            (repo / "benchmarks").mkdir(parents=True)
            fake_bin.mkdir()

            subprocess.run(["git", "init", "-q", "-b", "main"], cwd=repo, check=True)
            subprocess.run(
                ["git", "config", "user.email", "status-test@example.invalid"],
                cwd=repo,
                check=True,
            )
            subprocess.run(
                ["git", "config", "user.name", "Status Test"], cwd=repo, check=True
            )
            (repo / "benchmarks" / "RESULTS.md").write_text(
                "ratio: 3.0x\n", encoding="utf-8"
            )
            subprocess.run(["git", "add", "."], cwd=repo, check=True)
            subprocess.run(
                ["git", "commit", "-q", "-m", "Initial benchmark results"],
                cwd=repo,
                check=True,
            )

            subprocess.run(["git", "switch", "-q", "-c", "feature"], cwd=repo, check=True)
            (repo / "feature.txt").write_text("ready\n", encoding="utf-8")
            subprocess.run(["git", "add", "feature.txt"], cwd=repo, check=True)
            subprocess.run(
                ["git", "commit", "-q", "-m", "Add feature"], cwd=repo, check=True
            )
            subprocess.run(["git", "switch", "-q", "main"], cwd=repo, check=True)
            subprocess.run(
                ["git", "merge", "-q", "--no-ff", "feature", "-m", "Merge feature train"],
                cwd=repo,
                check=True,
            )
            (repo / "benchmarks" / "RESULTS.md").write_text(
                "ratio: 2.8x\ndetails: improved\n", encoding="utf-8"
            )
            subprocess.run(["git", "add", "benchmarks/RESULTS.md"], cwd=repo, check=True)
            subprocess.run(
                ["git", "commit", "-q", "-m", "Record benchmark improvement"],
                cwd=repo,
                check=True,
            )

            fake_mergetrain = fake_bin / "mergetrain"
            fake_mergetrain.write_text(
                """#!/usr/bin/env python3
import json
import sys

assert "status" in sys.argv
assert "--json" in sys.argv
assert sys.argv[sys.argv.index("--limit") + 1] == "1000"
print(json.dumps({
    "contract_version": 4,
    "health": "healthy",
    "state": "running",
    "summary": "1 job(s) are running",
    "next_action": {
        "code": "runner_active",
        "command": None,
        "requires_approval": "none"
    },
    "warnings": [],
    "attention_jobs": [{
        "id": 9,
        "task": "Repair benchmark conflict",
        "branch": "agent/repair",
        "state": "attention",
        "reason": "merge conflict"
    }],
    "recent_jobs": [
        {
            "id": 12,
            "task": "Already deployed",
            "branch": "agent/done",
            "state": "done"
        },
        {
            "id": 11,
            "task": "Optimize calls",
            "branch": "agent/calls",
            "state": "waiting"
        },
        {
            "id": 10,
            "task": "Compile tuples",
            "branch": "agent/tuples",
            "state": "running"
        }
    ]
}))
""",
                encoding="utf-8",
            )
            fake_mergetrain.chmod(0o755)

            process_environment = dict(os.environ)
            process_environment["PATH"] = f"{fake_bin}:{process_environment['PATH']}"
            completed = subprocess.run(
                [
                    str(source_root / "mergetrain-status"),
                    "--repo",
                    str(repo),
                    "--once",
                ],
                cwd=repo,
                env=process_environment,
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertEqual(completed.stderr, "")
            self.assertIn("health: healthy", completed.stdout)
            self.assertIn("RUNNING: 1 job(s) are running", completed.stdout)
            self.assertIn("in train:\n", completed.stdout)
            attention = completed.stdout.index(
                "  #9 attention Repair benchmark conflict [agent/repair] — merge conflict"
            )
            running = completed.stdout.index(
                "  #10 running Compile tuples [agent/tuples]"
            )
            waiting = completed.stdout.index(
                "  #11 waiting Optimize calls [agent/calls]"
            )
            self.assertLess(attention, running)
            self.assertLess(running, waiting)
            self.assertNotIn("Already deployed", completed.stdout)
            self.assertRegex(
                completed.stdout,
                r"recent merge:\n  [0-9a-f]{7,12} \d{4}-\d{2}-\d{2}T\S+ Merge feature train",
            )
            self.assertRegex(
                completed.stdout,
                r"recent benchmarks/RESULTS.md changes:\n"
                r"  [0-9a-f]{7,12} \d{4}-\d{2}-\d{2}T\S+ Record benchmark improvement \(\+2/-1\)",
            )


if __name__ == "__main__":
    unittest.main()
