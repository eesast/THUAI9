using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#if !UNITY_WEBGL || UNITY_EDITOR
using Grpc.Core;
#endif
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Live;
using THUAI9.Unity.Render;
using UnityEngine;

namespace THUAI9.Unity.Player
{
    /// <summary>
    /// Minimal controllable-player client for Unity.
    /// Editor/Standalone uses the same gRPC API as ClientTest2; WebGL dispatches
    /// actions to the browser host instead of linking native gRPC.
    /// </summary>
    public class PlayerControlClient : MonoBehaviour
    {
        public const string ClientObjectName = "PlayerControlClient";

        private const string DefaultServerAddress = "127.0.0.1:8888";
        private const int DefaultMoveDurationMs = 220;
        private const int DefaultAttackRange = 1000;

        private static PlayerControlClient instance;

        [Header("玩家接入")]
        public string serverAddress = DefaultServerAddress;
        [Min(1)] public long teamId = 1;
        public long registerPlayerId = 0;
        public long characterPlayerId = 1;
        [Min(0)] public int sideFlag = 1;
        [Min(1)] public int maxServerCharacterCount = 6;
        public CharacterType characterType = CharacterType.Robot;
        public bool keepSpectatorLiveWhenPlayerMode = true;
        public bool mirrorPlayerStreamToRenderer = false;

        [Header("默认动作")]
        public GoodsType produceGoodsType = GoodsType.Semiconductor;
        [Min(1)] public int maxProduceNum = 1;
        public TechType techType = TechType.IncreaseMoveSpeed;
        [Min(1)] public int recoverHp = 100;

#if !UNITY_WEBGL || UNITY_EDITOR
        private Channel channel;
        private AvailableService.AvailableServiceClient client;
        private AsyncServerStreamingCall<MessageToClient> stream;
        private CancellationTokenSource cancellation;
#endif

        private LiveSpectatorClient liveClient;
        private bool isConnecting;
        private bool isConnected;
        private bool playerMode;
        private int sentActionCount;
        private int successfulActionCount;
        private int failedActionCount;
        private int receivedPlayerStreamFrames;
        private string pendingActionFields = string.Empty;
        private string statusText = "玩家：未接入";
        private string lastActionText = "无";

        public static PlayerControlClient Instance => GetOrCreate();
        public bool IsPlayerMode => playerMode;
        public bool IsConnecting => isConnecting;
        public bool IsConnected => isConnected;
        public string StatusText => statusText;
        public string LastActionText => lastActionText;
        public int SentActionCount => sentActionCount;
        public int SuccessfulActionCount => successfulActionCount;
        public int FailedActionCount => failedActionCount;
        public int ReceivedPlayerStreamFrames => receivedPlayerStreamFrames;
        public string ModeText => playerMode ? "玩家+观战" : "观战/回放";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void THUAI9_DispatchUnityEvent(string eventName, string payload);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        public static PlayerControlClient GetOrCreate()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindObjectOfType<PlayerControlClient>();
            if (instance != null)
            {
                return instance;
            }

            GameObject go = GameObject.Find(ClientObjectName) ?? new GameObject(ClientObjectName);
            instance = go.AddComponent<PlayerControlClient>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            gameObject.name = ClientObjectName;
            DontDestroyOnLoad(gameObject);
            RefreshReferences();
        }

        public void ApplyConnectionSettings(string address, long newTeamId, long newRegisterPlayerId, long newCharacterPlayerId, int newSideFlag)
        {
            if (!string.IsNullOrWhiteSpace(address))
            {
                serverAddress = NormalizeGrpcTarget(address);
            }

            teamId = Math.Max(1, newTeamId);
            registerPlayerId = Math.Max(0, newRegisterPlayerId);
            characterPlayerId = Math.Max(1, newCharacterPlayerId);
            sideFlag = newSideFlag <= 0 ? (int)teamId : newSideFlag;
        }

        public void ConnectPlayer() => StartPlayerMode();

