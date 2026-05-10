using Grpc.Core;
using Protobuf;

// ============================================================================
// ClientTest4 — 事件系统 + 智慧大脑（AskAI）测试
//
// 测试目标:
//   1. GetCurrentEventStatus RPC — 客户端能否获取当前事件名称及描述
//   2. AskAI RPC — 基于事件描述请求 AI 生产策略建议
//   3. 端到端: 事件触发 → 获取状态 → 询问 AI → 执行生产
//
// 流程:
//   - 连接 → RegisterFactory → 等待游戏开始
//   - 召唤采集角色维持 Material 收入
//   - 每 5s 轮询 GetCurrentEventStatus
//   - 当事件从 "normal" 变为其他名称时，调用 AskAI 询问生产策略
//   - 根据 AI 回复执行 Produce
//
// 用法: dotnet run --project logic/clienttest4 -- <playerId> <teamId>
// ============================================================================

namespace ClientTest4
{
    public static class Program
    {
        private const int CellSize = 1000;
        private const int CellCenter = CellSize / 2;
        private const long HarvesterId = 1;

        private static readonly (int dr, int dc)[] Dirs = { (-1, 0), (1, 0), (0, -1), (0, 1) };

        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: clienttest4 <playerId> <teamId>");
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

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            var ct = cts.Token;

            // ── 帧流后台读取 ──
            var gameStarted = new TaskCompletionSource<bool>();
            int teamMaterial = 0;
            bool canProduce = false;
            var frameLock = new object();

            _ = Task.Run(async () =>
            {
                var stream = streamCall.ResponseStream;
                while (await stream.MoveNext(ct))
                {
                    var frame = stream.Current;
                    if (frame.GameState is GameState.GameStart or GameState.GameRunning)
                        gameStarted.TrySetResult(true);

                    lock (frameLock)
                    {
                        foreach (var obj in frame.ObjMessage)
                        {
                            var fac = obj.FactoryMessage;
                            if (fac != null && fac.TeamId == teamId)
                            {
                                canProduce = fac.CanProduce;
                            }

                            var tm = obj.TeamMessage;
                            if (tm != null && tm.TeamId == teamId)
                            {
                                teamMaterial = tm.Material;
                            }
                        }
                    }
                }
            }, ct);

