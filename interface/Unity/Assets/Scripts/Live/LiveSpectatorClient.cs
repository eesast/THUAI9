using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#if !UNITY_WEBGL || UNITY_EDITOR
using Grpc.Core;
#endif
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

        [Header("Event Status")]
        [Tooltip("GetCurrentEventStatus needs an existing team; spectator polls Team 1 by default for the global event.")]
        public long eventStatusTeamId = 1;
        public long eventStatusPlayerId = 0;
        [Min(1f)] public float eventStatusPollIntervalSeconds = 5f;

#if !UNITY_WEBGL || UNITY_EDITOR
        private Channel channel;
        private AvailableService.AvailableServiceClient client;
        private AsyncServerStreamingCall<MessageToClient> stream;
        private CancellationTokenSource cancellation;
#endif
        private PlaybackController playbackController;

        private bool liveRequested;
        private bool externalLiveMode;
        private bool isConnecting;
        private bool isConnected;
        private bool hasReceivedFirstFrame;
        private bool liveEndedCleanly;
        private DateTime nextConnectUtc = DateTime.MinValue;
        private string externalLiveSourceName = "WebGL Live";
        private string statusText = "实时：未连接";
        private string currentEventName = string.Empty;
        private string currentEventDescription = string.Empty;
        private bool hasCurrentEventStatus;
#if !UNITY_WEBGL || UNITY_EDITOR
        private bool eventStatusPollInFlight;
        private float nextEventStatusPollTime;
#endif
        private bool isApplicationQuitting;
        private int receivedFrameCount;
        private int lastReceivedObjectCount;
        private int lastReceivedTeamCount;
        private int lastReceivedCharacterCount;
        private int lastReceivedFactoryCount;
        private int lastReceivedResourceCount;
        private int lastReceivedMapMessageCount;
        private int maxReceivedCharacterCount;
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly long spectatorPlayerId = 2023 + System.Diagnostics.Process.GetCurrentProcess().Id;
        private static bool nativeGrpcSearchPathConfigured;
#endif

        public bool IsLiveMode => liveRequested || isConnecting || isConnected;
        public bool IsConnecting => isConnecting;
        public bool IsConnected => isConnected || externalLiveMode;
        public string StatusText => statusText;
        public string ServerAddress => serverAddress;
        public bool HasCurrentEventStatus => hasCurrentEventStatus;
        public string CurrentEventName => currentEventName;
        public string CurrentEventDescription => currentEventDescription;
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
            MaybePollCurrentEventStatus();

            if (!liveRequested || externalLiveMode || liveEndedCleanly || isConnected || isConnecting)
            {
                return;
            }

            if (DateTime.UtcNow < nextConnectUtc)
            {
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            _ = ConnectOnceAsync();
#endif
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
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(address))
            {
                externalLiveSourceName = address.Trim();
            }

            StartExternalLive(externalLiveSourceName);
            statusText = $"实时：WebGL 等待网页推帧 {externalLiveSourceName}";
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
            return;
#else
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
#endif
        }

        public void StopLive()
        {
            StopLive(resetFrameSource: true, waitForShutdown: false);
        }

        private void StopLive(bool resetFrameSource, bool waitForShutdown)
        {
            liveRequested = false;
            externalLiveMode = false;
            liveEndedCleanly = false;
            hasReceivedFirstFrame = false;
            ResetReceiveCounters();
            statusText = "实时：已断开";
            ReleaseConnectionResources(waitForShutdown);
            if (resetFrameSource)
            {
                FrameSourceHub.Reset(FrameSourceHub.SourceKind.None, "未选择", statusText);
            }
        }

        public void StartExternalLive(string sourceName = null)
        {
            if (!string.IsNullOrWhiteSpace(sourceName))
            {
                externalLiveSourceName = sourceName.Trim();
            }

            playbackController ??= FindObjectOfType<PlaybackController>();
            playbackController?.Stop();
            ReleaseConnectionResources(waitForShutdown: false);

            liveRequested = true;
            externalLiveMode = true;
            isConnecting = false;
            isConnected = false;
            liveEndedCleanly = false;
            hasReceivedFirstFrame = false;
            ResetReceiveCounters();
            statusText = $"实时：等待 {externalLiveSourceName} 推帧";
            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
        }

        public bool SubmitExternalLiveFrame(MessageToClient message, string sourceName = null)
        {
            if (message == null)
            {
                return false;
            }

            if (!externalLiveMode)
            {
                StartExternalLive(sourceName);
            }
            else if (!string.IsNullOrWhiteSpace(sourceName) && sourceName != externalLiveSourceName)
            {
                externalLiveSourceName = sourceName.Trim();
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
            }

            receivedFrameCount++;
            UpdateReceiveCounters(message);

            if (!hasReceivedFirstFrame)
            {
                hasReceivedFirstFrame = true;
                statusText = $"实时：WebGL 观战中 {externalLiveSourceName}";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
            }

            FrameSourceHub.EnqueueFrame(message, -1, 0, statusText);
            return true;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private Task ConnectOnceAsync()
        {
            if (isConnecting || isConnected)
            {
                return Task.CompletedTask;
            }

            isConnecting = true;
            hasReceivedFirstFrame = false;
            liveEndedCleanly = false;
            ResetReceiveCounters();
            statusText = $"实时：连接中 {serverAddress}";
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);

            try
            {
                ReleaseConnectionResources(waitForShutdown: false);
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
                ReleaseConnectionResources(waitForShutdown: false);
                ScheduleReconnect($"连接失败，{ShortError(ex)}");
            }
            finally
            {
                isConnecting = false;
            }

            return Task.CompletedTask;
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
                        string streamEndReason = hasReceivedFirstFrame
                            ? "服务器消息流已结束"
                            : "服务器消息流结束，未收到首帧";
                        statusText = $"实时：{streamEndReason}";
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
                ReleaseConnectionResources(waitForShutdown: false);
                if (liveRequested && shouldReconnect)
                {
                    ScheduleReconnect(statusText.StartsWith("实时：", StringComparison.Ordinal)
                        ? statusText.Substring("实时：".Length)
                        : "等待服务器");
                }
            }
        }
