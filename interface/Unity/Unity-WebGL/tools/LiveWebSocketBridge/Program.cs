using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Protobuf;

namespace LiveWebSocketBridge;

internal static class Program
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    public static async Task<int> Main(string[] args)
    {
        string server = GetArg(args, "--server", "127.0.0.1:8888");
        int port = GetIntArg(args, "--port", 18091);
        long spectatorId = GetLongArg(args, "--player-id", 2023 + Environment.ProcessId);

        using CancellationTokenSource cts = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        LiveFrameBroadcaster broadcaster = new(IPAddress.Loopback, port);
        Task wsTask = broadcaster.RunAsync(cts.Token);

        Console.WriteLine($"[THUAI9 Live Bridge] WebSocket: ws://127.0.0.1:{port}/live");
        Console.WriteLine($"[THUAI9 Live Bridge] Server: {server}, spectator player_id={spectatorId}");

        try
        {
            await RunGrpcSpectatorLoopAsync(server, spectatorId, broadcaster, cts.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            cts.Cancel();
            try { await wsTask.ConfigureAwait(false); } catch { }
        }
    }

    private static async Task RunGrpcSpectatorLoopAsync(string server, long spectatorId, LiveFrameBroadcaster broadcaster, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Channel? channel = null;
            AsyncServerStreamingCall<MessageToClient>? streamCall = null;
            try
            {
                channel = new Channel(server, ChannelCredentials.Insecure, new[]
                {
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, -1),
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1),
                });

                await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(8)).ConfigureAwait(false);
                AvailableService.AvailableServiceClient client = new(channel);
                RegisterFactoryMsg request = new()
                {
                    PlayerId = spectatorId,
                    TeamId = 0,
                    SideFlag = 0,
                };

                streamCall = client.RegisterFactory(request, cancellationToken: token);
                Console.WriteLine("[THUAI9 Live Bridge] Spectator stream registered; waiting for game frames...");

                int frameCount = 0;
                while (await streamCall.ResponseStream.MoveNext(token).ConfigureAwait(false))
                {
                    MessageToClient frame = streamCall.ResponseStream.Current;
                    string base64 = Convert.ToBase64String(frame.ToByteArray());
                    broadcaster.Publish(base64);
                    frameCount++;
                    if (frameCount == 1 || frameCount % 100 == 0)
                    {
                        Console.WriteLine($"[THUAI9 Live Bridge] Forwarded {frameCount} live frame(s); websocket clients={broadcaster.ClientCount}");
                    }
                }

                Console.WriteLine("[THUAI9 Live Bridge] Server stream ended; reconnecting...");
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled || token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[THUAI9 Live Bridge] gRPC error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { streamCall?.Dispose(); } catch { }
                if (channel != null)
                {
                    try { await channel.ShutdownAsync().ConfigureAwait(false); } catch { }
                }
            }

            await Task.Delay(RetryDelay, token).ConfigureAwait(false);
        }
    }

    private static string GetArg(string[] args, string name, string defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return defaultValue;
    }

    private static int GetIntArg(string[] args, string name, int defaultValue)
        => int.TryParse(GetArg(args, name, string.Empty), out int value) ? value : defaultValue;

    private static long GetLongArg(string[] args, string name, long defaultValue)
        => long.TryParse(GetArg(args, name, string.Empty), out long value) ? value : defaultValue;
}

internal sealed class LiveFrameBroadcaster
{
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly IPAddress address;
    private readonly int port;
    private readonly TcpListener listener;
    private readonly ConcurrentDictionary<int, WebSocketPeer> peers = new();
    private int nextPeerId;
    private string? latestFrameBase64;

    public LiveFrameBroadcaster(IPAddress address, int port)
    {
        this.address = address;
        this.port = port;
        listener = new TcpListener(address, port);
    }

    public int ClientCount => peers.Count;

