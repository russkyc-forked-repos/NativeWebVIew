using System;
using System.Collections.Generic;
using System.Threading;

namespace NativeWebView.Controls;

internal sealed class PendingNativeOperationRegistry<T>
    where T : class
{
    private readonly Lock _gate = new();
    private readonly HashSet<T> _operations = [];
    private bool _isClosed;

    public int Count
    {
        get
        {
            lock (_gate)
                return _operations.Count;
        }
    }

    public void Add(T operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!TryAdd(operation))
            throw new InvalidOperationException("The pending-operation registry is closed.");
    }

    public bool TryAdd(T operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
        {
            if (_isClosed)
                return false;

            _operations.Add(operation);
            return true;
        }
    }

    public void Remove(T operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_gate)
            _operations.Remove(operation);
    }

    public T[] Snapshot()
    {
        lock (_gate)
            return [.. _operations];
    }

    public T[] CloseAndSnapshot()
    {
        lock (_gate)
        {
            _isClosed = true;
            return [.. _operations];
        }
    }
}
