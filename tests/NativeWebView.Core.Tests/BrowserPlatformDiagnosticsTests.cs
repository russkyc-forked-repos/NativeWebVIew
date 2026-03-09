using NativeWebView.Core;
using NativeWebView.Platform.Browser;

namespace NativeWebView.Core.Tests;

public sealed class BrowserPlatformDiagnosticsTests
{
    [Fact]
    public void Create_WhenHostMismatch_IgnoresPopupEnvironmentFlags()
    {
        var diagnostics = BrowserPlatformDiagnostics.Create(
            isBrowserHost: false,
            popupSupport: "false");

        var issue = Assert.Single(diagnostics.Issues);
        Assert.Equal("browser.host.mismatch", issue.Code);
    }

    [Fact]
    public void Create_WhenBrowserHost_ReportsPopupDisabled()
    {
        var diagnostics = BrowserPlatformDiagnostics.Create(
            isBrowserHost: true,
            popupSupport: "false");

        var issue = Assert.Single(diagnostics.Issues);
        Assert.Equal("browser.popup.disabled", issue.Code);
    }
}
