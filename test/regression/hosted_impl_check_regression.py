#!/usr/bin/env python3
"""--check of a hosted impl's method beside the compiler's own impl of that trait."""
import http.server
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import threading
import urllib.parse


def location(name):
    owner, *parts = name.split(".")
    return dict(owner=owner, modules=parts[:-1], name=parts[-1])


def reference(name, hash_):
    return dict(originalName=name.split("."), resolved={"Ok": [dict(
        name={"Package": [{"Hash": [hash_]}]}, location={"Some": [location(name)]})]})


def main():
    compiler = str(Path(sys.argv[1]).resolve())
    # The interpreter's `impl Display for Int64`, whose method shows an Int64
    # through Display again: one more Display for Int64 beside the compiler's.
    method = "Darklang.Stdlib.Int64.Display.display"
    display = {"EApply": [1, {"EFnName": [2, dict(originalName=["Stdlib", "display"],
        resolved={"Error": ["unresolved"]}), []]}, [], [{"EArg": [3, 0]}]]}
    entities = {
        ("impl", "impl-hash"): ("Darklang.Stdlib.Int64.Display", dict(
            trait_=reference("Darklang.Stdlib.Display", "display-trait-hash"), traitTypeArgs=[],
            self={"TInt64": []}, typeParams=[], bounds=[], methods=[["display", reference(method, "method-hash")]])),
        ("function", "method-hash"): (method, dict(typeParams=[], bounds=[],
            parameters=[dict(name="value", typ={"TInt64": []})], returnType={"TString": []}, body=display)),
    }
    for (_, hash_), (_, body) in entities.items():
        body["hash"] = {"Hash": [hash_]}

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            search = urllib.parse.urlparse(self.path)
            parts = search.path.strip("/").split("/")
            payload = None
            if len(parts) == 1 or search.path == "/search":
                query = urllib.parse.parse_qs(search.query)
                module = (query["modules"][0] if search.path == "/search" else parts[0]).split(".")
                payload = dict(submodules=[], types=[], values=[], fns=[], traits=[], impls=[], tests=[])
                fields = {"function": "fns", "impl": "impls"}
                for (kind, _), (name, body) in entities.items():
                    parent = name.split(".")[:-1]
                    if parent == module and search.path != "/search":
                        payload[fields[kind]].append(dict(entity=body, location=location(name)))
                    elif parent[:len(module)] == module and len(parent) > len(module):
                        child = parent[:len(module) + 1]
                        if child not in payload["submodules"]:
                            payload["submodules"].append(child)
            elif len(parts) == 3 and parts[1] == "find":
                for (kind, hash_), (name, _) in entities.items():
                    if kind == parts[0] and name == parts[2]:
                        payload = {"Hash": [hash_]}
            elif len(parts) == 4 and parts[1:3] == ["get", "with-location"]:
                entity = entities.get((parts[0], parts[3]))
                if entity:
                    name, body = entity
                    payload = dict(entity=body, location=location(name))
            self.send_response(200 if payload else 404)
            self.end_headers()
            self.wfile.write(json.dumps(payload).encode())

        def log_message(self, *_):
            pass

    with http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler) as server:
        threading.Thread(target=server.serve_forever, daemon=True).start()
        with tempfile.TemporaryDirectory(prefix="hosted-impl-check-") as temporary:
            env = dict(os.environ, XDG_DATA_HOME=temporary)
            result = subprocess.run([compiler, "--package-server", f"http://127.0.0.1:{server.server_port}",
                                     "--check", method], env=env, capture_output=True, text=True, timeout=600)
            assert result.stdout.splitlines() == [method + ": compiles"], result.stdout + result.stderr
        server.shutdown()
    print("--check compiles a hosted impl's method beside the compiler's own impl")


if __name__ == "__main__":
    main()
