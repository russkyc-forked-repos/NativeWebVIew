using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NativeWebView.Core;
using NativeWebView.Platform.Windows;
using NativeWebView.Platform.Linux;
using NativeWebView.Platform.macOS;
using NativeWebView.Controls;
using NativeWebViewControl = NativeWebView.Controls.NativeWebView;

namespace NativeWebView.Core.Tests;

public sealed class InstanceConfigurationTests
{
    [Fact]
    public void NativeWebViewInstance_AppliesInitialConfigurationToBackend()
    {
        using var backend = new ConfigurableBackend();
        var configuration = CreateScriptConfiguration("initial");

        using var instance = new NativeWebViewInstance(backend, configuration);
        using var presenter = new NativeWebViewControl(instance);

        Assert.Equal(1, backend.ApplyCount);
        Assert.Equal("initial", Assert.Single(backend.LastConfiguration!.DocumentStartScripts).Source);
        configuration.DocumentStartScripts.Clear();
        Assert.Equal("initial", Assert.Single(presenter.InstanceConfiguration.DocumentStartScripts).Source);
        Assert.Equal("initial", Assert.Single(backend.LastConfiguration.DocumentStartScripts).Source);
    }

    [Fact]
    public void NativeWebViewInstance_InitialConfigurationFailureDisposesTransferredBackend()
    {
        var backend = new ConfigurableBackend
        {
            ThrowOnApply = true,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new NativeWebViewInstance(backend, CreateScriptConfiguration("rejected")));

        Assert.Equal("Rejected test configuration.", exception.Message);
        Assert.Equal(1, backend.DisposeCount);
        Assert.Equal(0, backend.EventSubscriberCount);
    }

    [Fact]
    public void NativeWebViewInstance_ConstructionCleanupFailureDoesNotReplaceConfigurationFailure()
    {
        var backend = new ConfigurableBackend
        {
            ThrowOnApply = true,
            ThrowOnDispose = true,
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new NativeWebViewInstance(backend, CreateScriptConfiguration("rejected")));

        Assert.Equal("Rejected test configuration.", exception.Message);
        var cleanupException = Assert.IsType<InvalidOperationException>(
            exception.Data[NativeWebViewInstance.ConstructionCleanupExceptionDataKey]);
        Assert.Equal("Rejected test disposal.", cleanupException.Message);
        Assert.Equal(1, backend.DisposeCount);
        Assert.Equal(0, backend.EventSubscriberCount);
    }

    [Fact]
    public void NativeWebViewInstance_AppliesLateConfigurationToExistingPresenterBackendOnce()
    {
        using var backend = new ConfigurableBackend();
        using var instance = new NativeWebViewInstance(backend);
        using var presenter = new NativeWebViewControl(instance);
        var applyCountBeforeUpdate = backend.ApplyCount;

        instance.ApplyInstanceConfiguration(CreateScriptConfiguration("late"));

        Assert.Equal(applyCountBeforeUpdate + 1, backend.ApplyCount);
        Assert.Equal("late", Assert.Single(backend.LastConfiguration!.DocumentStartScripts).Source);
    }

    [Fact]
    public void NativeWebViewInstance_PropagatesDirectDocumentStartScriptCollectionMutations()
    {
        using var backend = new ConfigurableBackend();
        using var instance = new NativeWebViewInstance(backend);
        using var presenter = new NativeWebViewControl(instance);

        presenter.InstanceConfiguration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("first"));
        Assert.Equal("first", Assert.Single(backend.LastConfiguration!.DocumentStartScripts).Source);

        presenter.InstanceConfiguration.DocumentStartScripts[0] = new NativeWebViewDocumentStartScript("replacement");
        Assert.Equal("replacement", Assert.Single(backend.LastConfiguration.DocumentStartScripts).Source);

