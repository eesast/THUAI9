using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Playback;
using UnityEngine;

namespace THUAI9.Unity.Live
{
    /// <summary>
    /// Runtime spectator client for the same gRPC stream used by the Avalonia UI.
    /// It only observes: team_id=0, side_flag=0, and never sends player actions.
    /// Frames received from the server are pushed into CoreParam.frameQueue and
    /// rendered by RenderManager's existing main-thread frame loop.
    /// </summary>
    public class LiveSpectatorClient : MonoBehaviour
    {
        private const string DefaultServerAddress = "127.0.0.1:8888";
        private const long SpectatorTeamId = 0;
        private const int SpectatorSideFlag = 0;

        [Header("实时观战")]
        public string serverAddress = DefaultServerAddress;
        public bool autoConnectOnStart = false;
        public bool autoReconnect = true;
        [Min(0.5f)] public float reconnectIntervalSeconds = 2f;

        private Channel channel;
        private AvailableService.AvailableServiceClient client;
        private AsyncServerStreamingCall<MessageToClient> stream;
        private CancellationTokenSource cancellation;
        private PlaybackController playbackController;

        private bool liveRequested;
        private bool isConnecting;
        private bool isConnected;
        private bool hasReceivedFirstFrame;
        private DateTime nextConnectUtc = DateTime.MinValue;
        private string statusText = "实时：未连接";
        private readonly long spectatorPlayerId = 2023 + System.Diagnostics.Process.GetCurrentProcess().Id;

        public bool IsLiveMode => liveRequested || isConnecting || isConnected;
        public bool IsConnecting => isConnecting;
        public bool IsConnected => isConnected;
        public string StatusText => statusText;
        public string ServerAddress => serverAddress;

        private void Awake()
        {
            playbackController = FindObjectOfType<PlaybackController>();
        }

        private void Start()
        {
            if (autoConnectOnStart)
            {
                StartLive(serverAddress);
            }
        }

        private void Update()
        {
            if (!liveRequested || isConnected || isConnecting)
            {
                return;
            }

            if (DateTime.UtcNow < nextConnectUtc)
            {
                return;
            }

            _ = ConnectOnceAsync();
        }

        public void StartLive(string address)
        {
            if (!string.IsNullOrWhiteSpace(address))
            {
                serverAddress = NormalizeGrpcTarget(address);
            }

            liveRequested = true;
            nextConnectUtc = DateTime.MinValue;
            statusText = $"实时：准备连接 {serverAddress}";

            if (isConnected || isConnecting)
            {
                return;
            }

            playbackController ??= FindObjectOfType<PlaybackController>();
            playbackController?.Stop();
            CoreParam.Reset();

            _ = ConnectOnceAsync();
        }

        public void StopLive()
        {
            liveRequested = false;
            hasReceivedFirstFrame = false;
            statusText = "实时：已断开";
            ReleaseConnectionResources();
        }

        private async Task ConnectOnceAsync()
        {
            if (isConnecting || isConnected)
            {
                return;
            }

            isConnecting = true;
            hasReceivedFirstFrame = false;
            statusText = $"实时：连接中 {serverAddress}";

            try
            {
                ReleaseConnectionResources();
                cancellation = new CancellationTokenSource();

                var channelOptions = new List<ChannelOption>
                {
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, -1),
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1)
                };

                channel = new Channel(serverAddress, ChannelCredentials.Insecure, channelOptions);
                await channel.ConnectAsync(deadline: DateTime.UtcNow.AddSeconds(10));
                client = new AvailableService.AvailableServiceClient(channel);

                await TryEnqueueStaticMapAsync(cancellation.Token);

                var request = new RegisterFactoryMsg
                {
                    TeamId = SpectatorTeamId,
                    PlayerId = spectatorPlayerId,
                    SideFlag = SpectatorSideFlag
                };

                stream = client.RegisterFactory(request, cancellationToken: cancellation.Token);
                isConnected = true;
                statusText = $"实时：已连接，等待首帧 spectator={spectatorPlayerId}";
                _ = ReceiveLoopAsync(cancellation.Token);
            }
            catch (Exception ex)
            {
                statusText = $"实时：连接失败，{ShortError(ex)}";
                ReleaseConnectionResources();
                ScheduleReconnect();
            }
            finally
            {
                isConnecting = false;
            }
        }

        private async Task TryEnqueueStaticMapAsync(CancellationToken token)
        {
            if (client == null)
            {
                return;
            }

            try
            {
                MessageOfMap map = await client.GetMapAsync(new NullRequest(), cancellationToken: token).ResponseAsync;
                if (map == null)
                {
                    return;
                }

                var mapFrame = new MessageToClient();
                mapFrame.ObjMessage.Add(new MessageOfObj { MapMessage = map });
                CoreParam.firstFrame = mapFrame;
                statusText = "实时：已拉取静态地图，等待实时帧";
            }
            catch (Exception ex)
            {
                statusText = $"实时：已连接，但拉取地图失败，{ShortError(ex)}";
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && isConnected && stream != null)
                {
                    bool hasMessage = await stream.ResponseStream.MoveNext(token);
                    if (!hasMessage)
                    {
                        statusText = hasReceivedFirstFrame
                            ? "实时：服务器消息流已结束"
                            : "实时：服务器消息流结束，未收到首帧";
                        break;
                    }

                    MessageToClient message = stream.ResponseStream.Current;
                    if (message == null)
                    {
                        continue;
                    }

                    if (!hasReceivedFirstFrame)
                    {
                        hasReceivedFirstFrame = true;
                        statusText = "实时：观战中";
                    }

                    CoreParam.playbackCurrentFrameIndex = -1;
                    CoreParam.playbackElapsedMilliseconds = 0;
                    CoreParam.frameQueue.Add(message);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (RpcException ex) when (ex.Status.StatusCode == StatusCode.Cancelled)
            {
            }
            catch (Exception ex)
            {
                if (liveRequested)
                {
                    statusText = $"实时：接收失败，{ShortError(ex)}";
                }
            }
            finally
            {
                isConnected = false;
                ReleaseConnectionResources();
                if (liveRequested)
                {
                    ScheduleReconnect();
                }
            }
        }

        private void ScheduleReconnect()
        {
            if (!liveRequested || !autoReconnect)
            {
                return;
            }

            nextConnectUtc = DateTime.UtcNow.AddSeconds(Mathf.Max(0.5f, reconnectIntervalSeconds));
        }

        private void ReleaseConnectionResources()
        {
            try
            {
                cancellation?.Cancel();
            }
            catch
            {
            }

            try
            {
                stream?.Dispose();
            }
            catch
            {
            }
            stream = null;
            client = null;

            if (channel != null)
            {
                try
                {
                    channel.ShutdownAsync().GetAwaiter().GetResult();
                }
                catch
                {
                }
                channel = null;
            }

            cancellation?.Dispose();
            cancellation = null;
            isConnected = false;
        }

        private static string NormalizeGrpcTarget(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return DefaultServerAddress;
            }

            string trimmed = address.Trim();
            if (!trimmed.Contains("://"))
            {
                return trimmed;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
            {
                return trimmed;
            }

            int port = uri.IsDefaultPort ? 8888 : uri.Port;
            return $"{uri.Host}:{port}";
        }

        private static string ShortError(Exception ex)
        {
            if (ex is RpcException rpc)
            {
                return $"{rpc.Status.StatusCode}: {rpc.Status.Detail}";
            }

            return ex.Message;
        }

        private void OnDestroy()
        {
            StopLive();
        }
    }
}
