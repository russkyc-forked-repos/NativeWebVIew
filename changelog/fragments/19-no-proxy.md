[Added] Added NoProxy configuration for Windows, Linux and macOS embedded views and dialogs, including authenticated loopback SOCKS5 forwarding on macOS.
[Fixed] Prevented Windows environment-option fallback from silently discarding explicit proxy policy and disabled proxy discovery for supported managed requests in Direct mode.
[Fixed] Preserved WebView2 SDK compatibility-version defaults when creating environments with explicit proxy options.
[Fixed] Applied finalized macOS proxy callbacks before native attachment and scoped persistent Direct locks to the application's native storage scope.
[Fixed] Prevented shared presenters from resetting initialization options and stopped macOS native host creation when initialization callbacks dispose the presenter or instance.
[Docs] Documented Direct profile isolation, runtime requirements and outstanding native qualification gates; macOS 14 release support requires runtime validation.
