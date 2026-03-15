---
title: "Windows"
---

# Windows

## Backend

- Package: `NativeWebView.Platform.Windows`
- Platform enum: `NativeWebViewPlatform.Windows`
- Native engine: Microsoft Edge WebView2

## Current Repo Implementation Status

- `NativeWebView`: implemented. The control now creates a real child HWND and WebView2 controller when hosted through the Avalonia `NativeWebView` control.
- `NativeWebDialog`: contract-only.
- `WebAuthenticationBroker`: contract-only.
- Check `NativeWebViewPlatformImplementationStatusMatrix.Get(NativeWebViewPlatform.Windows)` in code when you need the honest current repo status.

## Platform Engine Capability

- Embedded view
- GPU surface rendering
- Offscreen rendering
- Dialog
- Authentication broker
- DevTools, context menu, status bar, zoom
- Printing and print UI
- New window and resource request interception
- Environment and controller options
- Native handles
- Cookie manager and command manager

## Registration

```csharp
factory.UseNativeWebViewWindows();
```

## Diagnostics Notes

Use `NATIVEWEBVIEW_WEBVIEW2_RUNTIME_PATH` when you need an explicit runtime path override. If it is set, the path must exist.

## Proxy Notes

- WebView2 can be configured via environment options and Chromium proxy arguments.
- `NativeWebViewWindowsProxyArgumentsBuilder` converts shared proxy options into `AdditionalBrowserArguments` payloads for WebView2-style integrations.
- The embedded Windows `NativeWebView` control applies per-instance proxy settings by merging them into WebView2 `AdditionalBrowserArguments`.
