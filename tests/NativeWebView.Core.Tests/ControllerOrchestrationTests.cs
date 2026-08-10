using NativeWebView.Core;
using NativeWebViewControl = NativeWebView.Controls.NativeWebView;

namespace NativeWebView.Core.Tests;

#pragma warning disable CS0067
public sealed class ControllerOrchestrationTests
{
    [Fact]
    public async Task NativeWebViewController_InitializeAsync_IsIdempotent()
    {
        var backend = new TestWebViewBackend();
        using var controller = new NativeWebViewController(backend);

        await Task.WhenAll(
            controller.InitializeAsync().AsTask(),
            controller.InitializeAsync().AsTask(),
            controller.InitializeAsync().AsTask());

        Assert.Equal(1, backend.InitializeCallCount);
        Assert.Equal(NativeWebComponentState.Ready, controller.State);
        Assert.True(controller.IsInitialized);
    }

    [Fact]
    public void NativeWebViewController_TracksNavigationSnapshot()
    {
        var backend = new TestWebViewBackend();
        using var controller = new NativeWebViewController(backend);

        controller.Navigate("https://example.com/first");
        controller.Navigate("https://example.com/second");

        Assert.Equal(new Uri("https://example.com/second"), controller.CurrentUrl);
        Assert.True(controller.CanGoBack);
        Assert.False(controller.CanGoForward);

        controller.GoBack();

        Assert.Equal(new Uri("https://example.com/first"), controller.CurrentUrl);
        Assert.False(controller.CanGoBack);
        Assert.True(controller.CanGoForward);
    }

    [Fact]
    public void NativeWebViewController_DoesNotUpdateSnapshot_WhenNavigationIsCancelled()
    {
        var backend = new TestWebViewBackend();
        using var controller = new NativeWebViewController(backend);

        controller.Navigate("https://example.com/first");
        Assert.Equal(new Uri("https://example.com/first"), controller.CurrentUrl);

        controller.NavigationStarted += (_, e) => e.Cancel = true;
        controller.Navigate("https://example.com/cancelled");

        Assert.Equal(new Uri("https://example.com/first"), controller.CurrentUrl);
    }

    [Fact]
    public void NativeWebViewController_StopsEventDispatch_AfterDispose()
    {
        var backend = new TestWebViewBackend();
        var controller = new NativeWebViewController(backend);

        var messageCount = 0;
        controller.WebMessageReceived += (_, _) => messageCount++;

        backend.EmitWebMessage("first");
        Assert.Equal(1, messageCount);

        controller.Dispose();
        Assert.Equal(NativeWebComponentState.Disposed, controller.State);

        backend.EmitWebMessage("second");
        Assert.Equal(1, messageCount);

        Assert.Throws<ObjectDisposedException>(() => controller.Navigate("https://example.com/disposed"));
    }

    [Fact]
    public void NativeWebViewController_ForwardsNormalizedStatusText_WithoutDuplicates()
    {
        var backend = new TestWebViewBackend();
        using var controller = new NativeWebViewController(backend);
        var changes = new List<string?>();
        controller.StatusTextChanged += (_, args) => changes.Add(args.StatusText);

        backend.EmitStatusText("  https://example.com/target  ");
        backend.EmitStatusText("https://example.com/target");
        backend.EmitStatusText("   ");

        Assert.Null(controller.StatusText);
        Assert.Equal(["https://example.com/target", null], changes);
    }

    [Fact]
    public void NativeWebViewController_InitializesStatusText_FromProvider()
    {
        var backend = new TestWebViewBackend();
        backend.EmitStatusText("  https://example.com/initial  ");

        using var controller = new NativeWebViewController(backend);

        Assert.Equal("https://example.com/initial", controller.StatusText);
    }

    [Fact]
    public void NativeWebViewController_ClearsAndUnsubscribesStatusText_WhenDisposed()
    {
        var backend = new TestWebViewBackend();
        var controller = new NativeWebViewController(backend);
        var changes = new List<string?>();
        controller.StatusTextChanged += (_, args) => changes.Add(args.StatusText);
        backend.EmitStatusText("https://example.com/target");

        controller.Dispose();
        backend.EmitStatusText("https://example.com/stale");

        Assert.Null(controller.StatusText);
        Assert.Equal(["https://example.com/target", null], changes);
    }

