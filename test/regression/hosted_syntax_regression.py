#!/usr/bin/env python3
"""Hosted package bodies in the interpreter's current ProgramTypes JSON render faithfully."""
import http.server
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading


def location(name):
    owner, *parts = name.split(".")
    return dict(owner=owner, modules=parts[:-1], name=parts[-1])


def infix(operation, left, right):
    # Current and pinned interpreters both serialize the trait-implementation choice.
    return {"EInfix": [1, {"InfixFnCall": [{operation: []}]}, left, right, {"Unknown": []}]}


def fn(name, parameters, result, body):
    return name, dict(typeParams=[], bounds=[], returnType=result, body=body,
                      parameters=[dict(name=p, typ=t) for p, t in parameters])


INT64 = {"TInt64": []}
ARG0 = {"EArg": [2, 0]}

TYPES = {}
ENTITIES = {
    # (2 ** x) + (5 ^ 1) + (1 << 3): power must not render as `^`, which is xor.
    "operators-hash": fn("Hosted.operators", [("x", INT64)], INT64,
        infix("ArithmeticPlus",
              infix("ArithmeticPlus", infix("ArithmeticPower", {"EInt64": [3, 2]}, ARG0),
                    infix("BitwiseXor", {"EInt64": [4, 5]}, {"EInt64": [5, 1]})),
              infix("ShiftLeft", {"EInt64": [6, 1]}, {"EInt64": [7, 3]}))),
    "pipe-hash": fn("Hosted.pipePlus", [("x", INT64)], INT64,
        {"EPipe": [1, ARG0, [{"EPipeInfix": [2, {"InfixFnCall": [{"ArithmeticPlus": []}]},
                                             {"EInt64": [3, 1]}, {"Unknown": []}]}]]}),
    "bigint-hash": fn("Hosted.doubleBig", [("x", {"TBigInt": []})], {"TBigInt": []},
        infix("ArithmeticMultiply", ARG0, {"EBigInt": [3, 2]})),
    "float-hash": fn("Hosted.isHalf", [("x", {"TFloat": []})], {"TBool": []},
        {"EMatch": [1, ARG0, [
            dict(pat={"MPFloat": [2, {"Positive": []}, "0", "5"]}, whenCondition={"None": []},
                 rhs={"EBool": [3, True]}),
            dict(pat={"MPVariable": [4, "_other"]}, whenCondition={"None": []},
                 rhs={"EBool": [5, False]})]]}),
    "bytes-hash": fn("Hosted.sameBytes", [("b", {"TBytes": []})], {"TBytes": []}, ARG0),
    "record-hash": fn("Hosted.recordPattern", [("x", INT64)], INT64,
        {"EMatch": [1, ARG0, [dict(pat={"MPRecord": [2, [["a", {"MPVariable": [3, "a"]}]]]},
                                   whenCondition={"None": []}, rhs={"EInt64": [4, 1]})]]}),
    "decimal-hash": fn("Hosted.price", [("x", INT64)], {"TDecimal": []}, {"EDecimal": [1, 19.99]}),
}


def nested_adds(count):
    # Each call level costs three JSON levels ({"EApply": [... [args]]}), so
    # 180 nested calls, which the parser accepts, pass the old 512 bound.
    add = {"EFnName": [1, dict(originalName=["Stdlib", "Int64", "add"],
                                 resolved={"Error": ["unresolved"]}), []]}
    body = ARG0
    for _ in range(count):
        body = {"EApply": [2, add, [], [body, {"EInt64": [3, 1]}]]}
    return body


def builtin(name, *args):
    callee = {"EFnName": [1, dict(originalName=["Builtin", name], resolved={"Ok": [dict(
        name={"Builtin": [dict(name=name, version=0)]}, location={"None": []})]}), []]}
    return {"EApply": [2, callee, [], list(args)]}


