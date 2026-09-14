#!/usr/bin/env python3
"""Integration coverage for smoke validation of prebuilt benchmark binaries."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


class PrebuiltSmokeTests(unittest.TestCase):
    def test_smoke_validates_a_prebuilt_binary(self) -> None:
        project_root = Path(__file__).resolve().parents[2]
        with tempfile.TemporaryDirectory() as temporary:
            binaries = Path(temporary)
            binary = binaries / "ackermann" / "dark" / "main"
            binary.parent.mkdir(parents=True)
            binary.write_text("#!/bin/sh\nprintf '4093\\n'\n")
            binary.chmod(0o755)

            result = subprocess.run(
                [
                    project_root / "benchmarks" / "quick_check.sh",
                    "--smoke",
                    "--quiet",
                    "--benchmarks=ackermann",
                    f"--prebuilt-dir={binaries}",
                ],
                cwd=project_root,
                capture_output=True,
                text=True,
            )

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("SMOKE_SUMMARY passed=1 failed=0", result.stdout)


if __name__ == "__main__":
    unittest.main()