    [Fact]
    public void NativeWebViewController_ForwardsEffectiveZoomFactor_WithoutDuplicatesOrNativeEchoes()
    {
        var backend = new TestWebViewBackend();
        using var controller = new NativeWebViewController(backend);
        var changes = new List<double>();
        controller.ZoomFactorChanged += (_, args) => changes.Add(args.ZoomFactor);

        controller.SetZoomFactor(1.25);
        backend.EmitZoomFactor(1.25);
        backend.EmitZoomFactor(1.2505);
        backend.EmitZoomFactor(1.5);

        Assert.Equal(1.5, controller.ZoomFactor);
        Assert.Equal([1.25, 1.5], changes);
    }

    [Fact]
    public void ZoomFactorChangedEventArgs_RejectsInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeWebViewZoomFactorChangedEventArgs(0d));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeWebViewZoomFactorChangedEventArgs(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeWebViewZoomFactorChangedEventArgs(double.PositiveInfinity));
    }

    [Fact]
    public void NativeWebViewController_RejectsStaleZoomCallbacksAfterDisposal()
    {
        var backend = new TestWebViewBackend();
        var controller = new NativeWebViewController(backend);
        var changes = new List<double>();
        controller.ZoomFactorChanged += (_, args) => changes.Add(args.ZoomFactor);

        backend.EmitZoomFactor(1.25);
        controller.Dispose();
        backend.EmitZoomFactor(1.5);

        Assert.Equal([1.25], changes);
    }

    [Fact]
    public async Task NativeWebViewController_DoesNotBlockDisposalWhileStatusSubscriberRuns()
    {
        var backend = new TestWebViewBackend();
        var controller = new NativeWebViewController(backend);
        var changes = new List<string?>();
        using var statusEntered = new ManualResetEventSlim();
        using var releaseStatus = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        controller.StatusTextChanged += (_, args) =>
        {
            changes.Add(args.StatusText);
            if (args.StatusText is not null)
            {
                statusEntered.Set();
                releaseStatus.Wait();
            }
        };

        var statusTask = Task.Run(() => backend.EmitStatusText("https://example.com/target"));
        Assert.True(statusEntered.Wait(TimeSpan.FromSeconds(5)));

        var disposeTask = Task.Run(() =>
        {
            disposeStarted.Set();
            controller.Dispose();
        });
        Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5)));

        try
        {
            var completedTask = await Task.WhenAny(
                backend.DisposedTask,
                Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(backend.DisposedTask, completedTask);
        }
        finally
        {
            releaseStatus.Set();
        }

        await Task.WhenAll(statusTask, disposeTask);
        Assert.Null(controller.StatusText);
        Assert.True(backend.IsDisposed);
        Assert.Equal(["https://example.com/target", null], changes);
    }

    [Fact]
    public void NativeWebViewController_AllowsStatusSubscriberToDisposeController()
    {
        var backend = new TestWebViewBackend();
        var controller = new NativeWebViewController(backend);
        var changes = new List<string?>();
        controller.StatusTextChanged += (_, args) =>
        {
            changes.Add(args.StatusText);
            if (args.StatusText is not null)
                controller.Dispose();
        };

        backend.EmitStatusText("https://example.com/target");

        Assert.Equal(["https://example.com/target", null], changes);
        Assert.Null(controller.StatusText);
        Assert.True(backend.IsDisposed);
    }

    [Fact]
    public void NativeWebViewController_DisposesBackend_WhenStatusSubscriberThrowsDuringTeardown()
    {
        var backend = new TestWebViewBackend();
        backend.EmitStatusText("https://example.com/target");
        var controller = new NativeWebViewController(backend);
        controller.StatusTextChanged += (_, _) => throw new InvalidOperationException("Subscriber failure.");

        Assert.Throws<InvalidOperationException>(() => controller.Dispose());

        Assert.True(backend.IsDisposed);
        Assert.Equal(NativeWebComponentState.Disposed, controller.State);
    }

    [Fact]
    public async Task NativeWebViewController_DisposeDuringInitialization_StaysDisposed()
    {
        var backend = new TestWebViewBackend(
            initializeDelayMilliseconds: 100,
            allowInitializeAfterDispose: true);
        var controller = new NativeWebViewController(backend);

        var initializeTask = controller.InitializeAsync().AsTask();
        await backend.WaitForInitializationStartAsync();

        controller.Dispose();
        await initializeTask;

        Assert.Equal(NativeWebComponentState.Disposed, controller.State);
        Assert.Throws<ObjectDisposedException>(() => controller.Navigate("https://example.com/disposed"));
    }

    [Fact]
    public async Task NativeWebView_CapturesEmbeddedSnapshot_FromOptionalProvider()
    {
        var backend = new TestWebViewBackend();
        using var webView = new NativeWebViewControl(backend);
        await webView.InitializeAsync();

        var capture = webView.BeginCaptureSnapshot();
        var snapshot = await capture.Completion;

        Assert.True(capture.CaptureStarted.IsCompletedSuccessfully);
        Assert.NotNull(snapshot);
        Assert.Equal(
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
            snapshot.PngData.ToArray());
        Assert.Equal(1, backend.SnapshotCaptureCallCount);
    }

    [Fact]
    public async Task NativeWebView_EmbeddedSnapshot_ReturnsNull_WhenUnsupportedOrFailed()
    {
        var unsupportedBackend = new TestWebViewBackend(supportsSnapshot: false);
        using var unsupported = new NativeWebViewControl(unsupportedBackend);
        await unsupported.InitializeAsync();
        Assert.Null(await unsupported.CaptureSnapshotAsync());
        Assert.Equal(0, unsupportedBackend.SnapshotCaptureCallCount);

        var failingBackend = new TestWebViewBackend { ThrowDuringSnapshotCapture = true };
        using var failing = new NativeWebViewControl(failingBackend);
        await failing.InitializeAsync();
        Assert.Null(await failing.CaptureSnapshotAsync());
    }

    [Fact]
    public async Task NativeWebView_BeginEmbeddedSnapshot_NormalizesAsynchronousProviderFailure()
    {
        var backend = new TestWebViewBackend { FailSnapshotCaptureAsynchronously = true };
        using var webView = new NativeWebViewControl(backend);
        await webView.InitializeAsync();

        var capture = webView.BeginCaptureSnapshot();

        Assert.True(capture.CaptureStarted.IsCompletedSuccessfully);
        Assert.Null(await capture.Completion);
    }

    [Fact]
    public async Task NativeWebView_EmbeddedSnapshot_PropagatesCancellation()
    {
        var backend = new TestWebViewBackend();
        using var webView = new NativeWebViewControl(backend);
        await webView.InitializeAsync();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => webView.CaptureSnapshotAsync(cancellationSource.Token));
        Assert.Equal(0, backend.SnapshotCaptureCallCount);
    }

    [Fact]
    public void NativeWebDialogController_TracksVisibility_AndDisposal()
    {
        var backend = new TestDialogBackend();
        var controller = new NativeWebDialogController(backend);

        Assert.False(controller.IsVisible);

        controller.Show();
        Assert.True(controller.IsVisible);

        controller.Close();
        Assert.False(controller.IsVisible);

        controller.Dispose();
        Assert.Equal(NativeWebComponentState.Disposed, controller.State);

        Assert.Throws<ObjectDisposedException>(() => controller.Show());
    }

    [Fact]
    public async Task WebAuthenticationBrokerController_SerializesRequests()
    {
        var backend = new TestAuthenticationBackend(delayMilliseconds: 50);
        using var controller = new WebAuthenticationBrokerController(backend);

        var requestUri = new Uri("https://example.com/auth");
        var callbackUri = new Uri("https://example.com/callback");

        var first = controller.AuthenticateAsync(requestUri, callbackUri);
        var second = controller.AuthenticateAsync(requestUri, callbackUri);

        await Task.WhenAll(first, second);

        Assert.Equal(2, backend.CallCount);
        Assert.Equal(1, backend.MaxConcurrentRequests);
        Assert.Equal(WebAuthenticationBrokerState.Ready, controller.State);
    }

    [Fact]
    public void WebAuthenticationBrokerController_DisposesDisposableBackend()
    {
        var backend = new TestAuthenticationBackend(delayMilliseconds: 1);
        var controller = new WebAuthenticationBrokerController(backend);

        controller.Dispose();

        Assert.True(backend.IsDisposed);
    }

    [Fact]
    public async Task WebAuthenticationBrokerController_RejectsNonHttpRequestUri()
    {
        var backend = new TestAuthenticationBackend(delayMilliseconds: 1);
        using var controller = new WebAuthenticationBrokerController(backend);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => controller.AuthenticateAsync(
            new Uri("ftp://example.com/auth"),
            new Uri("https://example.com/callback")));

        Assert.Equal("requestUri", exception.ParamName);
        Assert.Equal(0, backend.CallCount);
    }

    [Fact]
    public async Task WebAuthenticationBrokerController_RejectsUnsafeCallbackUriScheme()
    {
        var backend = new TestAuthenticationBackend(delayMilliseconds: 1);
        using var controller = new WebAuthenticationBrokerController(backend);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => controller.AuthenticateAsync(
            new Uri("https://example.com/auth"),
            new Uri("javascript:alert(1)")));

        Assert.Equal("callbackUri", exception.ParamName);
        Assert.Equal(0, backend.CallCount);
    }

    [Fact]
    public async Task WebAuthenticationBrokerController_RejectsRelativeCallbackUri()
    {
        var backend = new TestAuthenticationBackend(delayMilliseconds: 1);
        using var controller = new WebAuthenticationBrokerController(backend);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => controller.AuthenticateAsync(
            new Uri("https://example.com/auth"),
            new Uri("/callback", UriKind.Relative)));

        Assert.Equal("callbackUri", exception.ParamName);
        Assert.Equal(0, backend.CallCount);
    }

    [Fact]
    public async Task WebAuthenticationBrokerController_RejectsUserInfoInUris()
    {
        var backend = new TestAuthenticationBackend(delayMilliseconds: 1);
        using var controller = new WebAuthenticationBrokerController(backend);

        var requestException = await Assert.ThrowsAsync<ArgumentException>(() => controller.AuthenticateAsync(
            new Uri("https://user:pass@example.com/auth"),
            new Uri("https://example.com/callback")));
        Assert.Equal("requestUri", requestException.ParamName);
        Assert.Equal(0, backend.CallCount);

        var callbackException = await Assert.ThrowsAsync<ArgumentException>(() => controller.AuthenticateAsync(
            new Uri("https://example.com/auth"),
            new Uri("myapp://user:pass@callback/path")));
        Assert.Equal("callbackUri", callbackException.ParamName);
        Assert.Equal(0, backend.CallCount);
    }

    private sealed class TestWebViewBackend :
        INativeWebViewBackend,
        INativeWebViewStatusTextProvider,
        INativeWebViewZoomFactorProvider,
        INativeWebViewSnapshotProvider
    {
        private readonly List<Uri> _history = [];
        private readonly int _initializeDelayMilliseconds;
        private readonly bool _allowInitializeAfterDispose;
        private readonly TaskCompletionSource<bool> _initializationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _historyIndex = -1;
        private bool _disposed;

        public TestWebViewBackend(
            int initializeDelayMilliseconds = 0,
            bool allowInitializeAfterDispose = false,
            bool supportsSnapshot = true)
        {
            _initializeDelayMilliseconds = initializeDelayMilliseconds;
            _allowInitializeAfterDispose = allowInitializeAfterDispose;
            Features = new WebViewPlatformFeatures(
                NativeWebViewPlatform.Windows,
                NativeWebViewFeature.EmbeddedView |
                NativeWebViewFeature.DevTools |
                NativeWebViewFeature.ContextMenu |
                NativeWebViewFeature.StatusBar |
                NativeWebViewFeature.ZoomControl |
                NativeWebViewFeature.Printing |
                NativeWebViewFeature.PrintUi |
                NativeWebViewFeature.WebMessageChannel |
                NativeWebViewFeature.ScriptExecution |
                NativeWebViewFeature.NewWindowRequestInterception |
                NativeWebViewFeature.WebResourceRequestInterception |
                NativeWebViewFeature.EnvironmentOptions |
                NativeWebViewFeature.ControllerOptions |
                (supportsSnapshot ? NativeWebViewFeature.EmbeddedSnapshotCapture : NativeWebViewFeature.None));
        }

        public NativeWebViewPlatform Platform => NativeWebViewPlatform.Windows;

        public IWebViewPlatformFeatures Features { get; }

        public Uri? CurrentUrl { get; private set; }

        public bool IsInitialized { get; private set; }

        public bool CanGoBack => _historyIndex > 0;

        public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

        public bool IsDevToolsEnabled { get; set; } = true;

        public bool IsContextMenuEnabled { get; set; } = true;

        public bool IsStatusBarEnabled { get; set; } = true;

        public bool IsZoomControlEnabled { get; set; } = true;

        public double ZoomFactor { get; private set; } = 1.0;

        public string? HeaderString { get; private set; }

        public string? UserAgentString { get; private set; }

        public string? StatusText { get; private set; }

        public int InitializeCallCount { get; private set; }

        public int SnapshotCaptureCallCount { get; private set; }

        public bool ThrowDuringSnapshotCapture { get; set; }

        public bool FailSnapshotCaptureAsynchronously { get; set; }

        public bool IsDisposed => _disposed;

        public Task DisposedTask => _disposedSignal.Task;

        public event EventHandler<CoreWebViewInitializedEventArgs>? CoreWebView2Initialized;

        public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

        public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

        public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

        public event EventHandler<NativeWebViewOpenDevToolsRequestedEventArgs>? OpenDevToolsRequested;

        public event EventHandler<NativeWebViewDestroyRequestedEventArgs>? DestroyRequested;

        public event EventHandler<NativeWebViewRequestCustomChromeEventArgs>? RequestCustomChrome;

        public event EventHandler<NativeWebViewRequestParentWindowPositionEventArgs>? RequestParentWindowPosition;

        public event EventHandler<NativeWebViewBeginMoveDragEventArgs>? BeginMoveDrag;

        public event EventHandler<NativeWebViewBeginResizeDragEventArgs>? BeginResizeDrag;

        public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

        public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

        public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

        public event EventHandler<NativeWebViewNavigationHistoryChangedEventArgs>? NavigationHistoryChanged;

        public event EventHandler<CoreWebViewEnvironmentRequestedEventArgs>? CoreWebView2EnvironmentRequested;

        public event EventHandler<CoreWebViewControllerOptionsRequestedEventArgs>? CoreWebView2ControllerOptionsRequested;

        public event EventHandler<NativeWebViewStatusTextChangedEventArgs>? StatusTextChanged;

        public event EventHandler<NativeWebViewZoomFactorChangedEventArgs>? ZoomFactorChanged;

        public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!_allowInitializeAfterDispose)
            {
                EnsureNotDisposed();
            }

            cancellationToken.ThrowIfCancellationRequested();
            _initializationStarted.TrySetResult(true);

            if (_initializeDelayMilliseconds > 0)
            {
                await Task.Delay(_initializeDelayMilliseconds, cancellationToken);
            }

            if (!_allowInitializeAfterDispose)
            {
                EnsureNotDisposed();
            }

            InitializeCallCount++;
            if (!IsInitialized)
            {
                IsInitialized = true;
                CoreWebView2EnvironmentRequested?.Invoke(this, new CoreWebViewEnvironmentRequestedEventArgs(new NativeWebViewEnvironmentOptions()));
                CoreWebView2ControllerOptionsRequested?.Invoke(this, new CoreWebViewControllerOptionsRequestedEventArgs(new NativeWebViewControllerOptions()));
                CoreWebView2Initialized?.Invoke(this, new CoreWebViewInitializedEventArgs(isSuccess: true));
            }

        }

        public Task WaitForInitializationStartAsync()
        {
            return _initializationStarted.Task;
        }

        public void Navigate(string url)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                throw new ArgumentException("Invalid URL.", nameof(url));
            }

            Navigate(uri);
        }

        public void Navigate(Uri uri)
        {
            EnsureNotDisposed();
            ArgumentNullException.ThrowIfNull(uri);

            var started = new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false);
            NavigationStarted?.Invoke(this, started);

            if (started.Cancel)
            {
                return;
            }

            if (_historyIndex < _history.Count - 1)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            }

            _history.Add(uri);
            _historyIndex = _history.Count - 1;
            CurrentUrl = uri;

            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
            NavigationHistoryChanged?.Invoke(this, new NativeWebViewNavigationHistoryChangedEventArgs(CanGoBack, CanGoForward));
        }

        public void Reload()
        {
            EnsureNotDisposed();

            if (CurrentUrl is null)
            {
                return;
            }

            NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(CurrentUrl, isRedirected: false));
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
        }

        public void Stop()
        {
            EnsureNotDisposed();
        }

        public void GoBack()
        {
            EnsureNotDisposed();

            if (!CanGoBack)
            {
                return;
            }

            _historyIndex--;
            CurrentUrl = _history[_historyIndex];
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
            NavigationHistoryChanged?.Invoke(this, new NativeWebViewNavigationHistoryChangedEventArgs(CanGoBack, CanGoForward));
        }

        public void GoForward()
        {
            EnsureNotDisposed();

            if (!CanGoForward)
            {
                return;
            }

            _historyIndex++;
            CurrentUrl = _history[_historyIndex];
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
            NavigationHistoryChanged?.Invoke(this, new NativeWebViewNavigationHistoryChangedEventArgs(CanGoBack, CanGoForward));
        }

        public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("ok");
        }

        public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: message));
            return Task.CompletedTask;
        }

        public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
            return Task.CompletedTask;
        }

        public Task<NativeWebViewSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotCaptureCallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowDuringSnapshotCapture)
                throw new InvalidOperationException("Snapshot failure.");
            return Task.FromResult<NativeWebViewSnapshot?>(
                new NativeWebViewSnapshot(Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=")));
        }

        public NativeWebViewSnapshotCapture BeginCaptureSnapshot(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailSnapshotCaptureAsynchronously)
            {
                return new NativeWebViewSnapshotCapture(
                    Task.CompletedTask,
                    Task.FromException<NativeWebViewSnapshot?>(
                        new InvalidOperationException("Asynchronous snapshot failure.")));
            }

            return new NativeWebViewSnapshotCapture(
                Task.CompletedTask,
                CaptureSnapshotAsync(cancellationToken));
        }

        public void OpenDevToolsWindow()
        {
            EnsureNotDisposed();
            OpenDevToolsRequested?.Invoke(this, new NativeWebViewOpenDevToolsRequestedEventArgs());
        }

        public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            _ = settings;
            return Task.FromResult(new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success));
        }

        public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public void SetZoomFactor(double zoomFactor)
        {
            EnsureNotDisposed();
            if (!NativeWebViewZoomFactor.HasChanged(ZoomFactor, zoomFactor))
                return;

            ZoomFactor = zoomFactor;
            ZoomFactorChanged?.Invoke(this, new NativeWebViewZoomFactorChangedEventArgs(zoomFactor));
        }

        public void SetUserAgent(string? userAgent)
        {
            EnsureNotDisposed();
            UserAgentString = userAgent;
        }

        public void SetHeader(string? header)
        {
            EnsureNotDisposed();
            HeaderString = header;
        }

        public bool TryGetCommandManager(out INativeWebViewCommandManager? commandManager)
        {
            commandManager = null;
            return false;
        }

        public bool TryGetCookieManager(out INativeWebViewCookieManager? cookieManager)
        {
            cookieManager = null;
            return false;
        }

        public void MoveFocus(NativeWebViewFocusMoveDirection direction)
        {
            EnsureNotDisposed();
            _ = direction;
        }

        public void EmitWebMessage(string message)
        {
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
        }

        public void EmitStatusText(string? statusText)
        {
            StatusText = statusText;
            StatusTextChanged?.Invoke(this, new NativeWebViewStatusTextChangedEventArgs(statusText));
        }

        public void EmitZoomFactor(double zoomFactor)
        {
            ZoomFactor = zoomFactor;
            ZoomFactorChanged?.Invoke(this, new NativeWebViewZoomFactorChangedEventArgs(zoomFactor));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _disposedSignal.TrySetResult(true);
            DestroyRequested?.Invoke(this, new NativeWebViewDestroyRequestedEventArgs("Disposed"));
        }

        private void EnsureNotDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private sealed class TestDialogBackend : INativeWebDialogBackend
    {
        private bool _disposed;
        private readonly List<Uri> _history = [];
        private int _historyIndex = -1;

        public NativeWebViewPlatform Platform => NativeWebViewPlatform.Windows;

        public IWebViewPlatformFeatures Features { get; } = new WebViewPlatformFeatures(
            NativeWebViewPlatform.Windows,
            NativeWebViewFeature.Dialog |
            NativeWebViewFeature.ScriptExecution |
            NativeWebViewFeature.WebMessageChannel);

        public bool IsVisible { get; private set; }

        public Uri? CurrentUrl { get; private set; }

        public bool CanGoBack => _historyIndex > 0;

        public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

        public bool IsDevToolsEnabled { get; set; }

        public bool IsContextMenuEnabled { get; set; }

        public bool IsStatusBarEnabled { get; set; }

        public bool IsZoomControlEnabled { get; set; }

        public double ZoomFactor { get; private set; } = 1.0;

        public string? HeaderString { get; private set; }

        public string? UserAgentString { get; private set; }

        public event EventHandler<EventArgs>? Shown;

        public event EventHandler<EventArgs>? Closed;

        public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

        public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

        public event EventHandler<NativeWebViewMessageReceivedEventArgs>? WebMessageReceived;

        public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

        public event EventHandler<NativeWebViewResourceRequestedEventArgs>? WebResourceRequested;

        public event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? ContextMenuRequested;

        public void Show(NativeWebDialogShowOptions? options = null)
        {
            EnsureNotDisposed();
            _ = options;
            IsVisible = true;
            Shown?.Invoke(this, EventArgs.Empty);
        }

        public void Close()
        {
            EnsureNotDisposed();
            IsVisible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        public void Move(double left, double top)
        {
            EnsureNotDisposed();
            _ = left;
            _ = top;
        }

        public void Resize(double width, double height)
        {
            EnsureNotDisposed();
            _ = width;
            _ = height;
        }

        public void Navigate(string url)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(url);
            Navigate(new Uri(url));
        }

        public void Navigate(Uri uri)
        {
            EnsureNotDisposed();
            ArgumentNullException.ThrowIfNull(uri);

            NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false));

            if (_historyIndex < _history.Count - 1)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            }

            _history.Add(uri);
            _historyIndex = _history.Count - 1;
            CurrentUrl = uri;

            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
        }

        public void Reload()
        {
            EnsureNotDisposed();
        }

        public void Stop()
        {
            EnsureNotDisposed();
        }

        public void GoBack()
        {
            EnsureNotDisposed();

            if (!CanGoBack)
            {
                return;
            }

            _historyIndex--;
            CurrentUrl = _history[_historyIndex];
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
        }

        public void GoForward()
        {
            EnsureNotDisposed();

            if (!CanGoForward)
            {
                return;
            }

            _historyIndex++;
            CurrentUrl = _history[_historyIndex];
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(CurrentUrl, isSuccess: true, httpStatusCode: 200));
        }

        public Task<string?> ExecuteScriptAsync(string script, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("ok");
        }

        public Task PostWebMessageAsJsonAsync(string message, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message: null, json: message));
            return Task.CompletedTask;
        }

        public Task PostWebMessageAsStringAsync(string message, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            WebMessageReceived?.Invoke(this, new NativeWebViewMessageReceivedEventArgs(message, json: null));
            return Task.CompletedTask;
        }

        public void OpenDevToolsWindow()
        {
            EnsureNotDisposed();
        }

        public Task<NativeWebViewPrintResult> PrintAsync(NativeWebViewPrintSettings? settings = null, CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            _ = settings;
            return Task.FromResult(new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success));
        }

        public Task<bool> ShowPrintUiAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public void SetZoomFactor(double zoomFactor)
        {
            EnsureNotDisposed();
            ZoomFactor = zoomFactor;
        }

        public void SetUserAgent(string? userAgent)
        {
            EnsureNotDisposed();
            UserAgentString = userAgent;
        }

        public void SetHeader(string? header)
        {
            EnsureNotDisposed();
            HeaderString = header;
        }

        public void Dispose()
        {
            _disposed = true;
            IsVisible = false;
        }

        private void EnsureNotDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }

    private sealed class TestAuthenticationBackend : IWebAuthenticationBrokerBackend, IDisposable
    {
        private readonly int _delayMilliseconds;

        private int _inFlight;

        public TestAuthenticationBackend(int delayMilliseconds)
        {
            _delayMilliseconds = delayMilliseconds;
        }

        public NativeWebViewPlatform Platform => NativeWebViewPlatform.Windows;

        public IWebViewPlatformFeatures Features { get; } = new WebViewPlatformFeatures(
            NativeWebViewPlatform.Windows,
            NativeWebViewFeature.AuthenticationBroker);

        public int CallCount { get; private set; }

        public int MaxConcurrentRequests { get; private set; }

        public bool IsDisposed { get; private set; }

        public async Task<WebAuthenticationResult> AuthenticateAsync(
            Uri requestUri,
            Uri callbackUri,
            WebAuthenticationOptions options = WebAuthenticationOptions.None,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requestUri);
            ArgumentNullException.ThrowIfNull(callbackUri);
            _ = options;

            CallCount++;
            var inFlight = Interlocked.Increment(ref _inFlight);
            MaxConcurrentRequests = Math.Max(MaxConcurrentRequests, inFlight);

            try
            {
                await Task.Delay(_delayMilliseconds, cancellationToken).ConfigureAwait(false);
                return WebAuthenticationResult.Success(callbackUri.ToString());
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
#pragma warning restore CS0067