# The interpreter's stdlib calls its per-type operator builtins by name.
ENTITIES["builtin-ops-hash"] = fn("Hosted.builtinOps", [("x", INT64)], {"TBool": []},
    builtin("int64LessThan", builtin("int64Subtract", ARG0, {"EInt64": [3, 1]}),
            builtin("int64Negate", {"EInt64": [4, -50]})))


# -(x - 1) * 3 < x % 4, through the polymorphic operator builtins.
ENTITIES["builtin-poly-hash"] = fn("Hosted.builtinPoly", [("x", INT64)], {"TBool": []},
    builtin("lessThan", builtin("multiply", builtin("negate", builtin("subtract", ARG0, {"EInt64": [3, 1]})),
                                {"EInt64": [4, 3]}), builtin("modulo", ARG0, {"EInt64": [5, 4]})))


# (2 ** x) / 100 % 10, through the Power, Divide, Mod and ToString builtins.
ENTITIES["builtin-more-hash"] = fn("Hosted.builtinMore", [("x", INT64)], {"TString": []},
    builtin("int64ToString", builtin("int64Mod", builtin("int64Divide",
        builtin("int64Power", {"EInt64": [3, 2]}, ARG0), {"EInt64": [4, 100]}), {"EInt64": [5, 10]})))


# A trait call saved by the interpreter resolves to the trait's location.
TO_STRING = {"EFnName": [1, dict(originalName=["Darklang", "Stdlib", "ToString", "toString"],
    resolved={"Ok": [dict(location={"Some": [dict(owner="Darklang", modules=["Stdlib"], name="ToString")]},
        name={"TraitMethod": [dict(method_="toString", trait_={"Hash": ["to-string-hash"]},
                                   implFn={"Unknown": []})]})]}), []]}
ENTITIES["trait-call-hash"] = fn("Hosted.describeNumber", [("x", INT64)], {"TString": []},
    {"EApply": [2, TO_STRING, [], [ARG0]]})


# Builtins that are a pinned library fn under another name.
ENTITIES["builtin-renamed-hash"] = fn("Hosted.renamedBuiltins", [("x", INT64)], {"TBool": []},
    builtin("dictContainsKey", {"EDict": [1, [[{"EString": [2, [{"StringText": ["a"]}]]}, ARG0]]]},
            {"EString": [3, [{"StringText": ["a"]}]]}))


# `Builtin.<module><Fn>` is the module's fn: stringTrim, stringToUppercase.
ENTITIES["module-builtins-hash"] = fn("Hosted.moduleBuiltins", [("x", INT64)], {"TString": []},
    builtin("stringToUppercase", builtin("stringTrim", {"EString": [1, [{"StringText": ["  ab "]}]]})))


# A hosted fn calling the interpreter's own `__` helper, which `__` names in
# user code may not reach directly.
HELPER_NAME = dict(originalName=["Hosted", "__helper"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["helper-hash"]}]},
    location={"Some": [dict(owner="Hosted", modules=[], name="__helper")]})]})
# An internal record type, `__Box`, built and read by a hosted fn.
BOX_NAME = dict(originalName=["Hosted", "__Box"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["box-type-hash"]}]},
    location={"Some": [dict(owner="Hosted", modules=[], name="__Box")]})]})
TYPES["box-type-hash"] = ("Hosted.__Box", dict(hash={"Hash": ["box-type-hash"]}, description="",
    deprecated={"NotDeprecated": []}, declaration=dict(typeParams=[], definition={"Record": [[
        dict(name="value", typ=INT64, description="")]]})))
ENTITIES["box-hash"] = fn("Hosted.boxed", [("x", INT64)], INT64,
    {"ERecordFieldAccess": [1, {"ERecord": [2, BOX_NAME, [], [["value", ARG0]]]}, "value"]})
