"""Serve the Obenseuer ledger page and pass its Check button on to the game PC.

Usage: python3 server.py
The saves are on the game PC, which opens no ports, so export_ledger.py holds a request to
/api/wait open here and a press of the button answers it. Python standard library only;
the state of the current check is kept in memory.
"""
import json
import os
import threading
import time
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

PORT = 8090
SITE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "site")
WAIT_SECONDS = 25  # how long one /api/wait is held open
CONNECTED_SECONDS = WAIT_SECONDS + 10  # silence after which the game PC counts as gone

changed = threading.Condition()
# Ids start from the clock so they keep rising across a restart; the game PC waits for a higher one.
check = {"id": int(time.time()), "phase": "idle", "message": ""}
seen = None  # when the game PC was last heard from


def touch():
    global seen
    seen = time.monotonic()


def status():
    return {**check, "connected": seen is not None and time.monotonic() - seen < CONNECTED_SECONDS}


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=SITE, **kwargs)

    def log_message(self, *args):
        pass  # an open page polls once a minute; errors still reach the journal

    def reply(self, body):
        data = json.dumps(body).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        url = urlparse(self.path)
        if url.path == "/api/status":
            self.reply(status())
        elif url.path == "/api/wait":
            self.wait(int(parse_qs(url.query).get("after", ["-1"])[0]))
        else:
            super().do_GET()

    def do_POST(self):
        path = urlparse(self.path).path
        if path == "/api/check":
            self.press()
        elif path == "/api/report":
            self.report()
        else:
            self.send_error(404)

    def wait(self, after):
        touch()  # only here: a wait ending says nothing, as its client may be long gone
        with changed:
            changed.wait_for(lambda: check["id"] > after, WAIT_SECONDS)
        self.reply(status())

    def press(self):
        """Start a check, or join the one already under way."""
        with changed:
            if check["phase"] not in ("requested", "running"):
                check.update(id=check["id"] + 1, phase="requested", message="")
                changed.notify_all()
        self.reply(status())

    def report(self):
        body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
        touch()
        with changed:
            if body.get("id") == check["id"]:
                check.update(phase=str(body.get("phase"))[:20], message=str(body.get("message"))[:300])
        self.reply(status())


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
