using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Playback;
using THUAI9.Unity.Render;
using UnityEngine;

namespace THUAI9.Unity.Live
{
    /// <summary>
    /// Runtime spectator client for the same gRPC stream used by the Avalonia UI.
    /// It only observes: team_id=0, side_flag=0, and never sends player actions.
    /// Frames received from the server are pushed through FrameSourceHub and
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
        private bool liveEndedCleanly;
        private DateTime nextConnectUtc = DateTime.MinValue;
        private string statusText = "实时：未连接";
        private int receivedFrameCount;
        private int lastReceivedObjectCount;
        private int lastReceivedTeamCount;
        private int lastReceivedCharacterCount;
        private int lastReceivedFactoryCount;
        private int lastReceivedResourceCount;
        private int lastReceivedMapMessageCount;
        private int maxReceivedCharacterCount;
        private readonly long spectatorPlayerId = 2023 + System.Diagnostics.Process.GetCurrentProcess().Id;
        private static bool nativeGrpcSearchPathConfigured;

        public bool IsLiveMode => liveRequested || isConnecting || isConnected;
        public bool IsConnecting => isConnecting;
        public bool IsConnected => isConnected;
        public string StatusText => statusText;
        public string ServerAddress => serverAddress;
        public int ReceivedFrameCount => receivedFrameCount;
        public int LastReceivedObjectCount => lastReceivedObjectCount;
        public int LastReceivedTeamCount => lastReceivedTeamCount;
        public int LastReceivedCharacterCount => lastReceivedCharacterCount;
        public int LastReceivedFactoryCount => lastReceivedFactoryCount;
        public int LastReceivedResourceCount => lastReceivedResourceCount;
        public int LastReceivedMapMessageCount => lastReceivedMapMessageCount;
        public int MaxReceivedCharacterCount => maxReceivedCharacterCount;
        public int QueuedFrameCount => FrameSourceHub.QueueSize;
        public int SubmittedFrameCount => FrameSourceHub.SubmittedFrameCount;
        public int DequeuedFrameCount => FrameSourceHub.DequeuedFrameCount;
        public int RenderedFrameCount => FrameSourceHub.RenderedFrameCount;

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
            PumpQueuedLiveFrames();

            if (!liveRequested || liveEndedCleanly || isConnected || isConnecting)
            {
                return;
            }

            if (DateTime.UtcNow < nextConnectUtc)
            {
                return;
            }

