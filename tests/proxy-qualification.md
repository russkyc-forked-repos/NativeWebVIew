# NoProxy native qualification

The managed test suite checks proxy resolution, unsupported targets, argument merging, fallback policy, SOCKS5 protocol behavior, long-lived tunnels, and registry ownership/rollback. It also injects accept failures, attempts competing socket binds while failed owners remain alive, and floods the bounded unauthenticated pool while checking an established tunnel. Fake native stores in registry tests do not establish cookie continuity or WKWebView routing.

Run `dotnet test tests/NativeWebView.Core.Tests/NativeWebView.Core.Tests.csproj`. The long-lived tunnel check deliberately runs for more than 60 seconds.

## Production-control probe

Build `tests/NativeWebView.Integration.Desktop`. With a GUI session, set these environment variables and launch that executable:

| Variable | Meaning |
| --- | --- |
| `NATIVEWEBVIEW_PROXY_TARGET` | Absolute HTTP/HTTPS URL, with a unique case nonce. Enables the focused probe. |
| `NATIVEWEBVIEW_PROXY_MODE` | `system`, `direct`, or `custom`. |
| `NATIVEWEBVIEW_PROXY_SERVER` | Explicit proxy URI for `custom`. |
| `NATIVEWEBVIEW_PROXY_EXPECT` | Distinct marker expected in the response body. |
| `NATIVEWEBVIEW_PROXY_PROFILE` | Optional fixed profile root for persistence checks; otherwise a fresh temporary root is used. |

On Windows, `python scripts/validate-windows-no-proxy.py --app <built-integration-DLL> --output artifacts/no-proxy/windows-routing.json --change-user-proxy` runs the controlled manual proxy matrix, restoring and verifying the captured user-proxy values afterward. It targets unique `example.com` URLs and logs only matching fixture traffic; other applications can generate unrelated requests while a system proxy is enabled.

For manually coordinated runs, `python scripts/proxy-fixture.py --output <sanitized-log.jsonl>` provides HTTP/CONNECT and rejecting SOCKS5 loggers plus a `/proxy.pac` fixture. Its ready record includes both ports; it logs only `example.com` by default (`--target-host` selects a controlled origin). It does not modify system settings.

The probe uses the actual embedded and dialog implementations. Its response assertions alone are insufficient to qualify routing: correlate them with proxy/origin logs. macOS dialog script execution is currently a placeholder in the repository; its probe records dispatch and requires external traffic evidence instead of claiming a script assertion passed.

## Required matrix

Use distinct instrumented system proxy and origin listeners. Before modifying host settings, snapshot their current state, restore that exact snapshot afterward, and verify restoration. Avoid managed settings and never restore stale snapshots from earlier experiments. Use real hostnames or public destinations; a LAN/loopback target is valid only when the System baseline actually reaches the proxy.

1. System proxies disabled: untouched HTTP and HTTPS reach the origin.
2. System proxies enabled: untouched HTTP and HTTPS demonstrably reach the logger. Repeat the baseline.
3. The same settings with Direct: origin traffic succeeds and no corresponding system-proxy traffic occurs. Repeat for embedded views and dialogs.
4. macOS forwarding startup failure, injected accept failure, refused outbound connection, and wrong credentials: bounded failure without system fallback for destinations requiring forwarding. Inject runtime failure through `DirectSocksForwarder.FailClosed()` or the accept fault seam in a test harness; do not substitute final `DisposeAsync()` while native owners remain alive.
5. Simultaneous System/Direct/Custom owners: independent routes. Shared persistent Direct embedded/dialog owners retain cookie state and service availability after one closes. Close all owners and reacquire; restart the app with a new listener and verify cookies persist. A second process using the same persistent Direct profile must be rejected.
6. Test redirects, scripts/images, fetch/XHR, ws/wss longer than 60 seconds, managed/native downloads and cancellation, and favicon retrieval with the instrumented system proxy enabled.
7. On macOS, set Direct through `CoreWebView2EnvironmentRequested` and verify both explicit initialization and attachment-triggered initialization. Run two applications with distinct bundle identifiers using default Direct profiles concurrently; both must work, while a second process in the same application storage scope must be rejected. Repeat in sandboxed deployment where supported.
8. With production embedded/dialog owners sharing a Direct context, inject accept failure and keep the views alive. From another process, attempt to bind the same loopback port (including with address reuse enabled); binding/listening must fail. Reload both views and verify rejection with no corresponding system-proxy/origin hits for traffic requiring the helper. Closing one owner must leave the port reserved. Verify the remaining native operations are canceled before final-owner disposal releases the port. Reacquisition must use fresh credentials and a newly configured native endpoint. Repeat under sustained unauthenticated connection attempts; record existing WebSocket/tunnel continuity, handshake expiry, bounded occupancy, CPU/memory usage, and recovery once the flood stops. Do not claim availability for new requests during a continuous local flood.

For system SOCKS, configure a separate logging SOCKS service and repeat the System/Direct comparison. For PAC, serve a local script returning the instrumented proxy (`function FindProxyForURL(url, host) { return "PROXY <logger-host>:<port>"; }`); prove PAC retrieval and proxied baseline traffic before comparing Direct. WPAD additionally needs controlled discovery infrastructure; do not infer WPAD qualification from an explicit PAC URL.

Inject native cleanup failures separately from forwarder failures, including embedded view teardown, dialog teardown, and final data-store release. Verify the context stops forwarding and retains its endpoint after all leases are disposed. A competing process must remain unable to bind the endpoint or acquire the same persistent profile until the owning process exits. Verify a fresh process can acquire the profile after exit. Managed tests cover injected cleanup/store-release failures, shared owners, private stores, and normal cleanup; native qualification must establish the corresponding production behavior.

Record OS/WebKit/WebView2 versions, unique URLs, listener observations, response markers, failure results, restoration, and any unsupported categories. Store sanitized evidence only; exclude credentials and raw proxy snapshots.

## Release gates

Windows production manual HTTP/HTTPS routing was exercised on 2026-09-14 using the focused probe. HTTP System produced two matching origin requests at the logger; HTTPS System produced eleven matching rejected CONNECTs. Direct reached the origin in both the embedded view and dialog with zero matching system-proxy hits for HTTP and HTTPS. HTTPS rejection stopped that baseline at the embedded probe, so an independent rejecting HTTPS dialog baseline remains unqualified. The captured user-proxy values were restored and verified. Unrelated background traffic was excluded from the counts; sanitized results are generated at `artifacts/no-proxy/windows-routing.json`.

- macOS 14 native execution and production rerun on the available macOS 27 release remain required. The historical prototype was tested only on macOS 27.0 build 26A428.
- Broader Windows coverage, Linux native routing, macOS persistence/cleanup, system SOCKS/PAC/WPAD, sandbox/entitlement deployment, and advertised downloads require measured qualification.
- External applications, unqualified service workers, WebRTC and UDP/QUIC are outside advertised coverage. Local direct routing does not establish universal fail-closed behavior.

Do not treat a deployment target, a fake-store test, or historical prototype evidence as a native release qualification.
