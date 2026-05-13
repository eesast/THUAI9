using Grpc.Core;
using Protobuf;

// ============================================================================
// 复杂多角色策略测试（固定分工 + view-range 战斗中断 + 独立推进召唤）
//
//   Robot   (charId=1) : 占领最近 ComputeCenter → 切换到 Load/Sell 循环
//   Car     (charId=2) : 飞向最近 Resource 持续 Harvest
//   Drone   (charId=3) : 持续锁定并攻击最近敌方 Factory
//
// 任意角色视野内出现敌方角色 → 中断当前任务追击至 atk_size(1000) 内攻击；
// 敌人离开视野/死亡后下一 tick 自动恢复主任务。
//
// 启动：dotnet run --project logic/ClientTest2 -- <playerId> <teamId>
// 推荐: --gameTimeInSecond 120 --teamCount 2
// ============================================================================

namespace ClientTest2
{
    public static class Program
    {
        private const int CellSize = 1000;
        private const int CellCenter = 500;
        private const double ArrivalRadius = 300.0;
        private const int CharCost = 50;          // GameData.{Drone,Robot,AutoCar}Cost = 50
        private const int AtkSize = 1000;         // GameData.*ATKsize = 1000
        private const long RobotId = 1;
        private const long CarId = 2;
        private const long DroneId = 3;

        // ────────────────────────────────────────────────────────────────────
        // SharedState：帧读取线程写、所有角色 task 读
        // ────────────────────────────────────────────────────────────────────
        private sealed class SharedState
        {
            private readonly object _lk = new();
            private readonly TaskCompletionSource<bool> _gameStartTcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task GameStartTask => _gameStartTcs.Task;

            // 团队经济
            private int _teamMaterial;
            private long _factoryCp;
            private bool _factoryCanRecruit = true;
            private bool _factoryCanProduce = true;
            private int _factoryHp;
            private int _facX = -1, _facY = -1;
            private readonly Dictionary<GoodsType, int> _facGoods = new();

            // 我方角色：playerId → MyChar
            private readonly Dictionary<long, MyChar> _myChars = new();

            // 敌方角色（每帧重建）
            private readonly List<EnemyChar> _enemies = new();

            // 敌方工厂（每帧重建）
            private readonly List<EnemyFactory> _enemyFactories = new();

            // 算力中心（每帧重建）
            private readonly List<CCInfo> _centers = new();

            public sealed class MyChar
            {
                public CharacterType Type;
                public int X, Y;
                public int Hp;
                public int CurrentLoad;
                public CharacterState State;
                public DateTime LastSeen;
            }

            public sealed class EnemyChar
            {
                public long TeamId, PlayerId;
                public int X, Y, Hp;
                public CharacterType Type;
            }

            public sealed class EnemyFactory
            {
                public long TeamId, FactoryId;
                public int X, Y, Hp;
            }

            public sealed class CCInfo
            {
                public long CenterId;
                public int X, Y;
                public long OwnerTeamId;
                public int OccupyProgress;
            }

            // 活跃资源位置（帧中非 HARVESTED 的资源 cell），Car 寻路用
            private readonly HashSet<(int cellX, int cellY)> _activeResourceCells = new();

