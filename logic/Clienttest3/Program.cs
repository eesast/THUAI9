using Grpc.Core;
using Protobuf;

// ============================================================================
// 复杂策略测试：召唤 Robot → 占领最近算力中心 → 搜索最近敌方角色 → 寻路进攻
//
// 运行前提：服务端需用 --gameTimeInSecond 60 --teamCount 2 启动多队伍
// 参数：<playerId> <teamId> [characterId]  （characterId 默认 1）
// ============================================================================

namespace ClientTest3
{
    public class Program
    {
        private const int CellSize = 1000;
        private const int CellCenter = 500;
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
            private int _facX = -1, _facY = -1;
            private bool _hasFac;

            private readonly List<ComputeCenterInfo> _centers = new();
            private readonly List<EnemyInfo> _enemies = new();

            public Task GameStartTask => _gameStartTcs.Task;
            public Task CharSeenTask => _charSeenTcs.Task;

            public sealed class ComputeCenterInfo
            {
                public long CenterId;
                public int X, Y;
                public long OwnerTeamId;
                public double OccupyProgress;
            }

            public sealed class EnemyInfo
            {
                public long TeamId;
                public long PlayerId;
                public int X, Y;
                public long Hp;
                public DateTime LastSeen;
            }

            public void ApplyFrame(MessageToClient frame, long teamId, long charId)
            {
                if (frame.GameState is GameState.GameStart or GameState.GameRunning)
                    _gameStartTcs.TrySetResult(true);

                lock (_lk)
                {
                    _centers.Clear();
                    _enemies.Clear();
                }

                foreach (var obj in frame.ObjMessage)
                {
                    var cc = obj.ComputeCenterMessage;
                    if (cc != null)
                    {
                        lock (_lk)
                        {
                            _centers.Add(new ComputeCenterInfo
                            {
                                CenterId = cc.CenterId,
                                X = cc.X,
                                Y = cc.Y,
                                OwnerTeamId = cc.OwnerTeamId,
                                OccupyProgress = cc.OccupyProgress
                            });
                        }
                    }

                    var fac = obj.FactoryMessage;
                    if (fac != null && fac.TeamId == teamId)
                    {
                        lock (_lk)
                        {
                            _facX = fac.X;
                            _facY = fac.Y;
                            _hasFac = true;
                        }
                    }

                    var ch = obj.CharacterMessage;
                    if (ch == null) continue;

                    if (ch.TeamId == teamId && ch.PlayerId == charId)
                    {
                        lock (_lk)
                        {
                            _hasPos = true;
                            _charX = ch.X;
                            _charY = ch.Y;
                        }
                        _charSeenTcs.TrySetResult(true);
                    }
                    else if (ch.TeamId != teamId)
                    {
                        lock (_lk)
                        {
                            _enemies.Add(new EnemyInfo
                            {
                                TeamId = ch.TeamId,
                                PlayerId = ch.PlayerId,
                                X = ch.X,
                                Y = ch.Y,
                                Hp = ch.Hp,
                                LastSeen = DateTime.UtcNow
                            });
                        }
                    }
                }
            }

            public bool TryGetPos(out int x, out int y)
            { lock (_lk) { x = _charX; y = _charY; return _hasPos; } }

            public bool TryGetFactoryPos(out int x, out int y)
            { lock (_lk) { x = _facX; y = _facY; return _hasFac; } }

            public List<ComputeCenterInfo> GetCenters()
            { lock (_lk) return _centers.ToList(); }

            public List<EnemyInfo> GetEnemies()
            { lock (_lk) return _enemies.Where(e => (DateTime.UtcNow - e.LastSeen).TotalSeconds < 3).ToList(); }
        }

        // ────────────────────────────────────────────────────────────────────
        // Main
        // ────────────────────────────────────────────────────────────────────
        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ClientTest3 <playerId> <teamId> [characterId]");
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
        // 核心流程：召唤 Robot → 占领算力中心 → 搜索敌方 → 进攻
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

