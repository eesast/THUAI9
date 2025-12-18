using System;
using System.Collections.Concurrent;
using System.Threading;
using GameClass.GameObj;
using GameClass.GameObj.Areas;
using Preparation.Utility;

namespace Game
{
    public partial class Game
    {
        private readonly ActionManager actionManager;

        private sealed class ActionManager
        {
            private readonly Game game;
            private readonly ConcurrentDictionary<long, CancellationTokenSource> occupying = new(); // key: ComputeCenter.ID
            private readonly ConcurrentDictionary<(long playerId, long resourceId), CancellationTokenSource> harvesting = new();

            public ActionManager(Game game)
            {
                this.game = game;
            }

            public bool Harvest(long playerId, Resource resource, long durationMs)
            {
                if (resource == null) return false;
                if (!game.characterManager.TryGetCharacter(playerId, out var ch)) return false;
                // 仅当在资源点周围 3x3 格内可采
                if (!GameData.ApproachToInteract(ch.Position, resource.Position)) return false;

                // 仅汽车可采集（每秒10单位），其他单位暂不支持
                int ratePerSec = ch.CharacterType == CharacterType.AUTONOMOUS_CAR ? 10 : 0;
                if (ratePerSec <= 0) return false;

                var key = (playerId, resource.ID);
                if (harvesting.ContainsKey(key)) return false; // 已有进行中的采集

                var cts = new CancellationTokenSource();
                if (!harvesting.TryAdd(key, cts)) { cts.Dispose(); return false; }

                new Thread(() =>
                {
                    try
                    {
                        int elapsed = 0;
                        int step = 200; // 200ms tick
                        while (!cts.IsCancellationRequested && elapsed < durationMs)
                        {
                            if (!game.Map.Timer.IsGaming) { Thread.Sleep(step); continue; }
                            // 位置保持在 3x3 范围内，否则中断
                            if (!GameData.ApproachToInteract(ch.Position, resource.Position)) break;

                            // 扣减资源：ratePerSec * step/1000
                            long delta = (long)Math.Max(1, ratePerSec * step / 1000.0);
                            resource.HP.SubPositiveV(delta);

                            // 资源耗尽：转为障碍（保留方块、Rigid=true，状态设为 HARVESTED），即形成阻挡
                            if (resource.HP.GetValue() == 0)
                            {
                                resource.SetERState(ResourceState.HARVESTED);
                                break;
                            }

                            Thread.Sleep(step);
                            elapsed += step;
                        }
                    }
                    finally
                    {
                        harvesting.TryRemove(key, out var oldCts);
                        oldCts?.Dispose();
                    }
                })
                { IsBackground = true }.Start();

                return true;
            }

            public bool Occupy(long playerId, ComputeCenter center)
            {
                if (center == null) return false;
                if (!game.characterManager.TryGetCharacter(playerId, out var ch)) return false;
                // 仅无人机/机器人可占领
                if (ch.CharacterType != CharacterType.DRONE && ch.CharacterType != CharacterType.ROBOT) return false;

                // 已在占领
                if (occupying.ContainsKey(center.ID)) return false;
                // 需在中心范围内（使用 1 格邻近判定）
                if (!GameData.ApproachToInteract(ch.Position, center.Position)) return false;

                var cts = new CancellationTokenSource();
                if (!occupying.TryAdd(center.ID, cts)) { cts.Dispose(); return false; }

                new Thread(() =>
                {
                    try
                    {
                        int occupyTimeMs = 10_000;
                        int elapsed = 0;
                        int tick = 200; // 200ms 检查
                        while (!cts.IsCancellationRequested && elapsed < occupyTimeMs)
                        {
                            if (!game.Map.Timer.IsGaming) { Thread.Sleep(tick); continue; }
                            // 离开范围则打断
                            if (!GameData.ApproachToInteract(ch.Position, center.Position)) return;
                            Thread.Sleep(tick);
                            elapsed += tick;
                        }
                        if (elapsed >= occupyTimeMs && !cts.IsCancellationRequested)
                        {
                            center.SetOccupied(ch.PlayerID.Get());
                        }
                    }
                    finally
                    {
                        occupying.TryRemove(center.ID, out var oldCts);
                        oldCts?.Dispose();
                    }
                })
                { IsBackground = true }.Start();

                return true;
            }

            public bool AddGoods(long playerId, GoodsType type, int delta)
            {
                if (!game.characterManager.TryGetCharacter(playerId, out var ch)) return false;
                return ch.GoodsLoad.Add(type, delta);
            }

            public bool SetGoods(long playerId, GoodsType type, int value)
            {
                if (!game.characterManager.TryGetCharacter(playerId, out var ch)) return false;
                return ch.GoodsLoad.Set(type, value);
            }
        }
    }
}
