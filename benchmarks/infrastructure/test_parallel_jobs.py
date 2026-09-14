#!/usr/bin/env python3
"""Concurrency-limit coverage for the benchmark runner's shell job scheduler."""

from __future__ import annotations

import subprocess
import tempfile
import time
import unittest
from pathlib import Path


class ParallelJobTests(unittest.TestCase):
    def test_two_workers_start_while_third_waits(self) -> None:
        helper = Path(__file__).with_name("parallel_jobs.sh")
        script = """
source "$1"
work_dir="$2"
worker() {
    touch "$work_dir/$1.started"
    while [ ! -f "$work_dir/release" ]; do sleep 0.01; done
}
run_parallel_jobs 2 worker first second third
"""
        with tempfile.TemporaryDirectory() as temporary:
            work_dir = Path(temporary)
            process = subprocess.Popen(
                ["bash", "-c", script, "bash", str(helper), str(work_dir)]
            )
            deadline = time.monotonic() + 2
            while time.monotonic() < deadline:
                if (work_dir / "first.started").exists() and (
                    work_dir / "second.started"
                ).exists():
                    break
                if process.poll() is not None:
                    break
                time.sleep(0.01)

            first_started = (work_dir / "first.started").exists()
            second_started = (work_dir / "second.started").exists()
            third_started_before_release = (work_dir / "third.started").exists()
            (work_dir / "release").touch()
            returncode = process.wait(timeout=2)

            self.assertTrue(first_started)
            self.assertTrue(second_started)
            self.assertFalse(third_started_before_release)
            self.assertEqual(returncode, 0)
            self.assertTrue((work_dir / "third.started").exists())


if __name__ == "__main__":
    unittest.main()
