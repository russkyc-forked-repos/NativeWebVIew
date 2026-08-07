using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NativeWebView.Auth;
using NativeWebView.Controls;
using NativeWebView.Core;
using NativeWebView.Dialog;
using NativeWebView.Interop;

namespace NativeWebView.Integration;

internal sealed class IntegrationView : UserControl
{
    private readonly NativeWebView.Controls.NativeWebView _webView;
    private readonly Grid _rootGrid;
    private readonly TextBlock _statusBlock;
    private readonly TextBox _logBox;
    private readonly StringBuilder _logBuffer = new();

    private bool _started;

    public IntegrationView()
    {
        _statusBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Text = "Waiting for integration host...",
        };

        _logBox = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
        };

        _webView = new NativeWebView.Controls.NativeWebView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        if (_webView.Features.Supports(NativeWebViewFeature.DocumentStartScriptInjection))
        {
            _webView.InstanceConfiguration.DocumentStartScripts.Add(
                new NativeWebViewDocumentStartScript(
                    "globalThis.__nativeWebViewDocumentStartErrorCount = 0; globalThis.addEventListener('error', () => { globalThis.__nativeWebViewDocumentStartErrorCount += 1; });",
                    NativeWebViewScriptFrameScope.AllFrames));
            _webView.InstanceConfiguration.DocumentStartScripts.Add(
                new NativeWebViewDocumentStartScript(
                    "let __nativeWebViewDocumentStartLexicalMarker = 'main-first'; globalThis.__nativeWebViewMainFrameExecutionMarker = 'main-only';",
                    NativeWebViewScriptFrameScope.MainFrame));
            _webView.InstanceConfiguration.DocumentStartScripts.Add(
                new NativeWebViewDocumentStartScript(
                    "globalThis.__nativeWebViewDocumentStartOrder = globalThis.__nativeWebViewDocumentStartOrder || []; globalThis.__nativeWebViewDocumentStartOrder.push(__nativeWebViewDocumentStartLexicalMarker);",
                    NativeWebViewScriptFrameScope.MainFrame));
            _webView.InstanceConfiguration.DocumentStartScripts.Add(
                new NativeWebViewDocumentStartScript(
                    "globalThis.__nativeWebViewObservedMainFrameExecutionMarker = globalThis.__nativeWebViewMainFrameExecutionMarker ?? null; globalThis.__nativeWebViewDocumentStartOrder = globalThis.__nativeWebViewDocumentStartOrder || []; globalThis.__nativeWebViewDocumentStartOrder.push('all-second');",
                    NativeWebViewScriptFrameScope.AllFrames));
        }

        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#EEF2FF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#C7D2FE")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 12),
            Child = _statusBlock,
        };

        var webViewBorder = new Border
        {
            Margin = new Thickness(0, 16, 0, 16),
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Child = _webView,
        };

        var logBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(1),
            Child = _logBox,
        };

        Grid.SetRow(webViewBorder, 1);
        Grid.SetRow(logBorder, 2);

        _rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,220"),
            Margin = new Thickness(16),
            Children =
            {
                headerBorder,
                webViewBorder,
                logBorder,
            },
        };
        Content = _rootGrid;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_started)
        {
            return;
        }

        _started = true;
        Dispatcher.UIThread.Post(() => _ = RunAsync(), DispatcherPriority.Background);
    }

    private async Task RunAsync()
    {
        var platform = NativeWebViewRuntime.CurrentPlatform;
        var result = new IntegrationRunResult
        {
            Platform = platform.ToString(),
            StartedAtUtc = DateTimeOffset.UtcNow,
        };

        AppendLog($"Starting integration run for {platform}.");
        UpdateStatus($"Running integration scenarios for {platform}...");

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            await using var pages = await IntegrationPageCatalog.CreateAsync(platform, cancellationSource.Token).ConfigureAwait(true);

            result.Scenarios.Add(await RunWebViewScenarioAsync(platform, pages, cancellationSource.Token).ConfigureAwait(true));
            result.Scenarios.Add(await RunDialogScenarioAsync(platform, pages, cancellationSource.Token).ConfigureAwait(true));
            result.Scenarios.Add(await RunAuthenticationScenarioAsync(platform, pages, cancellationSource.Token).ConfigureAwait(true));
        }
        catch (Exception ex)
        {
            result.Scenarios.Add(new IntegrationScenarioResult
            {
                Name = "harness",
                Passed = false,
                Details = $"{ex.GetType().Name}: {ex.Message}",
            });

            AppendLog($"Harness failure: {FormatException(ex)}");
        }

        result.CompletedAtUtc = DateTimeOffset.UtcNow;
        IntegrationLog.PublishResult(result);

        UpdateStatus(result.Passed
            ? $"Integration run passed for {platform}."
            : $"Integration run failed for {platform}.");

        AppendLog($"Integration run completed. Passed={result.Passed}.");
        TryShutdownIfDesktop(result.Passed ? 0 : 1);
    }

    private async Task<IntegrationScenarioResult> RunWebViewScenarioAsync(
        NativeWebViewPlatform platform,
        IntegrationPageCatalog pages,
        CancellationToken cancellationToken)
    {
        var scenario = new IntegrationScenarioResult { Name = "webview" };

        var initializedCompletion = new TaskCompletionSource<CoreWebViewInitializedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var navigationCompletion = new TaskCompletionSource<NativeWebViewNavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageReadyCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageJsonCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var directMacOsMessagesCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcherExceptionCompletion = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var messageAfterDispatcherExceptionCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var directMacOsMessageKinds = new HashSet<string>(StringComparer.Ordinal);

        void OnInitialized(object? sender, CoreWebViewInitializedEventArgs e)
        {
            initializedCompletion.TrySetResult(e);
        }

        void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
        {
            if (e.Uri is not null && AreSameUri(e.Uri, pages.WebViewPageUri))
            {
                navigationCompletion.TrySetResult(e);
            }
        }

        void OnWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e)
        {
            var message = e.Message ?? e.Json;
            if (string.Equals(message, "page-ready:webview", StringComparison.Ordinal))
            {
                pageReadyCompletion.TrySetResult(message!);
            }
            if (e.Json?.Contains("\"kind\":\"page-ready-json\"", StringComparison.Ordinal) == true)
            {
                pageJsonCompletion.TrySetResult(e.Json);
            }
            if (platform == NativeWebViewPlatform.MacOS &&
                e.Json?.Contains("\"directEnvelope\":true", StringComparison.Ordinal) == true)
            {
                directMacOsMessagesCompletion.TrySetException(
                    new InvalidOperationException("A directly posted Foundation object was incorrectly classified as a JSON envelope."));
            }
            if (platform == NativeWebViewPlatform.MacOS &&
                e.Json is null &&
                TryClassifyDirectMacOsMessage(e.Message, out var directMessageKind))
            {
                directMacOsMessageKinds.Add(directMessageKind);
                if (directMacOsMessageKinds.Count == 7)
                    directMacOsMessagesCompletion.TrySetResult(true);
            }
            if (string.Equals(e.Message, "dispatcher-handler-after", StringComparison.Ordinal))
                messageAfterDispatcherExceptionCompletion.TrySetResult(true);
        }

        void ThrowFromWebMessageHandler(object? sender, NativeWebViewMessageReceivedEventArgs e)
        {
            if (!string.Equals(e.Message, "dispatcher-handler-throw", StringComparison.Ordinal))
                return;

            _webView.WebMessageReceived -= ThrowFromWebMessageHandler;
            throw new InvalidOperationException("macOS dispatcher-isolated web-message handler failure");
        }

        void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (e.Exception is not InvalidOperationException exception ||
                !string.Equals(exception.Message, "macOS dispatcher-isolated web-message handler failure", StringComparison.Ordinal))
            {
                return;
            }

            e.Handled = true;
            dispatcherExceptionCompletion.TrySetResult(exception);
        }

        _webView.CoreWebView2Initialized += OnInitialized;
        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.WebMessageReceived += OnWebMessageReceived;
        if (platform == NativeWebViewPlatform.MacOS)
        {
            _webView.WebMessageReceived += ThrowFromWebMessageHandler;
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        }

        try
        {
            AppendLog("[webview] Initializing embedded control.");

            if (platform == NativeWebViewPlatform.MacOS)
            {
                _webView.RenderMode = NativeWebViewRenderMode.Offscreen;
            }

            await _webView.InitializeAsync(cancellationToken).ConfigureAwait(true);

            if (platform != NativeWebViewPlatform.MacOS)
            {
                var initializedArgs = await initializedCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);

                if (!initializedArgs.IsSuccess || initializedArgs.NativeObject is null)
                {
                    throw new InvalidOperationException("Embedded control did not report a runtime-native initialization object.");
                }

                scenario.Evidence.Add($"initialized:{initializedArgs.NativeObject.GetType().FullName ?? initializedArgs.NativeObject}");
            }
            else
            {
                scenario.Evidence.Add("initialized:macos-native-host");
            }

            _webView.Navigate(pages.WebViewPageUri);

            var navigationArgs = await navigationCompletion.Task
                .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(true);

            if (!navigationArgs.IsSuccess)
            {
                throw new InvalidOperationException($"Navigation failed: {navigationArgs.Error ?? "unknown error"}");
            }

            scenario.Evidence.Add($"navigated:{navigationArgs.Uri}");

            if (platform == NativeWebViewPlatform.MacOS)
            {
                var frame = await WaitForRenderFrameAsync(cancellationToken).ConfigureAwait(true);
                if (frame is not null && !frame.IsSynthetic)
                {
                    scenario.Evidence.Add($"frame:{frame.PixelWidth}x{frame.PixelHeight}:{frame.Origin}");
                }
                else
                {
                    var outputPath = Path.Combine(
                        IntegrationPlatformContext.GetArtifactsDirectory(platform),
                        "macos-webview-proof.pdf");

                    var printResult = await _webView.PrintAsync(
                            new NativeWebViewPrintSettings { OutputPath = outputPath },
                            cancellationToken)
                        .ConfigureAwait(true);

                    if (printResult.Status != NativeWebViewPrintStatus.Success ||
                        !File.Exists(outputPath) ||
                        new FileInfo(outputPath).Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"macOS embedded control did not produce a render proof. Print status={printResult.Status}, error={printResult.ErrorMessage ?? "<none>"}");
                    }

                    scenario.Evidence.Add($"printed:{outputPath}");
                }
            }
            else
            {
                if (platform != NativeWebViewPlatform.Browser)
                {
                    await pageReadyCompletion.Task
                        .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                        .ConfigureAwait(true);
                }
            }

            var pageReady = await EvaluateBooleanAsync(
                    _webView.ExecuteScriptAsync,
                    "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.pageReady",
                    cancellationToken)
                .ConfigureAwait(true);

            if (!pageReady)
            {
                throw new InvalidOperationException("Embedded page did not report a ready state.");
            }

            var location = await EvaluateStringAsync(_webView.ExecuteScriptAsync, "window.location.href", cancellationToken)
                .ConfigureAwait(true);

            if (!Uri.TryCreate(location, UriKind.Absolute, out var actualLocation) ||
                actualLocation is null ||
                !AreSameUri(actualLocation, pages.WebViewPageUri))
            {
                throw new InvalidOperationException($"Unexpected embedded page location '{location ?? "<null>"}'.");
            }

            scenario.Evidence.Add($"location:{location}");

            if (_webView.Features.Supports(NativeWebViewFeature.DocumentStartScriptInjection))
            {
                var mainFrameOrder = await EvaluateStringAsync(
                        _webView.ExecuteScriptAsync,
                        "JSON.stringify(window.__nativeWebViewIntegrationState.documentStartOrder)",
                        cancellationToken)
                    .ConfigureAwait(true);
                var childFrameOrder = await EvaluateStringAsync(
                        _webView.ExecuteScriptAsync,
                        "JSON.stringify(window.__nativeWebViewIntegrationState.childDocumentStartOrder)",
                        cancellationToken)
                    .ConfigureAwait(true);
                var mainLexicalMarker = await EvaluateStringAsync(
                        _webView.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState.documentStartLexicalMarker",
                        cancellationToken)
                    .ConfigureAwait(true);
                var childMainFrameMarker = await EvaluateStringAsync(
                        _webView.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState.childDocumentStartMainFrameMarker",
                        cancellationToken)
                    .ConfigureAwait(true);
                var childErrorCount = await EvaluateStringAsync(
                        _webView.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState.childDocumentStartErrorCount",
                        cancellationToken)
                    .ConfigureAwait(true);
                if (!string.Equals(mainFrameOrder, "[\"main-first\",\"all-second\"]", StringComparison.Ordinal) ||
                    !string.Equals(childFrameOrder, "[\"all-second\"]", StringComparison.Ordinal) ||
                    !string.Equals(mainLexicalMarker, "main-first", StringComparison.Ordinal) ||
                    childMainFrameMarker is not null ||
                    !string.Equals(childErrorCount, "0", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Unexpected document-start state. Main={mainFrameOrder ?? "<null>"}, child={childFrameOrder ?? "<null>"}, main lexical={mainLexicalMarker ?? "<null>"}, child main-frame marker={childMainFrameMarker ?? "<null>"}, child errors={childErrorCount ?? "<null>"}.");
                }

                scenario.Evidence.Add($"document-start:{mainFrameOrder}:{childFrameOrder}:lexical={mainLexicalMarker}:child-errors={childErrorCount}");
            }

            if (platform == NativeWebViewPlatform.MacOS)
            {
                await pageReadyCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);
                var pageJson = await pageJsonCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);
                using (var messageDocument = JsonDocument.Parse(pageJson))
                {
                    if (!string.Equals(
                            messageDocument.RootElement.GetProperty("kind").GetString(),
                            "page-ready-json",
                            StringComparison.Ordinal) ||
                        messageDocument.RootElement.GetProperty("value").GetInt32() != 42)
                    {
                        throw new InvalidOperationException($"Unexpected macOS JSON web message '{pageJson}'.");
                    }
                }

                await directMacOsMessagesCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);
                await dispatcherExceptionCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);
                await messageAfterDispatcherExceptionCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);

                await VerifyMacOsScriptExecutionAsync(cancellationToken).ConfigureAwait(true);
                await VerifyMacOsHostLifecycleAsync(pages, cancellationToken).ConfigureAwait(true);
                scenario.Evidence.Add("macos-web-message:string-json-direct-foundation-types-envelope-object");
                scenario.Evidence.Add("macos-web-message:dispatcher-exception-isolated-and-recovered");
                scenario.Evidence.Add("macos-script-execution:result-error-cancellation-order");
                scenario.Evidence.Add("macos-native-host:retained-download-block-dispose-recreate-collectible");
            }

            if (platform is NativeWebViewPlatform.Browser or NativeWebViewPlatform.MacOS)
            {
                scenario.Evidence.Add($"message-channel:not-asserted-{platform.ToString().ToLowerInvariant()}-runtime");
            }
            else
            {
                await _webView.PostWebMessageAsStringAsync("native-ping", cancellationToken).ConfigureAwait(true);
                var lastNativeMessage = await WaitForStringResultAsync(
                        _webView.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.lastNativeMessage",
                        "native-ping",
                        cancellationToken)
                    .ConfigureAwait(true);

                scenario.Evidence.Add($"native-message:{lastNativeMessage}");
            }

            scenario.Passed = true;
            scenario.Details = "Embedded control created and validated.";
            AppendLog("[webview] Embedded control validation passed.");
        }
        catch (Exception ex)
        {
            scenario.Passed = false;
            scenario.Details = FormatException(ex);
            scenario.Evidence.Add(ex.GetType().Name);
            AppendLog($"[webview] Failure: {FormatException(ex)}");
        }
        finally
        {
            _webView.CoreWebView2Initialized -= OnInitialized;
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _webView.WebMessageReceived -= OnWebMessageReceived;
            _webView.WebMessageReceived -= ThrowFromWebMessageHandler;
            Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
        }

        return scenario;
    }

    private static bool TryClassifyDirectMacOsMessage(string? message, out string kind)
    {
        kind = message switch
        {
            "direct-native-string" => "string",
            "314159" => "number",
            "false" => "boolean",
            "null" => "null",
            _ => string.Empty,
        };
        if (kind.Length != 0)
            return true;

        if (message is null)
            return false;

        try
        {
            using var document = JsonDocument.Parse(message);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("nativeWebViewVersion", out var version) &&
                version.GetInt32() == 1 &&
                document.RootElement.TryGetProperty("kind", out var envelopeKind) &&
                string.Equals(envelopeKind.GetString(), "json", StringComparison.Ordinal) &&
                document.RootElement.TryGetProperty("payload", out var envelopePayload) &&
                string.Equals(envelopePayload.GetString(), "{\"directEnvelope\":true}", StringComparison.Ordinal))
            {
                kind = "envelope-object";
                return true;
            }

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("kind", out var objectKind) &&
                string.Equals(objectKind.GetString(), "direct-native-object", StringComparison.Ordinal))
            {
                kind = "object";
                return true;
            }

            if (document.RootElement.ValueKind == JsonValueKind.Array &&
                document.RootElement.GetArrayLength() == 2 &&
                string.Equals(document.RootElement[0].GetString(), "direct-native-array", StringComparison.Ordinal) &&
                document.RootElement[1].GetInt32() == 7)
            {
                kind = "array";
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private async Task VerifyMacOsScriptExecutionAsync(CancellationToken cancellationToken)
    {
        var objectResult = await _webView.ExecuteScriptAsync("({ value: 42, ok: true })", cancellationToken).ConfigureAwait(true);
        using (var document = JsonDocument.Parse(objectResult ?? throw new InvalidOperationException("macOS object script returned null.")))
        {
            if (document.RootElement.GetProperty("value").GetInt32() != 42 ||
                !document.RootElement.GetProperty("ok").GetBoolean())
            {
                throw new InvalidOperationException($"Unexpected macOS object script result '{objectResult}'.");
            }
        }

        await _webView.ExecuteScriptAsync("window.__nativeWebViewAwaitOrder = 'complete'", cancellationToken).ConfigureAwait(true);
        var orderedResult = await EvaluateStringAsync(
                _webView.ExecuteScriptAsync,
                "window.__nativeWebViewAwaitOrder",
                cancellationToken)
            .ConfigureAwait(true);
        if (!string.Equals(orderedResult, "complete", StringComparison.Ordinal))
            throw new InvalidOperationException("macOS script completion did not preserve await ordering.");

        try
        {
            _ = await _webView.ExecuteScriptAsync("throw new Error('integration-script-error')", cancellationToken).ConfigureAwait(true);
            throw new InvalidOperationException("macOS JavaScript errors were not propagated.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("integration-script-error", StringComparison.OrdinalIgnoreCase))
        {
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await AssertCanceledAsync(() => _webView.ExecuteScriptAsync("1 + 1", canceled.Token)).ConfigureAwait(true);

        MacOSScriptEvaluationSetupRollbackSnapshot? rollbackSnapshot = null;
        MacOSNativeWebViewHostTestHooks.CancellationRegistrationFactory =
            static (_, _, _) => throw new ObjectDisposedException("integration-cancellation-registration");
        MacOSNativeWebViewHostTestHooks.ScriptEvaluationSetupRolledBack =
            snapshot => rollbackSnapshot = snapshot;
        try
        {
            try
            {
                _ = await _webView.ExecuteScriptAsync("2 + 2", cancellationToken).ConfigureAwait(true);
                throw new InvalidOperationException("macOS evaluation setup did not surface the injected registration failure.");
            }
            catch (ObjectDisposedException ex) when (
                string.Equals(ex.ObjectName, "integration-cancellation-registration", StringComparison.Ordinal))
            {
            }
        }
        finally
        {
            MacOSNativeWebViewHostTestHooks.CancellationRegistrationFactory = null;
            MacOSNativeWebViewHostTestHooks.ScriptEvaluationSetupRolledBack = null;
        }

        if (rollbackSnapshot is not
            {
                PendingEntryRemoved: true,
                ManagedCleanupCompleted: true,
                CreatorBlockReleased: true,
                ManagedOwnershipReleased: true,
                NativeOwnershipCount: 0,
                ManagedHandleReleased: true,
            })
        {
            throw new InvalidOperationException(
                $"macOS evaluation setup rollback left native state behind: {rollbackSnapshot?.ToString() ?? "no snapshot"}.");
        }

        var evaluationAfterSetupFailure = await _webView.ExecuteScriptAsync("6 * 7", cancellationToken).ConfigureAwait(true);
        if (!string.Equals(evaluationAfterSetupFailure, "42", StringComparison.Ordinal))
            throw new InvalidOperationException("macOS evaluation setup did not recover after registration rollback.");

        using var canceledAfterDispatch = new CancellationTokenSource();
        var delayedEvaluation = _webView.ExecuteScriptAsync(
            "(() => { window.__nativeWebViewCanceledEvaluation = 'started'; const deadline = Date.now() + 300; while (Date.now() < deadline) {} window.__nativeWebViewCanceledEvaluation = 'finished'; return true; })()",
            canceledAfterDispatch.Token);
        canceledAfterDispatch.Cancel();
        await AssertCanceledAsync(() => delayedEvaluation).ConfigureAwait(true);
        var cancellationMarker = await WaitForStringResultAsync(
                _webView.ExecuteScriptAsync,
                "window.__nativeWebViewCanceledEvaluation",
                "finished",
                cancellationToken)
            .ConfigureAwait(true);
        if (!string.Equals(cancellationMarker, "finished", StringComparison.Ordinal))
            throw new InvalidOperationException("macOS canceled script did not complete its native callback lifecycle.");
    }

    private async Task VerifyMacOsHostLifecycleAsync(
        IntegrationPageCatalog pages,
        CancellationToken cancellationToken)
    {
        var references = new List<WeakReference>();
        var nativeDispatchCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeCallbackCompletion =
            new TaskCompletionSource<MacOSStartDownloadCompletionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retainedCompletionBlock = IntPtr.Zero;

        MacOSNativeWebViewHostTestHooks.StartDownloadDispatch = (view, request, block) =>
        {
            if (view == IntPtr.Zero || request == IntPtr.Zero || block == IntPtr.Zero)
                throw new InvalidOperationException("macOS direct-download dispatch received an invalid native argument.");
            if (retainedCompletionBlock != IntPtr.Zero)
                throw new InvalidOperationException("macOS direct-download dispatch ran more than once.");

            retainedCompletionBlock = MacOSNativeWebViewHost.RetainStartDownloadCompletionBlockForTests(block);
            if (retainedCompletionBlock == IntPtr.Zero)
                throw new InvalidOperationException("macOS direct-download completion block could not be retained.");

            nativeDispatchCompletion.TrySetResult();
        };
        MacOSNativeWebViewHostTestHooks.StartDownloadCompleted =
            snapshot => nativeCallbackCompletion.TrySetResult(snapshot);

        try
        {
            references.Add(await CreateAndDisposeMacOsWebViewAsync(
                    pages.DelayedDownloadUri,
                    nativeDispatchCompletion.Task,
                    cancellationToken)
                .ConfigureAwait(true));

            if (retainedCompletionBlock == IntPtr.Zero)
                throw new InvalidOperationException("macOS direct-download dispatch did not retain its completion block.");

            var completionBlock = retainedCompletionBlock;
            retainedCompletionBlock = IntPtr.Zero;
            try
            {
                MacOSNativeWebViewHost.InvokeStartDownloadCompletionBlockForTests(completionBlock, IntPtr.Zero);
            }
            finally
            {
                MacOSNativeWebViewHost.ReleaseStartDownloadCompletionBlockForTests(completionBlock);
            }

            var completionSnapshot = await nativeCallbackCompletion.Task
                .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                .ConfigureAwait(true);
            if (!completionSnapshot.HostDisposed || completionSnapshot.DelegateAttached)
            {
                throw new InvalidOperationException(
                    $"macOS direct-download callback escaped disposal isolation: {completionSnapshot}.");
            }
        }
        finally
        {
            if (retainedCompletionBlock != IntPtr.Zero)
            {
                MacOSNativeWebViewHost.ReleaseStartDownloadCompletionBlockForTests(retainedCompletionBlock);
            }

            MacOSNativeWebViewHostTestHooks.StartDownloadDispatch = null;
            MacOSNativeWebViewHostTestHooks.StartDownloadCompleted = null;
        }

        for (var iteration = 1; iteration < 3; iteration++)
        {
            references.Add(await CreateAndDisposeMacOsWebViewAsync(
                    null,
                    null,
                    cancellationToken)
                .ConfigureAwait(true));
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(true);
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        for (var attempt = 0; attempt < 10 && references.Any(static reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        if (references.Any(static reference => reference.IsAlive))
            throw new InvalidOperationException("A disposed macOS native host remained reachable after repeated collection.");
    }

    private async Task<WeakReference> CreateAndDisposeMacOsWebViewAsync(
        Uri? delayedDownloadUri,
        Task? nativeDownloadDispatch,
        CancellationToken cancellationToken)
    {
        var webView = new NativeWebView.Controls.NativeWebView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RenderMode = NativeWebViewRenderMode.Offscreen,
        };
        Grid.SetRow(webView, 1);
        _rootGrid.Children.Add(webView);

        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            await webView.InitializeAsync(cancellationToken).ConfigureAwait(true);
            _ = await webView.ExecuteScriptAsync("1", cancellationToken).ConfigureAwait(true);
            if (delayedDownloadUri is not null)
            {
                if (nativeDownloadDispatch is null)
                    throw new InvalidOperationException("A direct-download dispatch signal is required for this lifecycle check.");

                var downloadUriLiteral = JsonSerializer.Serialize(delayedDownloadUri.AbsoluteUri);
                _ = await webView.ExecuteScriptAsync(
                        $"globalThis.webkit.messageHandlers.nativeWebViewDownload.postMessage('download\\n' + {downloadUriLiteral}); true",
                        cancellationToken)
                    .ConfigureAwait(true);
                await nativeDownloadDispatch
                    .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        finally
        {
            _rootGrid.Children.Remove(webView);
            webView.Dispose();
        }

        return new WeakReference(webView);
    }

    private static async Task AssertCanceledAsync(Func<Task<string?>> action)
    {
        try
        {
            _ = await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        throw new InvalidOperationException("Expected script execution cancellation.");
    }

    private async Task<IntegrationScenarioResult> RunDialogScenarioAsync(
        NativeWebViewPlatform platform,
        IntegrationPageCatalog pages,
        CancellationToken cancellationToken)
    {
        var scenario = new IntegrationScenarioResult { Name = "dialog" };

        using var dialog = new NativeWebDialog();

        if (!IsDesktopPlatform(platform))
        {
            try
            {
                dialog.Show();
                scenario.Passed = false;
                scenario.Details = "Dialog unexpectedly succeeded on a platform that should not support it.";
            }
            catch (PlatformNotSupportedException)
            {
                scenario.Passed = true;
                scenario.Details = "Dialog is unsupported on this platform, as expected.";
                scenario.Evidence.Add("unsupported");
            }
            catch (Exception ex)
            {
                scenario.Passed = false;
                scenario.Details = FormatException(ex);
                scenario.Evidence.Add(ex.GetType().Name);
            }

            AppendLog($"[dialog] {scenario.Details}");
            return scenario;
        }

        var navigationCompletion = new TaskCompletionSource<NativeWebViewNavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageToNativeCompletion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnNavigationCompleted(object? sender, NativeWebViewNavigationCompletedEventArgs e)
        {
            if (e.Uri is not null && AreSameUri(e.Uri, pages.DialogPageUri))
            {
                navigationCompletion.TrySetResult(e);
            }
        }

        void OnWebMessageReceived(object? sender, NativeWebViewMessageReceivedEventArgs e)
        {
            var message = e.Message ?? e.Json;
            if (string.Equals(message, "dialog-script-ping", StringComparison.Ordinal))
            {
                pageToNativeCompletion.TrySetResult(message!);
            }
        }

        dialog.NavigationCompleted += OnNavigationCompleted;
        dialog.WebMessageReceived += OnWebMessageReceived;

        try
        {
            AppendLog("[dialog] Showing native dialog.");

            dialog.Show(new NativeWebDialogShowOptions
            {
                Title = "NativeWebView Integration Dialog",
                Width = 960,
                Height = 720,
                CenterOnParent = true,
            });

            if (!dialog.IsVisible)
            {
                throw new InvalidOperationException("Dialog did not become visible.");
            }

            RequireNativeHandle(dialog.TryGetDialogHandle(out var dialogHandle), dialogHandle, "dialog");
            RequireNativeHandle(dialog.TryGetHostWindowHandle(out var hostHandle), hostHandle, "host");

            scenario.Evidence.Add($"dialog-handle:{dialogHandle.HandleDescriptor}");
            scenario.Evidence.Add($"host-handle:{hostHandle.HandleDescriptor}");

            dialog.Navigate(pages.DialogPageUri);

            if (platform == NativeWebViewPlatform.MacOS)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(true);

                var outputPath = Path.Combine(
                    IntegrationPlatformContext.GetArtifactsDirectory(platform),
                    "macos-dialog-proof.pdf");

                var printResult = await dialog.PrintAsync(
                        new NativeWebViewPrintSettings { OutputPath = outputPath },
                        cancellationToken)
                    .ConfigureAwait(true);

                if (printResult.Status != NativeWebViewPrintStatus.Success ||
                    !File.Exists(outputPath) ||
                    new FileInfo(outputPath).Length == 0)
                {
                    throw new InvalidOperationException(
                        $"macOS dialog did not produce a runtime proof. Print status={printResult.Status}, error={printResult.ErrorMessage ?? "<none>"}");
                }

                scenario.Evidence.Add($"printed:{outputPath}");
            }
            else
            {
                var location = await WaitForUriResultAsync(
                        () => dialog.CurrentUrl,
                        dialog.ExecuteScriptAsync,
                        "window.location.href",
                        pages.DialogPageUri,
                        cancellationToken)
                    .ConfigureAwait(true);

                scenario.Evidence.Add($"location:{location}");

                if (navigationCompletion.Task.IsCompletedSuccessfully)
                {
                    var navigationArgs = await navigationCompletion.Task.ConfigureAwait(true);
                    if (!navigationArgs.IsSuccess)
                    {
                        throw new InvalidOperationException($"Dialog navigation failed: {navigationArgs.Error ?? "unknown error"}");
                    }

                    scenario.Evidence.Add($"navigated:{navigationArgs.Uri}");
                }

                await WaitForBooleanResultAsync(
                        dialog.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.pageReady",
                        cancellationToken)
                    .ConfigureAwait(true);

                await dialog.ExecuteScriptAsync(
                        "if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') { window.chrome.webview.postMessage('dialog-script-ping'); }",
                        cancellationToken)
                    .ConfigureAwait(true);

                await pageToNativeCompletion.Task
                    .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                    .ConfigureAwait(true);

                await dialog.PostWebMessageAsStringAsync("dialog-native-ping", cancellationToken).ConfigureAwait(true);
                var lastNativeMessage = await WaitForStringResultAsync(
                        dialog.ExecuteScriptAsync,
                        "window.__nativeWebViewIntegrationState && window.__nativeWebViewIntegrationState.lastNativeMessage",
                        "dialog-native-ping",
                        cancellationToken)
                    .ConfigureAwait(true);

                scenario.Evidence.Add($"native-message:{lastNativeMessage}");
            }

            dialog.Close();
            scenario.Passed = true;
            scenario.Details = "Dialog runtime verified.";
            AppendLog("[dialog] Dialog validation passed.");
        }
        catch (Exception ex)
        {
            scenario.Passed = false;
            scenario.Details = FormatException(ex);
            scenario.Evidence.Add(ex.GetType().Name);
            AppendLog($"[dialog] Failure: {FormatException(ex)}");
        }
        finally
        {
            dialog.NavigationCompleted -= OnNavigationCompleted;
            dialog.WebMessageReceived -= OnWebMessageReceived;
        }

        return scenario;
    }

    private async Task<IntegrationScenarioResult> RunAuthenticationScenarioAsync(
        NativeWebViewPlatform platform,
        IntegrationPageCatalog pages,
        CancellationToken cancellationToken)
    {
        var scenario = new IntegrationScenarioResult { Name = "auth" };

        if (platform == NativeWebViewPlatform.MacOS)
        {
            scenario.Passed = true;
            scenario.Details = "Skipped on macOS until the native dialog backend surfaces WKWebView redirect callbacks.";
            scenario.Evidence.Add("skipped:macos-auth-redirect-callbacks");
            AppendLog("[auth] Skipping macOS runtime auth validation because redirect callbacks are not surfaced by the current native dialog backend.");
            return scenario;
        }

        try
        {
            AppendLog("[auth] Starting authentication flow.");

            using var broker = new WebAuthenticationBroker();
            using var authCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            authCancellationSource.CancelAfter(TimeSpan.FromSeconds(60));
            var result = await broker.AuthenticateAsync(
                    pages.AuthRequestUri,
                    pages.AuthCallbackUri,
                    WebAuthenticationOptions.UseTitle,
                    authCancellationSource.Token)
                .ConfigureAwait(true);

            if (result.ResponseStatus != WebAuthenticationStatus.Success)
            {
                throw new InvalidOperationException(
                    $"Authentication did not succeed. Status={result.ResponseStatus}, error={result.ResponseErrorDetail}.");
            }

            if (!Uri.TryCreate(result.ResponseData, UriKind.Absolute, out var responseUri) ||
                responseUri is null ||
                !MatchesCallbackPath(responseUri, pages.AuthCallbackUri))
            {
                throw new InvalidOperationException($"Unexpected authentication callback '{result.ResponseData ?? "<null>"}'.");
            }

            if (!responseUri.Query.Contains("token=integration-ok", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Authentication callback was missing the expected token.");
            }

            scenario.Passed = true;
            scenario.Details = "Authentication broker completed an interactive redirect.";
            scenario.Evidence.Add($"response:{result.ResponseData}");
            scenario.Evidence.Add($"platform:{platform}");
            AppendLog("[auth] Authentication validation passed.");
        }
        catch (Exception ex)
        {
            scenario.Passed = false;
            scenario.Details = FormatException(ex);
            scenario.Evidence.Add(ex.GetType().Name);
            AppendLog($"[auth] Failure: {FormatException(ex)}");
        }

        return scenario;
    }

    private void AppendLog(string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss}] {message}";
        _logBuffer.AppendLine(line);
        _logBox.Text = _logBuffer.ToString();
        IntegrationLog.Write(line);
    }

    private void UpdateStatus(string message)
    {
        _statusBlock.Text = message;
    }

    private static bool IsDesktopPlatform(NativeWebViewPlatform platform)
    {
        return platform is NativeWebViewPlatform.Windows or NativeWebViewPlatform.MacOS or NativeWebViewPlatform.Linux;
    }

    private async Task<NativeWebViewRenderFrame?> WaitForRenderFrameAsync(CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var frame = await _webView.CaptureRenderFrameAsync(cancellationToken).ConfigureAwait(true);
                if (frame is not null && !frame.IsSynthetic)
                {
                    return frame;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        return null;
    }

    private static void RequireNativeHandle(bool success, NativePlatformHandle handle, string description)
    {
        if (!success || handle.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Expected a real native {description} handle.");
        }
    }

    private static async Task<bool> EvaluateBooleanAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        CancellationToken cancellationToken)
    {
        var result = await executeScriptAsync(script, cancellationToken).ConfigureAwait(true);
        var parsed = ParseJsonLike(result);

        return parsed switch
        {
            bool booleanValue => booleanValue,
            string stringValue when bool.TryParse(stringValue, out var booleanValue) => booleanValue,
            string stringValue when stringValue == "1" => true,
            string stringValue when stringValue == "0" => false,
            _ => false,
        };
    }

    private static async Task<string?> EvaluateStringAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        CancellationToken cancellationToken)
    {
        var result = await executeScriptAsync(script, cancellationToken).ConfigureAwait(true);
        var parsed = ParseJsonLike(result);
        return parsed?.ToString();
    }

    private static async Task<string> WaitForStringResultAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var value = await EvaluateStringAsync(executeScriptAsync, script, cancellationToken).ConfigureAwait(true);
                if (string.Equals(value, expectedValue, StringComparison.Ordinal))
                {
                    return value!;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException($"Timed out waiting for script value '{expectedValue}'.");
    }

    private static async Task<string> WaitForUriResultAsync(
        Func<Uri?> currentUriProvider,
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        Uri expectedValue,
        CancellationToken cancellationToken,
        int maxAttempts = 100)
    {
        Exception? lastException = null;
        string? lastObservedValue = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentUri = currentUriProvider();
            if (currentUri is not null && AreSameUri(currentUri, expectedValue))
            {
                return currentUri.AbsoluteUri;
            }

            try
            {
                var value = await EvaluateStringAsync(executeScriptAsync, script, cancellationToken).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    lastObservedValue = value;

                    if (Uri.TryCreate(value, UriKind.Absolute, out var actualUri) &&
                        actualUri is not null &&
                        AreSameUri(actualUri, expectedValue))
                    {
                        return value;
                    }
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        var observed = currentUriProvider()?.AbsoluteUri ?? lastObservedValue ?? "<null>";
        throw new InvalidOperationException(
            $"Timed out waiting for URI '{expectedValue.AbsoluteUri}'. Last observed value was '{observed}'.");
    }

    private static async Task WaitForBooleanResultAsync(
        Func<string, CancellationToken, Task<string?>> executeScriptAsync,
        string script,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await EvaluateBooleanAsync(executeScriptAsync, script, cancellationToken).ConfigureAwait(true))
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(true);
        }

        if (lastException is not null)
        {
            throw lastException;
        }

        throw new InvalidOperationException("Timed out waiting for script boolean result to become true.");
    }

    private static object? ParseJsonLike(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.String => document.RootElement.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => document.RootElement.ToString(),
                JsonValueKind.Object => document.RootElement.GetRawText(),
                JsonValueKind.Array => document.RootElement.GetRawText(),
                JsonValueKind.Null => null,
                _ => trimmed,
            };
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static bool AreSameUri(Uri actual, Uri expected)
    {
        return Uri.Compare(
            actual,
            expected,
            UriComponents.SchemeAndServer | UriComponents.PathAndQuery,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool MatchesCallbackPath(Uri actual, Uri expected)
    {
        return Uri.Compare(
            actual,
            expected,
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static string FormatException(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message}";
    }

    private static void TryShutdownIfDesktop(int exitCode)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Environment.ExitCode = exitCode;
            desktop.Shutdown(exitCode);
        }
    }
}
