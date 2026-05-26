using Grpc.Core;
using Protobuf;

// ============================================================================
// 多算力中心占领 + 科技全面升级测试
//
//   1. 召唤 1 个 Robot
//   2. 不断占领算力中心（占完一个找下一个）
//   3. 20s 后尝试升级所有科技
//
// 启动：dotnet run --project logic/ClientTest2 -- <playerId> <teamId>
// 推荐: --teamCount 2
// ============================================================================

namespace ClientTest2
{
    public static class Program
    {
        private const int CellSize = 1000;
        private const int CellCenter = 500;
        private const double ArrivalRadius = 300.0;
        private const long CharId = 1;

        private sealed class SharedState
        {
            private readonly object _lk = new();
            private readonly TaskCompletionSource<bool> _gameStartTcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _charSeenTcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool _hasPos;
            private int _charX, _charY;
            private long _cp;
            private int _facX = -1, _facY = -1;
            private readonly List<CC> _centers = new();

            public sealed class CC
            {
                public long Id, OwnerTeamId;
                public int X, Y;
            }

            public Task GameStartTask => _gameStartTcs.Task;
            public Task CharSeenTask => _charSeenTcs.Task;

            public void ApplyFrame(MessageToClient frame, long teamId)
            {
                if (frame.GameState is GameState.GameStart or GameState.GameRunning)
                    _gameStartTcs.TrySetResult(true);

                lock (_lk)
                {
                    _centers.Clear();
                    foreach (var obj in frame.ObjMessage)
                    {
                        var fac = obj.FactoryMessage;
                        if (fac != null && fac.TeamId == teamId)
                        {
                            _cp = fac.ComputingPower;
                            _facX = fac.X;
                            _facY = fac.Y;
                        }

                        var cc = obj.ComputeCenterMessage;
                        if (cc != null)
                        {
                            _centers.Add(new CC
                            {
                                Id = cc.CenterId,
                                OwnerTeamId = cc.OwnerTeamId,
                                X = cc.X,
                                Y = cc.Y
                            });
                        }

                        var ch = obj.CharacterMessage;
                        if (ch != null && ch.TeamId == teamId && ch.PlayerId == CharId)
                        {
                            _hasPos = true;
                            _charX = ch.X;
                            _charY = ch.Y;
                            _charSeenTcs.TrySetResult(true);
                        }
                    }
                }
            }

            public bool TryGetPos(out int x, out int y)
            { lock (_lk) { x = _charX; y = _charY; return _hasPos; } }

            public long CP { get { lock (_lk) return _cp; } }

            public bool TryGetFactoryPos(out int x, out int y)
            { lock (_lk) { x = _facX; y = _facY; return _facX >= 0; } }

            public List<CC> GetCenters() { lock (_lk) return _centers.ToList(); }
        }

        public static async Task Main(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("Usage: ClientTest2 <playerId> <teamId>"); return; }
            if (!long.TryParse(args[0], out long pid) || !long.TryParse(args[1], out long tid)) return;

            var channel = new Channel("127.0.0.1:8888", ChannelCredentials.Insecure);
            await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(5));
            var client = new AvailableService.AvailableServiceClient(channel);

