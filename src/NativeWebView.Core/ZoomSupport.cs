namespace NativeWebView.Core;

/// <summary>Provides data for a native WebView zoom-factor change.</summary>
public sealed class NativeWebViewZoomFactorChangedEventArgs : EventArgs
{
    /// <summary>Initializes the event data.</summary>
    /// <param name="zoomFactor">The effective positive zoom factor.</param>
    public NativeWebViewZoomFactorChangedEventArgs(double zoomFactor)
    {
        if (!double.IsFinite(zoomFactor) || zoomFactor <= 0d)
            throw new ArgumentOutOfRangeException(nameof(zoomFactor), zoomFactor, "Zoom factor must be finite and greater than zero.");

        ZoomFactor = zoomFactor;
    }

    /// <summary>Gets the effective zoom factor.</summary>
    public double ZoomFactor { get; }
}

/// <summary>Reports effective zoom-factor changes from a native WebView backend.</summary>
public interface INativeWebViewZoomFactorProvider
{
    /// <summary>Occurs when the effective zoom factor changes.</summary>
    event EventHandler<NativeWebViewZoomFactorChangedEventArgs>? ZoomFactorChanged;
}

internal static class NativeWebViewZoomFactor
{
    public const double ChangeTolerance = 0.001d;

    public static bool IsValid(double value) => double.IsFinite(value) && value > 0d;

    public static bool HasChanged(double previous, double current) =>
        IsValid(current) && (!IsValid(previous) || Math.Abs(previous - current) >= ChangeTolerance);
}
