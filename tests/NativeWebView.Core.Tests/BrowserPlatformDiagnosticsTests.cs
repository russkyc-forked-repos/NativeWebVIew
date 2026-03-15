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

        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Code == "browser.control.contract_only");
        Assert.Contains(diagnostics.Issues, issue => issue.Code == "browser.host.mismatch");
    }

    [Fact]
    public void Create_WhenBrowserHost_ReportsPopupDisabled()
    {
        var diagnostics = BrowserPlatformDiagnostics.Create(
            isBrowserHost: true,
            popupSupport: "false");

        Assert.DoesNotContain(diagnostics.Issues, issue => issue.Code == "browser.control.contract_only");
        Assert.Contains(diagnostics.Issues, issue => issue.Code == "browser.popup.disabled");
    }

    [Fact]
    public void Create_WhenBrowserHostAndPopupSupportEnabled_ReportsReady()
    {
        var diagnostics = BrowserPlatformDiagnostics.Create(
            isBrowserHost: true,
            popupSupport: "true");

        Assert.Contains(diagnostics.Issues, issue => issue.Code == "browser.ready");
    }
}
