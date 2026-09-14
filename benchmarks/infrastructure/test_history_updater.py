#!/usr/bin/env python3
"""Focused tests for canonical benchmark report updates."""

import tempfile
import unittest
from pathlib import Path

from history_updater import update_baselines


class HistoryUpdaterTests(unittest.TestCase):
    def test_rust_baseline_contains_only_instruction_counts(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            benchmarks_dir = Path(temporary)
            (benchmarks_dir / "BASELINES.md").write_text(
                "# Benchmark Baselines\n\n"
                "| Benchmark | Language | Instructions |\n"
                "|---|---|---:|\n"
                "| alpha | rust | 100 |\n"
            )

            update_baselines(
                benchmarks_dir,
                {"alpha": [{"language": "rust", "instructions": 125}]},
            )

            report = (benchmarks_dir / "BASELINES.md").read_text()
            self.assertIn("| Benchmark     | Language | Instructions     |", report)
            self.assertIn("| alpha", report)
            self.assertIn("125", report)
            self.assertNotIn("Data Refs", report)
            self.assertNotIn("Branches", report)


if __name__ == "__main__":
    unittest.main()
