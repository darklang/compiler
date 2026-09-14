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
                }
            ]
        }
        with tempfile.TemporaryDirectory() as temporary:
            output_dir = Path(temporary)
            output = StringIO()
            with redirect_stdout(output):
                generate_summary(results, output_dir, quiet=True)

            report = output_dir / "cachegrind_summary.md"
            report_text = report.read_text()
            self.assertIn("## alpha", report_text)
            self.assertIn("| Language | Instructions | vs Rust |", report_text)
            self.assertNotIn("Data Refs", report_text)
            self.assertNotIn("Branches", report_text)
            self.assertEqual(
                output.getvalue(), f"Cachegrind summary written to: {report}\n"
            )


if __name__ == "__main__":
    unittest.main()
