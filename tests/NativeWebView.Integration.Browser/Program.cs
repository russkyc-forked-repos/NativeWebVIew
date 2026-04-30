using Avalonia;
using Avalonia.Browser;
using NativeWebView.Integration;
using NativeWebView.Platform.Browser;

namespace NativeWebView.Integration.Browser;

internal static class Program
{
    private static Task Main(string[] args)
    {
        NativeWebViewPlatformBrowserModule.RegisterDefault();
        IntegrationPlatformContext.BrowserEntryUrl = args.FirstOrDefault();
        IntegrationPlatformContext.ExternalLogger = BrowserConsoleBridge.Publish;
        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>();
    }
}
