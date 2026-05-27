using GameClass.GameObj;
using Gaming;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Preparation.Utility;
using Protobuf;
using System.Linq;
using System.Threading;
using Utility = Preparation.Utility;

namespace Server
{
    partial class GameServer : ServerBase
    {
        private int connectedTeamCount = 0;
        protected object spectatorLock = new();
        protected bool isSpectatorJoin = false;
        protected bool IsSpectatorJoin
        {
            get
            {
                lock (spectatorLock) return isSpectatorJoin;
            }

            set
            {
                lock (spectatorLock)
                    isSpectatorJoin = value;
            }
        }

        private bool IsTeamConnected(long teamId)
        {
            int index = (int)teamId;
            if (index <= 0 || index > TeamCount)
            {
                return false;
            }
            return semaDicts[index].Keys.Any(id => id < spectatorMinPlayerID);
        }

        private bool AllTeamsConnected()
        {
            for (int t = 1; t <= TeamCount; t++)
            {
                if (!IsTeamConnected(t))
                {
                    return false;
                }
            }
            return true;
        }

        #region 连接和初始化服务

        public override Task<BoolRes> TryConnection(IDMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY TryConnection: Player {request.PlayerId} from Team {request.TeamId}");
            var onConnection = new BoolRes();
            lock (gameLock)
            {
                if (0 <= request.PlayerId && request.PlayerId < playerNum)
                {
                    onConnection.ActSuccess = true;
                    GameServerLogging.logger.LogInfo($"TryConnection: {onConnection.ActSuccess}");
                    return Task.FromResult(onConnection);
                }
            }
            onConnection.ActSuccess = false;
            GameServerLogging.logger.LogDebug("END TryConnection");
            return Task.FromResult(onConnection);
        }

        #endregion

        #region 游戏开局调用一次的服务

