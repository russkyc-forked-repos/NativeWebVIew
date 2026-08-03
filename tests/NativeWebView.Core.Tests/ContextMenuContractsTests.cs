using System.Text;
using NativeWebView.Core;

namespace NativeWebView.Core.Tests;

public sealed class ContextMenuContractsTests
{
    [Fact]
    public void RequestedEventArgs_PreservesLegacyCoordinatesAndAcceptsAdditionalItems()
    {
        var target = new NativeWebViewContextMenuTarget("target", true, new Uri("https://example.com"));
        var args = new NativeWebViewContextMenuRequestedEventArgs(12, 34, target);
        args.AdditionalItems.Add(new NativeWebViewContextMenuItem(
            "root",
            "Royal Passwords",
            NativeWebViewContextMenuItemKind.Submenu,
            children: [new NativeWebViewContextMenuItem("username", "Username")]));

        Assert.Equal(12, args.X);
        Assert.Equal(34, args.Y);
        Assert.Same(target, args.Target);
        Assert.Single(args.AdditionalItems);
    }

    [Fact]
    public async Task Controller_ForwardsOptionalContextMenuCapability()
    {
        using var backend = new ContextMenuBackend();
        using var controller = new NativeWebViewController(backend);
        NativeWebViewContextMenuCommandInvokedEventArgs? invoked = null;
        controller.ContextMenuCommandInvoked += (_, args) => invoked = args;
        var target = new NativeWebViewContextMenuTarget("target", true);

        backend.Invoke("username", target);
        var inserted = await controller.InsertTextAtContextMenuTargetAsync(target, "alice");

        Assert.Equal("username", invoked?.CommandId);
        Assert.True(inserted);
        Assert.Equal("alice", backend.InsertedText);
    }

    [Fact]
    public void NativeWebView_ForwardsCommandWithControlAsSender()
    {
        using var backend = new ContextMenuBackend();
        using var webView = new Controls.NativeWebView(backend);
        object? sender = null;
        var target = new NativeWebViewContextMenuTarget("target", true);
        webView.ContextMenuCommandInvoked += (source, _) => sender = source;

        backend.Invoke("username", target);

        Assert.Same(webView, sender);
    }

    [Fact]
    public void ContextMenuIcon_CopiesPngDataAndCanBeAssignedToSubmenu()
    {
        var pngData = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01 };
        var icon = new NativeWebViewContextMenuIcon(pngData);
        var item = new NativeWebViewContextMenuItem(
            "root",
            "Royal Passwords",
            NativeWebViewContextMenuItemKind.Submenu,
            children: [],
            icon: icon);

        pngData[8] = 0x02;

        Assert.Same(icon, item.Icon);
        Assert.Equal(0x01, icon.PngData.Span[8]);
        Assert.True(icon.SvgData.IsEmpty);
    }

    [Fact]
    public void ContextMenuIcon_CopiesOptionalSvgData()
    {
        var pngData = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        var svgData = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        var icon = new NativeWebViewContextMenuIcon(pngData, svgData);

        svgData[0] = 0x00;

        Assert.Equal((byte)'<', icon.SvgData.Span[0]);
    }

    [Fact]
    public void ContextMenuIcon_CopiesOptionalHighDensityPngData()
    {
        var pngData = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        var highDensityPngData = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01 };
        var icon = new NativeWebViewContextMenuIcon(pngData, ReadOnlySpan<byte>.Empty, highDensityPngData);

        highDensityPngData[8] = 0x02;

        Assert.Equal(0x01, icon.HighDensityPngData.Span[8]);
    }

    [Fact]
    public void ContextMenuIcon_RejectsNonPngData()
    {
        var action = () => new NativeWebViewContextMenuIcon([0x01, 0x02, 0x03]);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void ContextMenuIcon_RejectsNonSvgScalableData()
    {
        var pngData = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        var action = () => new NativeWebViewContextMenuIcon(pngData, "not an image"u8);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void ContextMenuIcon_RejectsNonPngHighDensityData()
    {
        var pngData = new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
        var action = () => new NativeWebViewContextMenuIcon(pngData, ReadOnlySpan<byte>.Empty, [0x01, 0x02]);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void ContextMenuItem_RejectsIconOnSeparator()
    {
        var icon = new NativeWebViewContextMenuIcon(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

        var action = () => new NativeWebViewContextMenuItem(
            string.Empty,
            string.Empty,
            NativeWebViewContextMenuItemKind.Separator,
            icon: icon);

        Assert.Throws<ArgumentException>(action);
    }

    private sealed class ContextMenuBackend()
        : NativeWebViewBackendStubBase(
            NativeWebViewPlatform.Unknown,
            new WebViewPlatformFeatures(NativeWebViewPlatform.Unknown, NativeWebViewFeature.ContextMenu)),
          INativeWebViewContextMenuBackend
    {
        public event EventHandler<NativeWebViewContextMenuCommandInvokedEventArgs>? ContextMenuCommandInvoked;
        public string? InsertedText { get; private set; }

        public Task<bool> InsertTextAtContextMenuTargetAsync(
            NativeWebViewContextMenuTarget target,
            string text,
            CancellationToken cancellationToken = default)
        {
            InsertedText = text;
            return Task.FromResult(true);
        }

        public void Invoke(string commandId, NativeWebViewContextMenuTarget target) =>
            ContextMenuCommandInvoked?.Invoke(this, new NativeWebViewContextMenuCommandInvokedEventArgs(commandId, target));
    }
}