            public void ApplyFrame(MessageToClient frame, long teamId)
            {
                if (frame.GameState is GameState.GameStart or GameState.GameRunning)
                    _gameStartTcs.TrySetResult(true);

                if (frame.AllMessage != null)
                {
                    int idx = (int)teamId - 1;
                    if ((uint)idx < (uint)frame.AllMessage.Teams.Count)
                    {
                        lock (_lk) { _teamMaterial = frame.AllMessage.Teams[idx].Material; }
                    }
                }

                lock (_lk)
                {
                    _enemies.Clear();
                    _enemyFactories.Clear();
                    _centers.Clear();
                    _activeResourceCells.Clear();
                }

                var now = DateTime.UtcNow;
                foreach (var obj in frame.ObjMessage)
                {
                    var fac = obj.FactoryMessage;
                    if (fac != null)
                    {
                        if (fac.TeamId == teamId)
                        {
                            lock (_lk)
                            {
                                _factoryCp = fac.ComputingPower;
                                _factoryCanRecruit = fac.CanRecruit;
                                _factoryCanProduce = fac.CanProduce;
                                _factoryHp = fac.Hp;
                                _facX = fac.X;
                                _facY = fac.Y;
                                _facGoods.Clear();
                                foreach (var gs in fac.ProductInventory)
                                    _facGoods[gs.ProductType] = gs.Quantity;
                            }
                        }
                        else if (fac.TeamId is >= 1 and <= 4 && fac.Hp > 0)
                        {
                            lock (_lk)
                            {
                                _enemyFactories.Add(new EnemyFactory
                                {
                                    TeamId = fac.TeamId,
                                    FactoryId = fac.FactoryId,
                                    X = fac.X,
                                    Y = fac.Y,
                                    Hp = fac.Hp
                                });
                            }
                        }
                        continue;
                    }

                    var cc = obj.ComputeCenterMessage;
                    if (cc != null)
                    {
                        lock (_lk)
                        {
                            _centers.Add(new CCInfo
                            {
                                CenterId = cc.CenterId,
                                X = cc.X,
                                Y = cc.Y,
                                OwnerTeamId = cc.OwnerTeamId,
                                OccupyProgress = cc.OccupyProgress
                            });
                        }
                        continue;
                    }

                    var ch = obj.CharacterMessage;
                    if (ch != null)
                    {
                        if (ch.TeamId == teamId)
                        {
                            lock (_lk)
                            {
                                if (!_myChars.TryGetValue(ch.PlayerId, out var mc))
                                {
                                    mc = new MyChar();
                                    _myChars[ch.PlayerId] = mc;
                                }
                                mc.Type = ch.CharacterType;
                                mc.X = ch.X;
                                mc.Y = ch.Y;
                                mc.Hp = ch.Hp;
                                mc.CurrentLoad = ch.CurrentLoad;
                                mc.State = ch.CharacterActiveState;
                                mc.LastSeen = now;
                            }
                        }
                        else
                        {
                            lock (_lk)
                            {
                                _enemies.Add(new EnemyChar
                                {
                                    TeamId = ch.TeamId,
                                    PlayerId = ch.PlayerId,
                                    X = ch.X,
                                    Y = ch.Y,
                                    Hp = ch.Hp,
                                    Type = ch.CharacterType
                                });
                            }
                        }
                        continue;
                    }

                    var res = obj.ResourceMessage;
                    if (res != null)
                    {
                        if (res.ResourceState != Protobuf.ResourceState.Harvested
                            && res.RemainingAmount > 0)
                        {
                            lock (_lk)
                            {
                                _activeResourceCells.Add(
                                    (res.X / CellSize, res.Y / CellSize));
                            }
                        }
                    }
                }
            }

            // ── 读访问器 ────────────────────────────────────────────────
            public int Material { get { lock (_lk) return _teamMaterial; } }
            public long FactoryCp { get { lock (_lk) return _factoryCp; } }
            public bool FactoryCanRecruit { get { lock (_lk) return _factoryCanRecruit; } }
            public bool FactoryCanProduce { get { lock (_lk) return _factoryCanProduce; } }
            public int FactoryHp { get { lock (_lk) return _factoryHp; } }

            public bool TryGetFactoryPos(out int x, out int y)
            { lock (_lk) { x = _facX; y = _facY; return _facX >= 0; } }

            public int GetFactoryGoods(GoodsType type)
            { lock (_lk) return _facGoods.GetValueOrDefault(type, 0); }

            public bool TryGetMyCharPos(long pid, out int x, out int y)
            {
                lock (_lk)
                {
                    if (_myChars.TryGetValue(pid, out var mc))
                    { x = mc.X; y = mc.Y; return true; }
                    x = 0; y = 0; return false;
                }
            }

            public bool TryGetMyChar(long pid, out MyChar mc)
            {
                lock (_lk)
                {
                    if (_myChars.TryGetValue(pid, out var found))
                    { mc = found; return true; }
                    mc = null!; return false;
                }
            }

            public bool IsMyCharAlive(long pid, double withinSec = 1.5)
            {
                lock (_lk)
                {
                    if (!_myChars.TryGetValue(pid, out var mc)) return false;
                    return (DateTime.UtcNow - mc.LastSeen).TotalSeconds <= withinSec
                        && mc.State != CharacterState.Deceased;
                }
            }

            public List<EnemyChar> GetEnemies() { lock (_lk) return _enemies.ToList(); }
            public List<EnemyFactory> GetEnemyFactories() { lock (_lk) return _enemyFactories.ToList(); }
            public List<CCInfo> GetCenters() { lock (_lk) return _centers.ToList(); }
            public HashSet<(int, int)> GetActiveResourceCells() { lock (_lk) return new HashSet<(int, int)>(_activeResourceCells); }
        }

        // ────────────────────────────────────────────────────────────────────
        // Main
        // ────────────────────────────────────────────────────────────────────
        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ClientTest2 <playerId> <teamId>");
                return;
            }
            if (!long.TryParse(args[0], out long playerId) ||
                !long.TryParse(args[1], out long teamId))
            {
                Console.WriteLine("Invalid arguments.");
                return;
            }