            // ── [2] 召唤 Robot ────────────────────────────────────────────
            Log($"Creating Robot (charId={charId})...");
            var createRes = client.CreateCharacter(new CreateCharacterMsg
            {
                TeamId = teamId,
                PlayerId = charId,
                CharacterType = CharacterType.Robot
            });
            if (!createRes.ActSuccess)
            { Fail(cts, "CreateCharacter failed (factory may not have enough material)."); return; }

            if (!await TimeoutTask(state.CharSeenTask, 10, ct))
            { Fail(cts, "Character not seen in frame within 10s."); return; }
            Log("  Robot spawned.");

            // ── [3] 获取地图，寻路到最近算力中心 ──────────────────────────
            var map = client.GetMap(new NullRequest());
            Log("Navigating to nearest ComputeCenter...");
            if (!await NavigateToType(client, state, map, teamId, charId, PlaceType.ComputeCenter, ct))
            { Fail(cts, "Failed to reach any ComputeCenter."); return; }
            Log("  Arrived at ComputeCenter.");

            // ── [4] 占领算力中心 ──────────────────────────────────────────
            Log("Starting occupation...");
            bool occupying = false;
            for (int i = 0; i < 15 && !occupying && !ct.IsCancellationRequested; i++)
            {
                var or = client.Occupy(new OccupyMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    TargetX = 0,
                    TargetY = 0,
                    TargetComputeCenterId = -1
                });
                if (or.ActSuccess) occupying = true;
                else await Task.Delay(200, ct);
            }
            if (!occupying)
            { Fail(cts, "Occupy failed after 15 attempts."); return; }
            Log("  Occupation started, waiting for completion...");

            // 等待占领完成：检查帧中算力中心 OwnerTeamId 是否变为我方
            bool occupied = false;
            var occupyDeadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < occupyDeadline && !ct.IsCancellationRequested)
            {
                var centers = state.GetCenters();
                if (state.TryGetPos(out int cx, out int cy))
                {
                    foreach (var cc in centers)
                    {
                        double dist = Math.Sqrt((cc.X - cx) * (cc.X - cx) + (cc.Y - cy) * (cc.Y - cy));
                        if (dist < CellSize * 2 && cc.OwnerTeamId == teamId)
                        {
                            occupied = true;
                            Log($"  ComputeCenter {cc.CenterId} now owned by us! (progress={cc.OccupyProgress})");
                            break;
                        }
                    }
                }
                if (occupied) break;
                await Task.Delay(300, ct);
            }
            if (!occupied)
                Log("  WARNING: Occupation may not have completed, continuing anyway.");

            // ── [5] 搜索最近敌方角色 ──────────────────────────────────────
            Log("Searching for nearest enemy...");
            SharedState.EnemyInfo? target = null;
            var searchDeadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < searchDeadline && !ct.IsCancellationRequested)
            {
                var enemies = state.GetEnemies();
                if (enemies.Count > 0 && state.TryGetPos(out int cx, out int cy))
                {
                    double bestDist = double.MaxValue;
                    foreach (var e in enemies)
                    {
                        double d = Math.Sqrt((e.X - cx) * (e.X - cx) + (e.Y - cy) * (e.Y - cy));
                        if (d < bestDist) { bestDist = d; target = e; }
                    }
                    if (target != null)
                    {
                        Log($"  Found enemy: Team {target.TeamId} Player {target.PlayerId} at ({target.X}, {target.Y}), HP={target.Hp}, dist={bestDist:F0}");
                        break;
                    }
                }
                await Task.Delay(200, ct);
            }
            if (target == null)
            { Fail(cts, "No enemy character found within 20s."); return; }

            // ── [6] 停止当前动作，导航到敌方角色 ──────────────────────────
            Log("Stopping current action, navigating to enemy...");
            client.EndAllAction(new IDMsg { PlayerId = charId, TeamId = teamId });
            await Task.Delay(100, ct);

            if (!await NavigateToCell(client, state, map, teamId, charId,
                target.X / CellSize, target.Y / CellSize, ct))
            { Fail(cts, "Failed to reach enemy."); return; }
            Log("  Close to enemy.");

            // ── [7] 持续攻击（每 tick 重新��定最近敌人，避免追旧坐标）─────
            // 诊断 J：Server 端 ATKFrequency=1.0 → 攻击 cd 1000ms。攻击成功后
            //         至少等 1.05s 再发下一发，避免 80% 的 RPC 因 cd 被拒。
            // 诊断 K：Server 用 character.AttackSize=1000 单位（1 格）做范围检测。
            //         必须保持距离 ≤ 1000，否则 Game.Attack 找不到敌人直接返回 false。
            // 诊断 L：AttackedPlayerId/AttackedTeamId 字段 server 端忽略，但传当前
            //         锁定敌人 id 让 server log 可读（不再是 -1/-1 误导）。
            Log("Attacking enemy (target refreshed each tick)...");
            int attackCount = 0;
            int totalCalls = 0;
            int missStreak = 0;
            var attackDeadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < attackDeadline && !ct.IsCancellationRequested)
            {
                // 每 tick 从最新帧选最近敌人
                SharedState.EnemyInfo? cur = null;
                int selfX = 0, selfY = 0;
                if (state.TryGetPos(out selfX, out selfY))
                {
                    double bestDist = double.MaxValue;
                    foreach (var e in state.GetEnemies())
                    {
                        double d = Math.Sqrt((double)(e.X - selfX) * (e.X - selfX)
                                           + (double)(e.Y - selfY) * (e.Y - selfY));
                        if (d < bestDist) { bestDist = d; cur = e; }
                    }
                }
                if (cur == null)
                {
                    await Task.Delay(150, ct);
                    continue;
                }

                double dist = Math.Sqrt((double)(cur.X - selfX) * (cur.X - selfX)
                                      + (double)(cur.Y - selfY) * (cur.Y - selfY));

                totalCalls++;
                var ar = client.Attack(new AttackMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    AttackRange = 2500,
                    AttackedPlayerId = cur.PlayerId,
                    AttackedTeamId = cur.TeamId
                });

                if (ar.ActSuccess)
                {
                    attackCount++;
                    missStreak = 0;
                    Log($"  HIT  #{attackCount} → T{cur.TeamId}P{cur.PlayerId} hp={cur.Hp} dist={dist:F0}");
                    // 命中后至少等 1.05s 让 server cd 过期
                    await Task.Delay(1050, ct);
                }
                else
                {
                    missStreak++;
                    // 距离 > 900：大概率超出 attack range，往敌人方向移动
                    // 距离 ≤ 900：八成是 cd 还没过，等等
                    if (dist > 900 && state.TryGetPos(out int mx, out int my))
                    {
                        double angle = Math.Atan2(cur.Y - my, cur.X - mx);
                        client.Move(new MoveMsg
                        {
                            TeamId = teamId,
                            PlayerId = charId,
                            TimeInMilliseconds = 200,
                            Angle = angle
                        });
                        if (missStreak == 1 || missStreak % 5 == 0)
                            Log($"  miss #{missStreak} → reposition to ({cur.X},{cur.Y}) dist={dist:F0}");
                        await Task.Delay(150, ct);
                    }
                    else
                    {
                        // 在范围内但失败 = cd 未过，安静等
                        await Task.Delay(200, ct);
                    }
                }
            }
            Log($"Attack loop: {attackCount} hits / {totalCalls} calls.");

            // 最终检查：敌人是否还在附近
            bool enemyGone = true;
            if (state.TryGetPos(out int myX, out int myY))
            {
                var remaining = state.GetEnemies();
                foreach (var e in remaining)
                {
                    double d = Math.Sqrt((e.X - myX) * (e.X - myX) + (e.Y - myY) * (e.Y - myY));
                    if (d < CellSize * 5)
                    {
                        enemyGone = false;
                        break;
                    }
                }
            }

            // ── 结果汇总 ──────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("══════════════════════════════════════");
            Console.WriteLine($"  Recruit Robot    : OK");
            Console.WriteLine($"  Occupy CC        : {(occupied ? "OK" : "UNCERTAIN")}");
            Console.WriteLine($"  Find Enemy       : {(target != null ? "OK" : "FAIL")}");
            Console.WriteLine($"  Attacks Fired    : {attackCount}");
            Console.WriteLine($"  Enemy Gone       : {(enemyGone ? "Likely" : "Still nearby")}");
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
        {
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
