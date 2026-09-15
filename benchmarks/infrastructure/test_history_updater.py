#!/usr/bin/env python3
"""Focused tests for canonical benchmark report updates."""

import tempfile
import unittest
from types import SimpleNamespace
from pathlib import Path

from history_updater import (
    load_diagnostic_references,
    update_baselines,
    update_results,
)


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

    def test_results_include_diagnostic_instruction_counts_and_rust_multipliers(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            benchmarks_dir = Path(temporary)
            (benchmarks_dir / "baselines").mkdir()
            (benchmarks_dir / "BASELINES.md").write_text(
                "# Benchmark Baselines\n\n"
                "| Benchmark | Language | Instructions |\n"
                "|---|---|---:|\n"
                "| alpha | rust | 100 |\n"
            )
            (benchmarks_dir / "baselines" / "diagnostic-arm64-full-cachegrind.json").write_text(
                """{
  "schema_version": 1,
  "architecture": "arm64",
  "profile": "full",
  "measurement_policy": "policy",
  "contract_sha256": "contract",
  "generated_at": "2026-09-15T00:00:00+00:00",
  "implementations": {
    "darklang-interpreter": {"version": "v1", "benchmarks": [{"name": "alpha", "instructions": 250, "output_valid": true}]},
    "node": {"version": "v2", "benchmarks": [{"name": "alpha", "instructions": 400, "output_valid": false}]},
    "ocaml": {"version": "v3", "benchmarks": [{"name": "alpha", "instructions": 150, "output_valid": true}]},
    "python": {"version": "v4", "benchmarks": [{"name": "alpha", "instructions": 1000, "output_valid": true}]}
  }
}\n"""
            )
            snapshot = SimpleNamespace(
                benchmarks=[SimpleNamespace(name="alpha", instructions=200)],
                generated_at="2026-09-15T00:00:00+00:00",
                architecture="arm64",
                profile="full",
                schema_version=2,
                measurement_policy="policy",
                contract_sha256="contract",
                compiler=SimpleNamespace(commit="a" * 40, subject="subject"),
            )

            update_results(benchmarks_dir, snapshot)

            report = (benchmarks_dir / "RESULTS.md").read_text()
            self.assertIn("Darklang interpreter (2.50x)", report)
            self.assertNotIn("Node (4.00x)", report)
            self.assertIn("OCaml (1.50x)", report)
            self.assertIn("Python (10.0x)", report)
            self.assertIn("250 (2.50x)", report)
            self.assertNotIn("400 (4.00x)", report)
            self.assertIn("150 (1.50x)", report)
            self.assertIn("1,000 (10.0x)", report)

            stale_snapshot = SimpleNamespace(
                **{**snapshot.__dict__, "contract_sha256": "new-contract"}
            )
            self.assertEqual(
                load_diagnostic_references(benchmarks_dir, stale_snapshot), {}
            )


if __name__ == "__main__":
    unittest.main()
