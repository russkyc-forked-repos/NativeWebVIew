using System.Runtime.InteropServices;
using NativeWebView.Controls;
using NativeWebView.Core;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.macOS;

namespace NativeWebView.Core.Tests;

public sealed class SnapshotSupportTests
{
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    public void NativeWebViewSnapshot_CopiesCallerOwnedData()
    {
        var source = (byte[])ValidPng.Clone();

        var snapshot = new NativeWebViewSnapshot(source);
        source[0] = 0;

        Assert.Equal("image/png", snapshot.ContentType);
        Assert.Equal(ValidPng, snapshot.PngData.ToArray());
    }

    [Fact]
    public void NativeWebViewSnapshot_RejectsEmptyData()
    {
        Assert.Throws<ArgumentException>(() => new NativeWebViewSnapshot([]));
    }

    [Fact]
    public void NativeWebViewSnapshot_RejectsInvalidOrTruncatedPng()
    {
        Assert.Throws<ArgumentException>(() => new NativeWebViewSnapshot(new byte[33]));
        Assert.Throws<ArgumentException>(() => new NativeWebViewSnapshot([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
        Assert.Throws<ArgumentException>(() => new NativeWebViewSnapshot(ValidPng[..^12]));
    }

    [Fact]
    public void NativeWebViewSnapshot_RejectsCorruptChunkDataAndTrailingBytes()
    {
        var corruptImageData = (byte[])ValidPng.Clone();
        corruptImageData[45] ^= 0x01;
        var trailingData = new byte[ValidPng.Length + 1];
        ValidPng.CopyTo(trailingData, 0);

        Assert.Throws<ArgumentException>(() => new NativeWebViewSnapshot(corruptImageData));
        Assert.Throws<ArgumentException>(() => new NativeWebViewSnapshot(trailingData));
    }

    [Fact]
    public async Task NativeWebViewSnapshotCapture_ExposesCompletedResult()
    {
        var snapshot = new NativeWebViewSnapshot(ValidPng);

        var capture = NativeWebViewSnapshotCapture.FromResult(snapshot);

        Assert.True(capture.CaptureStarted.IsCompletedSuccessfully);
        Assert.Same(snapshot, await capture.Completion);
    }

    [Fact]
    public void NativeWebViewSnapshotCapture_RejectsNullTasks()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NativeWebViewSnapshotCapture(null!, Task.FromResult<NativeWebViewSnapshot?>(null)));
        Assert.Throws<ArgumentNullException>(() =>
            new NativeWebViewSnapshotCapture(Task.CompletedTask, null!));
    }

    [Fact]
    public async Task DefaultSnapshotProvider_WaitsForCompletionBeforeReportingCaptureStarted()
    {
        var completion = new TaskCompletionSource<NativeWebViewSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        INativeWebViewSnapshotProvider provider = new TestSnapshotProvider(_ => completion.Task);

        var capture = provider.BeginCaptureSnapshot();

        Assert.False(capture.CaptureStarted.IsCompleted);
        completion.SetResult(null);
        Assert.Null(await capture.Completion);
        await capture.CaptureStarted;
    }

    [Fact]
    public async Task DefaultSnapshotProvider_NormalizesAsynchronousFailures()
    {
        INativeWebViewSnapshotProvider provider = new TestSnapshotProvider(
            _ => Task.FromException<NativeWebViewSnapshot?>(new InvalidOperationException("Capture failure.")));

        var capture = provider.BeginCaptureSnapshot();

        Assert.Null(await capture.Completion);
        await capture.CaptureStarted;
    }

    [Fact]
    public void DesktopEmbeddedBackends_AdvertiseSnapshotCapability()
    {
        using var windows = new WindowsNativeWebViewBackend();
        using var linux = new LinuxNativeWebViewBackend();
        using var macOS = new MacOSNativeWebViewBackend();

        Assert.True(windows.Features.Supports(NativeWebViewFeature.EmbeddedSnapshotCapture));
        Assert.True(linux.Features.Supports(NativeWebViewFeature.EmbeddedSnapshotCapture));
        Assert.True(macOS.Features.Supports(NativeWebViewFeature.EmbeddedSnapshotCapture));
    }

    [Fact]
    public void StatusTextCapability_IsAvailableOnWindowsAndLinuxOnly()
    {
        using var windows = new WindowsNativeWebViewBackend();
        using var linux = new LinuxNativeWebViewBackend();
        using var macOS = new MacOSNativeWebViewBackend();

        Assert.True(windows.Features.Supports(NativeWebViewFeature.StatusText));
        Assert.True(linux.Features.Supports(NativeWebViewFeature.StatusText));
        Assert.False(macOS.Features.Supports(NativeWebViewFeature.StatusText));
        Assert.IsAssignableFrom<INativeWebViewStatusTextProvider>(windows);
        Assert.IsAssignableFrom<INativeWebViewStatusTextProvider>(linux);
    }

    [Fact]
    public void NativeZoomNotificationCapability_IsAvailableOnDesktopBackends()
    {
        using var windows = new WindowsNativeWebViewBackend();
        using var linux = new LinuxNativeWebViewBackend();
        using var macOS = new MacOSNativeWebViewBackend();

        Assert.True(windows.Features.Supports(NativeWebViewFeature.ZoomFactorChangeNotification));
        Assert.True(linux.Features.Supports(NativeWebViewFeature.ZoomFactorChangeNotification));
        Assert.True(macOS.Features.Supports(NativeWebViewFeature.ZoomFactorChangeNotification));
        Assert.IsAssignableFrom<INativeWebViewZoomFactorProvider>(windows);
        Assert.IsAssignableFrom<INativeWebViewZoomFactorProvider>(linux);
        Assert.IsAssignableFrom<INativeWebViewZoomFactorProvider>(macOS);
    }

    [Theory]
    [InlineData(false, 7, 7, true, true)]
    [InlineData(true, 7, 7, true, false)]
    [InlineData(false, 7, 8, true, false)]
    [InlineData(false, 7, 7, false, false)]
    public void WindowsSnapshotCapture_RejectsStaleCompletions(
        bool isDisposed,
        int captureGeneration,
        int currentGeneration,
        bool isSameWebView,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsNativeWebViewBackend.IsSnapshotCaptureCurrent(
                isDisposed,
                captureGeneration,
                currentGeneration,
                isSameWebView));
    }