    public async Task RunAsync(CancellationToken token)
    {
        listener.Start();
        try
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient tcpClient = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(tcpClient, token), token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            listener.Stop();
            foreach (WebSocketPeer peer in peers.Values)
            {
                peer.Dispose();
            }
        }
    }

    public void Publish(string base64Frame)
    {
        if (string.IsNullOrWhiteSpace(base64Frame))
        {
            return;
        }

        latestFrameBase64 = base64Frame;
        foreach (KeyValuePair<int, WebSocketPeer> pair in peers.ToArray())
        {
            if (!pair.Value.TrySendText(base64Frame))
            {
                RemovePeer(pair.Key);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken token)
    {
        int peerId = Interlocked.Increment(ref nextPeerId);
        WebSocketPeer? peer = null;
        try
        {
            tcpClient.NoDelay = true;
            NetworkStream stream = tcpClient.GetStream();
            string headers = await ReadHttpHeadersAsync(stream, token).ConfigureAwait(false);
            string key = ParseHeader(headers, "Sec-WebSocket-Key");
            if (string.IsNullOrWhiteSpace(key))
            {
                tcpClient.Dispose();
                return;
            }

            byte[] response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Accept: {BuildAcceptKey(key)}\r\n\r\n");
            await stream.WriteAsync(response, token).ConfigureAwait(false);

            peer = new WebSocketPeer(tcpClient, stream);
            peers[peerId] = peer;
            Console.WriteLine($"[THUAI9 Live Bridge] Browser websocket connected from {tcpClient.Client.RemoteEndPoint}; clients={peers.Count}");

            string? snapshot = latestFrameBase64;
            if (!string.IsNullOrWhiteSpace(snapshot))
            {
                peer.TrySendText(snapshot);
            }

            byte[] buffer = new byte[256];
            while (!token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[THUAI9 Live Bridge] WebSocket client closed: {ex.Message}");
        }
        finally
        {
            RemovePeer(peerId);
            peer?.Dispose();
        }
    }

    private void RemovePeer(int peerId)
    {
        if (peers.TryRemove(peerId, out WebSocketPeer? peer))
        {
            peer.Dispose();
            Console.WriteLine($"[THUAI9 Live Bridge] Browser websocket disconnected; clients={peers.Count}");
        }
    }

    private static async Task<string> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken token)
    {
        List<byte> bytes = new();
        byte[] one = new byte[1];
        while (bytes.Count < 16 * 1024)
        {
            int read = await stream.ReadAsync(one, token).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            bytes.Add(one[0]);
            int count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static string ParseHeader(string headers, string headerName)
    {
        foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string name = line[..colon].Trim();
            if (string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return line[(colon + 1)..].Trim();
            }
        }

        return string.Empty;
    }

    private static string BuildAcceptKey(string key)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid));
        return Convert.ToBase64String(hash);
    }
}

internal sealed class WebSocketPeer : IDisposable
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly object sendLock = new();
    private bool disposed;

    public WebSocketPeer(TcpClient client, NetworkStream stream)
    {
        this.client = client;
        this.stream = stream;
    }

    public bool TrySendText(string text)
    {
        if (disposed)
        {
            return false;
        }

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            byte[] header = BuildHeader(payload.Length);
            lock (sendLock)
            {
                stream.Write(header, 0, header.Length);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] BuildHeader(int payloadLength)
    {
        if (payloadLength < 126)
        {
            return new[] { (byte)0x81, (byte)payloadLength };
        }

        if (payloadLength <= ushort.MaxValue)
        {
            byte[] header = new byte[4];
            header[0] = 0x81;
            header[1] = 126;
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), (ushort)payloadLength);
            return header;
        }

        byte[] longHeader = new byte[10];
        longHeader[0] = 0x81;
        longHeader[1] = 127;
        BinaryPrimitives.WriteUInt64BigEndian(longHeader.AsSpan(2), (ulong)payloadLength);
        return longHeader;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try { stream.Dispose(); } catch { }
        try { client.Dispose(); } catch { }
    }
}
