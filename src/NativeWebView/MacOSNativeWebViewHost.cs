using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Platform;
using Avalonia.Threading;
using NativeWebView.Core;

namespace NativeWebView.Controls;

internal sealed class MacOSNativeWebViewHost : IDisposable
{
    private const string DownloadTracePrefix = "NativeWebView.macOS.download";
    private const double CompositedOverlayAlpha = 0.011;
    private const int MaxPendingNavigationAttempts = 80;
    private const int DownloadBufferSize = 81920;
    private const ulong NSEventModifierFlagShift = 1UL << 17;
    private const ulong NSEventModifierFlagControl = 1UL << 18;
    private const ulong NSEventModifierFlagOption = 1UL << 19;
    private const ulong NSEventModifierFlagCommand = 1UL << 20;
    private const nuint NSViewWidthSizable = 1u << 1;
    private const nuint NSViewHeightSizable = 1u << 4;
    private static readonly TimeSpan PendingNavigationRetryInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan AcceptedNavigationStartTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly HttpClient DownloadHttpClient = new();

    private static class NativeSymbols
    {
        public static readonly IntPtr NSArrayClass = ObjC.GetClass("NSArray");
        public static readonly IntPtr NSUUIDClass = ObjC.GetClass("NSUUID");
        public static readonly IntPtr NSStringClass = ObjC.GetClass("NSString");
        public static readonly IntPtr NSMenuItemClass = ObjC.GetClass("NSMenuItem");
        public static readonly IntPtr NSURLClass = ObjC.GetClass("NSURL");
        public static readonly IntPtr NSURLRequestClass = ObjC.GetClass("NSURLRequest");
        public static readonly IntPtr NSApplicationClass = ObjC.GetClass("NSApplication");
        public static readonly IntPtr WKUserContentControllerClass = ObjC.GetClass("WKUserContentController");
        public static readonly IntPtr WKUserScriptClass = ObjC.GetClass("WKUserScript");
        public static readonly IntPtr WKWebViewClass = ObjC.GetClass("WKWebView");
        public static readonly IntPtr WKWebViewConfigurationClass = ObjC.GetClass("WKWebViewConfiguration");
        public static readonly IntPtr WKWebsiteDataStoreClass = ObjC.GetClass("WKWebsiteDataStore");

        public static readonly IntPtr SelAlloc = ObjC.GetSelector("alloc");
        public static readonly IntPtr SelInit = ObjC.GetSelector("init");
        public static readonly IntPtr SelInitWithUUIDString = ObjC.GetSelector("initWithUUIDString:");
        public static readonly IntPtr SelRetain = ObjC.GetSelector("retain");
        public static readonly IntPtr SelRelease = ObjC.GetSelector("release");
        public static readonly IntPtr SelArrayWithObject = ObjC.GetSelector("arrayWithObject:");
        public static readonly IntPtr SelRemoveFromSuperview = ObjC.GetSelector("removeFromSuperview");
        public static readonly IntPtr SelAddSubview = ObjC.GetSelector("addSubview:");
        public static readonly IntPtr SelSetAutoresizingMask = ObjC.GetSelector("setAutoresizingMask:");
        public static readonly IntPtr SelBounds = ObjC.GetSelector("bounds");
        public static readonly IntPtr SelStringWithUtf8String = ObjC.GetSelector("stringWithUTF8String:");
        public static readonly IntPtr SelUtf8String = ObjC.GetSelector("UTF8String");
        public static readonly IntPtr SelUrlWithString = ObjC.GetSelector("URLWithString:");
        public static readonly IntPtr SelFileUrlWithPath = ObjC.GetSelector("fileURLWithPath:");
        public static readonly IntPtr SelRequestWithUrl = ObjC.GetSelector("requestWithURL:");
        public static readonly IntPtr SelSetNavigationDelegate = ObjC.GetSelector("setNavigationDelegate:");
        public static readonly IntPtr SelSetUiDelegate = ObjC.GetSelector("setUIDelegate:");
        public static readonly IntPtr SelSetDelegate = ObjC.GetSelector("setDelegate:");
        public static readonly IntPtr SelSetUserContentController = ObjC.GetSelector("setUserContentController:");
        public static readonly IntPtr SelAddUserScript = ObjC.GetSelector("addUserScript:");
        public static readonly IntPtr SelAddScriptMessageHandlerName = ObjC.GetSelector("addScriptMessageHandler:name:");
        public static readonly IntPtr SelRemoveScriptMessageHandlerForName = ObjC.GetSelector("removeScriptMessageHandlerForName:");
        public static readonly IntPtr SelInitWithSourceInjectionTimeForMainFrameOnly = ObjC.GetSelector("initWithSource:injectionTime:forMainFrameOnly:");
        public static readonly IntPtr SelStartDownloadUsingRequestCompletionHandler = ObjC.GetSelector("startDownloadUsingRequest:completionHandler:");
        public static readonly IntPtr SelResumeDownloadFromResumeDataCompletionHandler = ObjC.GetSelector("resumeDownloadFromResumeData:completionHandler:");
        public static readonly IntPtr SelInitWithFrameConfiguration = ObjC.GetSelector("initWithFrame:configuration:");
        public static readonly IntPtr SelLoadRequest = ObjC.GetSelector("loadRequest:");
        public static readonly IntPtr SelReload = ObjC.GetSelector("reload");
        public static readonly IntPtr SelStopLoading = ObjC.GetSelector("stopLoading");
        public static readonly IntPtr SelGoBack = ObjC.GetSelector("goBack");
        public static readonly IntPtr SelGoForward = ObjC.GetSelector("goForward");
        public static readonly IntPtr SelSetCustomUserAgent = ObjC.GetSelector("setCustomUserAgent:");
        public static readonly IntPtr SelRespondsToSelector = ObjC.GetSelector("respondsToSelector:");
        public static readonly IntPtr SelSetPageZoom = ObjC.GetSelector("setPageZoom:");
        public static readonly IntPtr SelPrint = ObjC.GetSelector("print:");
        public static readonly IntPtr SelDataWithPdfInsideRect = ObjC.GetSelector("dataWithPDFInsideRect:");
        public static readonly IntPtr SelWriteToFileAtomically = ObjC.GetSelector("writeToFile:atomically:");
        public static readonly IntPtr SelSetHidden = ObjC.GetSelector("setHidden:");
        public static readonly IntPtr SelSetNeedsDisplay = ObjC.GetSelector("setNeedsDisplay:");
        public static readonly IntPtr SelDisplayIfNeeded = ObjC.GetSelector("displayIfNeeded");
        public static readonly IntPtr SelSetFrame = ObjC.GetSelector("setFrame:");
        public static readonly IntPtr SelSetAlphaValue = ObjC.GetSelector("setAlphaValue:");
        public static readonly IntPtr SelSuperview = ObjC.GetSelector("superview");
        public static readonly IntPtr SelWindow = ObjC.GetSelector("window");
        public static readonly IntPtr SelMakeFirstResponder = ObjC.GetSelector("makeFirstResponder:");
        public static readonly IntPtr SelBackingScaleFactor = ObjC.GetSelector("backingScaleFactor");
        public static readonly IntPtr SelBitmapImageRepForCachingDisplayInRect = ObjC.GetSelector("bitmapImageRepForCachingDisplayInRect:");
        public static readonly IntPtr SelCacheDisplayInRectToBitmapImageRep = ObjC.GetSelector("cacheDisplayInRect:toBitmapImageRep:");
        public static readonly IntPtr SelBitmapData = ObjC.GetSelector("bitmapData");
        public static readonly IntPtr SelBytesPerRow = ObjC.GetSelector("bytesPerRow");
        public static readonly IntPtr SelPixelsWide = ObjC.GetSelector("pixelsWide");
        public static readonly IntPtr SelPixelsHigh = ObjC.GetSelector("pixelsHigh");
        public static readonly IntPtr SelDataStoreForIdentifier = ObjC.GetSelector("dataStoreForIdentifier:");
        public static readonly IntPtr SelSetWebsiteDataStore = ObjC.GetSelector("setWebsiteDataStore:");
        public static readonly IntPtr SelSetProxyConfigurations = ObjC.GetSelector("setProxyConfigurations:");
        public static readonly IntPtr SelRequest = ObjC.GetSelector("request");
        public static readonly IntPtr SelUrl = ObjC.GetSelector("URL");
        public static readonly IntPtr SelAbsoluteString = ObjC.GetSelector("absoluteString");
        public static readonly IntPtr SelResponse = ObjC.GetSelector("response");
        public static readonly IntPtr SelCanGoBack = ObjC.GetSelector("canGoBack");
        public static readonly IntPtr SelCanGoForward = ObjC.GetSelector("canGoForward");
        public static readonly IntPtr SelAllHeaderFields = ObjC.GetSelector("allHeaderFields");
        public static readonly IntPtr SelObjectForKey = ObjC.GetSelector("objectForKey:");
        public static readonly IntPtr SelCanShowMimeType = ObjC.GetSelector("canShowMIMEType");
        public static readonly IntPtr SelShouldPerformDownload = ObjC.GetSelector("shouldPerformDownload");
        public static readonly IntPtr SelTargetFrame = ObjC.GetSelector("targetFrame");
        public static readonly IntPtr SelExpectedContentLength = ObjC.GetSelector("expectedContentLength");
        public static readonly IntPtr SelMimeType = ObjC.GetSelector("MIMEType");
        public static readonly IntPtr SelProgress = ObjC.GetSelector("progress");
        public static readonly IntPtr SelBody = ObjC.GetSelector("body");
        public static readonly IntPtr SelFractionCompleted = ObjC.GetSelector("fractionCompleted");
        public static readonly IntPtr SelCompletedUnitCount = ObjC.GetSelector("completedUnitCount");
        public static readonly IntPtr SelTotalUnitCount = ObjC.GetSelector("totalUnitCount");
        public static readonly IntPtr SelCancel = ObjC.GetSelector("cancel:");
        public static readonly IntPtr SelLocalizedDescription = ObjC.GetSelector("localizedDescription");
        public static readonly IntPtr SelCode = ObjC.GetSelector("code");
        public static readonly IntPtr SelSharedApplication = ObjC.GetSelector("sharedApplication");
        public static readonly IntPtr SelTerminate = ObjC.GetSelector("terminate:");
        public static readonly IntPtr SelModifierFlags = ObjC.GetSelector("modifierFlags");
        public static readonly IntPtr SelCharactersIgnoringModifiers = ObjC.GetSelector("charactersIgnoringModifiers");
        public static readonly IntPtr SelMenuForEvent = ObjC.GetSelector("menuForEvent:");
        public static readonly IntPtr SelNumberOfItems = ObjC.GetSelector("numberOfItems");
        public static readonly IntPtr SelItemAtIndex = ObjC.GetSelector("itemAtIndex:");
        public static readonly IntPtr SelTitle = ObjC.GetSelector("title");
        public static readonly IntPtr SelAddItemWithTitleActionKeyEquivalent = ObjC.GetSelector("addItemWithTitle:action:keyEquivalent:");
        public static readonly IntPtr SelSetAction = ObjC.GetSelector("setAction:");
        public static readonly IntPtr SelSetTarget = ObjC.GetSelector("setTarget:");
        public static readonly IntPtr SelNativeDownloadContextLink = ObjC.GetSelector("nativeWebViewDownloadContextLink:");
    }

    private readonly NativeWebViewInstanceConfiguration _instanceConfiguration;
    private readonly NativeWebViewDownloadManager? _downloadManager;
    private readonly Dictionary<IntPtr, DownloadContext> _downloads = [];
    private readonly HashSet<ManagedDownloadContext> _managedDownloads = [];
    private bool _disposed;
    private NativeWebViewRenderMode _renderMode = NativeWebViewRenderMode.Embedded;
    private bool _compositedPassthroughEnabled;
    private int _capturePixelWidth = 1;
    private int _capturePixelHeight = 1;
    private int _layoutRefreshVersion;
    private int _pendingNavigationVersion;
    private Uri? _pendingNavigationUri;
    private Uri? _lastNavigationUri;
    private Uri? _contextMenuDownloadUri;
    private readonly HashSet<string> _forcedDownloadUris = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IntPtr> _pendingStartDownloadBlocks = [];
    private long _captureFrameSequence;
    private GCHandle _managedHandle;
    private IntPtr _navigationDelegateHandle;
    private IntPtr _userContentControllerHandle;
    private IntPtr _downloadBridgeNameHandle;

    public MacOSNativeWebViewHost(
        IPlatformHandle parent,
        NativeWebViewInstanceConfiguration? instanceConfiguration = null,
        NativeWebViewDownloadManager? downloadManager = null)
    {
        ArgumentNullException.ThrowIfNull(parent);

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("MacOSNativeWebViewHost can only be created on macOS.");
        }

        if (parent.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent native handle is invalid.");
        }

        _instanceConfiguration = instanceConfiguration?.Clone() ?? new NativeWebViewInstanceConfiguration();
        _downloadManager = downloadManager;
        TraceDownload("host.create", $"downloadManager={downloadManager is not null}");
        _managedHandle = GCHandle.Alloc(this);
        var initialFrame = ObjC.SendCGRect(parent.Handle, NativeSymbols.SelBounds);

        ConfigurationHandle = ObjC.SendIntPtr(ObjC.SendIntPtr(NativeSymbols.WKWebViewConfigurationClass, NativeSymbols.SelAlloc), NativeSymbols.SelInit);
        ApplyProxyConfiguration();
        InstallDownloadScriptBridge();
        ViewHandle = ObjC.SendIntPtrCGRectIntPtr(
            ObjC.SendIntPtr(MacOSKeyEquivalentWebView.ClassHandle, NativeSymbols.SelAlloc),
            NativeSymbols.SelInitWithFrameConfiguration,
            initialFrame,
            ConfigurationHandle);

        if (ViewHandle == IntPtr.Zero)
        {
            TraceDownload("host.create.failed", "WKWebView handle was zero");
            throw new InvalidOperationException("Failed to create WKWebView native view.");
        }

