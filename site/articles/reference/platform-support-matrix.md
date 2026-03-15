---
title: "Platform Support Matrix"
---

# Platform Support Matrix

## Current Repo Runtime Status

| Platform | `NativeWebView` control | `NativeWebDialog` | `WebAuthenticationBroker` | Per-instance proxy |
| --- | --- | --- | --- | --- |
| Windows | Implemented | Contract-only | Contract-only | Implemented |
| macOS | Implemented | Implemented | Contract-only | Implemented on macOS 14+ |
| Linux | Implemented | Contract-only | Contract-only | Implemented |
| iOS | Implemented when built with the .NET 8 Apple workload | Unsupported | Contract-only | Implemented on iOS 17+ when built with the .NET 8 Apple workload |
| Android | Implemented when built with the .NET 8 Android workload | Unsupported | Contract-only | Contract-only, app-wide platform API only |
| Browser | Implemented | Unsupported | Contract-only | Unsupported |

Use `NativeWebViewPlatformImplementationStatusMatrix.Get(platform)` to inspect the current repo status in code. Use `NativeWebViewProxyPlatformSupportMatrix.Get(platform)` for proxy-specific status.

## Capability Contract Notes

- Registered backend modules and `Features` continue to describe the broader platform capability contract for that engine family.
- Current repo runtime status is intentionally tracked separately so docs and applications can distinguish stubbed contracts from implemented native host paths.
- Today, Windows, macOS, Linux, iOS, Android, and Browser are the platforms with real embedded `NativeWebView` control hosts in this repository. The iOS and Android runtime paths are built from their platform-targeted backend assemblies rather than the default `net8.0` contract build; the Browser runtime is built from the browser-targeted backend assembly and hosts an `iframe` through Avalonia Browser native control hosting.
