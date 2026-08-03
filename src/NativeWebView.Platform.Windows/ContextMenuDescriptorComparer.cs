using NativeWebView.Core;

namespace NativeWebView.Platform.Windows;

internal static class ContextMenuDescriptorComparer
{
    public static bool AreEquivalent(
        IReadOnlyList<NativeWebViewContextMenuItem> first,
        IReadOnlyList<NativeWebViewContextMenuItem> second)
    {
        if (first.Count != second.Count)
            return false;

        for (var index = 0; index < first.Count; index++)
        {
            if (!AreEquivalent(first[index], second[index]))
                return false;
        }

        return true;
    }

    private static bool AreEquivalent(
        NativeWebViewContextMenuItem first,
        NativeWebViewContextMenuItem second) =>
        first.Id == second.Id &&
        first.Label == second.Label &&
        first.Kind == second.Kind &&
        first.IsEnabled == second.IsEnabled &&
        AreEquivalent(first.Icon, second.Icon) &&
        AreEquivalent(first.Children, second.Children);

    private static bool AreEquivalent(
        NativeWebViewContextMenuIcon? first,
        NativeWebViewContextMenuIcon? second)
    {
        if (ReferenceEquals(first, second))
            return true;
        if (first is null || second is null)
            return false;

        return first.PngData.Span.SequenceEqual(second.PngData.Span) &&
               first.SvgData.Span.SequenceEqual(second.SvgData.Span) &&
               first.HighDensityPngData.Span.SequenceEqual(second.HighDensityPngData.Span);
    }
}
