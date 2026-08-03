using System.Text;

namespace NativeWebView.Core;

public sealed class CoreWebViewInitializedEventArgs : EventArgs
{
    public CoreWebViewInitializedEventArgs(bool isSuccess, Exception? initializationException = null, object? nativeObject = null)
    {
        IsSuccess = isSuccess;
        InitializationException = initializationException;
        NativeObject = nativeObject;
    }

    public bool IsSuccess { get; }

    public Exception? InitializationException { get; }

    public object? NativeObject { get; }
}

public sealed class NativeWebViewNavigationStartedEventArgs : EventArgs
{
    public NativeWebViewNavigationStartedEventArgs(Uri? uri, bool isRedirected)
    {
        Uri = uri;
        IsRedirected = isRedirected;
    }

    public Uri? Uri { get; }

    public bool IsRedirected { get; }

    public bool Cancel { get; set; }
}

public sealed class NativeWebViewNavigationCompletedEventArgs : EventArgs
{
    public NativeWebViewNavigationCompletedEventArgs(Uri? uri, bool isSuccess, int? httpStatusCode = null, string? error = null)
    {
        Uri = uri;
        IsSuccess = isSuccess;
        HttpStatusCode = httpStatusCode;
        Error = error;
    }

    public Uri? Uri { get; }

    public bool IsSuccess { get; }

    public int? HttpStatusCode { get; }

    public string? Error { get; }
}

public sealed class NativeWebViewMessageReceivedEventArgs : EventArgs
{
    public NativeWebViewMessageReceivedEventArgs(string? message, string? json)
    {
        Message = message;
        Json = json;
    }

    public string? Message { get; }

    public string? Json { get; }
}

public sealed class NativeWebViewOpenDevToolsRequestedEventArgs : EventArgs
{
}

public sealed class NativeWebViewDestroyRequestedEventArgs : EventArgs
{
    public NativeWebViewDestroyRequestedEventArgs(string? reason = null)
    {
        Reason = reason;
    }

    public string? Reason { get; }
}

public sealed class NativeWebViewRequestCustomChromeEventArgs : EventArgs
{
    public bool UseCustomChrome { get; set; }
}

public sealed class NativeWebViewRequestParentWindowPositionEventArgs : EventArgs
{
    public int Left { get; set; }

