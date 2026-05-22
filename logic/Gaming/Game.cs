using GameClass.GameObj;
using GameClass.GameObj.Areas;
using GameClass.GameObj.Map;
using GameClass.MapGenerator;
using GameEngine;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using System;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gaming
{
    /// <summary>
    /// 面向选手的公开 API。请在其它 partial 文件中实现具体逻辑。
    /// 这些方法应是线程安全的入口，内部应走命令队列/权威循环执行。
    /// </summary>
    public partial class Game
    {
        private readonly Map gameMap;
        public Map GameMap => gameMap;
        private readonly int numOfTeam;
        public int NumOfTeam => numOfTeam;
        public Game(MapStruct mapResource, int numOfTeam)
        {
            this.numOfTeam = numOfTeam;
            gameMap = new(mapResource);
            characterManager = new(this, gameMap);
            actionManager = new(this, gameMap, characterManager);
            tradeManager = new(this, gameMap);
            uplevelManager = new(this);
            teams = new ConcurrentDictionary<long, TeamState>();
            InitTeams(numOfTeam);

            tradeManager = new TradeManager(this, gameMap);
            uplevelManager = new UplevelManager(this);

        }

        private bool EnsureGameStarted(string actionName)
        {
            if (gameMap.Timer.IsGaming) return true;
            LogicLogging.logger.LogWarning($"{actionName} failed: game has not started.");
            return false;
        }

        public bool StartGame(int milliSeconds)
        {
            if (milliSeconds <= 0)
                return false;
            if (gameMap.Timer.IsGaming)
                return false;

            ResetEventSchedule();

            lock (gameEndLock)
            {
                gameEnded = false;
            }

            foreach (var kv in teams)
            {
                long teamId = kv.Key;
                var factory = kv.Value.Factory;
                if (factory == null) continue;

                factory.TeamID.SetROri(teamId);
                factory.CanProduce.SetROri(true);
                factory.CanRecruit.SetROri(true);

                // Map 构造时会按地图数据预置 Factory（无队伍上下文）。
                // 这里按同格位置移除预置实例，确保地图中只保留 teams 维护的 Factory 对象。
                var factoryCell = GameData.PosGridToCellXY(factory.Position);
                gameMap.GameObjDict[GameObjType.FACTORY].RemoveAll(obj =>
                {
                    if (obj is not Factory oldFactory) return false;
                    var oldCell = GameData.PosGridToCellXY(oldFactory.Position);
                    return oldCell == factoryCell;
                });
                gameMap.Add(factory);
            }

            // 市场由地图预置（Map 构造时会根据 PlaceType.MARKET 创建），无需在此处硬编码创建

            if (!gameMap.Timer.Start(() => { }, () => CheckAndHandleGameEnd(), milliSeconds))
            {
                LogicLogging.logger.LogError("Failed to start game timer.");
                return false;
            }

            new Thread
            (
                () =>
                {
                    Thread.Sleep(GameData.CheckInterval);
                    new Timothy.FrameRateTask.FrameRateTaskExecutor<int>
                    (
                        loopCondition: () => gameMap.Timer.IsGaming,
                        loopToDo: () =>
                        {
                            int nowTimeMs = NowTime();
                            TryTriggerPeriodicEvent(nowTimeMs);

                            // Count occupied compute centers per team
                            int[] occupiedCounts = new int[teams.Count + 1];
                            if (gameMap.GameObjDict.TryGetValue(GameObjType.COMPUTE_CENTER, out var centerList))
                            {
                                var centers = centerList.Cast<ComputeCenter>()?.ToNewList();
                                if (centers != null)
                                {
                                    foreach (var cc in centers)
                                    {
                                        if (cc.IsOccupied)
                                        {
                                            long ownerId = cc.OccupiedByTeamId;
                                            if (ownerId > 0 && ownerId < occupiedCounts.Length)
                                                occupiedCounts[ownerId]++;
                                        }
                                    }
                                }
                            }
                            foreach (var team in teams)
                            {
                                var fac = team.Value.Factory;
                                if (fac == null) continue;
                                fac.SetOccupiedComputeCenters(occupiedCounts[team.Key]);
                                fac.TickComputingPower(GameData.CheckInterval);
                            }

                            return !CheckAndHandleGameEnd();
                        },
                        timeInterval: GameData.CheckInterval,
                        finallyReturn: () => 0
                    ).Start();
                }
            ).Start();
            return true;
        }

        public bool RecruitCharacterAtFactory(long teamId, long playerId, Preparation.Utility.CharacterType type)
        {
            if (!EnsureGameStarted(nameof(RecruitCharacterAtFactory)))
                return false;

            var fac = GetTeamFactory(teamId);
            if (fac == null)
            {
                LogicLogging.logger.LogWarning($"RecruitCharacterAtFactory failed: Factory for team {teamId} not found.");
                return false;
            }
            return characterManager.RecruitCharacter(teamId, playerId, type, fac.Position);
        }

        /// </summary>
        /// <param name="teamId">队伍 ID</param>
        /// <param name="playerId">队内玩家/单位 ID</param>
        /// <param name="timeMs">移动持续时间（毫秒）</param>
        /// <param name="direction">移动方向（弧度）</param>
        /// <returns>是否成功受理</returns>
        public bool Move(long teamId, long playerId, int timeMs, double direction)
        {
            if (!EnsureGameStarted(nameof(Move)))
                return false;

            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Move failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null || character.IsRemoved)
            {
                LogicLogging.logger.LogWarning($"Move failed: Character for team {teamId} player {playerId} is null or removed.");
                return false;
            }
            return actionManager.MoveCharacter(character, timeMs, direction);
        }

        /// <summary>
        /// 发起一次普通近战/远程攻击（根据地图上可见目标自动选择目标）。
        /// 使用 teamId + playerId（队内 id）作为发起者标识。
        /// </summary>
        /// <param name="teamId">发起者队伍 ID</param>
        /// <param name="playerId">发起者队内 ID</param>
        /// <returns>是否成功受理</returns>
        public bool Attack(long teamId, long playerId)
        {
            if (!EnsureGameStarted(nameof(Attack)))
                return false;

            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Attack failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null || character.IsRemoved)
            {
                LogicLogging.logger.LogWarning($"Attack failed: Character for team {teamId} player {playerId} is null or removed.");
                return false;
            }
            int attackRange = (int)character.AttackSize.GetValue();
            var enemies = gameMap.CharacterInTheRangeNotTeamID(character.Position, attackRange, character.TeamID.Get());
            if (enemies != null && enemies.Count > 0)
            {
                Character nearest = null;
                long bestDist = long.MaxValue;
                foreach (var e in enemies)
                {
                    if (e == null || e.IsRemoved) continue;
                    long d = (long)XY.DistanceCeil3(e.Position, character.Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        nearest = e;
                    }
                }
                if (nearest != null)
                {
                    return actionManager.Attack(character, nearest);
                }
            }

            var factory = (Factory?)gameMap.OneInTheRange(character.Position, attackRange, GameObjType.FACTORY);
            if (factory != null && factory.TeamID.Get() != character.TeamID.Get())
            {
                return actionManager.Attack(character, factory);
            }
            LogicLogging.logger.LogWarning($"Attack failed: No valid targets in range for character of team {teamId} player {playerId}.");
            return false;
        }


        /// </summary>
        /// <param name="teamId">队伍 ID</param>
        /// <param name="playerId">队内玩家 ID</param>
        /// <returns>是否成功受理</returns>
        public bool Harvest(long teamId, long playerId)
        {
            if (!EnsureGameStarted(nameof(Harvest)))
                return false;

            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Harvest failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null || character.IsRemoved)
            {
                LogicLogging.logger.LogWarning($"Harvest failed: Character for team {teamId} player {playerId} is null or removed.");
                return false;
            }
            return actionManager.Harvest(character);
        }

        /// <summary>
        /// 进行交易（购买/出售）某种商品。
        /// 使用 teamId + playerId 标识操作者。
        /// </summary>
        public bool Trade(long teamId, long playerId, Preparation.Utility.GoodsType type, int amount, bool buy)
        {
            if (!EnsureGameStarted(nameof(Trade)))
                return false;

            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Trade failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null)
            {
                LogicLogging.logger.LogWarning($"Trade failed: Character for team {teamId} player {playerId} is null.");
                return false;
            }
            return buy ? tradeManager.Buy(character, type, amount) : tradeManager.Sell(character, type, amount);
        }

        /// <summary>
        /// 获取当前生效事件（名称与描述）。
        /// 使用 teamId + playerId 标识调用者。
        /// </summary>
        public EventStatus? GetCurrentEventStatus(long teamId, long playerId)
        {
            if (!EnsureGameStarted(nameof(GetCurrentEventStatus)))
                return null;

            // 验证调用者属于该队伍：可以是已注册的工厂端或已创建的角色
            bool valid = characterManager.TryGetCharacter(teamId, playerId, out var character)
                         && character != null && !character.IsRemoved;
            if (!valid)
            {
                // 也允许工厂端 playerId 查询（工厂端可能尚未创建角色）
                var fac = GetTeamFactory(teamId);
                if (fac == null)
                {
                    LogicLogging.logger.LogWarning($"GetCurrentEventStatus failed: team {teamId} not found.");
                    return null;
                }
            }

            return new EventStatus(marketEvent.Name, marketEvent.Description);
        }

        /// <summary>
        /// 选手传入提示词请求 AI 答复，会消耗本队工厂算力。
        /// 返回 null 表示请求失败。
        /// </summary>
        public string? AskAI(long teamId, string prompt)
        {
            if (!EnsureGameStarted(nameof(AskAI)))
                return null;

            if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > GameData.AskAIPromptMaxLength)
            {
                LogicLogging.logger.LogWarning($"AskAI failed: invalid prompt length for team {teamId}.");
                return null;
            }

            var fac = GetTeamFactory(teamId);
            if (fac == null)
            {
                LogicLogging.logger.LogWarning($"AskAI failed: Factory for team {teamId} not found.");
                return null;
            }

            int cost = GameData.AskAICostComputingPower;
            while (true)
            {
                long cur = fac.ComputingPower.Get();
                if (cur < cost)
                {
                    LogicLogging.logger.LogWarning($"AskAI failed: insufficient computing power for team {teamId}. current={cur}, need={cost}");
                    return null;
                }
                if (fac.ComputingPower.CompareExROri(cur - cost, cur) == cur) break;
            }

            var answer = marketEvent.AskWithPrompt(prompt);
            if (string.IsNullOrWhiteSpace(answer))
            {
                fac.AddComputingPower(cost);
                LogicLogging.logger.LogWarning($"AskAI failed: model returned empty response for team {teamId}.");
                return null;
            }

            return answer;
        }

        /// <summary>
        /// 占领指定算力中心，一般需要持续占领一段时间。
        /// 使用 teamId + playerId 标识操作者。
        /// </summary>
        public bool Occupy(long teamId, long playerId)
        {
            if (!EnsureGameStarted(nameof(Occupy)))
                return false;

            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Occupy failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null || character.IsRemoved)
            {
                LogicLogging.logger.LogWarning($"Occupy failed: Character for team {teamId} player {playerId} is null or removed.");
                return false;
            }
            return actionManager.Occupy(character);
        }

        /// <summary>
        /// 使用/研发一项科技（根据实际赛题约束实现）。
        /// 使用 teamId + playerId 标识操作者。
        /// </summary>
        public bool UplevelTech(long teamId, Preparation.Utility.TechType tech)
        {
            if (!EnsureGameStarted(nameof(UplevelTech)))
                return false;

            return uplevelManager.UplevelTech(teamId, tech);
        }

        /// <summary>
        /// 回复指定队伍内某个角色的血量（按 teamId + playerId 定位）。
        /// </summary>
        public bool Recover(long teamId, long playerId, long recover)
        {
            if (!EnsureGameStarted(nameof(Recover)))
                return false;

            if (recover <= 0)
            {
                LogicLogging.logger.LogWarning($"Recover failed: Invalid recover amount {recover} for team {teamId} player {playerId}.");
                return false;
            }
            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Recover failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null || character.IsRemoved)
            {
                LogicLogging.logger.LogWarning($"Recover failed: Character for team {teamId} player {playerId} is null or removed.");
                return false;
            }
            return characterManager.Recover(character, recover);
        }

        /// <summary>
        /// 回复某队的工厂血量。
        /// </summary>
        public bool Recover(long teamId, long recover)
        {
            if (!EnsureGameStarted(nameof(Recover)))
                return false;

            if (recover <= 0)
            {
                LogicLogging.logger.LogWarning($"Recover failed: Invalid recover amount {recover} for team {teamId} factory.");
                return false;
            }
            var fac = GetTeamFactory(teamId);
            if (fac == null)
            {
                LogicLogging.logger.LogWarning($"Recover failed: Factory for team {teamId} not found.");
                return false;
            }
            fac.HP.AddPositiveV(recover);
            return true;
        }

        /// <summary>
        /// 获取当前帧/时刻对该玩家可见的世界快照。
        /// 使用 teamId + playerId 标识观察者。
        /// </summary>
        public WorldSnapshot GetSnapshot()
        {
            if (gameMap == null) return new WorldSnapshot(NowTime(), Array.Empty<object>());

            var visible = new List<object>(256);
            foreach (var kv in gameMap.GameObjDict)
            {
                var list = kv.Value;
                list.ForEach(obj => visible.Add(obj));
            }
            return new WorldSnapshot(NowTime(), visible);
        }

        /// <summary>
        /// 获取当前游戏进行的时间（毫秒）。
        /// </summary>
        public int NowTime()
            => gameMap?.Timer.NowTime() ?? 0;

        /// <summary>
        /// 获取某队当前的状态信息（含工厂血量/存储/算力/来源和科技等级等）。
        /// </summary>
        public TeamStatus GetTeamStatus(long teamId)
        {
            if (!teams.TryGetValue(teamId, out var t))
            {
                return new TeamStatus(teamId, 0, 0, 0, 0, 0, 0, 0, new Dictionary<string, int>(), false, false);
            }

            var fac = GetTeamFactory(teamId);
            long factoryHp = 0, factoryHpMax = 0, storage = 0, storageMax = 0, computing = 0, source = 0;
            bool canProduce = false, canRecruit = false;
            if (fac != null)
            {
                try { factoryHp = fac.HP.GetValue(); } catch { factoryHp = 0; }
                try { factoryHpMax = fac.HP.GetMaxV(); } catch { factoryHpMax = 0; }
                try { storage = fac.Storage.GetValue(); } catch { storage = 0; }
                try { storageMax = fac.Storage.GetMaxV(); } catch { storageMax = 0; }
                try { computing = fac.ComputingPower.Get(); } catch { computing = 0; }
                try { source = fac.Source.Get(); } catch { source = 0; }
                try { canProduce = fac.CanProduce.Get(); } catch { canProduce = false; }
                try { canRecruit = fac.CanRecruit.Get(); } catch { canRecruit = false; }
            }

            var techKeys = new string[] { "Cost", "Efficiency", "Market", "Robust", "Warrior", "Production", "Storage", "MoveSpeed", "Carry", "Price" };
            var techs = new Dictionary<string, int>(techKeys.Length);
            foreach (var k in techKeys) techs[k] = t.GetTech(k);

            return new TeamStatus(teamId, t.Score.Get(), factoryHp, factoryHpMax, storage, storageMax, computing, source, techs, canProduce, canRecruit);
        }

        /// <summary>
        /// 获取所有队伍的状态快照列表。
        /// </summary>
        public IReadOnlyList<TeamStatus> GetAllTeamStatus()
        {
            var list = new List<TeamStatus>(teams.Count);
            foreach (var kv in teams)
            {
                var teamId = kv.Key;
                var t = kv.Value;

                var fac = GetTeamFactory(teamId);
                long factoryHp = 0, factoryHpMax = 0, storage = 0, storageMax = 0, computing = 0, source = 0;
                bool canProduce = false, canRecruit = false;
                if (fac != null)
                {
                    try { factoryHp = fac.HP.GetValue(); } catch { factoryHp = 0; }
                    try { factoryHpMax = fac.HP.GetMaxV(); } catch { factoryHpMax = 0; }
                    try { storage = fac.Storage.GetValue(); } catch { storage = 0; }
                    try { storageMax = fac.Storage.GetMaxV(); } catch { storageMax = 0; }
                    try { computing = fac.ComputingPower.Get(); } catch { computing = 0; }
                    try { source = fac.Source.Get(); } catch { source = 0; }
                    try { canProduce = fac.CanProduce.Get(); } catch { canProduce = false; }
                    try { canRecruit = fac.CanRecruit.Get(); } catch { canRecruit = false; }
                }

                var techKeys = new string[] { "Cost", "Efficiency", "Market", "Robust", "Warrior", "Production", "Storage", "MoveSpeed", "Carry", "Price" };
                var techs = new Dictionary<string, int>(techKeys.Length);
                foreach (var k in techKeys) techs[k] = t.GetTech(k);

                list.Add(new TeamStatus(teamId, t.Score.Get(), factoryHp, factoryHpMax, storage, storageMax, computing, source, techs, canProduce, canRecruit));
            }
            list.Sort((a, b) => a.TeamId.CompareTo(b.TeamId));
            return list;
        }

        /// <summary>
        /// 获取某队工厂当前已存储的货物数量（各类型）。
        /// 返回字典：GoodsType -> 数量。找不到工厂返回空字典。
        /// </summary>
        public IReadOnlyDictionary<Preparation.Utility.GoodsType, int> GetFactoryGoods(long teamId)
        {
            var fac = GetTeamFactory(teamId);
            if (fac == null) return new Dictionary<Preparation.Utility.GoodsType, int>();

            var dict = new Dictionary<Preparation.Utility.GoodsType, int>();
            foreach (Preparation.Utility.GoodsType t in Enum.GetValues(typeof(Preparation.Utility.GoodsType)))
            {
                if (t == Preparation.Utility.GoodsType.NULL_GOODS_TYPE) continue;
                try
                {
                    dict[t] = fac.GetGoods(t);
                }
                catch
                {
                    dict[t] = 0;
                }
            }
            return dict;
        }

        // helper to build CharacterStatus from Character
        private CharacterStatus BuildCharacterStatus(Character ch)
        {
            var pos = ch.Position;

            long hp = 0, hpMax = 0, atk = 0, atkSize = 0, robust = 0, efficiency = 0, view = 0, carry = 0, moveSpeed = 0;
            bool visible = false, isRemoved = false, canMove = false;
            IReadOnlyDictionary<Preparation.Utility.GoodsType, int>? load = null;

            try { hp = ch.HP.GetValue(); } catch { hp = 0; }
            try { hpMax = ch.HP.GetMaxV(); } catch { hpMax = 0; }
            try { atk = ch.AttackPower.GetValue(); } catch { atk = 0; }
            try { atkSize = ch.AttackSize.GetValue(); } catch { atkSize = 0; }
            try { robust = ch.Robust.GetValue(); } catch { robust = 0; }
            try { efficiency = ch.Efficiency.GetValue(); } catch { efficiency = 0; }
            try { view = ch.ViewRange.GetValue(); } catch { view = 0; }
            try { carry = ch.Carry.GetValue(); } catch { carry = 0; }
            try { moveSpeed = ch.MoveSpeed.Get(); } catch { moveSpeed = 0; }
            try { visible = ch.Visible; } catch { visible = false; }
            try { isRemoved = ch.IsRemoved.Get(); } catch { isRemoved = false; }
            try { canMove = ch.CanMove.Get(); } catch { canMove = false; }
            try { load = ch.GoodsLoad.Snapshot(); } catch { load = null; }

            return new CharacterStatus(
                teamId: (long)ch.TeamID.Get(),
                playerId: (long)ch.PlayerID.Get(),
                id: ch.ID,
                position: pos,
                hp: hp,
                hpMax: hpMax,
                attackPower: atk,
                attackSize: atkSize,
                robust: robust,
                efficiency: efficiency,
                viewRange: view,
                carry: carry,
                moveSpeed: moveSpeed,
                characterType: ch.CharacterType,
                visible: visible,
                state: ch.CharacterState,
                isRemoved: isRemoved,
                canMove: canMove,
                goodsLoad: load
            );
        }

        /// <summary>
        /// 列出某队所有角色的状态快照。
        /// </summary>
        public IReadOnlyList<CharacterStatus> GetTeamCharacters(long teamId)
        {
            var list = new List<CharacterStatus>();
            foreach (var ch in characterManager.GetTeamCharacters(teamId))
            {
                if (ch == null) continue;
                var status = BuildCharacterStatus(ch);
                list.Add(status);
            }
            list.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));
            return list;
        }

        /// <summary>
        /// 列出场上所有角色的状态快照（按 teamId then playerId 排序）。
        /// </summary>
        public IReadOnlyList<CharacterStatus> GetAllCharacters()
        {
            var list = new List<CharacterStatus>();
            var seen = new HashSet<long>();
            foreach (var kv in teams)
            {
                long teamId = kv.Key;
                foreach (var ch in characterManager.GetTeamCharacters(teamId))
                {
                    if (ch == null) continue;
                    if (!seen.Add(ch.ID)) continue;
                    list.Add(BuildCharacterStatus(ch));
                }
            }
            list.Sort((a, b) =>
            {
                int c = a.TeamId.CompareTo(b.TeamId);
                if (c != 0) return c;
                return a.PlayerId.CompareTo(b.PlayerId);
            });
            return list;
        }

        /// <summary>
        /// 只读世界快照（示例占位类型，可在其它 partial 中扩展实际字段）。
        /// </summary>
        public readonly struct WorldSnapshot
        {
            public WorldSnapshot(int nowTimeMs, IReadOnlyList<object> objects)
            {
                NowTimeMs = nowTimeMs;
                Objects = objects;
            }
            public int NowTimeMs { get; }
            public IReadOnlyList<object> Objects { get; }
        }

        /// <summary>
        /// 角色状态快照（示例占位类型，可在其它 partial 中扩展实际字段）。
        /// </summary>
        public readonly struct CharacterStatus
        {
            public long TeamId { get; }
            public long PlayerId { get; }
            public long ID { get; }
            public XY Position { get; }
            public long HP { get; }
            public long HPMax { get; }
            public long AttackPower { get; }
            public long AttackSize { get; }
            public long Robust { get; }
            public long Efficiency { get; }
            public long ViewRange { get; }
            public long Carry { get; }
            public long MoveSpeed { get; }
            public Preparation.Utility.CharacterType CharacterType { get; }
            public bool Visible { get; }
            public CharacterState State { get; }
            public bool IsRemoved { get; }
            public bool CanMove { get; }
            public IReadOnlyDictionary<Preparation.Utility.GoodsType, int>? GoodsLoad { get; }

            public CharacterStatus(long teamId, long playerId, long id, XY position, long hp, long hpMax, long attackPower, long attackSize, long robust, long efficiency, long viewRange, long carry, long moveSpeed, Preparation.Utility.CharacterType characterType, bool visible, CharacterState state, bool isRemoved, bool canMove, IReadOnlyDictionary<Preparation.Utility.GoodsType, int>? goodsLoad)
            {
                TeamId = teamId;
                PlayerId = playerId;
                ID = id;
                Position = position;
                HP = hp;
                HPMax = hpMax;
                AttackPower = attackPower;
                AttackSize = attackSize;
                Robust = robust;
                Efficiency = efficiency;
                ViewRange = viewRange;
                Carry = carry;
                MoveSpeed = moveSpeed;
                CharacterType = characterType;
                Visible = visible;
                State = state;
                IsRemoved = isRemoved;
                CanMove = canMove;
                GoodsLoad = goodsLoad;
            }
        }

        /// <summary>
        /// 当前事件快照。
        /// </summary>
        public readonly struct EventStatus
        {
            public string Name { get; }
            public string Description { get; }

            public EventStatus(string name, string description)
            {
                Name = name;
                Description = description;
            }
        }

        /// <summary>
        /// 队伍状态快照（示例占位类型，可在其它 partial 中扩展实际字段）。
        /// </summary>
        public readonly struct TeamStatus
        {
            public long TeamId { get; }
            public long Score { get; }
            public long FactoryHP { get; }
            public long FactoryHPMax { get; }
            public long FactoryStorage { get; }
            public long FactoryStorageMax { get; }
            public long FactoryComputingPower { get; }
            public long FactorySource { get; }
            public IReadOnlyDictionary<string, int> TechLevels { get; }
            public bool FactoryCanProduce { get; }
            public bool FactoryCanRecruit { get; }

            public TeamStatus(long teamId, long score, long factoryHp, long factoryHpMax, long storage, long storageMax, long computing, long source, IReadOnlyDictionary<string, int> techLevels, bool canProduce, bool canRecruit)
            {
                TeamId = teamId;
                Score = score;
                FactoryHP = factoryHp;
                FactoryHPMax = factoryHpMax;
                FactoryStorage = storage;
                FactoryStorageMax = storageMax;
                FactoryComputingPower = computing;
                FactorySource = source;
                TechLevels = techLevels;
                FactoryCanProduce = canProduce;
                FactoryCanRecruit = canRecruit;
            }
        }

        /// <summary>
        /// 获取某队某角色的实时对象（teamId + playerId）。找不到返回 false。
        /// </summary>
        public bool TryGetCharacter(long teamId, long playerId, out Character? character)
        {
            return characterManager.TryGetCharacter(teamId, playerId, out character);
        }

        /// <summary>
        /// 获取某队某角色的当前信息（teamId + playerId）。找不到返回 null。
        /// </summary>
        public CharacterStatus? GetCharacterStatus(long teamId, long playerId)
        {
            if (!characterManager.TryGetCharacter(teamId, playerId, out var ch))
            {
                LogicLogging.logger.LogWarning($"GetCharacterStatus failed: Character for team {teamId} player {playerId} not found.");
                return null;
            }
            if (ch == null)
            {
                LogicLogging.logger.LogWarning($"GetCharacterStatus failed: Character for team {teamId} player {playerId} is null.");
                return null;
            }
            var pos = ch.Position;

            long hp = 0, hpMax = 0, atk = 0, atkSize = 0, robust = 0, efficiency = 0, view = 0, carry = 0, moveSpeed = 0;
            bool visible = false, isRemoved = false, canMove = false;
            IReadOnlyDictionary<Preparation.Utility.GoodsType, int>? load = null;

            try { hp = ch.HP.GetValue(); } catch { hp = 0; }
            try { hpMax = ch.HP.GetMaxV(); } catch { hpMax = 0; }
            try { atk = ch.AttackPower.GetValue(); } catch { atk = 0; }
            try { atkSize = ch.AttackSize.GetValue(); } catch { atkSize = 0; }
            try { robust = ch.Robust.GetValue(); } catch { robust = 0; }
            try { efficiency = ch.Efficiency.GetValue(); } catch { efficiency = 0; }
            try { view = ch.ViewRange.GetValue(); } catch { view = 0; }
            try { carry = ch.Carry.GetValue(); } catch { carry = 0; }
            try { moveSpeed = ch.MoveSpeed.Get(); } catch { moveSpeed = 0; }
            try { visible = ch.Visible; } catch { visible = false; }
            try { isRemoved = ch.IsRemoved.Get(); } catch { isRemoved = false; }
            try { canMove = ch.CanMove.Get(); } catch { canMove = false; }
            try { load = ch.GoodsLoad.Snapshot(); } catch { load = null; }

            return new CharacterStatus(
                teamId: (long)ch.TeamID.Get(),
                playerId: (long)ch.PlayerID.Get(),
                id: ch.ID,
                position: pos,
                hp: hp,
                hpMax: hpMax,
                attackPower: atk,
                attackSize: atkSize,
                robust: robust,
                efficiency: efficiency,
                viewRange: view,
                carry: carry,
                moveSpeed: moveSpeed,
                characterType: ch.CharacterType,
                visible: visible,
                state: ch.CharacterState,
                isRemoved: isRemoved,
                canMove: canMove,
                goodsLoad: load
            );
        }

        /// <summary>
        /// 获取所有算力中心的占领/状态快照列表。
        /// </summary>
        public IReadOnlyList<ComputeCenterStatus> GetAllComputeCenterStatus()
        {
            var list = new List<ComputeCenterStatus>();
            if (gameMap == null) return list;
            if (!gameMap.GameObjDict.TryGetValue(GameObjType.COMPUTE_CENTER, out var locked)) return list;

            var casted = locked.Cast<ComputeCenter>();
            var centers = casted?.ToNewList();
            if (centers == null) return list;

            foreach (var c in centers)
            {
                if (c == null) continue;
                list.Add(new ComputeCenterStatus(
                    id: c.ID,
                    position: c.Position,
                    centerType: c.EComputeCenterType,
                    state: c.EState,
                    isOccupied: c.IsOccupied,
                    occupiedByTeamId: c.OccupiedByTeamId
                ));
            }

            list.Sort((a, b) => a.ID.CompareTo(b.ID));
            return list;
        }

        public readonly struct ComputeCenterStatus
        {
            public long ID { get; }
            public XY Position { get; }
            public ComputeCenterType CenterType { get; }
            public ComputeCenterState State { get; }
            public bool IsOccupied { get; }
            public long OccupiedByTeamId { get; }

            public ComputeCenterStatus(long id, XY position, ComputeCenterType centerType, ComputeCenterState state, bool isOccupied, long occupiedByTeamId)
            {
                ID = id;
                Position = position;
                CenterType = centerType;
                State = state;
                IsOccupied = isOccupied;
                OccupiedByTeamId = occupiedByTeamId;
            }
        }

        /// <summary>
        /// 获取所有资源点的状态快照列表。
        /// </summary>
        public IReadOnlyList<ResourceStatus> GetAllResourceStatus()
        {
            var list = new List<ResourceStatus>();
            if (gameMap == null) return list;
            if (!gameMap.GameObjDict.TryGetValue(GameObjType.RESOURCE, out var locked)) return list;

            var casted = locked.Cast<Resource>();
            var resources = casted?.ToNewList();
            if (resources == null) return list;

            foreach (var r in resources)
            {
                if (r == null) continue;
                long hp = 0, hpMax = 0;
                try { hp = r.HP.GetValue(); } catch { hp = 0; }

                list.Add(new ResourceStatus(
                    id: r.ID,
                    position: r.Position,
                    resourceType: r.ResourceType,
                    state: r.Resourcestate,
                    hp: hp
                ));
            }

            list.Sort((a, b) => a.ID.CompareTo(b.ID));
            return list;
        }

        public readonly struct ResourceStatus
        {
            public long ID { get; }
            public XY Position { get; }
            public ResourceType ResourceType { get; }
            public ResourceState State { get; }
            public long HP { get; }

            public ResourceStatus(long id, XY position, ResourceType resourceType, ResourceState state, long hp)
            {
                ID = id;
                Position = position;
                ResourceType = resourceType;
                State = state;
                HP = hp;
            }
        }

        private readonly object gameEndLock = new();
        private bool gameEnded = false;

        /// <summary>
        /// 自动检测并处理游戏结束条件：
        /// - 超过时间上限（GameData.GameDurationInSecond）
        /// - 至少有 3 个队伍的工厂被摧毁（HP <= 0 或工厂丢失）
        /// 若检测到结束，会做必要的清理（停止计时器、打断工厂生产、移除场上角色等），并返回 true。
        /// 多次调用是幂等的。
        /// </summary>
        public bool CheckAndHandleGameEnd()
        {
            lock (gameEndLock)
            {
                if (gameEnded) return true;

                // 1) 时间判定
                try
                {
                    long now = NowTime();
                    long limit = (long)GameData.GameDurationInSecond * 1000L;
                    if (now >= limit)
                    {
                        // 触发结束
                        goto EndGame;
                    }
                }
                catch
                {
                    // 忽略时间读取异常，继续其他判定
                }

                // 2) 工厂被摧毁判定（统计工厂 HP<=0 或工厂为 null 的队伍数）
                int destroyedCount = 0;
                foreach (var kv in teams)
                {
                    try
                    {
                        var teamId = kv.Key;
                        var fac = GetTeamFactory(teamId);
                        if (fac == null)
                        {
                            destroyedCount++;
                        }
                        else
                        {
                            try
                            {
                                if (fac.HP.GetValue() <= 0) destroyedCount++;
                            }
                            catch
                            {
                                destroyedCount++;
                            }
                        }
                    }
                    catch
                    {
                        destroyedCount++;
                    }
                }
                if (destroyedCount >= 3)
                {
                    goto EndGame;
                }

                return false;

            EndGame:
                // 做必要的清理工作，尽量安全且幂等
                gameEnded = true;
                try
                {
                    // 停止计时器，标记游戏不再进行
                    gameMap?.Timer?.EndGame();
                }
                catch { }

                try
                {
                    // 中断所有工厂的生产/招募能力
                    foreach (var kv in teams)
                    {
                        try
                        {
                            var fac = GetTeamFactory(kv.Key);
                            fac?.Interupt();
                        }
                        catch { }
                    }
                }
                catch { }

                try
                {
                    // 移除/销毁所有角色，释放地图引用
                    foreach (var kv in teams)
                    {
                        long teamId = kv.Key;
                        var chars = characterManager.GetTeamCharacters(teamId)?.ToList() ?? new List<Character>();
                        foreach (var ch in chars)
                        {
                            try
                            {
                                // 标记为死亡并从地图移除
                                characterManager.Destroy(teamId, (long)ch.PlayerID.Get(), CharacterState.DECEASED);
                            }
                            catch { }
                        }
                    }
                }
                catch { }

                // 其它必要的释放/通知可以在这里加入（例如触发事件、记录结算结果等）

                return true;
            }
        }

        /// <summary>
        /// 指定某队某玩家的角色从工厂装载某类货物到自身（尝试装载 amount 个）。
        /// 使用 teamId + playerId 定位操作者。
        /// </summary>
        public bool Load(long teamId, long playerId, Preparation.Utility.GoodsType type, int amount)
        {
            if (!EnsureGameStarted(nameof(Load)))
                return false;

            if (amount <= 0)
            {
                LogicLogging.logger.LogWarning($"Load failed: Invalid amount {amount} for team {teamId} player {playerId}.");
                return false;
            }
            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Load failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null || character.IsRemoved)
            {
                LogicLogging.logger.LogWarning($"Load failed: Character for team {teamId} player {playerId} is null or removed.");
                return false;
            }
            return actionManager.Load(character, type, amount);
        }

        /// <summary>
        /// 让指定队伍的指定玩家的角色立即停止当前所有行动（停止移动/采集/占领/攻击等）。
        /// 使用 teamId + playerId 定位操作者。
        /// </summary>
        public bool Stop(long teamId, long playerId)
        {
            if (!EnsureGameStarted(nameof(Stop)))
                return false;

            if (!characterManager.TryGetCharacter(teamId, playerId, out var character))
            {
                LogicLogging.logger.LogWarning($"Stop failed: Character for team {teamId} player {playerId} not found.");
                return false;
            }
            if (character == null || character.IsRemoved)
            {
                LogicLogging.logger.LogWarning($"Stop failed: Character for team {teamId} player {playerId} is null or removed.");
                return false;
            }
            return ActionManager.Stop(character);
        }
    }
}