        protected readonly object addPlayerLock = new();
        public override async Task RegisterFactory(RegisterFactoryMsg request, IServerStreamWriter<MessageToClient> responseStream, ServerCallContext context)
        {
            try
            {
                GameServerLogging.logger.LogDebug($"TRY Register Factory: Team {request.TeamId}");

                bool isSpectatorRequest = request.PlayerId >= spectatorMinPlayerID && options.NotAllowSpectator == false;


                //if (communicationToGameID[request.TeamId][request.PlayerId] != GameObj.invalidID)
                //    return;

                // 观战玩家分支
                if (isSpectatorRequest)
                {
                    GameServerLogging.logger.LogDebug($"TRY Add Spectator: Player {request.PlayerId}");
                    lock (spectatorJoinLock)
                    {
                        if (semaDicts[0].TryAdd(request.PlayerId, (new SemaphoreSlim(0, 1), new SemaphoreSlim(0, 1))))
                        {
                            GameServerLogging.logger.LogInfo("A new spectator comes to watch this game");
                            IsSpectatorJoin = true;
                        }
                        else
                        {
                            GameServerLogging.logger.LogInfo($"Duplicated Spectator ID {request.PlayerId}");
                            return;
                        }
                    }

                    do
                    {
                        semaDicts[0][request.PlayerId].Item1.Wait();
                        try
                        {
                            if (currentGameInfo != null)
                            {
                                var info = currentGameInfo.Clone();
                                for (int i = info.ObjMessage.Count - 1; i >= 0; i--)
                                {
                                    if (info.ObjMessage[i].NewsMessage != null)
                                    {
                                        info.ObjMessage.RemoveAt(i);
                                    }
                                }
                                await responseStream.WriteAsync(info);
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            if (semaDicts[0].TryRemove(request.PlayerId, out var semas))
                            {
                                try
                                {
                                    semas.Item1.Release();
                                    semas.Item2.Release();
                                }
                                catch { }
                                GameServerLogging.logger.LogInfo($"The spectator {request.PlayerId} exited");
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            GameServerLogging.logger.LogInfo(ex.ToString());
                        }
                        finally
                        {
                            try
                            {
                                semaDicts[0][request.PlayerId].Item2.Release();
                            }
                            catch { }
                        }
                    } while (game.GameMap.Timer.IsGaming);

                    GameServerLogging.logger.LogDebug("END Add Spectator");
                    return;
                }

                if (currentGameInfo != null && currentGameInfo.GameState == GameState.GameEnd)
                    return;

                if (!ValidPlayerID(request.PlayerId))
                    return;

                if (request.TeamId <= 0 || request.TeamId > TeamCount)
                    return;

                GameServerLogging.logger.LogDebug("AddPlayer: Check Correct");

                // 加入玩家队列
                var playerSemas = (new SemaphoreSlim(0, 1), new SemaphoreSlim(0, 1));
                bool gameAlreadyStarted = game.GameMap?.Timer?.IsGaming ?? false;
                lock (addPlayerLock)
                {
                    lock (spectatorJoinLock)
                    {
                        GameServerLogging.logger.LogDebug($"player id :{request.PlayerId}  team id:{request.TeamId} sideflag: {request.SideFlag}");

                        bool teamConnectedBefore = IsTeamConnected(request.TeamId);
                        if (!semaDicts[request.TeamId].TryAdd(request.PlayerId, playerSemas))
                        {
                            GameServerLogging.logger.LogWarning($"Player {request.PlayerId} has already been registered in team {request.TeamId}");
                            return;
                        }

                        if (!teamConnectedBefore)
                        {
                            Interlocked.Increment(ref connectedTeamCount);
                        }

                        bool start = connectedTeamCount == TeamCount;
                        GameServerLogging.logger.LogInfo($"Register Factory: Team {request.TeamId}, connected teams: {connectedTeamCount}/{TeamCount}");

                        if (start)
                        {
                            StartGame();
                        }
                    }
                }

                if (gameAlreadyStarted && game.GameMap.Timer.IsGaming)
                {
                    try
                    {
                        var lateJoinFrame = BuildLateJoinStartFrame();
                        await responseStream.WriteAsync(lateJoinFrame);
                    }
                    catch (Exception ex)
                    {
                        GameServerLogging.logger.LogWarning($"Late join bootstrap failed for Team {request.TeamId}, Player {request.PlayerId}: {ex.Message}");
                    }
                }

                bool exitFlag = false;
                bool firstTime = true;
                do
                {
                    playerSemas.Item1.Wait();
                    if (!semaDicts[request.TeamId].ContainsKey(request.PlayerId))
                    {
                        GameServerLogging.logger.LogInfo($"Client {request.PlayerId} was removed (stale), exiting stream.");
                        break;
                    }
                    var character = game.GameMap.FindCharacterInPlayerID(request.TeamId, request.PlayerId);

                    if (!firstTime && request.PlayerId > 0 && (character == null || character.IsRemoved == true))
                    {
                        // character离开/死亡时可安全忽略继续发流
                    }
                    else
                    {
                        if (firstTime)
                            firstTime = false;

                        try
                        {
                            if (currentGameInfo != null && !exitFlag)
                            {
                                await responseStream.WriteAsync(currentGameInfo);
                            }
                        }
                        catch
                        {
                            if (!exitFlag)
                            {
                                GameServerLogging.logger.LogInfo($"The client {request.PlayerId} exited");
                                exitFlag = true;
                            }
                        }
                    }

                    try { playerSemas.Item2.Release(); } catch { }
                } while (game.GameMap.Timer.IsGaming);
            }
            catch (Exception ex)
            {
                GameServerLogging.logger.LogError($"RegisterFactory exception: {ex}");
            }
        }

        public override Task<MessageOfMap> GetMap(NullRequest request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"GetMap: IP {context.Peer}");
            return Task.FromResult(MapMsg());
        }

        #endregion

        #region 游戏过程中普通角色执行操作的服务

        public override Task<MoveRes> Move(MoveMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY Move: Player {request.PlayerId} from Team {request.TeamId}, " +
                $"TimeInMilliseconds: {request.TimeInMilliseconds}" + $"Angle: {request.Angle}");
            MoveRes moveRes = new();
            if (double.IsNaN(request.Angle))
            {
                moveRes.ActSuccess = false;
                return Task.FromResult(moveRes);
            }
            // var gameID = communicationToGameID[request.TeamId][request.PlayerId];
            moveRes.ActSuccess = game.Move(
                request.TeamId, request.PlayerId,
                (int)request.TimeInMilliseconds, request.Angle);
            if (!game.GameMap.Timer.IsGaming)
                moveRes.ActSuccess = false;
            GameServerLogging.logger.LogDebug($"END Move: {moveRes.ActSuccess}");
            return Task.FromResult(moveRes);
        }

