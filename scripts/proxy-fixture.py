#!/usr/bin/env python3
"""Instrumented HTTP/CONNECT logger and PAC fixture. No host-setting changes.

Use --output to record sanitized request metadata. This is a rejecting logger,
not a forwarding proxy: HTTP returns NWV_PROXY and CONNECT returns 502.
"""
import argparse
import http.server
import json
import socket
import socketserver
import threading
from urllib.parse import urlsplit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--output")
    parser.add_argument("--target-host", default="example.com", help="Only log this experiment destination")
    args = parser.parse_args()
    lock = threading.Lock()

    def emit(data):
        line = json.dumps(data)
        with lock:
            print(line, flush=True)
            if args.output:
                with open(args.output, "a", encoding="utf-8") as file:
                    file.write(line + "\n")

    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_CONNECT(self):
            if self.path.rsplit(":", 1)[0] == args.target_host:
                emit({"event": "connect", "destination": self.path})
            self.send_error(502, "Instrumented proxy rejection")

        def do_GET(self):
            if self.path == "/proxy.pac":
                emit({"event": "pac"})
                payload = ('function FindProxyForURL(url, host) { return "PROXY 127.0.0.1:%d"; }' % self.server.server_port).encode()
                content_type = "application/x-ns-proxy-autoconfig"
            else:
                if urlsplit(self.path).hostname == args.target_host:
                    emit({"event": "http", "target": self.path})
                payload = b"<html><body>NWV_PROXY</body></html>"
                content_type = "text/html"
            self.send_response(200)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(payload)

    class SocksHandler(socketserver.BaseRequestHandler):
        def handle(self):
            self.request.settimeout(5)

            def read(count):
                result = b""
                while len(result) < count:
                    part = self.request.recv(count - len(result))
                    if not part:
                        raise ConnectionError("EOF")
                    result += part
                return result

            try:
                version, length = read(2)
                if version != 5 or 0 not in read(length):
                    self.request.sendall(bytes([5, 255]))
                    return
                self.request.sendall(bytes([5, 0]))
                version, command, reserved, kind = read(4)
                if kind == 3:
                    host = read(read(1)[0]).decode("ascii")
                elif kind in (1, 4):
                    host = socket.inet_ntop(socket.AF_INET if kind == 1 else socket.AF_INET6, read(4 if kind == 1 else 16))
                else:
                    return
                port = int.from_bytes(read(2), "big")
                if host == args.target_host:
                    emit({"event": "socks-connect", "destination": host, "port": port, "command": command})
                self.request.sendall(bytes([5, 5, 0, 1, 0, 0, 0, 0, 0, 0]))
            except (OSError, ConnectionError, UnicodeError):
                pass

    server = http.server.ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    socks = socketserver.ThreadingTCPServer(("127.0.0.1", 0), SocksHandler)
    socks.daemon_threads = True
    threading.Thread(target=socks.serve_forever, daemon=True).start()
    emit({"event": "ready", "port": server.server_port, "socksPort": socks.server_address[1]})
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        socks.shutdown()
        socks.server_close()


if __name__ == "__main__":
    main()
