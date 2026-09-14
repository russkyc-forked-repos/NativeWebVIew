using NativeWebView.Interop;
using NativeWebView.Controls;

namespace NativeWebView.Core.Tests;

public sealed class MacOSDirectProxyContextTests
{
    private static NativeWebViewInstanceConfiguration Configuration(string name = "test", bool isPrivate = false)
    {
        var configuration = new NativeWebViewInstanceConfiguration();
        configuration.EnvironmentOptions.Proxy = new() { NoProxy = true };
        configuration.ControllerOptions.ProfileName = name;
        configuration.ControllerOptions.IsInPrivateModeEnabled = isPrivate;
        return configuration;
    }

    [Fact]
    public void ProfileIdentity_IsStableAndIndependentOfInternalEndpoint()
    {
        var configuration = Configuration();
        var id = MacOSDirectProxyContextRegistry.GetPersistentIdentifier(configuration);
        Assert.Equal(id, MacOSDirectProxyContextRegistry.GetPersistentIdentifier(configuration.Clone()));
        Assert.NotEqual(id, MacOSDirectProxyContextRegistry.GetPersistentIdentifier(Configuration("other")));
        configuration.EnvironmentOptions.UserDataFolder = "different";
        Assert.NotEqual(id, MacOSDirectProxyContextRegistry.GetPersistentIdentifier(configuration));
    }

    [Fact]
    public void SharedOwners_KeepRouteAliveUntilFinalRelease_ThenReconfigureOnReacquisition()
    {
        var factory = new Factory();
        var registry = Registry(factory);
        var configuration = Configuration();
        var first = registry.Acquire(configuration);
        var second = registry.Acquire(configuration.Clone());
        Assert.Equal(first.Store, second.Store);
        Assert.Equal(first.Port, second.Port);
        Assert.Equal(1, factory.Created);
        first.Dispose();
        Assert.Equal(0, factory.Released);
        Assert.True(factory.LastForwarder!.IsHealthy);
        second.Dispose();
        second.Dispose();
        Assert.Equal(1, factory.Released);
        var identity = factory.LastIdentifier;
        using var next = registry.Acquire(configuration);
        Assert.Equal(2, factory.Created);
        Assert.Equal(identity, factory.LastIdentifier);
        Assert.True(factory.LastForwarder!.IsHealthy);
    }

    [Fact]
    public void PrivateStores_AreIndependentAndNeverAcquirePersistentIdentity()
    {
        var factory = new Factory();
        var registry = Registry(factory);
        using var first = registry.Acquire(Configuration(isPrivate: true));
        using var second = registry.Acquire(Configuration(isPrivate: true));
        Assert.NotEqual(first.Store, second.Store);
        Assert.NotEqual(first.Port, second.Port);
        Assert.Null(factory.LastIdentifier);
    }

    [Fact]
    public void InitializationFailure_ReleasesProfileLockAndStopsForwarder()
    {
        var factory = new Factory { FailCreation = true };
        var registry = Registry(factory);
        var error = Assert.Throws<InvalidOperationException>(() => registry.Acquire(Configuration()));
        Assert.Equal("injected allocation failure", error.Message);
        factory.FailCreation = false;
        using var retry = registry.Acquire(Configuration());
        Assert.Equal(2, factory.Created);
    }

