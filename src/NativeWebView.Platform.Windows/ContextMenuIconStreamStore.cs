using System;
using System.Collections.Generic;
using System.IO;
using NativeWebView.Core;

namespace NativeWebView.Platform.Windows;

internal sealed class ContextMenuIconStreamStore : IDisposable
{
    private readonly List<MemoryStream> _streams = [];

    public Stream? Create(NativeWebViewContextMenuIcon? icon)
    {
        if (icon is null)
            return null;

        var imageData = !icon.HighDensityPngData.IsEmpty
            ? icon.HighDensityPngData
            : !icon.SvgData.IsEmpty
                ? icon.SvgData
                : icon.PngData;
        var stream = new MemoryStream(imageData.ToArray(), writable: false);
        _streams.Add(stream);
        return stream;
    }

    public void Reset()
    {
        foreach (var stream in _streams)
            stream.Dispose();

        _streams.Clear();
    }

    public void Dispose() => Reset();
}
