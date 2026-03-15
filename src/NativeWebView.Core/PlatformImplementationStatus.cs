namespace NativeWebView.Core;

public enum NativeWebViewRepositoryImplementationStatus
{
    Unsupported = 0,
    ContractOnly,
    RuntimeImplemented,
}

public sealed class NativeWebViewPlatformImplementationStatus
{
    internal NativeWebViewPlatformImplementationStatus(
        NativeWebViewPlatform platform,
        NativeWebViewRepositoryImplementationStatus embeddedControl,
        NativeWebViewRepositoryImplementationStatus dialog,
        NativeWebViewRepositoryImplementationStatus authenticationBroker,
        string summary,
        int? recommendedBringUpOrder = null)
    {
        Platform = platform;
        EmbeddedControl = embeddedControl;
        Dialog = dialog;
        AuthenticationBroker = authenticationBroker;
        Summary = summary;
        RecommendedBringUpOrder = recommendedBringUpOrder;
    }

    public NativeWebViewPlatform Platform { get; }

    public NativeWebViewRepositoryImplementationStatus EmbeddedControl { get; }

    public NativeWebViewRepositoryImplementationStatus Dialog { get; }

    public NativeWebViewRepositoryImplementationStatus AuthenticationBroker { get; }

    public string Summary { get; }

    public int? RecommendedBringUpOrder { get; }

    public bool HasEmbeddedControlRuntime =>
        EmbeddedControl == NativeWebViewRepositoryImplementationStatus.RuntimeImplemented;
}

public static class NativeWebViewPlatformImplementationStatusMatrix
{
    private static readonly IReadOnlyList<NativeWebViewPlatform> RemainingPlatformBringUpOrder =
        Array.AsReadOnly(Array.Empty<NativeWebViewPlatform>());

    public static NativeWebViewPlatformImplementationStatus Get(NativeWebViewPlatform platform)
    {
        return platform switch
        {
            NativeWebViewPlatform.Windows => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                summary: "Windows now ships real embedded NativeWebView, NativeWebDialog, and WebAuthenticationBroker runtime paths backed by WebView2 plus a native dialog host."),
            NativeWebViewPlatform.MacOS => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                summary: "macOS now ships real embedded NativeWebView, NativeWebDialog, and dialog-backed WebAuthenticationBroker runtime paths in this repo."),
            NativeWebViewPlatform.Linux => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                summary: "Linux now ships real embedded NativeWebView, NativeWebDialog, and WebAuthenticationBroker runtime paths backed by GTK3/WebKitGTK on X11."),
            NativeWebViewPlatform.IOS => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.Unsupported,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                summary: "iOS now ships a real embedded NativeWebView control runtime plus a modal WKWebView-based WebAuthenticationBroker when the iOS backend is built with the .NET 8 Apple workload; NativeWebDialog remains unsupported."),
            NativeWebViewPlatform.Android => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.Unsupported,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                summary: "Android now ships a real embedded NativeWebView control runtime plus a dedicated WebView-backed WebAuthenticationBroker activity when the Android backend is built with the .NET 8 Android workload; NativeWebDialog remains unsupported."),
            NativeWebViewPlatform.Browser => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.Unsupported,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                summary: "Browser now ships a real embedded NativeWebView control runtime plus a popup-driven WebAuthenticationBroker backed by Avalonia Browser hosting and DOM/browser APIs; NativeWebDialog remains unsupported."),
            _ => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.Unsupported,
                dialog: NativeWebViewRepositoryImplementationStatus.Unsupported,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.Unsupported,
                summary: "Runtime implementation status is unknown for this platform."),
        };
    }

    public static IReadOnlyList<NativeWebViewPlatform> GetRemainingPlatformBringUpOrder()
    {
        return RemainingPlatformBringUpOrder;
    }
}