#endif

#if !UNITY_WEBGL || UNITY_EDITOR
        private void MaybePollCurrentEventStatus()
        {
            if (!isConnected || externalLiveMode || client == null || eventStatusPollInFlight)
            {
                return;
            }

            if (Time.unscaledTime < nextEventStatusPollTime)
            {
                return;
            }

            AvailableService.AvailableServiceClient rpcClient = client;
            long queryTeamId = Math.Max(1, eventStatusTeamId);
            long queryPlayerId = Math.Max(0, eventStatusPlayerId);
            nextEventStatusPollTime = Time.unscaledTime + Mathf.Max(1f, eventStatusPollIntervalSeconds);
            eventStatusPollInFlight = true;

            _ = Task.Run(() =>
            {
                try
                {
                    EventStatusResponse response = rpcClient.GetCurrentEventStatus(
                        new EventStatusRequest { TeamId = queryTeamId, PlayerId = queryPlayerId },
                        deadline: DateTime.UtcNow.AddSeconds(1.5));

                    hasCurrentEventStatus = response != null && response.ActSuccess;
                    currentEventName = response?.EventName ?? string.Empty;
                    currentEventDescription = response?.EventDescription ?? string.Empty;
                }
                catch (Exception ex)
                {
                    hasCurrentEventStatus = false;
                    currentEventName = string.Empty;
                    currentEventDescription = ShortError(ex);
                }
                finally
                {
                    eventStatusPollInFlight = false;
                }
            });
        }
#else
        private void MaybePollCurrentEventStatus()
        {
        }
#endif

        private string BuildLiveSourceName()
        {
            if (externalLiveMode)
            {
                return $"实时：{externalLiveSourceName}";
            }

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
            ResetEventStatus();
        }

        private void ResetEventStatus()
        {
            hasCurrentEventStatus = false;
            currentEventName = string.Empty;
            currentEventDescription = string.Empty;
#if !UNITY_WEBGL || UNITY_EDITOR
            nextEventStatusPollTime = 0f;
            eventStatusPollInFlight = false;
#endif
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

        private void ScheduleReconnect(string reason = null)
        {
            if (!liveRequested || !autoReconnect)
            {
                return;
            }

            float delaySeconds = Mathf.Max(0.5f, reconnectIntervalSeconds);
            nextConnectUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            string reasonText = string.IsNullOrWhiteSpace(reason) ? "等待服务器" : reason.Trim();
            statusText = $"实时：{reasonText}，{delaySeconds:0.#}s 后自动重试";
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Live, BuildLiveSourceName(), statusText);
        }

        private void ReleaseConnectionResources(bool waitForShutdown = false)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
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
                ShutdownChannel(channel, waitForShutdown);
                channel = null;
            }

            cancellation?.Dispose();
            cancellation = null;
#endif
            isConnected = false;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private static void ShutdownChannel(Channel channelToShutdown, bool waitForShutdown)
        {
            if (channelToShutdown == null)
            {
                return;
            }

            if (waitForShutdown)
            {
                try
                {
                    channelToShutdown.ShutdownAsync().Wait(TimeSpan.FromSeconds(1.5));
                }
                catch
                {
                }

                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await channelToShutdown.ShutdownAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            });
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);
#endif

        public static void EnsureNativeGrpcSearchPath()
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
#endif

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
#if !UNITY_WEBGL || UNITY_EDITOR
            if (ex is RpcException rpc)
            {
                return $"{rpc.Status.StatusCode}: {rpc.Status.Detail}";
            }
#endif

            return ex.Message;
        }

        private void OnDestroy()
        {
            StopLive(resetFrameSource: !isApplicationQuitting, waitForShutdown: isApplicationQuitting);
        }

        private void OnApplicationQuit()
        {
            isApplicationQuitting = true;
            StopLive(resetFrameSource: false, waitForShutdown: true);
        }
    }
}
