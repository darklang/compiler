#!/usr/bin/env python3
"""Focused tests for Cachegrind report output."""

import tempfile
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path

from cachegrind_processor import generate_summary


class CachegrindProcessorTests(unittest.TestCase):
    def test_quiet_summary_keeps_report_but_omits_markdown_from_stdout(self) -> None:
        results = {
            "alpha": [
                {
                    "language": "dark",
                    "instructions": 100,
                    "data_refs": 50,
                    "d1_misses": 2,
                    "ll_misses": 1,
                    "branches": 10,
                    "branch_mispredicts": 1,
                }
            ]
        }
        with tempfile.TemporaryDirectory() as temporary:
            output_dir = Path(temporary)
            output = StringIO()
            with redirect_stdout(output):
                generate_summary(results, output_dir, quiet=True)

            report = output_dir / "cachegrind_summary.md"
            self.assertIn("## alpha", report.read_text())
            self.assertEqual(
                output.getvalue(), f"Cachegrind summary written to: {report}\n"
            )


if __name__ == "__main__":
    unittest.main()
