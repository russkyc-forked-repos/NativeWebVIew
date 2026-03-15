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
                dialog: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                summary: "Windows now ships a real embedded NativeWebView control runtime backed by WebView2 in this repo; NativeWebDialog and WebAuthenticationBroker remain contract-only."),
            NativeWebViewPlatform.MacOS => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                summary: "macOS currently has the only real embedded NativeWebView control host and NativeWebDialog runtime path in this repo; WebAuthenticationBroker remains stubbed."),
            NativeWebViewPlatform.Linux => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                summary: "Linux now ships a real embedded NativeWebView control runtime backed by GTK3/WebKitGTK on X11; NativeWebDialog and WebAuthenticationBroker remain contract-only."),
            NativeWebViewPlatform.IOS => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.Unsupported,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                summary: "iOS now ships a real embedded NativeWebView control runtime backed by UIView/WKWebView when the iOS backend is built with the .NET 8 Apple workload; NativeWebDialog remains unsupported and WebAuthenticationBroker remains contract-only."),
            NativeWebViewPlatform.Android => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.Unsupported,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                summary: "Android now ships a real embedded NativeWebView control runtime backed by android.webkit.WebView when the Android backend is built with the .NET 8 Android workload; NativeWebDialog remains unsupported and WebAuthenticationBroker remains contract-only."),
            NativeWebViewPlatform.Browser => new NativeWebViewPlatformImplementationStatus(
                platform,
                embeddedControl: NativeWebViewRepositoryImplementationStatus.RuntimeImplemented,
                dialog: NativeWebViewRepositoryImplementationStatus.Unsupported,
                authenticationBroker: NativeWebViewRepositoryImplementationStatus.ContractOnly,
                summary: "Browser now ships a real embedded NativeWebView control runtime backed by Avalonia Browser native hosting plus an iframe/DOM bridge; NativeWebDialog remains unsupported and WebAuthenticationBroker remains contract-only."),
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