        presenter.InstanceConfiguration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("second"));
        Assert.Collection(
            backend.LastConfiguration.DocumentStartScripts,
            script => Assert.Equal("replacement", script.Source),
            script => Assert.Equal("second", script.Source));

        presenter.InstanceConfiguration.DocumentStartScripts.RemoveAt(0);
        Assert.Equal("second", Assert.Single(backend.LastConfiguration.DocumentStartScripts).Source);

        presenter.InstanceConfiguration.DocumentStartScripts.Clear();
        Assert.Empty(backend.LastConfiguration.DocumentStartScripts);
    }

    [Fact]
    public async Task NativeWebViewInstance_RejectsConfigurationAfterInitializationOrNavigation()
    {
        using var initializedBackend = new ConfigurableBackend();
        using var initialized = new NativeWebViewInstance(initializedBackend);
        using var initializedPresenter = new NativeWebViewControl(initialized);
        await initializedPresenter.InitializeAsync();

        Assert.Throws<InvalidOperationException>(() =>
            initialized.ApplyInstanceConfiguration(CreateScriptConfiguration("too-late")));
        Assert.Throws<InvalidOperationException>(() =>
            initialized.InstanceConfiguration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("too-late")));

        using var navigatedBackend = new ConfigurableBackend();
        using var navigated = new NativeWebViewInstance(navigatedBackend);
        using var navigatedPresenter = new NativeWebViewControl(navigated);
        navigatedPresenter.Navigate(new Uri("https://example.com/"));

        Assert.Throws<InvalidOperationException>(() =>
            navigated.ApplyInstanceConfiguration(CreateScriptConfiguration("too-late")));
        Assert.Throws<InvalidOperationException>(() =>
            navigated.InstanceConfiguration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("too-late")));
    }

    [Fact]
    public void NativeWebViewInstance_FailedBackendApplicationLeavesPreviousConfigurationActive()
    {
        using var backend = new ConfigurableBackend();
        using var instance = new NativeWebViewInstance(backend, CreateScriptConfiguration("previous"));
        using var presenter = new NativeWebViewControl(instance);
        backend.ThrowOnApply = true;

        Assert.Throws<InvalidOperationException>(() =>
            instance.ApplyInstanceConfiguration(CreateScriptConfiguration("rejected")));

        Assert.Equal("previous", Assert.Single(presenter.InstanceConfiguration.DocumentStartScripts).Source);
    }

    [Fact]
    public void NativeWebViewInstance_FailedCollectionMutationRollsBackConfiguration()
    {
        using var backend = new ConfigurableBackend();
        using var instance = new NativeWebViewInstance(backend, CreateScriptConfiguration("previous"));
        using var presenter = new NativeWebViewControl(instance);
        backend.ThrowOnApply = true;

        Assert.Throws<InvalidOperationException>(() =>
            presenter.InstanceConfiguration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("rejected")));

        Assert.Equal("previous", Assert.Single(presenter.InstanceConfiguration.DocumentStartScripts).Source);
        Assert.Equal("previous", Assert.Single(backend.LastConfiguration!.DocumentStartScripts).Source);
    }

    [Fact]
    public void NativeWebViewInstance_RejectsCollectionMutationAfterLifecycleBoundary()
    {
        using var committedBackend = new ConfigurableBackend();
        using var committed = new NativeWebViewInstance(committedBackend, CreateScriptConfiguration("initial"));
        committed.CommitInstanceConfiguration();

        Assert.Throws<InvalidOperationException>(() =>
            committed.ApplyInstanceConfiguration(CreateScriptConfiguration("replacement")));
        Assert.Throws<InvalidOperationException>(() =>
            committed.InstanceConfiguration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("late")));
        Assert.Equal("initial", Assert.Single(committed.InstanceConfiguration.DocumentStartScripts).Source);
    }

    [Fact]
    public void NativeWebViewInstance_RejectsEveryActiveCollectionMutationAfterDisposal()
    {
        using var backend = new ConfigurableBackend();
        var instance = new NativeWebViewInstance(backend, CreateScriptConfiguration("initial"));
        var configuration = instance.InstanceConfiguration;
        instance.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            configuration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("add")));
        Assert.Throws<ObjectDisposedException>(() =>
            configuration.DocumentStartScripts[0] = new NativeWebViewDocumentStartScript("replace"));
        Assert.Throws<ObjectDisposedException>(() => configuration.DocumentStartScripts.RemoveAt(0));
        Assert.Throws<ObjectDisposedException>(() => configuration.DocumentStartScripts.Clear());
        Assert.Equal("initial", Assert.Single(configuration.DocumentStartScripts).Source);
    }

    [Fact]
    public void NativeWebViewInstance_RetainedConfigurationDoesNotRetainDisposedInstance()
    {
        var (configuration, instanceReference) = CreateDisposedInstanceAndRetainedConfiguration();

        for (var attempt = 0; attempt < 10 && instanceReference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(instanceReference.IsAlive);
        Assert.Throws<ObjectDisposedException>(() =>
            configuration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("rejected")));
        GC.KeepAlive(configuration);
    }

    [Fact]
    public void NativeWebViewInstance_DetachedConfigurationRemainsIndependentlyMutable()
    {
        using var backend = new ConfigurableBackend();
        using var instance = new NativeWebViewInstance(backend, CreateScriptConfiguration("detached"));
        var detached = instance.InstanceConfiguration;

        instance.ApplyInstanceConfiguration(CreateScriptConfiguration("active"));
        detached.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("independent"));

        Assert.Collection(
            detached.DocumentStartScripts,
            script => Assert.Equal("detached", script.Source),
            script => Assert.Equal("independent", script.Source));
        Assert.Equal("active", Assert.Single(instance.InstanceConfiguration.DocumentStartScripts).Source);
    }

    [Fact]
    public void DocumentStartScripts_AreValidatedAndClonedInOrder()
    {
        Assert.Throws<ArgumentException>(() => new NativeWebViewDocumentStartScript(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeWebViewDocumentStartScript("ok", (NativeWebViewScriptFrameScope)42));

        var configuration = new NativeWebViewInstanceConfiguration();
        Assert.Throws<ArgumentNullException>(() => configuration.DocumentStartScripts.Add(null!));
        configuration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("first", NativeWebViewScriptFrameScope.MainFrame));
        configuration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript("second", NativeWebViewScriptFrameScope.AllFrames));

        var clone = configuration.Clone();

        Assert.Collection(
            clone.DocumentStartScripts,
            script => { Assert.Equal("first", script.Source); Assert.Equal(NativeWebViewScriptFrameScope.MainFrame, script.FrameScope); },
            script => { Assert.Equal("second", script.Source); Assert.Equal(NativeWebViewScriptFrameScope.AllFrames, script.FrameScope); });
        Assert.NotSame(configuration.DocumentStartScripts[0], clone.DocumentStartScripts[0]);
    }

    [Fact]
    public void DesktopPlatforms_ReportDocumentStartScriptInjection()
    {
        Assert.True(new WindowsNativeWebViewBackend().Features.Supports(NativeWebViewFeature.DocumentStartScriptInjection));
        Assert.True(new LinuxNativeWebViewBackend().Features.Supports(NativeWebViewFeature.DocumentStartScriptInjection));
        Assert.True(new MacOSNativeWebViewBackend().Features.Supports(NativeWebViewFeature.DocumentStartScriptInjection));
        Assert.False(new MacOSNativeWebDialogBackend().Features.Supports(NativeWebViewFeature.DocumentStartScriptInjection));
        Assert.False(new MacOSWebAuthenticationBrokerBackend().Features.Supports(NativeWebViewFeature.DocumentStartScriptInjection));
    }

    [Theory]
    [InlineData("credential\n1", "Credential", "1")]
    [InlineData("context\nhttps://example.com/file", "Context", "https://example.com/file")]
    [InlineData("download\nhttps://example.com/file", "Download", "https://example.com/file")]
    [InlineData("https://example.com/file", "Download", "https://example.com/file")]
    public void MacOSDownloadBridgeMessage_IsClassified(
        string input,
        string expectedKind,
        string expectedPayload)
    {
        var message = MacOSNativeWebViewHost.ParseDownloadBridgeMessage(input);

        Assert.Equal(expectedKind, message.Kind.ToString());
        Assert.Equal(expectedPayload, message.Payload);
    }

    [Theory]
    [InlineData("nativeWebViewDownload", "Download")]
    [InlineData("nativeWebViewMessage", "WebMessage")]
    [InlineData(null, "None")]
    [InlineData("unknown", "None")]
    public void MacOSScriptMessageRoute_IsClassified(string? name, string expectedRoute)
    {
        var route = MacOSNativeWebViewHost.ClassifyScriptMessageRoute(name);

        Assert.Equal(expectedRoute, route.ToString());
    }

    [Theory]
    [InlineData("nativeWebViewDownload", "credential\n1", "ImmediateContextState")]
    [InlineData("nativeWebViewDownload", "context\nhttps://example.com/file", "ImmediateContextState")]
    [InlineData("nativeWebViewDownload", "download\nhttps://example.com/file", "Deferred")]
    [InlineData("nativeWebViewDownload", "https://example.com/file", "Deferred")]
    [InlineData("nativeWebViewMessage", "message", "Deferred")]
    [InlineData(null, null, "None")]
    [InlineData("unknown", "message", "None")]
    public void MacOSScriptMessageDelivery_IsClassified(
        string? name,
        string? payload,
        string expectedMode)
    {
        var mode = MacOSNativeWebViewHost.ClassifyScriptMessageDelivery(name, payload);

        Assert.Equal(expectedMode, mode.ToString());
    }

    [Theory]
    [InlineData("credential\n1", "Credential", "1")]
    [InlineData("context\nhttps://example.com/file", "Context", "https://example.com/file")]
    public void MacOSScriptMessageDispatch_AppliesContextStateBeforeReturning(
        string payload,
        string expectedKind,
        string expectedPayload)
    {
        var queued = new List<Action>();
        MacOSNativeWebViewHost.DownloadBridgeMessage? appliedState = null;
        var delivered = false;

        MacOSNativeWebViewHost.DispatchScriptMessage(
            queued.Add,
            static () => false,
            message => appliedState = message,
            (_, _) => delivered = true,
            "nativeWebViewDownload",
            new MacOSNativeWebViewHost.ScriptMessageBody(
                MacOSNativeWebViewHost.ScriptMessageBodyKind.NativeString,
                payload));

        Assert.NotNull(appliedState);
        Assert.Equal(expectedKind, appliedState.Value.Kind.ToString());
        Assert.Equal(expectedPayload, appliedState.Value.Payload);
        Assert.Empty(queued);
        Assert.False(delivered);
    }

    [Fact]
    public void MacOSScriptMessageDispatch_IsDeferredAndOrdered()
    {
        var queued = new List<Action>();
        var delivered = new List<string?>();

        MacOSNativeWebViewHost.DispatchScriptMessage(
            queued.Add,
            static () => false,
            _ => throw new InvalidOperationException("Context state must not be applied."),
            (name, _) => delivered.Add(name),
            "nativeWebViewMessage",
            new MacOSNativeWebViewHost.ScriptMessageBody(default, "first"));
        MacOSNativeWebViewHost.DispatchScriptMessage(
            queued.Add,
            static () => false,
            _ => throw new InvalidOperationException("Context state must not be applied."),
            (_, body) => delivered.Add(body.Payload),
            "nativeWebViewDownload",
            new MacOSNativeWebViewHost.ScriptMessageBody(default, "download\nsecond"));

        Assert.Empty(delivered);
        Assert.Equal(2, queued.Count);

        foreach (var action in queued)
            action();

        Assert.Equal(["nativeWebViewMessage", "download\nsecond"], delivered);
    }

    [Fact]
    public void MacOSScriptMessageDispatch_IgnoresMessageWhenAlreadyDisposed()
    {
        var queued = new List<Action>();
        var appliedState = false;
        var delivered = false;

        MacOSNativeWebViewHost.DispatchScriptMessage(
            queued.Add,
            static () => true,
            _ => appliedState = true,
            (_, _) => delivered = true,
            "nativeWebViewDownload",
            new MacOSNativeWebViewHost.ScriptMessageBody(default, "credential\n1"));

        Assert.Empty(queued);
        Assert.False(appliedState);
        Assert.False(delivered);
    }

    [Fact]
    public void MacOSScriptMessageDispatch_IgnoresUnknownBridge()
    {
        var queued = new List<Action>();
        var delivered = false;

        MacOSNativeWebViewHost.DispatchScriptMessage(
            queued.Add,
            static () => false,
            _ => delivered = true,
            (_, _) => delivered = true,
            "unknown",
            default);

        Assert.Empty(queued);
        Assert.False(delivered);
    }

    [Fact]
    public void MacOSScriptMessageDispatch_IgnoresDeferredDeliveryAfterDisposal()
    {
        var queued = new List<Action>();
        var disposed = false;
        var delivered = false;

        MacOSNativeWebViewHost.DispatchScriptMessage(
            queued.Add,
            () => disposed,
            _ => throw new InvalidOperationException("Context state must not be applied."),
            (_, _) => delivered = true,
            "nativeWebViewMessage",
            default);

        Assert.False(delivered);
        disposed = true;
        Assert.Single(queued)();
        Assert.False(delivered);
    }

    [Fact]
    public void MacOSNativeCleanup_RollsBackInReverseOrderExactlyOnce()
    {
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        var released = new List<string>();
        cleanup.RegisterManagedOwnerRelease(() => released.Add("managed"));
        cleanup.Register(() => released.Add("configuration"));
        cleanup.Register(() => released.Add("view"));

        var result = cleanup.Rollback();

        Assert.Empty(result.Exceptions);
        Assert.False(result.ManagedOwnerHandleRetained);
        Assert.Equal(["view", "configuration", "managed"], released);
        Assert.Equal(MacOSNativeWebViewHost.NativeResourceCleanupResult.Empty, cleanup.Rollback());
        Assert.Equal(3, released.Count);
    }

    [Fact]
    public void MacOSNativeCleanup_CommitSuppressesRollback()
    {
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        var released = false;
        cleanup.Register(() => released = true);
        cleanup.RegisterManagedOwnerRelease(() => released = true);

        cleanup.Commit();

        Assert.Equal(MacOSNativeWebViewHost.NativeResourceCleanupResult.Empty, cleanup.Rollback());
        Assert.False(released);
    }

    [Fact]
    public void MacOSNativeCleanup_AttachesFailuresWithoutReplacingPrimaryException()
    {
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        var finalReleaseRan = false;
        cleanup.RegisterManagedOwnerRelease(() => finalReleaseRan = true);
        cleanup.Register(static () => throw new InvalidOperationException("release failed"));
        var primary = new ApplicationException("construction failed");

        var cleanupResult = cleanup.Rollback();
        MacOSNativeWebViewHost.AttachConstructionCleanupFailures(primary, cleanupResult);

        Assert.True(finalReleaseRan);
        Assert.False(cleanupResult.ManagedOwnerHandleRetained);
        var aggregate = Assert.IsType<AggregateException>(
            primary.Data[MacOSNativeWebViewHost.ConstructionCleanupExceptionsDataKey]);
        Assert.Equal("release failed", Assert.Single(aggregate.InnerExceptions).Message);
        Assert.Equal("construction failed", primary.Message);
    }

    [Fact]
    public void MacOSNativeCleanup_CriticalFailureRetainsManagedOwnerAndRunsRemainingActions()
    {
        var owner = new object();
        var managedHandle = GCHandle.Alloc(owner);
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        var released = new List<string>();
        cleanup.RegisterManagedOwnerRelease(managedHandle.Free);
        cleanup.Register(() => released.Add("first"));
        cleanup.Register(
            static () => throw new InvalidOperationException("native owner release failed"),
            MacOSNativeWebViewHost.NativeResourceCleanupFailureRisk.ManagedOwnerMayRemainReachable);
        cleanup.Register(() => released.Add("last"));

        try
        {
            var result = cleanup.Rollback();

            Assert.True(result.ManagedOwnerHandleRetained);
            Assert.Equal(["last", "first"], released);
            Assert.Equal("native owner release failed", Assert.Single(result.Exceptions).Message);
            Assert.Same(owner, GCHandle.FromIntPtr(GCHandle.ToIntPtr(managedHandle)).Target);
        }
        finally
        {
            if (managedHandle.IsAllocated)
                managedHandle.Free();
        }
    }

    [Fact]
    public void MacOSNativeCleanup_CriticalFailureAddsRetentionDiagnosticWithoutReplacingPrimaryException()
    {
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        cleanup.RegisterManagedOwnerRelease(static () => throw new InvalidOperationException("must not run"));
        cleanup.Register(
            static () => throw new InvalidOperationException("native release failed"),
            MacOSNativeWebViewHost.NativeResourceCleanupFailureRisk.ManagedOwnerMayRemainReachable);
        var primary = new ApplicationException("construction failed");

        var result = cleanup.Rollback();
        MacOSNativeWebViewHost.AttachConstructionCleanupFailures(primary, result);

        Assert.True(result.ManagedOwnerHandleRetained);
        var aggregate = Assert.IsType<AggregateException>(
            primary.Data[MacOSNativeWebViewHost.ConstructionCleanupExceptionsDataKey]);
        Assert.Collection(
            aggregate.InnerExceptions,
            exception => Assert.Equal("native release failed", exception.Message),
            exception => Assert.Equal(MacOSNativeWebViewHost.ManagedOwnerHandleRetainedMessage, exception.Message));
        Assert.Equal("construction failed", primary.Message);
    }

    [Theory]
    [InlineData("{\"nativeWebViewVersion\":1,\"kind\":\"string\",\"payload\":\"hello\"}", "String", "hello")]
    [InlineData("{\"nativeWebViewVersion\":1,\"kind\":\"json\",\"payload\":\"{\\\"value\\\":42}\"}", "Json", "{\"value\":42}")]
    [InlineData("{\"nativeWebViewVersion\":1,\"kind\":\"json\",\"payload\":\"[1,true]\"}", "Json", "[1,true]")]
    [InlineData("{\"nativeWebViewVersion\":1,\"kind\":\"json\",\"payload\":\"null\"}", "Json", "null")]
    [InlineData("legacy raw message", "String", "legacy raw message")]
    [InlineData("{\"direct\":true}", "String", "{\"direct\":true}")]
    [InlineData("[1,true]", "String", "[1,true]")]
    [InlineData("314159", "String", "314159")]
    [InlineData("false", "String", "false")]
    [InlineData("null", "String", "null")]
    [InlineData("{\"nativeWebViewVersion\":1,\"kind\":\"json\",\"payload\":\"invalid\"}", "String", "{\"nativeWebViewVersion\":1,\"kind\":\"json\",\"payload\":\"invalid\"}")]
    public void MacOSWebMessageEnvelope_PreservesPayloadKind(
        string input,
        string expectedKind,
        string expectedPayload)
    {
        var message = MacOSNativeWebViewHost.ParseWebMessageBridgeMessage(input);

        Assert.Equal(expectedKind, message.Kind.ToString());
        Assert.Equal(expectedPayload, message.Payload);
    }

    [Fact]
    public void MacOSWebMessageEnvelope_NullRemainsLegacyStringPayload()
    {
        var message = MacOSNativeWebViewHost.ParseWebMessageBridgeMessage(null);

        Assert.Equal(MacOSNativeWebViewHost.WebMessageBridgeMessageKind.String, message.Kind);
        Assert.Null(message.Payload);
    }

    [Theory]
    [InlineData("{\"direct\":true}")]
    [InlineData("[1,true]")]
    [InlineData("314159")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("{\"nativeWebViewVersion\":1,\"kind\":\"json\",\"payload\":\"{\\\"directEnvelope\\\":true}\"}")]
    public void MacOSSerializedFoundationWebMessageBody_RemainsLegacyStringPayload(string payload)
    {
        var body = new MacOSNativeWebViewHost.ScriptMessageBody(
            MacOSNativeWebViewHost.ScriptMessageBodyKind.SerializedFoundationValue,
            payload);

        var message = MacOSNativeWebViewHost.ParseWebMessageBridgeMessage(body);

        Assert.Equal(MacOSNativeWebViewHost.WebMessageBridgeMessageKind.String, message.Kind);
        Assert.Equal(payload, message.Payload);
    }

    [Fact]
    public void MacOSNativeStringWebMessageBody_ParsesVersionedEnvelope()
    {
        const string envelope = "{\"nativeWebViewVersion\":1,\"kind\":\"json\",\"payload\":\"{\\\"value\\\":42}\"}";
        var body = new MacOSNativeWebViewHost.ScriptMessageBody(
            MacOSNativeWebViewHost.ScriptMessageBodyKind.NativeString,
            envelope);

        var message = MacOSNativeWebViewHost.ParseWebMessageBridgeMessage(body);

        Assert.Equal(MacOSNativeWebViewHost.WebMessageBridgeMessageKind.Json, message.Kind);
        Assert.Equal("{\"value\":42}", message.Payload);
    }

    [Fact]
    public void MacOSScriptEvaluationDispatchState_CancellationCanWinBeforeDispatch()
    {
        var state = new MacOSNativeWebViewHost.ScriptEvaluationDispatchState();

        Assert.True(state.TryCancelBeforeDispatch());
        Assert.False(state.TryMarkDispatched());
        Assert.Equal(MacOSNativeWebViewHost.ScriptEvaluationDispatchStatus.CanceledBeforeDispatch, state.Status);
    }

    [Fact]
    public void MacOSScriptEvaluationDispatchState_DispatchCanWinBeforeCancellation()
    {
        var state = new MacOSNativeWebViewHost.ScriptEvaluationDispatchState();

        Assert.True(state.TryMarkDispatched());
        Assert.False(state.TryCancelBeforeDispatch());
        Assert.Equal(MacOSNativeWebViewHost.ScriptEvaluationDispatchStatus.Dispatched, state.Status);
    }

    [Fact]
    public void MacOSNativeBlockOwnership_ManagedCleanupWaitsForEveryNativeCopy()
    {
        var state = new MacOSNativeWebViewHost.NativeBlockOwnershipState();
        state.AddNativeOwnership();
        state.AddNativeOwnership();

        var firstNativeRelease = state.ReleaseNativeOwnership();
        var managedRelease = state.ReleaseManagedOwnership();
        var finalNativeRelease = state.ReleaseNativeOwnership();

        Assert.False(firstNativeRelease.NativeOwnershipEnded);
        Assert.False(firstNativeRelease.ReleaseHandle);
        Assert.False(managedRelease);
        Assert.True(finalNativeRelease.NativeOwnershipEnded);
        Assert.True(finalNativeRelease.ReleaseHandle);
    }

    [Fact]
    public void MacOSNativeBlockOwnership_NativeDisposalWaitsForManagedCleanup()
    {
        var state = new MacOSNativeWebViewHost.NativeBlockOwnershipState();
        state.AddNativeOwnership();

        var nativeRelease = state.ReleaseNativeOwnership();

        Assert.True(nativeRelease.NativeOwnershipEnded);
        Assert.False(nativeRelease.ReleaseHandle);
        Assert.True(state.ReleaseManagedOwnership());
        Assert.False(state.ReleaseManagedOwnership());
    }

    [Fact]
    public void MacOSNativeBlockOwnership_ManagedFirstReleasesHandleAtFinalNativeDisposal()
    {
        var state = new MacOSNativeWebViewHost.NativeBlockOwnershipState();
        state.AddNativeOwnership();

        Assert.False(state.ReleaseManagedOwnership());
        var nativeRelease = state.ReleaseNativeOwnership();

        Assert.True(nativeRelease.NativeOwnershipEnded);
        Assert.True(nativeRelease.ReleaseHandle);
        Assert.False(state.ReleaseManagedOwnership());
    }

    [Fact]
    public async Task MacOSNativeBlockOwnership_ConcurrentFinalReleasesSignalHandleExactlyOnce()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var state = new MacOSNativeWebViewHost.NativeBlockOwnershipState();
            state.AddNativeOwnership();
            using var start = new ManualResetEventSlim();

            var managed = Task.Run(() =>
            {
                start.Wait();
                return state.ReleaseManagedOwnership();
            });
            var native = Task.Run(() =>
            {
                start.Wait();
                return state.ReleaseNativeOwnership().ReleaseHandle;
            });

            start.Set();
            var releaseSignals = (await managed ? 1 : 0) + (await native ? 1 : 0);
            Assert.Equal(1, releaseSignals);
        }
    }

    [Fact]
    public void MacOSScriptEvaluationSetupCleanup_RunsOnceAndAttachesFailureToPrimaryException()
    {
        var primary = new ObjectDisposedException(nameof(CancellationTokenSource));
        var cleanupCalls = 0;

        MacOSNativeWebViewHost.CleanupFailedScriptEvaluationSetup(
            primary,
            () =>
            {
                cleanupCalls++;
                throw new InvalidOperationException("cleanup failed");
            });

        Assert.Equal(1, cleanupCalls);
        var cleanupException = Assert.IsType<InvalidOperationException>(
            primary.Data[MacOSNativeWebViewHost.ScriptEvaluationSetupCleanupExceptionsDataKey]);
        Assert.Equal("cleanup failed", cleanupException.Message);
    }

    [Theory]
    [InlineData(
        "A JavaScript exception occurred",
        "Error: integration-script-error",
        "A JavaScript exception occurred: Error: integration-script-error")]
    [InlineData(null, "Error: integration-script-error", "Error: integration-script-error")]
    [InlineData("A JavaScript exception occurred", null, "A JavaScript exception occurred")]
    [InlineData(
        "A JavaScript exception occurred: Error: integration-script-error",
        "Error: integration-script-error",
        "A JavaScript exception occurred: Error: integration-script-error")]
    [InlineData("  A JavaScript exception occurred  ", "   ", "A JavaScript exception occurred")]
    [InlineData(null, null, null)]
    public void MacOSJavaScriptEvaluationError_CombinesWebKitDetailWithLocalizedDescription(
        string? localizedDescription,
        string? exceptionMessage,
        string? expected)
    {
        Assert.Equal(
            expected,
            MacOSNativeWebViewHost.CombineJavaScriptEvaluationErrorMessage(
                localizedDescription,
                exceptionMessage));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void MacOSStartDownloadCompletion_AttachesOnlyToLiveNonAbandonedHost(
        bool hostDisposed,
        bool contextAbandoned,
        bool expected)
    {
        Assert.Equal(
            expected,
            MacOSNativeWebViewHost.ShouldAttachCompletedStartDownload(hostDisposed, contextAbandoned));
    }

    [Fact]
    public async Task NativeWebView_InstanceConfiguration_IsolatedPerControlInstance()
    {
        var shared = new NativeWebViewInstanceConfiguration();
        shared.EnvironmentOptions.UserDataFolder = "/tmp/shared/user-data";
        shared.EnvironmentOptions.CacheFolder = "/tmp/shared/cache";
        shared.EnvironmentOptions.CookieDataFolder = "/tmp/shared/cookies";
        shared.EnvironmentOptions.SessionDataFolder = "/tmp/shared/session";
        shared.EnvironmentOptions.Language = "en-US";
        shared.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://shared-proxy:8080",
            BypassList = "localhost;127.0.0.1",
            AutoConfigUrl = "https://example.com/shared.pac",
        };
        shared.ControllerOptions.ProfileName = "shared-profile";
        shared.ControllerOptions.ScriptLocale = "en-US";

        using var first = new NativeWebViewControl(new WindowsNativeWebViewBackend())
        {
            InstanceConfiguration = shared,
        };

        using var second = new NativeWebViewControl(new WindowsNativeWebViewBackend())
        {
            InstanceConfiguration = shared,
        };

        first.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "/tmp/first/user-data";
        first.InstanceConfiguration.EnvironmentOptions.CacheFolder = "/tmp/first/cache";
        first.InstanceConfiguration.EnvironmentOptions.CookieDataFolder = "/tmp/first/cookies";
        first.InstanceConfiguration.EnvironmentOptions.SessionDataFolder = "/tmp/first/session";
        first.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://first-proxy:8080",
            BypassList = "localhost",
            AutoConfigUrl = "https://example.com/first.pac",
        };
        first.InstanceConfiguration.ControllerOptions.ProfileName = "first-profile";
        first.InstanceConfiguration.ControllerOptions.IsInPrivateModeEnabled = true;
        first.InstanceConfiguration.ControllerOptions.ScriptLocale = "pl-PL";

        second.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "/tmp/second/user-data";
        second.InstanceConfiguration.EnvironmentOptions.CacheFolder = "/tmp/second/cache";
        second.InstanceConfiguration.EnvironmentOptions.CookieDataFolder = "/tmp/second/cookies";
        second.InstanceConfiguration.EnvironmentOptions.SessionDataFolder = "/tmp/second/session";
        second.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://second-proxy:9090",
            BypassList = "localhost;intranet",
            AutoConfigUrl = "https://example.com/second.pac",
        };
        second.InstanceConfiguration.ControllerOptions.ProfileName = "second-profile";
        second.InstanceConfiguration.ControllerOptions.ScriptLocale = "de-DE";

        shared.EnvironmentOptions.UserDataFolder = "/tmp/shared/mutated";
        shared.EnvironmentOptions.Proxy!.Server = "http://shared-mutated:9999";
        shared.ControllerOptions.ProfileName = "shared-mutated-profile";

        NativeWebViewEnvironmentOptions? firstEnvironment = null;
        NativeWebViewControllerOptions? firstController = null;
        first.CoreWebView2EnvironmentRequested += (_, e) => firstEnvironment = e.Options.Clone();
        first.CoreWebView2ControllerOptionsRequested += (_, e) => firstController = e.Options.Clone();

        NativeWebViewEnvironmentOptions? secondEnvironment = null;
        NativeWebViewControllerOptions? secondController = null;
        second.CoreWebView2EnvironmentRequested += (_, e) => secondEnvironment = e.Options.Clone();
        second.CoreWebView2ControllerOptionsRequested += (_, e) => secondController = e.Options.Clone();

        await first.InitializeAsync();
        await second.InitializeAsync();

        Assert.NotNull(firstEnvironment);
        Assert.NotNull(firstController);
        Assert.NotNull(secondEnvironment);
        Assert.NotNull(secondController);

        Assert.Equal("/tmp/first/user-data", firstEnvironment!.UserDataFolder);
        Assert.Equal("/tmp/first/cache", firstEnvironment.CacheFolder);
        Assert.Equal("/tmp/first/cookies", firstEnvironment.CookieDataFolder);
        Assert.Equal("/tmp/first/session", firstEnvironment.SessionDataFolder);
        Assert.Equal("http://first-proxy:8080", firstEnvironment.Proxy?.Server);
        Assert.Equal("localhost", firstEnvironment.Proxy?.BypassList);
        Assert.Equal("https://example.com/first.pac", firstEnvironment.Proxy?.AutoConfigUrl);
        Assert.Equal("first-profile", firstController!.ProfileName);
        Assert.True(firstController.IsInPrivateModeEnabled);
        Assert.Equal("pl-PL", firstController.ScriptLocale);

        Assert.Equal("/tmp/second/user-data", secondEnvironment!.UserDataFolder);
        Assert.Equal("/tmp/second/cache", secondEnvironment.CacheFolder);
        Assert.Equal("/tmp/second/cookies", secondEnvironment.CookieDataFolder);
        Assert.Equal("/tmp/second/session", secondEnvironment.SessionDataFolder);
        Assert.Equal("http://second-proxy:9090", secondEnvironment.Proxy?.Server);
        Assert.Equal("localhost;intranet", secondEnvironment.Proxy?.BypassList);
        Assert.Equal("https://example.com/second.pac", secondEnvironment.Proxy?.AutoConfigUrl);
        Assert.Equal("second-profile", secondController!.ProfileName);
        Assert.False(secondController.IsInPrivateModeEnabled);
        Assert.Equal("de-DE", secondController.ScriptLocale);

        Assert.NotSame(firstEnvironment.Proxy, secondEnvironment.Proxy);
        Assert.NotEqual(shared.EnvironmentOptions.UserDataFolder, firstEnvironment.UserDataFolder);
        Assert.NotEqual(shared.EnvironmentOptions.UserDataFolder, secondEnvironment.UserDataFolder);
        Assert.NotEqual(shared.ControllerOptions.ProfileName, firstController.ProfileName);
        Assert.NotEqual(shared.ControllerOptions.ProfileName, secondController.ProfileName);
    }

    [Fact]
    public async Task NativeWebView_InstanceConfiguration_IsAppliedBeforePublicOptionHandlers()
    {
        using var control = new NativeWebViewControl(new WindowsNativeWebViewBackend());
        control.InstanceConfiguration.EnvironmentOptions.UserDataFolder = "/tmp/ordered/user-data";
        control.InstanceConfiguration.EnvironmentOptions.Proxy = new NativeWebViewProxyOptions
        {
            Server = "http://ordered-proxy:8080",
        };
        control.InstanceConfiguration.ControllerOptions.ProfileName = "ordered-profile";

        NativeWebViewEnvironmentOptions? capturedEnvironment = null;
        NativeWebViewControllerOptions? capturedController = null;

        control.CoreWebView2EnvironmentRequested += (_, e) =>
        {
            capturedEnvironment = e.Options.Clone();
            e.Options.Language = "fr-FR";
        };

        control.CoreWebView2ControllerOptionsRequested += (_, e) =>
        {
            capturedController = e.Options.Clone();
            e.Options.ScriptLocale = "fr-FR";
        };

        await control.InitializeAsync();

        Assert.NotNull(capturedEnvironment);
        Assert.Equal("/tmp/ordered/user-data", capturedEnvironment!.UserDataFolder);
        Assert.Equal("http://ordered-proxy:8080", capturedEnvironment.Proxy?.Server);

        Assert.NotNull(capturedController);
        Assert.Equal("ordered-profile", capturedController!.ProfileName);
    }

    private static NativeWebViewInstanceConfiguration CreateScriptConfiguration(string source)
    {
        var configuration = new NativeWebViewInstanceConfiguration();
        configuration.DocumentStartScripts.Add(new NativeWebViewDocumentStartScript(source));
        return configuration;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (NativeWebViewInstanceConfiguration Configuration, WeakReference InstanceReference)
        CreateDisposedInstanceAndRetainedConfiguration()
    {
        var instance = new NativeWebViewInstance(
            new ConfigurableBackend(),
            CreateScriptConfiguration("initial"));
        var configuration = instance.InstanceConfiguration;
        var instanceReference = new WeakReference(instance);
        instance.Dispose();
        return (configuration, instanceReference);
    }

    private sealed class ConfigurableBackend :
        NativeWebViewBackendStubBase,
        INativeWebViewBackend,
        INativeWebViewInstanceConfigurationTarget
    {
        public ConfigurableBackend()
            : base(
                NativeWebViewPlatform.Windows,
                new WebViewPlatformFeatures(
                    NativeWebViewPlatform.Windows,
                    NativeWebViewFeature.EmbeddedView |
                    NativeWebViewFeature.DocumentStartScriptInjection))
        {
        }

        public int ApplyCount { get; private set; }

        public NativeWebViewInstanceConfiguration? LastConfiguration { get; private set; }

        public bool ThrowOnApply { get; set; }

        public bool ThrowOnDispose { get; set; }

        public int DisposeCount { get; private set; }

        public int EventSubscriberCount => typeof(NativeWebViewBackendStubBase)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(static fieldInfo => typeof(MulticastDelegate).IsAssignableFrom(fieldInfo.FieldType))
            .Select(fieldInfo => fieldInfo.GetValue(this) as MulticastDelegate)
            .Where(static callback => callback is not null)
            .Sum(static callback => callback!.GetInvocationList().Length);

        public void ApplyInstanceConfiguration(NativeWebViewInstanceConfiguration configuration)
        {
            ApplyCount++;
            if (ThrowOnApply)
                throw new InvalidOperationException("Rejected test configuration.");
            LastConfiguration = configuration.Clone();
        }

        public new void Dispose()
        {
            if (DisposeCount != 0)
                return;

            DisposeCount++;
            if (ThrowOnDispose)
                throw new InvalidOperationException("Rejected test disposal.");
            base.Dispose();
        }

        void IDisposable.Dispose() => Dispose();
    }
}
