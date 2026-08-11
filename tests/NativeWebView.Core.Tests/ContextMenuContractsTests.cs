using System.Text;
using NativeWebView.Controls;
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
    public void NativeWebView_ForwardsRequestWithDerivedControlAsSenderAndPreservesMutableArgs()
    {
        using var backend = new ContextMenuBackend();
        using var instance = new NativeWebViewInstance(backend);
        var target = new NativeWebViewContextMenuTarget("target", true, new Uri("https://example.com/login"));
        object? controllerSender = null;
        NativeWebViewContextMenuRequestedEventArgs? controllerArgs = null;
        instance.Controller.ContextMenuRequested += (sender, args) =>
        {
            controllerSender = sender;
            controllerArgs = args;
        };
        using var webView = new DerivedNativeWebView(instance);
        object? controlSender = null;
        NativeWebViewContextMenuRequestedEventArgs? controlArgs = null;
        webView.ContextMenuRequested += (sender, args) =>
        {
            controlSender = sender;
            controlArgs = args;
            args.AdditionalItems.Add(new NativeWebViewContextMenuItem("username", "Username"));
            args.Handled = true;
        };

        var backendArgs = backend.Request(12, 34, target);

        Assert.Same(instance.Controller, controllerSender);
        Assert.Same(webView, controlSender);
        Assert.Same(backendArgs, controllerArgs);
        Assert.Same(controllerArgs, controlArgs);
        Assert.NotNull(controlArgs);
        Assert.Equal(12, controlArgs.X);
        Assert.Equal(34, controlArgs.Y);
        Assert.Same(target, controlArgs.Target);
        Assert.True(controlArgs.Handled);
        Assert.Single(controlArgs.AdditionalItems);
    }

    [Fact]
    public void NativeWebView_ForwardsCommandWithDerivedControlAsSender()
    {
        using var backend = new ContextMenuBackend();
        using var instance = new NativeWebViewInstance(backend);
        using var webView = new DerivedNativeWebView(instance);
        object? sender = null;
        var target = new NativeWebViewContextMenuTarget("target", true);
        webView.ContextMenuCommandInvoked += (source, _) => sender = source;

        backend.Invoke("username", target);

        Assert.Same(webView, sender);
    }

    [Fact]
    public void NativeWebView_ContextMenuRequestsDoNotCrossControlInstances()
    {
        using var firstBackend = new ContextMenuBackend();
        using var firstWebView = new Controls.NativeWebView(firstBackend);
        using var secondBackend = new ContextMenuBackend();
        using var secondWebView = new Controls.NativeWebView(secondBackend);
        var firstCount = 0;
        var secondCount = 0;
        firstWebView.ContextMenuRequested += (_, _) => firstCount++;
        secondWebView.ContextMenuRequested += (_, _) => secondCount++;

        firstBackend.Request(1, 2);

        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public void NativeWebView_DetachesContextMenuForwardersWhenDisposed()
    {
        using var backend = new ContextMenuBackend();
        using var instance = new NativeWebViewInstance(backend);
        var webView = new Controls.NativeWebView(instance);
        var requestCount = 0;
        var commandCount = 0;
        webView.ContextMenuRequested += (_, _) => requestCount++;
        webView.ContextMenuCommandInvoked += (_, _) => commandCount++;

        webView.Dispose();
        backend.Request(1, 2);
        backend.Invoke("username", new NativeWebViewContextMenuTarget("target", true));

        Assert.Equal(0, requestCount);
        Assert.Equal(0, commandCount);
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
          INativeWebViewBackend,
          INativeWebViewContextMenuBackend
    {
        private event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? Requested;

        event EventHandler<NativeWebViewContextMenuRequestedEventArgs>? INativeWebViewBackend.ContextMenuRequested
        {
            add => Requested += value;
            remove => Requested -= value;
        }

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

        public NativeWebViewContextMenuRequestedEventArgs Request(
            double x,
            double y,
            NativeWebViewContextMenuTarget? target = null)
        {
            var args = new NativeWebViewContextMenuRequestedEventArgs(x, y, target);
            Requested?.Invoke(this, args);
            return args;
        }
    }

    private sealed class DerivedNativeWebView(NativeWebViewInstance instance) : Controls.NativeWebView(instance);
}
