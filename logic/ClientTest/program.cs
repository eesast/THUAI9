using Grpc.Core;
using Protobuf;

// ============================================================================
// 科技升级测试：生成角色 → 占领算力中心 → 逐个升级所有科技
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
                        lock (_lk) { _computingPower = fac.ComputingPower; }
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
            }

            public bool TryGetPos(out int x, out int y)
            { lock (_lk) { x = _charX; y = _charY; return _hasPos; } }

            public long ComputingPower { get { lock (_lk) return _computingPower; } }
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
                Console.WriteLine($"[EXCEPTION] {ex.Message}");
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
            { Log("[FAIL] Character not seen in frame within 10s."); return; }
            Log("[OK] Character spawned.");

            // [3] 获取地图，寻路到最近算力中心
            var map = client.GetMap(new NullRequest());
            Log("Navigating to nearest ComputeCenter...");
            if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.ComputeCenter, ct))
            { Log("[FAIL] Failed to reach any ComputeCenter."); return; }
            Log("[OK] Arrived at ComputeCenter.");

            // [4] 占领算力中心
            Log("Occupying ComputeCenter...");
            bool occupied = false;
            for (int i = 0; i < 30 && !ct.IsCancellationRequested; i++)
            {
                var or = client.Occupy(new OccupyMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    TargetX = 0,
                    TargetY = 0,
                    TargetComputeCenterId = -1
                });
                if (or.ActSuccess)
                {
                    Log($"[OK] Occupy started (attempt {i + 1}).");
                    occupied = true;
                    break;
                }
                await Task.Delay(500, ct);
            }
            if (!occupied)
            { Log("[FAIL] Occupy failed."); return; }

            // [5] 等待算力积累（占领后每秒 +CP）
            Log("Waiting for Computing Power to accumulate...");
            await Task.Delay(3000, ct);
            Log($"Current CP = {state.ComputingPower}");

            // [6] 逐个升级所有科技
            // TechType 枚举值和成本:
            var allTechs = new (TechType type, string name, int cost)[]
            {
                (TechType.IncreaseHp,         "INCREASE_HP",          30),
                (TechType.IncreaseRobust,     "INCREASE_ROBUST",      30),
                (TechType.IncreaseAttackPower,"INCREASE_ATTACK_POWER",60),
                (TechType.IncreaseAttackSize, "INCREASE_ATTACK_SIZE", 60),
                (TechType.IncreaseMoveSpeed,  "INCREASE_MOVE_SPEED",  40),
                (TechType.IncreaseCarryCapacity,"INCREASE_CARRY_CAPACITY",50),
                (TechType.IncreaseEfficiency, "INCREASE_EFFICIENCY",  40),
                (TechType.IncreaseProduction, "INCREASE_PRODUCTION",  60),
                (TechType.IncreaseStorage,    "INCREASE_STORAGE",     50),
                (TechType.IncreasePrice,      "INCREASE_PRICE",       80),
                (TechType.DecreaseCost,        "DECREASE_COST",       50),
            };

            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════");
            Console.WriteLine("  Tech Upgrade Test — Costs and Results");
            Console.WriteLine("══════════════════════════════════════════════");

            foreach (var (type, name, cost) in allTechs)
            {
                if (ct.IsCancellationRequested) break;

                long cpBefore = state.ComputingPower;
                Console.WriteLine();
                Console.WriteLine($"--- {name} ---");
                Console.WriteLine($"  Cost: {cost}, CP before: {cpBefore}");

                var upRes = client.UplevelTech(new UplevelTechMsg
                {
                    TeamId = teamId,
                    TechType = type
                });

                long cpAfter = state.ComputingPower;
                Console.WriteLine($"  Result: {(upRes.ActSuccess ? "OK" : "FAIL")}");
                Console.WriteLine($"  CP after: {cpAfter} (delta: {cpAfter - cpBefore})");

                // 给算力恢复时间
                await Task.Delay(500, ct);
            }

            // [7] 尝试升级到第2级
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════");
            Console.WriteLine("  Level 2 Upgrade Attempts");
            Console.WriteLine("══════════════════════════════════════════════");

            // 先等更多算力
            Log("Waiting 5s for more CP...");
            await Task.Delay(5000, ct);
            Log($"CP = {state.ComputingPower}");

            foreach (var (type, name, cost) in allTechs)
            {
                if (ct.IsCancellationRequested) break;

                long cpBefore = state.ComputingPower;
                Console.WriteLine();
                Console.WriteLine($"--- {name} Lv.2 ---");
                Console.WriteLine($"  Cost: {cost}, CP before: {cpBefore}");

                var upRes = client.UplevelTech(new UplevelTechMsg
                {
                    TeamId = teamId,
                    TechType = type
                });

                long cpAfter = state.ComputingPower;
                Console.WriteLine($"  Result: {(upRes.ActSuccess ? "OK" : "FAIL")}");
                Console.WriteLine($"  CP after: {cpAfter} (delta: {cpAfter - cpBefore})");

                await Task.Delay(300, ct);
            }

            // [8] 尝试第3级（应该失败，max level = 2）
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════");
            Console.WriteLine("  Level 3 Upgrade Attempts (should FAIL — max=2)");
            Console.WriteLine("══════════════════════════════════════════════");

            await Task.Delay(3000, ct);
            Log($"CP = {state.ComputingPower}");

            foreach (var (type, name, cost) in allTechs.Take(3))
            {
                if (ct.IsCancellationRequested) break;
                Console.WriteLine();
                Console.WriteLine($"--- {name} Lv.3 ---");
                var upRes = client.UplevelTech(new UplevelTechMsg
                {
                    TeamId = teamId,
                    TechType = type
                });
                Console.WriteLine($"  Result: {(upRes.ActSuccess ? "OK" : "FAIL (expected)")}");
                await Task.Delay(200, ct);
            }

            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════════════");
            Console.WriteLine("  Tech test complete.");
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
        // 工具
        // ────────────────────────────────────────────────────────────────────
        private static async Task<bool> TimeoutTask(Task task, int sec, CancellationToken ct)
        {
            var delay = Task.Delay(TimeSpan.FromSeconds(sec), ct);
            return await Task.WhenAny(task, delay) == task;
        }

        private static void Log(string msg) =>
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }
}
