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
    "decimal-hash": fn("Hosted.price", [], {"TDecimal": []}, {"EDecimal": [1, "19.99"]}),
}


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
            assert str(run("Hosted.doubleBig 12345678901234567890\n")) == "24691357802469135780"
            assert run("Hosted.isHalf 0.5\n") is True
            assert run("Hosted.isHalf 0.25\n") is False
            assert str(run('Stdlib.Blob.length (Hosted.sameBytes (Stdlib.Blob.fromString "abc"))\n')) == "3"
            for text, feature in (("Hosted.recordPattern 1L\n", "uses a record pattern"),
                                  ("Hosted.price\n", "uses the Decimal type")):
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
        server.shutdown()
    print("Hosted operators, pipes, BigInt, Bytes and float patterns render; unsupported features name themselves, and batch reports give the reason")


if __name__ == "__main__":
    main()