# Recursive through `self`, which names the helper the way its declaration does:
# if x <= 0 then 1 else self(x - 1) + 1.
ENTITIES["helper-hash"] = fn("Hosted.__helper", [("x", INT64)], INT64,
    {"EIf": [6, infix("ComparisonLessThanOrEqual", ARG0, {"EInt64": [3, 0]}), {"EInt64": [4, 1]},
             {"Some": [infix("ArithmeticPlus",
                             {"EApply": [7, {"ESelf": [8]}, [], [infix("ArithmeticMinus", ARG0, {"EInt64": [9, 1]})]]},
                             {"EInt64": [5, 1]})]}]})
ENTITIES["uses-helper-hash"] = fn("Hosted.usesHelper", [("x", INT64)], INT64,
    {"EApply": [2, {"EFnName": [1, HELPER_NAME, []]}, [], [ARG0]]})
ENTITIES["braces-hash"] = fn("Hosted.braces", [("x", INT64)], {"TString": []},
    {"EString": [1, [{"StringText": ["a{b}c"]}]]})
# An uncalled fn with type args is the fn as a value (darklang/dark@b928144):
# EFnName's fourth field.
MAP_VALUE = {"EFnName": [1, dict(originalName=["Stdlib", "List", "map"],
    resolved={"Error": ["unresolved"]}), [], [INT64, INT64]]}
ENTITIES["fn-value-hash"] = fn("Hosted.mapWithValue", [("x", INT64)], {"TList": [INT64]},
    {"ELet": [2, {"LPVariable": [3, "m"]}, MAP_VALUE,
        {"EApply": [4, {"EVariable": [5, "m"]}, [], [
            {"EList": [6, [ARG0, {"EInt64": [7, 2]}]]},
            {"ELambda": [8, [{"LPVariable": [9, "v"]}], infix("ArithmeticMultiply", {"EVariable": [10, "v"]}, {"EInt64": [11, 10]})]}]]}]})
# The interpreter's List.length is an Int64; the pinned library's is an Int.
LENGTH = dict(originalName=["Stdlib", "List", "length"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["length-hash"]}]},
    location={"Some": [dict(owner="Darklang", modules=["Stdlib", "List"], name="length")]})]})
ENTITIES["counts-hash"] = fn("Hosted.counts", [("x", INT64)], INT64,
    infix("ArithmeticPlus",
          {"EApply": [1, {"EFnName": [2, LENGTH, [], []]}, [], [{"EList": [3, [ARG0, ARG0]]}]]},
          {"EPipe": [4, {"EList": [5, [ARG0]]}, [{"EPipeFnCall": [6, LENGTH, [], [], []]}]]}))
# The interpreter's String.slice takes Int64 indexes; the pinned library's take Ints.
SLICE = dict(originalName=["Stdlib", "String", "slice"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["slice-hash"]}]},
    location={"Some": [dict(owner="Darklang", modules=["Stdlib", "String"], name="slice")]})]})
ENTITIES["slices-hash"] = fn("Hosted.slices", [("x", INT64)], {"TString": []},
    {"EPipe": [1, {"EApply": [2, {"EFnName": [3, SLICE, [], []]}, [],
        [{"EString": [4, [{"StringText": ["abcdef"]}]]}, {"EInt64": [5, 1]}, {"EInt64": [6, 5]}]]},
        [{"EPipeFnCall": [7, SLICE, [], [{"EInt64": [8, 1]}, ARG0], []]}]]})
# String.indexOf answers Option<Int64> here and Option<Int> in the compiler; a
# piped DateTime.fromSeconds takes its Int64 as the piped value:
# (indexOf "abcdef" "d" |> withDefault 0) + toSeconds (x |> fromSeconds).
def stdlib(modules, name, hash_):
    return dict(originalName=["Stdlib"] + modules + [name], resolved={"Ok": [dict(
        name={"Package": [{"Hash": [hash_]}]},
        location={"Some": [dict(owner="Darklang", modules=["Stdlib"] + modules, name=name)]})]})
