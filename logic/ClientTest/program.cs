using Grpc.Core;
using Protobuf;

// ============================================================================
// 完整流程测试：召唤角色 → 寻路采集 → 工厂生产 → 返厂装载 → 前往市场售卖
//
// 运行前提：服务端需用 --gameTimeInSecond 60（以上）启动，否则默认 10s 不够一轮循环
// 参数：<playerId> <teamId> [characterId]  （characterId 默认 1）
// ============================================================================

namespace ClientTest
{
    public class Program
    {
        private const int CellSize = 1000;
        private const int CellCenter = 500;
        // 到达阈值：300 game-units（< 1 cell），保证落在目标格内
        private const double ArrivalRadius = 300.0;

        // ────────────────────────────────────────────────────────────────────
        // SharedState：由帧读取线程更新，主任务线程只读
        // ────────────────────────────────────────────────────────────────────
        private sealed class SharedState
        {
            private readonly object _lk = new();
            private readonly TaskCompletionSource<bool> _gameStartTcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _charSeenTcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool _hasPos;
            private int _charX, _charY;
            private int _currentLoad;
            private int _material;
            private bool _factoryCanProduce = true;
            // 本队工厂在游戏坐标系中的位置（首帧更新后有效）
            private int _facX = -1, _facY = -1;
            private readonly Dictionary<GoodsType, int> _facGoods = new();

            public Task GameStartTask => _gameStartTcs.Task;
            public Task CharSeenTask => _charSeenTcs.Task;

            public void ApplyFrame(MessageToClient frame, long teamId, long charId)
            {
                if (frame.GameState is GameState.GameStart or GameState.GameRunning)
                    _gameStartTcs.TrySetResult(true);

                // ── 队伍经济 ──────────────────────────────────────────────
                if (frame.AllMessage != null)
                {
                    int idx = (int)teamId - 1;
                    if ((uint)idx < (uint)frame.AllMessage.Teams.Count)
                        lock (_lk) { _material = frame.AllMessage.Teams[idx].Material; }
                }

                foreach (var obj in frame.ObjMessage)
                {
                    // ── 本队工厂 ──────────────────────────────────────────
                    var fac = obj.FactoryMessage;
                    if (fac != null && fac.TeamId == teamId)
                    {
                        lock (_lk)
                        {
                            _factoryCanProduce = fac.CanProduce;
                            _facX = fac.X;
                            _facY = fac.Y;
                            _facGoods.Clear();
                            // GoodsStack 字段：ProductType (GoodsType) + Quantity (int)
                            foreach (var gs in fac.ProductInventory)
                                _facGoods[gs.ProductType] = gs.Quantity;
                        }
                    }

                    // ── 本角色 ────────────────────────────────────────────
                    var ch = obj.CharacterMessage;
                    if (ch != null && ch.TeamId == teamId && ch.PlayerId == charId)
                    {
                        lock (_lk)
                        {
                            _hasPos = true;
                            _charX = ch.X;
                            _charY = ch.Y;
                            _currentLoad = ch.CurrentLoad;
                        }
                        _charSeenTcs.TrySetResult(true);
                    }
                }
            }

            public bool TryGetPos(out int x, out int y)
            { lock (_lk) { x = _charX; y = _charY; return _hasPos; } }

            public int Material { get { lock (_lk) return _material; } }
            public bool FactoryCanProduce { get { lock (_lk) return _factoryCanProduce; } }
            public int CurrentLoad { get { lock (_lk) return _currentLoad; } }

            public bool TryGetFactoryPos(out int x, out int y)
            { lock (_lk) { x = _facX; y = _facY; return _facX >= 0; } }

            public int GetFactoryGoods(GoodsType type)
            { lock (_lk) return _facGoods.GetValueOrDefault(type, 0); }
        }

