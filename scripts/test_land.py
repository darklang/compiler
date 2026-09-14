"""test_land.py - Focused tests for the branch-facing ./land command."""

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


class LandScriptTests(unittest.TestCase):
    def test_prints_landing_then_landed_for_exact_deployed_job(self) -> None:
        source_root = Path(__file__).resolve().parent.parent

        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            repo = root / "repo"
            fake_bin = root / "bin"
            repo.mkdir()
            fake_bin.mkdir()

            subprocess.run(["git", "init", "-q", "-b", "main"], cwd=repo, check=True)
            subprocess.run(
                ["git", "config", "user.email", "land-test@example.invalid"],
                cwd=repo,
                check=True,
            )
            subprocess.run(
                ["git", "config", "user.name", "Land Test"], cwd=repo, check=True
            )
            shutil.copy2(source_root / "land", repo / "land")
            subprocess.run(["git", "add", "land"], cwd=repo, check=True)
            subprocess.run(["git", "commit", "-q", "-m", "base"], cwd=repo, check=True)
            subprocess.run(["git", "switch", "-q", "-c", "task/test"], cwd=repo, check=True)

            marker = repo / "change.txt"
            marker.write_text("ready\n", encoding="utf-8")
            subprocess.run(["git", "add", "change.txt"], cwd=repo, check=True)
            subprocess.run(["git", "commit", "-q", "-m", "ready change"], cwd=repo, check=True)

            fake_mergetrain = fake_bin / "mergetrain"
            fake_mergetrain.write_text(
                """#!/usr/bin/env python3
import json
import subprocess
import sys

command = next(arg for arg in sys.argv if arg in {"status", "enqueue", "inspect"})
if command == "status":
    print(json.dumps({
        "contract_version": 4,
        "counts": {"attention": 0},
        "health": "healthy",
    }))
elif command == "enqueue":
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip()
    print(json.dumps({"job": {"id": 17, "head_sha": head}}))
else:
    print(json.dumps({"job": {"status": "deployed"}}))
""",
                encoding="utf-8",
            )
            fake_mergetrain.chmod(0o755)

            process_environment = dict(os.environ)
            process_environment["PATH"] = f"{fake_bin}:{process_environment['PATH']}"
            completed = subprocess.run(
                [str(repo / "land"), "--task", "test landing output"],
                cwd=repo,
                env=process_environment,
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertEqual(completed.stdout, "landing\nlanded\n")
            self.assertEqual(completed.stderr, "")


if __name__ == "__main__":
    unittest.main()