        public override Task<BoolRes> Recover(RecoverMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY Recover: Player {request.PlayerId} from Team {request.TeamId}" +
                $"RecoveredHp: {request.RecoveredHp}");
            BoolRes boolRes = new();

            boolRes.ActSuccess = game.Recover(request.TeamId, request.PlayerId, request.RecoveredHp);
            GameServerLogging.logger.LogDebug($"END Recover:{boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> Harvest(ResourceMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY Harvesting: Player {request.PlayerId} from Team {request.TeamId}" +
            $"HarvestedResource: {request.ResourceId}, Amount: {request.Amount}");
            BoolRes boolRes = new();
            // boolRes.ActSuccess = game.Harvest(request.TeamId, request.PlayerId, request.ResourceId, request.Amount);
            boolRes.ActSuccess = game.Harvest(request.TeamId, request.PlayerId);
            GameServerLogging.logger.LogDebug($"END Harvesting:{boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> Attack(AttackMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY Attack: Player {request.PlayerId} from Team {request.TeamId} attacking Player {request.AttackedPlayerId} from Team {request.AttackedTeamId}");
            BoolRes boolRes = new();
            if (request.AttackedTeamId > 0 && request.AttackedPlayerId == 0)
            {
                // 指定攻击工厂：跳过自动索敌，直接打目标工厂
                boolRes.ActSuccess = game.AttackFactory(
                    request.TeamId, request.PlayerId, request.AttackedTeamId);
            }
            else
            {
                boolRes.ActSuccess = game.Attack(
                    request.TeamId, request.PlayerId);
            }
            GameServerLogging.logger.LogDebug($"END Attack: {boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> Occupy(OccupyMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY Occupy: Player {request.PlayerId} from Team {request.TeamId} occupying Resource {request.TargetComputeCenterId}");
            BoolRes boolRes = new();
            boolRes.ActSuccess = game.Occupy(request.TeamId, request.PlayerId);
            GameServerLogging.logger.LogDebug($"END Occupy: {boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }
        public override Task<BoolRes> Send(SendMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY Send: From Player {request.PlayerId} To Player {request.ToPlayerId} from Team {request.TeamId}");
            BoolRes boolRes = new BoolRes();

            GameServerLogging.logger.LogDebug($"Send: As {request.MessageCase}");
            switch (request.MessageCase)
            {
                case SendMsg.MessageOneofCase.TextMessage:
                    {
                        if (request.TextMessage.Length > 256)
                        {
                            GameServerLogging.logger.LogDebug("Send: Text message string is too long!");
                            boolRes.ActSuccess = false;
                            return Task.FromResult(boolRes);
                        }
                        MessageOfNews news = new()
                        {
                            TextMessage = request.TextMessage,
                            FromId = request.PlayerId,
                            ToId = request.ToPlayerId,
                            TeamId = request.TeamId
                        };
                        lock (newsLock)
                        {
                            currentNews.Add(news);
                        }
                        GameServerLogging.logger.LogDebug("Send: Text: " + news.TextMessage);
                        boolRes.ActSuccess = true;
                        GameServerLogging.logger.LogDebug($"END Send");
                        return Task.FromResult(boolRes);
                    }
                case SendMsg.MessageOneofCase.BinaryMessage:
                    {
                        if (request.BinaryMessage.Length > 256)
                        {
                            GameServerLogging.logger.LogDebug("Send: Binary message string is too long!");
                            boolRes.ActSuccess = false;
                            return Task.FromResult(boolRes);
                        }
                        MessageOfNews news = new()
                        {
                            BinaryMessage = request.BinaryMessage,
                            FromId = request.PlayerId,
                            ToId = request.ToPlayerId,
                            TeamId = request.TeamId
                        };
                        lock (newsLock)
                        {
                            currentNews.Add(news);
                        }
                        GameServerLogging.logger.LogDebug($"BinaryMessageLength: {news.BinaryMessage.Length}");
                        boolRes.ActSuccess = true;
                        GameServerLogging.logger.LogDebug($"END Send");
                        return Task.FromResult(boolRes);
                    }
                default:
                    {
                        boolRes.ActSuccess = false;
                        return Task.FromResult(boolRes);
                    }
            }
        }


        public override Task<BoolRes> Load(LoadMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY Load: {request.PlayerId} from Team {request.TeamId} loading Product {request.ProductType} with Amount {request.ProductAmount}");
            BoolRes boolRes = new()
            {
                ActSuccess =
                    game.Load(
                        request.TeamId,
                        request.PlayerId,
                        Transformation.GoodsTypeFromProto(request.ProductType),
                        request.ProductAmount)
            };
            GameServerLogging.logger.LogDebug($"END Load:{boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> Trade(TradeMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY Trade: Player {request.PlayerId} {(request.IsBuy ? "buy from" : "sell to")} Team {request.TeamId}" +
            $" Product:{request.ProductType}, Amount:{request.ProductAmount}");
            var goodsType = Transformation.GoodsTypeFromProto(request.ProductType);
            BoolRes boolRes = new()
            {
                ActSuccess =
                    game.Trade(
                        request.TeamId,
                        request.PlayerId,
                        goodsType,
                        request.ProductAmount,
                        request.IsBuy)
            };
            if (!boolRes.ActSuccess)
            {
                if (game.TryGetCharacter(request.TeamId, request.PlayerId, out var character) && character != null)
                {
                    int have = character.GoodsLoad.Get(goodsType);
                    var cell = GameData.PosGridToCellXY(character.Position);
                    var nearMarket = game.GameMap.OneForInteract(character.Position, Utility.GameObjType.MARKET) != null;
                    GameServerLogging.logger.LogWarning(
                        $"Trade failed diagnostics: team={request.TeamId}, player={request.PlayerId}, buy={request.IsBuy}, type={goodsType}, amount={request.ProductAmount}, " +
                        $"have={have}, pos=({character.Position.x},{character.Position.y}), cell=({cell.x},{cell.y}), nearMarket={nearMarket}");
                }
                else
                {
                    GameServerLogging.logger.LogWarning($"Trade failed diagnostics: character not found, team={request.TeamId}, player={request.PlayerId}");
                }
            }
            GameServerLogging.logger.LogDebug($"END Trade:{boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        #endregion

        #region 核心角色操作

        public override Task<BoolRes> CreateCharacter(CreateCharacterMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY CreatCharacter: CharacterType {request.CharacterType} from Team {request.TeamId}");
            BoolRes boolRes = new();
            if (request.TeamId <= 0 || request.TeamId > TeamCount || request.PlayerId <= 0 || !ValidPlayerID(request.PlayerId))
            {
                boolRes.ActSuccess = false;
                GameServerLogging.logger.LogDebug($"END CreateCharacter: Invalid TeamId {request.TeamId} or PlayerId {request.PlayerId}");
                return Task.FromResult(boolRes);
            }
            boolRes.ActSuccess =
                game.RecruitCharacterAtFactory(
                    request.TeamId,
                    request.PlayerId,
                    Transformation.CharacterTypeFromProto(request.CharacterType));
            GameServerLogging.logger.LogDebug($"END CreateCharacter:{boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<CreatCharacterRes> CreateCharacterRID(CreateCharacterMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY CreateCharacterRID: CharacterType {request.CharacterType} from Team {request.TeamId}");
            CreatCharacterRes res = new();
            if (request.TeamId <= 0 || request.TeamId > TeamCount || request.PlayerId <= 0 || !ValidPlayerID(request.PlayerId))
            {
                res.ActSuccess = false;
                GameServerLogging.logger.LogDebug($"END CreateCharacterRID: Invalid TeamId {request.TeamId} or PlayerId {request.PlayerId}");
                return Task.FromResult(res);
            }
            res.ActSuccess =
                game.RecruitCharacterAtFactory(
                    request.TeamId,
                    request.PlayerId,
                    Transformation.CharacterTypeFromProto(request.CharacterType));
            if (res.ActSuccess)
                res.PlayerId = request.PlayerId;
            GameServerLogging.logger.LogDebug($"END CreateCharacterRID: {res.ActSuccess}, PlayerId={res.PlayerId}");
            return Task.FromResult(res);
        }

        public override Task<BoolRes> Produce(ProduceGoodsMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY Produce Goods: Team {request.TeamId} want to " +
                $"produce Type {request.ProductType}, with produce {request.MaxProduceNum} at most");
            BoolRes boolRes = new()
            {
                ActSuccess =
                    game.Produce(
                        request.TeamId,
                        Transformation.GoodsTypeFromProto(request.ProductType),
                        request.MaxProduceNum)
            };
            GameServerLogging.logger.LogDebug($"END Produce Goods: {boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> UplevelTech(UplevelTechMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY UplevelTech: Team {request.TeamId}, Tech {request.TechType}");
            BoolRes boolRes = new()
            {
                ActSuccess =
                    game.UplevelTech(
                        request.TeamId,
                        (Preparation.Utility.TechType)(int)request.TechType)
            };
            GameServerLogging.logger.LogDebug($"END UplevelTech: {boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> EndAllAction(IDMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY EndAllAction: Player {request.PlayerId} from Team {request.TeamId}");
            BoolRes boolRes = new()
            {
                ActSuccess = game.Stop(request.TeamId, request.PlayerId)
            };
            GameServerLogging.logger.LogDebug("END EndAllAction");
            return Task.FromResult(boolRes);
        }

        #endregion

        public override Task<StrategicAIResponse> AskAI(StrategicAIRequest request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY AskAI: Team {request.TeamId} at {request.CurrentGameTime}");
            var response = new StrategicAIResponse();
            var answer = game.AskAI(request.TeamId, request.Prompt);
            response.ActSuccess = !string.IsNullOrWhiteSpace(answer);
            if (response.ActSuccess)
            {
                response.Answer = answer!;
            }
            else
            {
                response.Explanation = "AskAI failed";
            }
            GameServerLogging.logger.LogDebug("END AskAI");
            return Task.FromResult(response);
        }

        public override Task<EventStatusResponse> GetCurrentEventStatus(EventStatusRequest request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY GetCurrentEventStatus: Team {request.TeamId}, Player {request.PlayerId}");
            var response = new EventStatusResponse();
            var status = game.GetCurrentEventStatus(request.TeamId, request.PlayerId);
            if (status.HasValue)
            {
                response.ActSuccess = true;
                response.EventName = status.Value.Name;
                response.EventDescription = status.Value.Description;
            }
            else
            {
                response.ActSuccess = false;
                response.EventName = "";
                response.EventDescription = "Failed to get event status";
            }
            GameServerLogging.logger.LogDebug($"END GetCurrentEventStatus: {response.ActSuccess}, name={response.EventName}");
            return Task.FromResult(response);
        }
    }
}
