using NativeWebView.Core;

namespace NativeWebView.Controls;

internal sealed class NativeWebViewStatusDispatchQueue
{
    private readonly Lock _gate = new();
    private NativeWebViewStatusTextChangedEventArgs? _latestNotification;
    private int _generation;
    private bool _isDispatchQueued;

    public bool TryQueue(
        NativeWebViewStatusTextChangedEventArgs notification,
        out int generation)
    {
        ArgumentNullException.ThrowIfNull(notification);

        lock (_gate)
        {
            _latestNotification = notification;
            generation = _generation;
            if (_isDispatchQueued)
                return false;

            _isDispatchQueued = true;
            return true;
        }
    }

    public NativeWebViewStatusTextChangedEventArgs? TakeLatest(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return null;

            var notification = _latestNotification;
            _latestNotification = null;
            _isDispatchQueued = false;
            return notification;
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _generation = unchecked(_generation + 1);
            _latestNotification = null;
            _isDispatchQueued = false;
        }
    }
}
