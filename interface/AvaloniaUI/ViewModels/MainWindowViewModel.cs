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
using System.Threading.Tasks;
using THUAI9_Avalonia.Models;

namespace THUAI9_Avalonia.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string gameLog = "等待连接...";

        [ObservableProperty]
        private string currentTime = "00:00";

        [ObservableProperty]
        private ObservableCollection<int> teamScores = new() { 0, 0, 0, 0 };

        [ObservableProperty]
        private string connectionStatus = "未连接";

        [ObservableProperty]
        private bool canConnectToServer = true;

        [ObservableProperty]
        private bool canDisconnectFromServer;

        [ObservableProperty]
        private int selectedTeamId = 1;

        [ObservableProperty]
        private string selectedPlayerIdText = "0";

        [ObservableProperty]
        private MapViewModel mapVM = new();

        [ObservableProperty]
        private LogConsoleViewModel logConsoleVM = new();

        [ObservableProperty]
        private PlaybackViewModel playbackVM = new();

        public ObservableCollection<CharacterViewModel> Team1Characters { get; } = new();
        public ObservableCollection<CharacterViewModel> Team2Characters { get; } = new();
        public ObservableCollection<CharacterViewModel> Team3Characters { get; } = new();
        public ObservableCollection<CharacterViewModel> Team4Characters { get; } = new();
        public ObservableCollection<LegendItem> MapLegendItems { get; } = new();
        public ObservableCollection<int> AvailableTeamIds { get; } = new() { 1, 2, 3, 4 };

        private Channel? _channel;
        private AvailableService.AvailableServiceClient? _client;
        private AsyncServerStreamingCall<MessageToClient>? _stream;
        private bool _isConnected;
        private bool _hasReceivedFirstFrame;
        private readonly object _drawPicLock = new();
        private Views.MapView? _mapView;

        public MainWindowViewModel()
        {
            InitializeMapLegend();
            LogConsoleVM.AddLog("THUAI9 调试界面已启动", "INFO");
            PlaybackVM.SetMessageCallback(ProcessPlaybackMessage);

            if (Avalonia.Controls.Design.IsDesignMode)
            {
                InitializeDesignTimeData();
            }
        }

        private void InitializeDesignTimeData()
        {
            GameLog = "设计模式 - 模拟数据";
            CurrentTime = "05:30";
            ConnectionStatus = "设计模式";

            Team1Characters.Add(new CharacterViewModel
            {
                Guid = 1,
                Name = "无人机 1 号",
                Hp = 80,
                MaxHp = 100,
                PosX = 10000,
                PosY = 10000,
                TeamId = 0,
                ActiveState = "移动"
            });
        }

        private void InitializeMapLegend()
        {
            MapLegendItems.Clear();
            MapLegendItems.Add(new LegendItem(Brushes.Cyan, "工厂"));
            MapLegendItems.Add(new LegendItem(Brushes.White, "空地", Brushes.LightGray, new Thickness(1)));
            MapLegendItems.Add(new LegendItem(Brushes.LightGreen, "草丛"));
            MapLegendItems.Add(new LegendItem(Brushes.DarkGray, "障碍物"));
            MapLegendItems.Add(new LegendItem(Brushes.Gold, "资源点"));
            MapLegendItems.Add(new LegendItem(Brushes.LightBlue, "算力中心"));
            MapLegendItems.Add(new LegendItem(Brushes.LightYellow, "市场"));
            MapLegendItems.Add(new LegendItem(Brushes.Red, "队伍 1 建筑"));
            MapLegendItems.Add(new LegendItem(Brushes.Blue, "队伍 2 建筑"));
            MapLegendItems.Add(new LegendItem(Brushes.Green, "队伍 3 建筑"));
            MapLegendItems.Add(new LegendItem(Brushes.Orange, "队伍 4 建筑"));
        }

        [RelayCommand]
        private async Task ConnectAsync(string serverAddress)
        {
            try
            {
                DisconnectCore(suppressLog: true);
                UpdateConnectionActionAvailability(canConnect: false, canDisconnect: false);

                string channelTarget = NormalizeGrpcTarget(serverAddress);
                LogConsoleVM.AddLog($"正在连接到 {channelTarget}...", "INFO");
                ConnectionStatus = "连接中...";

                var channelOptions = new List<ChannelOption>
                {
                    new(ChannelOptions.MaxSendMessageLength, -1),
                    new(ChannelOptions.MaxReceiveMessageLength, -1)
                };

                _channel = new Channel(channelTarget, ChannelCredentials.Insecure, channelOptions);
                await _channel.ConnectAsync(deadline: DateTime.UtcNow.AddSeconds(10));
                _client = new AvailableService.AvailableServiceClient(_channel);
                _hasReceivedFirstFrame = false;
                UpdateConnectionActionAvailability(canConnect: false, canDisconnect: true);

                ConnectionStatus = "已连通";
                LogConsoleVM.AddLog($"已建立到 {channelTarget} 的连接", "SUCCESS");

                try
                {
                    var mapMessage = await _client.GetMapAsync(new NullRequest()).ResponseAsync;
                    MapVM.UpdateMap(mapMessage);
                    _mapView?.RefreshMap();
                    LogConsoleVM.AddLog("已主动拉取静态地图，可在比赛开始前用于调试界面", "INFO");
                }
                catch (Exception ex)
                {
                    LogConsoleVM.AddLog($"已连通，但拉取静态地图失败：{ex.Message}", "WARNING");
                }

                if (!TryBuildRegisterFactoryRequest(out var request))
                {
                    DisconnectCore(suppressLog: true);
                    return;
                }

                _stream = _client.RegisterFactory(request);
                _isConnected = true;
                ConnectionStatus = "已连通 / 等待开局";
                LogConsoleVM.AddLog("已注册消息流，正在等待首帧游戏消息", "INFO");
                _ = ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                DisconnectCore(suppressLog: true);
                ConnectionStatus = "连接失败";
                LogConsoleVM.AddLog($"连接失败：{ex.Message}", "ERROR");
            }
        }

        [RelayCommand]
        private void Disconnect()
        {
            DisconnectCore(suppressLog: false);
        }

        private void DisconnectCore(bool suppressLog)
        {
            _isConnected = false;
            _hasReceivedFirstFrame = false;
            ReleaseConnectionResources();
            UpdateConnectionActionAvailability(canConnect: true, canDisconnect: false);

            ConnectionStatus = "已断开";
            if (!suppressLog)
            {
                LogConsoleVM.AddLog("已断开连接", "INFO");
            }
        }

        private void UpdateConnectionActionAvailability(bool canConnect, bool canDisconnect)
        {
            CanConnectToServer = canConnect;
            CanDisconnectFromServer = canDisconnect;
        }

        private bool TryBuildRegisterFactoryRequest(out RegisterFactoryMsg request)
        {
            request = new RegisterFactoryMsg();

            if (!AvailableTeamIds.Contains(SelectedTeamId))
            {
                ConnectionStatus = "连接失败";
                LogConsoleVM.AddLog($"队伍 ID 无效：{SelectedTeamId}，应在 1~4 范围内", "ERROR");
                UpdateConnectionActionAvailability(canConnect: true, canDisconnect: false);
                return false;
            }

            if (!long.TryParse(SelectedPlayerIdText?.Trim(), out long playerId) || playerId < 0)
            {
                ConnectionStatus = "连接失败";
                LogConsoleVM.AddLog($"玩家 ID 无效：{SelectedPlayerIdText}，请输入非负整数", "ERROR");
                UpdateConnectionActionAvailability(canConnect: true, canDisconnect: false);
                return false;
            }

            request.TeamId = SelectedTeamId;
            request.PlayerId = playerId;
            request.SideFlag = SelectedTeamId;
            LogConsoleVM.AddLog($"本次注册参数：TeamId={request.TeamId}, PlayerId={request.PlayerId}, SideFlag={request.SideFlag}", "INFO");
            return true;
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
                        UpdateConnectionActionAvailability(canConnect: true, canDisconnect: false);
                        ConnectionStatus = "已连通 / 消息流结束";
                        break;
                    }

                    var message = _stream.ResponseStream.Current;
                    _ = Dispatcher.UIThread.InvokeAsync(() => ProcessMessage(message));
                }
            }
            catch (Exception ex)
            {
                if (_isConnected)
                {
                    LogConsoleVM.AddLog($"接收消息错误：{ex.Message}", "ERROR");
                }
            }
        }

        private void ProcessMessage(MessageToClient message)
        {
            lock (_drawPicLock)
            {
                if (!_hasReceivedFirstFrame)
                {
                    _hasReceivedFirstFrame = true;
                    ConnectionStatus = "已收到首帧";
                    LogConsoleVM.AddLog("已收到首帧游戏消息", "SUCCESS");
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

                var existingCharacter = targetList.FirstOrDefault(c => c.Guid == data.Guid);
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
                        ActiveState = data.CharacterActiveState.ToString()
                    };

                    Dispatcher.UIThread.Post(() => targetList.Add(newCharacter));
                    UpdateCharacterOnMap(data, newCharacter.MaxHp);
                    continue;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    existingCharacter.Hp = data.Hp;
                    existingCharacter.PosX = data.X;
                    existingCharacter.PosY = data.Y;
                    existingCharacter.ActiveState = data.CharacterActiveState.ToString();
                });

                UpdateCharacterOnMap(data, existingCharacter.MaxHp);
            }

            RemoveUnseenCharacters(Team1Characters, currentFrameGuids);
            RemoveUnseenCharacters(Team2Characters, currentFrameGuids);
            RemoveUnseenCharacters(Team3Characters, currentFrameGuids);
            RemoveUnseenCharacters(Team4Characters, currentFrameGuids);
        }

        private void UpdateCharacterOnMap(MessageOfCharacter data, int maxHp)
        {
            int gridX = data.X / 1000;
            int gridY = data.Y / 1000;
            _mapView?.UpdateCharacterOnMap(
                data.Guid,
                GetCharacterName(data.CharacterType),
                gridX,
                gridY,
                (int)data.TeamId,
                data.Hp,
                maxHp);
        }

        private ObservableCollection<CharacterViewModel>? GetTeamList(long teamId)
        {
            return teamId switch
            {
                0 => Team1Characters,
                1 => Team2Characters,
                2 => Team3Characters,
                3 => Team4Characters,
                _ => null
            };
        }

        private void RemoveUnseenCharacters(ObservableCollection<CharacterViewModel> characterList, HashSet<long> seenGuids)
        {
            var charactersToRemove = characterList.Where(c => !seenGuids.Contains(c.Guid)).ToList();
            foreach (var character in charactersToRemove)
            {
                Dispatcher.UIThread.Post(() => characterList.Remove(character));
                _mapView?.RemoveCharacterFromMap(character.Guid);
            }
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
            CurrentTime = FormatGameTime(all.GameTime);

            for (int i = 0; i < all.Teams.Count && i < 4; i++)
            {
                if (i < TeamScores.Count)
                {
                    TeamScores[i] = all.Teams[i].Score;
                }
            }
        }

        private void UpdateMapElements(MessageToClient message)
        {
            foreach (var obj in message.ObjMessage)
            {
                if (obj.MapMessage != null)
                {
                    MapVM.UpdateMap(obj.MapMessage);
                }
                else if (obj.ResourceMessage != null)
                {
                    var res = obj.ResourceMessage;
                    MapVM.UpdateResourceCell(res.X / 1000, res.Y / 1000, res.RemainingAmount);
                }
                else if (obj.FactoryMessage != null)
                {
                    var factory = obj.FactoryMessage;
                    MapVM.UpdateBuildingCell(factory.X / 1000, factory.Y / 1000, GetTeamName(factory.TeamId), "Factory", factory.Hp);
                }
                else if (obj.ComputeCenterMessage != null)
                {
                    var center = obj.ComputeCenterMessage;
                    string teamName = center.OccupyProgress > 0 ? GetTeamName(center.OwnerTeamId) : "Neutral";
                    MapVM.UpdateBuildingCell(center.X / 1000, center.Y / 1000, teamName, "ComputeCenter", center.OccupyProgress);
                }
                else if (obj.MarketMessage != null)
                {
                    var market = obj.MarketMessage;
                    MapVM.UpdateBuildingCell(market.X / 1000, market.Y / 1000, "Unknown", "Market", market.PriceList.Count);
                }
            }

            _mapView?.RefreshMap();
        }

        private string FormatGameTime(int gameTimeInMilliseconds)
        {
            int totalSeconds = gameTimeInMilliseconds / 1000;
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return $"{minutes:D2}:{seconds:D2}";
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

        private int GetCharacterMaxHp(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => 100,
                CharacterType.Robot => 350,
                CharacterType.AutonomousCar => 300,
                _ => 1
            };
        }

        private static string GetTeamName(long teamId)
        {
            return teamId switch
            {
                0 => "Team1",
                1 => "Team2",
                2 => "Team3",
                3 => "Team4",
                _ => "Unknown"
            };
        }

        private static string NormalizeGrpcTarget(string serverAddress)
        {
            if (string.IsNullOrWhiteSpace(serverAddress))
            {
                return "127.0.0.1:8888";
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

                LogConsoleVM.AddLog($"正在加载回放：{filePath}", "INFO");
                PlaybackVM.Stop();
                PlaybackVM.LoadPlayback(filePath);
                LogConsoleVM.AddLog("回放加载成功", "SUCCESS");
            }
            catch (FileNotFoundException ex)
            {
                LogConsoleVM.AddLog($"回放文件不存在：{ex.FileName}", "ERROR");
            }
            catch (Exception ex)
            {
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
            DisconnectCore(suppressLog: true);
            PlaybackVM.Dispose();
            base.Dispose();
        }
    }
}
