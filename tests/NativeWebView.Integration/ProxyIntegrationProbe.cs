using NativeWebView.Core;
using NativeWebView.Dialog;

namespace NativeWebView.Integration;

// Opt-in probe for externally instrumented proxy/origin fixtures. It never edits system settings.
internal static class ProxyIntegrationProbe
{
    internal static bool IsEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NATIVEWEBVIEW_PROXY_TARGET"));

    internal static NativeWebViewInstanceConfiguration CreateConfiguration(string owner)
    {
        var configuration = new NativeWebViewInstanceConfiguration();
        var mode = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_PROXY_MODE") ?? "system";
        configuration.EnvironmentOptions.Proxy = mode switch
        {
            "direct" => new() { NoProxy = true },
            "custom" => new() { Server = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_PROXY_SERVER")
                ?? throw new InvalidOperationException("Custom proxy probe requires NATIVEWEBVIEW_PROXY_SERVER.") },
            "system" => null,
            _ => throw new ArgumentException("Proxy probe mode must be system, direct, or custom."),
        };
        var profileRoot = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_PROXY_PROFILE")
            ?? Path.Combine(Path.GetTempPath(), "nativewebview-proxy-probe", Guid.NewGuid().ToString("N"));
        configuration.EnvironmentOptions.UserDataFolder = Path.Combine(profileRoot, owner);
        configuration.ControllerOptions.ProfileName = owner;
        return configuration;
    }

    internal static async Task<IntegrationScenarioResult> RunAsync(Controls.NativeWebView view, CancellationToken cancellationToken)
    {
        var result = new IntegrationScenarioResult { Name = "proxy-routing" };
        var target = new Uri(Environment.GetEnvironmentVariable("NATIVEWEBVIEW_PROXY_TARGET")!, UriKind.Absolute);
        var marker = Environment.GetEnvironmentVariable("NATIVEWEBVIEW_PROXY_EXPECT")
            ?? throw new InvalidOperationException("Proxy probe requires an expected response marker.");
        await view.InitializeAsync(cancellationToken);
        view.Navigate(target);
        await WaitForMarkerAsync(view.ExecuteScriptAsync, marker, cancellationToken);
        result.Evidence.Add("embedded-response-marker:" + marker);
        using var dialog = new NativeWebDialog();
        dialog.InstanceConfiguration = CreateConfiguration("dialog");
        dialog.Show();
        dialog.Navigate(target);
        if (dialog.Platform == NativeWebViewPlatform.MacOS)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            result.Evidence.Add("macos-dialog-navigation-dispatched:external-traffic-proof-required");
            result.Details = "Embedded response verified; macOS dialog script execution is unavailable. Correlate dialog traffic externally; this run alone is not qualified.";
            return result;
        }
        await WaitForMarkerAsync(dialog.ExecuteScriptAsync, marker, cancellationToken);
        result.Evidence.Add("dialog-response-marker:" + marker);
        result.Passed = true;
        result.Details = "Both native owners returned the expected marker. Correlate with external proxy/origin logs to qualify routing.";
        return result;
    }

    private static async Task WaitForMarkerAsync(Func<string, CancellationToken, Task<string?>> execute, string marker, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var value = await execute("document.body ? document.body.innerText : ''", timeout.Token);
            if (value?.Contains(marker, StringComparison.Ordinal) == true)
                return;
            await Task.Delay(100, timeout.Token);
        }
    }
}
