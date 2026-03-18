using GameClass.GameObj;
using Gaming;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Preparation.Utility;
using Protobuf;
using Utility = Preparation.Utility;

namespace Server
{
    partial class GameServer : ServerBase
    {
        private int playerCountNow = 0;
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
        
        #region 连接和初始化服务

        public override Task<BoolRes> TryConnection(IDMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY TryConnection: Player {request.CharacterId} from Team {request.TeamId}");
            var onConnection = new BoolRes();
            lock (gameLock)
            {
                if (0 <= request.CharacterId && request.CharacterId < playerNum)
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
            // 待实现
            await Task.Delay(0);
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
            moveRes.ActSuccess = game.MoveCharacter(
                request.TeamId, request.CharacterId,
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
            boolRes.ActSuccess = game.Harvest(request.TeamId, request.PlayerId, request.ResourceId, request.Amount);
            GameServerLogging.logger.LogDebug($"END Harvesting:{boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> Attack(AttackMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY Attack: Player {request.CharacterId} from Team {request.TeamId} attacking Player {request.AttackedPlayerId} from Team {request.AttackedTeamId}");
            BoolRes boolRes = new();
            boolRes.ActSuccess = game.Attack(
                request.TeamId, request.PlayerId,
                request.AttackedTeamId, request.AttackedPlayerId);
            GameServerLogging.logger.LogDebug($"END Attack: {boolRes.ActSuccess}");
            return Task.FromResult(boolRes);
        }

        // public override Task<BoolRes> AttackConstruction(AttackFactoryMsg request, ServerCallContext context)
        // {
        //     GameServerLogging.logger.LogDebug(
        //         $"TRY AttackConstruction: Player {request.CharacterId} from Team {request.TeamId}");
        //     BoolRes boolRes = new();
        //     if (request.CharacterId >= spectatorMinPlayerID)
        //     {
        //         boolRes.ActSuccess = false;
        //         return Task.FromResult(boolRes);
        //     }
        //     boolRes.ActSuccess = game.AttackConstruction(request.TeamId, request.CharacterId);
        //     GameServerLogging.logger.LogDebug("END AttackConstruction");
        //     return Task.FromResult(boolRes);
        // }

        // public override Task<BoolRes> Repair(RepairFactory request, ServerCallContext context)
        // {
        //     GameServerLogging.logger.LogDebug($"TRY Repair");
        //     BoolRes boolRes = new();
        //     // 待实现
        //     GameServerLogging.logger.LogDebug("END Repair");
        //     return Task.FromResult(boolRes);
        // }

        public override Task<BoolRes> Occupy(OccupyMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY Occupy: Player {request.PlayerId} from Team {request.TeamId} occupying Resource {request.TargetComputeCenterId}");
            BoolRes boolRes = new();
            boolRes.ActSuccess = game.Occupy(request.TeamId, request.PlayerId, request.TargetComputeCenterId);
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
                            FromId = request.CharacterId,
                            ToId = request.ToCharacterId,
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


        public override Task<BoolRes> Load(LoadMeg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY Load: Player {request.PlayerId} from Team {request.TeamId}" +
            $" Semiconductor:{request.SemiconductorNum}, Medicine:{request.MedicineNum}, Handiwork:{request.HandiworkNum}, Costume:{request.CostumeNum}, Food:{request.FoodNum}");
            BoolRes boolRes = new();
            boolRes.ActSuccess = game.Load(request.TeamId, request.PlayerId, request.SemiconductorNum, request.MedicineNum, request.HandiworkNum, request.CostumeNum, request.FoodNum);
            GameServerLogging.logger.LogDebug("END Load");
            return Task.FromResult(boolRes);
        }

        public override Task<BoolRes> Sell(SellMeg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY Sell: Player {request.PlayerId} from Team {request.TeamId}" +
            $" Semiconductor:{request.SemiconductorNum}, Medicine:{request.MedicineNum}, Handiwork:{request.HandiworkNum}, Costume:{request.CostumeNum}, Food:{request.FoodNum}");
            BoolRes boolRes = new();
            boolRes.ActSuccess = game.Sell(request.TeamId, request.PlayerId, request.SemiconductorNum, request.MedicineNum, request.HandiworkNum, request.CostumeNum, request.FoodNum);
            GameServerLogging.logger.LogDebug("END Sell");
            return Task.FromResult(boolRes);
        }

        #endregion

        #region 核心角色操作

        public override Task<BoolRes> CreatCharacter(CreateCharacterMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY CreatCharacter: CharacterType {request.CharacterType} from Team {request.TeamId}");
            var activateCost = Transformation.CharacterTypeFromProto(request.CharacterType) switch
            {
                Utility.CharacterType.Drone => GameData.Dronecost,
                Utility.CharacterType.Robot => GameData.Robotcost,
                Utility.CharacterType.AutonomousCar => GameData.AutonomousCarcost,

                _ => int.MaxValue
            };
            var teamMoneyPool = game.TeamList[(int)request.TeamId].MoneyPool;
            if (activateCost > teamMoneyPool.Money)
            {
                return Task.FromResult(new BoolRes { ActSuccess = false });
            }
            BoolRes boolRes = new()
            {
                ActSuccess =
                    game.ActivateCharacter(
                        request.TeamId,
                        Transformation.CharacterTypeFromProto(request.CharacterType),
                        request.BirthpointIndex)
                    != GameObj.invalidID
            };
            if (boolRes.ActSuccess) teamMoneyPool.SubMoney(activateCost);
            GameServerLogging.logger.LogDebug("END CreatCharacter");
            return Task.FromResult(boolRes);
        }

        public override Task<CreatCharacterRes> CreatCharacterRID(CreateCharacterMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY CreatCharacterRID: CharacterType {request.CharacterType} from Team {request.TeamId}");
            var activateCost = Transformation.CharacterTypeFromProto(request.CharacterType) switch
            {
                Utility.CharacterType.Drone => GameData.Dronecost,
                Utility.CharacterType.Robot => GameData.Robotcost,
                Utility.CharacterType.AutonomousCar => GameData.AutonomousCarcost,

                _ => int.MaxValue
            };
            var teamMoneyPool = game.TeamList[(int)request.TeamId].MoneyPool;
            if (activateCost > teamMoneyPool.Money)
            {
                return Task.FromResult(new CreatCharacterRes { ActSuccess = false });
            }
            var playerId = game.ActivateCharacter(
                request.TeamId,
                Transformation.CharacterTypeFromProto(request.CharacterType),
                request.BirthpointIndex);

            CreatCharacterRes creatCharacterRes = new()
            {
                ActSuccess = playerId != GameObj.invalidID,
                PlayerId = playerId
            };
            if (creatCharacterRes.ActSuccess) teamMoneyPool.SubMoney(activateCost);
            GameServerLogging.logger.LogDebug("END CreatCharacterRID");
            return Task.FromResult(creatCharacterRes);
        }

        public override Task<BoolRes> EndAllAction(IDMsg request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug(
                $"TRY EndAllAction: Player {request.CharacterId} from Team {request.TeamId}");
            BoolRes boolRes = new();
            if (request.CharacterId >= spectatorMinPlayerID)
            {
                boolRes.ActSuccess = false;
                return Task.FromResult(boolRes);
            }
            boolRes.ActSuccess = game.Stop(request.TeamId, request.CharacterId);
            GameServerLogging.logger.LogDebug("END EndAllAction");
            return Task.FromResult(boolRes);
        }

        #endregion

        #region AI 服务

        public override Task<StrategicAIResponse> AskAI(StrategicAIRequest request, ServerCallContext context)
        {
            GameServerLogging.logger.LogDebug($"TRY AskAI: Team {request.TeamId}");
            StrategicAIResponse response = new();
            // 待实现
            Boolean boolRes = new();
            // boolRes.ActSuccess = game.AskAI(request.TeamId, request.GameState, request.AIAction);
            GameServerLogging.logger.LogDebug("END AskAI");
            return Task.FromResult(response);
        }

        #endregion
    }
}