    [Fact]
    public async Task LinuxCancellationState_PreservesCallerToken()
    {
        using var cancellationSource = new CancellationTokenSource();
        var completion = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new LinuxCancellationState<string?>(completion, cancellationSource.Token);

        state.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => completion.Task);
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task LinuxCancellationState_PreventsCanceledQueuedWorkFromStarting()
    {
        using var cancellationSource = new CancellationTokenSource();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new LinuxCancellationState<bool>(completion, cancellationSource.Token);

        cancellationSource.Cancel();
        state.Cancel();

        Assert.False(state.TryBeginExecution());
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => completion.Task);
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task LinuxCancellationState_DoesNotCancelWorkAfterExecutionStarts()
    {
        using var cancellationSource = new CancellationTokenSource();
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new LinuxCancellationState<bool>(completion, cancellationSource.Token);

        Assert.True(state.TryBeginExecution());
        cancellationSource.Cancel();
        state.Cancel();
        completion.TrySetResult(true);

        Assert.True(await completion.Task);
    }

    [Fact]
    public async Task LinuxJavaScriptRequest_CancellationRetainsNativeCallbackStateUntilDisposed()
    {
        using var cancellationSource = new CancellationTokenSource();
        var nativeCancellationCount = 0;
        var nativeReleaseCount = 0;
        using var request = new LinuxJavaScriptRequest(
            cancellationSource.Token,
            new IntPtr(42),
            _ => nativeCancellationCount++,
            _ => nativeReleaseCount++);
        var userData = request.UserData;

        cancellationSource.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => request.Completion);
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Equal(1, nativeCancellationCount);
        Assert.Equal(0, nativeReleaseCount);
        Assert.False(request.IsDisposed);
        Assert.Same(request, GCHandle.FromIntPtr(userData).Target);

        request.Dispose();

