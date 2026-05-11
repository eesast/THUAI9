using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Protobuf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using THUAI9_Avalonia.Models;

namespace THUAI9_Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private const string DefaultServerAddress = "127.0.0.1:8888";
        private const long SpectatorTeamId = 0;
        private const int SpectatorSideFlag = 0;
        private static readonly TimeSpan AutoReconnectInterval = TimeSpan.FromSeconds(2);

        [ObservableProperty]
        private ObservableCollection<int> teamScores = new() { 0, 0, 0, 0 };

        [ObservableProperty]
        private string connectionStatus = "未连接";

        [ObservableProperty]
        private MapViewModel mapVM = new();

        [ObservableProperty]
        private LogConsoleViewModel logConsoleVM = new();

        [ObservableProperty]
        private PlaybackViewModel playbackVM = new();

        [ObservableProperty]
        private string buildingSummary = "工厂：等待数据";

        [ObservableProperty]
        private string resourceSummary = "资源点：等待数据";

        [ObservableProperty]
        private string objectiveSummary = "算力中心：等待数据";

        [ObservableProperty]
        private string marketSummary = "市场：等待数据";

        public ObservableCollection<CharacterViewModel> Team1Characters { get; } = new();
        public ObservableCollection<CharacterViewModel> Team2Characters { get; } = new();
        public ObservableCollection<CharacterViewModel> Team3Characters { get; } = new();
        public ObservableCollection<CharacterViewModel> Team4Characters { get; } = new();
        public ObservableCollection<LegendItem> MapLegendItems { get; } = new();
        public ObservableCollection<TeamOverviewItem> TeamOverviews { get; } = new();

        private readonly Dictionary<long, CharacterViewModel> _team1CharacterIndex = new();
        private readonly Dictionary<long, CharacterViewModel> _team2CharacterIndex = new();
        private readonly Dictionary<long, CharacterViewModel> _team3CharacterIndex = new();
        private readonly Dictionary<long, CharacterViewModel> _team4CharacterIndex = new();
        private readonly struct TeamMemberUuidInfo
        {
            public TeamMemberUuidInfo(long playerId, long guid)
            {
                PlayerId = playerId;
                Guid = guid;
            }

            public long PlayerId { get; }
            public long Guid { get; }
        }

        private readonly MapDynamicStateManager _dynamicStateManager;
        private readonly long _spectatorPlayerId = 2023 + Environment.ProcessId;

        private Channel? _channel;
        private AvailableService.AvailableServiceClient? _client;
        private AsyncServerStreamingCall<MessageToClient>? _stream;
        private bool _isConnected;
        private bool _isConnecting;
        private bool _hasReceivedFirstFrame;
        private bool _hasLoggedRetrying;
        private bool _playbackModeActive;
        private readonly object _drawPicLock = new();
        private readonly CancellationTokenSource _autoConnectCts = new();
        private Task? _autoConnectTask;
        private Views.MapView? _mapView;

        public MainWindowViewModel()
        {
            InitializeTeamOverviews();
            InitializeMapLegend();
            _dynamicStateManager = new MapDynamicStateManager(MapVM);
            PlaybackVM.SetMessageCallback(ProcessPlaybackMessage);

            if (Avalonia.Controls.Design.IsDesignMode)
            {
                InitializeDesignTimeData();
                return;
            }

            LogConsoleVM.AddLog("THUAI9 调试界面已启动", "INFO");
            LogConsoleVM.AddLog($"已启用自动连接，将持续尝试连接 {DefaultServerAddress}", "INFO");
            StartAutoConnectLoop();
        }

        private void InitializeTeamOverviews()
        {
            TeamOverviews.Clear();
            for (int teamId = 1; teamId <= 4; teamId++)
            {
                TeamOverviews.Add(new TeamOverviewItem
                {
                    TeamId = teamId,
                    Score = 0,
                    Material = 0,
                    ComputePower = 0,
                    FactoryHp = 0,
                    MemberUuidText = BuildWaitingMemberUuidText()
                });
            }
        }

        private void StartAutoConnectLoop()
        {
            if (_autoConnectTask == null || _autoConnectTask.IsCompleted)
            {
                _autoConnectTask = AutoConnectLoopAsync(_autoConnectCts.Token);
            }
        }

        private async Task AutoConnectLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (_playbackModeActive || _isConnected || _isConnecting)
                    {
                        await Task.Delay(500, cancellationToken);
                        continue;
                    }

                    bool connected = await TryConnectOnceAsync(DefaultServerAddress, cancellationToken);
                    if (!connected)
                    {
                        if (!_hasLoggedRetrying)
                        {
                            LogConsoleVM.AddLog($"尚未连接到 {DefaultServerAddress}，将继续自动重试", "INFO");
                            _hasLoggedRetrying = true;
                        }

                        ConnectionStatus = "等待服务器";
                        await Task.Delay(AutoReconnectInterval, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void InitializeDesignTimeData()
        {
            ConnectionStatus = "设计模式";
            BuildingSummary = "工厂：队伍1 1 座（总血量 3） | 队伍2 1 座（总血量 3） | 队伍3 0 座 | 队伍4 0 座";
            ResourceSummary = "资源点：3 处 · 剩余 210/300 · 可采集 2 处";
            ObjectiveSummary = "算力中心：队伍1 1 座 | 队伍2 0 座 | 队伍3 0 座 | 队伍4 0 座 | 中立 1 座 | 正在争夺 1 座";
            MarketSummary = "市场：2 处 · 当前价目 10 条";

            Team1Characters.Add(new CharacterViewModel
            {
                Guid = 1,
                CharacterId = 1,
                Name = "无人机 1 号",
                Hp = 2,
                MaxHp = 3,
                PosX = 10000,
                PosY = 10000,
                TeamId = 1,
                ActiveState = "移动中"
            });

            if (TeamOverviews.Count >= 4)
            {
                TeamOverviews[0].Score = 12;
                TeamOverviews[0].Material = 80;
                TeamOverviews[0].ComputePower = 45;
                TeamOverviews[0].FactoryHp = 3;
                TeamOverviews[0].MemberUuidText = "成员 uuid：P1=uuid 1";
                TeamOverviews[1].Score = 9;
                TeamOverviews[1].Material = 65;
                TeamOverviews[1].ComputePower = 30;
                TeamOverviews[1].FactoryHp = 2;
            }
        }

        private void InitializeMapLegend()
        {
            MapLegendItems.Clear();
            MapLegendItems.Add(new LegendItem(Brushes.Cyan, "工厂出生点"));
            MapLegendItems.Add(new LegendItem(Brushes.White, "空地", Brushes.LightGray, new Thickness(1)));
            MapLegendItems.Add(new LegendItem(Brushes.LightGreen, "草丛"));
            MapLegendItems.Add(new LegendItem(Brushes.DarkGray, "障碍物"));
            MapLegendItems.Add(new LegendItem(Brushes.Gold, "资源点底图"));
            MapLegendItems.Add(new LegendItem(Brushes.LightBlue, "算力中心底图"));
            MapLegendItems.Add(new LegendItem(Brushes.LightYellow, "市场底图"));
            MapLegendItems.Add(new LegendItem(Brushes.Red, "队伍 1 覆盖物"));
            MapLegendItems.Add(new LegendItem(Brushes.Blue, "队伍 2 覆盖物"));
            MapLegendItems.Add(new LegendItem(Brushes.Green, "队伍 3 覆盖物"));
            MapLegendItems.Add(new LegendItem(Brushes.Orange, "队伍 4 覆盖物"));
        }

        private async Task<bool> TryConnectOnceAsync(string serverAddress, CancellationToken cancellationToken)
        {
            _isConnecting = true;
            try
            {
                ReleaseConnectionResources();
                _isConnected = false;
                _hasReceivedFirstFrame = false;

                string channelTarget = NormalizeGrpcTarget(serverAddress);
                ConnectionStatus = "连接中...";

                var channelOptions = new List<ChannelOption>
                {
                    new(ChannelOptions.MaxSendMessageLength, -1),
                    new(ChannelOptions.MaxReceiveMessageLength, -1)
                };

                _channel = new Channel(channelTarget, ChannelCredentials.Insecure, channelOptions);
                await _channel.ConnectAsync(deadline: DateTime.UtcNow.AddSeconds(10));
                _client = new AvailableService.AvailableServiceClient(_channel);

                _hasLoggedRetrying = false;
                ConnectionStatus = "已连接";
                LogConsoleVM.AddLog($"已连接到 {channelTarget}", "SUCCESS");

                ResetMatchVisualizationState(resetBaseMap: true);

                try
                {
                    var mapMessage = await _client.GetMapAsync(new NullRequest()).ResponseAsync;
                    MapVM.UpdateMap(mapMessage);
                    _mapView?.RefreshMap();
                    LogConsoleVM.AddLog("已成功拉取静态地图", "INFO");
                }
                catch (Exception ex)
                {
                    LogConsoleVM.AddLog($"连接成功，但拉取静态地图失败：{ex.Message}", "WARNING");
                }

                _isConnected = true;
                bool streamStarted = await StartSpectatorStreamAsync(cancellationToken);
                if (!streamStarted)
                {
                    ReleaseConnectionResources();
                    _isConnected = false;
                    ConnectionStatus = "等待服务器";
                    return false;
                }

                ConnectionStatus = "等待首帧";
                LogConsoleVM.AddLog("当前以 spectator 身份接入实时流，不占用任何队伍。", "INFO");
                return true;
            }
            catch
            {
                ReleaseConnectionResources();
                _isConnected = false;
                return false;
            }
            finally
            {
                _isConnecting = false;
            }
        }

        private void ReleaseConnectionResources()
        {
            _stream?.Dispose();
            _stream = null;
            _client = null;

            if (_channel != null)
            {
                _channel.ShutdownAsync().GetAwaiter().GetResult();
                _channel = null;
            }
        }

        private async Task<bool> StartSpectatorStreamAsync(CancellationToken cancellationToken)
        {
            if (_client == null)
            {
                return false;
            }

            try
            {
                _stream?.Dispose();
                _stream = null;
                _hasReceivedFirstFrame = false;

                var request = new RegisterFactoryMsg
                {
                    TeamId = SpectatorTeamId,
                    PlayerId = _spectatorPlayerId,
                    SideFlag = SpectatorSideFlag
                };

                _stream = _client.RegisterFactory(request, cancellationToken: cancellationToken);
                LogConsoleVM.AddLog($"已发起实时观战流注册：SpectatorId={_spectatorPlayerId}", "INFO");
                _ = ReceiveMessagesAsync();
                await Task.Yield();
                return true;
            }
            catch (RpcException ex)
            {
                LogConsoleVM.AddLog($"实时观战流注册失败：{ex.Status.StatusCode} - {ex.Status.Detail}", "ERROR");
                _stream?.Dispose();
                _stream = null;
                return false;
            }
            catch (Exception ex)
            {
                LogConsoleVM.AddLog($"实时观战流注册失败：{ex.Message}", "ERROR");
                _stream?.Dispose();
                _stream = null;
                return false;
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            try
            {
                while (_isConnected && _stream != null)
                {
                    bool hasMessage = await _stream.ResponseStream.MoveNext(default);
                    if (!hasMessage)
                    {
                        string endMessage = _hasReceivedFirstFrame
                            ? "服务端消息流已结束，后续不会再收到实时帧"
                            : "服务端消息流已结束，未收到任何首帧游戏消息";
                        LogConsoleVM.AddLog(endMessage, "WARNING");
                        _isConnected = false;
                        ReleaseConnectionResources();
                        ConnectionStatus = "等待服务器";
                        break;
                    }

                    var message = _stream.ResponseStream.Current;
                    await Dispatcher.UIThread.InvokeAsync(() => ProcessMessage(message));
                }
            }
            catch (Exception ex)
            {
                if (_isConnected)
                {
                    LogConsoleVM.AddLog($"接收消息失败：{ex.Message}", "ERROR");
                    _isConnected = false;
                    ReleaseConnectionResources();
                    ConnectionStatus = "等待服务器";
                }
            }
        }

        private void ProcessMessage(MessageToClient message)
        {
            if (_playbackModeActive)
            {
                return;
            }

            lock (_drawPicLock)
            {
                if (!_hasReceivedFirstFrame)
                {
                    _hasReceivedFirstFrame = true;
                    ConnectionStatus = "实时观战中";
                    LogConsoleVM.AddLog("已收到首帧实时游戏消息", "SUCCESS");
                }

                UpdateCharacters(message);
                UpdateGameStatus(message);
                UpdateMapElements(message);
            }
        }

        private void UpdateCharacters(MessageToClient message)
        {
            var currentFrameGuids = new HashSet<long>();

            foreach (var obj in message.ObjMessage)
            {
                if (obj.CharacterMessage == null)
                {
                    continue;
                }

                var data = obj.CharacterMessage;
                currentFrameGuids.Add(data.Guid);

                var targetList = GetTeamList(data.TeamId);
                if (targetList == null)
                {
                    continue;
                }

                var targetIndex = GetTeamCharacterIndex(data.TeamId);
                if (targetIndex == null)
                {
                    continue;
                }

                targetIndex.TryGetValue(data.Guid, out var existingCharacter);
                if (existingCharacter == null)
                {
                    var newCharacter = new CharacterViewModel
                    {
                        Guid = data.Guid,
                        TeamId = (int)data.TeamId,
                        CharacterId = data.PlayerId,
                        CharacterType = data.CharacterType,
                        Name = GetCharacterName(data.CharacterType),
                        MaxHp = GetCharacterMaxHp(data.CharacterType),
                        Hp = data.Hp,
                        PosX = data.X,
                        PosY = data.Y,
                        ActiveState = GetCharacterStateName(data.CharacterActiveState, keepMoving: false)
                    };

                    InsertCharacterSorted(targetList, newCharacter);
                    targetIndex[data.Guid] = newCharacter;
                    UpdateCharacterOnMap(data, newCharacter.MaxHp);
                    continue;
                }

                bool movedThisFrame = existingCharacter.PosX != data.X
                    || existingCharacter.PosY != data.Y
                    ;
                string activeState = GetCharacterStateName(
                    data.CharacterActiveState,
                    keepMoving: existingCharacter.ActiveState == "移动中" && movedThisFrame);
                bool visualChanged = movedThisFrame
                    || existingCharacter.Hp != data.Hp
                    || existingCharacter.ActiveState != activeState
                    || existingCharacter.TeamId != data.TeamId;

                existingCharacter.TeamId = (int)data.TeamId;
                existingCharacter.Hp = data.Hp;
                existingCharacter.PosX = data.X;
                existingCharacter.PosY = data.Y;
                existingCharacter.ActiveState = activeState;

                if (visualChanged)
                {
                    UpdateCharacterOnMap(data, existingCharacter.MaxHp);
                }
            }

            RemoveUnseenCharacters(Team1Characters, _team1CharacterIndex, currentFrameGuids);
            RemoveUnseenCharacters(Team2Characters, _team2CharacterIndex, currentFrameGuids);
            RemoveUnseenCharacters(Team3Characters, _team3CharacterIndex, currentFrameGuids);
            RemoveUnseenCharacters(Team4Characters, _team4CharacterIndex, currentFrameGuids);

            UpdateTeamUuidSummaries(message);
        }

        private void UpdateCharacterOnMap(MessageOfCharacter data, int maxHp)
        {
            _mapView?.UpdateCharacterOnMap(
                data.Guid,
                data.X,
                data.Y,
                (int)data.TeamId,
                data.Hp,
                maxHp,
                data.PlayerId,
                data.CharacterType);
        }

        private static void InsertCharacterSorted(ObservableCollection<CharacterViewModel> targetList, CharacterViewModel character)
        {
            int insertIndex = 0;
            while (insertIndex < targetList.Count && targetList[insertIndex].CharacterId <= character.CharacterId)
            {
                insertIndex++;
            }

            targetList.Insert(insertIndex, character);
        }

        private ObservableCollection<CharacterViewModel>? GetTeamList(long teamId)
        {
            return teamId switch
            {
                1 => Team1Characters,
                2 => Team2Characters,
                3 => Team3Characters,
                4 => Team4Characters,
                _ => null
            };
        }

        private Dictionary<long, CharacterViewModel>? GetTeamCharacterIndex(long teamId)
        {
            return teamId switch
            {
                1 => _team1CharacterIndex,
                2 => _team2CharacterIndex,
                3 => _team3CharacterIndex,
                4 => _team4CharacterIndex,
                _ => null
            };
        }

        private void RemoveUnseenCharacters(ObservableCollection<CharacterViewModel> characterList, Dictionary<long, CharacterViewModel> characterIndex, HashSet<long> seenGuids)
        {
            var charactersToRemove = characterList.Where(c => !seenGuids.Contains(c.Guid)).ToList();
            foreach (var character in charactersToRemove)
            {
                characterList.Remove(character);
                characterIndex.Remove(character.Guid);
                _mapView?.RemoveCharacterFromMap(character.Guid);
            }
        }

        private void ClearCharacterState()
        {
            Team1Characters.Clear();
            Team2Characters.Clear();
            Team3Characters.Clear();
            Team4Characters.Clear();

            _team1CharacterIndex.Clear();
            _team2CharacterIndex.Clear();
            _team3CharacterIndex.Clear();
            _team4CharacterIndex.Clear();

            _mapView?.ClearAllCharacters();
        }

        public void SetMapView(Views.MapView mapView)
        {
            _mapView = mapView;
        }

        private void UpdateGameStatus(MessageToClient message)
        {
            if (message.AllMessage == null)
            {
                return;
            }

            var all = message.AllMessage;
            for (int i = 0; i < 4; i++)
            {
                int score = 0;
                int material = 0;
                int computePower = 0;
                int factoryHp = 0;

                if (i < all.Teams.Count)
                {
                    score = all.Teams[i].Score;
                    material = all.Teams[i].Material;
                    computePower = all.Teams[i].ComputePower;
                    factoryHp = all.Teams[i].FactoryHp;
                }

                if (i < TeamScores.Count)
                {
                    TeamScores[i] = score;
                }

                if (i < TeamOverviews.Count)
                {
                    TeamOverviews[i].Score = score;
                    TeamOverviews[i].Material = material;
                    TeamOverviews[i].ComputePower = computePower;
                    TeamOverviews[i].FactoryHp = factoryHp;
                }
            }
        }

        private void UpdateMapElements(MessageToClient message)
        {
            bool mapUpdated = false;

            foreach (var obj in message.ObjMessage)
            {
                if (obj.MapMessage != null)
                {
                    MapVM.UpdateMap(obj.MapMessage);
                    mapUpdated = true;
                }
            }

            _dynamicStateManager.ApplyFrame(message.ObjMessage, LogSemanticEvent);
            ApplyDynamicSummary();

            if (mapUpdated)
            {
                _mapView?.RefreshMap();
            }
        }

        private void ApplyDynamicSummary()
        {
            BuildingSummary = _dynamicStateManager.Summary.BuildingSummary;
            ResourceSummary = _dynamicStateManager.Summary.ResourceSummary;
            ObjectiveSummary = _dynamicStateManager.Summary.ObjectiveSummary;
            MarketSummary = _dynamicStateManager.Summary.MarketSummary;
        }

        private void ResetMatchVisualizationState(bool resetBaseMap)
        {
            ClearCharacterState();
            _dynamicStateManager.Reset(resetBaseMap);
            ResetSummaryState();
            ResetTeamOverviewState();
            ResetScores();
            if (resetBaseMap)
            {
                _mapView?.RefreshMap();
            }
        }

        private void ResetSummaryState()
        {
            BuildingSummary = "工厂：等待数据";
            ResourceSummary = "资源点：等待数据";
            ObjectiveSummary = "算力中心：等待数据";
            MarketSummary = "市场：等待数据";
        }

        private void ResetScores()
        {
            if (TeamScores.Count != 4)
            {
                TeamScores = new ObservableCollection<int> { 0, 0, 0, 0 };
                return;
            }

            for (int i = 0; i < TeamScores.Count; i++)
            {
                TeamScores[i] = 0;
            }
        }

        private void ResetTeamOverviewState()
        {
            if (TeamOverviews.Count != 4)
            {
                InitializeTeamOverviews();
                return;
            }

            for (int i = 0; i < TeamOverviews.Count; i++)
            {
                TeamOverviews[i].TeamId = i + 1;
                TeamOverviews[i].Score = 0;
                TeamOverviews[i].Material = 0;
                TeamOverviews[i].ComputePower = 0;
                TeamOverviews[i].FactoryHp = 0;
                TeamOverviews[i].MemberUuidText = BuildWaitingMemberUuidText();
            }
        }

        private void UpdateTeamUuidSummaries(MessageToClient message)
        {
            for (int teamId = 1; teamId <= 4 && teamId <= TeamOverviews.Count; teamId++)
            {
                TeamOverviews[teamId - 1].MemberUuidText = FormatTeamUuidSummary(teamId, message);
            }
        }

        private string FormatTeamUuidSummary(int teamId, MessageToClient message)
        {
            var members = new List<TeamMemberUuidInfo>();

            ObservableCollection<CharacterViewModel>? characterList = GetTeamList(teamId);
            if (characterList != null)
            {
                foreach (CharacterViewModel character in characterList)
                {
                    AddOrMergeTeamMemberUuid(members, character.CharacterId, character.Guid);
                }
            }

            if (message.ObjMessage != null)
            {
                foreach (MessageOfObj obj in message.ObjMessage)
                {
                    MessageOfTeam? team = obj.TeamMessage;
                    if (team == null || team.TeamId != teamId)
                    {
                        continue;
                    }

                    AddOrMergeTeamMemberUuid(members, team.PlayerId, 0);
                }
            }

            if (members.Count == 0)
            {
                return BuildWaitingMemberUuidText();
            }

            members.Sort((left, right) =>
            {
                int playerCompare = NormalizePlayerIdForSort(left.PlayerId).CompareTo(NormalizePlayerIdForSort(right.PlayerId));
                return playerCompare != 0 ? playerCompare : left.Guid.CompareTo(right.Guid);
            });

            IEnumerable<string> labels = members.Select(member =>
            {
                string playerLabel = member.PlayerId > 0 ? $"P{member.PlayerId}" : "P?";
                string guidLabel = member.Guid > 0 ? member.Guid.ToString() : "暂无";
                return $"{playerLabel}=uuid {guidLabel}";
            });

            return $"成员 uuid：{string.Join("；", labels)}";
        }

        private static void AddOrMergeTeamMemberUuid(List<TeamMemberUuidInfo> members, long playerId, long guid)
        {
            if (playerId <= 0 && guid <= 0)
            {
                return;
            }

            for (int i = 0; i < members.Count; i++)
            {
                TeamMemberUuidInfo existing = members[i];
                bool samePlayer = playerId > 0 && existing.PlayerId == playerId;
                bool sameGuidWithoutPlayer = playerId <= 0 && guid > 0 && existing.Guid == guid;
                if (!samePlayer && !sameGuidWithoutPlayer)
                {
                    continue;
                }

                long mergedPlayerId = existing.PlayerId > 0 ? existing.PlayerId : playerId;
                long mergedGuid = existing.Guid > 0 ? existing.Guid : guid;
                members[i] = new TeamMemberUuidInfo(mergedPlayerId, mergedGuid);
                return;
            }

            members.Add(new TeamMemberUuidInfo(playerId, guid));
        }

        private static long NormalizePlayerIdForSort(long playerId)
        {
            return playerId > 0 ? playerId : long.MaxValue;
        }

        private static string BuildWaitingMemberUuidText()
        {
            return "成员 uuid：等待角色创建";
        }

        private void LogSemanticEvent(string message, string level)
        {
            LogConsoleVM.AddLog(message, level);
        }

        private string GetCharacterName(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => "无人机",
                CharacterType.Robot => "机器人",
                CharacterType.AutonomousCar => "无人车",
                _ => "未知"
            };
        }

        private static string GetCharacterStateName(CharacterState state, bool keepMoving)
        {
            return state switch
            {
                CharacterState.None => keepMoving ? "移动中" : "空闲",
                CharacterState.Idle => "空闲",
                CharacterState.Harvesting => "采集中",
                CharacterState.Attacking => "攻击中",
                CharacterState.Ocuppying => "占领中",
                CharacterState.Trading => "交易中",
                CharacterState.Moving => "移动中",
                CharacterState.KnockedBack => "被击退",
                CharacterState.Deceased => "已死亡",
                _ => "未知"
            };
        }

        private int GetCharacterMaxHp(CharacterType type)
        {
            // 须与 logic/Preparation/Utility/GameData.cs 中的 *HP 常量保持一致
            return type switch
            {
                CharacterType.Drone => 100,
                CharacterType.Robot => 150,
                CharacterType.AutonomousCar => 100,
                _ => 1
            };
        }

        private static string NormalizeGrpcTarget(string serverAddress)
        {
            if (string.IsNullOrWhiteSpace(serverAddress))
            {
                return DefaultServerAddress;
            }

            string trimmed = serverAddress.Trim();
            if (!trimmed.Contains("://"))
            {
                return trimmed;
            }

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return trimmed;
            }

            int port = uri.IsDefaultPort ? 8888 : uri.Port;
            return $"{uri.Host}:{port}";
        }

        [RelayCommand]
        private async Task LoadPlaybackAsync(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    LogConsoleVM.AddLog("请输入回放文件路径", "WARNING");
                    return;
                }

                _playbackModeActive = true;
                _isConnected = false;
                _isConnecting = false;
                ReleaseConnectionResources();
                ResetMatchVisualizationState(resetBaseMap: true);

                LogConsoleVM.AddLog($"正在加载回放：{filePath}", "INFO");
                PlaybackVM.Stop();
                PlaybackVM.LoadPlayback(filePath);
                ConnectionStatus = "回放模式";
                LogConsoleVM.AddLog("已进入回放模式，实时自动连接已暂停", "INFO");
                LogConsoleVM.AddLog("回放加载成功", "SUCCESS");
            }
            catch (FileNotFoundException ex)
            {
                _playbackModeActive = false;
                LogConsoleVM.AddLog($"回放文件不存在：{ex.FileName}", "ERROR");
            }
            catch (Exception ex)
            {
                _playbackModeActive = false;
                LogConsoleVM.AddLog($"回放加载失败：{ex.Message}", "ERROR");
            }

            await Task.CompletedTask;
        }

        private void ProcessPlaybackMessage(MessageToClient message)
        {
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_drawPicLock)
                {
                    UpdateCharacters(message);
                    UpdateGameStatus(message);
                    UpdateMapElements(message);
                }
            });
        }

        public override void Dispose()
        {
            _autoConnectCts.Cancel();
            ReleaseConnectionResources();
            PlaybackVM.Dispose();
            base.Dispose();
        }
    }
}
