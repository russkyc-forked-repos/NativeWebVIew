using System.Collections.Generic;
using NativeWebView.Core;
using NativeWebView.Platform.Android;
using NativeWebView.Platform.Browser;
using NativeWebView.Platform.iOS;

namespace NativeWebView.Core.Tests;

public sealed class ZoomSupportTests
{
    [Fact]
    public void ProgrammaticZoomChanges_AreReportedOnMobileAndBrowserBackends()
    {
        INativeWebViewBackend[] backends =
        [
            new AndroidNativeWebViewBackend(),
            new IOSNativeWebViewBackend(),
            new BrowserNativeWebViewBackend()
        ];

        foreach (var backend in backends)
        {
            using (backend)
            using (var controller = new NativeWebViewController(backend))
            {
                var changes = new List<double>();
                controller.ZoomFactorChanged += (_, args) => changes.Add(args.ZoomFactor);

                controller.SetZoomFactor(1.25);
                controller.SetZoomFactor(1.25);

                Assert.Equal([1.25], changes);
                Assert.False(backend.Features.Supports(NativeWebViewFeature.ZoomFactorChangeNotification));
            }
        }
    }
}
