using System.Runtime.CompilerServices;

namespace NativeWebView.Platform.Browser;

internal static class BrowserPlatformModuleInitializer
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        NativeWebViewPlatformBrowserModule.RegisterDefault();
    }
}
