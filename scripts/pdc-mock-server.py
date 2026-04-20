#!/usr/bin/env python3
import argparse
import json
import re
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def _utc_now_text() -> str:
    return datetime.now(timezone.utc).isoformat()


class PdcMockHandler(BaseHTTPRequestHandler):
    token = ""
    data_dir = Path(".")

    def _write_json(self, status_code: int, payload: dict) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _read_json_body(self) -> dict:
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return {}
        raw = self.rfile.read(length)
        if not raw:
            return {}
        try:
            return json.loads(raw.decode("utf-8"))
        except Exception:
            return {}

    def _save_payload(self, name: str, payload: dict) -> None:
        out = self.data_dir / f"{name}.json"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(
            json.dumps(
                {
                    "savedAtUtc": _utc_now_text(),
                    "path": self.path,
                    "payload": payload,
                },
                ensure_ascii=False,
                indent=2,
            ),
            encoding="utf-8",
        )

    def _check_token(self) -> bool:
        expected = (self.token or "").strip()
        if not expected:
            return True
        provided = (self.headers.get("X-PDC-Token") or "").strip()
        return provided == expected

    def do_GET(self) -> None:
        if self.path == "/healthz":
            self._write_json(200, {"ok": True, "timeUtc": _utc_now_text()})
            return

        self._write_json(404, {"error": "not_found", "path": self.path})

    def do_POST(self) -> None:
        if not self._check_token():
            self._write_json(401, {"error": "unauthorized"})
            return

        payload = self._read_json_body()

        if self.path == "/api/v1/fileMaps/diff":
            items = payload.get("items") if isinstance(payload, dict) else {}
            keys = sorted(items.keys()) if isinstance(items, dict) else []
            self._save_payload("filemaps-diff-request", payload)
            result = {
                "success": True,
                "code": 0,
                "message": "ok",
                "content": keys,
                "Content": keys,
            }
            self._write_json(200, result)
            return

        if self.path == "/api/v1/fileMaps/upload":
            self._save_payload("filemaps-upload-request", payload)
            result = {
                "success": True,
                "code": 0,
                "message": "ok",
                "content": True,
                "Content": True,
            }
            self._write_json(200, result)
            return

        m = re.match(r"^/api/v1/distribution/([^/]+)/([^/]+)$", self.path)
        if m:
            primary_version = m.group(1)
            version = m.group(2)
            distribution_id = f"{primary_version}-{version}"
            self._save_payload("distribution-request", payload)
            result = {
                "success": True,
                "code": 0,
                "message": "ok",
                "content": {"distributionId": distribution_id},
                "Content": {"distributionId": distribution_id},
            }
            self._write_json(200, result)
            return

        self._write_json(404, {"error": "not_found", "path": self.path})

    def log_message(self, fmt: str, *args) -> None:
        print(f"[pdc-mock] {self.address_string()} - {fmt % args}")


def main() -> None:
    parser = argparse.ArgumentParser(description="PDC mock server for CI fallback")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=18765)
    parser.add_argument("--token", default="")
    parser.add_argument("--data-dir", required=True)
    args = parser.parse_args()

    PdcMockHandler.token = args.token
    PdcMockHandler.data_dir = Path(args.data_dir)
    PdcMockHandler.data_dir.mkdir(parents=True, exist_ok=True)

    server = ThreadingHTTPServer((args.host, args.port), PdcMockHandler)
    print(f"[pdc-mock] listening on http://{args.host}:{args.port}")
    server.serve_forever()


if __name__ == "__main__":
    main()
