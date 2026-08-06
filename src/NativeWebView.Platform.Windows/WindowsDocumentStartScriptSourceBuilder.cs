using NativeWebView.Core;

namespace NativeWebView.Platform.Windows;

internal static class WindowsDocumentStartScriptSourceBuilder
{
    internal const string MainFrameBootstrapSource = """
        (() => {
            const propertyName = "__nativeWebViewMainFrameAbort_7D1C94B1";
            if (Object.prototype.hasOwnProperty.call(globalThis, propertyName)) {
                return;
            }

            const sentinels = new WeakSet();
            Object.defineProperty(globalThis, propertyName, {
                configurable: false,
                enumerable: false,
                writable: false,
                value: () => {
                    const sentinel = {};
                    sentinels.add(sentinel);
                    throw sentinel;
                }
            });

            globalThis.addEventListener("error", event => {
                if (sentinels.delete(event.error)) {
                    event.preventDefault();
                    event.stopImmediatePropagation();
                }
            }, { capture: true });
        })();
        """;

    private const string MainFrameGuard = """
        if (globalThis.top !== globalThis) {
            globalThis.__nativeWebViewMainFrameAbort_7D1C94B1();
        }
        """;

    public static string Build(NativeWebViewDocumentStartScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (script.FrameScope == NativeWebViewScriptFrameScope.AllFrames)
            return script.Source;

        var insertionIndex = FindDirectivePrologueEnd(script.Source);
        return script.Source[..insertionIndex] +
               Environment.NewLine +
               MainFrameGuard +
               Environment.NewLine +
               script.Source[insertionIndex..];
    }

    internal static int FindDirectivePrologueEnd(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var position = 0;
        while (true)
        {
            var statementStart = SkipTrivia(source, position, out _);
            if (statementStart >= source.Length || source[statementStart] is not ('\'' or '"'))
                return statementStart;

            if (!TryScanStringLiteral(source, statementStart, out var literalEnd))
                return statementStart;

            var tokenStart = SkipTrivia(source, literalEnd, out var hadLineTerminator);
            if (tokenStart >= source.Length)
                return tokenStart;

            if (source[tokenStart] == ';')
            {
                position = tokenStart + 1;
                continue;
            }

            if (!hadLineTerminator || CanContinueExpression(source, tokenStart))
                return statementStart;

            position = tokenStart;
        }
    }

    private static int SkipTrivia(string source, int position, out bool hadLineTerminator)
    {
        hadLineTerminator = false;
        while (position < source.Length)
        {
            var current = source[position];
            if (current == '\uFEFF' || char.IsWhiteSpace(current))
            {
                hadLineTerminator |= IsLineTerminator(current);
                position++;
                continue;
            }

            if (current == '#' &&
                position + 1 < source.Length &&
                source[position + 1] == '!' &&
                (position == 0 || position == 1 && source[0] == '\uFEFF'))
            {
                position += 2;
                while (position < source.Length && !IsLineTerminator(source[position]))
                    position++;
                continue;
            }

            if (current != '/' || position + 1 >= source.Length)
                break;

            var next = source[position + 1];
            if (next == '/')
            {
                position += 2;
                while (position < source.Length && !IsLineTerminator(source[position]))
                    position++;
                continue;
            }

            if (next != '*')
                break;

            position += 2;
            while (position + 1 < source.Length &&
                   (source[position] != '*' || source[position + 1] != '/'))
            {
                hadLineTerminator |= IsLineTerminator(source[position]);
                position++;
            }

            if (position + 1 >= source.Length)
                return source.Length;

            position += 2;
        }

        return position;
    }

    private static bool TryScanStringLiteral(string source, int start, out int end)
    {
        var quote = source[start];
        for (var position = start + 1; position < source.Length; position++)
        {
            var current = source[position];
            if (current == quote)
            {
                end = position + 1;
                return true;
            }

            if (IsLineTerminator(current))
                break;

            if (current != '\\')
                continue;

            position++;
            if (position >= source.Length)
                break;

            if (source[position] == '\r' && position + 1 < source.Length && source[position + 1] == '\n')
                position++;
        }

        end = start;
        return false;
    }

    private static bool CanContinueExpression(string source, int tokenStart)
    {
        var current = source[tokenStart];
        if (current == '+')
            return tokenStart + 1 >= source.Length || source[tokenStart + 1] != '+';

        if (current == '-')
            return tokenStart + 1 >= source.Length || source[tokenStart + 1] != '-';

        if (current == '!')
            return tokenStart + 1 < source.Length && source[tokenStart + 1] == '=';

        if (current is '(' or '[' or '.' or '`' or '*' or '/' or '%' or '<' or '>' or
            '=' or '&' or '|' or '^' or '?' or ',')
        {
            return true;
        }

        return StartsKeyword(source, tokenStart, "in") || StartsKeyword(source, tokenStart, "instanceof");
    }

    private static bool StartsKeyword(string source, int start, string keyword)
    {
        if (!source.AsSpan(start).StartsWith(keyword, StringComparison.Ordinal))
            return false;

        var end = start + keyword.Length;
        return end >= source.Length || !IsIdentifierPart(source[end]);
    }

    private static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$' || value > 127;

    private static bool IsLineTerminator(char value) => value is '\r' or '\n' or '\u2028' or '\u2029';
}