        Assert.Equal(1, nativeReleaseCount);
    }

    [Fact]
    public void LinuxGtkEnqueue_ThrowsWhenIdleSourceRegistrationFails()
    {
        var actionInvoked = false;

        Assert.Throws<InvalidOperationException>(() =>
            LinuxNativeInterop.EnqueueOnGtkThread(
                () => actionInvoked = true,
                static _ => 0));

        Assert.False(actionInvoked);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void LinuxMouseStatus_IsRejectedDuringOrAfterTeardown(
        bool isDisposed,
        bool isRuntimeInitialized,
        bool expected)
    {
        Assert.Equal(
            expected,
            LinuxNativeWebViewBackend.CanAcceptMouseStatus(isDisposed, isRuntimeInitialized));
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void WindowsRuntime_IsNotReadyUntilInitializationAndBothNativeObjectsExist(
        bool isRuntimeInitialized,
        bool hasCoreWebView,
        bool hasController,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsNativeWebViewBackend.IsRuntimeReady(
                isRuntimeInitialized,
                hasCoreWebView,
                hasController));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void LinuxRuntime_IsNotReadyUntilInitializationAndNativeViewExist(
        bool isRuntimeInitialized,
        bool hasWebView,
        bool expected)
    {
        Assert.Equal(
            expected,
            LinuxNativeWebViewBackend.IsRuntimeReady(isRuntimeInitialized, hasWebView));
    }

    [Fact]
    public void RuntimeNavigationReplayState_DoesNotLetOlderCompletionReplaceNewerRequest()
    {
        var firstUri = new Uri("https://example.test/first");
        var secondUri = new Uri("https://example.test/second");
        var state = new RuntimeNavigationReplayState();
        var firstRequest = state.SetRequested(firstUri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, firstUri, isRedirected: false);

        var secondRequest = state.SetRequested(secondUri, isRuntimeReady: true);
        var promoted = state.CompleteNavigation(1, firstUri);

        Assert.False(promoted);
        Assert.Equal(secondUri, state.ReplayUri);
        Assert.False(state.IsCurrent(firstRequest, isDisposed: false, isRuntimeReady: true));
        Assert.True(state.IsCurrent(secondRequest, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_DoesNotLetOlderCompletionReplaceCompletedNewerNavigation()
    {
        var firstUri = new Uri("https://example.test/first");
        var secondUri = new Uri("https://example.test/second");
        var state = new RuntimeNavigationReplayState();
        state.SetRequested(firstUri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, firstUri, isRedirected: false);
        state.SetRequested(secondUri, isRuntimeReady: true);
        state.TrackNavigationStarted(2, secondUri, isRedirected: false);

        Assert.True(state.CompleteNavigation(2, secondUri));
        Assert.False(state.CompleteNavigation(1, firstUri));

        Assert.Equal(secondUri, state.ReplayUri);
    }

    [Fact]
    public void RuntimeNavigationReplayState_PreservesPathAndQueryCasingWhenMatchingRequests()
    {
        var requestedUri = new Uri("https://example.test/Account?Token=A");
        var differentUri = new Uri("https://example.test/account?token=a");
        var state = new RuntimeNavigationReplayState();
        var request = state.SetRequested(requestedUri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, differentUri, isRedirected: false);

        Assert.False(state.CompleteNavigation(1, differentUri));

        Assert.Equal(requestedUri, state.ReplayUri);
        Assert.True(state.IsCurrent(request, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_PromotesOnlyMatchingRequestCompletion()
    {
        var requestedUri = new Uri("https://example.test/login");
        var reachedUri = new Uri("https://example.test/account");
        var state = new RuntimeNavigationReplayState();
        var request = state.SetRequested(requestedUri, isRuntimeReady: true);
        state.TrackNavigationStarted(42, requestedUri, isRedirected: false);

        var promoted = state.CompleteNavigation(42, reachedUri);

        Assert.True(promoted);
        Assert.Equal(reachedUri, state.ReplayUri);
        Assert.False(state.IsCurrent(request, isDisposed: false, isRuntimeReady: true));
        var replay = state.PublishRuntimeReady();
        Assert.Equal(reachedUri, replay.Uri);
        Assert.True(state.IsCurrent(replay, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_RetainsRequestAcrossRuntimeDestruction()
    {
        var uri = new Uri("https://example.test/pending");
        var state = new RuntimeNavigationReplayState();
        var request = state.SetRequested(uri, isRuntimeReady: true);
        state.TrackNavigationStarted(7, uri, isRedirected: false);

        state.RuntimeDestroyed();
        var replay = state.PublishRuntimeReady();

        Assert.Equal(request.Version, replay.Version);
        Assert.Equal(uri, replay.Uri);
        Assert.True(state.IsCurrent(replay, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_MatchesOlderFailureByItsStartedUri()
    {
        var firstUri = new Uri("https://example.test/first");
        var secondUri = new Uri("https://example.test/second");
        var state = new RuntimeNavigationReplayState();
        state.SetRequested(firstUri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, firstUri, isRedirected: false);
        var secondRequest = state.SetRequested(secondUri, isRuntimeReady: true);
        state.TrackNavigationStarted(2, secondUri, isRedirected: false);

        var promoted = state.CompleteNavigation(firstUri, fallbackNavigationId: 2, reachedUri: firstUri);

        Assert.False(promoted);
        Assert.Equal(secondUri, state.ReplayUri);
        Assert.True(state.IsCurrent(secondRequest, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_MatchesOlderRedirectedFailureByItsLatestUri()
    {
        var firstUri = new Uri("https://example.test/first");
        var redirectedUri = new Uri("https://identity.example.test/sign-in");
        var secondUri = new Uri("https://example.test/second");
        var state = new RuntimeNavigationReplayState();
        state.SetRequested(firstUri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, firstUri, isRedirected: false);
        state.TrackNavigationStarted(1, redirectedUri, isRedirected: true);
        var secondRequest = state.SetRequested(secondUri, isRuntimeReady: true);
        state.TrackNavigationStarted(2, secondUri, isRedirected: false);

        var promoted = state.CompleteNavigation(
            redirectedUri,
            fallbackNavigationId: 2,
            reachedUri: redirectedUri);

        Assert.False(promoted);
        Assert.Equal(secondUri, state.ReplayUri);
        Assert.True(state.IsCurrent(secondRequest, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_DoesNotCompleteMismatchedFallbackWithMultipleNavigations()
    {
        var firstUri = new Uri("https://example.test/first");
        var secondUri = new Uri("https://example.test/second");
        var unknownUri = new Uri("https://example.test/unknown");
        var state = new RuntimeNavigationReplayState();
        state.SetRequested(firstUri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, firstUri, isRedirected: false);
        var secondRequest = state.SetRequested(secondUri, isRuntimeReady: true);
        state.TrackNavigationStarted(2, secondUri, isRedirected: false);

        var promoted = state.CompleteNavigation(
            unknownUri,
            fallbackNavigationId: 2,
            reachedUri: unknownUri);

        Assert.False(promoted);
        Assert.Equal(secondUri, state.ReplayUri);
        Assert.True(state.IsCurrent(secondRequest, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_UsesSoleNavigationAsFailureFallback()
    {
        var requestedUri = new Uri("https://example.test/requested");
        var failingUri = new Uri("https://example.test/canonicalized");
        var state = new RuntimeNavigationReplayState();
        var request = state.SetRequested(requestedUri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, requestedUri, isRedirected: false);

        var promoted = state.CompleteNavigation(
            failingUri,
            fallbackNavigationId: 1,
            reachedUri: failingUri);

        Assert.True(promoted);
        Assert.Equal(failingUri, state.ReplayUri);
        Assert.False(state.IsCurrent(request, isDisposed: false, isRuntimeReady: true));
    }

    [Fact]
    public void RuntimeNavigationReplayState_PrefersCurrentFailureWhenUrisMatch()
    {
        var uri = new Uri("https://example.test/login");
        var state = new RuntimeNavigationReplayState();
        state.SetRequested(uri, isRuntimeReady: true);
        state.TrackNavigationStarted(1, uri, isRedirected: false);
        var currentRequest = state.SetRequested(uri, isRuntimeReady: true);
        state.TrackNavigationStarted(2, uri, isRedirected: false);

        var promoted = state.CompleteNavigation(uri, fallbackNavigationId: 2, reachedUri: uri);

        Assert.True(promoted);
        Assert.False(state.IsCurrent(currentRequest, isDisposed: false, isRuntimeReady: true));
        Assert.Equal(uri, state.ReplayUri);
    }

    [Fact]
    public void LinuxRuntimeNavigationLifecycle_DoesNotFinishNewNavigationForOlderFailure()
    {
        var lifecycle = new LinuxRuntimeNavigationLifecycle();
        var failedNavigationId = lifecycle.StartNavigation();
        Assert.Equal(failedNavigationId, lifecycle.FailNavigation());
        var currentNavigationId = lifecycle.StartNavigation();

        Assert.False(lifecycle.TryFinishNavigation(out _));
        Assert.True(lifecycle.TryFinishNavigation(out var finishedNavigationId));
        Assert.Equal(currentNavigationId, finishedNavigationId);
    }

    [Fact]
    public void LinuxRuntimeNavigationLifecycle_DropsFailedTerminalStateWhenRuntimeIsDestroyed()
    {
        var lifecycle = new LinuxRuntimeNavigationLifecycle();
        lifecycle.StartNavigation();
        lifecycle.FailNavigation();

        lifecycle.RuntimeDestroyed();
        var currentNavigationId = lifecycle.StartNavigation();

        Assert.True(lifecycle.TryFinishNavigation(out var finishedNavigationId));
        Assert.Equal(currentNavigationId, finishedNavigationId);
    }

    [Fact]
    public void PendingNativeOperationRegistry_SnapshotSurvivesConcurrentRemoval()
    {
        var registry = new PendingNativeOperationRegistry<object>();
        var first = new object();
        var second = new object();
        registry.Add(first);
        registry.Add(second);

        var snapshot = registry.Snapshot();
        registry.Remove(first);

        Assert.Equal(2, snapshot.Length);
        Assert.Contains(first, snapshot);
        Assert.Contains(second, snapshot);
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void PendingNativeOperationRegistry_CloseRejectsLaterRegistration()
    {
        var registry = new PendingNativeOperationRegistry<object>();
        var registered = new object();
        registry.Add(registered);

        var snapshot = registry.CloseAndSnapshot();

        Assert.Single(snapshot);
        Assert.Same(registered, snapshot[0]);
        Assert.False(registry.TryAdd(new object()));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new object()));
    }

    [Fact]
    public void StatusDispatchQueue_CoalescesToLatestNotification()
    {
        var queue = new NativeWebViewStatusDispatchQueue();
        var first = new NativeWebViewStatusTextChangedEventArgs("https://example.test/first");
        var second = new NativeWebViewStatusTextChangedEventArgs("https://example.test/second");

        Assert.True(queue.TryQueue(first, out var generation));
        Assert.False(queue.TryQueue(second, out var coalescedGeneration));

        Assert.Equal(generation, coalescedGeneration);
        Assert.Same(second, queue.TakeLatest(generation));
        Assert.Null(queue.TakeLatest(generation));
    }

    [Fact]
    public void StatusDispatchQueue_InvalidationRejectsStaleCallbackWithoutClearingNewWork()
    {
        var queue = new NativeWebViewStatusDispatchQueue();
        var stale = new NativeWebViewStatusTextChangedEventArgs("https://example.test/stale");
        var current = new NativeWebViewStatusTextChangedEventArgs("https://example.test/current");
        Assert.True(queue.TryQueue(stale, out var staleGeneration));

        queue.Invalidate();
        Assert.True(queue.TryQueue(current, out var currentGeneration));

        Assert.Null(queue.TakeLatest(staleGeneration));
        Assert.Same(current, queue.TakeLatest(currentGeneration));
    }

    [Fact]
    public void LinuxSignalCleanup_ContinuesAfterIndividualDisposalFailure()
    {
        var first = new TestDisposable(throwOnDispose: true);
        var second = new TestDisposable(throwOnDispose: false);

        LinuxNativeWebViewBackend.DisposeSubscriptions([first, second]);

        Assert.True(first.WasDisposed);
        Assert.True(second.WasDisposed);
    }

    [Fact]
    public void StatusTextNormalizer_TrimsAndBoundsPageControlledText()
    {
        var oversizedStatus = $"  {new string('x', NativeWebViewStatusTextNormalizer.MaximumLength + 100)}  ";

        var normalized = NativeWebViewStatusTextNormalizer.Normalize(oversizedStatus);

        Assert.NotNull(normalized);
        Assert.Equal(NativeWebViewStatusTextNormalizer.MaximumLength, normalized.Length);
        Assert.All(normalized, character => Assert.Equal('x', character));
        Assert.Null(NativeWebViewStatusTextNormalizer.Normalize("   "));
        Assert.Null(NativeWebViewStatusTextNormalizer.Normalize(null));
    }

    [Fact]
    public void StatusTextNormalizer_DoesNotSplitSurrogatePairAtLengthBoundary()
    {
        var prefix = new string('x', NativeWebViewStatusTextNormalizer.MaximumLength - 1);
        var oversizedStatus = $"{prefix}\U0001F600tail";

        var normalized = NativeWebViewStatusTextNormalizer.Normalize(oversizedStatus);

        Assert.Equal(prefix, normalized);
    }

    private sealed class TestSnapshotProvider(
        Func<CancellationToken, Task<NativeWebViewSnapshot?>> capture) : INativeWebViewSnapshotProvider
    {
        public Task<NativeWebViewSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default) =>
            capture(cancellationToken);
    }

    private sealed class TestDisposable(bool throwOnDispose) : IDisposable
    {
        public bool WasDisposed { get; private set; }

        public void Dispose()
        {
            WasDisposed = true;
            if (throwOnDispose)
                throw new InvalidOperationException("Test disposal failure.");
        }
    }
}
