#!/usr/bin/env python3
"""Qualify production embedded/dialog routing with a temporary Windows user proxy.

Requires --change-user-proxy. Restores and verifies the exact starting values on
ordinary completion, exceptions and Ctrl+C. Run in an isolated test session.
"""
import argparse
import ctypes
import http.server
import json
import os
from pathlib import Path
import subprocess
import threading
import time
import uuid
import winreg


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app", required=True, help="Built integration desktop DLL")
    parser.add_argument("--output", required=True, help="Sanitized JSON result file")
    parser.add_argument("--change-user-proxy", action="store_true", required=True)
    args = parser.parse_args()
    observations = []
    current_case = "setup"

    class Logger(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_GET(self):
            if self.path.startswith("http://example.com/?nwv="):
                observations.append({"case": current_case, "method": "GET", "target": self.path})
            payload = b"<html><body>NWV_PROXY</body></html>"
            self.send_response(200)
            self.send_header("Content-Length", str(len(payload)))
            self.send_header("Content-Type", "text/html")
            self.end_headers()
            self.wfile.write(payload)

        def do_CONNECT(self):
            if self.path == "example.com:443":
                observations.append({"case": current_case, "method": "CONNECT", "target": self.path})
            self.send_error(502, "Instrumented proxy rejection")

    names = ["ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL", "AutoDetect"]
    key = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r"Software\Microsoft\Windows\CurrentVersion\Internet Settings",
                         0, winreg.KEY_QUERY_VALUE | winreg.KEY_SET_VALUE)

    def read(name):
        try:
            return winreg.QueryValueEx(key, name)
        except FileNotFoundError:
            return None

    starting = {name: read(name) for name in names}

    def notify():
        wininet = ctypes.WinDLL("wininet", use_last_error=True)
        for option in [39, 37]:
            if not wininet.InternetSetOptionW(None, option, None, 0):
                raise ctypes.WinError(ctypes.get_last_error())

    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Logger)
    worker = threading.Thread(target=server.serve_forever, daemon=True)
    worker.start()
    results = []
    restored = False
    try:
        for name in ["AutoConfigURL"]:
            try:
                winreg.DeleteValue(key, name)
            except FileNotFoundError:
                pass
        winreg.SetValueEx(key, "AutoDetect", 0, winreg.REG_DWORD, 0)
        winreg.SetValueEx(key, "ProxyOverride", 0, winreg.REG_SZ, "")
        endpoint = "127.0.0.1:%d" % server.server_port
        winreg.SetValueEx(key, "ProxyServer", 0, winreg.REG_SZ, "http=" + endpoint + ";https=" + endpoint)
        for scheme in ["http", "https"]:
            for case, mode, enabled in [("A", "system", 0), ("B", "system", 1), ("C", "direct", 1)]:
                current_case = case + "-" + scheme
                winreg.SetValueEx(key, "ProxyEnable", 0, winreg.REG_DWORD, enabled)
                notify()
                env = os.environ.copy()
                env.update(NATIVEWEBVIEW_PROXY_TARGET=scheme + "://example.com/?nwv=" + uuid.uuid4().hex,
                           NATIVEWEBVIEW_PROXY_MODE=mode,
                           NATIVEWEBVIEW_PROXY_EXPECT="NWV_PROXY" if case == "B" and scheme == "http" else "Example Domain")
                env.pop("NATIVEWEBVIEW_PROXY_PROFILE", None)
                start = time.monotonic()
                process = subprocess.Popen(["dotnet", str(Path(args.app).resolve())], env=env,
                                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                try:
                    stdout, _ = process.communicate(timeout=75)
                finally:
                    if process.poll() is None:
                        process.kill()
                        process.communicate()
                hits = [entry for entry in observations if entry["case"] == current_case]
                passed = (bool(hits) if case == "B" else process.returncode == 0 and not hits)
                if case == "B" and scheme == "http":
                    passed = passed and process.returncode == 0
                result = {"case": current_case, "passed": passed, "exitCode": process.returncode,
                          "systemHits": len(hits), "seconds": round(time.monotonic() - start, 3),
                          "appResult": next((line for line in stdout.splitlines() if line.startswith("NATIVEWEBVIEW_INTEGRATION_RESULT:")), None)}
                results.append(result)
                print(json.dumps(result), flush=True)
    finally:
        try:
            for name, value in starting.items():
                if value is None:
                    try:
                        winreg.DeleteValue(key, name)
                    except FileNotFoundError:
                        pass
                else:
                    winreg.SetValueEx(key, name, 0, value[1], value[0])
            notify()
            restored = all(read(name) == value for name, value in starting.items())
        finally:
            winreg.CloseKey(key)
            server.shutdown()
            server.server_close()
            Path(args.output).parent.mkdir(parents=True, exist_ok=True)
            Path(args.output).write_text(json.dumps({"results": results, "observations": observations,
                                                   "settingsRestored": restored}, indent=2), encoding="utf-8")
            print(json.dumps({"settingsRestored": restored}), flush=True)
    return 0 if restored and len(results) == 6 and all(row["passed"] for row in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
