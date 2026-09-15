#!/usr/bin/env python3
"""Focused tests for diagnostic reference benchmark measurement."""

import unittest

from diagnostic_references import (
    adapt_interpreter_source,
    adapt_node_source,
    inject_interpreter_arguments,
    parse_instruction_count,
)


class DiagnosticReferenceTests(unittest.TestCase):
    def test_interpreter_arguments_replace_each_cli_lookup(self) -> None:
        source = (
            "match (Stdlib.Cli.Args.int64 0, Stdlib.Cli.Args.int64 1) with\n"
            "| (Ok first, Ok second) -> first + second\n"
        )

        transformed = inject_interpreter_arguments(source, ("4", "7"))

        self.assertIn("match (Ok 4L, Ok 7L) with", transformed)
        self.assertNotIn("Stdlib.Cli.Args", transformed)

    def test_interpreter_argument_injection_rejects_an_unknown_index(self) -> None:
        with self.assertRaisesRegex(ValueError, "argument index 2"):
            inject_interpreter_arguments("Stdlib.Cli.Args.int64 2", ("4", "7"))

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

    def test_node_leibniz_adapter_preserves_the_loop_work(self) -> None:
        source = """function leibnizLoop(i, n, sum, sign) {
    if (i >= n) return sum * 4.0;
    const term = sign / (2 * i + 1);
    return leibnizLoop(i + 1, n, sum + term, -sign);
}

function leibnizPi(n) { return leibnizLoop(0, n, 0.0, 1.0); }
"""

        transformed = adapt_node_source("leibniz", source)

        self.assertIn("while (i < n)", transformed)
        self.assertIn("sum += term", transformed)
        self.assertIn("function leibnizPi", transformed)

    def test_interpreter_adapter_translates_compatibility_only_syntax(self) -> None:
        source = (
            "type Tree = Leaf | Node of Int64\n"
            "let f (values: Dict<Int64>) (pair: (Int64 * Int64)) = pair.0\n"
            "Stdlib.String.equals left right\n"
            "Stdlib.Float.toInt 1.0\n"
            "Stdlib.Cli.Args.int64 0\n"
        )

        transformed = adapt_interpreter_source(source, ("4",))

        self.assertIn("type Tree = Leaf | Node of Int64", transformed)
        self.assertIn("Dict<String, Int64>", transformed)
        self.assertIn("Stdlib.Tuple2.first pair", transformed)
        self.assertIn("left == right", transformed)
        self.assertIn("interpreterFloatToInt64 1.0", transformed)


if __name__ == "__main__":
    unittest.main()