            var channel = new Channel("127.0.0.1:8888", ChannelCredentials.Insecure);
            await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(5));
            var client = new AvailableService.AvailableServiceClient(channel);

            var streamCall = client.RegisterFactory(new RegisterFactoryMsg
            {
                PlayerId = playerId,
                TeamId = teamId,
                SideFlag = (int)teamId
            });

            var state = new SharedState();
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var streamTask = ReadStreamAsync(streamCall, state, teamId, cts.Token);

            try
            {
                await Run(client, state, cts, teamId);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPTION] {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                cts.Cancel();
                try { await streamTask; } catch { }
                await channel.ShutdownAsync();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // 主流程编排
        // ────────────────────────────────────────────────────────────────────
        private static async Task Run(
            AvailableService.AvailableServiceClient client,
            SharedState state,
            CancellationTokenSource cts,
            long teamId)
        {
            var ct = cts.Token;

            Log("Waiting for game start...");
            if (!await TimeoutTask(state.GameStartTask, 30, ct))
            { Log("[FAIL] Game start timeout."); return; }

            var map = client.GetMap(new NullRequest());

            // ── 召唤 Robot（CP 初值=100，足够立即召唤）──────────────────
            if (!await SpawnAndAwaitVisible(client, state, teamId, RobotId, CharacterType.Robot, ct))
            { Log("[FAIL] Robot spawn failed."); return; }

            // 启动后台生产协调
            var produceTask = Task.Run(() => ProduceCoordinator(client, state, teamId, ct), ct);

            // 启动 Robot 任务
            var robotTask = Task.Run(() => RobotTask(client, state, map, teamId, ct), ct);

            // 异步等 Car CP 到位再召唤
            var carTask = Task.Run(async () =>
            {
                if (await SpawnWhenAffordable(client, state, teamId, CarId, CharacterType.AutonomousCar, ct))
                    await CarTask(client, state, map, teamId, ct);
            }, ct);

            // 异步等 Drone CP 到位再召唤
            var droneTask = Task.Run(async () =>
            {
                if (await SpawnWhenAffordable(client, state, teamId, DroneId, CharacterType.Drone, ct))
                    await DroneTask(client, state, map, teamId, ct);
            }, ct);

            // 等到 ct 取消（游戏结束 / Ctrl+C）
            await Task.WhenAny(
                Task.WhenAll(robotTask, carTask, droneTask, produceTask),
                Task.Delay(Timeout.Infinite, ct));
        }

        // ────────────────────────────────────────────────────────────────────
        // 召唤 + 等待可见
        // ────────────────────────────────────────────────────────────────────
        private static async Task<bool> SpawnAndAwaitVisible(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, long charId, CharacterType type,
            CancellationToken ct)
        {
            Log($"Creating {type} (charId={charId})...");
            for (int i = 0; i < 20 && !ct.IsCancellationRequested; i++)
            {
                if (state.FactoryCanRecruit)
                {
                    var res = client.CreateCharacter(new CreateCharacterMsg
                    {
                        TeamId = teamId,
                        PlayerId = charId,
                        CharacterType = type
                    });
                    if (res.ActSuccess) break;
                }
                await Task.Delay(200, ct);
            }

            // 等待帧里看到这个 charId
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (state.TryGetMyCharPos(charId, out _, out _))
                {
                    Log($"  {type} (charId={charId}) spawned.");
                    return true;
                }
                await Task.Delay(120, ct);
            }
            return false;
        }

        // 等到 CP 足够 + CanRecruit 后召唤；轮询直到成功或取消
        private static async Task<bool> SpawnWhenAffordable(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, long charId, CharacterType type,
            CancellationToken ct)
        {
            Log($"Waiting for CP >= {CharCost} to recruit {type}...");
            while (!ct.IsCancellationRequested)
            {
                if (state.FactoryCp >= CharCost && state.FactoryCanRecruit)
                {
                    if (await SpawnAndAwaitVisible(client, state, teamId, charId, type, ct))
                        return true;
                    Log($"  {type} spawn attempt failed, retry...");
                }
                await Task.Delay(400, ct);
            }
            return false;
        }

        // ────────────────────────────────────────────────────────────────────
        // Robot：占领 CC → Load/Sell 循环
        // ────────────────────────────────────────────────────────────────────
        private static async Task RobotTask(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, CancellationToken ct)
        {
            Log("[Robot] Phase 1: navigate to nearest unowned ComputeCenter.");
            if (!await NavigateToType(client, state, map, teamId, RobotId, PlaceType.ComputeCenter, ct))
            { Log("[Robot][WARN] Failed to reach ComputeCenter; aborting Robot task."); return; }

            // 占领
            Log("[Robot] Phase 2: occupying ComputeCenter.");
            for (int i = 0; i < 25 && !ct.IsCancellationRequested; i++)
            {
                var or = client.Occupy(new OccupyMsg
                {
                    TeamId = teamId,
                    PlayerId = RobotId,
                    TargetX = 0,
                    TargetY = 0,
                    TargetComputeCenterId = -1
                });
                if (or.ActSuccess) break;
                await Task.Delay(200, ct);
            }
            // 等待我方占领进度完成
            var occDeadline = DateTime.UtcNow.AddSeconds(20);
            bool occupied = false;
            while (DateTime.UtcNow < occDeadline && !ct.IsCancellationRequested)
            {
                if (state.TryGetMyCharPos(RobotId, out int rx, out int ry))
                {
                    foreach (var cc in state.GetCenters())
                    {
                        long dx = cc.X - rx, dy = cc.Y - ry;
                        if (dx * dx + dy * dy < (long)(CellSize * 2) * (CellSize * 2)
                            && cc.OwnerTeamId == teamId)
                        { occupied = true; break; }
                    }
                }
                if (occupied) break;
                await Task.Delay(300, ct);
            }
            Log(occupied ? "[Robot] CC occupied." : "[Robot][WARN] CC occupy uncertain; continuing.");

            // 切到 Load/Sell 循环；先 EndAllAction 清掉 OCUPPYING 状态
            client.EndAllAction(new IDMsg { TeamId = teamId, PlayerId = RobotId });
            await Task.Delay(150, ct);

            const GoodsType Sell = GoodsType.Semiconductor;
            int cycle = 0;
            while (!ct.IsCancellationRequested)
            {
                if (await CombatPriority(client, state, teamId, RobotId, ct)) continue;

                if (state.GetFactoryGoods(Sell) < 1)
                {
                    await Task.Delay(400, ct);
                    continue;
                }

                cycle++;
                Log($"[Robot] Cycle #{cycle}: navigating to factory.");
                if (!state.TryGetFactoryPos(out int fx, out int fy))
                { await Task.Delay(300, ct); continue; }

                if (!await NavigateToCellWithCombat(client, state, map, teamId, RobotId,
                        fx / CellSize, fy / CellSize, ct))
                { Log("[Robot] Navigate to factory failed, retry."); continue; }

                // Load
                bool loaded = false;
                for (int i = 0; i < 12 && !ct.IsCancellationRequested; i++)
                {
                    var lr = client.Load(new LoadMsg
                    {
                        TeamId = teamId,
                        PlayerId = RobotId,
                        ProductType = Sell,
                        ProductAmount = 1
                    });
                    if (lr.ActSuccess) { loaded = true; break; }
                    await Task.Delay(200, ct);
                }
                if (!loaded) { Log("[Robot] Load failed, retry cycle."); await Task.Delay(300, ct); continue; }
                Log($"[Robot] Loaded x1 {Sell}.");

                // 去市场
                if (!await NavigateToTypeWithCombat(client, state, map, teamId, RobotId, PlaceType.Market, ct))
                { Log("[Robot] Navigate to market failed, retry."); continue; }

                // Trade
                bool sold = false;
                for (int i = 0; i < 12 && !ct.IsCancellationRequested; i++)
                {
                    var tr = client.Trade(new TradeMsg
                    {
                        TeamId = teamId,
                        PlayerId = RobotId,
                        ProductType = Sell,
                        ProductAmount = 1,
                        IsBuy = false
                    });
                    if (tr.ActSuccess) { sold = true; break; }
                    await Task.Delay(200, ct);
                }
                Log(sold ? $"[Robot] Sold cycle #{cycle} OK." : "[Robot] Trade failed.");
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Car：去最近资源点持续 Harvest
        // ────────────────────────────────────────────────────────────────────
        private static async Task CarTask(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, CancellationToken ct)
        {
            Log("[Car] Phase 1: navigate to nearest Resource.");
            if (!await NavigateToTypeWithCombat(client, state, map, teamId, CarId, PlaceType.Resource, ct))
            { Log("[Car][WARN] Failed to reach Resource."); return; }

            int harvestCount = 0;
            while (!ct.IsCancellationRequested)
            {
                if (await CombatPriority(client, state, teamId, CarId, ct)) continue;

                var hr = client.Harvest(new ResourceMsg
                {
                    TeamId = teamId,
                    PlayerId = CarId,
                    ResourceId = 0,
                    Amount = 0
                });
                if (hr.ActSuccess)
                {
                    harvestCount++;
                    if (harvestCount % 5 == 1)
                        Log($"[Car] Harvest call OK (count={harvestCount}, material={state.Material}).");
                    await Task.Delay(2000, ct);
                }
                else
                {
                    // 资源耗尽或不在资源旁 → 用帧中活跃资源位置重新寻路
                    Log("[Car] Harvest failed, searching next active resource...");
                    if (!await NavigateToNearestActiveResourceWithCombat(
                            client, state, map, teamId, CarId, ct))
                    { Log("[Car] No reachable active resource, idle."); await Task.Delay(2000, ct); }
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Drone：持续锁定攻击最近敌方 Factory
        // ────────────────────────────────────────────────────────────────────
        private static async Task DroneTask(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, CancellationToken ct)
        {
            Log("[Drone] Phase: hunting enemy factory.");
            int hits = 0;
            while (!ct.IsCancellationRequested)
            {
                if (await CombatPriority(client, state, teamId, DroneId, ct)) continue;

                // 选目标
                if (!state.TryGetMyCharPos(DroneId, out int dx, out int dy))
                { await Task.Delay(150, ct); continue; }

                var enemies = state.GetEnemyFactories();
                if (enemies.Count == 0)
                { await Task.Delay(300, ct); continue; }

                SharedState.EnemyFactory? target = null;
                double bestD = double.MaxValue;
                foreach (var f in enemies)
                {
                    if (f.Hp <= 0) continue;
                    double d = Dist(f.X, f.Y, dx, dy);
                    if (d < bestD) { bestD = d; target = f; }
                }
                if (target == null)
                { await Task.Delay(300, ct); continue; }

                // 范围内：Attack
                // BFS 导航到工厂相邻格的理论距离 = 1000，但 ArrivalRadius=300，
                // 角色可能停在距工厂中心 700~1300 的点。一旦 >1000 就超出 atk range。
                // 所以用 bestD <= AtkSize+200 的宽松阈值发起攻击。
                if (bestD <= AtkSize + 200)
                {
                    var ar = client.Attack(new AttackMsg
                    {
                        TeamId = teamId,
                        PlayerId = DroneId,
                        AttackRange = AtkSize,
                        AttackedPlayerId = -1,
                        AttackedTeamId = target.TeamId
                    });
                    if (ar.ActSuccess)
                    {
                        hits++;
                        if (hits % 5 == 1)
                            Log($"[Drone] Hit factory T{target.TeamId} hp={target.Hp} d={bestD:F0} (hits={hits}).");
                        await Task.Delay(1050, ct);
                    }
                    else
                    {
                        // 可能距离仍然不够，朝工厂直移一步
                        double a = Math.Atan2(target.Y - dy, target.X - dx);
                        client.Move(new MoveMsg { TeamId = teamId, PlayerId = DroneId, TimeInMilliseconds = 200, Angle = a });
                        await Task.Delay(200, ct);
                    }
                }
                else if (bestD <= 3000)
                {
                    // 2~3 格以内：直接朝工厂移动（精度 > BFS 到相邻格再超距循环）
                    double angle = Math.Atan2(target.Y - dy, target.X - dx);
                    client.Move(new MoveMsg { TeamId = teamId, PlayerId = DroneId, TimeInMilliseconds = 200, Angle = angle });
                    await Task.Delay(150, ct);
                }
                else
                {
                    // 远距离：BFS 导航到 target.cell 旁边
                    int tr = target.X / CellSize, tc = target.Y / CellSize;
                    await NavigateToCellWithCombat(client, state, map, teamId, DroneId, tr, tc, ct);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // 战斗中断（任何角色都可调用）
        // 返回 true 表示这个 tick 已处理完毕（调用方 continue 即可）
        // ────────────────────────────────────────────────────────────────────
        private static async Task<bool> CombatPriority(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, long charId, CancellationToken ct)
        {
            if (!state.IsMyCharAlive(charId)) return false;
            if (!state.TryGetMyCharPos(charId, out int sx, out int sy)) return false;
            if (!state.TryGetMyChar(charId, out var me)) return false;

            int viewRange = me.Type switch
            {
                CharacterType.Drone => 7000,
                CharacterType.Robot => 5000,
                CharacterType.AutonomousCar => 5000,
                _ => 5000
            };

            SharedState.EnemyChar? nearest = null;
            double bestD = double.MaxValue;
            foreach (var e in state.GetEnemies())
            {
                double d = Dist(e.X, e.Y, sx, sy);
                if (d < bestD) { bestD = d; nearest = e; }
            }
            if (nearest == null || bestD > viewRange) return false;

            if (bestD <= AtkSize)
            {
                var ar = client.Attack(new AttackMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    AttackRange = AtkSize,
                    AttackedPlayerId = nearest.PlayerId,
                    AttackedTeamId = nearest.TeamId
                });
                if (ar.ActSuccess)
                {
                    Log($"[Combat][char {charId}] hit T{nearest.TeamId}P{nearest.PlayerId} d={bestD:F0} hp={nearest.Hp}");
                    await Task.Delay(1050, ct);
                }
                else
                {
                    await Task.Delay(180, ct);
                }
                return true;
            }
            else
            {
                // 朝敌人移动 200ms
                double angle = Math.Atan2(nearest.Y - sy, nearest.X - sx);
                client.Move(new MoveMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    TimeInMilliseconds = 200,
                    Angle = angle
                });
                await Task.Delay(150, ct);
                return true;
            }
        }

        // 在导航过程中，每个 cell 之间允许战斗中断
        private static async Task<bool> NavigateToTypeWithCombat(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, long charId,
            PlaceType targetType, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 6 && !ct.IsCancellationRequested; attempt++)
            {
                if (await CombatPriority(client, state, teamId, charId, ct)) continue;
                if (!state.TryGetMyCharPos(charId, out int cx, out int cy))
                { await Task.Delay(120, ct); continue; }

                var path = FindPathToType(map, cx / CellSize, cy / CellSize, targetType);
                if (path == null) return false;
                if (path.Count <= 1) return true;

                bool ok = true;
                foreach (var cell in path.Skip(1))
                {
                    if (await CombatPriority(client, state, teamId, charId, ct)) { ok = false; break; }
                    if (!await MoveToCellAsync(client, state, teamId, charId, cell.r, cell.c, ct))
                    { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }

        // 同 NavigateToTypeWithCombat(Resource) 但只考虑活跃（未枯竭）资源
        private static async Task<bool> NavigateToNearestActiveResourceWithCombat(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, long charId,
            CancellationToken ct)
        {
            for (int attempt = 0; attempt < 6 && !ct.IsCancellationRequested; attempt++)
            {
                if (await CombatPriority(client, state, teamId, charId, ct)) continue;
                if (!state.TryGetMyCharPos(charId, out int cx, out int cy))
                { await Task.Delay(120, ct); continue; }

                var actives = state.GetActiveResourceCells();
                if (actives.Count == 0) return false;
                var path = FindPathToNearestActiveResource(map, cx / CellSize, cy / CellSize, actives);
                if (path == null) return false;
                if (path.Count <= 1) return true;

                bool ok = true;
                foreach (var cell in path.Skip(1))
                {
                    if (await CombatPriority(client, state, teamId, charId, ct)) { ok = false; break; }
                    if (!await MoveToCellAsync(client, state, teamId, charId, cell.r, cell.c, ct))
                    { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }

        private static async Task<bool> NavigateToCellWithCombat(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, long charId,
            int tr, int tc, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 6 && !ct.IsCancellationRequested; attempt++)
            {
                if (await CombatPriority(client, state, teamId, charId, ct)) continue;
                if (!state.TryGetMyCharPos(charId, out int cx, out int cy))
                { await Task.Delay(120, ct); continue; }

                var path = FindPathAdjacentTo(map, cx / CellSize, cy / CellSize, tr, tc);
                if (path == null) return false;
                if (path.Count <= 1) return true;

                bool ok = true;
                foreach (var cell in path.Skip(1))
                {
                    if (await CombatPriority(client, state, teamId, charId, ct)) { ok = false; break; }
                    if (!await MoveToCellAsync(client, state, teamId, charId, cell.r, cell.c, ct))
                    { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }

        // 不带战斗的导航（Robot 占领之前用，避免战斗打断占领前的部署）
        private static async Task<bool> NavigateToType(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, long charId,
            PlaceType targetType, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 6 && !ct.IsCancellationRequested; attempt++)
            {
                if (!state.TryGetMyCharPos(charId, out int cx, out int cy))
                { await Task.Delay(120, ct); continue; }

                var path = FindPathToType(map, cx / CellSize, cy / CellSize, targetType);
                if (path == null) return false;
                if (path.Count <= 1) return true;

                bool ok = true;
                foreach (var cell in path.Skip(1))
                {
                    if (!await MoveToCellAsync(client, state, teamId, charId, cell.r, cell.c, ct))
                    { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }

        // ────────────────────────────────────────────────────────────────────
        // 后台生产协调：material 攒够就触发 Produce(Semiconductor)
        // ────────────────────────────────────────────────────────────────────
        private static async Task ProduceCoordinator(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, CancellationToken ct)
        {
            const GoodsType Product = GoodsType.Semiconductor;
            const int Cost = 10; // GameData.CostSemiconductor
            int produced = 0;

            while (!ct.IsCancellationRequested)
            {
                if (state.Material >= Cost && state.FactoryCanProduce
                    && state.GetFactoryGoods(Product) < 5)  // 留点容量给销售
                {
                    var pr = client.Produce(new ProduceGoodsMsg
                    {
                        TeamId = teamId,
                        ProductType = Product,
                        MaxProduceNum = 1
                    });
                    if (pr.ActSuccess)
                    {
                        produced++;
                        Log($"[Produce] Issued #{produced} (material={state.Material}).");
                        // 等到生产完成（CanProduce 重新为 true）
                        var deadline = DateTime.UtcNow.AddSeconds(15);
                        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                        {
                            await Task.Delay(300, ct);
                            if (state.FactoryCanProduce) break;
                        }
                    }
                    else
                    {
                        await Task.Delay(500, ct);
                    }
                }
                else
                {
                    await Task.Delay(500, ct);
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // 移动到 cell 中心（带反卡死偏转）
        // ────────────────────────────────────────────────────────────────────
        private static async Task<bool> MoveToCellAsync(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, long charId, int row, int col, CancellationToken ct)
        {
            int tx = row * CellSize + CellCenter;
            int ty = col * CellSize + CellCenter;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            double lastDis = double.MaxValue;
            int stall = 0;

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (!state.TryGetMyCharPos(charId, out int cx, out int cy))
                { await Task.Delay(60, ct); continue; }

                double dx = tx - cx, dy = ty - cy;
                double dis = Math.Sqrt(dx * dx + dy * dy);
                if (dis <= ArrivalRadius) return true;

                double angle = Math.Atan2(dy, dx);
                stall = dis >= lastDis - 20 ? stall + 1 : 0;
                if (stall >= 4) angle += (stall / 4) % 2 == 0 ? 0.35 : -0.35;

                client.Move(new MoveMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    TimeInMilliseconds = 200,
                    Angle = angle
                });
                lastDis = dis;
                await Task.Delay(120, ct);
            }
            return false;
        }

        // ────────────────────────────────────────────────────────────────────
        // BFS 寻路
        // ────────────────────────────────────────────────────────────────────
        private static bool IsPassable(PlaceType p) =>
            p is PlaceType.Space or PlaceType.Bush;

        private static bool IsTraversable(MessageOfMap map, int r, int c, int clearance)
        {
            int h = map.Rows.Count, w = map.Rows[0].Cols.Count;
            if ((uint)r >= (uint)h || (uint)c >= (uint)w) return false;
            if (!IsPassable(map.Rows[r].Cols[c])) return false;
            for (int dr = -clearance; dr <= clearance; dr++)
                for (int dc = -clearance; dc <= clearance; dc++)
                {
                    int nr = r + dr, nc = c + dc;
                    if ((uint)nr < (uint)h && (uint)nc < (uint)w &&
                        !IsPassable(map.Rows[nr].Cols[nc])) return false;
                }
            return true;
        }

        private static List<(int r, int c)>? FindPathToType(
            MessageOfMap map, int sr, int sc, PlaceType targetType)
            => FindPathToType(map, sr, sc, targetType, 1) ?? FindPathToType(map, sr, sc, targetType, 0);

        private static List<(int r, int c)>? FindPathToType(
            MessageOfMap map, int sr, int sc, PlaceType targetType, int clearance)
        {
            int h = map.Rows.Count;
            if (h == 0) return null;
            int w = map.Rows[0].Cols.Count;
            if ((uint)sr >= (uint)h || (uint)sc >= (uint)w) return null;

            var (dist, prevR, prevC) = BfsFrom(map, sr, sc, h, w, clearance);

            int[] dr = [-1, 1, 0, 0], dc = [0, 0, -1, 1];
            (int r, int c)? best = null;
            int bestD = int.MaxValue;
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    if (map.Rows[r].Cols[c] != targetType) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int ar = r + dr[k], ac = c + dc[k];
                        if ((uint)ar >= (uint)h || (uint)ac >= (uint)w) continue;
                        if (dist[ar, ac] < 0 || dist[ar, ac] >= bestD) continue;
                        bestD = dist[ar, ac]; best = (ar, ac);
                    }
                }
            return best == null ? null : Reconstruct(best.Value, sr, sc, prevR, prevC);
        }

        // 同 FindPathToType(Resource) 但跳过不在 activeCells 中的资源格
        private static List<(int r, int c)>? FindPathToNearestActiveResource(
            MessageOfMap map, int sr, int sc, HashSet<(int, int)> activeCells)
            => FindPathToNearestActiveResource(map, sr, sc, activeCells, 0);

        private static List<(int r, int c)>? FindPathToNearestActiveResource(
            MessageOfMap map, int sr, int sc, HashSet<(int, int)> activeCells, int clearance)
        {
            int h = map.Rows.Count;
            if (h == 0) return null;
            int w = map.Rows[0].Cols.Count;
            if ((uint)sr >= (uint)h || (uint)sc >= (uint)w) return null;

            var (dist, prevR, prevC) = BfsFrom(map, sr, sc, h, w, clearance);

            int[] dr = [-1, 1, 0, 0], dc = [0, 0, -1, 1];
            (int r, int c)? best = null;
            int bestD = int.MaxValue;
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    if (map.Rows[r].Cols[c] != PlaceType.Resource) continue;
                    if (!activeCells.Contains((r, c))) continue;   // 跳过已枯竭的资源
                    for (int k = 0; k < 4; k++)
                    {
                        int ar = r + dr[k], ac = c + dc[k];
                        if ((uint)ar >= (uint)h || (uint)ac >= (uint)w) continue;
                        if (dist[ar, ac] < 0 || dist[ar, ac] >= bestD) continue;
                        bestD = dist[ar, ac]; best = (ar, ac);
                    }
                }
            return best == null ? null : Reconstruct(best.Value, sr, sc, prevR, prevC);
        }

        private static List<(int r, int c)>? FindPathAdjacentTo(
            MessageOfMap map, int sr, int sc, int tr, int tc)
        {
            int h = map.Rows.Count;
            if (h == 0) return null;
            int w = map.Rows[0].Cols.Count;

            var (dist, prevR, prevC) = BfsFrom(map, sr, sc, h, w, clearance: 0);
            int[] dr = [-1, 1, 0, 0], dc = [0, 0, -1, 1];
            (int r, int c)? best = null;
            int bestD = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int ar = tr + dr[k], ac = tc + dc[k];
                if ((uint)ar >= (uint)h || (uint)ac >= (uint)w) continue;
                if (dist[ar, ac] < 0 || dist[ar, ac] >= bestD) continue;
                bestD = dist[ar, ac]; best = (ar, ac);
            }
            return best == null ? null : Reconstruct(best.Value, sr, sc, prevR, prevC);
        }

        private static (int[,] dist, int[,] prevR, int[,] prevC) BfsFrom(
            MessageOfMap map, int sr, int sc, int h, int w, int clearance)
        {
            var dist = new int[h, w];
            var prevR = new int[h, w];
            var prevC = new int[h, w];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                { dist[r, c] = -1; prevR[r, c] = prevC[r, c] = -1; }

            dist[sr, sc] = 0; prevR[sr, sc] = sr; prevC[sr, sc] = sc;
            var q = new Queue<(int, int)>();
            int[] dr = [-1, 1, 0, 0], dcc = [0, 0, -1, 1];

            if (!IsTraversable(map, sr, sc, clearance))
            {
                for (int k = 0; k < 4; k++)
                {
                    int nr = sr + dr[k], nc = sc + dcc[k];
                    if ((uint)nr >= (uint)h || (uint)nc >= (uint)w) continue;
                    if (IsTraversable(map, nr, nc, clearance))
                    {
                        dist[nr, nc] = 1;
                        prevR[nr, nc] = sr;
                        prevC[nr, nc] = sc;
                        q.Enqueue((nr, nc));
                    }
                }
            }
            else q.Enqueue((sr, sc));

            while (q.Count > 0)
            {
                var (r, c) = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nr = r + dr[k], nc = c + dcc[k];
                    if ((uint)nr >= (uint)h || (uint)nc >= (uint)w) continue;
                    if (dist[nr, nc] >= 0) continue;
                    if (!IsTraversable(map, nr, nc, clearance)) continue;
                    dist[nr, nc] = dist[r, c] + 1;
                    prevR[nr, nc] = r; prevC[nr, nc] = c;
                    q.Enqueue((nr, nc));
                }
            }
            return (dist, prevR, prevC);
        }

        private static List<(int r, int c)> Reconstruct(
            (int r, int c) end, int sr, int sc, int[,] prevR, int[,] prevC)
        {
            var path = new List<(int, int)>();
            var cur = end;
            while (!(cur.r == sr && cur.c == sc))
            {
                path.Add(cur);
                int pr = prevR[cur.r, cur.c], pc = prevC[cur.r, cur.c];
                if (pr < 0) return [];
                cur = (pr, pc);
            }
            path.Add((sr, sc));
            path.Reverse();
            return path;
        }

        // ────────────────────────────────────────────────────────────────────
        // 流式帧读取 + 杂项
        // ────────────────────────────────────────────────────────────────────
        private static async Task ReadStreamAsync(
            AsyncServerStreamingCall<MessageToClient> call,
            SharedState state, long teamId, CancellationToken ct)
        {
            try
            {
                while (await call.ResponseStream.MoveNext(ct))
                    state.ApplyFrame(call.ResponseStream.Current, teamId);
            }
            catch (RpcException) { }
            catch (OperationCanceledException) { }
        }

        private static async Task<bool> TimeoutTask(Task task, int sec, CancellationToken ct)
        {
            var delay = Task.Delay(TimeSpan.FromSeconds(sec), ct);
            return await Task.WhenAny(task, delay) == task;
        }

        private static double Dist(int x1, int y1, int x2, int y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void Log(string msg) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }
}