INDEX_OF = stdlib(["String"], "indexOf", "index-of-hash")
WITH_DEFAULT = stdlib(["Option"], "withDefault", "with-default-hash")
FROM_SECONDS = stdlib(["DateTime"], "fromSeconds", "from-seconds-hash")
TO_SECONDS = stdlib(["DateTime"], "toSeconds", "to-seconds-hash")
ENTITIES["conversions-hash"] = fn("Hosted.conversions", [("x", INT64)], INT64,
    infix("ArithmeticPlus",
        {"EPipe": [1, {"EApply": [2, {"EFnName": [3, INDEX_OF, [], []]}, [],
            [{"EString": [4, [{"StringText": ["abcdef"]}]]}, {"EString": [5, [{"StringText": ["d"]}]]}]]},
            [{"EPipeFnCall": [6, WITH_DEFAULT, [], [{"EInt64": [7, 0]}], []]}]]},
        {"EApply": [8, {"EFnName": [9, TO_SECONDS, [], []]}, [],
            [{"EPipe": [10, ARG0, [{"EPipeFnCall": [11, FROM_SECONDS, [], [], []]}]]}]]}))
# The interpreter's Dict.fromList keeps the last of a repeated key:
# Dict.size (Dict.fromList [(1, x); (1, x)]) is 1.
FROM_LIST = stdlib(["Dict"], "fromList", "from-list-hash")
DICT_SIZE = stdlib(["Dict"], "size", "dict-size-hash")
ENTITIES["dict-last-hash"] = fn("Hosted.dictLast", [("x", INT64)], INT64,
    {"EApply": [1, {"EFnName": [2, DICT_SIZE, [], []]}, [], [
        {"EApply": [3, {"EFnName": [4, FROM_LIST, [], []]}, [], [{"EList": [5, [
            {"ETuple": [6, {"EInt64": [7, 1]}, ARG0, []]},
            {"ETuple": [8, {"EInt64": [9, 1]}, ARG0, []]}]]}]]}]]})
# A record field named by a keyword: (Tagged { type = x }).type.
TAGGED_NAME = dict(originalName=["Hosted", "Tagged"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["tagged-type-hash"]}]},
    location={"Some": [dict(owner="Hosted", modules=[], name="Tagged")]})]})
TYPES["tagged-type-hash"] = ("Hosted.Tagged", dict(hash={"Hash": ["tagged-type-hash"]}, description="",
    deprecated={"NotDeprecated": []}, declaration=dict(typeParams=[], definition={"Record": [[
        dict(name="type", typ=INT64, description="")]]})))
ENTITIES["tagged-hash"] = fn("Hosted.tagged", [("x", INT64)], INT64,
    {"ERecordFieldAccess": [1, {"ERecord": [2, TAGGED_NAME, [], [["type", ARG0]]]}, "type"]})
# `as` inside other patterns, refutable and not, with and without a guard:
# match (x, 1) with | (5 as five, n) -> five * 10 + n
#                   | ((a as b), _) when b > 5 -> a + b | _ -> 0
def case(pat, rhs, guard=None):
    return dict(pat=pat, rhs=rhs, whenCondition={"Some": [guard]} if guard else {"None": []})
VAR = lambda name: {"EVariable": [1, name]}
ENTITIES["as-hash"] = fn("Hosted.asPatterns", [("x", INT64)], INT64,
    {"EMatch": [1, {"ETuple": [2, ARG0, {"EInt64": [3, 1]}, []]}, [
        case({"MPTuple": [4, {"MPAs": [5, {"MPInt64": [6, 5]}, "five"]}, {"MPVariable": [7, "n"]}, []]},
             infix("ArithmeticPlus", infix("ArithmeticMultiply", VAR("five"), {"EInt64": [8, 10]}), VAR("n"))),
        case({"MPTuple": [9, {"MPAs": [10, {"MPVariable": [11, "a"]}, "b"]}, {"MPVariable": [12, "_"]}, []]},
             infix("ArithmeticPlus", VAR("a"), VAR("b")),
             infix("ComparisonGreaterThan", VAR("b"), {"EInt64": [13, 5]})),
        case({"MPVariable": [14, "_"]}, {"EInt64": [15, 0]})]]})
