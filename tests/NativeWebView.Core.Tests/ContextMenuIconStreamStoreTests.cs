using System;
using System.Text;
using NativeWebView.Core;
using NativeWebView.Platform.Windows;

namespace NativeWebView.Core.Tests;

public sealed class ContextMenuIconStreamStoreTests
{
    [Fact]
    public void Create_KeepsIconStreamReadableUntilReset()
    {
        using var store = new ContextMenuIconStreamStore();
        var icon = new NativeWebViewContextMenuIcon([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var stream = store.Create(icon);

        Assert.NotNull(stream);
        Assert.Equal(0x89, stream.ReadByte());

        store.Reset();

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    public void Create_WithNoIcon_DoesNotCreateStream()
    {
        using var store = new ContextMenuIconStreamStore();

        Assert.Null(store.Create(icon: null));
    }

    [Fact]
    public void Create_PrefersScalableIconData()
    {
        using var store = new ContextMenuIconStreamStore();
        var icon = new NativeWebViewContextMenuIcon(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8);

        using var reader = new StreamReader(store.Create(icon)!, Encoding.UTF8, leaveOpen: true);

        Assert.StartsWith("<svg", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PrefersHighDensityPngOverScalableData()
    {
        using var store = new ContextMenuIconStreamStore();
        var icon = new NativeWebViewContextMenuIcon(
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x2A]);

        var stream = store.Create(icon);

        Assert.NotNull(stream);
        stream.Position = 8;
        Assert.Equal(0x2A, stream.ReadByte());
    }
}
