using Grpc.Core;
using Protobuf;

namespace ClientTest2
{
    public static class Program
    {
        private const int CellSize = 1000;
        private const int CellCenterOffset = 500;

        private sealed class SharedState
        {
            private readonly object stateLock = new();
            private readonly TaskCompletionSource<bool> gameStartTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> characterSeenTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            private bool hasCharacterPosition;
            private int characterX;
            private int characterY;
            private int teamMaterial;
            private bool factoryCanProduce = true;

            public Task GameStartTask => gameStartTcs.Task;
            public Task CharacterSeenTask => characterSeenTcs.Task;

            public void ApplyFrame(MessageToClient frame, long teamId, long characterId)
            {
                if (frame.GameState == GameState.GameStart || frame.GameState == GameState.GameRunning)
                {
                    gameStartTcs.TrySetResult(true);
                }

                if (frame.AllMessage != null)
                {
                    int teamIdx = (int)teamId - 1;
                    if (teamIdx >= 0 && teamIdx < frame.AllMessage.Teams.Count)
                    {
                        lock (stateLock)
                        {
                            teamMaterial = frame.AllMessage.Teams[teamIdx].Material;
                        }
                    }
                }

                foreach (var obj in frame.ObjMessage)
                {
                    var fac = obj.FactoryMessage;
                    if (fac == null) continue;
                    if (fac.TeamId != teamId) continue;

                    lock (stateLock)
                    {
                        factoryCanProduce = fac.CanProduce;
                    }
                }

                foreach (var obj in frame.ObjMessage)
                {
                    var ch = obj.CharacterMessage;
                    if (ch == null) continue;
                    if (ch.TeamId != teamId || ch.PlayerId != characterId) continue;

                    lock (stateLock)
                    {
                        hasCharacterPosition = true;
                        characterX = ch.X;
                        characterY = ch.Y;
                    }
                    characterSeenTcs.TrySetResult(true);
                }
            }

            public bool TryGetCharacterPosition(out int x, out int y)
            {
                lock (stateLock)
                {
                    x = characterX;
                    y = characterY;
                    return hasCharacterPosition;
                }
            }

            public int GetTeamMaterial()
            {
                lock (stateLock)
                {
                    return teamMaterial;
                }
            }

            public bool IsFactoryCanProduce()
            {
                lock (stateLock)
                {
                    return factoryCanProduce;
                }
            }
        }

        public static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ClientTest2 <playerId> <teamId> [characterId]");
                return;
            }

            if (!long.TryParse(args[0], out long playerId))
            {
                Console.WriteLine("Invalid playerId.");
                return;
            }

            if (!long.TryParse(args[1], out long teamId))
            {
                Console.WriteLine("Invalid teamId.");
                return;
            }

            long characterId = 1;
            if (args.Length >= 3 && !long.TryParse(args[2], out characterId))
            {
                Console.WriteLine("Invalid characterId.");
                return;
            }

            var channel = new Channel("127.0.0.1:8888", ChannelCredentials.Insecure);
            await channel.ConnectAsync(DateTime.UtcNow.AddSeconds(5));
            var client = new AvailableService.AvailableServiceClient(channel);

            var register = new RegisterFactoryMsg
            {
                PlayerId = playerId,
                TeamId = teamId,
                SideFlag = (int)teamId
            };

            var streamCall = client.RegisterFactory(register);
            var state = new SharedState();
            using var cts = new CancellationTokenSource();

            var streamTask = ReadStreamAsync(streamCall, state, teamId, characterId, cts.Token);

            if (!await WaitWithTimeout(state.GameStartTask, TimeSpan.FromSeconds(30), cts.Token))
            {
                Console.WriteLine("Timeout waiting for game start.");
                cts.Cancel();
                await channel.ShutdownAsync();
                return;
            }

            var createRes = client.CreateCharacter(new CreateCharacterMsg
            {
                TeamId = teamId,
                PlayerId = characterId,
                CharacterType = CharacterType.Robot
            });