    [Fact]
    public void DifferentRegistries_CannotOwnSamePersistentProfileConcurrently()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NativeWebView.Direct.Tests", Guid.NewGuid().ToString("N"));
        var firstRegistry = new MacOSDirectProxyContextRegistry(new Factory(), directory);
        var otherRegistry = new MacOSDirectProxyContextRegistry(new Factory(), directory);
        var owner = firstRegistry.Acquire(Configuration());
        try { Assert.Throws<InvalidOperationException>(() => otherRegistry.Acquire(Configuration())); }
        finally { owner.Dispose(); }
        using var replacement = otherRegistry.Acquire(Configuration());
    }

    [Fact]
    public void ApplicationScopes_AllowIndependentStoresButRejectDuplicateOwners()
    {
        var library = Path.Combine(Path.GetTempPath(), "NativeWebView.Direct.Tests", Guid.NewGuid().ToString("N"));
        var firstFactory = new Factory(library, "com.example.first");
        var secondFactory = new Factory(library, "com.example.second");
        var first = new MacOSDirectProxyContextRegistry(firstFactory);
        var second = new MacOSDirectProxyContextRegistry(secondFactory);
        var duplicate = new MacOSDirectProxyContextRegistry(new Factory(library, "com.example.first"));
        using var firstOwner = first.Acquire(Configuration());
        using var secondOwner = second.Acquire(Configuration());
        Assert.Equal(firstFactory.LastIdentifier, secondFactory.LastIdentifier);
        Assert.Throws<InvalidOperationException>(() => duplicate.Acquire(Configuration()));
        firstOwner.Dispose();
        using var reacquired = duplicate.Acquire(Configuration());
    }

    [Fact]
    public void ApplicationLockScope_UsesContainerRootAndSafeStableDirectoryNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "nwv-library");
        var first = MacOSDirectProxyContextRegistry.GetLockDirectory(root, "app/../name");
        Assert.Equal(first, MacOSDirectProxyContextRegistry.GetLockDirectory(root, "app/../name"));
        Assert.Equal(64, Path.GetFileName(first).Length);
        Assert.StartsWith(Path.Combine(root, "NativeWebView", "direct-profile-locks"), first);
        Assert.NotEqual(first, MacOSDirectProxyContextRegistry.GetLockDirectory(root + "-container", "app/../name"));
    }

    [Fact]
    public void FailedService_IsNotReplacedOrUnboundUntilFinalNativeOwnerReleases()
    {
        var factory = new Factory();
        var registry = Registry(factory);
        using var owner = registry.Acquire(Configuration());
        using var sharedOwner = registry.Acquire(Configuration());
        var port = owner.Port;
        factory.LastForwarder!.FailClosed();
        DirectSocksForwarderTests.AssertEndpointReserved(port);
        Assert.Throws<InvalidOperationException>(() => registry.Acquire(Configuration()));
        Assert.Equal(1, factory.Created);
        owner.Dispose();
        DirectSocksForwarderTests.AssertEndpointReserved(port);
        factory.OnRelease = () => DirectSocksForwarderTests.AssertEndpointReserved(port);
        sharedOwner.Dispose();
        Assert.Equal(1, factory.Released);
        using var replacement = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
        replacement.Start();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoreReleaseFailure_QuarantinesEndpointAndNeverRetriesNativeRelease(bool isPrivate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NativeWebView.Direct.Tests", Guid.NewGuid().ToString("N"));
        var releaseAttempts = 0;
        var error = new InvalidOperationException("injected store release failure");
        var factory = new Factory { OnRelease = () => { releaseAttempts++; throw error; } };
        var registry = new MacOSDirectProxyContextRegistry(factory, directory);
        var configuration = Configuration(isPrivate: isPrivate);
        var lease = registry.Acquire(configuration);
        var port = lease.Port;

        Assert.Same(error, Assert.Throws<InvalidOperationException>(lease.Dispose));
        lease.Dispose();
        lease.RetainAfterCleanupFailure();
        Assert.Equal(1, releaseAttempts);
        Assert.Equal(0, factory.Released);
        Assert.False(factory.LastForwarder!.IsHealthy);
        DirectSocksForwarderTests.AssertEndpointReserved(port);
        if (!isPrivate)
        {
            Assert.Contains("Restart", Assert.Throws<InvalidOperationException>(() => registry.Acquire(configuration)).Message);
            var competitor = new MacOSDirectProxyContextRegistry(new Factory(), directory);
            Assert.Throws<InvalidOperationException>(() => competitor.Acquire(configuration));
        }
        else
        {
            // Independent private stores are still usable after another store is quarantined.
            using var next = new MacOSDirectProxyContextRegistry(new Factory(), directory).Acquire(configuration);
            Assert.NotEqual(port, next.Port);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostCleanupFailure_RetainsSharedRouteAndProfileLockAfterAllLeasesDispose(bool critical)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NativeWebView.Direct.Tests", Guid.NewGuid().ToString("N"));
        var factory = new Factory();
        var registry = new MacOSDirectProxyContextRegistry(factory, directory);
        var lease = registry.Acquire(Configuration());
        using var otherOwner = registry.Acquire(Configuration());
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        cleanup.RegisterDirectProxyLease(lease);
        var managedReleased = false;
        cleanup.RegisterManagedOwnerRelease(() => managedReleased = true);
        var error = new InvalidOperationException("injected native cleanup failure");
        cleanup.Register(() => throw error, critical
            ? MacOSNativeWebViewHost.NativeResourceCleanupFailureRisk.ManagedOwnerMayRemainReachable
            : MacOSNativeWebViewHost.NativeResourceCleanupFailureRisk.None);

        var result = cleanup.Rollback();
        Assert.Same(error, Assert.Single(result.Exceptions));
        Assert.Equal(critical, result.ManagedOwnerHandleRetained);
        Assert.Equal(!critical, managedReleased);
        Assert.False(factory.LastForwarder!.IsHealthy);
        DirectSocksForwarderTests.AssertEndpointReserved(lease.Port);
        otherOwner.Dispose();
        lease.Dispose();
        Assert.Equal(0, factory.Released);
        Assert.Equal(MacOSNativeWebViewHost.NativeResourceCleanupResult.Empty, cleanup.Rollback());
        DirectSocksForwarderTests.AssertEndpointReserved(lease.Port);
        Assert.Throws<InvalidOperationException>(() => registry.Acquire(Configuration()));
        var competitor = new MacOSDirectProxyContextRegistry(new Factory(), directory);
        Assert.Throws<InvalidOperationException>(() => competitor.Acquire(Configuration()));
    }

    [Fact]
    public void HostCleanup_ReportsStoreReleaseFailureAndRetainsEndpoint()
    {
        var error = new InvalidOperationException("injected store release failure");
        var factory = new Factory { OnRelease = () => throw error };
        var registry = Registry(factory);
        var lease = registry.Acquire(Configuration());
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        cleanup.RegisterDirectProxyLease(lease);
        Assert.Same(error, Assert.Single(cleanup.Rollback().Exceptions));
        Assert.False(factory.LastForwarder!.IsHealthy);
        DirectSocksForwarderTests.AssertEndpointReserved(lease.Port);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostCleanup_ReleasesRouteOnlyAfterSuccessfulTeardown_OrLeavesOwnershipOnCommit(bool commit)
    {
        var factory = new Factory();
        var registry = Registry(factory);
        using var lease = registry.Acquire(Configuration());
        var cleanup = new MacOSNativeWebViewHost.NativeResourceCleanupCoordinator();
        cleanup.RegisterDirectProxyLease(lease);
        var nativeReleased = false;
        cleanup.Register(() =>
        {
            DirectSocksForwarderTests.AssertEndpointReserved(lease.Port);
            nativeReleased = true;
        });
        if (commit)
            cleanup.Commit();
        Assert.Empty(cleanup.Rollback().Exceptions);
        Assert.Equal(!commit, nativeReleased);
        Assert.Equal(commit ? 0 : 1, factory.Released);
        if (commit)
            DirectSocksForwarderTests.AssertEndpointReserved(lease.Port);
        lease.Dispose();
        Assert.Equal(1, factory.Released);
        using var replacement = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, lease.Port);
        replacement.Start();
    }

    private static MacOSDirectProxyContextRegistry Registry(Factory factory) => new(factory,
        Path.Combine(Path.GetTempPath(), "NativeWebView.Direct.Tests", Guid.NewGuid().ToString("N")));

    private sealed class Factory(string? libraryDirectory = null, string applicationIdentifier = "test") : IMacOSDirectStoreFactory
    {
        internal int Created, Released;
        internal bool FailCreation;
        internal Guid? LastIdentifier;
        internal DirectSocksForwarder? LastForwarder;
        internal Action? OnRelease;
        public void VerifyAccess() { }
        public string GetLockDirectory() => MacOSDirectProxyContextRegistry.GetLockDirectory(
            libraryDirectory ?? throw new InvalidOperationException("Tests must specify a lock directory."), applicationIdentifier);
        public IntPtr Create(Guid? persistentIdentifier, DirectSocksForwarder forwarder)
        {
            Created++;
            LastIdentifier = persistentIdentifier;
            LastForwarder = forwarder;
            if (FailCreation) throw new InvalidOperationException("injected allocation failure");
            return (IntPtr)Created;
        }
        public void Release(IntPtr store)
        {
            OnRelease?.Invoke();
            Released++;
        }
    }
}