        public void ConnectPlayer(string payload)
        {
            if (!string.IsNullOrWhiteSpace(payload))
            {
                string[] parts = payload.Split('|');
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0])) serverAddress = NormalizeGrpcTarget(parts[0]);
                if (parts.Length > 1 && long.TryParse(parts[1], out long parsedTeam)) teamId = Math.Max(1, parsedTeam);
                if (parts.Length > 2 && long.TryParse(parts[2], out long parsedRegister)) registerPlayerId = Math.Max(1, parsedRegister);
                if (parts.Length > 3 && long.TryParse(parts[3], out long parsedCharacter)) characterPlayerId = Math.Max(1, parsedCharacter);
                if (parts.Length > 4 && int.TryParse(parts[4], out int parsedSide)) sideFlag = parsedSide <= 0 ? (int)teamId : parsedSide;
            }

            StartPlayerMode();
        }

        public void StartSpectatorMode()
        {
            DisconnectPlayer();
            RefreshReferences();
            liveClient?.StartLive(serverAddress);
            statusText = $"模式：观战 {serverAddress}";
        }

        public void StartPlayerMode()
        {
            playerMode = true;
            RefreshReferences();

            if (!ValidateServerPlayerIds())
            {
                playerMode = false;
                return;
            }

            if (keepSpectatorLiveWhenPlayerMode)
            {
                liveClient ??= new GameObject("LiveSpectatorClient").AddComponent<LiveSpectatorClient>();
                if (!liveClient.IsLiveMode)
                {
                    liveClient.StartLive(serverAddress);
                }
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            isConnected = true;
            isConnecting = false;
            statusText = $"玩家：WebGL 玩家模式，等待网页层转发动作 T{teamId}/P{characterPlayerId}";
            DispatchWebPlayerAction("connect", BuildEnvelopeJson("connect", "ConnectPlayer"), true, "web-player-mode");
#else
            _ = ConnectPlayerAsync();
#endif
        }

        public void DisconnectPlayer()
        {
            playerMode = false;
            isConnecting = false;
            isConnected = false;
            statusText = "玩家：已断开";
            ReleaseConnectionResources();
#if UNITY_WEBGL && !UNITY_EDITOR
            DispatchWebPlayerAction("disconnect", BuildEnvelopeJson("disconnect", "DisconnectPlayer"), true, "web-player-disconnect");
#endif
        }

        private bool ValidateServerPlayerIds()
        {
            bool validRegister = registerPlayerId == 0 || (registerPlayerId >= 1 && registerPlayerId <= maxServerCharacterCount);
            if (!validRegister)
            {
                statusText = $"玩家：Register ID 必须是 0 或 1..{maxServerCharacterCount}。当前 {registerPlayerId} 会被 Server 忽略或当作 spectator，无法触发开局。";
                Debug.LogWarning(statusText, this);
                return false;
            }

            if (characterPlayerId < 1 || characterPlayerId > maxServerCharacterCount)
            {
                statusText = $"玩家：Character ID 必须是 1..{maxServerCharacterCount}。当前 {characterPlayerId} 不能创建/控制角色。";
                Debug.LogWarning(statusText, this);
                return false;
            }

            return true;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private Task ConnectPlayerAsync()
        {
            if (isConnecting || isConnected)
            {
                return Task.CompletedTask;
            }

            isConnecting = true;
            statusText = $"玩家：连接中 {serverAddress} T{teamId}/P{characterPlayerId}";

            try
            {
                ReleaseConnectionResources();
                cancellation = new CancellationTokenSource();
                LiveSpectatorClient.EnsureNativeGrpcSearchPath();

                var options = new List<ChannelOption>
                {
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, -1),
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, -1)
                };

                channel = new Channel(serverAddress, ChannelCredentials.Insecure, options);
                client = new AvailableService.AvailableServiceClient(channel);
                stream = client.RegisterFactory(new RegisterFactoryMsg
                {
                    TeamId = teamId,
                    PlayerId = registerPlayerId,
                    SideFlag = sideFlag <= 0 ? (int)teamId : sideFlag
                }, cancellationToken: cancellation.Token);

                isConnected = true;
                statusText = $"玩家：已接入 T{teamId}/P{characterPlayerId}，可创建/控制角色";
                CancellationToken receiveToken = cancellation.Token;
                _ = Task.Run(() => ReadPlayerStreamAsync(receiveToken), receiveToken);
            }
            catch (Exception ex)
            {
                statusText = $"玩家：连接失败，{ShortError(ex)}";
                Debug.LogWarning(statusText, this);
                ReleaseConnectionResources();
            }
            finally
            {
                isConnecting = false;
            }

            return Task.CompletedTask;
        }

        private async Task ReadPlayerStreamAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && stream != null && await stream.ResponseStream.MoveNext(token).ConfigureAwait(false))
                {
                    MessageToClient frame = stream.ResponseStream.Current;
                    receivedPlayerStreamFrames++;
                    if (mirrorPlayerStreamToRenderer && frame != null && FrameSourceHub.ActiveKind != FrameSourceHub.SourceKind.Playback)
                    {
                        FrameSourceHub.EnqueueFrame(frame, -1, 0, statusText);
                    }
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
                if (playerMode)
                {
                    statusText = $"玩家：接收流断开，{ShortError(ex)}";
                    Debug.LogWarning(statusText, this);
                }
            }
            finally
            {
                isConnected = false;
                if (playerMode)
                {
                    statusText = "玩家：连接已断开";
                }
                ReleaseConnectionResources();
            }
        }