            if (!createRes.ActSuccess)
            {
                Console.WriteLine("CreateCharacter failed.");
                cts.Cancel();
                await channel.ShutdownAsync();
                return;
            }

            if (!await WaitWithTimeout(state.CharacterSeenTask, TimeSpan.FromSeconds(10), cts.Token))
            {
                Console.WriteLine("Timeout waiting for character frame.");
                cts.Cancel();
                await channel.ShutdownAsync();
                return;
            }

            var map = client.GetMap(new NullRequest());
            if (!state.TryGetCharacterPosition(out int startX, out int startY))
            {
                Console.WriteLine("Character position not available.");
                cts.Cancel();
                await channel.ShutdownAsync();
                return;
            }

            bool reachedResource = await NavigateToNearestResourceAsync(client, state, map, teamId, characterId, cts.Token);
            if (!reachedResource)
            {
                Console.WriteLine("Failed to navigate to a harvestable position near resource.");
                cts.Cancel();
                try { await streamTask; } catch { }
                await channel.ShutdownAsync();
                return;
            }

            bool startedHarvest = false;
            for (int i = 0; i < 15; i++)
            {
                var harvestRes = client.Harvest(new ResourceMsg
                {
                    TeamId = teamId,
                    PlayerId = characterId,
                    ResourceId = 0,
                    Amount = 0
                });

                if (harvestRes.ActSuccess)
                {
                    startedHarvest = true;
                    break;
                }

                await Task.Delay(200, cts.Token);
            }

            Console.WriteLine(startedHarvest ? "Harvest started." : "Harvest request failed.");

            if (!startedHarvest)
            {
                cts.Cancel();
                try { await streamTask; } catch { }
                await channel.ShutdownAsync();
                return;
            }

            const int requiredMaterial = 54; // (10+5+1+8+3) * 2
            if (!await WaitUntilMaterialEnoughAsync(state, requiredMaterial, TimeSpan.FromSeconds(40), cts.Token))
            {
                Console.WriteLine($"Material not enough in time. current={state.GetTeamMaterial()}, required={requiredMaterial}");
                cts.Cancel();
                try { await streamTask; } catch { }
                await channel.ShutdownAsync();
                return;
            }

            var produceTargets = new (GoodsType type, string name)[]
            {
                (GoodsType.Semiconductor, "Semiconductor"),
                (GoodsType.Medicine, "Medicine"),
                (GoodsType.Toys, "Toys"),
                (GoodsType.Clothes, "Clothes"),
                (GoodsType.Food, "Food")
            };

            foreach (var target in produceTargets)
            {
                bool canStart = await WaitUntilFactoryCanProduceAsync(state, TimeSpan.FromSeconds(30), cts.Token);
                if (!canStart)
                {
                    Console.WriteLine($"Factory stayed busy too long before producing {target.name}.");
                    break;
                }

                var produceRes = client.Produce(new ProduceGoodsMsg
                {
                    TeamId = teamId,
                    ProductType = target.type,
                    MaxProduceNum = 2
                });
                Console.WriteLine($"Produce {target.name} x2: {(produceRes.ActSuccess ? "OK" : "FAIL")}");

                if (produceRes.ActSuccess)
                {
                    bool finished = await WaitUntilFactoryCanProduceAsync(state, TimeSpan.FromSeconds(30), cts.Token);
                    if (!finished)
                    {
                        Console.WriteLine($"Factory busy timeout after producing {target.name}.");
                        break;
                    }
                }
                else
                {
                    await Task.Delay(200, cts.Token);
                }
            }

            await Task.Delay(3000, cts.Token);

