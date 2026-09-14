using NativeWebView.Core;

namespace NativeWebView.Controls;

public sealed class NativeWebViewInstance : IDisposable
{
    internal const string ConstructionCleanupExceptionDataKey =
        "NativeWebView.InstanceConstructionCleanupException";

    private bool _isDisposed;
    private bool _isConfigurationCommitted;
    private NativeWebViewInstanceConfiguration? _configurationBeforeScriptMutation;

    public NativeWebViewInstance() : this(CreateDefaultBackend(), instanceConfiguration: null)
    {
    }

    public NativeWebViewInstance(NativeWebViewInstanceConfiguration? instanceConfiguration) : this(CreateDefaultBackend(), instanceConfiguration)
    {
    }

    public NativeWebViewInstance(INativeWebViewBackend backend, NativeWebViewInstanceConfiguration? instanceConfiguration = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        Controller = new NativeWebViewController(backend);
        InstanceConfiguration = new NativeWebViewInstanceConfiguration();
        try
        {
            ApplyInstanceConfigurationCore(instanceConfiguration ?? InstanceConfiguration, validateLifecycle: false);
            Controller.CoreWebView2EnvironmentRequested += OnEnvironmentOptionsRequested;
            Controller.CoreWebView2ControllerOptionsRequested += OnControllerOptionsRequested;
        }
        catch (Exception constructionException)
        {
            try
            {
                Controller.Dispose();
            }
            catch (Exception cleanupException)
            {
                constructionException.Data[ConstructionCleanupExceptionDataKey] = cleanupException;
            }

            throw;
        }
    }

    internal NativeWebViewController Controller { get; }

    internal NativeWebViewInstanceConfiguration InstanceConfiguration { get; private set; }

    internal event EventHandler<CoreWebViewEnvironmentRequestedEventArgs>? EnvironmentOptionsRequested;
    internal event EventHandler<CoreWebViewControllerOptionsRequestedEventArgs>? ControllerOptionsRequested;

    internal MacOSNativeWebViewHost? MacOSHost { get; set; }

    internal INativeNavigationState? NativeNavigationState { get; set; }

    private NativeWebViewEnvironmentOptions? _finalizedMacOSEnvironmentOptions;
    private NativeWebViewControllerOptions? _finalizedMacOSControllerOptions;

    internal NativeWebViewInstanceConfiguration PrepareMacOSHostConfiguration()
    {
        // The macOS backend finalizes options synchronously. Never create a native store
        // while option callbacks are still pending, or block the AppKit thread waiting for them.
        var initialization = Controller.InitializeAsync();
        if (!initialization.IsCompleted)
            throw new InvalidOperationException("Complete initialization before attaching the macOS native host.");
        initialization.GetAwaiter().GetResult();
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return GetMacOSHostConfiguration();
    }

    internal NativeWebViewInstanceConfiguration GetMacOSHostConfiguration()
    {
        var configuration = InstanceConfiguration.Clone();
        if (_finalizedMacOSEnvironmentOptions is { } environment)
            configuration.EnvironmentOptions = environment.Clone();
        if (_finalizedMacOSControllerOptions is { } controller)
            configuration.ControllerOptions = controller.Clone();
        return configuration;
    }

