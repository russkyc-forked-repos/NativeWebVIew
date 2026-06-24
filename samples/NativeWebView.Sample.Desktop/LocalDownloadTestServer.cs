using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NativeWebView.Sample.Desktop;

internal sealed class LocalDownloadTestServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Action<string> _log;
    private readonly Task _acceptLoop;
    private bool _disposed;

    private LocalDownloadTestServer(TcpListener listener, Action<string> log)
    {
        _listener = listener;
        _log = log;
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        PageUri = new Uri(BaseUri, "index.html");
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cancellationTokenSource.Token));
    }

    public Uri BaseUri { get; }

    public Uri PageUri { get; }

    public static LocalDownloadTestServer Start(Action<string> log)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LocalDownloadTestServer(listener, log);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        _cancellationTokenSource.Dispose();
        _ = _acceptLoop;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                _log($"Download test server accept failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        try
        {
            await using var stream = client.GetStream();
            var requestText = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            var requestLine = requestText.Split("\r\n").FirstOrDefault() ?? string.Empty;
            var path = ResolvePath(requestLine);
            var response = CreateResponse(path);
            await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
            _log($"Download test server: {requestLine}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log($"Download test server request failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<string> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var builder = new StringBuilder();

        while (builder.Length < 16384)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;

            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (builder.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                break;
        }

        return builder.ToString();
    }

    private static string ResolvePath(string requestLine)
    {
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return "/";

        return Uri.TryCreate(parts[1], UriKind.RelativeOrAbsolute, out var uri)
            ? uri.IsAbsoluteUri ? uri.PathAndQuery : parts[1]
            : "/";
    }

    private byte[] CreateResponse(string path)
    {
        var questionIndex = path.IndexOf('?', StringComparison.Ordinal);
        var pathOnly = questionIndex >= 0 ? path[..questionIndex] : path;

        return pathOnly switch
        {
            "/" or "/index.html" => CreateTextResponse("text/html; charset=utf-8", CreateIndexHtml()),
            "/download/attachment.txt" => CreateDownloadResponse(
                "text/plain; charset=utf-8",
                "attachment; filename=\"nativewebview-attachment.txt\"",
                "NativeWebView attachment download response.\n"),
            "/download/plain.txt" => CreateDownloadResponse(
                "text/plain; charset=utf-8",
                contentDisposition: null,
                "NativeWebView plain text response without Content-Disposition.\n"),
            "/download/file.zip" => CreateDownloadResponse(
                "application/zip",
                "attachment; filename=\"nativewebview-test.zip\"",
                "PK\u0005\u0006".PadRight(128, '\0')),
            "/export/report.csv" => CreateDownloadResponse(
                "text/csv; charset=utf-8",
                "attachment; filename=\"nativewebview-report.csv\"",
                "name,value\nalpha,1\nbeta,2\n"),
            "/download/attribute.bin" => CreateDownloadResponse(
                "application/octet-stream",
                contentDisposition: null,
                "NativeWebView download attribute response.\n"),
            _ => CreateTextResponse("text/plain; charset=utf-8", $"Not found: {path}\n", status: "404 Not Found"),
        };
    }

    private string CreateIndexHtml()
    {
        return $$"""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8">
              <title>NativeWebView Download Diagnostics</title>
              <style>
                body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; margin: 32px; line-height: 1.45; }
                a { display: block; margin: 12px 0; font-size: 16px; }
                code { background: #f1f3f5; padding: 2px 5px; border-radius: 4px; }
              </style>
            </head>
            <body>
              <h1>NativeWebView Download Diagnostics</h1>
              <p>Base URI: <code>{{BaseUri}}</code></p>
              <p>Click these links and watch the sample event log. Right-click each link to test the bridge path.</p>
              <a id="attachment" href="/download/attachment.txt">Attachment header text download</a>
              <a id="plain" href="/download/plain.txt">Plain .txt link without Content-Disposition</a>
              <a id="zip" href="/download/file.zip">ZIP-looking link with attachment header</a>
              <a id="query" href="/export/report.csv?download=1">Export route with query download marker</a>
              <a id="attribute" href="/download/attribute.bin" download="attribute-suggested.bin">HTML download attribute link</a>
              <script>
                document.addEventListener('click', event => {
                  const link = event.target && event.target.closest && event.target.closest('a[href]');
                  if (link) console.log('clicked ' + link.id + ' ' + link.href);
                }, true);
              </script>
            </body>
            </html>
            """;
    }

    private static byte[] CreateTextResponse(string contentType, string body, string status = "200 OK")
    {
        return CreateDownloadResponse(contentType, contentDisposition: null, body, status);
    }

    private static byte[] CreateDownloadResponse(
        string contentType,
        string? contentDisposition,
        string body,
        string status = "200 OK")
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n");

        if (!string.IsNullOrWhiteSpace(contentDisposition))
            headers.Append("Content-Disposition: ").Append(contentDisposition).Append("\r\n");

        headers.Append("\r\n");
        return [.. Encoding.ASCII.GetBytes(headers.ToString()), .. bodyBytes];
    }
}