#endif

        public void CreateCharacter() => CreateCharacter(characterType.ToString());

        public void CreateCharacter(string typeName)
        {
            CharacterType type = ParseCharacterType(typeName, characterType);
            var request = new CreateCharacterMsg { TeamId = teamId, PlayerId = characterPlayerId, CharacterType = type };
            pendingActionFields = $"\"characterType\":\"{type}\",\"playerId\":{characterPlayerId}";
            SendBoolAction("create-character", "CreateCharacter", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.CreateCharacter(request);
#else
                return null;
#endif
            });
        }

        public void MoveTowardWorld(Vector3 worldPosition, WorldObjectInfo selectedInfo = null)
        {
            long playerId = ResolveControlledPlayerId(selectedInfo);
            if (!TryGetCharacterByPlayerId(playerId, out MessageOfCharacter character))
            {
                SetActionFailed($"移动失败：未在当前帧找到 T{teamId}/P{playerId}，请先创建角色或等待 Live 帧。", false);
                return;
            }

            Vector2 targetGame = Tool.GridToGame(WorldToGrid(worldPosition).x, WorldToGrid(worldPosition).y);
            double angle = Math.Atan2(targetGame.y - character.Y, targetGame.x - character.X);
            MoveAngle((float)angle, DefaultMoveDurationMs, playerId);
        }

        public void MoveAngle(float angleRadians, int durationMs = DefaultMoveDurationMs, long explicitPlayerId = 0)
        {
            long playerId = explicitPlayerId > 0 ? explicitPlayerId : ResolveControlledPlayerId(null);
            var request = new MoveMsg
            {
                TeamId = teamId,
                PlayerId = playerId,
                Angle = angleRadians,
                TimeInMilliseconds = Mathf.Max(40, durationMs)
            };

            pendingActionFields =
                $"\"playerId\":{playerId},\"angle\":{angleRadians.ToString(CultureInfo.InvariantCulture)},\"timeInMilliseconds\":{request.TimeInMilliseconds}";
            SendAction("move", "Move", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                MoveRes res = client.Move(request);
                return res != null && res.ActSuccess
                    ? new ActionResult(true, $"speed={res.ActualSpeed:0.#}")
                    : new ActionResult(false, "ActSuccess=false");
#else
                return ActionResult.WebDispatched;
#endif
            });
        }

        public void Attack(WorldObjectInfo targetInfo)
        {
            if (targetInfo == null || !string.Equals(targetInfo.objectType, "Character", StringComparison.OrdinalIgnoreCase) || targetInfo.playerId <= 0)
            {
                SetActionFailed("攻击失败：请右键敌方单位，或先选中敌方单位后按 A。", false);
                return;
            }

            Attack(targetInfo.playerId, targetInfo.teamId, ResolveAttackRange(ResolveControlledPlayerId(null)));
        }

        public void Attack(long targetPlayerId, long targetTeamId, int attackRange = DefaultAttackRange)
        {
            var request = new AttackMsg
            {
                TeamId = teamId,
                PlayerId = ResolveControlledPlayerId(null),
                AttackRange = Mathf.Max(1, attackRange),
                AttackedPlayerId = targetPlayerId,
                AttackedTeamId = targetTeamId
            };

            pendingActionFields =
                $"\"playerId\":{request.PlayerId},\"attackRange\":{request.AttackRange},\"attackedPlayerId\":{targetPlayerId},\"attackedTeamId\":{targetTeamId}";
            SendBoolAction("attack", "Attack", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.Attack(request);
#else
                return null;
#endif
            });
        }

        public void Harvest(WorldObjectInfo resourceInfo = null, int amount = 0)
        {
            Vector2Int grid = resourceInfo != null && resourceInfo.gridX >= 0
                ? new Vector2Int(resourceInfo.gridX, resourceInfo.gridY)
                : FindNearestGrid(PlaceType.Resource, ResolveControlledPlayerId(null));
            Vector2 target = Tool.GridToGame(grid.x, grid.y);
            var request = new ResourceMsg
            {
                TeamId = teamId,
                PlayerId = ResolveControlledPlayerId(null),
                ResourceId = resourceInfo != null && string.Equals(resourceInfo.objectType, "Resource", StringComparison.OrdinalIgnoreCase) ? resourceInfo.guid : 0,
                TargetX = Mathf.RoundToInt(target.x),
                TargetY = Mathf.RoundToInt(target.y),
                Amount = Mathf.Max(0, amount)
            };

            pendingActionFields =
                $"\"playerId\":{request.PlayerId},\"resourceId\":{request.ResourceId},\"targetX\":{request.TargetX},\"targetY\":{request.TargetY},\"amount\":{request.Amount}";
            SendBoolAction("harvest", "Harvest", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.Harvest(request);
#else
                return null;
#endif
            });
        }

        public void Occupy(WorldObjectInfo computeCenterInfo = null)
        {
            Vector2Int grid = computeCenterInfo != null && computeCenterInfo.gridX >= 0
                ? new Vector2Int(computeCenterInfo.gridX, computeCenterInfo.gridY)
                : FindNearestGrid(PlaceType.ComputeCenter, ResolveControlledPlayerId(null));
            Vector2 target = Tool.GridToGame(grid.x, grid.y);
            var request = new OccupyMsg
            {
                TeamId = teamId,
                PlayerId = ResolveControlledPlayerId(null),
                TargetX = Mathf.RoundToInt(target.x),
                TargetY = Mathf.RoundToInt(target.y),
                TargetComputeCenterId = computeCenterInfo != null ? computeCenterInfo.guid : 0
            };

            pendingActionFields =
                $"\"playerId\":{request.PlayerId},\"targetX\":{request.TargetX},\"targetY\":{request.TargetY},\"targetComputeCenterId\":{request.TargetComputeCenterId}";
            SendBoolAction("occupy", "Occupy", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.Occupy(request);
#else
                return null;
#endif
            });
        }

        public void Recover()
        {
            var request = new RecoverMsg { TeamId = teamId, PlayerId = ResolveControlledPlayerId(null), RecoveredHp = Mathf.Max(1, recoverHp) };
            pendingActionFields = $"\"playerId\":{request.PlayerId},\"recoveredHp\":{request.RecoveredHp}";
            SendBoolAction("recover", "Recover", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.Recover(request);
#else
                return null;
#endif
            });
        }

        public void ProduceDefaultGoods() => Produce(produceGoodsType, maxProduceNum);

        public void Produce(GoodsType goodsType, int count)
        {
            var request = new ProduceGoodsMsg { TeamId = teamId, ProductType = goodsType, MaxProduceNum = Mathf.Max(1, count) };
            pendingActionFields = $"\"productType\":\"{goodsType}\",\"maxProduceNum\":{request.MaxProduceNum}";
            SendBoolAction("produce", "Produce", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.Produce(request);
#else
                return null;
#endif
            });
        }

        public void UplevelDefaultTech() => UplevelTech(techType);

        public void UplevelTech(TechType targetTechType)
        {
            var request = new UplevelTechMsg { TeamId = teamId, TechType = targetTechType };
            pendingActionFields = $"\"techType\":\"{targetTechType}\"";
            SendBoolAction("uplevel-tech", "UplevelTech", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.UplevelTech(request);
#else
                return null;
#endif
            });
        }

        public void EndAllAction()
        {
            var request = new IDMsg { TeamId = teamId, PlayerId = ResolveControlledPlayerId(null) };
            pendingActionFields = $"\"playerId\":{request.PlayerId}";
            SendBoolAction("end-all-action", "EndAllAction", () =>
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                return client.EndAllAction(request);
#else
                return null;
#endif
            });
        }

        private void SendBoolAction(string actionName, string requestType, Func<BoolRes> sendFunc)
        {
            SendAction(actionName, requestType, () =>
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return ActionResult.WebDispatched;
#else
                BoolRes res = sendFunc.Invoke();
                return res == null
                    ? new ActionResult(false, "null BoolRes")
                    : new ActionResult(res.ActSuccess, res.ActSuccess ? "" : "ActSuccess=false");
#endif
            });
        }

        private void SendAction(string actionName, string requestType, Func<ActionResult> sendFunc)
        {
            if (!playerMode)
            {
                SetActionFailed($"{actionName} 未发送：当前是观战模式。", false);
                return;
            }

#if !UNITY_WEBGL || UNITY_EDITOR
            if (client == null || !isConnected)
            {
                SetActionFailed($"{actionName} 未发送：玩家尚未连接 Server。", false);
                return;
            }
#endif

            sentActionCount++;
            string envelope = BuildEnvelopeJson(actionName, requestType);

#if UNITY_WEBGL && !UNITY_EDITOR
            DispatchWebPlayerAction(actionName, envelope, true, "web-dispatched");
            successfulActionCount++;
            lastActionText = $"{actionName} -> WebGL bridge";
            statusText = $"玩家：WebGL 已发送 {actionName} 到网页层";
            return;
#else
            try
            {
                ActionResult result = sendFunc.Invoke();
                if (result.Success)
                {
                    successfulActionCount++;
                    lastActionText = $"{actionName} OK {result.Detail}".Trim();
                    statusText = $"玩家：动作成功 {lastActionText}";
                }
                else
                {
                    failedActionCount++;
                    lastActionText = $"{actionName} FAIL {result.Detail}".Trim();
                    statusText = $"玩家：动作失败 {lastActionText}";
                    Debug.LogWarning(statusText, this);
                }
            }
            catch (Exception ex)
            {
                failedActionCount++;
                lastActionText = $"{actionName} EX {ShortError(ex)}";
                statusText = $"玩家：动作异常 {lastActionText}";
                Debug.LogWarning(statusText, this);
            }
#endif
        }

        private void SetActionFailed(string message, bool countAsFailure)
        {
            if (countAsFailure)
            {
                failedActionCount++;
            }

            lastActionText = message;
            statusText = $"玩家：{message}";
            Debug.Log(statusText, this);
        }

        private long ResolveControlledPlayerId(WorldObjectInfo selectedInfo)
        {
            if (selectedInfo != null
                && string.Equals(selectedInfo.objectType, "Character", StringComparison.OrdinalIgnoreCase)
                && selectedInfo.teamId == teamId
                && selectedInfo.playerId > 0)
            {
                return selectedInfo.playerId;
            }

            return characterPlayerId;
        }

        private int ResolveAttackRange(long playerId)
        {
            foreach (MessageOfCharacter character in CoreParam.characters.Values)
            {
                if (character.TeamId == teamId && character.PlayerId == playerId)
                {
                    return character.CommonAttackRange > 0 ? character.CommonAttackRange : DefaultAttackRange;
                }
            }

            return DefaultAttackRange;
        }

        private bool TryGetCharacterByPlayerId(long playerId, out MessageOfCharacter character)
        {
            foreach (MessageOfCharacter current in CoreParam.characters.Values)
            {
                if (current.TeamId == teamId && current.PlayerId == playerId)
                {
                    character = current;
                    return true;
                }
            }

            character = null;
            return false;
        }

        private Vector2Int FindNearestGrid(PlaceType placeType, long playerId)
        {
            if (!TryGetCharacterByPlayerId(playerId, out MessageOfCharacter character))
            {
                return Vector2Int.zero;
            }

            Vector2Int origin = Tool.GameToGrid(character.X, character.Y);
            if (CoreParam.map == null)
            {
                return origin;
            }

            int bestDistance = int.MaxValue;
            Vector2Int best = origin;
            for (int r = 0; r < CoreParam.map.Rows.Count; r++)
            {
                for (int c = 0; c < CoreParam.map.Rows[r].Cols.Count; c++)
                {
                    if (CoreParam.map.Rows[r].Cols[c] != placeType)
                    {
                        continue;
                    }

                    int distance = Mathf.Abs(origin.x - r) + Mathf.Abs(origin.y - c);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new Vector2Int(r, c);
                    }
                }
            }

            return best;
        }

        private static Vector2Int WorldToGrid(Vector3 worldPosition)
        {
            int row = Mathf.Clamp(Mathf.FloorToInt(Tool.GetMapRows() - worldPosition.y), 0, Mathf.Max(Tool.GetMapRows() - 1, 0));
            int col = Mathf.Clamp(Mathf.FloorToInt(worldPosition.x), 0, Mathf.Max(Tool.GetMapCols() - 1, 0));
            return new Vector2Int(row, col);
        }

        private void RefreshReferences()
        {
            liveClient ??= FindObjectOfType<LiveSpectatorClient>();
        }

        private static CharacterType ParseCharacterType(string value, CharacterType fallback)
        {
            if (Enum.TryParse(value, true, out CharacterType parsed) && parsed != CharacterType.NullCharacterType)
            {
                return parsed;
            }

            return fallback == CharacterType.NullCharacterType ? CharacterType.Robot : fallback;
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

        private void ReleaseConnectionResources()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            try { cancellation?.Cancel(); } catch { }
            try { stream?.Dispose(); } catch { }
            stream = null;
            client = null;

            if (channel != null)
            {
                ShutdownChannelInBackground(channel);
                channel = null;
            }

            cancellation?.Dispose();
            cancellation = null;
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
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
#endif

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

        private string BuildEnvelopeJson(string actionName, string requestType)
        {
            string extraFields = string.IsNullOrWhiteSpace(pendingActionFields) ? string.Empty : "," + pendingActionFields.Trim().TrimStart(',');
            pendingActionFields = string.Empty;
            return "{" +
                   $"\"action\":\"{EscapeJson(actionName)}\"," +
                   $"\"requestType\":\"{EscapeJson(requestType)}\"," +
                   $"\"serverAddress\":\"{EscapeJson(serverAddress)}\"," +
                   $"\"teamId\":{teamId}," +
                   $"\"registerPlayerId\":{registerPlayerId}," +
                   $"\"characterPlayerId\":{characterPlayerId}," +
                   $"\"timestampMs\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}" +
                   extraFields +
                   "}";
        }

        private static void DispatchWebPlayerAction(string actionName, string payload, bool success, string detail)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            string json = string.IsNullOrWhiteSpace(payload)
                ? $"{{\"action\":\"{EscapeJson(actionName)}\",\"success\":{success.ToString().ToLowerInvariant()},\"detail\":\"{EscapeJson(detail)}\"}}"
                : payload;

            THUAI9_DispatchUnityEvent("player-action", json);
#else
            _ = actionName;
            _ = payload;
            _ = success;
            _ = detail;
#endif
        }

        private static string EscapeJson(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            DisconnectPlayer();
        }

        private readonly struct ActionResult
        {
            public static readonly ActionResult WebDispatched = new ActionResult(true, "web-dispatched");

            public ActionResult(bool success, string detail)
            {
                Success = success;
                Detail = detail ?? string.Empty;
            }

            public bool Success { get; }
            public string Detail { get; }
        }
    }
}