    public int Top { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public sealed class NativeWebViewBeginMoveDragEventArgs : EventArgs
{
}

public sealed class NativeWebViewBeginResizeDragEventArgs : EventArgs
{
    public NativeWebViewBeginResizeDragEventArgs(NativeWindowResizeEdge edge)
    {
        Edge = edge;
    }

    public NativeWindowResizeEdge Edge { get; }
}

public sealed class NativeWebViewNewWindowRequestedEventArgs : EventArgs
{
    public NativeWebViewNewWindowRequestedEventArgs(Uri? uri)
    {
        Uri = uri;
    }

    public Uri? Uri { get; }

    public bool Handled { get; set; }
}

public sealed class NativeWebViewResourceRequestedEventArgs : EventArgs
{
    public NativeWebViewResourceRequestedEventArgs(Uri? uri, string method, IReadOnlyDictionary<string, string>? headers = null)
    {
        Uri = uri;
        Method = method;
        Headers = headers ?? EmptyReadOnlyDictionary.Instance;
    }

    public Uri? Uri { get; }

    public string Method { get; }

    public IReadOnlyDictionary<string, string> Headers { get; }

    public bool Handled { get; set; }

    public int StatusCode { get; set; } = 200;

    public string? ContentType { get; set; }

    public string? ResponseBody { get; set; }
}

/// <summary>Identifies the kind of an application-provided native web view context-menu item.</summary>
public enum NativeWebViewContextMenuItemKind
{
    /// <summary>A selectable command.</summary>
    Command = 0,
    /// <summary>A visual separator.</summary>
    Separator,
    /// <summary>A nested submenu.</summary>
    Submenu,
}

/// <summary>
/// Provides immutable encoded image data for a native context-menu icon, with a required PNG fallback
/// and an optional SVG representation for backends that support scalable menu artwork.
/// </summary>
public sealed class NativeWebViewContextMenuIcon
{
    private readonly byte[] _pngData;
    private readonly byte[] _svgData;
    private readonly byte[] _highDensityPngData;

    /// <summary>Initializes an icon from encoded PNG image data.</summary>
    public NativeWebViewContextMenuIcon(ReadOnlySpan<byte> pngData)
        : this(pngData, ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty)
    {
    }

    /// <summary>
    /// Initializes an icon from encoded PNG fallback data and optional UTF-8 SVG data.
    /// Backends with SVG support should prefer <paramref name="svgData"/> to avoid DPI scaling artifacts.
    /// </summary>
    public NativeWebViewContextMenuIcon(ReadOnlySpan<byte> pngData, ReadOnlySpan<byte> svgData)
        : this(pngData, svgData, ReadOnlySpan<byte>.Empty)
    {
    }

    /// <summary>
    /// Initializes an icon from encoded PNG fallback data, optional UTF-8 SVG data, and optional high-density PNG data.
    /// The high-density PNG provides a reliable DPI-aware representation for native backends whose SVG decoder is unavailable.
    /// </summary>
    public NativeWebViewContextMenuIcon(
        ReadOnlySpan<byte> pngData,
        ReadOnlySpan<byte> svgData,
        ReadOnlySpan<byte> highDensityPngData)
    {
        if (!HasPngSignature(pngData))
        {
            throw new ArgumentException("Context-menu icons must contain encoded PNG image data.", nameof(pngData));
        }
        if (!svgData.IsEmpty &&
            !Encoding.UTF8.GetString(svgData).Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Scalable context-menu icon data must contain a UTF-8 SVG document.", nameof(svgData));
        }
        if (!highDensityPngData.IsEmpty && !HasPngSignature(highDensityPngData))
        {
            throw new ArgumentException(
                "High-density context-menu icons must contain encoded PNG image data.",
                nameof(highDensityPngData));
        }

        _pngData = pngData.ToArray();
        _svgData = svgData.ToArray();
        _highDensityPngData = highDensityPngData.ToArray();
    }

    /// <summary>Gets immutable encoded PNG image data.</summary>
    public ReadOnlyMemory<byte> PngData => _pngData;

    /// <summary>
    /// Gets immutable UTF-8 SVG image data, or an empty value when no scalable representation was supplied.
    /// </summary>
    public ReadOnlyMemory<byte> SvgData => _svgData;

    /// <summary>
    /// Gets immutable encoded high-density PNG data, or an empty value when no high-density representation was supplied.
    /// </summary>
    public ReadOnlyMemory<byte> HighDensityPngData => _highDensityPngData;

    private static bool HasPngSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 8 &&
        data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
}

/// <summary>Describes an application-provided native web view context-menu item.</summary>
public sealed class NativeWebViewContextMenuItem
{
    /// <summary>Initializes a context-menu item.</summary>
    public NativeWebViewContextMenuItem(
        string id,
        string label,
        NativeWebViewContextMenuItemKind kind = NativeWebViewContextMenuItemKind.Command,
        bool isEnabled = true,
        IReadOnlyList<NativeWebViewContextMenuItem>? children = null,
        NativeWebViewContextMenuIcon? icon = null)
    {
        Id = string.IsNullOrWhiteSpace(id) && kind != NativeWebViewContextMenuItemKind.Separator
            ? throw new ArgumentException("Context-menu command identifiers must not be empty.", nameof(id))
            : id ?? string.Empty;
        Label = label ?? throw new ArgumentNullException(nameof(label));
        Kind = kind;
        IsEnabled = isEnabled;
        Icon = icon;
        Children = children ?? Array.Empty<NativeWebViewContextMenuItem>();
        if (kind != NativeWebViewContextMenuItemKind.Submenu && Children.Count != 0)
            throw new ArgumentException("Only submenu items can contain children.", nameof(children));
        if (kind == NativeWebViewContextMenuItemKind.Separator && icon is not null)
            throw new ArgumentException("Separator items cannot contain icons.", nameof(icon));
    }

    /// <summary>Gets the application-defined command identifier.</summary>
    public string Id { get; }
    /// <summary>Gets the user-visible label.</summary>
    public string Label { get; }
    /// <summary>Gets the item kind.</summary>
    public NativeWebViewContextMenuItemKind Kind { get; }
    /// <summary>Gets whether the item can be selected.</summary>
    public bool IsEnabled { get; }
    /// <summary>
    /// Gets the optional icon. Backends should prefer its SVG representation when supported and otherwise use its PNG fallback.
    /// Backends without native context-menu image support ignore this value.
    /// </summary>
    public NativeWebViewContextMenuIcon? Icon { get; }
    /// <summary>Gets child items for a submenu.</summary>
    public IReadOnlyList<NativeWebViewContextMenuItem> Children { get; }
}

/// <summary>Identifies the editable page target which opened a native context menu.</summary>
public sealed class NativeWebViewContextMenuTarget
{
    /// <summary>Initializes an opaque context-menu target.</summary>
    public NativeWebViewContextMenuTarget(string token, bool isEditable, Uri? pageUri = null, Uri? frameUri = null, bool isMainFrame = true)
    {
        Token = string.IsNullOrWhiteSpace(token)
            ? throw new ArgumentException("Context-menu target tokens must not be empty.", nameof(token))
            : token;
        IsEditable = isEditable;
        PageUri = pageUri;
        FrameUri = frameUri;
        IsMainFrame = isMainFrame;
    }

    /// <summary>Gets the opaque, short-lived target token.</summary>
    public string Token { get; }
    /// <summary>Gets whether the target accepts text input.</summary>
    public bool IsEditable { get; }
    /// <summary>Gets the top-level page URI, when known.</summary>
    public Uri? PageUri { get; }
    /// <summary>Gets the owning frame URI, when known.</summary>
    public Uri? FrameUri { get; }
    /// <summary>Gets whether the target belongs to the main frame.</summary>
    public bool IsMainFrame { get; }
}

/// <summary>Provides append-only application context-menu items for one native menu request.</summary>
public sealed class NativeWebViewContextMenuItemCollection : IReadOnlyList<NativeWebViewContextMenuItem>
{
    private readonly List<NativeWebViewContextMenuItem> _items = [];

    /// <summary>Gets the number of appended items.</summary>
    public int Count => _items.Count;

    /// <summary>Gets an appended item by index.</summary>
    public NativeWebViewContextMenuItem this[int index] => _items[index];

    /// <summary>Appends an item to the native menu.</summary>
    public void Add(NativeWebViewContextMenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _items.Add(item);
    }

    /// <inheritdoc />
    public IEnumerator<NativeWebViewContextMenuItem> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Provides information about a native web view context-menu request.</summary>
public sealed class NativeWebViewContextMenuRequestedEventArgs : EventArgs
{
    /// <summary>Initializes a context-menu request.</summary>
    public NativeWebViewContextMenuRequestedEventArgs(double x, double y, NativeWebViewContextMenuTarget? target = null)
    {
        X = x;
        Y = y;
        Target = target;
    }

    /// <summary>Gets the horizontal native-menu location.</summary>
    public double X { get; }
    /// <summary>Gets the vertical native-menu location.</summary>
    public double Y { get; }
    /// <summary>Gets the page target that opened the menu, when available.</summary>
    public NativeWebViewContextMenuTarget? Target { get; }
    /// <summary>Gets the append-only list of application-provided menu items.</summary>
    public NativeWebViewContextMenuItemCollection AdditionalItems { get; } = new();
    /// <summary>Gets or sets whether the complete native menu is suppressed.</summary>
    public bool Handled { get; set; }
}

/// <summary>Provides an application context-menu command and its originating target.</summary>
public sealed class NativeWebViewContextMenuCommandInvokedEventArgs : EventArgs
{
    /// <summary>Initializes a context-menu command invocation.</summary>
    public NativeWebViewContextMenuCommandInvokedEventArgs(string commandId, NativeWebViewContextMenuTarget target)
    {
        CommandId = string.IsNullOrWhiteSpace(commandId)
            ? throw new ArgumentException("Context-menu command identifiers must not be empty.", nameof(commandId))
            : commandId;
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>Gets the application-defined command identifier.</summary>
    public string CommandId { get; }
    /// <summary>Gets the target captured when the native menu opened.</summary>
    public NativeWebViewContextMenuTarget Target { get; }
}

public sealed class NativeWebViewNavigationHistoryChangedEventArgs : EventArgs
{
    public NativeWebViewNavigationHistoryChangedEventArgs(bool canGoBack, bool canGoForward)
    {
        CanGoBack = canGoBack;
        CanGoForward = canGoForward;
    }

    public bool CanGoBack { get; }

    public bool CanGoForward { get; }
}

public sealed class NativeWebViewFaviconChangedEventArgs : EventArgs
{
    public NativeWebViewFaviconChangedEventArgs(Uri? uri)
    {
        Uri = uri;
    }

    public Uri? Uri { get; }
}

public sealed class NativeWebViewRenderFrameCapturedEventArgs : EventArgs
{
    public NativeWebViewRenderFrameCapturedEventArgs(NativeWebViewRenderFrame frame)
    {
        Frame = frame ?? throw new ArgumentNullException(nameof(frame));
    }

    public NativeWebViewRenderFrame Frame { get; }
}

public sealed class CoreWebViewEnvironmentRequestedEventArgs : EventArgs
{
    public CoreWebViewEnvironmentRequestedEventArgs(NativeWebViewEnvironmentOptions options)
    {
        Options = options;
    }

    public NativeWebViewEnvironmentOptions Options { get; }
}

public sealed class CoreWebViewControllerOptionsRequestedEventArgs : EventArgs
{
    public CoreWebViewControllerOptionsRequestedEventArgs(NativeWebViewControllerOptions options)
    {
        Options = options;
    }

    public NativeWebViewControllerOptions Options { get; }
}
