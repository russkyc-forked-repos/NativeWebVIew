namespace NativeWebView.Core;

internal sealed class RuntimeNavigationReplayState
{
    private readonly Lock _gate = new();
    private readonly Dictionary<ulong, TrackedRuntimeNavigation> _runtimeNavigations = [];
    private Uri? _replayUri;
    private int _requestVersion;
    private bool _hasOutstandingRequest;
    private long _nextNavigationSequence;
    private long _latestPromotedNavigationSequence;

    public Uri? ReplayUri
    {
        get
        {
            lock (_gate)
                return _replayUri;
        }
    }

    public RuntimeNavigationRequest SetRequested(Uri uri, bool isRuntimeReady)
    {
        ArgumentNullException.ThrowIfNull(uri);

        lock (_gate)
        {
            _replayUri = uri;
            _requestVersion = unchecked(_requestVersion + 1);
            _hasOutstandingRequest = true;
            return new RuntimeNavigationRequest(uri, _requestVersion, isRuntimeReady);
        }
    }

    public RuntimeNavigationRequest PublishRuntimeReady()
    {
        lock (_gate)
        {
            if (_replayUri is not null && !_hasOutstandingRequest)
            {
                _requestVersion = unchecked(_requestVersion + 1);
                _hasOutstandingRequest = true;
            }

            return new RuntimeNavigationRequest(_replayUri, _requestVersion, IsRuntimeReady: true);
        }
    }

    public bool IsCurrent(RuntimeNavigationRequest request, bool isDisposed, bool isRuntimeReady)
    {
        lock (_gate)
        {
            return !isDisposed &&
                   isRuntimeReady &&
                   request.Uri is not null &&
                   _hasOutstandingRequest &&
                   request.Version == _requestVersion;
        }
    }

    public void TrackNavigationStarted(ulong navigationId, Uri? uri, bool isRedirected)
    {
        lock (_gate)
        {
            if (isRedirected &&
                _runtimeNavigations.TryGetValue(navigationId, out var redirectedNavigation))
            {
                if (uri is not null)
                {
                    _runtimeNavigations[navigationId] = redirectedNavigation with
                    {
                        CurrentUri = uri,
                    };
                }

                return;
            }

            _runtimeNavigations[navigationId] = new TrackedRuntimeNavigation(
                _hasOutstandingRequest && AreSameUri(uri, _replayUri)
                    ? _requestVersion
                    : 0,
                uri,
                uri,
                ++_nextNavigationSequence);
        }
    }

    public bool CompleteNavigation(ulong navigationId, Uri? reachedUri)
    {
        lock (_gate)
            return CompleteNavigationCore(navigationId, reachedUri);
    }

    public bool CompleteNavigation(Uri? startedUri, ulong fallbackNavigationId, Uri? reachedUri)
    {
        lock (_gate)
        {
            if (_runtimeNavigations.TryGetValue(fallbackNavigationId, out var fallbackNavigation) &&
                MatchesNavigationUri(fallbackNavigation, startedUri))
            {
                return CompleteNavigationCore(fallbackNavigationId, reachedUri);
            }

            ulong? soleNavigationId = null;
            var hasMultipleNavigations = false;
            foreach (var candidate in _runtimeNavigations)
            {
                if (MatchesNavigationUri(candidate.Value, startedUri))
                    return CompleteNavigationCore(candidate.Key, reachedUri);

                if (soleNavigationId is null)
                    soleNavigationId = candidate.Key;
                else
                    hasMultipleNavigations = true;
            }

            return !hasMultipleNavigations && soleNavigationId is { } navigationId
                ? CompleteNavigationCore(navigationId, reachedUri)
                : false;
        }
    }

    public bool TryUpdateReached(Uri? reachedUri)
    {
        lock (_gate)
        {
            if (_hasOutstandingRequest)
                return false;

            if (reachedUri is not null)
                _replayUri = reachedUri;
            return true;
        }
    }

    public void RuntimeDestroyed()
    {
        lock (_gate)
            _runtimeNavigations.Clear();
    }

    private bool CompleteNavigationCore(ulong navigationId, Uri? reachedUri)
    {
        var isTracked = _runtimeNavigations.Remove(navigationId, out var trackedNavigation);
        var navigationVersion = isTracked ? trackedNavigation.RequestVersion : 0;

        if (_hasOutstandingRequest && navigationVersion == _requestVersion)
            _hasOutstandingRequest = false;

        if (_hasOutstandingRequest)
            return false;

        if (isTracked)
        {
            if (trackedNavigation.Sequence <= _latestPromotedNavigationSequence)
                return false;

            _latestPromotedNavigationSequence = trackedNavigation.Sequence;
        }
        else if (_latestPromotedNavigationSequence != 0)
        {
            return false;
        }

        if (reachedUri is not null)
            _replayUri = reachedUri;
        return true;
    }

    private static bool AreSameUri(Uri? left, Uri? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return Uri.Compare(
                   left,
                   right,
                   UriComponents.AbsoluteUri,
                   UriFormat.SafeUnescaped,
                   StringComparison.Ordinal) == 0;
    }

    private static bool MatchesNavigationUri(TrackedRuntimeNavigation navigation, Uri? uri) =>
        AreSameUri(navigation.StartedUri, uri) ||
        AreSameUri(navigation.CurrentUri, uri);

    private readonly record struct TrackedRuntimeNavigation(
        int RequestVersion,
        Uri? StartedUri,
        Uri? CurrentUri,
        long Sequence);
}

internal readonly record struct RuntimeNavigationRequest(
    Uri? Uri,
    int Version,
    bool IsRuntimeReady);
