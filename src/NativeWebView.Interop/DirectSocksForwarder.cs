using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NativeWebView.Interop;

// Adapted from the issue-19 macOS feasibility spike. TCP CONNECT only; no proxy discovery or TLS interception.
internal sealed class DirectSocksForwarder : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<long, Task> _clients = new();
    private readonly SemaphoreSlim _slots;
    private readonly byte[] _username;
    private readonly byte[] _password;
    private readonly TimeSpan _timeout;
    private readonly Task _accepting;
    private readonly object _disposeGate = new();
    private Task? _disposal;
    private long _sequence;

    internal DirectSocksForwarder(int maxConnections = 128, TimeSpan? timeout = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConnections);
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        Username = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _username = Encoding.ASCII.GetBytes(Username);
        _password = Encoding.ASCII.GetBytes(Password);
        try
        {
            _listener.Start(maxConnections);
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accepting = AcceptAsync();
        }
        catch
        {
            _listener.Stop();
            _slots.Dispose();
            _stopping.Dispose();
            throw;
        }
    }

    internal int Port { get; }
    internal string Username { get; }
    internal string Password { get; }
    internal bool IsHealthy => !_stopping.IsCancellationRequested && !_accepting.IsCompleted;

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token).ConfigureAwait(false);
                if (!_slots.Wait(0))
                {
                    client.Dispose();
                    continue;
                }
                var id = Interlocked.Increment(ref _sequence);
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _clients[id] = completion.Task;
                _ = HandleTrackedAsync(id, client, completion);
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // A failed listener is not restarted beneath live stores. Cancel tunnels and leave the native route intact.
            _stopping.Cancel();
            _listener.Stop();
        }
    }

    private async Task HandleTrackedAsync(long id, TcpClient client, TaskCompletionSource completion)
    {
        try { await HandleAsync(client).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or ArgumentException)
        {
            // Connection failures belong to this tunnel and surface through the socket to WebKit.
        }
        finally
        {
            client.Dispose();
            _slots.Release();
            completion.TrySetResult();
            _clients.TryRemove(id, out _);
        }
    }

    private static async Task<byte[]> ReadAsync(NetworkStream stream, int length, CancellationToken token)
    {
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    private static ValueTask ReplyAsync(NetworkStream stream, byte code, CancellationToken token, IPEndPoint? bound = null)
    {
        var address = bound?.Address.GetAddressBytes() ?? new byte[4];
        var message = new byte[6 + address.Length];
        message[0] = 5;
        message[1] = code;
        message[3] = address.Length == 16 ? (byte)4 : (byte)1;
        address.CopyTo(message, 4);
        var port = bound?.Port ?? 0;
        message[^2] = (byte)(port >> 8);
        message[^1] = (byte)port;
        return stream.WriteAsync(message, token);
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        using var outbound = new TcpClient();
        handshake.CancelAfter(_timeout);
        var token = handshake.Token;
        var stream = client.GetStream();
        var hello = await ReadAsync(stream, 2, token).ConfigureAwait(false);
        if (hello[0] != 5 || hello[1] == 0)
            return;
        var methods = await ReadAsync(stream, hello[1], token).ConfigureAwait(false);
        if (!methods.Contains((byte)2))
        {
            await stream.WriteAsync(new byte[] { 5, 255 }, token).ConfigureAwait(false);
            return;
        }
        await stream.WriteAsync(new byte[] { 5, 2 }, token).ConfigureAwait(false);
        var auth = await ReadAsync(stream, 2, token).ConfigureAwait(false);
        if (auth[0] != 1 || auth[1] == 0)
        {
            await stream.WriteAsync(new byte[] { 1, 1 }, token).ConfigureAwait(false);
            return;
        }
        var suppliedUser = await ReadAsync(stream, auth[1], token).ConfigureAwait(false);
        byte[]? suppliedPassword = null;
        bool valid;
        try
        {
            var length = (await ReadAsync(stream, 1, token).ConfigureAwait(false))[0];
            suppliedPassword = await ReadAsync(stream, length, token).ConfigureAwait(false);
            valid = CryptographicOperations.FixedTimeEquals(_username, suppliedUser) &
                CryptographicOperations.FixedTimeEquals(_password, suppliedPassword);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(suppliedUser);
            if (suppliedPassword is not null)
                CryptographicOperations.ZeroMemory(suppliedPassword);
        }
        await stream.WriteAsync(new byte[] { 1, valid ? (byte)0 : (byte)1 }, token).ConfigureAwait(false);
        if (!valid)
            return;

        var header = await ReadAsync(stream, 4, token).ConfigureAwait(false);
        if (header[0] != 5 || header[2] != 0)
        {
            await ReplyAsync(stream, 1, token).ConfigureAwait(false);
            return;
        }
        if (header[1] != 1)
        {
            await ReplyAsync(stream, 7, token).ConfigureAwait(false);
            return;
        }
        string host;
        switch (header[3])
        {
            case 1: host = new IPAddress(await ReadAsync(stream, 4, token).ConfigureAwait(false)).ToString(); break;
            case 4: host = new IPAddress(await ReadAsync(stream, 16, token).ConfigureAwait(false)).ToString(); break;
            case 3:
                var length = (await ReadAsync(stream, 1, token).ConfigureAwait(false))[0];
                var name = await ReadAsync(stream, length, token).ConfigureAwait(false);
                if (length == 0 || name.Any(b => !(b is >= 48 and <= 57 or >= 65 and <= 90 or >= 97 and <= 122 or 45 or 46)))
                {
                    await ReplyAsync(stream, 8, token).ConfigureAwait(false);
                    return;
                }
                host = Encoding.ASCII.GetString(name);
                break;
            default:
                await ReplyAsync(stream, 8, token).ConfigureAwait(false);
                return;
        }
        var portBytes = await ReadAsync(stream, 2, token).ConfigureAwait(false);
        var port = (portBytes[0] << 8) | portBytes[1];
        handshake.CancelAfter(Timeout.InfiniteTimeSpan);
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        connect.CancelAfter(_timeout);
        try
        {
            if (port == 0)
                throw new ArgumentException("Destination port must be nonzero.");
            await outbound.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            var code = (ex as SocketException)?.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => (byte)5,
                SocketError.NetworkUnreachable => (byte)3,
                SocketError.HostNotFound or SocketError.HostUnreachable => (byte)4,
                _ => (byte)1,
            };
            using var reply = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            reply.CancelAfter(TimeSpan.FromSeconds(1));
            await ReplyAsync(stream, code, reply.Token).ConfigureAwait(false);
            return;
        }
        using (var reply = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token))
        {
            reply.CancelAfter(TimeSpan.FromSeconds(1));
            await ReplyAsync(stream, 0, reply.Token, (IPEndPoint)outbound.Client.LocalEndPoint!).ConfigureAwait(false);
        }
        client.NoDelay = outbound.NoDelay = true;
        using var relay = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        async Task PumpAsync(NetworkStream from, NetworkStream to, Socket destination)
        {
            var buffer = new byte[32 * 1024];
            try
            {
                while (true)
                {
                    var count = await from.ReadAsync(buffer, relay.Token).ConfigureAwait(false);
                    if (count == 0)
                    {
                        destination.Shutdown(SocketShutdown.Send);
                        return;
                    }
                    await to.WriteAsync(buffer.AsMemory(0, count), relay.Token).ConfigureAwait(false);
                }
            }
            catch { relay.Cancel(); throw; }
        }
        // Cache before either pump can observe EOF and half-close the socket.
        var outboundStream = outbound.GetStream();
        await Task.WhenAll(PumpAsync(stream, outboundStream, outbound.Client),
            PumpAsync(outboundStream, stream, client.Client)).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate)
            return new ValueTask(_disposal ??= StopAsync());
    }

    private async Task StopAsync()
    {
        _stopping.Cancel();
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        await Task.WhenAll(_clients.Values).ConfigureAwait(false);
        CryptographicOperations.ZeroMemory(_username);
        CryptographicOperations.ZeroMemory(_password);
        _slots.Dispose();
        _stopping.Dispose();
    }
}
