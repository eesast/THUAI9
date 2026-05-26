using Grpc.Core;
using Protobuf;

// ============================================================================
// 价格科技测试：采资源 → 生产2 Food → 装载 → 卖1 → 升级售价科技 → 卖1
//
// 运行：dotnet run --project logic/ClientTest -- <playerId> <teamId>
// ============================================================================

namespace ClientTest
{
    public class Program
    {
        private const int CellSize = 1000;
        private const int CellCenter = 500;
        private const double ArrivalRadius = 300.0;
        private const GoodsType Food = GoodsType.Food;

        private sealed class SharedState
        {
            private readonly object _lk = new();
            private readonly TaskCompletionSource<bool> _gameStartTcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _charSeenTcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool _hasPos;
            private int _charX, _charY;
            private long _computingPower;
            private long _teamScore;
            private int _facX = -1, _facY = -1;
            private int _material;
            private readonly Dictionary<GoodsType, int> _facGoods = new();
            private bool _factoryCanProduce = true;

            public Task GameStartTask => _gameStartTcs.Task;
            public Task CharSeenTask => _charSeenTcs.Task;

            public void ApplyFrame(MessageToClient frame, long teamId, long charId)
            {
                if (frame.GameState is GameState.GameStart or GameState.GameRunning)
                    _gameStartTcs.TrySetResult(true);

                foreach (var obj in frame.ObjMessage)
                {
                    var fac = obj.FactoryMessage;
                    if (fac != null && fac.TeamId == teamId)
                    {
                        lock (_lk)
                        {
                            _computingPower = fac.ComputingPower;
                            _facX = fac.X;
                            _facY = fac.Y;
                            _factoryCanProduce = fac.CanProduce;
                            _facGoods.Clear();
                            foreach (var gs in fac.ProductInventory)
                                _facGoods[gs.ProductType] = gs.Quantity;
                        }
                    }

                    var ch = obj.CharacterMessage;
                    if (ch != null && ch.TeamId == teamId && ch.PlayerId == charId)
                    {
                        lock (_lk)
                        {
                            _hasPos = true;
                            _charX = ch.X;
                            _charY = ch.Y;
                        }
                        _charSeenTcs.TrySetResult(true);
                    }
                }

                if (frame.AllMessage != null)
                {
                    int idx = (int)teamId - 1;
                    if ((uint)idx < (uint)frame.AllMessage.Teams.Count)
                    {
                        lock (_lk)
                        {
                            _material = frame.AllMessage.Teams[idx].Material;
                            _teamScore = frame.AllMessage.Teams[idx].Score;
                        }
                    }
                }
            }

            public bool TryGetPos(out int x, out int y)
            { lock (_lk) { x = _charX; y = _charY; return _hasPos; } }

            public long ComputingPower { get { lock (_lk) return _computingPower; } }
            public long TeamScore { get { lock (_lk) return _teamScore; } }
            public int Material { get { lock (_lk) return _material; } }

            public bool TryGetFactoryPos(out int x, out int y)
            { lock (_lk) { x = _facX; y = _facY; return _facX >= 0; } }

            public int GetFactoryGoods(GoodsType type)
            { lock (_lk) return _facGoods.GetValueOrDefault(type, 0); }

            public bool FactoryCanProduce { get { lock (_lk) return _factoryCanProduce; } }
        }

        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ClientTest <playerId> <teamId>");
                return;
            }
            if (!long.TryParse(args[0], out long playerId) ||
                !long.TryParse(args[1], out long teamId))
            {
                Console.WriteLine("Invalid arguments.");
                return;
            }
            long charId = 1;

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
            var streamTask = ReadStreamAsync(streamCall, state, teamId, charId, cts.Token);

