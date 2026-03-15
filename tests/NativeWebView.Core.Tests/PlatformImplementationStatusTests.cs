using NativeWebView.Core;

namespace NativeWebView.Core.Tests;

public sealed class PlatformImplementationStatusTests
{
    public static TheoryData<
        NativeWebViewPlatform,
        NativeWebViewRepositoryImplementationStatus,
        NativeWebViewRepositoryImplementationStatus,
        NativeWebViewRepositoryImplementationStatus,
        int?> MatrixCases =>
        new()
        {
            {
                NativeWebViewPlatform.Windows,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                null
            },
            {
                NativeWebViewPlatform.MacOS,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                null
            },
            {
                NativeWebViewPlatform.Linux,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                null
            },
            {
                NativeWebViewPlatform.IOS,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.Unsupported,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                null
            },
            {
                NativeWebViewPlatform.Android,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.Unsupported,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                null
            },
            {
                NativeWebViewPlatform.Browser,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                NativeWebViewRepositoryImplementationStatus.Unsupported,
                NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                null
            },
            {
                NativeWebViewPlatform.Unknown,
                NativeWebViewRepositoryImplementationStatus.Unsupported,
                NativeWebViewRepositoryImplementationStatus.Unsupported,
                NativeWebViewRepositoryImplementationStatus.Unsupported,
                null
            },
        };

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void Matrix_ReportsExpectedRepositoryImplementationStatus(
        NativeWebViewPlatform platform,
        NativeWebViewRepositoryImplementationStatus embeddedControl,
        NativeWebViewRepositoryImplementationStatus dialog,
        NativeWebViewRepositoryImplementationStatus authenticationBroker,
        int? recommendedBringUpOrder)
    {
        var status = NativeWebViewPlatformImplementationStatusMatrix.Get(platform);

        Assert.Equal(platform, status.Platform);
        Assert.Equal(embeddedControl, status.EmbeddedControl);
        Assert.Equal(dialog, status.Dialog);
        Assert.Equal(authenticationBroker, status.AuthenticationBroker);
        Assert.Equal(recommendedBringUpOrder, status.RecommendedBringUpOrder);
        Assert.False(string.IsNullOrWhiteSpace(status.Summary));
    }

    [Fact]
    public void RemainingBringUpOrder_IsStable()
    {
        var order = NativeWebViewPlatformImplementationStatusMatrix.GetRemainingPlatformBringUpOrder();

        Assert.Empty(order);
    }

    [Fact]
    public void RemainingBringUpOrder_IsExposedAsReadOnlySequence()
    {
        var order = NativeWebViewPlatformImplementationStatusMatrix.GetRemainingPlatformBringUpOrder();

        Assert.False(order is NativeWebViewPlatform[]);

        var list = Assert.IsAssignableFrom<IList<NativeWebViewPlatform>>(order);
        Assert.True(list.IsReadOnly);
    }

    [Fact]
    public void AllSupportedPlatformsExceptUnknown_HaveEmbeddedControlRuntimeToday()
    {
        foreach (var platform in Enum.GetValues<NativeWebViewPlatform>())
        {
            var status = NativeWebViewPlatformImplementationStatusMatrix.Get(platform);
            var shouldHaveRuntime =
                platform == NativeWebViewPlatform.Android ||
                platform == NativeWebViewPlatform.Browser ||
                platform == NativeWebViewPlatform.IOS ||
                platform == NativeWebViewPlatform.MacOS ||
                platform == NativeWebViewPlatform.Linux ||
                platform == NativeWebViewPlatform.Windows;

            Assert.Equal(shouldHaveRuntime, status.HasEmbeddedControlRuntime);
        }
    }
}