        TraceDownload("host.create.ready", $"view=0x{ViewHandle.ToInt64():X}");
        MacOSKeyEquivalentWebView.SetOwner(ViewHandle, _managedHandle);
        ObjC.SendVoidIntPtr(parent.Handle, NativeSymbols.SelAddSubview, ViewHandle);
        InstallWebViewDelegates();
        ObjC.SendVoidCGRect(ViewHandle, NativeSymbols.SelSetFrame, initialFrame);
        ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
        ObjC.SendVoidNUInt(ViewHandle, NativeSymbols.SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);
        PlatformHandle = new PlatformHandle(ViewHandle, "NSView");
        RequestLayoutForCurrentMode();
    }

    public IPlatformHandle PlatformHandle { get; }

    public IntPtr ViewHandle { get; private set; }

    public IntPtr ConfigurationHandle { get; private set; }

    public event EventHandler<NativeWebViewNavigationStartedEventArgs>? NavigationStarted;

    public event EventHandler<NativeWebViewNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler<NativeWebViewNavigationHistoryChangedEventArgs>? NavigationHistoryChanged;

    public event EventHandler<NativeWebViewNewWindowRequestedEventArgs>? NewWindowRequested;

    public event EventHandler? NativeFocusRequested;

    public void AttachToParent(IPlatformHandle parent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parent);

        if (parent.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Parent native handle is invalid.");
        }

        ObjC.SendVoid(ViewHandle, NativeSymbols.SelRemoveFromSuperview);
        ObjC.SendVoidIntPtr(parent.Handle, NativeSymbols.SelAddSubview, ViewHandle);
        ObjC.SendVoidNUInt(ViewHandle, NativeSymbols.SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);
        RequestLayoutForCurrentMode();
        RequestPendingNavigation();
    }

    public void DetachFromParent(bool preserveRuntime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!preserveRuntime)
        {
            Dispose();
            return;
        }

        if (ViewHandle != IntPtr.Zero)
        {
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelRemoveFromSuperview);
        }
    }

    public bool SupportsRenderMode(NativeWebViewRenderMode renderMode)
    {
        return renderMode is NativeWebViewRenderMode.GpuSurface or NativeWebViewRenderMode.Offscreen;
    }

    public void SetRenderMode(NativeWebViewRenderMode renderMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _renderMode = renderMode;

        if (_renderMode == NativeWebViewRenderMode.Embedded)
        {
            ObjC.SendVoidByte(ViewHandle, NativeSymbols.SelSetHidden, 0);
            ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
        }
        else
        {
            // AppKit skips hit-testing for near-zero alpha values; keep a faint overlay so
            // mouse/keyboard continue to target the WKWebView in composited modes.
            ObjC.SendVoidByte(ViewHandle, NativeSymbols.SelSetHidden, 0);
            ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
            TryMakeFirstResponder();
        }

        RequestLayoutForCurrentMode();
    }

    public void SetCompositedPassthrough(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _compositedPassthroughEnabled = enabled;
        if (_renderMode != NativeWebViewRenderMode.Embedded && ViewHandle != IntPtr.Zero)
        {
            ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
        }
    }

    public void SetCaptureSize(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var newWidth = Math.Max(1, pixelWidth);
        var newHeight = Math.Max(1, pixelHeight);
        var captureSizeChanged = newWidth != _capturePixelWidth || newHeight != _capturePixelHeight;

        _capturePixelWidth = newWidth;
        _capturePixelHeight = newHeight;

        if (_renderMode != NativeWebViewRenderMode.Embedded && captureSizeChanged)
        {
            RequestLayoutForCurrentMode();
        }
    }

    public void UpdateLayoutForCurrentMode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero)
        {
            return;
        }

        if (_renderMode == NativeWebViewRenderMode.Embedded)
        {
            RestoreEmbeddedFrame();
        }
        else
        {
            // Composited mode keeps the view aligned with host bounds so pointer/keyboard input remains active.
            RestoreEmbeddedFrame();
        }
    }

    public bool TryCaptureFrame(
        NativeWebViewRenderMode renderMode,
        int pixelWidth,
        int pixelHeight,
        out NativeWebViewRenderFrame? frame)
    {
        frame = null;

        if (!SupportsRenderMode(renderMode) || renderMode == NativeWebViewRenderMode.Embedded)
        {
            return false;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero || !ObjC.IsMainThread())
        {
            return false;
        }

        var width = Math.Max(1, pixelWidth);
        var height = Math.Max(1, pixelHeight);
        SetCaptureSize(width, height);

        var captureRect = ObjC.SendCGRect(ViewHandle, NativeSymbols.SelBounds);
        if (captureRect.Size.Width <= 0 || captureRect.Size.Height <= 0)
        {
            var scale = GetBackingScaleFactor();
            captureRect = new CGRect(
                new CGPoint(0, 0),
                new CGSize(Math.Max(1, width / scale), Math.Max(1, height / scale)));
        }

        try
        {
            var restoreOverlayAlpha = false;
            if (_renderMode != NativeWebViewRenderMode.Embedded)
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
                restoreOverlayAlpha = true;
            }

            ObjC.SendVoidByte(ViewHandle, NativeSymbols.SelSetNeedsDisplay, 1);
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelDisplayIfNeeded);

            var bitmapRep = ObjC.SendIntPtrCGRect(ViewHandle, NativeSymbols.SelBitmapImageRepForCachingDisplayInRect, captureRect);
            if (bitmapRep == IntPtr.Zero)
            {
                return false;
            }

            ObjC.SendVoidCGRectIntPtr(ViewHandle, NativeSymbols.SelCacheDisplayInRectToBitmapImageRep, captureRect, bitmapRep);

            var bitmapData = ObjC.SendIntPtr(bitmapRep, NativeSymbols.SelBitmapData);
            if (bitmapData == IntPtr.Zero)
            {
                return false;
            }

            var bytesPerRow = ObjC.SendNInt(bitmapRep, NativeSymbols.SelBytesPerRow);
            var pixelsWide = ObjC.SendNInt(bitmapRep, NativeSymbols.SelPixelsWide);
            var pixelsHigh = ObjC.SendNInt(bitmapRep, NativeSymbols.SelPixelsHigh);

            if (bytesPerRow <= 0 || pixelsWide <= 0 || pixelsHigh <= 0)
            {
                return false;
            }

            var totalBytes = checked((int)bytesPerRow * (int)pixelsHigh);
            var pixelData = new byte[totalBytes];
            Marshal.Copy(bitmapData, pixelData, 0, totalBytes);

            frame = new NativeWebViewRenderFrame(
                (int)pixelsWide,
                (int)pixelsHigh,
                (int)bytesPerRow,
                NativeWebViewRenderPixelFormat.Bgra8888Premultiplied,
                pixelData,
                isSynthetic: false,
                frameId: Interlocked.Increment(ref _captureFrameSequence),
                capturedAtUtc: DateTimeOffset.UtcNow,
                renderMode: renderMode,
                origin: NativeWebViewRenderFrameOrigin.NativeCapture);

            if (restoreOverlayAlpha)
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
            }

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (_renderMode == NativeWebViewRenderMode.Embedded)
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, 1d);
            }
            else
            {
                ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetAlphaValue, ResolveCompositedOverlayAlpha());
            }
        }
    }

    public void Navigate(Uri uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(uri);

        _pendingNavigationUri = uri;
        TryLoadOrSchedulePendingNavigation(++_pendingNavigationVersion, attempt: 0);
    }

    private void TryLoadOrSchedulePendingNavigation(int version, int attempt)
    {
        if (_disposed || version != _pendingNavigationVersion || _pendingNavigationUri is not { } uri)
            return;

        if (CanLoadNavigation() || attempt >= MaxPendingNavigationAttempts)
        {
            if (TryLoadRequest(uri))
            {
                DispatcherTimer.RunOnce(
                    () => RetryAcceptedNavigationIfNotStarted(version, attempt + 1),
                    AcceptedNavigationStartTimeout,
                    DispatcherPriority.Background);
                return;
            }

            if (attempt >= MaxPendingNavigationAttempts)
            {
                TraceDownload("navigation.load.rejected", uri.AbsoluteUri);
                if (version == _pendingNavigationVersion)
                    _pendingNavigationUri = null;
                return;
            }
        }

        DispatcherTimer.RunOnce(
            () => TryLoadOrSchedulePendingNavigation(version, attempt + 1),
            PendingNavigationRetryInterval,
            DispatcherPriority.Background);
    }

    private void RetryAcceptedNavigationIfNotStarted(int version, int attempt)
    {
        if (_disposed ||
            version != _pendingNavigationVersion ||
            _pendingNavigationUri is null)
        {
            return;
        }

        TryLoadOrSchedulePendingNavigation(version, attempt);
    }

    private void RequestPendingNavigation()
    {
        if (_pendingNavigationUri is not null)
            TryLoadOrSchedulePendingNavigation(_pendingNavigationVersion, attempt: 0);
    }

    private void ViewDidMoveToWindow()
    {
        if (_disposed)
            return;

        TraceDownload("view.did-move-to-window", $"window=0x{ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelWindow).ToInt64():X}");
        RequestLayoutForCurrentMode();
        RequestPendingNavigation();
    }

    private bool CanLoadNavigation()
    {
        if (ViewHandle == IntPtr.Zero || ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelWindow) == IntPtr.Zero)
            return false;

        var superView = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelSuperview);
        if (superView == IntPtr.Zero)
            return false;

        var bounds = ObjC.SendCGRect(superView, NativeSymbols.SelBounds);
        return bounds.Size.Width > 0 && bounds.Size.Height > 0;
    }

    private bool TryLoadRequest(Uri uri)
    {
        _lastNavigationUri = uri;

        var nsUrl = CreateNSStringBackedObject(NativeSymbols.NSURLClass, NativeSymbols.SelUrlWithString, uri.AbsoluteUri);
        if (nsUrl == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create NSURL from URI.");
        }

        var request = ObjC.SendIntPtrIntPtr(NativeSymbols.NSURLRequestClass, NativeSymbols.SelRequestWithUrl, nsUrl);
        if (request == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create NSURLRequest.");
        }

        var navigation = ObjC.SendIntPtrIntPtr(ViewHandle, NativeSymbols.SelLoadRequest, request);
        TraceDownload("navigation.load-request", $"uri={uri.AbsoluteUri}, accepted={navigation != IntPtr.Zero}");
        return navigation != IntPtr.Zero;
    }

    public void Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelReload);
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjC.SendVoid(ViewHandle, NativeSymbols.SelStopLoading);
    }

    public void GoBack()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelGoBack);
    }

    public void GoForward()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _ = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelGoForward);
    }

    public void SetUserAgent(string? userAgent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var agent = userAgent is null
            ? IntPtr.Zero
            : CreateNSString(userAgent);

        ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelSetCustomUserAgent, agent);
    }

    public void SetZoomFactor(double zoomFactor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelSetPageZoom))
        {
            return;
        }

        ObjC.SendVoidDouble(ViewHandle, NativeSymbols.SelSetPageZoom, zoomFactor);
    }

    public NativeWebViewPrintResult Print(NativeWebViewPrintSettings? settings = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero)
        {
            return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "Native WKWebView handle is unavailable.");
        }

        if (!ObjC.IsMainThread())
        {
            return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "Print operations must run on the macOS main thread.");
        }

        if (!string.IsNullOrWhiteSpace(settings?.OutputPath))
        {
            return ExportPdf(settings.OutputPath!);
        }

        return ShowPrintUi()
            ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
            : new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "Native print UI is unavailable.");
    }

    public bool ShowPrintUi()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (ViewHandle == IntPtr.Zero || !ObjC.IsMainThread())
        {
            return false;
        }

        if (!ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelPrint))
        {
            return false;
        }

        ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelPrint, IntPtr.Zero);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var downloadContexts = _downloads.Values.ToArray();
        _downloads.Clear();
        foreach (var context in downloadContexts)
        {
            context.CancelForHostDispose();
            context.Dispose();
        }

        var managedDownloadContexts = _managedDownloads.ToArray();
        _managedDownloads.Clear();
        foreach (var context in managedDownloadContexts)
        {
            context.CancelForHostDispose();
            context.Dispose();
        }

        foreach (var block in _pendingStartDownloadBlocks.ToArray())
        {
            _pendingStartDownloadBlocks.Remove(block);
            MacOSWebKitDownloadDelegate.ReleaseStartDownloadBlock(block);
        }

        if (ViewHandle != IntPtr.Zero)
        {
            ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelSetNavigationDelegate, IntPtr.Zero);
            ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelSetUiDelegate, IntPtr.Zero);
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelStopLoading);
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelRemoveFromSuperview);
            ObjC.SendVoid(ViewHandle, NativeSymbols.SelRelease);
            ViewHandle = IntPtr.Zero;
        }

        if (_userContentControllerHandle != IntPtr.Zero)
        {
            if (_downloadBridgeNameHandle != IntPtr.Zero &&
                ObjC.SendBoolIntPtr(_userContentControllerHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelRemoveScriptMessageHandlerForName))
            {
                ObjC.SendVoidIntPtr(_userContentControllerHandle, NativeSymbols.SelRemoveScriptMessageHandlerForName, _downloadBridgeNameHandle);
            }

            ObjC.SendVoid(_userContentControllerHandle, NativeSymbols.SelRelease);
            _userContentControllerHandle = IntPtr.Zero;
        }

        if (_downloadBridgeNameHandle != IntPtr.Zero)
        {
            ObjC.SendVoid(_downloadBridgeNameHandle, NativeSymbols.SelRelease);
            _downloadBridgeNameHandle = IntPtr.Zero;
        }

        if (_navigationDelegateHandle != IntPtr.Zero)
        {
            ObjC.SendVoid(_navigationDelegateHandle, NativeSymbols.SelRelease);
            _navigationDelegateHandle = IntPtr.Zero;
        }

        if (ConfigurationHandle != IntPtr.Zero)
        {
            ObjC.SendVoid(ConfigurationHandle, NativeSymbols.SelRelease);
            ConfigurationHandle = IntPtr.Zero;
        }

        if (_managedHandle.IsAllocated)
        {
            _managedHandle.Free();
        }
    }

    private void RestoreEmbeddedFrame()
    {
        var superView = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelSuperview);
        if (superView == IntPtr.Zero)
        {
            return;
        }

        var bounds = ObjC.SendCGRect(superView, NativeSymbols.SelBounds);
        ObjC.SendVoidCGRect(ViewHandle, NativeSymbols.SelSetFrame, bounds);
    }

    private void InstallWebViewDelegates()
    {
        if (ViewHandle == IntPtr.Zero)
        {
            TraceDownload("delegate.skip", $"downloadManager={_downloadManager is not null}, view=0x{ViewHandle.ToInt64():X}");
            return;
        }

        EnsureDownloadDelegate();
        ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelSetNavigationDelegate, _navigationDelegateHandle);
        ObjC.SendVoidIntPtr(ViewHandle, NativeSymbols.SelSetUiDelegate, _navigationDelegateHandle);
        TraceDownload("delegate.installed", $"delegate=0x{_navigationDelegateHandle.ToInt64():X}");
    }

    private void InstallDownloadScriptBridge()
    {
        if (_downloadManager is null || ConfigurationHandle == IntPtr.Zero)
        {
            TraceDownload("bridge.skip", $"downloadManager={_downloadManager is not null}, configuration=0x{ConfigurationHandle.ToInt64():X}");
            return;
        }

        EnsureDownloadDelegate();
        _downloadBridgeNameHandle = CreateNSString("nativeWebViewDownload");
        if (_downloadBridgeNameHandle != IntPtr.Zero)
            _downloadBridgeNameHandle = ObjC.SendIntPtr(_downloadBridgeNameHandle, NativeSymbols.SelRetain);

        _userContentControllerHandle = ObjC.SendIntPtr(ObjC.SendIntPtr(NativeSymbols.WKUserContentControllerClass, NativeSymbols.SelAlloc), NativeSymbols.SelInit);
        if (_downloadBridgeNameHandle == IntPtr.Zero || _userContentControllerHandle == IntPtr.Zero)
        {
            TraceDownload("bridge.failed", $"name=0x{_downloadBridgeNameHandle.ToInt64():X}, controller=0x{_userContentControllerHandle.ToInt64():X}");
            return;
        }

        ObjC.SendVoidIntPtrIntPtr(
            _userContentControllerHandle,
            NativeSymbols.SelAddScriptMessageHandlerName,
            _navigationDelegateHandle,
            _downloadBridgeNameHandle);

        var sourceHandle = CreateNSString(CreateDownloadBridgeScript());
        var userScript = ObjC.SendIntPtrIntPtrNIntBool(
            ObjC.SendIntPtr(NativeSymbols.WKUserScriptClass, NativeSymbols.SelAlloc),
            NativeSymbols.SelInitWithSourceInjectionTimeForMainFrameOnly,
            sourceHandle,
            0,
            false);

        if (userScript != IntPtr.Zero)
        {
            ObjC.SendVoidIntPtr(_userContentControllerHandle, NativeSymbols.SelAddUserScript, userScript);
            ObjC.SendVoid(userScript, NativeSymbols.SelRelease);
        }
        else
        {
            TraceDownload("bridge.script.failed", "WKUserScript handle was zero");
        }

        ObjC.SendVoidIntPtr(ConfigurationHandle, NativeSymbols.SelSetUserContentController, _userContentControllerHandle);
        TraceDownload("bridge.installed", $"controller=0x{_userContentControllerHandle.ToInt64():X}, script={userScript != IntPtr.Zero}");
    }

    private void EnsureDownloadDelegate()
    {
        if (_navigationDelegateHandle == IntPtr.Zero)
            _navigationDelegateHandle = MacOSWebKitDownloadDelegate.Create(_managedHandle);
    }

    private void DecideNavigationActionPolicy(IntPtr navigationAction, IntPtr decisionHandler)
    {
        var policy = ShouldDownloadNavigationAction(navigationAction)
            ? MacOSWebKitDownloadDelegate.WKNavigationActionPolicyDownload
            : MacOSWebKitDownloadDelegate.WKNavigationActionPolicyAllow;

        TraceDownload("navigation.action.policy", $"uri={ResolveNavigationActionUri(navigationAction)?.AbsoluteUri ?? "<null>"}, policy={policy}");
        MacOSWebKitDownloadDelegate.InvokePolicyDecision(decisionHandler, policy);
    }

    private void DecideNavigationActionPolicy(IntPtr navigationAction, IntPtr preferences, IntPtr decisionHandler)
    {
        var policy = ShouldDownloadNavigationAction(navigationAction)
            ? MacOSWebKitDownloadDelegate.WKNavigationActionPolicyDownload
            : MacOSWebKitDownloadDelegate.WKNavigationActionPolicyAllow;

        TraceDownload("navigation.action.preferences.policy", $"uri={ResolveNavigationActionUri(navigationAction)?.AbsoluteUri ?? "<null>"}, policy={policy}");
        MacOSWebKitDownloadDelegate.InvokePolicyDecision(decisionHandler, policy, preferences);
    }

    private void DecideNavigationResponsePolicy(IntPtr navigationResponse, IntPtr decisionHandler)
    {
        var policy = ShouldDownloadNavigationResponse(navigationResponse)
            ? MacOSWebKitDownloadDelegate.WKNavigationResponsePolicyDownload
            : MacOSWebKitDownloadDelegate.WKNavigationResponsePolicyAllow;

        TraceDownload("navigation.response.policy", $"uri={ResolveResponseUri(ObjC.SendIntPtr(navigationResponse, NativeSymbols.SelResponse))?.AbsoluteUri ?? "<null>"}, policy={policy}");
        MacOSWebKitDownloadDelegate.InvokePolicyDecision(decisionHandler, policy);
    }

    private bool ShouldDownloadNavigationAction(IntPtr navigationAction)
    {
        if (_downloadManager is null || navigationAction == IntPtr.Zero)
            return false;

        var uri = ResolveNavigationActionUri(navigationAction);
        if (uri is not null && _forcedDownloadUris.Remove(uri.AbsoluteUri))
        {
            TraceDownload("navigation.action.force-download", uri.AbsoluteUri);
            return true;
        }

        var shouldPerformDownload =
            ObjC.SendBoolIntPtr(navigationAction, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelShouldPerformDownload) &&
            ObjC.SendBool(navigationAction, NativeSymbols.SelShouldPerformDownload);
        if (shouldPerformDownload)
            TraceDownload("navigation.action.should-perform-download", uri?.AbsoluteUri ?? "<null>");

        return shouldPerformDownload;
    }

    private bool ShouldDownloadNavigationResponse(IntPtr navigationResponse)
    {
        if (_downloadManager is null || navigationResponse == IntPtr.Zero)
            return false;

        var response = ObjC.SendIntPtr(navigationResponse, NativeSymbols.SelResponse);
        var uri = ResolveResponseUri(response);
        var mimeType = ResolveMimeType(response);
        var hasAttachment = HasAttachmentContentDisposition(response);
        var cannotShowMime =
            ObjC.SendBoolIntPtr(navigationResponse, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelCanShowMimeType) &&
            !ObjC.SendBool(navigationResponse, NativeSymbols.SelCanShowMimeType);
        var hasDownloadOnlyMime = IsDownloadOnlyMimeType(mimeType);
        var hasDownloadOnlyUri = IsDownloadOnlyUri(uri);
        if (hasAttachment || hasDownloadOnlyMime || hasDownloadOnlyUri)
        {
            TraceDownload(
                "navigation.response.download",
                $"uri={uri?.AbsoluteUri ?? "<null>"}, attachment={hasAttachment}, cannotShowMime={cannotShowMime}, downloadMime={hasDownloadOnlyMime}, downloadUri={hasDownloadOnlyUri}, mime={mimeType ?? "<null>"}");
        }
        else if (cannotShowMime)
        {
            TraceDownload(
                "navigation.response.allow.untrusted-mime",
                $"uri={uri?.AbsoluteUri ?? "<null>"}, mime={mimeType ?? "<null>"}");
        }

        return hasAttachment || hasDownloadOnlyMime || hasDownloadOnlyUri;
    }

    private void NavigationDidStart()
    {
        var uri = ResolveWebViewUri() ?? _lastNavigationUri ?? _pendingNavigationUri;
        TraceDownload("navigation.did-start", uri?.AbsoluteUri ?? "<null>");
        ClearPendingNavigation(uri);
        if (uri is not null)
            NavigationStarted?.Invoke(this, new NativeWebViewNavigationStartedEventArgs(uri, isRedirected: false));

        RaiseNavigationHistoryChanged();
    }

    private void NavigationDidFinish()
    {
        var uri = ResolveWebViewUri() ?? _lastNavigationUri ?? _pendingNavigationUri;
        TraceDownload("navigation.did-finish", uri?.AbsoluteUri ?? "<null>");
        ClearPendingNavigation(uri);
        if (uri is not null)
        {
            _lastNavigationUri = uri;
            NavigationCompleted?.Invoke(this, new NativeWebViewNavigationCompletedEventArgs(uri, isSuccess: true, httpStatusCode: 200));
        }

        RaiseNavigationHistoryChanged();
    }

    private void NavigationDidFail(IntPtr error)
    {
        var uri = ResolveWebViewUri() ?? _lastNavigationUri ?? _pendingNavigationUri;
        TraceDownload("navigation.did-fail", $"{uri?.AbsoluteUri ?? "<null>"}, error={ResolveErrorCode(error) ?? "<null>"}");
        ClearPendingNavigation(uri);
        if (uri is not null)
        {
            NavigationCompleted?.Invoke(
                this,
                new NativeWebViewNavigationCompletedEventArgs(
                    uri,
                    isSuccess: false,
                    httpStatusCode: null,
                    error: ResolveErrorCode(error)));
        }

        RaiseNavigationHistoryChanged();
    }

    private void ClearPendingNavigation(Uri? uri)
    {
        if (uri is null ||
            _pendingNavigationUri is null ||
            Uri.Compare(_pendingNavigationUri, uri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.Ordinal) != 0)
        {
            return;
        }

        _pendingNavigationUri = null;
    }

    private void RaiseNavigationHistoryChanged()
    {
        if (ViewHandle == IntPtr.Zero)
            return;

        NavigationHistoryChanged?.Invoke(
            this,
            new NativeWebViewNavigationHistoryChangedEventArgs(
                ObjC.SendBool(ViewHandle, NativeSymbols.SelCanGoBack),
                ObjC.SendBool(ViewHandle, NativeSymbols.SelCanGoForward)));
    }

    private void NavigationActionDidBecomeDownload(IntPtr navigationAction, IntPtr download)
    {
        _ = navigationAction;
        TraceDownload("navigation.action.didBecomeDownload", $"download=0x{download.ToInt64():X}");
        AttachDownloadDelegate(download);
    }

    private void NavigationResponseDidBecomeDownload(IntPtr navigationResponse, IntPtr download)
    {
        _ = navigationResponse;
        TraceDownload("navigation.response.didBecomeDownload", $"download=0x{download.ToInt64():X}");
        AttachDownloadDelegate(download);
    }

    private void CreateWebViewRequested(IntPtr navigationAction)
    {
        var uri = ResolveNavigationActionUri(navigationAction);
        TraceDownload("new-window.requested", uri?.AbsoluteUri ?? "<null>");

        var args = new NativeWebViewNewWindowRequestedEventArgs(uri);
        NewWindowRequested?.Invoke(this, args);

        if (!args.Handled && uri is not null)
        {
            Navigate(uri);
        }
    }

    private void NotifyNativeFocus()
    {
        NativeFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AttachDownloadDelegate(IntPtr download)
    {
        if (_downloadManager is null || download == IntPtr.Zero)
        {
            TraceDownload("download.delegate.skip", $"downloadManager={_downloadManager is not null}, download=0x{download.ToInt64():X}");
            return;
        }

        ObjC.SendVoidIntPtr(download, NativeSymbols.SelSetDelegate, _navigationDelegateHandle);
        TraceDownload("download.delegate.attached", $"download=0x{download.ToInt64():X}, delegate=0x{_navigationDelegateHandle.ToInt64():X}");
    }

    private void HandleDownloadBridgeMessage(string? value)
    {
        if (value?.StartsWith("context\n", StringComparison.Ordinal) == true)
        {
            UpdateContextMenuDownloadUri(value["context\n".Length..]);
            return;
        }

        if (value?.StartsWith("download\n", StringComparison.Ordinal) == true)
        {
            StartForcedDownload(value["download\n".Length..]);
            return;
        }

        StartForcedDownload(value);
    }

    private void UpdateContextMenuDownloadUri(string? value)
    {
        _contextMenuDownloadUri = ResolveBridgeUri(value);
        TraceDownload("bridge.context-link", _contextMenuDownloadUri?.AbsoluteUri ?? "<null>");
    }

    private Uri? ResolveBridgeUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var baseUri = _lastNavigationUri ?? _pendingNavigationUri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (baseUri is null || !Uri.TryCreate(baseUri, value, out uri)))
        {
            TraceDownload("bridge.message.invalid-uri", value);
            return null;
        }

        if (uri.Scheme is not ("http" or "https" or "file"))
        {
            TraceDownload("bridge.message.unsupported-scheme", uri.AbsoluteUri);
            return null;
        }

        return uri;
    }

    private void StartContextMenuDownload(string? senderTitle)
    {
        if (!IsDownloadLinkedFileMenuTitle(senderTitle))
        {
            TraceDownload("context-download.skip", $"sender={senderTitle ?? "<null>"}");
            return;
        }

        var uri = _contextMenuDownloadUri;
        _contextMenuDownloadUri = null;
        if (uri is null)
        {
            TraceDownload("context-download.skip", "no link uri");
            return;
        }

        TraceDownload("context-download.start", uri.AbsoluteUri);
        if (!TryStartWebKitDownload(uri))
            _ = StartManagedDownloadAsync(uri, options: null, cancellationToken: CancellationToken.None);
    }

    private static bool IsDownloadLinkedFileMenuTitle(string? title)
    {
        return !string.IsNullOrWhiteSpace(title) &&
               title.Contains("Download", StringComparison.OrdinalIgnoreCase) &&
               (title.Contains("Linked", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Link", StringComparison.OrdinalIgnoreCase));
    }

    private void StartForcedDownload(string? value)
    {
        TraceDownload("bridge.message", value ?? "<null>");
        if (_downloadManager is null || _disposed || string.IsNullOrWhiteSpace(value))
        {
            TraceDownload("bridge.message.ignored", $"downloadManager={_downloadManager is not null}, disposed={_disposed}, empty={string.IsNullOrWhiteSpace(value)}");
            return;
        }

        var uri = ResolveBridgeUri(value);
        if (uri is null)
            return;

        TraceDownload("bridge.download-start", uri.AbsoluteUri);
        if (!TryStartWebKitDownload(uri))
            _ = StartManagedDownloadAsync(uri, options: null, cancellationToken: CancellationToken.None);
    }

    private async Task<INativeWebViewDownloadItem?> StartManagedDownloadAsync(
        Uri uri,
        NativeWebViewDownloadRequestOptions? options,
        CancellationToken cancellationToken)
    {
        var downloadManager = _downloadManager;
        if (downloadManager is null)
            return null;

        var context = new ManagedDownloadContext();
        NativeWebViewDownloadManager.NativeWebViewDownloadItem? item = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed)
                throw new ObjectDisposedException(nameof(MacOSNativeWebViewHost));

            var preparedOptions = MergeDownloadOptions(
                options,
                new NativeWebViewDownloadRequestOptions
                {
                    SuggestedFileName = ResolveSuggestedFileName(uri, options?.SuggestedFileName),
                    DestinationPath = options?.DestinationPath,
                    AllowOverwrite = options?.AllowOverwrite == true,
                });

            var nativeOperation = new NativeWebViewDownloadNativeOperation
            {
                CancelAsync = cancelCancellationToken =>
                {
                    cancelCancellationToken.ThrowIfCancellationRequested();
                    context.Cancel();
                    return Task.FromResult(NativeWebViewDownloadActionResult.Success());
                },
            };

            AddManagedDownload(context);
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.CancellationToken);
            var starting = await downloadManager
                .PrepareDownloadAsync(uri, preparedOptions, nativeOperation, cancellationToken: linkedCancellation.Token)
                .ConfigureAwait(true);

            item = (NativeWebViewDownloadManager.NativeWebViewDownloadItem)starting.Item;
            context.Item = item;
            TraceDownload(
                "managed.prepare.completed",
                $"cancel={starting.Cancel}, destination={starting.DestinationPath ?? "<null>"}, item={item.Snapshot.Id}");

            if (_disposed || starting.Cancel || string.IsNullOrWhiteSpace(starting.DestinationPath))
            {
                if (item.Snapshot.State is not NativeWebViewDownloadState.Canceled)
                    context.MarkCanceled("Download was canceled before a destination was selected.");

                return item;
            }

            item.MarkStarted();
            await TransferManagedDownloadAsync(uri, starting.DestinationPath!, starting.AllowOverwrite, item, linkedCancellation.Token)
                .ConfigureAwait(true);

            context.MarkCompleted();
            TraceDownload("managed.completed", $"item={item.Snapshot.Id}, destination={starting.DestinationPath}");
            return item;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || context.CancellationToken.IsCancellationRequested || _disposed)
        {
            context.MarkCanceled("Download was canceled.");
            TraceDownload("managed.canceled", uri.AbsoluteUri);
            return item;
        }
        catch (Exception ex)
        {
            context.MarkFailed(ex.Message, ex.GetType().Name);
            TraceDownload("managed.failed", $"{uri.AbsoluteUri}: {ex.GetType().Name}: {ex.Message}");
            return item;
        }
        finally
        {
            RemoveManagedDownload(context);
            context.Dispose();
        }
    }

    private static async Task TransferManagedDownloadAsync(
        Uri uri,
        string destinationPath,
        bool allowOverwrite,
        NativeWebViewDownloadManager.NativeWebViewDownloadItem item,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var fileMode = allowOverwrite ? FileMode.Create : FileMode.CreateNew;
        await using var destination = new FileStream(
            fullPath,
            fileMode,
            FileAccess.Write,
            FileShare.Read,
            DownloadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (uri.IsFile)
        {
            await CopyFileDownloadAsync(uri, destination, item, cancellationToken).ConfigureAwait(true);
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await DownloadHttpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(true);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);
        await CopyDownloadStreamAsync(source, destination, item, totalBytes, cancellationToken).ConfigureAwait(true);
    }

    private static async Task CopyFileDownloadAsync(
        Uri uri,
        Stream destination,
        NativeWebViewDownloadManager.NativeWebViewDownloadItem item,
        CancellationToken cancellationToken)
    {
        var sourcePath = uri.LocalPath;
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            DownloadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var totalBytes = source.CanSeek ? (long?)source.Length : null;
        await CopyDownloadStreamAsync(source, destination, item, totalBytes, cancellationToken).ConfigureAwait(true);
    }

    private static async Task CopyDownloadStreamAsync(
        Stream source,
        Stream destination,
        NativeWebViewDownloadManager.NativeWebViewDownloadItem item,
        long? totalBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[DownloadBufferSize];
        long bytesReceived = 0;
        item.UpdateProgress(bytesReceived, totalBytes);

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(true);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(true);
            bytesReceived += read;
            item.UpdateProgress(bytesReceived, totalBytes);
        }
    }

    private void AddManagedDownload(ManagedDownloadContext context)
    {
        if (_disposed)
        {
            context.Cancel();
            return;
        }

        _managedDownloads.Add(context);
    }

    private void RemoveManagedDownload(ManagedDownloadContext context)
    {
        _managedDownloads.Remove(context);
    }

    private bool TryStartWebKitDownload(Uri uri)
    {
        if (ViewHandle == IntPtr.Zero ||
            !ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelStartDownloadUsingRequestCompletionHandler))
        {
            TraceDownload("direct-start.unavailable", $"view=0x{ViewHandle.ToInt64():X}");
            return false;
        }

        var nsUrl = CreateNSStringBackedObject(NativeSymbols.NSURLClass, NativeSymbols.SelUrlWithString, uri.AbsoluteUri);
        if (nsUrl == IntPtr.Zero)
        {
            TraceDownload("direct-start.failed-url", uri.AbsoluteUri);
            return false;
        }

        var request = ObjC.SendIntPtrIntPtr(NativeSymbols.NSURLRequestClass, NativeSymbols.SelRequestWithUrl, nsUrl);
        if (request == IntPtr.Zero)
        {
            TraceDownload("direct-start.failed-request", uri.AbsoluteUri);
            return false;
        }

        var block = MacOSWebKitDownloadDelegate.CreateStartDownloadBlock(_managedHandle);
        if (block == IntPtr.Zero)
        {
            TraceDownload("direct-start.failed-block", uri.AbsoluteUri);
            return false;
        }

        _pendingStartDownloadBlocks.Add(block);
        ObjC.SendVoidIntPtrIntPtr(ViewHandle, NativeSymbols.SelStartDownloadUsingRequestCompletionHandler, request, block);
        TraceDownload("direct-start.invoked", $"uri={uri.AbsoluteUri}, block=0x{block.ToInt64():X}");
        return true;
    }

    private void StartDownloadUsingRequestCompleted(IntPtr block, IntPtr download)
    {
        TraceDownload("direct-start.completed", $"block=0x{block.ToInt64():X}, download=0x{download.ToInt64():X}");
        if (_pendingStartDownloadBlocks.Remove(block))
            MacOSWebKitDownloadDelegate.ReleaseStartDownloadBlock(block);

        AttachDownloadDelegate(download);
    }

    private void DecideDownloadDestination(IntPtr download, IntPtr response, IntPtr suggestedFilename, IntPtr completionHandler)
    {
        TraceDownload(
            "destination.decide",
            $"download=0x{download.ToInt64():X}, uri={ResolveResponseUri(response)?.AbsoluteUri ?? "<null>"}, suggested={ObjC.StringFromNSString(suggestedFilename) ?? "<null>"}, response=0x{response.ToInt64():X}, completion=0x{completionHandler.ToInt64():X}");
        if (_downloadManager is null || download == IntPtr.Zero || completionHandler == IntPtr.Zero)
        {
            TraceDownload("destination.decide.cancel", $"downloadManager={_downloadManager is not null}");
            MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, IntPtr.Zero);
            return;
        }

        var copiedCompletionHandler = MacOSWebKitDownloadDelegate.BlockCopy(completionHandler);
        var retainedDownload = ObjC.SendIntPtr(download, NativeSymbols.SelRetain);
        if (retainedDownload == IntPtr.Zero || copiedCompletionHandler == IntPtr.Zero)
        {
            TraceDownload("destination.decide.copy-failed", $"retainedDownload=0x{retainedDownload.ToInt64():X}, copiedCompletion=0x{copiedCompletionHandler.ToInt64():X}");
            if (copiedCompletionHandler != IntPtr.Zero)
                MacOSWebKitDownloadDelegate.BlockRelease(copiedCompletionHandler);
            MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, IntPtr.Zero);
            CancelDownload(download);
            return;
        }

        _ = DecideDownloadDestinationAsync(
            retainedDownload,
            response,
            ObjC.StringFromNSString(suggestedFilename),
            copiedCompletionHandler);
    }

    private async Task DecideDownloadDestinationAsync(
        IntPtr download,
        IntPtr response,
        string? suggestedFilename,
        IntPtr completionHandler)
    {
        var downloadManager = _downloadManager;
        if (downloadManager is null)
        {
            MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, IntPtr.Zero);
            MacOSWebKitDownloadDelegate.BlockRelease(completionHandler);
            ObjC.SendVoid(download, NativeSymbols.SelRelease);
            return;
        }

        NativeWebViewDownloadManager.NativeWebViewDownloadItem? item = null;
        DownloadContext? context = null;

        try
        {
            if (_disposed)
            {
                TraceDownload("destination.prepare.disposed", $"download=0x{download.ToInt64():X}");
                CancelDownload(download);
                MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, IntPtr.Zero);
                return;
            }

            var uri = ResolveResponseUri(response) ?? _pendingNavigationUri ?? new Uri("about:blank", UriKind.Relative);
            var options = new NativeWebViewDownloadRequestOptions
            {
                SuggestedFileName = string.IsNullOrWhiteSpace(suggestedFilename) ? null : suggestedFilename,
                MimeType = ResolveMimeType(response),
                TotalBytesToReceive = ResolveExpectedContentLength(response),
            };
            var supportsPauseResume = SupportsNativeDownloadPauseResume(download);

            var nativeOperation = new NativeWebViewDownloadNativeOperation
            {
                CancelAsync = cancellationToken =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return context is null
                        ? CancelUntrackedDownloadAsync(download)
                        : context.CancelAsync(cancellationToken);
                },
                PauseAsync = supportsPauseResume
                    ? cancellationToken => context is null
                        ? Task.FromResult(NativeWebViewDownloadActionResult.InvalidState("Download has not started."))
                        : context.PauseAsync(cancellationToken)
                    : null,
                ResumeAsync = supportsPauseResume
                    ? cancellationToken => context is null
                        ? Task.FromResult(NativeWebViewDownloadActionResult.InvalidState("Download has not started."))
                        : context.ResumeAsync(cancellationToken)
                    : null,
            };

            var starting = await downloadManager
                .PrepareDownloadAsync(uri, options, nativeOperation, cancellationToken: CancellationToken.None)
                .ConfigureAwait(true);

            item = (NativeWebViewDownloadManager.NativeWebViewDownloadItem)starting.Item;
            TraceDownload(
                "destination.prepare.completed",
                $"cancel={starting.Cancel}, destination={starting.DestinationPath ?? "<null>"}, item={item.Snapshot.Id}");
            if (_disposed || starting.Cancel || string.IsNullOrWhiteSpace(starting.DestinationPath))
            {
                TraceDownload("destination.prepare.canceled", $"disposed={_disposed}, cancel={starting.Cancel}, destinationEmpty={string.IsNullOrWhiteSpace(starting.DestinationPath)}");
                if (item.Snapshot.State is not NativeWebViewDownloadState.Canceled)
                    item.MarkCanceled("Download was canceled before a destination was selected.");

                CancelDownload(download);
                MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, IntPtr.Zero);
                return;
            }

            context = new DownloadContext(this, download, item);
            _downloads[download] = context;

            if (!TryPrepareDestinationFile(starting.DestinationPath!, starting.AllowOverwrite, out var destinationError))
            {
                TraceDownload("destination.prepare-file.failed", destinationError ?? "<null>");
                _downloads.Remove(download);
                context.MarkFailed(destinationError, "DestinationUnavailable");
                context.Dispose();
                context = null;
                CancelDownload(download);
                MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, IntPtr.Zero);
                return;
            }

            var fileUrl = CreateFileUrl(starting.DestinationPath!);
            MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, fileUrl);
            item.MarkStarted();
            TraceDownload("download.started", $"item={item.Snapshot.Id}, destination={starting.DestinationPath}");
            context.StartProgressTimer();
        }
        catch (Exception ex)
        {
            TraceDownload("destination.prepare.exception", $"{ex.GetType().Name}: {ex.Message}");
            item?.MarkFailed(ex.Message, ex.GetType().Name);
            CancelDownload(download);
            MacOSWebKitDownloadDelegate.InvokeDownloadDestination(completionHandler, IntPtr.Zero);
        }
        finally
        {
            MacOSWebKitDownloadDelegate.BlockRelease(completionHandler);
            if (context is null && download != IntPtr.Zero)
                ObjC.SendVoid(download, NativeSymbols.SelRelease);
        }
    }

    private void DownloadDidFinish(IntPtr download)
    {
        if (!_downloads.Remove(download, out var context))
        {
            TraceDownload("download.finish.untracked", $"download=0x{download.ToInt64():X}");
            return;
        }

        TraceDownload("download.finish", $"download=0x{download.ToInt64():X}");
        context.MarkCompleted();
        context.Dispose();
    }

    private void DownloadDidFail(IntPtr download, IntPtr error)
    {
        if (!_downloads.Remove(download, out var context))
        {
            TraceDownload("download.fail.untracked", $"download=0x{download.ToInt64():X}, error={ResolveErrorMessage(error) ?? "<null>"}");
            return;
        }

        TraceDownload("download.fail", $"download=0x{download.ToInt64():X}, error={ResolveErrorMessage(error) ?? "<null>"}, code={ResolveErrorCode(error) ?? "<null>"}");
        if (context.IsPausing)
        {
            context.MarkPauseFailed(ResolveErrorMessage(error), ResolveErrorCode(error));
            return;
        }

        if (IsCanceledError(error))
            context.MarkCanceled(ResolveErrorMessage(error) ?? "Download was canceled.");
        else
            context.MarkFailed(ResolveErrorMessage(error), ResolveErrorCode(error));

        context.Dispose();
    }

    private void DownloadDidCancel(IntPtr download)
    {
        if (!_downloads.Remove(download, out var context))
        {
            TraceDownload("download.cancel.untracked", $"download=0x{download.ToInt64():X}");
            return;
        }

        TraceDownload("download.cancel", $"download=0x{download.ToInt64():X}");
        if (context.IsPausing)
        {
            context.MarkNativeCanceledForPause();
            return;
        }

        context.MarkCanceled("Download was canceled.");
        context.Dispose();
    }

    private bool SupportsNativeDownloadPauseResume(IntPtr download)
    {
        return ViewHandle != IntPtr.Zero &&
               download != IntPtr.Zero &&
               ObjC.SendBoolIntPtr(download, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelCancel) &&
               ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelResumeDownloadFromResumeDataCompletionHandler);
    }

    private static Task<NativeWebViewDownloadActionResult> CancelUntrackedDownloadAsync(IntPtr download)
    {
        CancelDownload(download);
        return Task.FromResult(NativeWebViewDownloadActionResult.Success());
    }

    private static void CancelDownload(IntPtr download)
    {
        if (download != IntPtr.Zero)
            ObjC.SendVoidIntPtr(download, NativeSymbols.SelCancel, IntPtr.Zero);
    }

    private Uri? ResolveResponseUri(IntPtr response)
    {
        if (response == IntPtr.Zero)
            return null;

        var url = ObjC.SendIntPtr(response, NativeSymbols.SelUrl);
        var absoluteString = ObjC.StringFromNSString(ObjC.SendIntPtr(url, NativeSymbols.SelAbsoluteString));
        return Uri.TryCreate(absoluteString, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private static Uri? ResolveNavigationActionUri(IntPtr navigationAction)
    {
        if (navigationAction == IntPtr.Zero)
            return null;

        var request = ObjC.SendIntPtr(navigationAction, NativeSymbols.SelRequest);
        var url = ObjC.SendIntPtr(request, NativeSymbols.SelUrl);
        var absoluteString = ObjC.StringFromNSString(ObjC.SendIntPtr(url, NativeSymbols.SelAbsoluteString));
        return Uri.TryCreate(absoluteString, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private Uri? ResolveWebViewUri()
    {
        if (ViewHandle == IntPtr.Zero)
            return null;

        var url = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelUrl);
        var absoluteString = ObjC.StringFromNSString(ObjC.SendIntPtr(url, NativeSymbols.SelAbsoluteString));
        return Uri.TryCreate(absoluteString, UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private static bool HasAttachmentContentDisposition(IntPtr response)
    {
        var contentDisposition = GetHttpHeader(response, "Content-Disposition");
        return contentDisposition?.Contains("attachment", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? GetHttpHeader(IntPtr response, string name)
    {
        if (response == IntPtr.Zero ||
            !ObjC.SendBoolIntPtr(response, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelAllHeaderFields))
        {
            return null;
        }

        var headers = ObjC.SendIntPtr(response, NativeSymbols.SelAllHeaderFields);
        if (headers == IntPtr.Zero)
            return null;

        var value = ObjC.SendIntPtrIntPtr(headers, NativeSymbols.SelObjectForKey, CreateNSString(name));
        if (value != IntPtr.Zero)
            return ObjC.StringFromNSString(value);

        value = ObjC.SendIntPtrIntPtr(headers, NativeSymbols.SelObjectForKey, CreateNSString(name.ToLowerInvariant()));
        return ObjC.StringFromNSString(value);
    }

    private static string? ResolveMimeType(IntPtr response)
    {
        return response == IntPtr.Zero
            ? null
            : ObjC.StringFromNSString(ObjC.SendIntPtr(response, NativeSymbols.SelMimeType));
    }

    private static bool IsDownloadOnlyMimeType(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
            return false;

        var normalized = mimeType.Trim().ToLowerInvariant();
        if (normalized.StartsWith("text/", StringComparison.Ordinal) ||
            normalized.StartsWith("image/", StringComparison.Ordinal) ||
            normalized.StartsWith("audio/", StringComparison.Ordinal) ||
            normalized.StartsWith("video/", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized is
            "application/json" or
            "application/pdf" or
            "application/rss+xml" or
            "application/xhtml+xml" or
            "application/xml")
        {
            return false;
        }

        return normalized is
            "application/octet-stream" or
            "application/msi" or
            "application/zip" or
            "application/x-7z-compressed" or
            "application/x-apple-diskimage" or
            "application/x-bzip2" or
            "application/x-gzip" or
            "application/x-msi" or
            "application/x-ms-installer" or
            "application/x-msdownload" or
            "application/x-rar-compressed" or
            "application/x-tar" or
            "application/x-xz" or
            "application/vnd.apple.installer+xml" or
            "application/vnd.microsoft.portable-executable";
    }

    private static bool IsDownloadOnlyUri(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return false;

        return Path.GetExtension(uri.LocalPath).ToLowerInvariant() is
            ".7z" or
            ".appinstaller" or
            ".bin" or
            ".bz2" or
            ".cab" or
            ".deb" or
            ".dmg" or
            ".exe" or
            ".gz" or
            ".iso" or
            ".msi" or
            ".msix" or
            ".msixbundle" or
            ".pkg" or
            ".rar" or
            ".rpm" or
            ".tar" or
            ".tgz" or
            ".xip" or
            ".xz" or
            ".zip";
    }

    private static long? ResolveExpectedContentLength(IntPtr response)
    {
        if (response == IntPtr.Zero)
            return null;

        var length = ObjC.SendNInt(response, NativeSymbols.SelExpectedContentLength);
        return length > 0 ? length : null;
    }

    private static IntPtr CreateFileUrl(string path)
    {
        var pathHandle = CreateNSString(Path.GetFullPath(path));
        return ObjC.SendIntPtrIntPtr(NativeSymbols.NSURLClass, NativeSymbols.SelFileUrlWithPath, pathHandle);
    }

    private static bool TryPrepareDestinationFile(string path, bool allowOverwrite, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return true;

            if (!allowOverwrite)
            {
                errorMessage = $"The file '{fullPath}' already exists.";
                return false;
            }

            File.Delete(fullPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool IsCanceledError(IntPtr error)
    {
        return error != IntPtr.Zero && ObjC.SendNInt(error, NativeSymbols.SelCode) == -999;
    }

    private static NativeWebViewDownloadRequestOptions MergeDownloadOptions(
        NativeWebViewDownloadRequestOptions? preferred,
        NativeWebViewDownloadRequestOptions fallback)
    {
        if (preferred is null)
            return fallback;

        return new NativeWebViewDownloadRequestOptions
        {
            SuggestedFileName = preferred.SuggestedFileName ?? fallback.SuggestedFileName,
            DestinationPath = preferred.DestinationPath ?? fallback.DestinationPath,
            AllowOverwrite = preferred.AllowOverwrite || fallback.AllowOverwrite,
            MimeType = preferred.MimeType ?? fallback.MimeType,
            ContentDisposition = preferred.ContentDisposition ?? fallback.ContentDisposition,
            TotalBytesToReceive = preferred.TotalBytesToReceive ?? fallback.TotalBytesToReceive,
        };
    }

    private static string ResolveSuggestedFileName(Uri uri, string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred;

        var path = uri.IsFile ? uri.LocalPath : uri.AbsolutePath;
        var fileName = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(fileName) ? "download" : Uri.UnescapeDataString(fileName);
    }

    private static string? ResolveErrorMessage(IntPtr error)
    {
        return error == IntPtr.Zero
            ? null
            : ObjC.StringFromNSString(ObjC.SendIntPtr(error, NativeSymbols.SelLocalizedDescription));
    }

    private static string? ResolveErrorCode(IntPtr error)
    {
        return error == IntPtr.Zero
            ? null
            : ObjC.SendNInt(error, NativeSymbols.SelCode).ToString(CultureInfo.InvariantCulture);
    }

    private static string CreateDownloadBridgeScript()
    {
        return """
            (() => {
              const handler = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeWebViewDownload;
              if (!handler) return;
              function anchorFromEvent(event) {
                const target = event.target;
                if (!target || !target.closest) return null;
                const link = target.closest('a[href]');
                if (!link || !link.href) return null;
                return link;
              }
              function post(kind, link) {
                try { handler.postMessage(kind + '\n' + (link ? link.href : '')); } catch (_) { }
              }
              document.addEventListener('click', event => {
                if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
                const link = anchorFromEvent(event);
                if (!link || !link.hasAttribute('download')) return;
                event.preventDefault();
                post('download', link);
              }, true);
              document.addEventListener('contextmenu', event => {
                post('context', anchorFromEvent(event));
              }, true);
            })();
            """;
    }

    private void RequestLayoutForCurrentMode()
    {
        var version = ++_layoutRefreshVersion;
        UpdateLayoutForCurrentMode();
        RequestPendingNavigation();

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_disposed && version == _layoutRefreshVersion)
                {
                    UpdateLayoutForCurrentMode();
                    RequestPendingNavigation();
                }
            },
            DispatcherPriority.Render);

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!_disposed && version == _layoutRefreshVersion)
                {
                    UpdateLayoutForCurrentMode();
                    RequestPendingNavigation();
                }
            },
            DispatcherPriority.Background);
    }

    private double GetBackingScaleFactor()
    {
        var window = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelWindow);
        if (window == IntPtr.Zero)
        {
            return 1d;
        }

        var scale = ObjC.SendDouble(window, NativeSymbols.SelBackingScaleFactor);
        return scale > 0 ? scale : 1d;
    }

    private void TryMakeFirstResponder()
    {
        var window = ObjC.SendIntPtr(ViewHandle, NativeSymbols.SelWindow);
        if (window == IntPtr.Zero)
        {
            return;
        }

        _ = ObjC.SendBoolIntPtr(window, NativeSymbols.SelMakeFirstResponder, ViewHandle);
    }

    private double ResolveCompositedOverlayAlpha()
    {
        return _compositedPassthroughEnabled ? 1d : CompositedOverlayAlpha;
    }

    private static void TraceDownload(string stage, string message)
    {
        Trace.WriteLine($"{DownloadTracePrefix}.{stage}: {message}");
    }

    private static IntPtr CreateNSStringBackedObject(IntPtr classHandle, IntPtr selector, string value)
    {
        var nsString = CreateNSString(value);
        return ObjC.SendIntPtrIntPtr(classHandle, selector, nsString);
    }

    private void ApplyProxyConfiguration()
    {
        var proxyConfiguration = NativeWebViewProxyConfigurationResolver.Resolve(_instanceConfiguration.EnvironmentOptions.Proxy);
        if (proxyConfiguration is null)
        {
            return;
        }

        if (proxyConfiguration.Kind == NativeWebViewProxyKind.AutoConfigUrl)
        {
            throw new PlatformNotSupportedException(
                "WKWebView proxy auto-configuration URLs are not supported by the current macOS integration. Use an explicit http(s) or socks5 proxy server.");
        }

        if (!OperatingSystem.IsMacOSVersionAtLeast(14))
        {
            throw new PlatformNotSupportedException(
                "Per-instance proxy configuration requires macOS 14.0 or later for WKWebsiteDataStore.proxyConfigurations.");
        }

        var dataStoreHandle = CreateWebsiteDataStoreHandle(proxyConfiguration);
        if (dataStoreHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create a dedicated WKWebsiteDataStore for proxy configuration.");
        }

        if (!ObjC.SendBoolIntPtr(dataStoreHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelSetProxyConfigurations))
        {
            throw new PlatformNotSupportedException(
                "The current WKWebsiteDataStore runtime does not expose proxyConfigurations.");
        }

        var nativeProxyConfiguration = CreateNativeProxyConfiguration(proxyConfiguration);
        try
        {
            var arrayHandle = ObjC.SendIntPtrIntPtr(NativeSymbols.NSArrayClass, NativeSymbols.SelArrayWithObject, nativeProxyConfiguration);
            if (arrayHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create the proxy configuration array for WKWebsiteDataStore.");
            }

            ObjC.SendVoidIntPtr(dataStoreHandle, NativeSymbols.SelSetProxyConfigurations, arrayHandle);
            ObjC.SendVoidIntPtr(ConfigurationHandle, NativeSymbols.SelSetWebsiteDataStore, dataStoreHandle);
        }
        finally
        {
            Network.nw_release(nativeProxyConfiguration);
        }
    }

    private IntPtr CreateWebsiteDataStoreHandle(NativeWebViewResolvedProxyConfiguration proxyConfiguration)
    {
        var identifier = CreateWebsiteDataStoreIdentifier(_instanceConfiguration, proxyConfiguration);
        var uuidHandle = CreateNativeUuid(identifier);
        try
        {
            return ObjC.SendIntPtrIntPtr(
                NativeSymbols.WKWebsiteDataStoreClass,
                NativeSymbols.SelDataStoreForIdentifier,
                uuidHandle);
        }
        finally
        {
            if (uuidHandle != IntPtr.Zero)
            {
                ObjC.SendVoid(uuidHandle, NativeSymbols.SelRelease);
            }
        }
    }

    private static IntPtr CreateNativeProxyConfiguration(NativeWebViewResolvedProxyConfiguration configuration)
    {
        var endpoint = CreateEndpoint(configuration.Host, configuration.Port);
        IntPtr tlsOptions = IntPtr.Zero;

        try
        {
            if (configuration.UseTls)
            {
                tlsOptions = Network.nw_tls_create_options();
                if (tlsOptions == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to create TLS options for the configured proxy.");
                }
            }

            var proxyHandle = configuration.Kind switch
            {
                NativeWebViewProxyKind.HttpConnect => Network.nw_proxy_config_create_http_connect(endpoint, tlsOptions),
                NativeWebViewProxyKind.Socks5 => Network.nw_proxy_config_create_socksv5(endpoint),
                _ => IntPtr.Zero,
            };

            if (proxyHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create the native proxy configuration.");
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(configuration.Username))
                {
                    Network.nw_proxy_config_set_username_and_password(
                        proxyHandle,
                        configuration.Username!,
                        configuration.Password);
                }

                foreach (var excludedDomain in configuration.ExcludedDomains)
                {
                    if (TryNormalizeExcludedDomain(excludedDomain, out var normalizedExcludedDomain))
                    {
                        Network.nw_proxy_config_add_excluded_domain(proxyHandle, normalizedExcludedDomain);
                    }
                }

                return proxyHandle;
            }
            catch
            {
                Network.nw_release(proxyHandle);
                throw;
            }
        }
        finally
        {
            if (tlsOptions != IntPtr.Zero)
            {
                Network.nw_release(tlsOptions);
            }

            if (endpoint != IntPtr.Zero)
            {
                Network.nw_release(endpoint);
            }
        }
    }

    private static IntPtr CreateEndpoint(string host, int port)
    {
        var hostUtf8 = Marshal.StringToCoTaskMemUTF8(host);
        var portUtf8 = Marshal.StringToCoTaskMemUTF8(port.ToString(CultureInfo.InvariantCulture));
        try
        {
            var endpoint = Network.nw_endpoint_create_host(hostUtf8, portUtf8);
            if (endpoint == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to create a proxy endpoint for '{host}:{port}'.");
            }

            return endpoint;
        }
        finally
        {
            Marshal.FreeCoTaskMem(hostUtf8);
            Marshal.FreeCoTaskMem(portUtf8);
        }
    }

    private static bool TryNormalizeExcludedDomain(string value, out string normalized)
    {
        normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.StartsWith('<') && normalized.EndsWith('>'))
        {
            return false;
        }

        if (normalized.StartsWith("*.", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith(".", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (normalized.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate(normalized, UriKind.Absolute, out var normalizedUri))
        {
            normalized = normalizedUri.Host;
        }

        normalized = normalized.Replace("*", string.Empty, StringComparison.Ordinal).Trim();
        return normalized.Length > 0;
    }

    private static Guid CreateWebsiteDataStoreIdentifier(
        NativeWebViewInstanceConfiguration configuration,
        NativeWebViewResolvedProxyConfiguration proxyConfiguration)
    {
        var builder = new StringBuilder();
        AppendIdentityPart(builder, "proxy-kind", proxyConfiguration.Kind.ToString());
        AppendIdentityPart(builder, "proxy-host", proxyConfiguration.Host);
        AppendIdentityPart(builder, "proxy-port", proxyConfiguration.Port.ToString(CultureInfo.InvariantCulture));
        AppendIdentityPart(builder, "proxy-tls", proxyConfiguration.UseTls ? "true" : "false");
        AppendIdentityPart(builder, "proxy-username", proxyConfiguration.Username);
        AppendIdentityPart(builder, "proxy-autoconfig", proxyConfiguration.AutoConfigUrl);

        var normalizedExcludedDomains = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var excludedDomain in proxyConfiguration.ExcludedDomains)
        {
            if (TryNormalizeExcludedDomain(excludedDomain, out var normalizedExcludedDomain))
            {
                normalizedExcludedDomains.Add(normalizedExcludedDomain);
            }
        }

        foreach (var excludedDomain in normalizedExcludedDomains)
        {
            AppendIdentityPart(builder, "proxy-bypass", excludedDomain);
        }

        var environmentOptions = configuration.EnvironmentOptions;
        AppendIdentityPart(builder, "user-data-folder", environmentOptions.UserDataFolder);
        AppendIdentityPart(builder, "cache-folder", environmentOptions.CacheFolder);
        AppendIdentityPart(builder, "cookie-data-folder", environmentOptions.CookieDataFolder);
        AppendIdentityPart(builder, "session-data-folder", environmentOptions.SessionDataFolder);
        AppendIdentityPart(builder, "profile-name", configuration.ControllerOptions.ProfileName);

        return CreateDeterministicGuid(builder.ToString());
    }

    private static void AppendIdentityPart(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(key)
            .Append('=')
            .Append(value.Trim())
            .Append('\n');
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);

        // Mark the identifier as a stable name-based UUID.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }

    private static IntPtr CreateNativeUuid(Guid identifier)
    {
        var identifierString = CreateNSString(identifier.ToString("D"));
        var uuidHandle = ObjC.SendIntPtrIntPtr(
            ObjC.SendIntPtr(NativeSymbols.NSUUIDClass, NativeSymbols.SelAlloc),
            NativeSymbols.SelInitWithUUIDString,
            identifierString);

        if (uuidHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to create NSUUID for website data store identifier '{identifier:D}'.");
        }

        return uuidHandle;
    }

    private NativeWebViewPrintResult ExportPdf(string outputPath)
    {
        try
        {
            if (!ObjC.SendBoolIntPtr(ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelDataWithPdfInsideRect))
            {
                return new NativeWebViewPrintResult(
                    NativeWebViewPrintStatus.NotSupported,
                    "WKWebView PDF export API is unavailable on this macOS runtime.");
            }

            var fullPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bounds = ObjC.SendCGRect(ViewHandle, NativeSymbols.SelBounds);
            var pdfData = ObjC.SendIntPtrCGRect(ViewHandle, NativeSymbols.SelDataWithPdfInsideRect, bounds);

            if (pdfData == IntPtr.Zero)
            {
                return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, "WKWebView returned no PDF data.");
            }

            var pathHandle = CreateNSString(fullPath);
            var written = ObjC.SendBoolIntPtrBool(pdfData, NativeSymbols.SelWriteToFileAtomically, pathHandle, true);
            return written
                ? new NativeWebViewPrintResult(NativeWebViewPrintStatus.Success)
                : new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, $"Failed to write PDF to '{fullPath}'.");
        }
        catch (Exception ex)
        {
            return new NativeWebViewPrintResult(NativeWebViewPrintStatus.Failed, ex.Message);
        }
    }

    private static IntPtr CreateNSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return ObjC.SendIntPtrIntPtr(NativeSymbols.NSStringClass, NativeSymbols.SelStringWithUtf8String, utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public readonly double X;
        public readonly double Y;

        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize
    {
        public readonly double Width;
        public readonly double Height;

        public CGSize(double width, double height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect
    {
        public readonly CGPoint Origin;
        public readonly CGSize Size;

        public CGRect(CGPoint origin, CGSize size)
        {
            Origin = origin;
            Size = size;
        }

        public static CGRect Zero => new(new CGPoint(0, 0), new CGSize(0, 0));
    }

    private sealed class DownloadContext : IDisposable
    {
        private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);
        private readonly MacOSNativeWebViewHost _owner;
        private readonly NativeWebViewDownloadManager.NativeWebViewDownloadItem _item;
        private IntPtr _download;
        private IntPtr _resumeData;
        private TaskCompletionSource<IntPtr>? _pauseCompletion;
        private TaskCompletionSource<IntPtr>? _resumeCompletion;
        private DispatcherTimer? _progressTimer;
        private bool _disposed;
        private bool _terminal;
        private bool _pausing;

        public DownloadContext(
            MacOSNativeWebViewHost owner,
            IntPtr download,
            NativeWebViewDownloadManager.NativeWebViewDownloadItem item)
        {
            _owner = owner;
            _download = download;
            _item = item;
        }

        public bool IsPausing => _pausing;

        public void StartProgressTimer()
        {
            if (_progressTimer is not null)
                return;

            _progressTimer = new DispatcherTimer(ProgressInterval, DispatcherPriority.Background, ProgressTimer_Tick);
            _progressTimer.Start();
            UpdateProgress();
        }

        public void MarkCompleted()
        {
            if (_terminal)
                return;

            _terminal = true;
            UpdateProgress();
            _item.MarkCompleted();
        }

        public void MarkFailed(string? message, string? code)
        {
            if (_terminal)
                return;

            _terminal = true;
            UpdateProgress();
            _item.MarkFailed(message, code);
        }

        public void MarkCanceled(string? message)
        {
            if (_terminal)
                return;

            _terminal = true;
            UpdateProgress();
            _item.MarkCanceled(message);
        }

        public void MarkCanceledFromManagedCommand()
        {
            if (_terminal)
                return;

            _terminal = true;
            _item.MarkCanceled("Download was canceled.");
        }

        public Task<NativeWebViewDownloadActionResult> CancelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_terminal)
                return Task.FromResult(NativeWebViewDownloadActionResult.InvalidState("Download has already completed."));

            if (_download == IntPtr.Zero && _resumeData != IntPtr.Zero)
            {
                MarkCanceledFromManagedCommand();
                Dispose();
                return Task.FromResult(NativeWebViewDownloadActionResult.Success());
            }

            CancelDownload(_download);
            MarkCanceledFromManagedCommand();
            return Task.FromResult(NativeWebViewDownloadActionResult.Success());
        }

        public async Task<NativeWebViewDownloadActionResult> PauseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || _terminal)
                return NativeWebViewDownloadActionResult.InvalidState("Download has already completed.");

            if (_download == IntPtr.Zero)
                return NativeWebViewDownloadActionResult.InvalidState("Download is not active.");

            if (_pausing || _pauseCompletion is not null)
                return NativeWebViewDownloadActionResult.InvalidState("Download is already pausing.");

            var state = _item.Snapshot.State;
            if (state is not NativeWebViewDownloadState.InProgress)
                return NativeWebViewDownloadActionResult.InvalidState("Only active downloads can be paused.");

            var contextHandle = GCHandle.Alloc(this);
            var block = MacOSWebKitDownloadDelegate.CreateDownloadResumeDataBlock(contextHandle);
            if (block == IntPtr.Zero)
            {
                contextHandle.Free();
                return NativeWebViewDownloadActionResult.Unsupported("This WebKit download cannot provide resume data.");
            }

            _pausing = true;
            _pauseCompletion = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
            StopProgressTimer();
            ObjC.SendVoidIntPtr(_download, NativeSymbols.SelCancel, block);

            IntPtr resumeData;
            try
            {
                resumeData = await _pauseCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                _pausing = false;
                StartProgressTimer();
                throw;
            }
            finally
            {
                _pauseCompletion = null;
            }

            if (resumeData == IntPtr.Zero)
            {
                _owner._downloads.Remove(_download);
                ReleaseCurrentDownload();
                _pausing = false;
                MarkCanceled("Download could not be paused because WebKit did not return resume data.");
                Dispose();
                return NativeWebViewDownloadActionResult.Failed("WebKit did not return resume data for this download.");
            }

            _resumeData = resumeData;
            _owner._downloads.Remove(_download);
            ReleaseCurrentDownload();
            _pausing = false;
            _item.MarkPaused();
            return NativeWebViewDownloadActionResult.Success();
        }

        public async Task<NativeWebViewDownloadActionResult> ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || _terminal)
                return NativeWebViewDownloadActionResult.InvalidState("Download has already completed.");

            if (_resumeData == IntPtr.Zero)
                return NativeWebViewDownloadActionResult.InvalidState("No resume data is available for this download.");

            if (_owner.ViewHandle == IntPtr.Zero ||
                !ObjC.SendBoolIntPtr(_owner.ViewHandle, NativeSymbols.SelRespondsToSelector, NativeSymbols.SelResumeDownloadFromResumeDataCompletionHandler))
            {
                return NativeWebViewDownloadActionResult.Unsupported("This WebKit runtime cannot resume downloads.");
            }

            if (_resumeCompletion is not null)
                return NativeWebViewDownloadActionResult.InvalidState("Download is already resuming.");

            var contextHandle = GCHandle.Alloc(this);
            var block = MacOSWebKitDownloadDelegate.CreateResumeDownloadBlock(contextHandle);
            if (block == IntPtr.Zero)
            {
                contextHandle.Free();
                return NativeWebViewDownloadActionResult.Unsupported("This WebKit runtime cannot resume downloads.");
            }

            _resumeCompletion = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
            ObjC.SendVoidIntPtrIntPtr(
                _owner.ViewHandle,
                NativeSymbols.SelResumeDownloadFromResumeDataCompletionHandler,
                _resumeData,
                block);

            IntPtr resumedDownload;
            try
            {
                resumedDownload = await _resumeCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                _resumeCompletion = null;
            }

            if (resumedDownload == IntPtr.Zero)
                return NativeWebViewDownloadActionResult.Failed("WebKit did not create a resumed download.");

            ReleaseResumeData();
            _download = resumedDownload;
            _owner.AttachDownloadDelegate(_download);
            _owner._downloads[_download] = this;
            _item.MarkResumed();
            StartProgressTimer();
            return NativeWebViewDownloadActionResult.Success();
        }

        public void CompletePause(IntPtr resumeData)
        {
            if (_pauseCompletion?.TrySetResult(resumeData) != true && resumeData != IntPtr.Zero)
                ObjC.SendVoid(resumeData, NativeSymbols.SelRelease);
        }

        public void CompleteResume(IntPtr download)
        {
            if (_resumeCompletion?.TrySetResult(download) != true && download != IntPtr.Zero)
                ObjC.SendVoid(download, NativeSymbols.SelRelease);
        }

        public void MarkNativeCanceledForPause()
        {
            TraceDownload("download.pause.native-cancel", $"download=0x{_download.ToInt64():X}");
        }

        public void MarkPauseFailed(string? message, string? code)
        {
            _pausing = false;
            _pauseCompletion?.TrySetResult(IntPtr.Zero);
            MarkFailed(message ?? "Download pause failed.", code);
            Dispose();
        }

        public void CancelForHostDispose()
        {
            if (!_terminal)
            {
                CancelDownload(_download);
                MarkCanceled("Download was canceled.");
            }
        }

        private void StopProgressTimer()
        {
            if (_progressTimer is null)
                return;

            _progressTimer.Stop();
            _progressTimer.Tick -= ProgressTimer_Tick;
            _progressTimer = null;
        }

        private void ProgressTimer_Tick(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            UpdateProgress();
        }

        private void UpdateProgress()
        {
            if (_disposed || _download == IntPtr.Zero)
                return;

            var progress = ObjC.SendIntPtr(_download, NativeSymbols.SelProgress);
            if (progress == IntPtr.Zero)
                return;

            var fraction = ObjC.SendDouble(progress, NativeSymbols.SelFractionCompleted);
            if (double.IsNaN(fraction) || double.IsInfinity(fraction) || fraction < 0)
                fraction = 0;

            var completed = ObjC.SendNInt(progress, NativeSymbols.SelCompletedUnitCount);
            var total = ObjC.SendNInt(progress, NativeSymbols.SelTotalUnitCount);
            _item.UpdateProgress(
                Math.Max(0, completed),
                total > 0 ? total : null,
                Math.Clamp(fraction, 0, 1));
        }

        private void ReleaseCurrentDownload()
        {
            if (_download == IntPtr.Zero)
                return;

            ObjC.SendVoid(_download, NativeSymbols.SelRelease);
            _download = IntPtr.Zero;
        }

        private void ReleaseResumeData()
        {
            if (_resumeData == IntPtr.Zero)
                return;

            ObjC.SendVoid(_resumeData, NativeSymbols.SelRelease);
            _resumeData = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            StopProgressTimer();
            ReleaseCurrentDownload();
            ReleaseResumeData();

            _ = _owner;
        }
    }

    private sealed class ManagedDownloadContext : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;
        private bool _terminal;

        public NativeWebViewDownloadManager.NativeWebViewDownloadItem? Item { get; set; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public void Cancel()
        {
            if (!_cancellation.IsCancellationRequested)
                _cancellation.Cancel();
        }

        public void MarkCompleted()
        {
            if (_terminal)
                return;

            _terminal = true;
            Item?.MarkCompleted();
        }

        public void MarkFailed(string? message, string? code)
        {
            if (_terminal)
                return;

            _terminal = true;
            Item?.MarkFailed(message, code);
        }

        public void MarkCanceled(string? message)
        {
            if (_terminal)
                return;

            _terminal = true;
            Item?.MarkCanceled(message);
        }

        public void CancelForHostDispose()
        {
            Cancel();
            MarkCanceled("Download was canceled.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Dispose();
        }
    }

    private static class MacOSKeyEquivalentWebView
    {
        private const string ClassName = "NativeWebViewMacOSKeyEquivalentWebView";
        private const string ManagedHandleIvarName = "_managedHandle";
        private const ulong AppCommandModifierMask =
            NSEventModifierFlagShift |
            NSEventModifierFlagControl |
            NSEventModifierFlagOption |
            NSEventModifierFlagCommand;

        private static readonly Lazy<IntPtr> ViewClass = new(CreateViewClass);
        private static readonly PerformKeyEquivalentDelegate PerformKeyEquivalentCallback = PerformKeyEquivalent;
        private static readonly ViewDidMoveToWindowDelegate ViewDidMoveToWindowCallback = ViewDidMoveToWindow;
        private static readonly AcceptsFirstMouseDelegate AcceptsFirstMouseCallback = AcceptsFirstMouse;
        private static readonly MouseEventDelegate MouseDownCallback = MouseDown;
        private static readonly MouseEventDelegate RightMouseDownCallback = RightMouseDown;
        private static readonly MouseEventDelegate OtherMouseDownCallback = OtherMouseDown;
        private static readonly MenuForEventDelegate MenuForEventCallback = MenuForEvent;
        private static readonly MenuEventDelegate WillOpenMenuCallback = WillOpenMenu;
        private static readonly MenuEventDelegate DidCloseMenuCallback = DidCloseMenu;
        private static readonly NativeDownloadContextLinkDelegate NativeDownloadContextLinkCallback = NativeDownloadContextLink;

        public static IntPtr ClassHandle => ViewClass.Value;

        public static void SetOwner(IntPtr view, GCHandle ownerHandle)
        {
            if (view == IntPtr.Zero || !ownerHandle.IsAllocated)
                return;

            ObjC.SetInstanceVariable(view, ManagedHandleIvarName, GCHandle.ToIntPtr(ownerHandle));
        }

        private static IntPtr CreateViewClass()
        {
            var classHandle = ObjC.objc_allocateClassPair(NativeSymbols.WKWebViewClass, ClassName, 0);
            if (classHandle == IntPtr.Zero)
                return ObjC.GetClass(ClassName);

            ObjC.class_addIvar(classHandle, ManagedHandleIvarName, (nuint)IntPtr.Size, IntPtr.Size == 8 ? (byte)3 : (byte)2, "^v");
            AddMethod(
                classHandle,
                "performKeyEquivalent:",
                PerformKeyEquivalentCallback,
                "c@:@");
            AddMethod(
                classHandle,
                "viewDidMoveToWindow",
                ViewDidMoveToWindowCallback,
                "v@:");
            AddMethod(
                classHandle,
                "acceptsFirstMouse:",
                AcceptsFirstMouseCallback,
                "c@:@");
            AddMethod(
                classHandle,
                "mouseDown:",
                MouseDownCallback,
                "v@:@");
            AddMethod(
                classHandle,
                "rightMouseDown:",
                RightMouseDownCallback,
                "v@:@");
            AddMethod(
                classHandle,
                "otherMouseDown:",
                OtherMouseDownCallback,
                "v@:@");
            AddMethod(
                classHandle,
                "menuForEvent:",
                MenuForEventCallback,
                "@@:@");
            AddMethod(
                classHandle,
                "willOpenMenu:withEvent:",
                WillOpenMenuCallback,
                "v@:@@");
            AddMethod(
                classHandle,
                "didCloseMenu:withEvent:",
                DidCloseMenuCallback,
                "v@:@@");
            AddMethod(
                classHandle,
                "nativeWebViewDownloadContextLink:",
                NativeDownloadContextLinkCallback,
                "v@:@");

            ObjC.objc_registerClassPair(classHandle);
            return classHandle;
        }

        private static void AddMethod<TDelegate>(IntPtr classHandle, string selectorName, TDelegate callback, string types)
            where TDelegate : Delegate
        {
            var selector = ObjC.GetSelector(selectorName);
            var implementation = Marshal.GetFunctionPointerForDelegate(callback);
            if (!ObjC.class_addMethod(classHandle, selector, implementation, types))
                throw new InvalidOperationException($"Failed to add Objective-C method '{selectorName}'.");
        }

        private static byte PerformKeyEquivalent(IntPtr self, IntPtr selector, IntPtr eventHandle)
        {
            if (IsQuitKeyEquivalent(eventHandle))
            {
                var application = ObjC.SendIntPtr(NativeSymbols.NSApplicationClass, NativeSymbols.SelSharedApplication);
                if (application != IntPtr.Zero)
                {
                    ObjC.SendVoidIntPtr(application, NativeSymbols.SelTerminate, self);
                    return 1;
                }
            }

            return ObjC.SendSuperBoolIntPtr(self, NativeSymbols.WKWebViewClass, selector, eventHandle) ? (byte)1 : (byte)0;
        }

        private static void ViewDidMoveToWindow(IntPtr self, IntPtr selector)
        {
            ObjC.SendSuperVoid(self, NativeSymbols.WKWebViewClass, selector);
            GetOwner(self)?.ViewDidMoveToWindow();
        }

        private static byte AcceptsFirstMouse(IntPtr self, IntPtr selector, IntPtr eventHandle)
        {
            _ = selector;
            _ = eventHandle;
            GetOwner(self)?.NotifyNativeFocus();
            return 1;
        }

        private static void MouseDown(IntPtr self, IntPtr selector, IntPtr eventHandle)
        {
            GetOwner(self)?.NotifyNativeFocus();
            ObjC.SendSuperVoidIntPtr(self, NativeSymbols.WKWebViewClass, selector, eventHandle);
        }

        private static void RightMouseDown(IntPtr self, IntPtr selector, IntPtr eventHandle)
        {
            GetOwner(self)?.NotifyNativeFocus();
            ObjC.SendSuperVoidIntPtr(self, NativeSymbols.WKWebViewClass, selector, eventHandle);
        }

        private static void OtherMouseDown(IntPtr self, IntPtr selector, IntPtr eventHandle)
        {
            GetOwner(self)?.NotifyNativeFocus();
            ObjC.SendSuperVoidIntPtr(self, NativeSymbols.WKWebViewClass, selector, eventHandle);
        }

        private static IntPtr MenuForEvent(IntPtr self, IntPtr selector, IntPtr eventHandle)
        {
            var menu = ObjC.SendSuperIntPtrIntPtr(self, NativeSymbols.WKWebViewClass, selector, eventHandle);
            if (menu != IntPtr.Zero)
                EnsureDownloadLinkedFileMenuItem(self, menu);

            return menu;
        }

        private static void WillOpenMenu(IntPtr self, IntPtr selector, IntPtr menu, IntPtr eventHandle)
        {
            ObjC.SendSuperVoidIntPtrIntPtr(self, NativeSymbols.WKWebViewClass, selector, menu, eventHandle);
            if (menu != IntPtr.Zero)
                EnsureDownloadLinkedFileMenuItem(self, menu);
        }

        private static void DidCloseMenu(IntPtr self, IntPtr selector, IntPtr menu, IntPtr eventHandle)
        {
            ObjC.SendSuperVoidIntPtrIntPtr(self, NativeSymbols.WKWebViewClass, selector, menu, eventHandle);
            TraceDownload("context-menu.closed", $"menu=0x{menu.ToInt64():X}");
        }

        private static void NativeDownloadContextLink(IntPtr self, IntPtr selector, IntPtr sender)
        {
            _ = selector;
            var senderTitle = ObjC.StringFromNSString(ObjC.SendIntPtr(sender, NativeSymbols.SelTitle));
            TraceDownload("context-menu.action", $"sender={senderTitle ?? "<null>"}");
            GetOwner(self)?.StartContextMenuDownload(senderTitle);
        }

        private static void EnsureDownloadLinkedFileMenuItem(IntPtr webView, IntPtr menu)
        {
            var owner = GetOwner(webView);
            if (owner?._contextMenuDownloadUri is null)
                return;

            var itemCount = ObjC.SendNInt(menu, NativeSymbols.SelNumberOfItems);
            TraceDownload("context-menu.enumerate", $"items={itemCount}, contextUri={owner._contextMenuDownloadUri.AbsoluteUri}");
            for (nint index = 0; index < itemCount; index++)
            {
                var item = ObjC.SendIntPtrNInt(menu, NativeSymbols.SelItemAtIndex, index);
                if (item == IntPtr.Zero)
                    continue;

                var title = ObjC.StringFromNSString(ObjC.SendIntPtr(item, NativeSymbols.SelTitle));
                TraceDownload("context-menu.item", $"index={index}, title={title ?? "<null>"}");
                if (!IsDownloadLinkedFileMenuTitle(title))
                    continue;

                ObjC.SendVoidIntPtr(item, NativeSymbols.SelSetTarget, webView);
                ObjC.SendVoidIntPtr(item, NativeSymbols.SelSetAction, NativeSymbols.SelNativeDownloadContextLink);
                TraceDownload("context-menu.retarget", title ?? "<null>");
                return;
            }

            var titleHandle = CreateNSString("Download Linked File");
            var keyEquivalentHandle = CreateNSString(string.Empty);
            var addedItem = ObjC.SendIntPtrIntPtrIntPtrIntPtr(
                menu,
                NativeSymbols.SelAddItemWithTitleActionKeyEquivalent,
                titleHandle,
                NativeSymbols.SelNativeDownloadContextLink,
                keyEquivalentHandle);
            if (addedItem != IntPtr.Zero)
            {
                ObjC.SendVoidIntPtr(addedItem, NativeSymbols.SelSetTarget, webView);
                TraceDownload("context-menu.inject", $"title=Download Linked File, contextUri={owner._contextMenuDownloadUri.AbsoluteUri}");
            }
            else
            {
                TraceDownload("context-menu.inject.failed", owner._contextMenuDownloadUri.AbsoluteUri);
            }
        }

        private static MacOSNativeWebViewHost? GetOwner(IntPtr self)
        {
            var handlePointer = ObjC.GetInstanceVariable(self, ManagedHandleIvarName);
            if (handlePointer == IntPtr.Zero)
                return null;

            var handle = GCHandle.FromIntPtr(handlePointer);
            return handle.Target as MacOSNativeWebViewHost;
        }

        private static bool IsQuitKeyEquivalent(IntPtr eventHandle)
        {
            if (eventHandle == IntPtr.Zero)
                return false;

            var modifierFlags = ObjC.SendNUInt(eventHandle, NativeSymbols.SelModifierFlags) & AppCommandModifierMask;
            if (modifierFlags != NSEventModifierFlagCommand)
                return false;

            var characters = ObjC.StringFromNSString(
                ObjC.SendIntPtr(eventHandle, NativeSymbols.SelCharactersIgnoringModifiers));
            return string.Equals(characters, "q", StringComparison.OrdinalIgnoreCase);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte PerformKeyEquivalentDelegate(IntPtr self, IntPtr selector, IntPtr eventHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ViewDidMoveToWindowDelegate(IntPtr self, IntPtr selector);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte AcceptsFirstMouseDelegate(IntPtr self, IntPtr selector, IntPtr eventHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MouseEventDelegate(IntPtr self, IntPtr selector, IntPtr eventHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MenuForEventDelegate(IntPtr self, IntPtr selector, IntPtr eventHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MenuEventDelegate(IntPtr self, IntPtr selector, IntPtr menu, IntPtr eventHandle);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void NativeDownloadContextLinkDelegate(IntPtr self, IntPtr selector, IntPtr sender);
    }

    private static class MacOSWebKitDownloadDelegate
    {
        public const nint WKNavigationActionPolicyCancel = 0;
        public const nint WKNavigationActionPolicyAllow = 1;
        public const nint WKNavigationActionPolicyDownload = 2;
        public const nint WKNavigationResponsePolicyCancel = 0;
        public const nint WKNavigationResponsePolicyAllow = 1;
        public const nint WKNavigationResponsePolicyDownload = 2;
        private const string ManagedHandleIvarName = "_managedHandle";
        private const int BlockHasSignature = 1 << 30;

        private static readonly Lazy<IntPtr> DelegateClass = new(CreateDelegateClass);
        private static readonly Lazy<IntPtr> StartDownloadBlockDescriptor = new(CreateStartDownloadBlockDescriptor);
        private static readonly Lazy<IntPtr> DownloadContextBlockDescriptor = new(CreateDownloadContextBlockDescriptor);
        private static readonly DecidePolicyForNavigationActionDelegate DecidePolicyForNavigationActionCallback = DecidePolicyForNavigationAction;
        private static readonly DecidePolicyForNavigationActionWithPreferencesDelegate DecidePolicyForNavigationActionWithPreferencesCallback = DecidePolicyForNavigationActionWithPreferences;
        private static readonly DecidePolicyForNavigationResponseDelegate DecidePolicyForNavigationResponseCallback = DecidePolicyForNavigationResponse;
        private static readonly DidBecomeNavigationActionDownloadDelegate DidBecomeNavigationActionDownloadCallback = DidBecomeNavigationActionDownload;
        private static readonly DidBecomeNavigationResponseDownloadDelegate DidBecomeNavigationResponseDownloadCallback = DidBecomeNavigationResponseDownload;
        private static readonly DidStartNavigationDelegate DidStartNavigationCallback = DidStartNavigation;
        private static readonly DidFinishNavigationDelegate DidFinishNavigationCallback = DidFinishNavigation;
        private static readonly DidFailNavigationDelegate DidFailNavigationCallback = DidFailNavigation;
        private static readonly DecideDestinationDelegate DecideDestinationCallback = DecideDestination;
        private static readonly DownloadDidFinishDelegate DownloadDidFinishCallback = DownloadDidFinish;
        private static readonly DownloadDidFailDelegate DownloadDidFailCallback = DownloadDidFail;
        private static readonly DownloadDidCancelDelegate DownloadDidCancelCallback = DownloadDidCancel;
        private static readonly ScriptMessageReceivedDelegate ScriptMessageReceivedCallback = ScriptMessageReceived;
        private static readonly CreateWebViewDelegate CreateWebViewCallback = CreateWebView;
        private static readonly StartDownloadCompletionDelegate StartDownloadCompletionCallback = StartDownloadCompleted;
        private static readonly DownloadContextCompletionDelegate PauseDownloadCompletedCallback = PauseDownloadCompleted;
        private static readonly DownloadContextCompletionDelegate ResumeDownloadCompletedCallback = ResumeDownloadCompleted;

        public static IntPtr Create(GCHandle ownerHandle)
        {
            var instance = ObjC.SendIntPtr(ObjC.SendIntPtr(DelegateClass.Value, NativeSymbols.SelAlloc), NativeSymbols.SelInit);
            if (instance == IntPtr.Zero)
                throw new InvalidOperationException("Failed to create WKWebView download delegate.");

            ObjC.SetInstanceVariable(instance, ManagedHandleIvarName, GCHandle.ToIntPtr(ownerHandle));
            return instance;
        }

        public static void InvokePolicyDecision(IntPtr decisionHandler, nint policy)
        {
            if (decisionHandler == IntPtr.Zero)
                return;

            var block = Marshal.PtrToStructure<BlockLiteral>(decisionHandler);
            var invoke = Marshal.GetDelegateForFunctionPointer<PolicyDecisionBlock>(block.Invoke);
            invoke(decisionHandler, policy);
        }

        public static void InvokePolicyDecision(IntPtr decisionHandler, nint policy, IntPtr preferences)
        {
            if (decisionHandler == IntPtr.Zero)
                return;

            var block = Marshal.PtrToStructure<BlockLiteral>(decisionHandler);
            var invoke = Marshal.GetDelegateForFunctionPointer<PolicyPreferencesDecisionBlock>(block.Invoke);
            invoke(decisionHandler, policy, preferences);
        }

        public static void InvokeDownloadDestination(IntPtr completionHandler, IntPtr destination)
        {
            if (completionHandler == IntPtr.Zero)
                return;

            var block = Marshal.PtrToStructure<BlockLiteral>(completionHandler);
            var invoke = Marshal.GetDelegateForFunctionPointer<DownloadDestinationBlock>(block.Invoke);
            invoke(completionHandler, destination);
        }

        public static IntPtr BlockCopy(IntPtr block)
        {
            return block == IntPtr.Zero ? IntPtr.Zero : Blocks.Block_copy(block);
        }

        public static void BlockRelease(IntPtr block)
        {
            if (block != IntPtr.Zero)
                Blocks.Block_release(block);
        }

        public static IntPtr CreateStartDownloadBlock(GCHandle ownerHandle)
        {
            var concreteBlockClass = Blocks.GetConcreteStackBlockClass();
            if (concreteBlockClass == IntPtr.Zero)
                return IntPtr.Zero;

            var literal = new StartDownloadBlockLiteral
            {
                Isa = concreteBlockClass,
                Flags = BlockHasSignature,
                Reserved = 0,
                Invoke = Marshal.GetFunctionPointerForDelegate(StartDownloadCompletionCallback),
                Descriptor = StartDownloadBlockDescriptor.Value,
                OwnerHandle = GCHandle.ToIntPtr(ownerHandle),
            };

            var stackBlock = Marshal.AllocHGlobal(Marshal.SizeOf<StartDownloadBlockLiteral>());
            try
            {
                Marshal.StructureToPtr(literal, stackBlock, fDeleteOld: false);
                return Blocks.Block_copy(stackBlock);
            }
            finally
            {
                Marshal.FreeHGlobal(stackBlock);
            }
        }

        public static void ReleaseStartDownloadBlock(IntPtr block)
        {
            BlockRelease(block);
        }

        public static IntPtr CreateDownloadResumeDataBlock(GCHandle contextHandle) =>
            CreateDownloadContextBlock(contextHandle, PauseDownloadCompletedCallback);

        public static IntPtr CreateResumeDownloadBlock(GCHandle contextHandle) =>
            CreateDownloadContextBlock(contextHandle, ResumeDownloadCompletedCallback);

        private static IntPtr CreateDownloadContextBlock(GCHandle contextHandle, DownloadContextCompletionDelegate callback)
        {
            var concreteBlockClass = Blocks.GetConcreteStackBlockClass();
            if (concreteBlockClass == IntPtr.Zero)
                return IntPtr.Zero;

            var literal = new DownloadContextBlockLiteral
            {
                Isa = concreteBlockClass,
                Flags = BlockHasSignature,
                Reserved = 0,
                Invoke = Marshal.GetFunctionPointerForDelegate(callback),
                Descriptor = DownloadContextBlockDescriptor.Value,
                ContextHandle = GCHandle.ToIntPtr(contextHandle),
            };

            var stackBlock = Marshal.AllocHGlobal(Marshal.SizeOf<DownloadContextBlockLiteral>());
            try
            {
                Marshal.StructureToPtr(literal, stackBlock, fDeleteOld: false);
                return Blocks.Block_copy(stackBlock);
            }
            finally
            {
                Marshal.FreeHGlobal(stackBlock);
            }
        }

        private static IntPtr CreateDelegateClass()
        {
            var baseClass = ObjC.GetClass("NSObject");
            var classHandle = ObjC.objc_allocateClassPair(baseClass, "NativeWebViewMacOSDownloadDelegate", 0);
            if (classHandle == IntPtr.Zero)
                classHandle = ObjC.GetClass("NativeWebViewMacOSDownloadDelegate");
            else
                RegisterMethods(classHandle);

            return classHandle;
        }

        private static void RegisterMethods(IntPtr classHandle)
        {
            ObjC.class_addIvar(classHandle, ManagedHandleIvarName, (nuint)IntPtr.Size, IntPtr.Size == 8 ? (byte)3 : (byte)2, "^v");
            AddMethod(
                classHandle,
                "webView:decidePolicyForNavigationAction:decisionHandler:",
                DecidePolicyForNavigationActionCallback,
                "v@:@@@");
            AddMethod(
                classHandle,
                "webView:decidePolicyForNavigationAction:preferences:decisionHandler:",
                DecidePolicyForNavigationActionWithPreferencesCallback,
                "v@:@@@@");
            AddMethod(
                classHandle,
                "webView:decidePolicyForNavigationResponse:decisionHandler:",
                DecidePolicyForNavigationResponseCallback,
                "v@:@@@");
            AddMethod(
                classHandle,
                "webView:navigationAction:didBecomeDownload:",
                DidBecomeNavigationActionDownloadCallback,
                "v@:@@@");
            AddMethod(
                classHandle,
                "webView:navigationResponse:didBecomeDownload:",
                DidBecomeNavigationResponseDownloadCallback,
                "v@:@@@");
            AddMethod(
                classHandle,
                "webView:didStartProvisionalNavigation:",
                DidStartNavigationCallback,
                "v@:@@");
            AddMethod(
                classHandle,
                "webView:didFinishNavigation:",
                DidFinishNavigationCallback,
                "v@:@@");
            AddMethod(
                classHandle,
                "webView:didFailNavigation:withError:",
                DidFailNavigationCallback,
                "v@:@@@");
            AddMethod(
                classHandle,
                "webView:didFailProvisionalNavigation:withError:",
                DidFailNavigationCallback,
                "v@:@@@");
            AddMethod(
                classHandle,
                "download:decideDestinationUsingResponse:suggestedFilename:completionHandler:",
                DecideDestinationCallback,
                "v@:@@@@");
            AddMethod(
                classHandle,
                "downloadDidFinish:",
                DownloadDidFinishCallback,
                "v@:@");
            AddMethod(
                classHandle,
                "download:didFailWithError:resumeData:",
                DownloadDidFailCallback,
                "v@:@@@");
            AddMethod(
                classHandle,
                "downloadDidCancel:",
                DownloadDidCancelCallback,
                "v@:@");
            AddMethod(
                classHandle,
                "userContentController:didReceiveScriptMessage:",
                ScriptMessageReceivedCallback,
                "v@:@@");
            AddMethod(
                classHandle,
                "webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:",
                CreateWebViewCallback,
                "@@:@@@@");

            ObjC.objc_registerClassPair(classHandle);
        }

        private static void AddMethod<TDelegate>(IntPtr classHandle, string selectorName, TDelegate callback, string types)
            where TDelegate : Delegate
        {
            var selector = ObjC.GetSelector(selectorName);
            var implementation = Marshal.GetFunctionPointerForDelegate(callback);
            if (!ObjC.class_addMethod(classHandle, selector, implementation, types))
                throw new InvalidOperationException($"Failed to add Objective-C method '{selectorName}'.");
        }

        private static MacOSNativeWebViewHost? GetOwner(IntPtr self)
        {
            var handle = ObjC.GetInstanceVariable(self, ManagedHandleIvarName);
            return handle == IntPtr.Zero
                ? null
                : GCHandle.FromIntPtr(handle).Target as MacOSNativeWebViewHost;
        }

        private static void DecidePolicyForNavigationAction(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigationAction, IntPtr decisionHandler)
        {
            _ = selector;
            _ = webView;
            var owner = GetOwner(self);
            if (owner is null)
                InvokePolicyDecision(decisionHandler, WKNavigationActionPolicyAllow);
            else
                owner.DecideNavigationActionPolicy(navigationAction, decisionHandler);
        }

        private static void DecidePolicyForNavigationActionWithPreferences(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigationAction, IntPtr preferences, IntPtr decisionHandler)
        {
            _ = selector;
            _ = webView;
            var owner = GetOwner(self);
            if (owner is null)
                InvokePolicyDecision(decisionHandler, WKNavigationActionPolicyAllow, preferences);
            else
                owner.DecideNavigationActionPolicy(navigationAction, preferences, decisionHandler);
        }

        private static void DecidePolicyForNavigationResponse(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigationResponse, IntPtr decisionHandler)
        {
            _ = selector;
            _ = webView;
            var owner = GetOwner(self);
            if (owner is null)
                InvokePolicyDecision(decisionHandler, WKNavigationResponsePolicyAllow);
            else
                owner.DecideNavigationResponsePolicy(navigationResponse, decisionHandler);
        }

        private static void DidBecomeNavigationActionDownload(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigationAction, IntPtr download)
        {
            _ = selector;
            _ = webView;
            GetOwner(self)?.NavigationActionDidBecomeDownload(navigationAction, download);
        }

        private static void DidBecomeNavigationResponseDownload(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigationResponse, IntPtr download)
        {
            _ = selector;
            _ = webView;
            GetOwner(self)?.NavigationResponseDidBecomeDownload(navigationResponse, download);
        }

        private static void DidStartNavigation(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation)
        {
            _ = selector;
            _ = webView;
            _ = navigation;
            GetOwner(self)?.NavigationDidStart();
        }

        private static void DidFinishNavigation(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation)
        {
            _ = selector;
            _ = webView;
            _ = navigation;
            GetOwner(self)?.NavigationDidFinish();
        }

        private static void DidFailNavigation(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation, IntPtr error)
        {
            _ = selector;
            _ = webView;
            _ = navigation;
            GetOwner(self)?.NavigationDidFail(error);
        }

        private static void DecideDestination(IntPtr self, IntPtr selector, IntPtr download, IntPtr response, IntPtr suggestedFilename, IntPtr completionHandler)
        {
            _ = selector;
            var owner = GetOwner(self);
            if (owner is null)
                InvokeDownloadDestination(completionHandler, IntPtr.Zero);
            else
                owner.DecideDownloadDestination(download, response, suggestedFilename, completionHandler);
        }

        private static void DownloadDidFinish(IntPtr self, IntPtr selector, IntPtr download)
        {
            _ = selector;
            GetOwner(self)?.DownloadDidFinish(download);
        }

        private static void DownloadDidFail(IntPtr self, IntPtr selector, IntPtr download, IntPtr error, IntPtr resumeData)
        {
            _ = selector;
            _ = resumeData;
            GetOwner(self)?.DownloadDidFail(download, error);
        }

        private static void DownloadDidCancel(IntPtr self, IntPtr selector, IntPtr download)
        {
            _ = selector;
            GetOwner(self)?.DownloadDidCancel(download);
        }

        private static void ScriptMessageReceived(IntPtr self, IntPtr selector, IntPtr userContentController, IntPtr message)
        {
            _ = selector;
            _ = userContentController;
            var body = ObjC.SendIntPtr(message, NativeSymbols.SelBody);
            GetOwner(self)?.HandleDownloadBridgeMessage(ObjC.StringFromNSString(body));
        }

        private static IntPtr CreateWebView(IntPtr self, IntPtr selector, IntPtr webView, IntPtr configuration, IntPtr navigationAction, IntPtr windowFeatures)
        {
            _ = selector;
            _ = webView;
            _ = configuration;
            _ = windowFeatures;
            GetOwner(self)?.CreateWebViewRequested(navigationAction);
            return IntPtr.Zero;
        }

        private static void StartDownloadCompleted(IntPtr block, IntPtr download)
        {
            var literal = Marshal.PtrToStructure<StartDownloadBlockLiteral>(block);
            var owner = literal.OwnerHandle == IntPtr.Zero
                ? null
                : GCHandle.FromIntPtr(literal.OwnerHandle).Target as MacOSNativeWebViewHost;

            owner?.StartDownloadUsingRequestCompleted(block, download);
        }

        private static void PauseDownloadCompleted(IntPtr block, IntPtr resumeData)
        {
            var literal = Marshal.PtrToStructure<DownloadContextBlockLiteral>(block);
            var contextHandle = literal.ContextHandle;
            var retainedResumeData = resumeData == IntPtr.Zero
                ? IntPtr.Zero
                : ObjC.SendIntPtr(resumeData, NativeSymbols.SelRetain);

            try
            {
                if (contextHandle != IntPtr.Zero &&
                    GCHandle.FromIntPtr(contextHandle).Target is DownloadContext context)
                {
                    context.CompletePause(retainedResumeData);
                }
                else if (retainedResumeData != IntPtr.Zero)
                {
                    ObjC.SendVoid(retainedResumeData, NativeSymbols.SelRelease);
                }
            }
            finally
            {
                ReleaseDownloadContextBlock(block);
            }
        }

        private static void ResumeDownloadCompleted(IntPtr block, IntPtr download)
        {
            var literal = Marshal.PtrToStructure<DownloadContextBlockLiteral>(block);
            var contextHandle = literal.ContextHandle;
            var retainedDownload = download == IntPtr.Zero
                ? IntPtr.Zero
                : ObjC.SendIntPtr(download, NativeSymbols.SelRetain);

            try
            {
                if (contextHandle != IntPtr.Zero &&
                    GCHandle.FromIntPtr(contextHandle).Target is DownloadContext context)
                {
                    context.CompleteResume(retainedDownload);
                }
                else if (retainedDownload != IntPtr.Zero)
                {
                    ObjC.SendVoid(retainedDownload, NativeSymbols.SelRelease);
                }
            }
            finally
            {
                ReleaseDownloadContextBlock(block);
            }
        }

        private static void ReleaseDownloadContextBlock(IntPtr block)
        {
            var literal = Marshal.PtrToStructure<DownloadContextBlockLiteral>(block);
            if (literal.ContextHandle != IntPtr.Zero)
                GCHandle.FromIntPtr(literal.ContextHandle).Free();

            BlockRelease(block);
        }

        private static IntPtr CreateStartDownloadBlockDescriptor()
        {
            var descriptor = new BlockDescriptor
            {
                Reserved = UIntPtr.Zero,
                Size = (UIntPtr)Marshal.SizeOf<StartDownloadBlockLiteral>(),
                Signature = Marshal.StringToHGlobalAnsi("v@?@"),
            };

            var handle = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
            Marshal.StructureToPtr(descriptor, handle, fDeleteOld: false);
            return handle;
        }

        private static IntPtr CreateDownloadContextBlockDescriptor()
        {
            var descriptor = new BlockDescriptor
            {
                Reserved = UIntPtr.Zero,
                Size = (UIntPtr)Marshal.SizeOf<DownloadContextBlockLiteral>(),
                Signature = Marshal.StringToHGlobalAnsi("v@?@"),
            };

            var handle = Marshal.AllocHGlobal(Marshal.SizeOf<BlockDescriptor>());
            Marshal.StructureToPtr(descriptor, handle, fDeleteOld: false);
            return handle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct BlockLiteral
        {
            public readonly IntPtr Isa;
            public readonly int Flags;
            public readonly int Reserved;
            public readonly IntPtr Invoke;
            public readonly IntPtr Descriptor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartDownloadBlockLiteral
        {
            public IntPtr Isa;
            public int Flags;
            public int Reserved;
            public IntPtr Invoke;
            public IntPtr Descriptor;
            public IntPtr OwnerHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DownloadContextBlockLiteral
        {
            public IntPtr Isa;
            public int Flags;
            public int Reserved;
            public IntPtr Invoke;
            public IntPtr Descriptor;
            public IntPtr ContextHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlockDescriptor
        {
            public UIntPtr Reserved;
            public UIntPtr Size;
            public IntPtr Signature;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PolicyDecisionBlock(IntPtr block, nint policy);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PolicyPreferencesDecisionBlock(IntPtr block, nint policy, IntPtr preferences);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DownloadDestinationBlock(IntPtr block, IntPtr destination);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DecidePolicyForNavigationActionDelegate(
            IntPtr self,
            IntPtr selector,
            IntPtr webView,
            IntPtr navigationAction,
            IntPtr decisionHandler);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DecidePolicyForNavigationActionWithPreferencesDelegate(
            IntPtr self,
            IntPtr selector,
            IntPtr webView,
            IntPtr navigationAction,
            IntPtr preferences,
            IntPtr decisionHandler);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DecidePolicyForNavigationResponseDelegate(
            IntPtr self,
            IntPtr selector,
            IntPtr webView,
            IntPtr navigationResponse,
            IntPtr decisionHandler);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DidBecomeNavigationActionDownloadDelegate(
            IntPtr self,
            IntPtr selector,
            IntPtr webView,
            IntPtr navigationAction,
            IntPtr download);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DidBecomeNavigationResponseDownloadDelegate(
            IntPtr self,
            IntPtr selector,
            IntPtr webView,
            IntPtr navigationResponse,
            IntPtr download);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DidStartNavigationDelegate(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DidFinishNavigationDelegate(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DidFailNavigationDelegate(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation, IntPtr error);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DecideDestinationDelegate(
            IntPtr self,
            IntPtr selector,
            IntPtr download,
            IntPtr response,
            IntPtr suggestedFilename,
            IntPtr completionHandler);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DownloadDidFinishDelegate(IntPtr self, IntPtr selector, IntPtr download);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DownloadDidFailDelegate(IntPtr self, IntPtr selector, IntPtr download, IntPtr error, IntPtr resumeData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DownloadDidCancelDelegate(IntPtr self, IntPtr selector, IntPtr download);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ScriptMessageReceivedDelegate(IntPtr self, IntPtr selector, IntPtr userContentController, IntPtr message);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr CreateWebViewDelegate(
            IntPtr self,
            IntPtr selector,
            IntPtr webView,
            IntPtr configuration,
            IntPtr navigationAction,
            IntPtr windowFeatures);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StartDownloadCompletionDelegate(IntPtr block, IntPtr download);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DownloadContextCompletionDelegate(IntPtr block, IntPtr value);
    }

    private static class ObjC
    {
        private const int RtldNow = 2;
        private static readonly object FrameworkLoadGate = new();
        private static bool _frameworksLoaded;

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern void objc_registerClassPair(IntPtr cls);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern bool class_addIvar(IntPtr cls, string name, nuint size, byte alignment, string types);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr object_setInstanceVariable(IntPtr obj, string name, IntPtr value);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        private static extern IntPtr object_getInstanceVariable(IntPtr obj, string name, out IntPtr value);

        [DllImport("/usr/lib/libSystem.B.dylib")]
        private static extern IntPtr dlopen(string path, int mode);

        [DllImport("/usr/lib/libSystem.B.dylib")]
        private static extern int pthread_main_np();

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern nint objc_msgSend_NInt(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern nuint objc_msgSend_NUInt(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_Byte(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr_IntPtr_IntPtr(
            IntPtr receiver,
            IntPtr selector,
            IntPtr arg1,
            IntPtr arg2,
            IntPtr arg3);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_NInt(IntPtr receiver, IntPtr selector, nint arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_IntPtr_NInt_Byte(IntPtr receiver, IntPtr selector, IntPtr arg1, nint arg2, byte arg3);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_CGRect_IntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern CGRect objc_msgSend_CGRect(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_Byte_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_Byte_IntPtr_Byte(IntPtr receiver, IntPtr selector, IntPtr arg1, byte arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_Double(IntPtr receiver, IntPtr selector, double arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_NUInt(IntPtr receiver, IntPtr selector, nuint arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_Byte(IntPtr receiver, IntPtr selector, byte arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_CGRect(IntPtr receiver, IntPtr selector, CGRect arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_Void_CGRect_IntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        private static extern double objc_msgSend_Double(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
        private static extern byte objc_msgSendSuper_Byte_IntPtr(ref ObjCSuper super, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
        private static extern IntPtr objc_msgSendSuper_IntPtr_IntPtr(ref ObjCSuper super, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
        private static extern void objc_msgSendSuper_Void_IntPtr_IntPtr(ref ObjCSuper super, IntPtr selector, IntPtr arg1, IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
        private static extern void objc_msgSendSuper_Void(ref ObjCSuper super, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSendSuper")]
        private static extern void objc_msgSendSuper_Void_IntPtr(ref ObjCSuper super, IntPtr selector, IntPtr arg1);

        public static IntPtr GetClass(string name)
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("Objective-C interop is only available on macOS.");
            }

            EnsureFrameworksLoaded();

            var handle = objc_getClass(name);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Objective-C class '{name}' was not found.");
            }

            return handle;
        }

        public static IntPtr GetSelector(string name)
        {
            if (!OperatingSystem.IsMacOS())
            {
                throw new PlatformNotSupportedException("Objective-C interop is only available on macOS.");
            }

            var handle = sel_registerName(name);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Objective-C selector '{name}' was not found.");
            }

            return handle;
        }

        public static IntPtr SendIntPtr(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_IntPtr(receiver, selector);
        }

        public static nint SendNInt(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_NInt(receiver, selector);
        }

        public static nuint SendNUInt(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_NUInt(receiver, selector);
        }

        public static bool SendBool(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_Byte(receiver, selector) != 0;
        }

        public static IntPtr SendIntPtrIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
        {
            return objc_msgSend_IntPtr_IntPtr(receiver, selector, arg1);
        }

        public static IntPtr SendIntPtrIntPtrIntPtrIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3)
        {
            return objc_msgSend_IntPtr_IntPtr_IntPtr_IntPtr(receiver, selector, arg1, arg2, arg3);
        }

        public static IntPtr SendIntPtrNInt(IntPtr receiver, IntPtr selector, nint arg1)
        {
            return objc_msgSend_IntPtr_NInt(receiver, selector, arg1);
        }

        public static IntPtr SendIntPtrIntPtrNIntBool(IntPtr receiver, IntPtr selector, IntPtr arg1, nint arg2, bool arg3)
        {
            return objc_msgSend_IntPtr_IntPtr_NInt_Byte(receiver, selector, arg1, arg2, arg3 ? (byte)1 : (byte)0);
        }

        public static IntPtr SendIntPtrCGRectIntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2)
        {
            return objc_msgSend_IntPtr_CGRect_IntPtr(receiver, selector, arg1, arg2);
        }

        public static IntPtr SendIntPtrCGRect(IntPtr receiver, IntPtr selector, CGRect arg1)
        {
            return objc_msgSend_IntPtr_CGRect(receiver, selector, arg1);
        }

        public static string? StringFromNSString(IntPtr nsString)
        {
            if (nsString == IntPtr.Zero)
                return null;

            var utf8 = SendIntPtr(nsString, NativeSymbols.SelUtf8String);
            return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
        }

        public static void SetInstanceVariable(IntPtr obj, string name, IntPtr value)
        {
            _ = object_setInstanceVariable(obj, name, value);
        }

        public static IntPtr GetInstanceVariable(IntPtr obj, string name)
        {
            _ = object_getInstanceVariable(obj, name, out var value);
            return value;
        }

        public static CGRect SendCGRect(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_CGRect(receiver, selector);
        }

        public static bool SendBoolIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
        {
            return objc_msgSend_Byte_IntPtr(receiver, selector, arg1) != 0;
        }

        public static bool SendBoolIntPtrBool(IntPtr receiver, IntPtr selector, IntPtr arg1, bool arg2)
        {
            return objc_msgSend_Byte_IntPtr_Byte(receiver, selector, arg1, arg2 ? (byte)1 : (byte)0) != 0;
        }

        public static bool SendSuperBoolIntPtr(IntPtr receiver, IntPtr superClass, IntPtr selector, IntPtr arg1)
        {
            var super = new ObjCSuper(receiver, superClass);
            return objc_msgSendSuper_Byte_IntPtr(ref super, selector, arg1) != 0;
        }

        public static IntPtr SendSuperIntPtrIntPtr(IntPtr receiver, IntPtr superClass, IntPtr selector, IntPtr arg1)
        {
            var super = new ObjCSuper(receiver, superClass);
            return objc_msgSendSuper_IntPtr_IntPtr(ref super, selector, arg1);
        }

        public static void SendSuperVoid(IntPtr receiver, IntPtr superClass, IntPtr selector)
        {
            var super = new ObjCSuper(receiver, superClass);
            objc_msgSendSuper_Void(ref super, selector);
        }

        public static void SendSuperVoidIntPtr(IntPtr receiver, IntPtr superClass, IntPtr selector, IntPtr arg1)
        {
            var super = new ObjCSuper(receiver, superClass);
            objc_msgSendSuper_Void_IntPtr(ref super, selector, arg1);
        }

        public static void SendSuperVoidIntPtrIntPtr(IntPtr receiver, IntPtr superClass, IntPtr selector, IntPtr arg1, IntPtr arg2)
        {
            var super = new ObjCSuper(receiver, superClass);
            objc_msgSendSuper_Void_IntPtr_IntPtr(ref super, selector, arg1, arg2);
        }

        public static double SendDouble(IntPtr receiver, IntPtr selector)
        {
            return objc_msgSend_Double(receiver, selector);
        }

        public static bool IsMainThread()
        {
            return pthread_main_np() != 0;
        }

        public static void SendVoid(IntPtr receiver, IntPtr selector)
        {
            objc_msgSend_Void(receiver, selector);
        }

        public static void SendVoidIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1)
        {
            objc_msgSend_Void_IntPtr(receiver, selector, arg1);
        }

        public static void SendVoidIntPtrIntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2)
        {
            objc_msgSend_Void_IntPtr_IntPtr(receiver, selector, arg1, arg2);
        }

        public static void SendVoidDouble(IntPtr receiver, IntPtr selector, double arg1)
        {
            objc_msgSend_Void_Double(receiver, selector, arg1);
        }

        public static void SendVoidNUInt(IntPtr receiver, IntPtr selector, nuint arg1)
        {
            objc_msgSend_Void_NUInt(receiver, selector, arg1);
        }

        public static void SendVoidByte(IntPtr receiver, IntPtr selector, byte arg1)
        {
            objc_msgSend_Void_Byte(receiver, selector, arg1);
        }

        public static void SendVoidCGRect(IntPtr receiver, IntPtr selector, CGRect arg1)
        {
            objc_msgSend_Void_CGRect(receiver, selector, arg1);
        }

        public static void SendVoidCGRectIntPtr(IntPtr receiver, IntPtr selector, CGRect arg1, IntPtr arg2)
        {
            objc_msgSend_Void_CGRect_IntPtr(receiver, selector, arg1, arg2);
        }

        private static void EnsureFrameworksLoaded()
        {
            if (_frameworksLoaded)
            {
                return;
            }

            lock (FrameworkLoadGate)
            {
                if (_frameworksLoaded)
                {
                    return;
                }

                LoadFramework("/System/Library/Frameworks/Foundation.framework/Foundation");
                LoadFramework("/System/Library/Frameworks/AppKit.framework/AppKit");
                LoadFramework("/System/Library/Frameworks/WebKit.framework/WebKit");
                LoadFramework("/System/Library/Frameworks/Network.framework/Network");
                _frameworksLoaded = true;
            }
        }

        private static void LoadFramework(string path)
        {
            var handle = dlopen(path, RtldNow);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException($"Failed to load framework '{path}'.");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ObjCSuper
        {
            public IntPtr Receiver;
            public IntPtr SuperClass;

            public ObjCSuper(IntPtr receiver, IntPtr superClass)
            {
                Receiver = receiver;
                SuperClass = superClass;
            }
        }
    }

    private static class Blocks
    {
        private const int RtldDefault = -2;
        private static readonly Lazy<IntPtr> ConcreteStackBlockClass = new(ResolveConcreteStackBlockClass);

        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_copy")]
        public static extern IntPtr Block_copy(IntPtr block);

        [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "_Block_release")]
        public static extern void Block_release(IntPtr block);

        [DllImport("/usr/lib/libSystem.B.dylib")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        public static IntPtr GetConcreteStackBlockClass()
        {
            return ConcreteStackBlockClass.Value;
        }

        private static IntPtr ResolveConcreteStackBlockClass()
        {
            return dlsym(new IntPtr(RtldDefault), "_NSConcreteStackBlock");
        }
    }

    private static class Network
    {
        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_endpoint_create_host(IntPtr hostname, IntPtr port);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_proxy_config_create_http_connect(IntPtr proxyEndpoint, IntPtr proxyTlsOptions);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_proxy_config_create_socksv5(IntPtr proxyEndpoint);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern IntPtr nw_tls_create_options();

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        private static extern void nw_proxy_config_set_username_and_password(IntPtr proxyConfig, IntPtr username, IntPtr password);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        private static extern void nw_proxy_config_add_excluded_domain(IntPtr proxyConfig, IntPtr excludedDomain);

        [DllImport("/System/Library/Frameworks/Network.framework/Network")]
        public static extern void nw_release(IntPtr obj);

        public static void nw_proxy_config_set_username_and_password(IntPtr proxyConfig, string username, string? password)
        {
            var usernameUtf8 = Marshal.StringToCoTaskMemUTF8(username);
            var passwordUtf8 = password is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(password);
            try
            {
                nw_proxy_config_set_username_and_password(proxyConfig, usernameUtf8, passwordUtf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(usernameUtf8);
                if (passwordUtf8 != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(passwordUtf8);
                }
            }
        }

        public static void nw_proxy_config_add_excluded_domain(IntPtr proxyConfig, string excludedDomain)
        {
            var excludedDomainUtf8 = Marshal.StringToCoTaskMemUTF8(excludedDomain);
            try
            {
                nw_proxy_config_add_excluded_domain(proxyConfig, excludedDomainUtf8);
            }
            finally
            {
                Marshal.FreeCoTaskMem(excludedDomainUtf8);
            }
        }

    }
}
