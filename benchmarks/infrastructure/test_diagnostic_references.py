#!/usr/bin/env python3
"""Focused tests for diagnostic reference benchmark measurement."""

import re
import unittest
from pathlib import Path

from diagnostic_references import (
    adapt_interpreter_source,
    inject_interpreter_arguments,
    parse_instruction_count,
    source_path,
)
from benchmark_profiles import load_invocation, load_profile


class DiagnosticReferenceTests(unittest.TestCase):
    def test_interpreter_arguments_replace_each_cli_lookup(self) -> None:
        source = (
            "match (Stdlib.Cli.Args.int64 0, "
            "Stdlib.Cli.Args.int64 1) with\n"
            "| (Ok first, Ok second) -> first + second\n"
        )

        transformed = inject_interpreter_arguments(source, ("4", "7"))

        self.assertIn("match (Ok 4L, Ok 7L) with", transformed)
        self.assertNotIn("Stdlib.Cli.Args.int64", transformed)

    def test_interpreter_argument_injection_rejects_an_unknown_index(self) -> None:
        with self.assertRaisesRegex(ValueError, "argument index 2"):
            inject_interpreter_arguments(
                "Stdlib.Cli.Args.int64 2", ("4", "7")
            )

    def test_interpreter_arguments_support_the_shared_index_helper(self) -> None:
        transformed = inject_interpreter_arguments(
            "match Stdlib.Cli.Args.int64 index with | Ok value -> value",
            ("4", "7"),
        )

        self.assertIn("match index with | 0 -> Ok 4L | 1 -> Ok 7L", transformed)

    def test_instruction_count_requires_one_positive_cachegrind_summary(self) -> None:
        self.assertEqual(parse_instruction_count("==1== I refs: 12,345\n"), 12345)
        with self.assertRaisesRegex(ValueError, "exactly one"):
            parse_instruction_count("==1== I refs: 12\n==2== I refs: 13\n")

    def test_every_reference_source_consumes_each_profile_argument(self) -> None:
        benchmarks_dir = Path(__file__).resolve().parent.parent
        patterns = {
            "darklang-interpreter": lambda index: (
                rf"\b(?:benchmarkArg|Stdlib\.Cli\.Args\.int64)\s+{index}\b"
            ),
            "node": lambda index: rf"\bargument\(\s*{index}\s*\)",
            "ocaml": lambda index: rf"\bargument(?:64)?\s+{index}\b",
            "python": lambda index: rf"\bargument\(\s*{index}\s*\)",
        }
        for name in load_profile(benchmarks_dir, "full"):
            invocation = load_invocation(benchmarks_dir, "full", name)
            for language in patterns:
                source = source_path(benchmarks_dir, name, language)
                if not source.is_file():
                    continue
                contents = source.read_text()
                if language == "darklang-interpreter":
                    self.assertIn("Stdlib.Cli.Args.int64", contents)
                    self.assertNotRegex(contents, r"Stdlib\.[A-Za-z0-9_.]*__")
                for index in range(len(invocation.args)):
                    with self.subTest(name=name, language=language, index=index):
                        self.assertRegex(contents, re.compile(patterns[language](index)))

    def test_interpreter_adapter_translates_compatibility_only_syntax(self) -> None:
        source = (
            "type Tree = Leaf | Node of Int64\n"
            "let f (values: Dict<Int64>) (pair: (Int64 * Int64)) = pair.0\n"
            "Stdlib.Dict.get<Int64> values \"key\"\n"
            "Stdlib.String.equals left right\n"
            "| Ok closed ->\n"
            "            let nextState = moveCompiler (closed.0) next "
            "(closed.0.reversed) (closed.1) state.trimNext in\n"
            "            let withGoto = emit nextState (Goto (closed.2)) in\n"
            "| Ok closed -> use closed.0 closed.1\n"
            "Stdlib.Cli.Args.int64 0\n"
        )

        transformed = adapt_interpreter_source(source, ("4",))

        self.assertIn("type Tree = Leaf | Node of Int64", transformed)
        self.assertIn("Dict<String, Int64>", transformed)
        self.assertIn("Stdlib.Dict.get<String, Int64>", transformed)
        self.assertIn("Stdlib.Tuple2.first pair", transformed)
        self.assertIn("left == right", transformed)
        self.assertIn("Stdlib.Tuple3.second closedFor", transformed)
        self.assertIn("Stdlib.Tuple2.second closed", transformed)


if __name__ == "__main__":
    unittest.main()
