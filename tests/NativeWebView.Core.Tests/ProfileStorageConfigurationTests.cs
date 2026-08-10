using NativeWebView.Controls;
using NativeWebView.Core;
using NativeWebView.Platform.Linux;

namespace NativeWebView.Core.Tests;

public sealed class ProfileStorageConfigurationTests
{
    [Fact]
    public void LinuxPersistentStorage_MapsConfiguredDataAndCacheDirectories()
    {
        var configuration = new NativeWebViewEnvironmentOptions
        {
            UserDataFolder = Path.GetFullPath("profile-data"),
            CacheFolder = Path.GetFullPath("profile-cache"),
        };

        var storage = LinuxNativeWebViewBackend.ResolvePersistentStorageConfiguration(configuration);

        Assert.NotNull(storage);
        Assert.Equal(configuration.UserDataFolder, storage.BaseDataDirectory);
        Assert.Equal(configuration.CacheFolder, storage.BaseCacheDirectory);
    }

    [Fact]
    public void LinuxPersistentStorage_UsesSessionDirectoryWhenUserDataIsAbsent()
    {
        var configuration = new NativeWebViewEnvironmentOptions
        {
            SessionDataFolder = Path.GetFullPath("profile-session"),
        };

        var storage = LinuxNativeWebViewBackend.ResolvePersistentStorageConfiguration(configuration);

        Assert.NotNull(storage);
        Assert.Equal(configuration.SessionDataFolder, storage.BaseDataDirectory);
        Assert.Null(storage.BaseCacheDirectory);
    }

    [Fact]
    public void MacOSWebsiteDataStore_PrivateModeAlwaysUsesNonPersistentStore()
    {
        var configuration = CreatePersistentConfiguration();
        configuration.ControllerOptions.IsInPrivateModeEnabled = true;

        var kind = MacOSNativeWebViewHost.ResolveWebsiteDataStoreKind(configuration, proxyConfiguration: null);

        Assert.Equal(MacOSNativeWebViewHost.MacOSWebsiteDataStoreKind.NonPersistent, kind);
    }

    [Fact]
    public void MacOSWebsiteDataStore_ProfileConfigurationUsesDedicatedPersistentStore()
    {
        var kind = MacOSNativeWebViewHost.ResolveWebsiteDataStoreKind(
            CreatePersistentConfiguration(),
            proxyConfiguration: null);

        Assert.Equal(MacOSNativeWebViewHost.MacOSWebsiteDataStoreKind.DedicatedPersistent, kind);
    }

    [Fact]
    public void MacOSWebsiteDataStore_DefaultConfigurationUsesDefaultStore()
    {
        var kind = MacOSNativeWebViewHost.ResolveWebsiteDataStoreKind(
            new NativeWebViewInstanceConfiguration(),
            proxyConfiguration: null);

        Assert.Equal(MacOSNativeWebViewHost.MacOSWebsiteDataStoreKind.Default, kind);
    }

    private static NativeWebViewInstanceConfiguration CreatePersistentConfiguration() => new()
    {
        EnvironmentOptions =
        {
            UserDataFolder = Path.GetFullPath("profile-data"),
            CacheFolder = Path.GetFullPath("profile-cache"),
        },
        ControllerOptions =
        {
            ProfileName = "profile",
        },
    };
}
