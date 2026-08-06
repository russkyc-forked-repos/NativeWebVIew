using NativeWebView.Core;
using NativeWebView.Platform.Windows;

namespace NativeWebView.Core.Tests;

public sealed class DocumentStartScriptSourceBuilderTests
{
    [Fact]
    public void SourceBuilder_AllFrames_ReturnsSourceUnchanged()
    {
        const string source = "\uFEFF/* leading */\n\"use strict\";\nlet marker = 1;";

        var result = WindowsDocumentStartScriptSourceBuilder.Build(
            new NativeWebViewDocumentStartScript(source, NativeWebViewScriptFrameScope.AllFrames));

        Assert.Same(source, result);
    }

    [Fact]
    public void SourceBuilder_MainFrame_InsertsGuardAfterDirectivePrologue()
    {
        const string source = "/* leading */\n\"use strict\";\n'use client';\nlet lexical = 1;\nconst value = 2;\nclass Marker {}\nfunction read() { return lexical; }";

        var result = WindowsDocumentStartScriptSourceBuilder.Build(
            new NativeWebViewDocumentStartScript(source, NativeWebViewScriptFrameScope.MainFrame));

        var strictIndex = result.IndexOf("'use client';", StringComparison.Ordinal);
        var guardIndex = result.IndexOf("if (globalThis.top !== globalThis)", StringComparison.Ordinal);
        var declarationIndex = result.IndexOf("let lexical = 1;", StringComparison.Ordinal);
        Assert.True(strictIndex >= 0 && strictIndex < guardIndex);
        Assert.True(guardIndex < declarationIndex);
        Assert.Contains("class Marker {}", result, StringComparison.Ordinal);
        Assert.Contains("function read()", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceBuilder_MainFrame_RecognizesEscapedDirectiveAndAsiBoundary()
    {
        const string source = "// heading\n\"use\\x20strict\" /* directive comment */\nlet marker = \"escaped\\nvalue\";";

        var result = WindowsDocumentStartScriptSourceBuilder.Build(
            new NativeWebViewDocumentStartScript(source, NativeWebViewScriptFrameScope.MainFrame));

        Assert.True(
            result.IndexOf("\"use\\x20strict\"", StringComparison.Ordinal) <
            result.IndexOf("if (globalThis.top !== globalThis)", StringComparison.Ordinal));
        Assert.True(
            result.IndexOf("if (globalThis.top !== globalThis)", StringComparison.Ordinal) <
            result.IndexOf("let marker", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceBuilder_MainFrame_PreservesLeadingHashbangBeforeGuard()
    {
        const string source = "#!/usr/bin/env node\n\"use strict\";\nglobalThis.marker = true;";

        var result = WindowsDocumentStartScriptSourceBuilder.Build(
            new NativeWebViewDocumentStartScript(source, NativeWebViewScriptFrameScope.MainFrame));

        Assert.StartsWith("#!/usr/bin/env node", result, StringComparison.Ordinal);
        Assert.True(
            result.IndexOf("\"use strict\";", StringComparison.Ordinal) <
            result.IndexOf("if (globalThis.top !== globalThis)", StringComparison.Ordinal));
    }

    [Fact]
    public void SourceBuilder_MainFrame_PreservesBomPrefixedDirectivePrologue()
    {
        const string source = "\uFEFF\"use strict\";\nlet marker = 1;";

        var result = WindowsDocumentStartScriptSourceBuilder.Build(
            new NativeWebViewDocumentStartScript(source, NativeWebViewScriptFrameScope.MainFrame));

        Assert.StartsWith("\uFEFF\"use strict\";", result, StringComparison.Ordinal);
        Assert.True(
            result.IndexOf("\"use strict\";", StringComparison.Ordinal) <
            result.IndexOf("if (globalThis.top !== globalThis)", StringComparison.Ordinal));
    }

    [Fact]
    public void DirectiveScanner_DoesNotSplitContinuedStringExpression()
    {
        const string source = "\"not a directive\"\n(function () {})();";

        var insertionIndex = WindowsDocumentStartScriptSourceBuilder.FindDirectivePrologueEnd(source);

        Assert.Equal(0, insertionIndex);
    }

    [Theory]
    [InlineData("++counter;")]
    [InlineData("--counter;")]
    [InlineData("!flag;")]
    public void DirectiveScanner_PreservesDirectiveBeforePrefixOrUnaryStatement(string statement)
    {
        var source = "\"use strict\"\n" + statement;

        var insertionIndex = WindowsDocumentStartScriptSourceBuilder.FindDirectivePrologueEnd(source);

        Assert.Equal(source.IndexOf(statement, StringComparison.Ordinal), insertionIndex);
    }

    [Theory]
    [InlineData("+value;")]
    [InlineData("-value;")]
    [InlineData("!= value;")]
    [InlineData("!== value;")]
    public void DirectiveScanner_DoesNotSplitContinuedBinaryExpression(string continuation)
    {
        var source = "\"not a directive\"\n" + continuation;

        var insertionIndex = WindowsDocumentStartScriptSourceBuilder.FindDirectivePrologueEnd(source);

        Assert.Equal(0, insertionIndex);
    }

    [Fact]
    public void SourceBuilder_MainFrame_UsesBootstrapAbortWithoutDomInjection()
    {
        const string source = "globalThis.marker = true;";

        var result = WindowsDocumentStartScriptSourceBuilder.Build(
            new NativeWebViewDocumentStartScript(source, NativeWebViewScriptFrameScope.MainFrame));

        Assert.Contains("globalThis.__nativeWebViewMainFrameAbort_7D1C94B1();", result, StringComparison.Ordinal);
        Assert.DoesNotContain("addEventListener(\"error\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("createElement", result, StringComparison.Ordinal);
        Assert.DoesNotContain("MutationObserver", result, StringComparison.Ordinal);
        Assert.EndsWith(source, result, StringComparison.Ordinal);
    }

    [Fact]
    public void MainFrameBootstrap_InstallsEarlyPrivateSentinelSuppressor()
    {
        var source = WindowsDocumentStartScriptSourceBuilder.MainFrameBootstrapSource;

        Assert.Contains("new WeakSet()", source, StringComparison.Ordinal);
        Assert.Contains("Object.defineProperty(globalThis, propertyName", source, StringComparison.Ordinal);
        Assert.Contains("configurable: false", source, StringComparison.Ordinal);
        Assert.Contains("enumerable: false", source, StringComparison.Ordinal);
        Assert.Contains("writable: false", source, StringComparison.Ordinal);
        Assert.Contains("sentinels.delete(event.error)", source, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault();", source, StringComparison.Ordinal);
        Assert.Contains("event.stopImmediatePropagation();", source, StringComparison.Ordinal);
        Assert.Contains("{ capture: true }", source, StringComparison.Ordinal);
    }
}
