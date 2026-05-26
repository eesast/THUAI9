using Grpc.Core;
using Protobuf;

// ============================================================================
// 攻击敌方工厂测试
//
// 运行：dotnet run --project logic/Clienttest3 -- <playerId> <teamId> [characterId]
// 需要至少 2 队：--teamCount 2
// ============================================================================

namespace ClientTest3
{
    public class Program
    {
        private const int CellSize = 1000;
        private const int CellCenter = 500;
        private const double ArrivalRadius = 300.0;
        private const int AtkCD = 1050; // 攻击 cd ~1s

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

            private readonly List<EnemyFactory> _enemyFactories = new();

            public sealed class EnemyFactory
            {
                public long TeamId;
                public int X, Y;
                public int Hp;
                public DateTime LastSeen;
            }

            public Task GameStartTask => _gameStartTcs.Task;
            public Task CharSeenTask => _charSeenTcs.Task;

            public void ApplyFrame(MessageToClient frame, long teamId, long charId)
            {
                if (frame.GameState is GameState.GameStart or GameState.GameRunning)
                    _gameStartTcs.TrySetResult(true);

                lock (_lk)
                {
                    _enemyFactories.Clear();

                    foreach (var obj in frame.ObjMessage)
                    {
                        var fac = obj.FactoryMessage;
                        if (fac != null)
                        {
                            if (fac.TeamId == teamId)
                            {
                                _computingPower = fac.ComputingPower;
                            }
                            else
                            {
                                _enemyFactories.Add(new EnemyFactory
                                {
                                    TeamId = fac.TeamId,
                                    X = fac.X,
                                    Y = fac.Y,
                                    Hp = fac.Hp,
                                    LastSeen = DateTime.UtcNow
                                });
                            }
                        }

                        var ch = obj.CharacterMessage;
                        if (ch != null && ch.TeamId == teamId && ch.PlayerId == charId)
                        {
                            _hasPos = true;
                            _charX = ch.X;
                            _charY = ch.Y;
                            _charSeenTcs.TrySetResult(true);
                        }
                    }

                    if (frame.AllMessage != null)
                    {
                        int idx = (int)teamId - 1;
                        if ((uint)idx < (uint)frame.AllMessage.Teams.Count)
                            _teamScore = frame.AllMessage.Teams[idx].Score;
                    }
                }
            }

            public bool TryGetPos(out int x, out int y)
            { lock (_lk) { x = _charX; y = _charY; return _hasPos; } }

            public long ComputingPower { get { lock (_lk) return _computingPower; } }
            public long TeamScore { get { lock (_lk) return _teamScore; } }

            public List<EnemyFactory> GetEnemyFactories()
            { lock (_lk) return _enemyFactories.Where(f => (DateTime.UtcNow - f.LastSeen).TotalSeconds < 3).ToList(); }
        }

        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            { Console.WriteLine("Usage: ClientTest3 <playerId> <teamId> [characterId]"); return; }
            if (!long.TryParse(args[0], out long pid) || !long.TryParse(args[1], out long tid)) return;
            long cid = args.Length >= 3 && long.TryParse(args[2], out long x) ? x : 1L;

