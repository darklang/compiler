#!/usr/bin/env python3
"""Focused tests for scripts/validate-darklang.py."""

import importlib.util
from pathlib import Path


def load_validate_darklang_module():
    script_path = Path(__file__).with_name("validate-darklang.py")
    spec = importlib.util.spec_from_file_location("validate_darklang", script_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def test_canonical_source_is_embedded_unchanged():
    module = load_validate_darklang_module()
    expected = "fun x y -> x + y"
    source = module.CanonicalSource()
    actual = source.prepare(expected)
    assert actual == expected, f"expected {expected!r}, got {actual!r}"
    assert "let __result = [1, 2]" in source.generate_file_code("[1, 2]")


def main():
    test_canonical_source_is_embedded_unchanged()


if __name__ == "__main__":
    main()