            try
            {
                Console.WriteLine("[*] Waiting for game start (max 60s)...");
                if (await Task.WhenAny(gameStarted.Task, Task.Delay(60_000, ct)) != gameStarted.Task)
                {
                    Console.WriteLine("[FAIL] Game start timeout.");
                    return;
                }
                Console.WriteLine("[OK] Game started.");

                var map = client.GetMap(new NullRequest());

                // ── 召唤采集角色 ──
                Console.WriteLine("[*] Spawning harvester (AutonomousCar, charId=1)...");
                if (!await SpawnCharacter(client, teamId, HarvesterId, CharacterType.AutonomousCar, ct))
                {
                    Console.WriteLine("[FAIL] Harvester spawn failed.");
                    return;
                }
                Console.WriteLine("[OK] Harvester spawned (charId=1).");

                // ── 并行: 采集 + 事件/AI 循环 ──
                var harvestTask = HarvestLoop(client, map, teamId, ct);
                var aiTask = EventAiLoop(client, teamId, playerId, () =>
                {
                    lock (frameLock) return canProduce;
                }, ct);

                await Task.WhenAny(
                    Task.WhenAll(harvestTask, aiTask),
                    Task.Delay(Timeout.Infinite, ct));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXCEPTION] {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                cts.Cancel();
                await channel.ShutdownAsync();
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 事件 + AskAI 循环
        // ════════════════════════════════════════════════════════════════════
        private static async Task EventAiLoop(
            AvailableService.AvailableServiceClient client,
            long teamId, long playerId,
            Func<bool> canProduce,
            CancellationToken ct)
        {
            string lastEventName = "normal";
            DateTime lastAskAi = DateTime.MinValue;
            const int pollMs = 5_000;
            const int cooldownMs = 25_000;

            // 等几秒让第一个事件可能触发
            await Task.Delay(10_000, ct);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(pollMs, ct);

                // 1. 获取当前事件
                var evt = client.GetCurrentEventStatus(new EventStatusRequest
                {
                    TeamId = teamId,
                    PlayerId = playerId
                });

                if (!evt.ActSuccess)
                {
                    Console.WriteLine($"[GetEventStatus] FAILED: {evt.EventDescription}");
                    continue;
                }

                Console.WriteLine($"[GetEventStatus] OK — name=\"{evt.EventName}\", desc=\"{evt.EventDescription}\"");

                // 2. 检测事件变化
                if (evt.EventName == lastEventName)
                    continue;

                Console.WriteLine($"[Event] CHANGED: \"{lastEventName}\" → \"{evt.EventName}\"");
                lastEventName = evt.EventName;

                // 3. "normal" 不需要问 AI
                if (string.IsNullOrEmpty(evt.EventName) || evt.EventName == "normal")
                {
                    Console.WriteLine("[Event] Back to normal — no AI query.");
                    continue;
                }

                // 4. AskAI 冷却
                if ((DateTime.UtcNow - lastAskAi).TotalMilliseconds < cooldownMs)
                {
                    Console.WriteLine("[AskAI] In cooldown, skip.");
                    continue;
                }

                // 5. 构造 prompt
                string prompt =
                    $"当前游戏内发生事件：{evt.EventName}。事件描述：{evt.EventDescription}。" +
                    $"基于此事件，我应该优先生产下列哪种商品以获得最大利润？" +
                    $"可选：SEMICONDUCTOR（半导体）、MEDICINE（药品）、TOYS（玩具）、CLOTHES（服装）、FOOD（食品）。" +
                    $"请只回复一个商品英文名，并附最多一句话的理由。";

                Console.WriteLine($"[AskAI] Sending prompt ({prompt.Length} chars)...");
                Console.WriteLine($"[AskAI] Prompt: {prompt}");

                try
                {
                    var aiRes = client.AskAI(new StrategicAIRequest
                    {
                        TeamId = teamId,
                        CurrentGameTime = 0,
                        Prompt = prompt
                    });
                    lastAskAi = DateTime.UtcNow;

                    if (!aiRes.ActSuccess)
                    {
                        Console.WriteLine($"[AskAI] FAILED: {aiRes.Explanation}");
                        continue;
                    }

                    Console.WriteLine($"[AskAI] Answer: {aiRes.Answer}");

                    // 6. 解析并执行
                    var goods = ParseGoodsType(aiRes.Answer);
                    if (goods == null)
                    {
                        Console.WriteLine("[AskAI] Could not determine goods type from response.");
                        continue;
                    }

                    if (!canProduce())
                    {
                        Console.WriteLine($"[Produce] Factory cannot produce right now, but AI recommends: {goods}");
                        continue;
                    }

                    var pr = client.Produce(new ProduceGoodsMsg
                    {
                        ProductType = goods.Value,
                        TeamId = teamId,
                        MaxProduceNum = 1
                    });
                    Console.WriteLine($"[Produce] {goods}: {(pr.ActSuccess ? "OK" : "FAIL")}");
                }
                catch (RpcException ex)
                {
                    Console.WriteLine($"[AskAI] RPC error: {ex.Status.Detail}");
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // 采集循环（保持 Material 流入）
        // ════════════════════════════════════════════════════════════════════
        private static async Task HarvestLoop(
            AvailableService.AvailableServiceClient client,
            MessageOfMap map, long teamId, CancellationToken ct)
        {
            // 找到最近的资源 cell
            var resCells = new List<(int r, int c)>();
            for (int r = 0; r < map.Rows.Count; r++)
                for (int c = 0; c < map.Rows[r].Cols.Count; c++)
                    if (map.Rows[r].Cols[c] == PlaceType.Resource)
                        resCells.Add((r, c));

            if (resCells.Count == 0)
            {
                Console.WriteLine("[Harvest] No resource cells.");
                return;
            }

            // 按到中心区域距离排序，取最近
            var target = resCells.OrderBy(rc => Math.Abs(rc.r - 24) + Math.Abs(rc.c - 24)).First();
            Console.WriteLine($"[Harvest] Target resource at cell ({target.r},{target.c}).");

            // 导航到目标 cell 并持续采集
            await MoveToCell(client, teamId, HarvesterId, target.r, target.c, map, ct);

            while (!ct.IsCancellationRequested)
            {
                var hr = client.Harvest(new ResourceMsg
                {
                    TeamId = teamId,
                    PlayerId = HarvesterId,
                    Amount = 1
                });
                // 减少日志噪音：只在状态变化时输出
                if (!hr.ActSuccess)
                {
                    Console.WriteLine("[Harvest] Harvest returned false (resource may be depleted).");
                    // 寻找新的资源点（简化：随机偏移到邻居资源 cell）
                    break;
                }
                await Task.Delay(1500, ct);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // BFS + 移动
        // ════════════════════════════════════════════════════════════════════
        private static List<(int r, int c)>? BfsPath(
            MessageOfMap map, int sr, int sc, int tr, int tc)
        {
            int rows = map.Rows.Count, cols = map.Rows[0].Cols.Count;
            if (sr < 0 || sc < 0 || tr < 0 || tc < 0) return null;
            if (sr >= rows || sc >= cols || tr >= rows || tc >= cols) return null;

            var prev = new Dictionary<(int, int), (int, int)>();
            var q = new Queue<(int, int)>();
            var visited = new HashSet<(int, int)> { (sr, sc) };
            q.Enqueue((sr, sc));

            while (q.Count > 0)
            {
                var (cr, cc) = q.Dequeue();
                if (cr == tr && cc == tc)
                {
                    var path = new List<(int, int)> { (cr, cc) };
                    while (prev.TryGetValue(path[^1], out var p))
                        path.Add(p);
                    path.Reverse();
                    return path;
                }

                foreach (var (dr, dc) in Dirs)
                {
                    int nr = cr + dr, nc = cc + dc;
                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                    if (!visited.Add((nr, nc))) continue;

                    var pt = map.Rows[nr].Cols[nc];
                    if (pt == PlaceType.Barrier || pt == PlaceType.Factory) continue;
                    prev[(nr, nc)] = (cr, cc);
                    q.Enqueue((nr, nc));
                }
            }
            return null;
        }

        private static async Task MoveToCell(
            AvailableService.AvailableServiceClient client,
            long teamId, long playerId,
            int tr, int tc, MessageOfMap map, CancellationToken ct)
        {
            // 简化: 从工厂附近的 cell 出发
            // 实际角色出生点在工厂 cell，地图上 4 个工厂分别在 (3,3) (3,45) (45,3) (45,45)
            // teamId 1→(3,3), 2→(45,3), 3→(3,45), 4→(45,45)
            int sr = teamId switch { 1 => 3, 2 => 45, 3 => 3, 4 => 45, _ => 3 };
            int sc = teamId switch { 1 => 3, 2 => 3, 3 => 45, 4 => 45, _ => 3 };

            var path = BfsPath(map, sr, sc, tr, tc);
            if (path == null)
            {
                Console.WriteLine($"[Nav] No path from ({sr},{sc}) to ({tr},{tc}).");
                return;
            }
            Console.WriteLine($"[Nav] {path.Count} steps from ({sr},{sc}) → ({tr},{tc}).");

            int curR = sr, curC = sc;
            for (int i = 1; i < path.Count && !ct.IsCancellationRequested; i++) // skip [0] = start
            {
                int nr = path[i].r, nc = path[i].c;
                int tx = nc * CellSize + CellCenter;
                int ty = nr * CellSize + CellCenter;
                int cx = curC * CellSize + CellCenter;
                int cy = curR * CellSize + CellCenter;

                double angle = Math.Atan2(ty - cy, tx - cx);
                client.Move(new MoveMsg
                {
                    TeamId = teamId,
                    PlayerId = playerId,
                    TimeInMilliseconds = 400,
                    Angle = angle
                });
                await Task.Delay(300, ct);
                curR = nr;
                curC = nc;
            }
            Console.WriteLine($"[Nav] Arrived at ({tr},{tc}).");
        }

        // ════════════════════════════════════════════════════════════════════
        // 工具
        // ════════════════════════════════════════════════════════════════════
        private static async Task<bool> SpawnCharacter(
            AvailableService.AvailableServiceClient client,
            long teamId, long charId, CharacterType type, CancellationToken ct)
        {
            for (int i = 0; i < 20 && !ct.IsCancellationRequested; i++)
            {
                var res = client.CreateCharacter(new CreateCharacterMsg
                {
                    TeamId = teamId,
                    PlayerId = charId,
                    CharacterType = type
                });
                if (res.ActSuccess) return true;
                await Task.Delay(500, ct);
            }
            return false;
        }

        private static GoodsType? ParseGoodsType(string aiResponse)
        {
            var u = aiResponse.ToUpperInvariant();
            if (u.Contains("SEMICONDUCTOR") || u.Contains("半导体")) return GoodsType.Semiconductor;
            if (u.Contains("MEDICINE") || u.Contains("药品") || u.Contains("医药")) return GoodsType.Medicine;
            if (u.Contains("TOYS") || u.Contains("玩具")) return GoodsType.Toys;
            if (u.Contains("CLOTHES") || u.Contains("服装") || u.Contains("衣服")) return GoodsType.Clothes;
            if (u.Contains("FOOD") || u.Contains("食品") || u.Contains("食物")) return GoodsType.Food;
            return null;
        }
    }
}
