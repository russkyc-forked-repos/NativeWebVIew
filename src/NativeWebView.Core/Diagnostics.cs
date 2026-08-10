using System.Collections.ObjectModel;

namespace NativeWebView.Core;

public enum NativeWebViewDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

/// <summary>Identifies an action that can remediate a platform diagnostic issue.</summary>
public enum NativeWebViewDiagnosticRemediationKind
{
    /// <summary>Installs the native web runtime required by the platform backend.</summary>
    InstallRuntime = 0
}

/// <summary>Describes a platform-neutral action that can remediate a diagnostic issue.</summary>
public sealed class NativeWebViewDiagnosticRemediation
{
    /// <summary>Creates a remediation action.</summary>
    /// <param name="kind">The kind of remediation.</param>
    /// <param name="uri">The absolute URI used to perform the remediation.</param>
    public NativeWebViewDiagnosticRemediation(NativeWebViewDiagnosticRemediationKind kind, Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("The remediation URI must be absolute.", nameof(uri));

        Kind = kind;
        Uri = uri;
    }

    /// <summary>Gets the remediation kind.</summary>
    public NativeWebViewDiagnosticRemediationKind Kind { get; }

    /// <summary>Gets the absolute remediation URI.</summary>
    public Uri Uri { get; }
}

public sealed class NativeWebViewDiagnosticIssue
{
    public NativeWebViewDiagnosticIssue(
        string code,
        NativeWebViewDiagnosticSeverity severity,
        string message,
        string? recommendation = null)
        : this(code, severity, message, recommendation, remediation: null)
    {
    }

    /// <summary>Creates a diagnostic issue with an optional machine-actionable remediation.</summary>
    public NativeWebViewDiagnosticIssue(
        string code,
        NativeWebViewDiagnosticSeverity severity,
        string message,
        string? recommendation,
        NativeWebViewDiagnosticRemediation? remediation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Severity = severity;
        Message = message;
        Recommendation = recommendation;
        Remediation = remediation;
    }

    public string Code { get; }

    public NativeWebViewDiagnosticSeverity Severity { get; }

    public string Message { get; }

    public string? Recommendation { get; }

    /// <summary>Gets an optional platform-neutral remediation action.</summary>
    public NativeWebViewDiagnosticRemediation? Remediation { get; }
}

public sealed class NativeWebViewPlatformDiagnostics
{
    public static readonly NativeWebViewPlatformDiagnostics Unknown = new(
        NativeWebViewPlatform.Unknown,
        "unregistered",
        [
            new NativeWebViewDiagnosticIssue(
                code: "platform.unregistered",
                severity: NativeWebViewDiagnosticSeverity.Error,
                message: "No diagnostics provider is registered for this platform.",
                recommendation: "Register a platform package module before requesting diagnostics.")
        ]);

    public NativeWebViewPlatformDiagnostics(
        NativeWebViewPlatform platform,
        string providerName,
        IReadOnlyList<NativeWebViewDiagnosticIssue> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(issues);

        Platform = platform;
        ProviderName = providerName;
        Issues = new ReadOnlyCollection<NativeWebViewDiagnosticIssue>([.. issues]);
    }

    public NativeWebViewPlatform Platform { get; }

    public string ProviderName { get; }

    public IReadOnlyList<NativeWebViewDiagnosticIssue> Issues { get; }

    public bool IsReady => Issues.All(static issue => issue.Severity != NativeWebViewDiagnosticSeverity.Error);
}
