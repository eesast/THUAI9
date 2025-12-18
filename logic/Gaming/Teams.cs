using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.Atomic;
using GameClass.GameObj;

namespace Game
{
    public partial class Game
    {
        private readonly ConcurrentDictionary<long, TeamState> teams = new();

        private sealed class TeamInventory
        {
            private readonly AtomicInt[] counts = new AtomicInt[6]
            {
                new(0), // NULL_GOODS_TYPE
                new(0), // SEMICONDUCTOR
                new(0), // MEDICINE
                new(0), // TOYS
                new(0), // CLOTHES
                new(0)  // FOOD
            };

            public int Get(GoodsType type) => counts[(int)type].Get();
            public void Set(GoodsType type, int value)
            {
                if (value < 0) value = 0;
                counts[(int)type].Set(value);
            }
            public void Add(GoodsType type, int delta)
            {
                if (delta == 0) return;
                int now = counts[(int)type].Get();
                int target = now + delta;
                if (target < 0) target = 0;
                counts[(int)type].Set(target);
            }
            public IReadOnlyDictionary<GoodsType, int> Snapshot()
            {
                var d = new Dictionary<GoodsType, int>(5)
                {
                    { GoodsType.SEMICONDUCTOR, counts[(int)GoodsType.SEMICONDUCTOR].Get() },
                    { GoodsType.MEDICINE, counts[(int)GoodsType.MEDICINE].Get() },
                    { GoodsType.TOYS, counts[(int)GoodsType.TOYS].Get() },
                    { GoodsType.CLOTHES, counts[(int)GoodsType.CLOTHES].Get() },
                    { GoodsType.FOOD, counts[(int)GoodsType.FOOD].Get() },
                };
                return d;
            }
        }

        private sealed class TeamState
        {
            public long TeamId { get; }
            public AtomicLong Power { get; } = new(0); // 算力
            public AtomicLong Score { get; } = new(0); // 分数
            public TeamInventory Inventory { get; } = new(); // 队伍产品库存
            public Factory Factory { get; }

            public TeamState(long teamId, Factory factory)
            {
                TeamId = teamId; Factory = factory;
            }
        }

        public readonly struct TeamSnapshot
        {
            public long TeamId { get; }
            public long Power { get; }
            public long Score { get; }
            public IReadOnlyDictionary<GoodsType, int> Inventory { get; }
            public TeamSnapshot(long teamId, long power, long score, IReadOnlyDictionary<GoodsType, int> inventory)
            {
                TeamId = teamId; Power = power; Score = score; Inventory = inventory;
            }
        }

        private void InitTeams()
        {
            // 预置四支队伍（1..4），并在地图四角实例化工厂
            var corners = new (int cx, int cy)[]
            {
                (0, 0),
                (0, GameData.MapCols - 1),
                (GameData.MapRows - 1, 0),
                (GameData.MapRows - 1, GameData.MapCols - 1)
            };
            for (int i = 0; i < 4; i++)
            {
                long teamId = i + 1;
                var (cx, cy) = corners[i];
                XY pos = GameData.GetCellCenterPos(cx, cy);
                var fac = new Factory(pos);
                fac.TeamID.SetROri(teamId);
                teams.TryAdd(teamId, new TeamState(teamId, fac));
            }
        }

        public bool RegisterTeam(long teamId)
        {
            // 默认将工厂放置在 (0,0)；若需要自定义出生点，可另行重载
            var fac = new Factory(GameData.GetCellCenterPos(0, 0));
            fac.TeamID.SetROri(teamId);
            return teams.TryAdd(teamId, new TeamState(teamId, fac));
        }

        public bool AddTeamPower(long teamId, long delta)
        {
            if (!teams.TryGetValue(teamId, out var t)) return false;
            if (delta == 0) return true;
            if (delta > 0) t.Power.Add(delta); else t.Power.Sub(-delta);
            return true;
        }

        public bool AddTeamScore(long teamId, long delta)
        {
            if (!teams.TryGetValue(teamId, out var t)) return false;
            if (delta == 0) return true;
            if (delta > 0) t.Score.Add(delta); else t.Score.Sub(-delta);
            return true;
        }

        public bool AddTeamGoods(long teamId, GoodsType type, int delta)
        {
            if (!teams.TryGetValue(teamId, out var t)) return false;
            t.Inventory.Add(type, delta);
            return true;
        }

        public bool SetTeamGoods(long teamId, GoodsType type, int value)
        {
            if (!teams.TryGetValue(teamId, out var t)) return false;
            t.Inventory.Set(type, value);
            return true;
        }

        public TeamSnapshot GetTeamSnapshot(long teamId)
        {
            if (!teams.TryGetValue(teamId, out var t)) return new TeamSnapshot(teamId, 0, 0, new Dictionary<GoodsType, int>());
            return new TeamSnapshot(teamId, t.Power.Get(), t.Score.Get(), t.Inventory.Snapshot());
        }

        public Factory? GetTeamFactory(long teamId)
        {
            return teams.TryGetValue(teamId, out var t) ? t.Factory : null;
        }

        public IReadOnlyList<TeamSnapshot> GetAllTeamsSnapshot()
        {
            var list = new List<TeamSnapshot>(teams.Count);
            foreach (var kv in teams)
            {
                var t = kv.Value;
                list.Add(new TeamSnapshot(t.TeamId, t.Power.Get(), t.Score.Get(), t.Inventory.Snapshot()));
            }
            list.Sort((a, b) => a.TeamId.CompareTo(b.TeamId));
            return list;
        }
    }
}