            cts.Cancel();
            try { await streamTask; } catch { }
            await channel.ShutdownAsync();
        }

        private static async Task ReadStreamAsync(
            AsyncServerStreamingCall<MessageToClient> call,
            SharedState state,
            long teamId,
            long characterId,
            CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && await call.ResponseStream.MoveNext(ct))
            {
                var frame = call.ResponseStream.Current;
                state.ApplyFrame(frame, teamId, characterId);
            }
        }

        private static async Task<bool> WaitWithTimeout(Task task, TimeSpan timeout, CancellationToken ct)
        {
            var timeoutTask = Task.Delay(timeout, ct);
            var done = await Task.WhenAny(task, timeoutTask);
            return done == task;
        }

        private static async Task<bool> WaitUntilMaterialEnoughAsync(SharedState state, int required, TimeSpan timeout, CancellationToken ct)
        {
            var end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
            {
                if (state.GetTeamMaterial() >= required)
                    return true;
                await Task.Delay(150, ct);
            }
            return state.GetTeamMaterial() >= required;
        }

        private static async Task<bool> WaitUntilFactoryCanProduceAsync(SharedState state, TimeSpan timeout, CancellationToken ct)
        {
            var end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end && !ct.IsCancellationRequested)
            {
                if (state.IsFactoryCanProduce())
                    return true;
                await Task.Delay(120, ct);
            }
            return state.IsFactoryCanProduce();
        }

        private static bool IsPassable(PlaceType place)
        {
            return place != PlaceType.Barrier
                && place != PlaceType.Factory
                && place != PlaceType.ComputeCenter
                && place != PlaceType.Market
                && place != PlaceType.Resource;
        }

        private static bool IsTraversable(MessageOfMap map, int r, int c, int clearance)
        {
            int h = map.Rows.Count;
            int w = h == 0 ? 0 : map.Rows[0].Cols.Count;
            if (r < 0 || r >= h || c < 0 || c >= w)
                return false;
            if (!IsPassable(map.Rows[r].Cols[c]))
                return false;

            if (clearance <= 0)
                return true;

            for (int dr = -clearance; dr <= clearance; dr++)
            {
                for (int dc = -clearance; dc <= clearance; dc++)
                {
                    int nr = r + dr;
                    int nc = c + dc;
                    if (nr < 0 || nr >= h || nc < 0 || nc >= w)
                        continue;
                    if (!IsPassable(map.Rows[nr].Cols[nc]))
                        return false;
                }
            }
            return true;
        }

        private static List<(int r, int c)>? FindPathToNearestResource(MessageOfMap map, int startR, int startC)
        {
            var safePath = FindPathToNearestResource(map, startR, startC, clearance: 1);
            if (safePath != null)
                return safePath;
            return FindPathToNearestResource(map, startR, startC, clearance: 0);
        }

        private static List<(int r, int c)>? FindPathToNearestResource(MessageOfMap map, int startR, int startC, int clearance)
        {
            int h = map.Rows.Count;
            if (h == 0) return null;
            int w = map.Rows[0].Cols.Count;

            if (startR < 0 || startR >= h || startC < 0 || startC >= w)
                return null;
            if (!IsTraversable(map, startR, startC, 0))
                return null;

            int[,] dist = new int[h, w];
            int[,] prevR = new int[h, w];
            int[,] prevC = new int[h, w];
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    dist[r, c] = -1;
                    prevR[r, c] = -1;
                    prevC[r, c] = -1;
                }
            }

            var q = new Queue<(int r, int c)>();
            dist[startR, startC] = 0;
            prevR[startR, startC] = startR;
            prevC[startR, startC] = startC;
            q.Enqueue((startR, startC));

            int[] dr4 = [-1, 1, 0, 0];
            int[] dc4 = [0, 0, -1, 1];

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                for (int k = 0; k < 4; k++)
                {
                    int nr = cur.r + dr4[k];
                    int nc = cur.c + dc4[k];
                    if (nr < 0 || nr >= h || nc < 0 || nc >= w) continue;
                    if (dist[nr, nc] != -1) continue;
                    if (!IsTraversable(map, nr, nc, clearance)) continue;

                    dist[nr, nc] = dist[cur.r, cur.c] + 1;
                    prevR[nr, nc] = cur.r;
                    prevC[nr, nc] = cur.c;
                    q.Enqueue((nr, nc));
                }
            }

            (int r, int c)? bestTarget = null;
            int bestDist = int.MaxValue;

            for (int rr = 0; rr < h; rr++)
            {
                for (int cc = 0; cc < w; cc++)
                {
                    if (map.Rows[rr].Cols[cc] != PlaceType.Resource) continue;

                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int tr = rr + dr;
                            int tc = cc + dc;
                            if (tr < 0 || tr >= h || tc < 0 || tc >= w) continue;
                            if (!IsTraversable(map, tr, tc, clearance)) continue;
                            if (dist[tr, tc] < 0) continue;

                            if (dist[tr, tc] < bestDist)
                            {
                                bestDist = dist[tr, tc];
                                bestTarget = (tr, tc);
                            }
                        }
                    }
                }
            }

            if (bestTarget == null)
                return null;

            var path = new List<(int r, int c)>();
            var curCell = bestTarget.Value;
            while (!(curCell.r == startR && curCell.c == startC))
            {
                path.Add(curCell);
                int pr = prevR[curCell.r, curCell.c];
                int pc = prevC[curCell.r, curCell.c];
                if (pr < 0 || pc < 0) return null;
                curCell = (pr, pc);
            }
            path.Add((startR, startC));
            path.Reverse();
            return path;
        }

        private static async Task<bool> NavigateToNearestResourceAsync(
            AvailableService.AvailableServiceClient client,
            SharedState state,
            MessageOfMap map,
            long teamId,
            long characterId,
            CancellationToken ct)
        {
            for (int attempt = 0; attempt < 8 && !ct.IsCancellationRequested; attempt++)
            {
                if (!state.TryGetCharacterPosition(out int curX, out int curY))
                {
                    await Task.Delay(100, ct);
                    continue;
                }

                int startRow = curX / CellSize;
                int startCol = curY / CellSize;
                var path = FindPathToNearestResource(map, startRow, startCol);
                if (path == null || path.Count <= 1)
                    return true;

                bool broken = false;
                foreach (var cell in path.Skip(1))
                {
                    bool reached = await MoveToCellAsync(client, state, teamId, characterId, cell.r, cell.c, ct);
                    if (!reached)
                    {
                        broken = true;
                        break;
                    }
                }

                if (!broken)
                    return true;
            }

            return false;
        }

        private static async Task<bool> MoveToCellAsync(
            AvailableService.AvailableServiceClient client,
            SharedState state,
            long teamId,
            long characterId,
            int targetRow,
            int targetCol,
            CancellationToken ct)
        {
            int targetX = targetRow * CellSize + CellCenterOffset;
            int targetY = targetCol * CellSize + CellCenterOffset;

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(12);
            double lastDistance = double.MaxValue;
            int stallCount = 0;

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (!state.TryGetCharacterPosition(out int curX, out int curY))
                {
                    await Task.Delay(60, ct);
                    continue;
                }

                double dx = targetX - curX;
                double dy = targetY - curY;
                double dis = Math.Sqrt(dx * dx + dy * dy);
                if (dis <= 260)
                    return true;

                double angle = Math.Atan2(dy, dx);
                if (dis >= lastDistance - 20)
                {
                    stallCount++;
                }
                else
                {
                    stallCount = 0;
                }

                if (stallCount >= 4)
                {
                    double offset = ((stallCount / 4) % 2 == 0 ? 0.35 : -0.35);
                    angle += offset;
                }

                _ = client.Move(new MoveMsg
                {
                    TeamId = teamId,
                    PlayerId = characterId,
                    TimeInMilliseconds = 200,
                    Angle = angle
                });

                lastDistance = dis;
                await Task.Delay(120, ct);
            }

            return false;
        }
    }
}