# Builtin.bytesLength answers an Int64; the compiler's Blob.length an Int.
ENTITIES["bytes-length-hash"] = fn("Hosted.bytesLen", [("b", {"TBytes": []})], INT64,
    builtin("bytesLength", ARG0))
# `**` on Int128 goes through the compiler's Power implementation.
ENTITIES["int128-power-hash"] = fn("Hosted.int128Power", [("x", {"TInt128": []})], {"TInt128": []},
    builtin("int128Power", ARG0, {"EInt128": [3, 3]}))


ENTITIES["deep-hash"] = fn("Hosted.deepAdds", [("x", INT64)], INT64, nested_adds(180))


# `type Overflow = | Overflow`: one case, named like its type, as in Stdlib.Math.
OVERFLOW_TYPE = dict(hash={"Hash": ["overflow-type-hash"]}, description="", deprecated={"NotDeprecated": []},
    declaration=dict(typeParams=[], definition={"Enum": [[dict(name="Overflow", fields=[], description="")]]}))
OVERFLOW_NAME = dict(originalName=["Hosted", "Overflow"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["overflow-type-hash"]}]},
    location={"Some": [dict(owner="Hosted", modules=[], name="Overflow")]})]})
ENTITIES["overflow-fn-hash"] = fn("Hosted.overflowName", [("u", {"TUnit": []})], {"TString": []},
    {"EMatch": [1, {"EEnum": [2, OVERFLOW_NAME, [], "Overflow", []]}, [
        dict(pat={"MPEnum": [3, "Overflow", []]}, whenCondition={"None": []},
             rhs={"EString": [4, [{"StringText": ["overflow"]}]]})]]})