            try
            {
                await RunAsync(client, state, cts, teamId, charId);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPTION] {ex}");
            }
            finally
            {
                cts.Cancel();
                try { await streamTask; } catch { }
                await channel.ShutdownAsync();
            }
        }

        private static async Task RunAsync(
            AvailableService.AvailableServiceClient client,
            SharedState state,
            CancellationTokenSource cts,
            long teamId, long charId)
        {
            var ct = cts.Token;

            // [1] 等待游戏开始
            Log("Waiting for game start...");
            if (!await TimeoutTask(state.GameStartTask, 30, ct))
            { Log("[FAIL] Game start timeout."); return; }
            Log("[OK] Game started.");

            // [2] 召唤角色
            Log($"Creating Robot (charId={charId})...");
            var createRes = client.CreateCharacter(new CreateCharacterMsg
            {
                TeamId = teamId,
                PlayerId = charId,
                CharacterType = CharacterType.Robot
            });
            if (!createRes.ActSuccess)
            { Log("[FAIL] CreateCharacter failed."); return; }

            if (!await TimeoutTask(state.CharSeenTask, 10, ct))
            { Log("[FAIL] Character not seen in frame."); return; }
            Log("[OK] Character spawned.");

            // [3] 获取地图
            var map = client.GetMap(new NullRequest());

            // [4] 寻路到最近资源并采集
            Log("Navigating to nearest Resource...");
            if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.Resource, ct))
            { Log("[FAIL] Failed to reach Resource."); return; }
            Log("[OK] Arrived at Resource.");

            // 持续采集直到 material 足够生产 2 个 Food
            Log("Harvesting resources...");
            int targetMaterial = 10 * 2; // Food cost = 10 each (CostFood), need 20 total
            var harvestDeadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < harvestDeadline && !ct.IsCancellationRequested)
            {
                if (state.Material >= targetMaterial) break;

                var hr = client.Harvest(new ResourceMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    ResourceId = 0,
                    Amount = 0
                });
                await Task.Delay(600, ct);
            }
            Log($"Material after harvest: {state.Material} (target {targetMaterial})");

            if (state.Material < targetMaterial)
            { Log("[FAIL] Not enough material."); return; }

            // [5] 生产 2 单位 Food（每单位需等上一次生产完成）
            Log("Producing 2x Food at factory...");
            for (int i = 0; i < 2 && !ct.IsCancellationRequested; i++)
            {
                // 等工厂空闲（CanProduce 由流异步更新，Produce 后需给帧时间）
                await WaitFor(refreshMs: 400, timeoutSec: 12, ct,
                    () => state.FactoryCanProduce,
                    desc: $"CanProduce before produce #{i + 1}");

                var pr = client.Produce(new ProduceGoodsMsg
                {
                    TeamId = teamId,
                    ProductType = Food,
                    MaxProduceNum = 1
                });
                Log($"  Produce #{i + 1}: {(pr.ActSuccess ? "OK" : "FAIL")}");

                if (!pr.ActSuccess) continue;

                // 等生产完成 + 流推送库存更新
                await WaitFor(refreshMs: 400, timeoutSec: 15, ct,
                    () => state.FactoryCanProduce && state.GetFactoryGoods(Food) > i,
                    desc: $"produce #{i + 1} complete");
            }
            Log($"Factory Food stock: {state.GetFactoryGoods(Food)}, CP: {state.ComputingPower}");

            // [6] 导航回工厂装载 2 单位 Food
            Log("Navigating to Factory...");
            if (!state.TryGetFactoryPos(out int fx, out int fy))
            { Log("[FAIL] Factory position unknown."); return; }
            if (!await NavigateToCell(client, state, teamId, charId, fx / CellSize, fy / CellSize, map, ct))
            { Log("[FAIL] Failed to reach Factory."); return; }
            Log("[OK] Arrived at Factory.");

            Log("Loading 2x Food...");
            for (int i = 0; i < 2 && !ct.IsCancellationRequested; i++)
            {
                var lr = client.Load(new LoadMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    ProductType = Food,
                    ProductAmount = 1
                });
                Log($"  Load #{i + 1}: {(lr.ActSuccess ? "OK" : "FAIL")}");
                await Task.Delay(200, ct);
            }

            // [7] 寻路到最近市场
            Log("Navigating to nearest Market...");
            if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.Market, ct))
            { Log("[FAIL] Failed to reach Market."); return; }
            Log("[OK] Arrived at Market.");

            // [8] 卖出 1 单位 Food（升级前基准价，看 Score 增长）
            long scoreBefore1 = state.TeamScore;
            Log($"Selling 1x Food (before price upgrade), Score before: {scoreBefore1}...");
            var tr1 = client.Trade(new TradeMsg
            {
                TeamId = teamId,
                PlayerId = charId,
                ProductType = Food,
                ProductAmount = 1,
                IsBuy = false
            });
            await Task.Delay(500, ct);
            long scoreAfter1 = state.TeamScore;
            long sell1Gain = scoreAfter1 - scoreBefore1;
            Log($"  Sell #1: {(tr1.ActSuccess ? "OK" : "FAIL")}, Score +{sell1Gain} (CP={state.ComputingPower})");

            // [9] 攒够 80 CP 来升级（Trade 加的是 Score 不是 CP，CP 靠工厂自然生成）
            if (state.ComputingPower < 80)
            {
                Log($"CP ({state.ComputingPower}) < 80, waiting for factory CP generation...");
                await GrindCP(client, state, teamId, charId, map, ct);
            }

            await Task.Delay(400, ct);
            long cpBeforeUpgrade = state.ComputingPower;
            Log($"CP before upgrade: {cpBeforeUpgrade}");

            // [10] 升级 INCREASE_PRICE 科技
            Log("Upgrading INCREASE_PRICE tech (cost 80)...");
            var upRes = client.UplevelTech(new UplevelTechMsg
            {
                TeamId = teamId,
                TechType = TechType.IncreasePrice
            });
            await Task.Delay(500, ct);
            long cpAfterUpgrade = state.ComputingPower;
            Log($"  Upgrade: {(upRes.ActSuccess ? "OK" : "FAIL")}, CP after: {cpAfterUpgrade}");

            // [11] 再卖出 1 单位 Food（确保在市场旁，看 Score 增长）
            long scoreBefore2 = state.TeamScore;
            Log($"Selling 1x Food (after price upgrade), Score before: {scoreBefore2}...");
            if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.Market, ct))
            { Log("[FAIL] Lost market."); return; }
            var tr2 = client.Trade(new TradeMsg
            {
                TeamId = teamId,
                PlayerId = charId,
                ProductType = Food,
                ProductAmount = 1,
                IsBuy = false
            });
            await Task.Delay(500, ct);
            long scoreAfter2 = state.TeamScore;
            long sell2Gain = scoreAfter2 - scoreBefore2;
            Log($"  Sell #2: {(tr2.ActSuccess ? "OK" : "FAIL")}, Score +{sell2Gain} (CP={state.ComputingPower})");

            if (tr1.ActSuccess && tr2.ActSuccess)
            {
                Log($"  Sell #1 gain: {sell1Gain}, Sell #2 gain: {sell2Gain}");
                Log($"  Price tech effect: {(sell2Gain > sell1Gain ? $"+{sell2Gain - sell1Gain} ({(double)sell2Gain / sell1Gain:F2}x)" : "no change")}");
            }

            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════");
            Console.WriteLine("  Price tech test complete.");
            Console.WriteLine("══════════════════════════════════════════════");
        }

        // ────────────────────────────────────────────────────────────────────
        // 流式帧读取
        // ────────────────────────────────────────────────────────────────────
        private static async Task ReadStreamAsync(
            AsyncServerStreamingCall<MessageToClient> call,
            SharedState state, long teamId, long charId, CancellationToken ct)
        {
            try
            {
                while (await call.ResponseStream.MoveNext(ct))
                    state.ApplyFrame(call.ResponseStream.Current, teamId, charId);
            }
            catch (RpcException) { }
            catch (OperationCanceledException) { }
        }

        // ────────────────────────────────────────────────────────────────────
        // 导航
        // ────────────────────────────────────────────────────────────────────
        private static async Task<bool> NavigateToType(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, long charId,
            PlaceType targetType, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8 && !ct.IsCancellationRequested; attempt++)
            {
                if (!state.TryGetPos(out int cx, out int cy))
                { await Task.Delay(100, ct); continue; }

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

        private static async Task<bool> NavigateToCell(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, long charId,
            int row, int col, MessageOfMap map, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8 && !ct.IsCancellationRequested; attempt++)
            {
                if (!state.TryGetPos(out int cx, out int cy))
                { await Task.Delay(100, ct); continue; }

                var path = FindPathToCell(map, cx / CellSize, cy / CellSize, row, col);
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

        private static async Task<bool> MoveToCellAsync(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, long charId,
            int row, int col, CancellationToken ct)
        {
            int tx = row * CellSize + CellCenter;
            int ty = col * CellSize + CellCenter;
            var deadline = DateTime.UtcNow.AddSeconds(12);
            double lastDis = double.MaxValue;
            int stall = 0;

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (!state.TryGetPos(out int cx, out int cy))
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
        // BFS
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
            => FindPathToType(map, sr, sc, targetType, 1)
                ?? FindPathToType(map, sr, sc, targetType, 0);

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

        private static List<(int r, int c)>? FindPathToCell(
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
        // 攒钱辅助：回到工厂 → 生产 Food → 装货 → 到市场卖 → 循环直到 CP 达标
        // ────────────────────────────────────────────────────────────────────
        private static async Task GrindCP(
            AvailableService.AvailableServiceClient client,
            SharedState state, long teamId, long charId,
            MessageOfMap map, CancellationToken ct)
        {
            const int targetCP = 80;
            for (int round = 0; round < 10 && !ct.IsCancellationRequested; round++)
            {
                if (state.ComputingPower >= targetCP) break;

                // 回工厂
                if (!state.TryGetFactoryPos(out int fx, out int fy)) break;
                if (!await NavigateToCell(client, state, teamId, charId, fx / CellSize, fy / CellSize, map, ct))
                { Log("[GrindCP] Can't reach factory."); break; }

                // 等空闲 + 生产
                await WaitFor(refreshMs: 400, timeoutSec: 12, ct,
                    () => state.FactoryCanProduce,
                    desc: "CanProduce for grind");
                client.Produce(new ProduceGoodsMsg { TeamId = teamId, ProductType = Food, MaxProduceNum = 1 });

                // 等生产完成
                await Task.Delay(2500, ct);
                await WaitFor(refreshMs: 400, timeoutSec: 10, ct,
                    () => state.GetFactoryGoods(Food) >= 1,
                    desc: "grind produce complete");

                if (state.GetFactoryGoods(Food) < 1) continue;

                // 装货
                client.Load(new LoadMsg { TeamId = teamId, PlayerId = charId, ProductType = Food, ProductAmount = 1 });
                await Task.Delay(300, ct);

                // 去市场卖
                if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.Market, ct)) break;
                client.Trade(new TradeMsg { TeamId = teamId, PlayerId = charId, ProductType = Food, ProductAmount = 1, IsBuy = false });
                await Task.Delay(500, ct); // 等流推送 CP
                Log($"[GrindCP] Round {round + 1}: CP={state.ComputingPower}, Score={state.TeamScore}");
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // 工具
        // ────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 等待 condition 为 true，每次检查前 sleep refreshMs 让流推送新帧。
        /// </summary>
        private static async Task<bool> WaitFor(
            int refreshMs, int timeoutSec, CancellationToken ct,
            Func<bool> condition, string desc)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (condition()) return true;
                await Task.Delay(refreshMs, ct);
            }
            Log($"[WaitFor] Timeout waiting for: {desc}");
            return false;
        }

        private static async Task<bool> TimeoutTask(Task task, int sec, CancellationToken ct)
        {
            var delay = Task.Delay(TimeSpan.FromSeconds(sec), ct);
            return await Task.WhenAny(task, delay) == task;
        }

        private static void Log(string msg) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }
}
