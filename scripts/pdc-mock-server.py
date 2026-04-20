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
    protocol_version = "HTTP/1.1"
    token = ""
    data_dir = Path(".")

    def _write_json(self, status_code: int, payload: dict) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status_code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(body)
        self.wfile.flush()
        self.close_connection = True

    def handle_expect_100(self) -> bool:
        self.send_response_only(100)
        self.end_headers()
        return True

    def _read_chunked_body(self) -> bytes:
        chunks = bytearray()
        while True:
            size_line = self.rfile.readline()
            if not size_line:
                break

            size_line = size_line.strip()
            if not size_line:
                continue

            size_text = size_line.split(b";", 1)[0]
            chunk_size = int(size_text, 16)
            if chunk_size == 0:
                # Consume optional trailer headers until the terminating blank line.
                while True:
                    trailer = self.rfile.readline()
                    if trailer in (b"", b"\r\n", b"\n"):
                        break
                break

            remaining = chunk_size
            while remaining > 0:
                part = self.rfile.read(remaining)
                if not part:
                    raise ConnectionError("unexpected end of stream while reading chunked request body")
                chunks.extend(part)
                remaining -= len(part)

            chunk_terminator = self.rfile.read(2)
            if chunk_terminator == b"\r\n":
                continue
            if chunk_terminator[:1] != b"\n":
                raise ValueError("invalid chunk terminator")

        return bytes(chunks)

    def _read_request_body(self) -> bytes:
        transfer_encoding = (self.headers.get("Transfer-Encoding") or "").lower()
        if "chunked" in transfer_encoding:
            return self._read_chunked_body()

        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0:
            return b""
        return self.rfile.read(length)

    def _read_json_body(self) -> tuple[dict, bytes]:
        raw = self._read_request_body()
        if not raw:
            return {}, raw
        try:
            return json.loads(raw.decode("utf-8")), raw
        except Exception:
            return {}, raw

    def _save_payload(self, name: str, payload: dict, raw_body: bytes) -> None:
        out = self.data_dir / f"{name}.json"
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(
            json.dumps(
                {
                    "savedAtUtc": _utc_now_text(),
                    "path": self.path,
                    "method": self.command,
                    "headers": {key: value for key, value in self.headers.items()},
                    "rawBodyLength": len(raw_body),
                    "rawBodyPreview": raw_body[:4096].decode("utf-8", errors="replace"),
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
        print(
            f"[pdc-mock] {self.command} {self.path} "
            f"content-length={self.headers.get('Content-Length', '')} "
            f"transfer-encoding={self.headers.get('Transfer-Encoding', '')} "
            f"expect={self.headers.get('Expect', '')}"
        )

        if not self._check_token():
            self._write_json(401, {"error": "unauthorized"})
            return

        payload, raw_body = self._read_json_body()

        if self.path == "/api/v1/fileMaps/diff":
            items = payload.get("items") if isinstance(payload, dict) else {}
            keys = sorted(items.keys()) if isinstance(items, dict) else []
            self._save_payload("filemaps-diff-request", payload, raw_body)
            # CI fallback mode: return empty diff to avoid long object uploads
            # against a local mock endpoint. Real PDC endpoint will return
            # actual missing object hashes.
            result = {
                "success": True,
                "code": 0,
                "message": "ok",
                "content": [],
                "Content": [],
                "requestedCount": len(keys),
            }
            self._write_json(200, result)
            return

        if self.path == "/api/v1/fileMaps/upload":
            self._save_payload("filemaps-upload-request", payload, raw_body)
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
            self._save_payload("distribution-request", payload, raw_body)
            result = {
                "success": True,
                "code": 0,
                "message": "ok",
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
