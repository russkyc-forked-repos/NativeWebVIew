using NativeWebView.Core;
using NativeWebView.Platform.Windows;

namespace NativeWebView.Core.Tests;

public sealed class ContextMenuDescriptorComparerTests
{
    [Fact]
    public void AreEquivalent_WithMatchingTrees_ReturnsTrue()
    {
        var iconData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var first = CreateMenu(new NativeWebViewContextMenuIcon(iconData));
        var second = CreateMenu(new NativeWebViewContextMenuIcon(iconData));

        Assert.True(ContextMenuDescriptorComparer.AreEquivalent(first, second));
    }

    [Fact]
    public void AreEquivalent_WithChangedChild_ReturnsFalse()
    {
        var first = CreateMenu();
        var second = new NativeWebViewContextMenuItem[]
        {
            new(
                "root",
                "Royal Connect Passwords",
                NativeWebViewContextMenuItemKind.Submenu,
                children: [new NativeWebViewContextMenuItem("password", "Password")]),
        };

        Assert.False(ContextMenuDescriptorComparer.AreEquivalent(first, second));
    }

    private static NativeWebViewContextMenuItem[] CreateMenu(NativeWebViewContextMenuIcon? icon = null) =>
    [
        new NativeWebViewContextMenuItem(
            "root",
            "Royal Connect Passwords",
            NativeWebViewContextMenuItemKind.Submenu,
            children: [new NativeWebViewContextMenuItem("username", "Username")],
            icon: icon),
    ];
}
