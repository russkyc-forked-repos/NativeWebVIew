---
title: Direct connections (NoProxy)
---

# Direct connections (NoProxy)

Set `NativeWebViewProxyOptions.NoProxy` before initialization to request direct connections for supported web traffic. The default is `false`, preserving existing system/default and explicit-proxy behavior.

```csharp
using NativeWebView.Core;
using NativeWebView.Dialog;

var configuration = new NativeWebViewInstanceConfiguration();
configuration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
{
    NoProxy = true,
};

var webView = new NativeWebView.Controls.NativeWebView(configuration);

// Configure a dialog before Show(). Dispose it when its lifetime ends.
using var dialog = new NativeWebDialog();
dialog.InstanceConfiguration = configuration.Clone();
dialog.Show();
```

`NoProxy = true` cannot be combined with a nonblank `Server`, `AutoConfigUrl`, or `BypassList`; contradictory settings throw `ArgumentException`. A bypass list by itself continues to have its previous behavior and is not a substitute for Direct mode. Configuration is applied during initialization, not changed underneath a running view.

For embedded macOS views, `CoreWebView2EnvironmentRequested` can also set `e.Options.Proxy.NoProxy` during initialization. Register handlers before native attachment or explicit initialization. The finalized environment and profile options are captured before WKWebView creation and retained when the presenter is replaced.

`NativeWebViewProxyPlatformSupportMatrix.Get(platform).SupportsNoProxy` describes the implementation capability, subject to minimum OS and runtime availability. It is not a certification of every OS release or network configuration.

| Platform | Implementation |
| --- | --- |
| Windows | WebView2 `--no-proxy-server`; explicit proxy policy prevents initialization fallback without environment options. |
| Linux | WebKitGTK `NoProxy` with a null settings pointer on the supported X11 runtime path. |
| macOS 14+ | Authenticated app-owned loopback SOCKS5 forwarding with direct outbound sockets and native failover disabled. Minimum-version runtime qualification remains pending. |
| iOS, Android, Browser | `NoProxy` is rejected as unsupported. |

On Windows, concurrent WebView2 environments with different proxy policies require separate `UserDataFolder` values. The library does not change that folder automatically or discard proxy options to work around an incompatible shared environment.

## macOS profiles and lifetime

System, Direct, and Custom routes use separate data-store identities. Changing an existing profile to Direct therefore uses a separate cookie/storage profile and can require signing in again. Direct cookies persist across restarts; generated listener ports and credentials never participate in persistent identity. Existing System and Custom identities are unchanged.

Embedded views and dialogs with matching persistent Direct configuration share one store and forwarding service. Private stores are independent. Closing one owner does not stop another owner's service. The final owner cancels its operations, releases native resources, and closes the service. The same persistent Direct profile cannot be acquired concurrently by another process in the same application storage scope. Unrelated applications can use their own default Direct profiles concurrently.

The helper binds exclusively to loopback and requires generated per-service credentials. It has five-second handshake/connect deadlines, a 128-connection limit per context, at most 16 pending unauthenticated handshakes within that total, bounded relay buffers, and no fixed lifetime for established connections. Excess connections are rejected. TLS remains end to end and platform certificate validation is unchanged. Credentials remain in memory and are not exported in diagnostics.

A startup failure fails initialization. A failed forwarding service cancels its tunnels and retains the original bound listening socket until the final native owner releases the context. It rejects new connections when accepting is possible; persistent accept errors retry with a delay on the same socket. It never resumes forwarding, rebinds the endpoint, or clears the native override to recover through the system proxy. Retaining the port prevents another local process from taking over the endpoint while owners still reference it. Dispose all owners of a failed context before recreating it. Desktop managed favicon retrieval and macOS fallback downloads also disable proxy discovery in Direct mode.

Loopback is not private to an application. The handshake limit bounds unauthenticated occupancy and leaves established tunnels running during connection floods, but cannot guarantee new connections will succeed under a sustained local attack. SOCKS username/password authentication does not authenticate the server and sends credentials in cleartext over loopback. This design does not protect against an attacker able to inspect process memory or capture loopback traffic. Authenticated clients can request any TCP destination reachable by the application; HTTPS should be preferred where device support permits it.

If native cleanup fails, the affected Direct context is disabled and retained until process exit, including its bound endpoint and persistent profile lock. Closing the remaining owners does not release this reservation. Restart the application to use that persistent profile again. This deliberate resource retention prevents endpoint takeover while native objects may still reference the route; uncertain native releases are not retried.

## Traffic scope and qualification

Direct controls configured application/system proxy selection for supported traffic. It does not override DNS configuration, VPN routes, firewalls, or network extensions. WKWebView can connect directly to local destinations without consulting the SOCKS listener; this is not an assertion that every connection traverses the helper or fails whenever the helper stops.

The prototype qualified public HTTP/HTTPS navigation against instrumented manual system HTTP/HTTPS proxies on macOS 27.0 build 26A428, arm64. It also exercised authentication, shared nonpersistent stores, and forwarding failures. Supplementary fetch/XHR and WebSocket coverage was run separately with system proxies disabled. Prototype results do not qualify the production integration.

Before advertising a macOS 14+ release, execute the production routing matrix on macOS 14 and the available current release, including persistent cookies, shared embedded/dialog owners, rollback, and disposal. System SOCKS and PAC/WPAD require separate qualification before general system-proxy bypass claims. Downloads, service workers, external application handoff, WebRTC, UDP/QUIC, and public IPv6 routing must not be assumed covered by the TCP prototype.

The native qualification procedure and outstanding checks are in `tests/proxy-qualification.md` in the repository.