    private void OnEnvironmentOptionsRequested(object? sender, CoreWebViewEnvironmentRequestedEventArgs e)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        // Seed once for the shared instance, then let every live presenter's handlers
        // contribute before validating and retaining the final configuration.
        InstanceConfiguration.ApplyEnvironmentOptions(e.Options);
        EnvironmentOptionsRequested?.Invoke(sender, e);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (Platform == NativeWebViewPlatform.MacOS)
        {
            NativeWebViewProxyPlatformSupportMatrix.ValidateNoProxy(Platform, e.Options.Proxy);
            _ = NativeWebViewProxyConfigurationResolver.Resolve(e.Options.Proxy);
            _finalizedMacOSEnvironmentOptions = e.Options.Clone();
        }
    }

    private void OnControllerOptionsRequested(object? sender, CoreWebViewControllerOptionsRequestedEventArgs e)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        InstanceConfiguration.ApplyControllerOptions(e.Options);
        ControllerOptionsRequested?.Invoke(sender, e);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (Platform == NativeWebViewPlatform.MacOS)
        {
            _finalizedMacOSControllerOptions = e.Options.Clone();
            MacOSHost?.SetPageJavaScriptEnabled(e.Options.IsJavaScriptEnabled);
        }
    }

    internal long ActivePresenterId
    {
        get => Interlocked.Read(ref field);
        set => Interlocked.Exchange(ref field, value);
    }

    public bool IsDisposed => _isDisposed;

    public NativeWebViewPlatform Platform => Controller.Platform;

    public IWebViewPlatformFeatures Features => Controller.Features;

    public NativeWebComponentState LifecycleState => Controller.State;

    public Uri? CurrentUrl => NativeNavigationState is { } native ? native.CurrentUrl : Controller.CurrentUrl;

    internal bool CanGoBack => NativeNavigationState?.CanGoBack ?? Controller.CanGoBack;

    internal bool CanGoForward => NativeNavigationState?.CanGoForward ?? Controller.CanGoForward;

    public bool IsInitialized => Controller.IsInitialized;

    /// <summary>
    /// Applies a cloned instance configuration to the native backend before initialization or navigation begins.
    /// </summary>
    /// <param name="instanceConfiguration">The configuration to clone and apply.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when native host creation, initialization, or navigation has already started.
    /// </exception>
    public void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration instanceConfiguration)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(instanceConfiguration);
        ApplyInstanceConfigurationCore(instanceConfiguration, validateLifecycle: true);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Controller.CoreWebView2EnvironmentRequested -= OnEnvironmentOptionsRequested;
        Controller.CoreWebView2ControllerOptionsRequested -= OnControllerOptionsRequested;
        EnvironmentOptionsRequested = null;
        ControllerOptionsRequested = null;
        DetachConfigurationEvents(InstanceConfiguration);
        _configurationBeforeScriptMutation = null;
        AttachDisposedConfigurationGuard(InstanceConfiguration);
        MacOSHost?.Dispose();
        MacOSHost = null;
        NativeNavigationState = null;
        Controller.Dispose();
    }

    private static INativeWebViewBackend CreateDefaultBackend()
    {
        NativeWebViewRuntime.EnsureCurrentPlatformRegistered();
        NativeWebViewRuntime.Factory.TryCreateNativeWebViewBackend(NativeWebViewRuntime.CurrentPlatform, out var backend);
        return backend;
    }

    private void ApplyInstanceConfigurationCore(
        NativeWebViewInstanceConfiguration instanceConfiguration,
        bool validateLifecycle)
    {
        if (validateLifecycle)
            ValidateConfigurationCanChange();

        var clone = instanceConfiguration.Clone();
        NativeWebViewProxyPlatformSupportMatrix.ValidateNoProxy(Platform, clone.EnvironmentOptions.Proxy);
        ApplyConfigurationToBackend(clone, InstanceConfiguration);

        DetachConfigurationEvents(InstanceConfiguration);
        InstanceConfiguration = clone;
        AttachConfigurationEvents(InstanceConfiguration);
    }

    internal void CommitInstanceConfiguration()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _isConfigurationCommitted = true;
    }

    private void AttachConfigurationEvents(NativeWebViewInstanceConfiguration configuration)
    {
        configuration.DocumentStartScriptsChanging += OnDocumentStartScriptsChanging;
        configuration.DocumentStartScriptsChanged += OnDocumentStartScriptsChanged;
    }

    private void DetachConfigurationEvents(NativeWebViewInstanceConfiguration configuration)
    {
        configuration.DocumentStartScriptsChanging -= OnDocumentStartScriptsChanging;
        configuration.DocumentStartScriptsChanged -= OnDocumentStartScriptsChanged;
    }

    private static void AttachDisposedConfigurationGuard(NativeWebViewInstanceConfiguration configuration)
    {
        configuration.DocumentStartScriptsChanging += ThrowDisposedConfigurationMutation;
    }

    private static void ThrowDisposedConfigurationMutation()
    {
        throw new ObjectDisposedException(nameof(NativeWebViewInstance));
    }

    private void OnDocumentStartScriptsChanging()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ValidateConfigurationCanChange();
        _configurationBeforeScriptMutation = InstanceConfiguration.Clone();
    }

    private void OnDocumentStartScriptsChanged()
    {
        var previous = _configurationBeforeScriptMutation ?? InstanceConfiguration.Clone();
        try
        {
            ApplyConfigurationToBackend(InstanceConfiguration.Clone(), previous);
        }
        finally
        {
            _configurationBeforeScriptMutation = null;
        }
    }

    private void ApplyConfigurationToBackend(
        NativeWebViewInstanceConfiguration configuration,
        NativeWebViewInstanceConfiguration? rollbackConfiguration)
    {
        if (!Controller.TryGetBackend<INativeWebViewInstanceConfigurationTarget>(out var target))
            return;

        try
        {
            target.ApplyInstanceConfiguration(configuration);
        }
        catch (Exception applicationException)
        {
            if (rollbackConfiguration is not null)
            {
                try
                {
                    target.ApplyInstanceConfiguration(rollbackConfiguration.Clone());
                }
                catch (Exception rollbackException)
                {
                    applicationException.Data["NativeWebView.InstanceConfigurationRollbackException"] = rollbackException;
                }
            }

            throw;
        }
    }

    private void ValidateConfigurationCanChange()
    {
        if (_isConfigurationCommitted ||
            LifecycleState != NativeWebComponentState.Created ||
            CurrentUrl is not null)
        {
            throw new InvalidOperationException(
                "The native WebView instance configuration cannot change after native host creation, initialization, or navigation begins.");
        }
    }
}