TYPES["overflow-type-hash"] = ("Hosted.Overflow", OVERFLOW_TYPE)


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    for hash_, (_, body) in ENTITIES.items():
        body["hash"] = {"Hash": [hash_]}

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            parts = self.path.strip("/").split("/")
            payload = None
            if len(parts) == 3 and parts[:2] == ["function", "find"]:
                for hash_, (name, _) in ENTITIES.items():
                    if name == parts[2]:
                        payload = {"Hash": [hash_]}
            elif len(parts) == 4 and parts[:3] == ["type", "get", "with-location"]:
                if parts[3] in TYPES:
                    name, body = TYPES[parts[3]]
                    payload = dict(entity=body, location=location(name))
            elif len(parts) == 4 and parts[:3] == ["function", "get", "with-location"]:
                if parts[3] in ENTITIES:
                    name, body = ENTITIES[parts[3]]
                    payload = dict(entity=body, location=location(name))
            self.send_response(200 if payload else 404)
            self.end_headers()
            self.wfile.write(json.dumps(payload).encode())

        def log_message(self, *_):
            pass

    with http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        threading.Thread(target=server.serve_forever, daemon=True).start()
        with tempfile.TemporaryDirectory(prefix="hosted-syntax-") as temporary:
            directory = Path(temporary)
            env = dict(os.environ, XDG_DATA_HOME=temporary)
            source = directory / "input.dark"
            program = directory / "program"

            def compile_(text):
                source.write_text(text)
                return subprocess.run([compiler, "-q", "--emit-result", "--package-server",
                    f"http://127.0.0.1:{server.server_port}", str(source), "-o", str(program)],
                    env=env, capture_output=True, text=True, timeout=300)

            def run(text):
                result = compile_(text)
                assert result.returncode == 0, text + result.stdout + result.stderr
                return json.loads(subprocess.check_output([str(program)], text=True, timeout=10))

            assert run("Hosted.operators 10L\n") == 1036
            assert run("Hosted.pipePlus 41L\n") == 42
            assert run("Hosted.deepAdds 1L\n") == 181
            assert run("Hosted.builtinOps 50L\n") is True
            assert run("Hosted.overflowName ()\n") == "overflow"
            assert run("Hosted.describeNumber 42L\n") == "42"
            assert run("Hosted.renamedBuiltins 1L\n") is True
            assert run("Hosted.moduleBuiltins 1L\n") == "AB"
            assert run("Hosted.usesHelper 41L\n") == 42
            assert run("Hosted.mapWithValue 1L\n") == [10, 20]
            assert run("Hosted.counts 7L\n") == 3
            assert run("Hosted.boxed 42L\n") == 42
            assert run("Hosted.slices 3L\n") == "cd"
            assert run("Hosted.conversions 100L\n") == 103
            assert run('Hosted.bytesLen (Stdlib.Blob.fromString "abc")\n') == 3
            assert run("Hosted.dictLast 5L\n") == 1
            assert run("Hosted.tagged 7L\n") == 7
            assert [run(f"Hosted.asPatterns {x}L\n") for x in (5, 7, 1)] == [51, 14, 0]
            assert run("Hosted.braces 1L\n") == "a{b}c"
            assert str(run("Hosted.int128Power 2Q\n")) == "8"
            assert run("Hosted.builtinMore 7L\n") == "1"
            assert run("Hosted.builtinOps 52L\n") is False
            assert run("Hosted.builtinPoly 2L\n") is True
            assert run("Hosted.builtinPoly 0L\n") is False
            assert str(run("Hosted.doubleBig 12345678901234567890\n")) == "24691357802469135780"
            assert run("Hosted.isHalf 0.5\n") is True
            assert run("Hosted.isHalf 0.25\n") is False
            assert str(run('Stdlib.Blob.length (Hosted.sameBytes (Stdlib.Blob.fromString "abc"))\n')) == "3"
            for text, feature in (("Hosted.recordPattern 1L\n", "uses a record pattern"),):
                refused = compile_(text)
                output = refused.stdout + refused.stderr
                assert refused.returncode != 0, output
                assert "can't compile: " + feature in output, output
            # A batch report gives each failure a one-line reason an audit can count.
            items = []
            for index, text in enumerate(("Hosted.pipePlus 1L\n", "Hosted.recordPattern 1L\n",
                                          'Stdlib.Dict.containsKey (Dict { a: 1L }) "a"\n')):
                item = directory / f"item{index}.dark"
                item.write_text(text)
                items.append(dict(kind="function", name=f"item{index}", source=str(item),
                                  output=str(directory / f"item{index}.out")))
            manifest, report = directory / "manifest.json", directory / "report.jsonl"
            manifest.write_text(json.dumps(items))
            subprocess.run([compiler, "--batch", "-q", "--package-server",
                f"http://127.0.0.1:{server.server_port}", "--manifest", str(manifest),
                "--keep-going", "--report", str(report)], env=env, capture_output=True, timeout=300)
            reasons = [json.loads(line)["reason"] for line in report.read_text().splitlines()]
            assert reasons == ["", "uses a record pattern, which this compiler does not support",
                "uses function or value Stdlib.Dict.containsKey, which the compiler does not define;"
                " the compiler's pinned name is Stdlib.Dict.member"], reasons
            # --check answers per package item, without a caller or arguments.
            checked = subprocess.run([compiler, "--check", "--package-server",
                f"http://127.0.0.1:{server.server_port}", "Hosted.pipePlus", "Hosted.price",
                "Hosted.recordPattern", "Hosted.__helper"],
                env=env, capture_output=True, text=True, timeout=300)
            assert checked.returncode == 1, checked.stdout + checked.stderr
            assert checked.stdout.splitlines() == [
                "Hosted.pipePlus: compiles",
                "Hosted.price: compiles",
                "Hosted.recordPattern: can't compile: uses a record pattern, which this compiler does not support",
                "Hosted.__helper: compiles",
            ], checked.stdout + checked.stderr
        server.shutdown()
    print("Hosted operators, pipes, BigInt, Bytes and float patterns render; unsupported features name themselves, and batch and --check reports give the reason")


if __name__ == "__main__":
    main()
