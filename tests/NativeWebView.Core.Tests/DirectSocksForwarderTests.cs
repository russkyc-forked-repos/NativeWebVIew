using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using NativeWebView.Interop;

namespace NativeWebView.Core.Tests;

public sealed class DirectSocksForwarderTests
{
    private static async Task<byte[]> ReadAsync(NetworkStream stream, int count)
    {
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        return bytes;
    }

    private static async Task<TcpClient> AuthenticateAsync(DirectSocksForwarder forwarder, string? password = null, bool fragment = false)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, forwarder.Port);
        try
        {
            var stream = client.GetStream();
            await stream.WriteAsync(new byte[] { 5, 1, 2 });
            Assert.Equal(new byte[] { 5, 2 }, await ReadAsync(stream, 2));
            var user = Encoding.ASCII.GetBytes(forwarder.Username);
            var pass = Encoding.ASCII.GetBytes(password ?? forwarder.Password);
            byte[] auth = [1, (byte)user.Length, .. user, (byte)pass.Length, .. pass];
            if (fragment)
            {
                foreach (var value in auth)
                    await stream.WriteAsync(new byte[] { value });
            }
            else
                await stream.WriteAsync(auth);
            Assert.Equal(new byte[] { 1, password is null ? (byte)0 : (byte)1 }, await ReadAsync(stream, 2));
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task<byte> ConnectAsync(TcpClient client, string host, int port, byte addressType)
    {
        byte[] address = addressType == 3
            ? [(byte)host.Length, .. Encoding.ASCII.GetBytes(host)] : IPAddress.Parse(host).GetAddressBytes();
        await client.GetStream().WriteAsync(new byte[] { 5, 1, 0, addressType }.Concat(address).Concat(new byte[] { (byte)(port >> 8), (byte)port }).ToArray());
        var header = await ReadAsync(client.GetStream(), 4);
        Assert.Equal(5, header[0]);
        await ReadAsync(client.GetStream(), (header[3] == 4 ? 16 : 4) + 2);
        return header[1];
    }

    [Theory]
    [InlineData("127.0.0.1", 1)]
    [InlineData("localhost", 3)]
    [InlineData("::1", 4)]
    public async Task Connect_RelaysLargePayloadAndHalfClose(string host, byte type)
    {
        var address = type == 4 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var origin = new TcpListener(address, 0);
        origin.Start();
        await using var proxy = new DirectSocksForwarder();
        using var client = await AuthenticateAsync(proxy, fragment: true);
        var accept = origin.AcceptTcpClientAsync();
        Assert.Equal(0, await ConnectAsync(client, host, ((IPEndPoint)origin.LocalEndpoint).Port, type));
        using var peer = await accept.WaitAsync(TimeSpan.FromSeconds(10));
        var payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        var originWork = Task.Run(async () =>
        {
            using var received = new MemoryStream();
            await peer.GetStream().CopyToAsync(received);
            Assert.Equal(payload, received.ToArray());
            await peer.GetStream().WriteAsync(SHA256.HashData(received.ToArray()));
            peer.Client.Shutdown(SocketShutdown.Send);
        });
        var stream = client.GetStream();
        await stream.WriteAsync(payload);
        client.Client.Shutdown(SocketShutdown.Send);
        Assert.Equal(SHA256.HashData(payload), await ReadAsync(stream, 32));
        Assert.Equal(0, await stream.ReadAsync(new byte[1]));
        await originWork.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ImmediateClientHalfClose_AllowsOriginResponse()
    {
        await using var proxy = new DirectSocksForwarder();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var client = await AuthenticateAsync(proxy);
        var stream = client.GetStream();
        Assert.Equal(0, await ConnectAsync(client, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        client.Client.Shutdown(SocketShutdown.Send);
        using var peer = await origin.AcceptTcpClientAsync();
        var peerStream = peer.GetStream();
        Assert.Equal(0, await peerStream.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        await peerStream.WriteAsync(new byte[] { 17 });
        Assert.Equal(new byte[] { 17 }, await ReadAsync(stream, 1));
    }

    [Fact]
    public async Task Authentication_RejectsMissingAndWrongCredentials()
    {
        await using var proxy = new DirectSocksForwarder();
        using var unauthenticated = new TcpClient();
        await unauthenticated.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await unauthenticated.GetStream().WriteAsync(new byte[] { 5, 1, 0 });
        Assert.Equal(new byte[] { 5, 255 }, await ReadAsync(unauthenticated.GetStream(), 2));
        using var wrong = await AuthenticateAsync(proxy, "incorrect");
        Assert.Equal(0, await wrong.GetStream().ReadAsync(new byte[1]));
    }

    [Theory]
    [InlineData(2, 1, 0, 7)]
    [InlineData(3, 1, 0, 7)]
    [InlineData(1, 9, 0, 8)]
    [InlineData(1, 1, 1, 1)]
    public async Task UnsupportedAndMalformedRequests_ReturnProtocolFailure(byte command, byte addressType, byte reserved, byte expected)
    {
        await using var proxy = new DirectSocksForwarder();
        using var client = await AuthenticateAsync(proxy);
        await client.GetStream().WriteAsync(new byte[] { 5, command, reserved, addressType });
        Assert.Equal(expected, (await ReadAsync(client.GetStream(), 10))[1]);
    }

    [Fact]
    public async Task OutboundFailures_ReturnReplies()
    {
        using var closed = new TcpListener(IPAddress.Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        await using var proxy = new DirectSocksForwarder();
        using var client = await AuthenticateAsync(proxy);
        Assert.Equal(5, await ConnectAsync(client, "127.0.0.1", port, 1));
        using var dns = await AuthenticateAsync(proxy);
        Assert.NotEqual(0, await ConnectAsync(dns, "nativewebview.invalid", 80, 3));
    }

    [Fact]
    public async Task HandshakeTimeoutAndCapacity_AreBounded()
    {
        await using var proxy = new DirectSocksForwarder(maxConnections: 1, timeout: TimeSpan.FromMilliseconds(300));
        using var first = new TcpClient();
        await first.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await first.GetStream().WriteAsync(new byte[] { 5, 1, 2 });
        await ReadAsync(first.GetStream(), 2); // Slot is definitely occupied.
        using var excess = new TcpClient();
        await excess.ConnectAsync(IPAddress.Loopback, proxy.Port);
        Assert.Equal(0, await excess.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, await first.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Disposal_CancelsActiveAndIncompleteConnections_AndIsIdempotent()
    {
        var proxy = new DirectSocksForwarder();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var active = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(active, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var peer = await origin.AcceptTcpClientAsync();
        using var incomplete = new TcpClient();
        await incomplete.ConnectAsync(IPAddress.Loopback, proxy.Port);
        await Task.WhenAll(proxy.DisposeAsync().AsTask(), proxy.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, await active.GetStream().ReadAsync(new byte[1]));
        Assert.Equal(0, await peer.GetStream().ReadAsync(new byte[1]));
    }

    [Fact]
    public async Task EstablishedTunnel_RemainsUsableBeyondPrototypeLifetime()
    {
        await using var proxy = new DirectSocksForwarder();
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var client = await AuthenticateAsync(proxy);
        Assert.Equal(0, await ConnectAsync(client, "127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port, 1));
        using var peer = await origin.AcceptTcpClientAsync();
        await Task.Delay(TimeSpan.FromSeconds(61));
        await client.GetStream().WriteAsync(new byte[] { 42 });
        Assert.Equal(new byte[] { 42 }, await ReadAsync(peer.GetStream(), 1));
    }
}