            var streamCall = client.RegisterFactory(new RegisterFactoryMsg
            { PlayerId = pid, TeamId = tid, SideFlag = (int)tid });
            var state = new SharedState();
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var streamTask = ReadStreamAsync(streamCall, state, tid, cts.Token);
            try { await Run(client, state, cts, tid); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.WriteLine($"[EX] {ex}"); }
            finally { cts.Cancel(); try { await streamTask; } catch { } await channel.ShutdownAsync(); }
        }

        private static async Task Run(
            AvailableService.AvailableServiceClient client, SharedState state,
            CancellationTokenSource cts, long teamId)
        {
            var ct = cts.Token;

            Log("Waiting for game start...");
            if (!await Timeout(state.GameStartTask, 30, ct)) { Log("[FAIL] Start timeout."); return; }
            Log("[OK] Game started.");

            // [1] 召唤 Robot
            Log("Creating Robot...");
            var cr = client.CreateCharacter(new CreateCharacterMsg
            { TeamId = teamId, PlayerId = CharId, CharacterType = CharacterType.Robot });
            if (!cr.ActSuccess) { Log("[FAIL] CreateCharacter."); return; }
            if (!await Timeout(state.CharSeenTask, 10, ct)) { Log("[FAIL] Char not seen."); return; }
            Log($"[OK] Robot spawned. CP={state.CP}");

            // [2] 获取地图
            var map = client.GetMap(new NullRequest());
            var gameStartTime = DateTime.UtcNow;
            var techTime = gameStartTime.AddSeconds(20);

            // [3] 持续占领 CC，直到 20s 倒计时结束
            Log("Starting CC occupation loop (20s)...");
            int occCount = 0;
            var occupiedIds = new HashSet<long>();

            while (DateTime.UtcNow < techTime && !ct.IsCancellationRequested)
            {
                var centers = state.GetCenters();

                // 找最近的未被我方占领的 CC
                if (!state.TryGetPos(out int cx, out int cy))
                { await Task.Delay(150, ct); continue; }

                SharedState.CC? target = null;
                long bestD = long.MaxValue;
                foreach (var cc in centers)
                {
                    if (cc.OwnerTeamId == teamId || occupiedIds.Contains(cc.Id)) continue;
                    long dx = cc.X - cx, dy = cc.Y - cy;
                    long d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; target = cc; }
                }

                if (target == null)
                {
                    Log("All visible CCs occupied or no CCs found. Idle...");
                    await Task.Delay(1000, ct);
                    continue;
                }

                // 导航到 CC
                int tr = target.X / CellSize, tc = target.Y / CellSize;
                Log($"Navigating to CC#{target.Id} at ({tr},{tc})...");
                if (!await MoveToCell(client, state, teamId, map, tr, tc, ct)) continue;

                // 占领
                Log($"Occupying CC#{target.Id}...");
                for (int i = 0; i < 30 && !ct.IsCancellationRequested; i++)
                {
                    var or = client.Occupy(new OccupyMsg
                    { TeamId = teamId, PlayerId = CharId, TargetX = 0, TargetY = 0, TargetComputeCenterId = -1 });
                    if (or.ActSuccess) break;
                    await Task.Delay(200, ct);
                }

                // 等占领完成
                bool gotIt = false;
                var od = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < od && !ct.IsCancellationRequested)
                {
                    foreach (var cc in state.GetCenters())
                    {
                        long dx = cc.X - target.X, dy = cc.Y - target.Y;
                        if (dx * dx + dy * dy < 500 * 500 && cc.OwnerTeamId == teamId)
                        { gotIt = true; break; }
                    }
                    if (gotIt) break;
                    await Task.Delay(500, ct);
                }

                if (gotIt)
                {
                    occupiedIds.Add(target.Id);
                    occCount++;
                    Log($"[OK] CC#{target.Id} occupied! (total={occCount}, CP={state.CP})");
                }
                else
                    Log($"[WARN] CC#{target.Id} occupy timeout.");
            }

            // [4] 20s 到了，尝试升级所有科技
            var waitRemaining = techTime - DateTime.UtcNow;
            if (waitRemaining > TimeSpan.Zero) await Task.Delay(waitRemaining, ct);

            Log($"=== Tech upgrade phase ===");
            Log($"CP before upgrades: {state.CP}");

            var allTechs = new (TechType type, string name, int cost)[]
            {
                (TechType.IncreaseHp,          "INCREASE_HP",           30),
                (TechType.IncreaseRobust,      "INCREASE_ROBUST",       30),
                (TechType.IncreaseAttackPower, "INCREASE_ATTACK_POWER", 60),
                (TechType.IncreaseAttackSize,  "INCREASE_ATTACK_SIZE",  60),
                (TechType.IncreaseMoveSpeed,   "INCREASE_MOVE_SPEED",   40),
                (TechType.IncreaseCarryCapacity,"INCREASE_CARRY_CAPACITY",50),
                (TechType.IncreaseEfficiency,  "INCREASE_EFFICIENCY",   40),
                (TechType.IncreaseProduction,  "INCREASE_PRODUCTION",   60),
                (TechType.IncreaseStorage,     "INCREASE_STORAGE",      50),
                (TechType.IncreasePrice,       "INCREASE_PRICE",        80),
                (TechType.DecreaseCost,        "DECREASE_COST",         50),
            };

            Console.WriteLine();
            Console.WriteLine("==============================================");
            Console.WriteLine("  Tech Upgrade Results");
            Console.WriteLine("==============================================");

            int totalOk = 0;
            foreach (var (type, name, cost) in allTechs)
            {
                if (ct.IsCancellationRequested) break;

                long cpBefore = state.CP;
                var res = client.UplevelTech(new UplevelTechMsg
                { TeamId = teamId, TechType = type });
                await Task.Delay(400, ct);
                long cpAfter = state.CP;
                long delta = cpAfter - cpBefore;

                string status = res.ActSuccess ? "OK" : "FAIL";
                Console.WriteLine($"  {name,-30} cost={cost,3}  cp_before={cpBefore,4}  cp_after={cpAfter,4}  {status}");
                if (res.ActSuccess) totalOk++;
            }

            // [5] 尝试第 2 级
            Console.WriteLine();
            Console.WriteLine("--- Level 2 ---");
            await Task.Delay(2000, ct);
            Log($"CP before L2: {state.CP}");

            foreach (var (type, name, cost) in allTechs)
            {
                if (ct.IsCancellationRequested) break;

                long cpBefore = state.CP;
                var res = client.UplevelTech(new UplevelTechMsg
                { TeamId = teamId, TechType = type });
                await Task.Delay(400, ct);
                long cpAfter = state.CP;
                long delta = cpAfter - cpBefore;

                string status = res.ActSuccess ? "OK" : "FAIL";
                Console.WriteLine($"  {name,-30} cost={cost,3}  cp_before={cpBefore,4}  cp_after={cpAfter,4}  {status}");
                if (res.ActSuccess) totalOk++;
            }

            // [6] 尝试第 3 级（应全部失败，max=2）
            Console.WriteLine();
            Console.WriteLine("--- Level 3 (should all FAIL) ---");
            await Task.Delay(2000, ct);

            foreach (var (type, name, cost) in allTechs.Take(4))
            {
                if (ct.IsCancellationRequested) break;
                var res = client.UplevelTech(new UplevelTechMsg
                { TeamId = teamId, TechType = type });
                await Task.Delay(200, ct);
                Console.WriteLine($"  {name,-30} {(res.ActSuccess ? "OK" : "FAIL (expected)")}");
            }

            Console.WriteLine();
            Console.WriteLine("==============================================");
            Console.WriteLine($"  Total successful upgrades: {totalOk}");
            Console.WriteLine("==============================================");
        }

        // =========================================================================
        // 导航到指定 cell
        // =========================================================================
        private static async Task<bool> MoveToCell(
            AvailableService.AvailableServiceClient client, SharedState state,
            long teamId, MessageOfMap map, int tr, int tc, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8 && !ct.IsCancellationRequested; attempt++)
            {
                if (!state.TryGetPos(out int cx, out int cy))
                { await Task.Delay(120, ct); continue; }

                var path = FindPathTo(map, cx / CellSize, cy / CellSize, tr, tc);
                if (path == null) return false;
                if (path.Count <= 1) return true;

                bool ok = true;
                foreach (var cell in path.Skip(1))
                {
                    if (!await MoveToCellStep(client, state, teamId, cell.r, cell.c, ct))
                    { ok = false; break; }
                }
                if (ok) return true;
            }
            return false;
        }

        private static async Task<bool> MoveToCellStep(
            AvailableService.AvailableServiceClient client, SharedState state,
            long teamId, int row, int col, CancellationToken ct)
        {
            int tx = row * CellSize + CellCenter, ty = col * CellSize + CellCenter;
            var dl = DateTime.UtcNow.AddSeconds(12);
            double lastDis = double.MaxValue;
            int stall = 0;

            while (DateTime.UtcNow < dl && !ct.IsCancellationRequested)
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
                { TeamId = teamId, PlayerId = CharId, TimeInMilliseconds = 200, Angle = angle });
                lastDis = dis;
                await Task.Delay(120, ct);
            }
            return false;
        }

        // =========================================================================
        // BFS
        // =========================================================================
        private static bool IsPassable(PlaceType p) => p is PlaceType.Space or PlaceType.Bush;

        private static bool IsTraversable(MessageOfMap map, int r, int c, int clearance)
        {
            int h = map.Rows.Count, w = map.Rows[0].Cols.Count;
            if ((uint)r >= (uint)h || (uint)c >= (uint)w) return false;
            if (!IsPassable(map.Rows[r].Cols[c])) return false;
            for (int dr = -clearance; dr <= clearance; dr++)
                for (int dc = -clearance; dc <= clearance; dc++)
                {
                    int nr = r + dr, nc = c + dc;
                    if ((uint)nr < (uint)h && (uint)nc < (uint)w && !IsPassable(map.Rows[nr].Cols[nc]))
                        return false;
                }
            return true;
        }

        private static List<(int r, int c)>? FindPathTo(MessageOfMap map, int sr, int sc, int tr, int tc)
        {
            int h = map.Rows.Count; if (h == 0) return null;
            int w = map.Rows[0].Cols.Count;
            var (dist, prevR, prevC) = Bfs(map, sr, sc, h, w, clearance: 0);
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

        private static (int[,] dist, int[,] prevR, int[,] prevC) Bfs(
            MessageOfMap map, int sr, int sc, int h, int w, int clearance)
        {
            var dist = new int[h, w]; var prevR = new int[h, w]; var prevC = new int[h, w];
            for (int r = 0; r < h; r++) for (int c = 0; c < w; c++)
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
                    { dist[nr, nc] = 1; prevR[nr, nc] = sr; prevC[nr, nc] = sc; q.Enqueue((nr, nc)); }
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
                    dist[nr, nc] = dist[r, c] + 1; prevR[nr, nc] = r; prevC[nr, nc] = c;
                    q.Enqueue((nr, nc));
                }
            }
            return (dist, prevR, prevC);
        }

        private static List<(int r, int c)> Reconstruct(
            (int r, int c) end, int sr, int sc, int[,] prevR, int[,] prevC)
        {
            var path = new List<(int, int)>(); var cur = end;
            while (!(cur.r == sr && cur.c == sc))
            {
                path.Add(cur); int pr = prevR[cur.r, cur.c], pc = prevC[cur.r, cur.c];
                if (pr < 0) return []; cur = (pr, pc);
            }
            path.Add((sr, sc)); path.Reverse(); return path;
        }

        // =========================================================================
        // Misc
        // =========================================================================
        private static async Task ReadStreamAsync(
            AsyncServerStreamingCall<MessageToClient> call, SharedState state,
            long teamId, CancellationToken ct)
        {
            try { while (await call.ResponseStream.MoveNext(ct)) state.ApplyFrame(call.ResponseStream.Current, teamId); }
            catch (RpcException) { }
            catch (OperationCanceledException) { }
        }

        private static async Task<bool> Timeout(Task task, int sec, CancellationToken ct)
        {
            var delay = Task.Delay(TimeSpan.FromSeconds(sec), ct);
            return await Task.WhenAny(task, delay) == task;
        }

        private static void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }
}
