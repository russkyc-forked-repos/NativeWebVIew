using NativeWebView.Core;
using Microsoft.Web.WebView2.Core;

namespace NativeWebView.Platform.Windows;

internal static class WindowsPlatformDiagnostics
{
    private static readonly Uri RuntimeInstallerUri = new("https://go.microsoft.com/fwlink/p/?LinkId=2124703");

    public static NativeWebViewPlatformDiagnostics Create()
        => Create(static () => CoreWebView2Environment.GetAvailableBrowserVersionString());

    internal static NativeWebViewPlatformDiagnostics Create(Func<string?> runtimeVersionProvider)
    {
        ArgumentNullException.ThrowIfNull(runtimeVersionProvider);
        var issues = new List<NativeWebViewDiagnosticIssue>();
        AddContractOnlyControlWarning(issues);

        if (!NativeWebViewDiagnosticsHostPlatformOverride.IsEffectiveHostPlatform(NativeWebViewPlatform.Windows))
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "windows.host.mismatch",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "Windows backend diagnostics are running on a non-Windows host.",
                recommendation: "Run this diagnostic on Windows to validate native runtime requirements."));
        }
        else
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            {
                issues.Add(new NativeWebViewDiagnosticIssue(
                    code: "windows.os.version",
                    severity: NativeWebViewDiagnosticSeverity.Error,
                    message: "Windows 10 or newer is required.",
                    recommendation: "Upgrade the host OS to Windows 10+."));
            }

            else if (!IsRuntimeAvailable(runtimeVersionProvider))
            {
                issues.Add(new NativeWebViewDiagnosticIssue(
                    code: "windows.runtime.unavailable",
                    severity: NativeWebViewDiagnosticSeverity.Error,
                    message: "The Windows web runtime is not installed or cannot be loaded.",
                    recommendation: "Install or repair the Windows web runtime.",
                    remediation: new NativeWebViewDiagnosticRemediation(
                        NativeWebViewDiagnosticRemediationKind.InstallRuntime,
                        RuntimeInstallerUri)));
            }

            var runtimePath = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_WEBVIEW2_RUNTIME_PATH");
            if (!string.IsNullOrWhiteSpace(runtimePath) && !Directory.Exists(runtimePath))
            {
                issues.Add(new NativeWebViewDiagnosticIssue(
                    code: "windows.webview2.path",
                    severity: NativeWebViewDiagnosticSeverity.Error,
                    message: $"NATIVEWEBVIEW_WEBVIEW2_RUNTIME_PATH does not exist: {runtimePath}",
                    recommendation: "Fix the path or unset the override environment variable."));
            }
        }

        if (issues.Count == 0)
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "windows.ready",
                severity: NativeWebViewDiagnosticSeverity.Info,
                message: "Windows prerequisite checks passed."));
        }

        return new NativeWebViewPlatformDiagnostics(
            NativeWebViewPlatform.Windows,
            providerName: nameof(WindowsPlatformDiagnostics),
            issues);
    }

    private static bool IsRuntimeAvailable(Func<string?> runtimeVersionProvider)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(runtimeVersionProvider());
        }
        catch
        {
            return false;
        }
    }

    private static void AddContractOnlyControlWarning(List<NativeWebViewDiagnosticIssue> issues)
    {
        var implementationStatus = NativeWebViewPlatformImplementationStatusMatrix.Get(NativeWebViewPlatform.Windows);
        if (implementationStatus.EmbeddedControl != NativeWebViewRepositoryImplementationStatus.RuntimeImplemented)
        {
            issues.Add(new NativeWebViewDiagnosticIssue(
                code: "windows.control.contract_only",
                severity: NativeWebViewDiagnosticSeverity.Warning,
                message: "Windows currently registers the NativeWebView control contract, but the embedded control runtime is not implemented in this repo yet.",
                recommendation: "Treat Windows embedded control support as planned work and check NativeWebViewPlatformImplementationStatusMatrix before shipping."));
        }
    }
}