            var channel = new Channel("127.0.0.1:8888", ChannelCredentials.Insecure);
            await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(5));
            var client = new AvailableService.AvailableServiceClient(channel);
            var streamCall = client.RegisterFactory(new RegisterFactoryMsg
            { PlayerId = pid, TeamId = tid, SideFlag = (int)tid });
            var state = new SharedState();
            using var cts = new CancellationTokenSource();
            var streamTask = ReadStreamAsync(streamCall, state, tid, cid, cts.Token);

            try { await Run(client, state, cts, tid, cid); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.WriteLine($"[EX] {ex}"); }
            finally { cts.Cancel(); try { await streamTask; } catch { } await channel.ShutdownAsync(); }
        }

        private static async Task Run(
            AvailableService.AvailableServiceClient client, SharedState state,
            CancellationTokenSource cts, long teamId, long charId)
        {
            var ct = cts.Token;

            Log("Waiting for game start...");
            if (!await Timeout(state.GameStartTask, 30, ct)) { Fail(cts, "Start timeout."); return; }
            Log("[OK] Game started.");

            Log($"Creating Drone (charId={charId})...");
            var cr = client.CreateCharacter(new CreateCharacterMsg
            { TeamId = teamId, PlayerId = charId, CharacterType = CharacterType.Drone });
            if (!cr.ActSuccess) { Fail(cts, "CreateCharacter failed."); return; }
            if (!await Timeout(state.CharSeenTask, 10, ct)) { Fail(cts, "Char not seen."); return; }
            Log("[OK] Drone spawned.");

            var map = client.GetMap(new NullRequest());

            long initialScore = state.TeamScore;
            int totalHits = 0, totalMisses = 0, destroyedCount = 0;
            var destroyedIds = new HashSet<long>();

            while (!ct.IsCancellationRequested)
            {
                // === 找最近敌方工厂 ===
                var enemies = state.GetEnemyFactories().Where(f => !destroyedIds.Contains(f.TeamId)).ToList();
                if (enemies.Count == 0)
                {
                    if (destroyedCount > 0) Log("All enemy factories destroyed!");
                    else Log("No enemy factories found.");
                    break;
                }

                SharedState.EnemyFactory target = null!;
                if (state.TryGetPos(out int sx, out int sy))
                {
                    double best = double.MaxValue;
                    foreach (var f in enemies)
                    {
                        double d = Math.Sqrt((double)(f.X - sx) * (f.X - sx) + (double)(f.Y - sy) * (f.Y - sy));
                        if (d < best) { best = d; target = f; }
                    }
                }
                if (target == null) break;

                Log($"=== Target: T{target.TeamId} factory at ({target.X},{target.Y}) HP={target.Hp} ===");

                // 导航
                if (!await NavigateToCell(client, state, map, teamId, charId,
                    target.X / CellSize, target.Y / CellSize, ct))
                { Log("Can't reach, skip."); destroyedIds.Add(target.TeamId); continue; }

                // 攻击直到摧毁
                int fabHits = 0, fabMisses = 0;
                bool destroyed = false;
                while (!ct.IsCancellationRequested)
                {
                    var cur = state.GetEnemyFactories().FirstOrDefault(f => f.TeamId == target.TeamId);
                    long hpNow = cur?.Hp ?? -1;
                    if (hpNow <= 0 && fabHits > 0 && !destroyed)
                    {
                        destroyed = true;
                        destroyedCount++;
                        destroyedIds.Add(target.TeamId);
                        Log($"FACTORY T{target.TeamId} DESTROYED! Score={state.TeamScore}");
                    }

                    if (destroyed)
                    {
                        // 多砍 3 刀验证死工厂不再加分
                        long scoreBefore = state.TeamScore;
                        for (int i = 0; i < 3 && !ct.IsCancellationRequested; i++)
                        {
                            var atk = client.Attack(new AttackMsg
                            { TeamId = teamId, PlayerId = charId, AttackRange = 2500 });
                            await Task.Delay(400, ct);
                            Log($"  Post-mortem attack #{i + 1}: {(atk.ActSuccess ? $"OK  score+{state.TeamScore - scoreBefore}" : "FAIL (expected)")}" +
                                $"  (total score={state.TeamScore})");
                            scoreBefore = state.TeamScore;
                        }
                        break; // 去找下一个工厂
                    }

                    long scoreBefore2 = state.TeamScore;
                    var ar = client.Attack(new AttackMsg
                    { TeamId = teamId, PlayerId = charId, AttackRange = 2500 });
                    await Task.Delay(300, ct);

                    if (ar.ActSuccess)
                    {
                        fabHits++; totalHits++;
                        var fresh = state.GetEnemyFactories().FirstOrDefault(f => f.TeamId == target.TeamId);
                        long dam = state.TeamScore - scoreBefore2;
                        Log($"  HIT #{fabHits}  hp={fresh?.Hp ?? -1}  score+{dam}");
                        await Task.Delay(AtkCD, ct);
                    }
                    else
                    {
                        fabMisses++; totalMisses++;
                        if (state.TryGetPos(out int mx, out int my) && cur != null)
                        {
                            double dx = cur.X - mx, dy = cur.Y - my;
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            if (dist > 900)
                            {
                                double angle = Math.Atan2(dy, dx);
                                client.Move(new MoveMsg { TeamId = teamId, PlayerId = charId, TimeInMilliseconds = 200, Angle = angle });
                            }
                        }
                        if (fabMisses % 10 == 0) Log($"  miss x{fabMisses}");
                        await Task.Delay(200, ct);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("==============================================");
            Console.WriteLine("  Attack Summary");
            Console.WriteLine("==============================================");
            Console.WriteLine($"  Factories destroyed : {destroyedCount}");
            Console.WriteLine($"  Total hits          : {totalHits}");
            Console.WriteLine($"  Total misses        : {totalMisses}");
            Console.WriteLine($"  Score start         : {initialScore}");
            Console.WriteLine($"  Score final         : {state.TeamScore}");
            Console.WriteLine($"  Score gain          : {state.TeamScore - initialScore}");
            Console.WriteLine("==============================================");
        }

        // =========================================================================
        // Frame reader
        // =========================================================================
        private static async Task ReadStreamAsync(
            AsyncServerStreamingCall<MessageToClient> call, SharedState state,
            long teamId, long charId, CancellationToken ct)
        {
            try { while (await call.ResponseStream.MoveNext(ct)) state.ApplyFrame(call.ResponseStream.Current, teamId, charId); }
            catch (RpcException) { }
            catch (OperationCanceledException) { }
        }

        // =========================================================================
        // Navigation
        // =========================================================================
        private static async Task<bool> NavigateToCell(
            AvailableService.AvailableServiceClient client, SharedState state,
            MessageOfMap map, long teamId, long charId, int tr, int tc, CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8 && !ct.IsCancellationRequested; attempt++)
            {
                if (!state.TryGetPos(out int cx, out int cy)) { await Task.Delay(120, ct); continue; }
                var path = FindPathAdjacentTo(map, cx / CellSize, cy / CellSize, tr, tc);
                if (path == null) return false;
                if (path.Count <= 1) return true;
                bool ok = true;
                foreach (var cell in path.Skip(1))
                    if (!await MoveStep(client, state, teamId, charId, cell.r, cell.c, ct)) { ok = false; break; }
                if (ok) return true;
            }
            return false;
        }

        private static async Task<bool> MoveStep(
            AvailableService.AvailableServiceClient client, SharedState state,
            long teamId, long charId, int row, int col, CancellationToken ct)
        {
            int tx = row * CellSize + CellCenter, ty = col * CellSize + CellCenter;
            var dl = DateTime.UtcNow.AddSeconds(10);
            double lastDis = double.MaxValue; int stall = 0;
            while (DateTime.UtcNow < dl && !ct.IsCancellationRequested)
            {
                if (!state.TryGetPos(out int cx, out int cy)) { await Task.Delay(60, ct); continue; }
                double dx = tx - cx, dy = ty - cy;
                double dis = Math.Sqrt(dx * dx + dy * dy);
                if (dis <= ArrivalRadius) return true;
                double angle = Math.Atan2(dy, dx);
                stall = dis >= lastDis - 20 ? stall + 1 : 0;
                if (stall >= 4) angle += (stall / 4) % 2 == 0 ? 0.35 : -0.35;
                client.Move(new MoveMsg { TeamId = teamId, PlayerId = charId, TimeInMilliseconds = 200, Angle = angle });
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
                    if ((uint)nr < (uint)h && (uint)nc < (uint)w && !IsPassable(map.Rows[nr].Cols[nc])) return false;
                }
            return true;
        }

        private static List<(int r, int c)>? FindPathAdjacentTo(
            MessageOfMap map, int sr, int sc, int tr, int tc)
        {
            int h = map.Rows.Count; if (h == 0) return null;
            int w = map.Rows[0].Cols.Count;
            var (dist, prevR, prevC) = Bfs(map, sr, sc, h, w, clearance: 0);
            int[] dr = [-1, 1, 0, 0], dc = [0, 0, -1, 1];
            (int, int)? best = null; int bestD = int.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                int ar = tr + dr[k], ac = tc + dc[k];
                if ((uint)ar >= (uint)h || (uint)ac >= (uint)w) continue;
                if (dist[ar, ac] < 0 || dist[ar, ac] >= bestD) continue;
                bestD = dist[ar, ac]; best = (ar, ac);
            }
            return best == null ? null : Reconstruct(best.Value, sr, sc, prevR, prevC);
        }

        private static (int[,], int[,], int[,]) Bfs(
            MessageOfMap map, int sr, int sc, int h, int w, int clearance)
        {
            var dist = new int[h, w]; var prevR = new int[h, w]; var prevC = new int[h, w];
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                {
                    dist[r, c] = -1;
                    prevR[r, c] = prevC[r, c] = -1;
                }
            dist[sr, sc] = 0; prevR[sr, sc] = sr; prevC[sr, sc] = sc;
            var q = new Queue<(int, int)>();
            int[] dr = [-1, 1, 0, 0], dc = [0, 0, -1, 1];
            if (!IsTraversable(map, sr, sc, clearance))
            {
                for (int k = 0; k < 4; k++)
                {
                    int nr = sr + dr[k], nc = sc + dc[k];
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
                    int nr = r + dr[k], nc = c + dc[k];
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
        // Utils
        // =========================================================================
        private static async Task<bool> Timeout(Task t, int sec, CancellationToken ct)
        { return await Task.WhenAny(t, Task.Delay(sec * 1000, ct)) == t; }

        private static void Fail(CancellationTokenSource cts, string msg)
        { Console.WriteLine($"[FAIL] {msg}"); cts.Cancel(); }

        private static void Log(string msg)
            => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
    }
}