        // ────────────────────────────────────────────────────────────────────
        // Main：负责连接、注册、清理；游戏逻辑委托给 RunAsync
        // ────────────────────────────────────────────────────────────────────
        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ClientTest <playerId> <teamId> [characterId]");
                return;
            }
            if (!long.TryParse(args[0], out long playerId) ||
                !long.TryParse(args[1], out long teamId))
            {
                Console.WriteLine("Invalid arguments.");
                return;
            }
            long charId = args.Length >= 3 && long.TryParse(args[2], out long cid) ? cid : 1L;

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
                Console.WriteLine($"[EXCEPTION] {ex.Message}");
            }
            finally
            {
                cts.Cancel();
                try { await streamTask; } catch { }
                await channel.ShutdownAsync();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // 核心流程：7 步完整循环
        // ────────────────────────────────────────────────────────────────────
        private static async Task RunAsync(
            AvailableService.AvailableServiceClient client,
            SharedState state,
            CancellationTokenSource cts,
            long teamId, long charId)
        {
            var ct = cts.Token;

            // ── [1] 等待游戏开始 ──────────────────────────────────────────
            Log("Waiting for game start...");
            if (!await TimeoutTask(state.GameStartTask, 30, ct))
            { Fail(cts, "Game start timeout."); return; }

            // ── [2] 召唤角色 ──────────────────────────────────────────────
            // 诊断：CreateCharacterRID 可取回分配的 PlayerId；这里用 CreateCharacter 固定 Id
            Log($"Creating Robot (charId={charId})...");
            var createRes = client.CreateCharacter(new CreateCharacterMsg
            {
                TeamId = teamId,
                PlayerId = charId,
                CharacterType = CharacterType.Robot
            });
            if (!createRes.ActSuccess)
            { Fail(cts, "CreateCharacter failed (工厂可能无 Material 或未开局)."); return; }

            if (!await TimeoutTask(state.CharSeenTask, 10, ct))
            { Fail(cts, "Character not seen in frame within 10s."); return; }
            Log("  Character spawned.");

            // ── [3] 获取地图，寻路到最近资源 ─────────────────────────────
            var map = client.GetMap(new NullRequest());
            Log("Navigating to nearest resource...");
            if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.Resource, ct))
            { Fail(cts, "Failed to reach any resource."); return; }

            // ── [4] 开始采集 ──────────────────────────────────────────────
            // 诊断 A：Harvest 不需要 ResourceId，服务端用 OneForInteract 按位置找最近资源
            // 诊断 B：资源可能在导航途中被耗尽，重试 15 次兜底
            Log("Starting harvest...");
            bool harvesting = false;
            for (int i = 0; i < 15 && !harvesting && !ct.IsCancellationRequested; i++)
            {
                var hr = client.Harvest(new ResourceMsg { TeamId = teamId, PlayerId = charId });
                if (hr.ActSuccess) harvesting = true;
                else await Task.Delay(150, ct);
            }
            if (!harvesting)
            { Fail(cts, "Harvest failed（角色可能不在资源旁边，或资源已耗尽）."); return; }
            Log("  Harvest running.");

            // ── [5] 等待 Material 足够，下达生产命令 ─────────────────────
            // 选择：Semiconductor（成本 10，生产时间 5s，基础价格 80，收益最高）
            const GoodsType Product = GoodsType.Semiconductor;
            const int ProduceCost = 10; // CostSemiconductor

            Log($"Waiting for material >= {ProduceCost}...");
            // 诊断：material 通过 AllMessage.Teams[idx].Material 推送，约 50ms 延迟
            if (!await WaitFor(() => state.Material >= ProduceCost, 90, ct))
            { Fail(cts, $"Material still {state.Material} after 90s."); return; }
            Log($"  Material = {state.Material}. Ready to produce.");

            // ── [6] 工厂生产 ──────────────────────────────────────────────
            // 诊断 C：必须先等 CanProduce=true；游戏一开始 CanProduce 可能为 false
            //         （工厂被攻击后有一段 disable 时间）
            Log("Issuing Produce command...");
            await WaitFor(() => state.FactoryCanProduce, 15, ct);

            var produceRes = client.Produce(new ProduceGoodsMsg
            {
                TeamId = teamId,
                ProductType = Product,
                MaxProduceNum = 1
            });
            Log($"  Produce: {(produceRes.ActSuccess ? "OK" : "FAIL")}");

            if (produceRes.ActSuccess)
            {
                // 诊断 D：Produce 成功后的下一帧 CanProduce 可能仍为 true（旧帧值），
                //         加 150ms 延迟让"繁忙帧"先到达，再等恢复
                await Task.Delay(150, ct);
                bool finished = await WaitFor(() => state.FactoryCanProduce, 30, ct);
                if (!finished)
                    Log("  WARNING: factory still busy after 30s, continuing anyway.");

                // 同时确认仓库里确实有货（FactoryMessage.ProductInventory 已更新）
                await WaitFor(() => state.GetFactoryGoods(Product) >= 1, 10, ct);
                Log($"  Factory inventory: {Product} x{state.GetFactoryGoods(Product)}");
            }

            // ── [7] 停止采集，导航回本队工厂 ─────────────────────────────
            // 诊断 E：EndAllAction 在服务端 ActionLock 内同步将角色状态清为 NULL，
            //         所以 Move 在其后立即到达服务端时不会被 HARVESTING 状态拒绝。
            //         100ms 延迟是额外安全余量。
            Log("Stopping harvest, navigating to own factory...");
            client.EndAllAction(new IDMsg { PlayerId = charId, TeamId = teamId });
            await Task.Delay(100, ct);

            if (!state.TryGetFactoryPos(out int facX, out int facY))
            { Fail(cts, "Factory position not seen in any frame yet."); return; }

            int facRow = facX / CellSize;
            int facCol = facY / CellSize;
            Log($"  Own factory cell: [{facRow}, {facCol}]");

            if (!await NavigateToCell(client, state, map, teamId, charId, facRow, facCol, ct))
            { Fail(cts, "Failed to reach own factory."); return; }

            if (!await WaitFor(() =>
            {
                if (!state.TryGetPos(out int x, out int y)) return false;
                return Math.Abs(x / CellSize - facRow) <= 1 && Math.Abs(y / CellSize - facCol) <= 1;
            }, 8, ct))
            { Fail(cts, "Character is not close enough to own factory for Load."); return; }

            // ── [8] 装载货物 ──────────────────────────────────────────────
            // 诊断 F：Load 用 OneForInteract 找"最近工厂"，不检查团队归属。
            //         若敌方工厂更近，会从敌方工厂扣货（即使为空也会失败）。
            //         通过导航到 TryGetFactoryPos 返回的坐标，保证本队工厂是最近的。
            // 诊断 G：Load 要求 amount > 0，且工厂仓库 >= amount；若生产未完成则失败。
            Log($"Loading {Product} x1 from factory...");
            bool loaded = false;
            for (int i = 0; i < 15 && !loaded && !ct.IsCancellationRequested; i++)
            {
                var lr = client.Load(new LoadMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    ProductType = Product,
                    ProductAmount = 1
                });
                if (lr.ActSuccess) loaded = true;
                else
                {
                    Log($"  Load attempt {i + 1} failed (factory goods={state.GetFactoryGoods(Product)}, load={state.CurrentLoad})");
                    await Task.Delay(200, ct);
                }
            }
            if (!loaded)
            { Fail(cts, "Load failed after 15 attempts."); return; }
            Log($"  Load succeeded. CurrentLoad={state.CurrentLoad}");

            // ── [9] 寻路到最近市场 ────────────────────────────────────────
            Log("Navigating to nearest market...");
            if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.Market, ct))
            { Fail(cts, "Failed to reach any market."); return; }

            // ── [10] 售卖货物 ──────────────────────────────────────────────
            // 诊断 H：Trade 同样用 OneForInteract 找最近市场，ApproachToInteract 检查
            //         格坐标 Chebyshev 距离 ≤ 1（即角色所在格与目标格相邻即可）。
            //         BFS 导航到目标格的相邻格，满足此条件。
            // 诊断 I：市场价格有衰减机制（同类型商品大量交易后价格下降），
            //         Semiconductor 基础价 80，小市场 x1.1 = 88 分；尽早卖出更划算。
            Log($"Selling {Product} x1...");
            bool sold = false;
            for (int i = 0; i < 15 && !sold && !ct.IsCancellationRequested; i++)
            {
                var tr = client.Trade(new TradeMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    ProductType = Product,
                    ProductAmount = 1,
                    IsBuy = false   // 卖出
                });
                if (tr.ActSuccess) sold = true;
                else
                {
                    Log($"  Trade attempt {i + 1} failed");
                    await Task.Delay(200, ct);
                }
            }

            // ── 结果汇总 ──────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════");
            Console.WriteLine($"  Harvest    : OK");
            Console.WriteLine($"  Produce    : {(produceRes.ActSuccess ? "OK" : "FAIL")}");
            Console.WriteLine($"  Load       : {(loaded ? "OK" : "FAIL")}");
            Console.WriteLine($"  Trade/Sell : {(sold ? "OK" : "FAIL")}");
            Console.WriteLine($"  Full cycle : {(produceRes.ActSuccess && loaded && sold ? "PASS ✓" : "FAIL ✗")}");
            Console.WriteLine("══════════════════════════════════════");
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
        // 寻路导航
        // ────────────────────────────────────────────────────────────────────

        // 导航到地图上最近的 targetType 类型格旁边（BFS + 重试）
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
                if (path.Count <= 1) return true;   // 已经到了

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

        // 导航到 (targetRow, targetCol) 格旁边的可行走格（用于精确到达本队工厂）
        private static async Task<bool> NavigateToCell(
            AvailableService.AvailableServiceClient client,
            SharedState state, MessageOfMap map, long teamId, long charId,
            int targetRow, int targetCol, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8 && !ct.IsCancellationRequested; attempt++)
            {
                if (!state.TryGetPos(out int cx, out int cy))
                { await Task.Delay(100, ct); continue; }

                var path = FindPathAdjacentTo(map, cx / CellSize, cy / CellSize, targetRow, targetCol);
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

        // 移动到某个格的中心，带防卡死偏转
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
                // 卡死检测：连续 4 帧未前进则左右偏转
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

        // Space 和 Bush 可行走；Factory/Market/Resource/ComputeCenter/Barrier 不可行走
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

        // 从起点 BFS，找到最近 targetType 旁边的可行走格，返回完整路径
        private static List<(int r, int c)>? FindPathToType(
            MessageOfMap map, int sr, int sc, PlaceType targetType)
        {
            // clearance=1 优先（避免紧贴墙走），失败回退 clearance=0
            return FindPathToType(map, sr, sc, targetType, 1)
                ?? FindPathToType(map, sr, sc, targetType, 0);
        }

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

        // 到指定格 (tr,tc) 相邻格的路径（用于精确导航到本队工厂）
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

            // 如果起点本身不可通行（如站在资源上），也从邻近的可通行格开始扩展
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
            else
            {
                q.Enqueue((sr, sc));
            }

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
        // 通用工具
        // ────────────────────────────────────────────────────────────────────

        private static async Task<bool> WaitFor(Func<bool> cond, int timeoutSec, CancellationToken ct)
        {
            var end = DateTime.UtcNow.AddSeconds(timeoutSec);
            while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
            {
                if (cond()) return true;
                await Task.Delay(120, ct);
            }
            return cond();
        }

        private static async Task<bool> TimeoutTask(Task task, int sec, CancellationToken ct)
        {
            var delay = Task.Delay(TimeSpan.FromSeconds(sec), ct);
            return await Task.WhenAny(task, delay) == task;
        }

        private static void Fail(CancellationTokenSource cts, string msg)
        {
            Console.WriteLine($"[FAIL] {msg}");
            cts.Cancel();
        }

        private static void Log(string msg) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }
}
