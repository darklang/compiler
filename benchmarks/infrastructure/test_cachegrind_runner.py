#!/usr/bin/env python3
"""Focused tests for instruction-only Cachegrind measurement."""

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path


INFRASTRUCTURE_DIR = Path(__file__).resolve().parent
RUNNER = INFRASTRUCTURE_DIR / "cachegrind_runner.sh"


class CachegrindRunnerTests(unittest.TestCase):
    def test_runner_disables_cache_and_branch_simulation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            binary = root / "benchmark"
            binary.write_text("#!/bin/sh\nprintf '32765\\n'\n")
            binary.chmod(0o755)

            tool_dir = root / "bin"
            tool_dir.mkdir()
            arguments_log = root / "valgrind-arguments"
            valgrind = tool_dir / "valgrind"
            valgrind.write_text(
                "#!/bin/sh\n"
                "printf '%s\\n' \"$@\" > \"$FAKE_VALGRIND_ARGUMENTS\"\n"
                "printf '==1== I refs: 123,456\\n' >&2\n"
            )
            valgrind.chmod(0o755)

            environment = os.environ.copy()
            environment["PATH"] = f"{tool_dir}:{environment['PATH']}"
            environment["FAKE_VALGRIND_ARGUMENTS"] = str(arguments_log)
            completed = subprocess.run(
                [
                    str(RUNNER),
                    "ackermann",
                    str(root),
                    "comparable",
                    "false",
                    str(binary),
                    "full",
                ],
                check=False,
                capture_output=True,
                text=True,
                env=environment,
            )

            self.assertEqual(
                completed.returncode, 0, completed.stdout + completed.stderr
            )
            arguments = arguments_log.read_text().splitlines()
            self.assertIn("--cache-sim=no", arguments)
            self.assertIn("--branch-sim=no", arguments)
            self.assertNotIn("--cache-sim=yes", arguments)
            self.assertNotIn("--branch-sim=yes", arguments)
            result = json.loads((root / "ackermann_cachegrind.json").read_text())
            self.assertEqual(
                result,
                {
                    "benchmark": "ackermann",
                    "results": [{"language": "dark", "instructions": 123456}],
                },
            )


if __name__ == "__main__":
    unittest.main()
