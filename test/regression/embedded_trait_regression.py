#!/usr/bin/env python3
"""A trait the compiler embeds, listed beside a hosted fn, is not declared twice."""
import http.server
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import urllib.parse

# inc calls the trait's method by the trait's hash: x ^ 3.
XOR_CALL = dict(originalName=["Stdlib", "BitwiseXor", "bitwiseXor"], resolved={"Ok": [dict(
    name={"TraitMethod": [dict(method_="bitwiseXor", trait_={"Hash": ["xor-hash"]})]},
    location={"Some": [dict(owner="Darklang", modules=["Stdlib"], name="BitwiseXor")]})]})
FN = dict(hash={"Hash": ["inc-hash"]}, typeParams=[], bounds=[], returnType={"TInt64": []},
          parameters=[dict(name="x", typ={"TInt64": []})],
          body={"EApply": [1, {"EFnName": [2, XOR_CALL, []]}, [], [{"EArg": [3, 0]}, {"EInt64": [4, 3]}]]})
LOCATION = dict(owner="Darklang", modules=["Stdlib"], name="incXor")
# The interpreter's own BitwiseXor trait, which the compiler also declares.
TRAIT = dict(hash={"Hash": ["xor-hash"]}, typeParams=["a"], bounds=[], methods=[
    dict(name="bitwiseXor", typeParams=[], bounds=[],
         parameters=[dict(name="a", typ={"TVariable": ["a"]}), dict(name="b", typ={"TVariable": ["a"]})],
         returnType={"TVariable": ["a"]})])


def inventory(module):
    stdlib = module == "Darklang.Stdlib"
    return dict(submodules=[], types=[], values=[],
                fns=[dict(entity=FN, location=LOCATION)] if stdlib else [],
                traits=[dict(entity=TRAIT, location=dict(owner="Darklang", modules=["Stdlib"],
                                                         name="BitwiseXor"))] if stdlib else [],
                impls=[], tests=[])


def main():
    compiler = str(Path(sys.argv[1]).resolve())

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            url = urllib.parse.urlparse(self.path)
            parts = url.path.strip("/").split("/")
            if url.path == "/search":
                payload = inventory(urllib.parse.parse_qs(url.query)["modules"][0])
            elif parts == ["function", "find", "Darklang.Stdlib.incXor"]:
                payload = {"Hash": ["inc-hash"]}
            elif parts[:3] == ["function", "get", "with-location"] and parts[3] == "inc-hash":
                payload = dict(entity=FN, location=LOCATION)
            elif parts[:3] == ["trait", "get", "with-location"] and parts[3] == "xor-hash":
                payload = dict(entity=TRAIT, location=dict(owner="Darklang", modules=["Stdlib"],
                                                           name="BitwiseXor"))
            elif len(parts) == 1 and parts[0].startswith("Darklang"):
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
        with tempfile.TemporaryDirectory(prefix="embedded-trait-") as temporary:
            env = dict(os.environ, XDG_DATA_HOME=temporary)
            result = subprocess.run([compiler, "--package-server",
                f"http://127.0.0.1:{server.server_port}", "--check", "Darklang.Stdlib.incXor"],
                env=env, capture_output=True, text=True, timeout=300)
            assert result.stdout.strip() == "Darklang.Stdlib.incXor: compiles", \
                result.stdout + result.stderr
        server.shutdown()
    print("A hosted trait the compiler already declares is left to the compiler's declaration")


if __name__ == "__main__":
    main()
