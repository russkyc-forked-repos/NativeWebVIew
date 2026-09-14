using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NativeWebView.Core;

namespace NativeWebView.Interop;

internal interface IMacOSDirectStoreFactory
{
    void VerifyAccess();
    string GetLockDirectory();
    IntPtr Create(Guid? persistentIdentifier, DirectSocksForwarder forwarder);
    void Release(IntPtr store);
}

// A single registry in the common assembly serves both embedded and dialog owners.
internal sealed class MacOSDirectProxyContextRegistry
{
    internal static readonly MacOSDirectProxyContextRegistry Shared = new(new MacOSDirectStoreFactory());
    private readonly IMacOSDirectStoreFactory _factory;
    private readonly string? _lockDirectory;
    private readonly Dictionary<Guid, Context> _persistent = [];
    private readonly object _gate = new();

    internal MacOSDirectProxyContextRegistry(IMacOSDirectStoreFactory factory, string? lockDirectory = null)
    {
        _factory = factory;
        _lockDirectory = lockDirectory;
    }

    internal static string GetLockDirectory(string libraryDirectory, string applicationIdentifier) =>
        Path.Combine(libraryDirectory, "NativeWebView", "direct-profile-locks",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(applicationIdentifier))));

    internal static Guid GetPersistentIdentifier(NativeWebViewInstanceConfiguration configuration)
    {
        var value = new StringBuilder("NativeWebView.Direct.v1\n");
        var options = configuration.EnvironmentOptions;
        foreach (var part in new[] { options.UserDataFolder, options.CacheFolder, options.CookieDataFolder,
                     options.SessionDataFolder, configuration.ControllerOptions.ProfileName })
        {
            var normalized = part?.Trim() ?? string.Empty;
            value.Append(normalized.Length).Append(':').Append(normalized).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new Guid(hash.AsSpan(0, 16));
    }

    internal Lease Acquire(NativeWebViewInstanceConfiguration configuration)
    {
        _factory.VerifyAccess();
        Guid? key = configuration.ControllerOptions.IsInPrivateModeEnabled ? null : GetPersistentIdentifier(configuration);
        lock (_gate)
        {
            if (key is { } id && _persistent.TryGetValue(id, out var existing))
            {
                if (!existing.Forwarder.IsHealthy)
                    throw new InvalidOperationException("The Direct proxy context is unavailable. Dispose its owners before recreating it.");
                existing.Owners++;
                return new Lease(this, existing);
            }
            FileStream? profileLock = null;
            DirectSocksForwarder? forwarder = null;
            try
            {
                if (key is { } persistentId)
                {
                    var lockDirectory = _lockDirectory ?? _factory.GetLockDirectory();
                    Directory.CreateDirectory(lockDirectory);
                    try
                    {
                        // Keep the inode stable: deleting a lock file would allow a second owner of a different inode.
                        profileLock = new FileStream(Path.Combine(lockDirectory, persistentId.ToString("N") + ".lock"),
                            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    }
                    catch (IOException ex)
                    {
                        throw new InvalidOperationException("The persistent Direct profile is already owned by another process or cannot be locked.", ex);
                    }
                }
                forwarder = new DirectSocksForwarder();
                var store = _factory.Create(key, forwarder);
                if (store == IntPtr.Zero)
                    throw new InvalidOperationException("Failed to create the Direct website data store.");
                var context = new Context(key, store, forwarder, profileLock);
                if (key is { } persistentKey)
                    _persistent.Add(persistentKey, context);
                return new Lease(this, context);
            }
            catch
            {
                if (forwarder is not null)
                    ObserveShutdown(forwarder.DisposeAsync().AsTask());
                profileLock?.Dispose();
                throw;
            }
        }
    }

    private void Release(Context context)
    {
        _factory.VerifyAccess();
        lock (_gate)
        {
            if (--context.Owners != 0)
                return;
            try { _factory.Release(context.Store); }
            finally
            {
                if (context.Key is { } key)
                    _persistent.Remove(key);
                // Cancellation/listener close starts synchronously. Socket draining does not need AppKit.
                ObserveShutdown(context.Forwarder.DisposeAsync().AsTask());
                context.ProfileLock?.Dispose();
            }
        }
    }

    private static async void ObserveShutdown(Task shutdown)
    {
        try { await shutdown.ConfigureAwait(false); }
        catch (Exception ex) { Trace.TraceError("NativeWebView Direct proxy shutdown failed: {0}", ex.GetType().Name); }
    }

    internal sealed class Context(Guid? key, IntPtr store, DirectSocksForwarder forwarder, FileStream? profileLock)
    {
        internal readonly Guid? Key = key;
        internal readonly IntPtr Store = store;
        internal readonly DirectSocksForwarder Forwarder = forwarder;
        internal readonly FileStream? ProfileLock = profileLock;
        internal int Owners = 1;
    }

    internal sealed class Lease(MacOSDirectProxyContextRegistry registry, Context context) : IDisposable
    {
        private bool _disposed;
        internal IntPtr Store => context.Store;
        internal int Port => context.Forwarder.Port;
        public void Dispose()
        {
            registry._factory.VerifyAccess();
            if (_disposed)
                return;
            _disposed = true;
            registry.Release(context);
        }
    }
}

internal sealed class MacOSDirectStoreFactory : IMacOSDirectStoreFactory
{
    public string GetLockDirectory()
    {
        VerifyAccess();
        // Use Foundation's container-aware Library directory and WebKit's bundle-ID /
        // process-name scope. Managed application-data paths can be overridden independently
        // of WebKit's storage root, and must not split ownership of the same store.
        var manager = Send(objc_getClass("NSFileManager"), sel_registerName("defaultManager"));
        var urls = Send(manager, sel_registerName("URLsForDirectory:inDomains:"), 5, 1); // NSLibraryDirectory, NSUserDomainMask
        var directory = GetString(Send(Send(urls, sel_registerName("firstObject")), sel_registerName("path")));
        var bundle = Send(objc_getClass("NSBundle"), sel_registerName("mainBundle"));
        var identifier = GetString(Send(bundle, sel_registerName("bundleIdentifier")));
        if (string.IsNullOrEmpty(identifier))
            identifier = GetString(Send(Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo")), sel_registerName("processName")));
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(identifier))
            throw new InvalidOperationException("Unable to resolve the application scope for the Direct profile lock.");
        return MacOSDirectProxyContextRegistry.GetLockDirectory(directory, identifier);
    }

    private static string? GetString(IntPtr value) => value == IntPtr.Zero ? null :
        Marshal.PtrToStringUTF8(Send(value, sel_registerName("UTF8String")));

    public void VerifyAccess()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(14))
            throw new PlatformNotSupportedException("NoProxy requires macOS 14 or later.");
        if (pthread_main_np() == 0)
            throw new InvalidOperationException("Direct website data stores must be acquired and released on the AppKit thread.");
    }

    public IntPtr Create(Guid? persistentIdentifier, DirectSocksForwarder forwarder)
    {
        VerifyAccess();
        var storeClass = objc_getClass("WKWebsiteDataStore");
        IntPtr store;
        if (persistentIdentifier is { } identifier)
        {
            var text = Marshal.StringToCoTaskMemUTF8(identifier.ToString("D"));
            IntPtr uuid = IntPtr.Zero;
            try
            {
                var nsString = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), text);
                uuid = Send(Send(objc_getClass("NSUUID"), sel_registerName("alloc")), sel_registerName("initWithUUIDString:"), nsString);
                store = Send(storeClass, sel_registerName("dataStoreForIdentifier:"), uuid);
            }
            finally
            {
                Marshal.FreeCoTaskMem(text);
                if (uuid != IntPtr.Zero) Release(uuid);
            }
        }
        else
            store = Send(storeClass, sel_registerName("nonPersistentDataStore"));
        if (store == IntPtr.Zero)
            throw new InvalidOperationException("WKWebsiteDataStore creation failed.");
        store = Send(store, sel_registerName("retain"));
        IntPtr endpoint = IntPtr.Zero, proxy = IntPtr.Zero;
        try
        {
            if (!SendBool(store, sel_registerName("respondsToSelector:"), sel_registerName("setProxyConfigurations:")))
                throw new NotSupportedException("WKWebsiteDataStore.proxyConfigurations is unavailable.");
            endpoint = nw_endpoint_create_host("127.0.0.1", forwarder.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (endpoint == IntPtr.Zero)
                throw new InvalidOperationException("Direct proxy endpoint creation failed.");
            proxy = nw_proxy_config_create_socksv5(endpoint);
            if (proxy == IntPtr.Zero)
                throw new InvalidOperationException("Direct SOCKS5 configuration creation failed.");
            nw_proxy_config_set_username_and_password(proxy, forwarder.Username, forwarder.Password);
            nw_proxy_config_set_failover_allowed(proxy, false);
            var array = Send(objc_getClass("NSArray"), sel_registerName("arrayWithObject:"), proxy);
            if (array == IntPtr.Zero)
                throw new InvalidOperationException("Direct proxy configuration array creation failed.");
            SendVoid(store, sel_registerName("setProxyConfigurations:"), array);
            return store;
        }
        catch { Release(store); throw; }
        finally
        {
            if (proxy != IntPtr.Zero) nw_release(proxy);
            if (endpoint != IntPtr.Zero) nw_release(endpoint);
        }
    }

    public void Release(IntPtr store) => SendVoid(store, sel_registerName("release"));

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Network = "/System/Library/Frameworks/Network.framework/Network";
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int pthread_main_np();
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr target, IntPtr selector, nuint directory, nuint domain);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr target, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr target, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(IntPtr target, IntPtr selector, IntPtr value);
    [DllImport(Network)] private static extern IntPtr nw_endpoint_create_host([MarshalAs(UnmanagedType.LPUTF8Str)] string host, [MarshalAs(UnmanagedType.LPUTF8Str)] string port);
    [DllImport(Network)] private static extern IntPtr nw_proxy_config_create_socksv5(IntPtr endpoint);
    [DllImport(Network)] private static extern void nw_proxy_config_set_username_and_password(IntPtr proxy, [MarshalAs(UnmanagedType.LPUTF8Str)] string username, [MarshalAs(UnmanagedType.LPUTF8Str)] string password);
    [DllImport(Network)] private static extern void nw_proxy_config_set_failover_allowed(IntPtr proxy, [MarshalAs(UnmanagedType.I1)] bool allowed);
    [DllImport(Network)] private static extern void nw_release(IntPtr value);
}
