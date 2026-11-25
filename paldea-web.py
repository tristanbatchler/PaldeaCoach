#!/usr/bin/env python3
"""Tiny phone-friendly web UI for Paldea Coach.

  POKEMON_SAVE=/path/to/main python3 paldea-web.py     ->  open http://<this-pc-ip>:8765 on your phone

Environment: POKEMON_SAVE (save file; defaults to ./main next to this script),
PALDEA_WEB_HOST (default 0.0.0.0, i.e. reachable on your LAN), PALDEA_WEB_PORT (default 8765).
No authentication: only run it on a network you trust.
"""
import html, os, re, subprocess, urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

ROOT = os.path.dirname(os.path.abspath(__file__))
CLI = os.path.join(ROOT, "paldea")
HOST = os.environ.get("PALDEA_WEB_HOST", "0.0.0.0")
PORT = int(os.environ.get("PALDEA_WEB_PORT", "8765"))

def run(*args):
    r = subprocess.run([CLI, *args], capture_output=True, text=True)
    return (r.stdout + r.stderr).strip()

# Names for autosuggest, e.g. "Meowth", "Meowth-Alola", "Iron Valiant"
NAMES = sorted(set(re.findall(r"^#\d+ (.+?) \[", run("dex", ""), re.M)))
OPTIONS = "".join(f'<option value="{html.escape(n)}">' for n in NAMES)

PAGE = """<!doctype html><meta name=viewport content="width=device-width,initial-scale=1">
<title>Paldea Coach</title><style>
body{font:16px system-ui;margin:0;padding:16px;background:#111;color:#eee}
form{display:flex;gap:8px}input{flex:1;min-width:0}
input,button{font-size:18px;padding:12px;border-radius:10px;border:1px solid #444;background:#222;color:#eee}
button{background:#c33;border:0;color:#fff}a{color:#8af}
pre{white-space:pre-wrap;font-size:13px;line-height:1.4;background:#1a1a1a;padding:12px;border-radius:10px}
</style>
<form action=/><input name=vs list=n placeholder="Opponent…" value="{q}" autocomplete=off autofocus>
<button>Go</button></form><datalist id=n>{options}</datalist>
<p><a href=/?summary=1>My party</a> · <a href=/?coverage=1>Type coverage</a> · <a href=/?craft=1>TM crafting</a> · <a href=/?bagplan=1>Bag plan</a></p><pre>{out}</pre>"""

PAGES = ("summary", "coverage", "craft", "bagplan")

class H(BaseHTTPRequestHandler):
    def do_GET(self):
        q = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
        name = q.get("vs", [""])[0].strip()
        page = next((p for p in PAGES if p in q), None)
        out = run("vs", name) if name else run(page) if page else ""
        body = (PAGE.replace("{q}", html.escape(name)).replace("{options}", OPTIONS)
                    .replace("{out}", html.escape(out))).encode()
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.end_headers()
        self.wfile.write(body)

print(f"{len(NAMES)} names loaded. Open http://<this-pc-ip>:{PORT} on your phone.")
ThreadingHTTPServer((HOST, PORT), H).serve_forever()
