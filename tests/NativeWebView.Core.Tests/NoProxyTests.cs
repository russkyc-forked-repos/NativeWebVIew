using System.Runtime.InteropServices;
using NativeWebView.Dialog;
using NativeWebView.Interop;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.macOS;
using NativeWebView.Controls;

namespace NativeWebView.Core.Tests;

public sealed class NoProxyTests
{
    [Theory]
    [InlineData("environment")]
    [InlineData("controller")]
    [InlineData("initialized")]
    public void MacOSAttachment_StopsWhenCallbackDisposesInstance(string stage)
    {
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend());
        using var presenter = new Controls.NativeWebView(instance);
        presenter.CoreWebView2EnvironmentRequested += (_, e) => e.Options.Proxy = new() { NoProxy = true };
        DisposeDuringInitialization(presenter, stage, instance.Dispose);

        Assert.Throws<ObjectDisposedException>(() => presenter.PrepareMacOSHostConfiguration());
        Assert.True(instance.IsDisposed);
        Assert.Null(instance.MacOSHost);
        Assert.Null(instance.NativeNavigationState);
    }

    [Theory]
    [InlineData("environment")]
    [InlineData("controller")]
    [InlineData("initialized")]
    public void MacOSAttachment_StopsForDisposedPresenterButPreservesSharedInstance(string stage)
    {
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend());
        using var presenter = new Controls.NativeWebView(instance);
        presenter.CoreWebView2EnvironmentRequested += (_, e) => e.Options.Proxy = new() { NoProxy = true };
        DisposeDuringInitialization(presenter, stage, presenter.Dispose);

        Assert.Throws<ObjectDisposedException>(() => presenter.PrepareMacOSHostConfiguration());
        Assert.False(instance.IsDisposed);
        Assert.Null(instance.MacOSHost);
        using var replacement = new Controls.NativeWebView(instance);
        Assert.True(replacement.PrepareMacOSHostConfiguration().EnvironmentOptions.Proxy!.NoProxy);
    }

    private static void DisposeDuringInitialization(Controls.NativeWebView presenter, string stage, Action dispose)
    {
        switch (stage)
        {
            case "environment": presenter.CoreWebView2EnvironmentRequested += (_, _) => dispose(); break;
            case "controller": presenter.CoreWebView2ControllerOptionsRequested += (_, _) => dispose(); break;
            case "initialized": presenter.CoreWebView2Initialized += (_, _) => dispose(); break;
            default: throw new ArgumentOutOfRangeException(nameof(stage));
        }
    }

    [Fact]
    public void SharedPresenters_DoNotResetEarlierProxyAndProfileCallbacks()
    {
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend());
        using var first = new Controls.NativeWebView(instance);
        using var second = new Controls.NativeWebView(instance);
        first.CoreWebView2EnvironmentRequested += (_, e) => e.Options.Proxy = new() { NoProxy = true };
        first.CoreWebView2ControllerOptionsRequested += (_, e) =>
        {
            e.Options.ProfileName = "first-profile";
            e.Options.IsInPrivateModeEnabled = true;
        };
        var host = first.PrepareMacOSHostConfiguration();
        Assert.True(host.EnvironmentOptions.Proxy!.NoProxy);
        Assert.Equal("first-profile", host.ControllerOptions.ProfileName);
        Assert.True(host.ControllerOptions.IsInPrivateModeEnabled);
        Assert.True(second.PrepareMacOSHostConfiguration().EnvironmentOptions.Proxy!.NoProxy);
    }

    [Fact]
    public void SharedPresenters_ValidateOnlyAfterAllOptionCallbacks()
    {
        var saved = new NativeWebViewInstanceConfiguration();
        saved.EnvironmentOptions.Proxy = new() { Server = "proxy:8080" };
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend(), saved);
        using var first = new Controls.NativeWebView(instance);
        using var second = new Controls.NativeWebView(instance);
        first.CoreWebView2EnvironmentRequested += (_, e) => e.Options.Proxy!.NoProxy = true;
        second.CoreWebView2EnvironmentRequested += (_, e) =>
        {
            Assert.True(e.Options.Proxy!.NoProxy);
            e.Options.Proxy.Server = null;
        };
        second.CoreWebView2ControllerOptionsRequested += (_, e) => e.Options.ProfileName = "last-profile";
        var host = first.PrepareMacOSHostConfiguration();
        Assert.True(host.EnvironmentOptions.Proxy!.NoProxy);
        Assert.Null(host.EnvironmentOptions.Proxy.Server);
        Assert.Equal("last-profile", host.ControllerOptions.ProfileName);
        Assert.Equal("proxy:8080", first.InstanceConfiguration.EnvironmentOptions.Proxy!.Server);
    }

    [Fact]
    public void SharedPresenters_SkipPresenterDisposedByEarlierCallback()
    {
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend());
        using var first = new Controls.NativeWebView(instance);
        using var second = new Controls.NativeWebView(instance);
        first.CoreWebView2EnvironmentRequested += (_, e) =>
        {
            e.Options.Proxy = new() { NoProxy = true };
            second.Dispose();
        };
        var disposedCallbackRan = false;
        second.CoreWebView2EnvironmentRequested += (_, e) =>
        {
            disposedCallbackRan = true;
            e.Options.Proxy = null;
        };
        Assert.True(first.PrepareMacOSHostConfiguration().EnvironmentOptions.Proxy!.NoProxy);
        Assert.False(disposedCallbackRan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MacOSAttachment_FinalizesProxyAndProfileCallbacksBeforeHostCreation(bool direct)
    {
        var saved = new NativeWebViewInstanceConfiguration();
        saved.EnvironmentOptions.Proxy = new() { NoProxy = !direct };
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend(), saved);
        NativeWebViewEnvironmentOptions? eventOptions = null;
        var calls = 0;
        using (var presenter = new Controls.NativeWebView(instance))
        {
            presenter.CoreWebView2EnvironmentRequested += (_, e) =>
            {
                calls++;
                eventOptions = e.Options;
                e.Options.Proxy = new() { NoProxy = direct };
                e.Options.UserDataFolder = "callback-storage";
            };
            presenter.CoreWebView2ControllerOptionsRequested += (_, e) =>
            {
                e.Options.ProfileName = "callback-profile";
                e.Options.IsInPrivateModeEnabled = true;
            };
            var host = instance.PrepareMacOSHostConfiguration();
            Assert.Equal(direct, host.EnvironmentOptions.Proxy!.NoProxy);
            Assert.Equal("callback-storage", host.EnvironmentOptions.UserDataFolder);
            Assert.Equal("callback-profile", host.ControllerOptions.ProfileName);
            Assert.True(host.ControllerOptions.IsInPrivateModeEnabled);
            Assert.Equal(!direct, presenter.InstanceConfiguration.EnvironmentOptions.Proxy!.NoProxy);
            eventOptions!.Proxy!.NoProxy = !direct;
            host.EnvironmentOptions.Proxy.NoProxy = !direct;
        }
        using var replacement = new Controls.NativeWebView(instance);
        Assert.Equal(direct, instance.PrepareMacOSHostConfiguration().EnvironmentOptions.Proxy!.NoProxy);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MacOSExplicitInitialization_PreservesFinalizedDirectPolicy()
    {
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend());
        using var presenter = new Controls.NativeWebView(instance);
        presenter.CoreWebView2EnvironmentRequested += (_, e) => e.Options.Proxy = new() { NoProxy = true };
        await presenter.InitializeAsync();
        Assert.True(instance.PrepareMacOSHostConfiguration().EnvironmentOptions.Proxy!.NoProxy);
    }

    [Theory]
    [InlineData("proxy:8080", null, null)]
    [InlineData(null, "https://proxy/pac", null)]
    [InlineData(null, null, "*")]
    public void MacOSAttachment_RejectsConflictingCallbackPolicy(string? server, string? pac, string? bypass)
    {
        using var instance = new NativeWebViewInstance(new MacOSNativeWebViewBackend());
        using var presenter = new Controls.NativeWebView(instance);
        presenter.CoreWebView2EnvironmentRequested += (_, e) => e.Options.Proxy = new()
        {
            NoProxy = true, Server = server, AutoConfigUrl = pac, BypassList = bypass,
        };
        Assert.Throws<ArgumentException>(() => instance.PrepareMacOSHostConfiguration());
        Assert.False(instance.IsInitialized);
    }

    [Fact]
    public void DirectEnvironment_PreservesSdkCompatibilityVersion()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var factory = typeof(WindowsNativeWebViewBackend).GetMethod("CreateRuntimeEnvironmentOptions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var options = factory.Invoke(null, new object[] { new NativeWebViewEnvironmentOptions { Proxy = new() { NoProxy = true } } })!;
        Assert.False(string.IsNullOrWhiteSpace((string?)options.GetType().GetProperty("TargetCompatibleBrowserVersion")!.GetValue(options)));
        Assert.Equal("--no-proxy-server", options.GetType().GetProperty("AdditionalBrowserArguments")!.GetValue(options));
    }

    [Fact]
    public void Direct_ResolvesAndSurvivesConfigurationCopies()
    {
        var configuration = new NativeWebViewInstanceConfiguration();
        configuration.EnvironmentOptions.Proxy = new() { NoProxy = true };
        var clone = configuration.Clone();
        var applied = new NativeWebViewProxyOptions();
        clone.EnvironmentOptions.Proxy!.ApplyTo(applied);
        Assert.True(applied.NoProxy);
        Assert.Equal(NativeWebViewProxyKind.Direct, NativeWebViewProxyConfigurationResolver.Resolve(applied)!.Kind);
        Assert.Null(NativeWebViewProxyConfigurationResolver.Resolve(new()));
        Assert.Null(NativeWebViewProxyConfigurationResolver.Resolve(new() { BypassList = "*" }));
    }

    [Theory]
    [InlineData("http://proxy:8080", null, null)]
    [InlineData(null, "https://proxy/pac", null)]
    [InlineData(null, null, "*")]
    public void Direct_RejectsContradictoryOptions(string? server, string? pac, string? bypass)
    {
        Assert.Throws<ArgumentException>(() => NativeWebViewProxyConfigurationResolver.Resolve(
            new() { NoProxy = true, Server = server, AutoConfigUrl = pac, BypassList = bypass }));
    }

    [Fact]
    public void Direct_MapsToNativeDesktopSettings()
    {
        var options = new NativeWebViewProxyOptions { NoProxy = true };
        var arguments = NativeWebViewWindowsProxyArgumentsBuilder.Merge(
            "--disable-gpu --proxy-server=old:80 --proxy-bypass-list=localhost --proxy-pac-url=https://old/pac --proxy-auto-detect --no-proxy-server", options);
        Assert.Equal("--disable-gpu --no-proxy-server", arguments);
        Assert.DoesNotContain("--no-proxy-server", NativeWebViewWindowsProxyArgumentsBuilder.Merge(arguments, new() { Server = "proxy:8080" })!);
        var linux = NativeWebViewLinuxProxySettingsBuilder.Build(options)!;
        Assert.True(linux.NoProxy);
        Assert.Empty(linux.DefaultProxyUri);
        Assert.Empty(linux.IgnoreHosts);
    }

    [Theory]
    [InlineData("--no-proxy-server")]
    [InlineData("--proxy-server=host:80")]
    [InlineData("--proxy-bypass-list=localhost")]
    [InlineData("--proxy-pac-url=https://host/pac")]
    [InlineData("--proxy-auto-detect")]
    public void ExplicitProxyPolicy_NeverRetriesWithoutOptions(string arguments)
    {
        var error = new COMException("invalid options", unchecked((int)0x80070057));
        Assert.False(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(new() { AdditionalBrowserArguments = arguments }, error));
        Assert.False(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(new() { Proxy = new() { NoProxy = true } }, error));
        Assert.False(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(new() { Proxy = new() { Server = "host:80" } }, error));
        Assert.True(WindowsNativeWebViewBackend.ShouldRetryEnvironmentCreationWithoutOptions(new() { Language = "en-US" }, error));
    }

    [Theory]
    [InlineData(NativeWebViewPlatform.IOS)]
    [InlineData(NativeWebViewPlatform.Android)]
    [InlineData(NativeWebViewPlatform.Browser)]
    public void UnsupportedPlatforms_RejectDirect(NativeWebViewPlatform platform)
    {
        Assert.False(NativeWebViewProxyPlatformSupportMatrix.Get(platform).SupportsNoProxy);
        Assert.Throws<NotSupportedException>(() => NativeWebViewProxyPlatformSupportMatrix.ValidateNoProxy(platform, new() { NoProxy = true }));
    }

    [Fact]
    public void Dialog_ForwardsDirectBeforeShow()
    {
        using var backend = new DialogBackend();
        using var dialog = new NativeWebDialog(backend);
        dialog.InstanceConfiguration.EnvironmentOptions.Proxy = new() { NoProxy = true };
        dialog.Show();
        Assert.True(backend.Options!.NoProxy);
    }

    private sealed class DialogBackend() : NativeWebDialogBackendStubBase(NativeWebViewPlatform.Windows,
        new WebViewPlatformFeatures(NativeWebViewPlatform.Windows, NativeWebViewFeature.Dialog)), INativeWebViewInstanceConfigurationTarget
    {
        internal NativeWebViewProxyOptions? Options;
        public void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration configuration) => Options = configuration.EnvironmentOptions.Proxy?.Clone();
    }
}
