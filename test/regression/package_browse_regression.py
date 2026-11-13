#!/usr/bin/env python3
"""Resolving one hosted name reads the modules near it, not the whole package tree."""
import http.server
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import urllib.parse

# Big.Unrelated0..19, each with five submodules, beside the one module in use.
UNRELATED = [f"Big.Unrelated{i}" for i in range(20)]
MODULES = ["Big", "Big.App", "Big.App.Math"] + UNRELATED + [
    f"{module}.Part{j}" for module in UNRELATED for j in range(5)]
FN = dict(hash={"Hash": ["inc-hash"]}, typeParams=[], bounds=[], returnType={"TInt64": []},
          parameters=[dict(name="x", typ={"TInt64": []})],
          body={"EInfix": [1, {"InfixFnCall": [{"ArithmeticPlus": []}]}, {"EArg": [2, 0]},
                           {"EInt64": [3, 1]}, {"Unknown": []}]})
LOCATION = dict(owner="Big", modules=["App", "Math"], name="inc")
# A trait beside inc, with an implementation over Decimal that inc never uses.
TRAIT = dict(hash={"Hash": ["show-hash"]}, typeParams=["a"], bounds=[], methods=[
    dict(name="show", typeParams=[], bounds=[], parameters=[dict(name="v", typ={"TVariable": ["a"]})],
         returnType={"TString": []})])
SHOW_DECIMAL = dict(hash={"Hash": ["show-decimal-hash"]}, typeParams=[], bounds=[],
                    parameters=[dict(name="v", typ={"TDecimal": []})], returnType={"TString": []},
                    body={"EString": [1, [{"StringText": ["decimal"]}]]})
SHOW_REF = dict(originalName=["Big", "App", "Math", "Show"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["show-hash"]}]},
    location={"Some": [dict(owner="Big", modules=["App", "Math"], name="Show")]})]})
DECIMAL_REF = dict(originalName=["Big", "App", "Math", "showDecimal"], resolved={"Ok": [dict(
    name={"Package": [{"Hash": ["show-decimal-hash"]}]},
    location={"Some": [dict(owner="Big", modules=["App", "Math"], name="showDecimal")]})]})
IMPL = dict(hash={"Hash": ["impl-hash"]}, trait_=SHOW_REF, traitTypeArgs=[], self={"TDecimal": []},
            typeParams=[], bounds=[], methods=[["show", DECIMAL_REF]])


def inventory(module):
    # Like the interpreter's /:location browser: a module's direct items and children.
    parts = module.split(".")
    children = [m.split(".") for m in MODULES if m.split(".")[:-1] == parts]
    here = module == "Big.App.Math"
    at = lambda name: dict(owner="Big", modules=["App", "Math"], name=name)
    fns = [dict(entity=FN, location=LOCATION),
           dict(entity=SHOW_DECIMAL, location=at("showDecimal"))] if here else []
    traits = [dict(entity=TRAIT, location=at("Show"))] if here else []
    impls = [dict(entity=IMPL, location=at("DecimalShow"))] if here else []
    return dict(submodules=children, types=[], values=[], fns=fns, traits=traits, impls=impls, tests=[])


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    requests = []

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            requests.append(self.path)
            url = urllib.parse.urlparse(self.path)
            parts = url.path.strip("/").split("/")
            if url.path == "/search":
                payload = inventory(urllib.parse.parse_qs(url.query)["modules"][0])
            elif parts == ["function", "find", "Big.App.Math.inc"]:
                payload = {"Hash": ["inc-hash"]}
            elif parts[:3] == ["function", "get", "with-location"] and parts[3] == "inc-hash":
                payload = dict(entity=FN, location=LOCATION)
            elif parts[:3] == ["function", "get", "with-location"] and parts[3] == "show-decimal-hash":
                payload = dict(entity=SHOW_DECIMAL, location=dict(owner="Big", modules=["App", "Math"],
                                                                   name="showDecimal"))
            elif len(parts) == 1 and parts[0] == "Big.App.Math.inc":
                payload = FN
            elif len(parts) == 1 and parts[0].startswith("Big"):
                # Every other location answers with a (possibly empty) module listing.
                payload = inventory(parts[0])
            else:
                payload = None
            self.send_response(200 if payload is not None else 404)
            self.end_headers()
            self.wfile.write(json.dumps(payload).encode())

        def log_message(self, *_):
            pass

    with http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        threading.Thread(target=server.serve_forever, daemon=True).start()
        with tempfile.TemporaryDirectory(prefix="package-browse-") as temporary:
            directory = Path(temporary)
            env = dict(os.environ, XDG_DATA_HOME=temporary)
            source, program = directory / "input.dark", directory / "program"
            for text, succeeds in (("Big.App.Math.inc 41L\n", True), ("Big.App.Math.nope 41L\n", False)):
                requests.clear()
                source.write_text(text)
                result = subprocess.run([compiler, "-q", "--emit-result", "--package-server",
                    f"http://127.0.0.1:{server.server_port}", str(source), "-o", str(program)],
                    env=env, capture_output=True, text=True, timeout=300)
                assert (result.returncode == 0) == succeeds, text + result.stdout + result.stderr
                if succeeds:
                    assert json.loads(subprocess.check_output([str(program)], text=True, timeout=10)) == 42
                else:
                    assert "Big.App.Math.nope" in result.stdout + result.stderr, result.stdout + result.stderr
                unrelated = [r for r in requests if "Unrelated" in r]
                assert unrelated == [], (text, len(unrelated), unrelated[:5])
            # A --check over several names in one module reads each answer once.
            requests.clear()
            result = subprocess.run([compiler, "--package-server",
                f"http://127.0.0.1:{server.server_port}", "--check", "Big.App.Math.inc",
                "Big.App.Math.inc"],
                env=env, capture_output=True, text=True, timeout=300)
            assert result.stdout.count(": compiles") == 2, result.stdout + result.stderr
            repeated = sorted({r for r in requests if requests.count(r) > 1})
            assert repeated == [], repeated
        server.shutdown()
    print("Hosted names resolve from their own modules without reading unrelated ones, "
          "an unused implementation over an unsupported type does not block them, "
          "and a --check batch reads each package answer once")


if __name__ == "__main__":
    main()