            _ = ConnectOnceAsync();
        }

        private void PumpQueuedLiveFrames()
        {
            if (!IsLiveMode || FrameSourceHub.ActiveKind != FrameSourceHub.SourceKind.Live || FrameSourceHub.QueueSize <= 0)
            {
                return;
            }

            if (!RenderManager.TryGetInstance(out RenderManager renderManager) || renderManager == null)
            {
                return;
            }

            // Keep this as a safety net only; RenderManager owns the normal frame
            // loop.  One extra frame per Unity Update is enough to prevent a live
            // queue from staying permanently stuck if the coroutine was interrupted.
            renderManager.PumpQueuedFrames(1);
        }

        public void StartLive(string address)
        {
            if (!string.IsNullOrWhiteSpace(address))
            {
                serverAddress = NormalizeGrpcTarget(address);
            }

            liveRequested = true;
            liveEndedCleanly = false;
            ResetReceiveCounters();
            nextConnectUtc = DateTime.MinValue;
            statusText = $"实时：准备连接 {serverAddress}";

            if (isConnected || isConnecting)
            {
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
                return;
            }

            playbackController ??= FindObjectOfType<PlaybackController>();
            playbackController?.Stop();
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);

            _ = ConnectOnceAsync();
        }

        public void StopLive()
        {
            liveRequested = false;
            liveEndedCleanly = false;
            hasReceivedFirstFrame = false;
            ResetReceiveCounters();
            statusText = "实时：已断开";
            ReleaseConnectionResources();
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.None, "未选择", statusText);
        }

        private async Task ConnectOnceAsync()
        {
            if (isConnecting || isConnected)
            {
                return;
            }

            isConnecting = true;
            hasReceivedFirstFrame = false;
            liveEndedCleanly = false;
            ResetReceiveCounters();
            statusText = $"实时：连接中 {serverAddress}";
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);

            try
            {
                ReleaseConnectionResources();
                cancellation = new CancellationTokenSource();
                EnsureNativeGrpcSearchPath();

                var channelOptions = new List<ChannelOption>
                {
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, -1),
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1)
                };

                channel = new Channel(serverAddress, ChannelCredentials.Insecure, channelOptions);
                client = new AvailableService.AvailableServiceClient(channel);

                var request = new RegisterFactoryMsg
                {
                    TeamId = SpectatorTeamId,
                    PlayerId = spectatorPlayerId,
                    SideFlag = SpectatorSideFlag
                };

                stream = client.RegisterFactory(request, cancellationToken: cancellation.Token);
                isConnected = true;
                statusText = $"实时：已连接，等待首帧 spectator={spectatorPlayerId}";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
                CancellationToken receiveToken = cancellation.Token;
                _ = Task.Run(() => ReceiveLoopAsync(receiveToken), receiveToken);
            }
            catch (Exception ex)
            {
                statusText = $"实时：连接失败，{ShortError(ex)}";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
                ReleaseConnectionResources();
                ScheduleReconnect();
            }
            finally
            {
                isConnecting = false;
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            bool shouldReconnect = true;
            try
            {
                while (!token.IsCancellationRequested && isConnected && stream != null)
                {
                    bool hasMessage = await stream.ResponseStream.MoveNext(token).ConfigureAwait(false);
                    if (!hasMessage)
                    {
                        statusText = hasReceivedFirstFrame
                            ? "实时：服务器消息流已结束"
                            : "实时：服务器消息流结束，未收到首帧";
                        FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
                        liveEndedCleanly = hasReceivedFirstFrame;
                        shouldReconnect = !hasReceivedFirstFrame;
                        break;
                    }

                    MessageToClient message = stream.ResponseStream.Current;
                    if (message == null)
                    {
                        continue;
                    }

                    receivedFrameCount++;
                    UpdateReceiveCounters(message);

                    if (!hasReceivedFirstFrame)
                    {
                        hasReceivedFirstFrame = true;
                        statusText = "实时：观战中";
                        FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
                    }

                    FrameSourceHub.EnqueueFrame(message, -1, 0);
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
                    FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
                }
            }
            finally
            {
                isConnected = false;
                ReleaseConnectionResources();
                if (liveRequested && shouldReconnect)
                {
                    ScheduleReconnect();
                }
            }
        }

        private string BuildLiveSourceName()
        {
            return $"实时：{serverAddress}";
        }

        private void ResetReceiveCounters()
        {
            receivedFrameCount = 0;
            lastReceivedObjectCount = 0;
            lastReceivedTeamCount = 0;
            lastReceivedCharacterCount = 0;
            lastReceivedFactoryCount = 0;
            lastReceivedResourceCount = 0;
            lastReceivedMapMessageCount = 0;
            maxReceivedCharacterCount = 0;
        }

        private void UpdateReceiveCounters(MessageToClient message)
        {
            lastReceivedObjectCount = message.ObjMessage.Count;
            lastReceivedTeamCount = message.AllMessage?.Teams.Count ?? 0;
            lastReceivedCharacterCount = 0;
            lastReceivedFactoryCount = 0;
            lastReceivedResourceCount = 0;
            lastReceivedMapMessageCount = 0;

            foreach (MessageOfObj obj in message.ObjMessage)
            {
                switch (obj.MessageOfObjCase)
                {
                    case MessageOfObj.MessageOfObjOneofCase.CharacterMessage:
                        lastReceivedCharacterCount++;
                        break;
                    case MessageOfObj.MessageOfObjOneofCase.FactoryMessage:
                        lastReceivedFactoryCount++;
                        break;
                    case MessageOfObj.MessageOfObjOneofCase.ResourceMessage:
                        lastReceivedResourceCount++;
                        break;
                    case MessageOfObj.MessageOfObjOneofCase.MapMessage:
                        lastReceivedMapMessageCount++;
                        break;
                }
            }

            maxReceivedCharacterCount = Math.Max(maxReceivedCharacterCount, lastReceivedCharacterCount);
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
                ShutdownChannelInBackground(channel);
                channel = null;
            }

            cancellation?.Dispose();
            cancellation = null;
            isConnected = false;
        }

        private static async void ShutdownChannelInBackground(Channel channelToShutdown)
        {
            try
            {
                await channelToShutdown.ShutdownAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);
#endif

        private static void EnsureNativeGrpcSearchPath()
        {
            if (nativeGrpcSearchPathConfigured)
            {
                return;
            }

            nativeGrpcSearchPathConfigured = true;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string pluginDirectory = Path.Combine(Application.dataPath, "Plugins", "x86_64");
            if (!Directory.Exists(pluginDirectory))
            {
                return;
            }

            string sourceLibraryPath = Path.Combine(pluginDirectory, "grpc_csharp_ext.x64.dll");
            if (!File.Exists(sourceLibraryPath))
            {
                return;
            }

            string grpcTempDirectory = Path.Combine(
                Application.temporaryCachePath,
                $"grpc-native-{System.Diagnostics.Process.GetCurrentProcess().Id}");
            Directory.CreateDirectory(grpcTempDirectory);

            // Grpc.Core's Unity fallback imports the native extension by the base
            // name "grpc_csharp_ext".  The NuGet runtime asset is suffixed
            // ".x64.dll", so expose both names from a writable runtime directory
            // instead of duplicating the 12 MB binary inside Assets.
            string aliasLibraryPath = Path.Combine(grpcTempDirectory, "grpc_csharp_ext.dll");
            string suffixedLibraryPath = Path.Combine(grpcTempDirectory, "grpc_csharp_ext.x64.dll");
            CopyNativeLibraryIfMissing(sourceLibraryPath, aliasLibraryPath);
            CopyNativeLibraryIfMissing(sourceLibraryPath, suffixedLibraryPath);

            SetDllDirectory(grpcTempDirectory);
            IntPtr loadedLibrary = LoadLibrary(aliasLibraryPath);
            if (loadedLibrary == IntPtr.Zero)
            {
                LoadLibrary(suffixedLibraryPath);
            }
#endif
        }

        private static void CopyNativeLibraryIfMissing(string sourcePath, string destinationPath)
        {
            if (!File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath);
            }
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
