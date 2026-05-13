using GameClass.GameObj;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.Atomic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using static System.Formats.Asn1.AsnWriter;

namespace Gaming
{
    public partial class Game
    {
        private readonly ConcurrentDictionary<long, TeamState> teams;

        private sealed class TeamState
        {
            public long TeamId { get; }
            public AtomicLong Score { get; } = new(GameData.FactoryInitialScore);
            public Factory Factory { get; }
            public ConcurrentDictionary<string, AtomicLong> Tech { get; } = new();

            public TeamState(long teamId, Factory factory)
            {
                TeamId = teamId; Factory = factory;
                Tech.TryAdd("Cost", new AtomicLong(0));
                Tech.TryAdd("Efficiency", new AtomicLong(0));
                Tech.TryAdd("Market", new AtomicLong(0));
                Tech.TryAdd("Robust", new AtomicLong(0));
                Tech.TryAdd("Warrior", new AtomicLong(0));
                Tech.TryAdd("Production", new AtomicLong(0));
                Tech.TryAdd("Storage", new AtomicLong(0));
                Tech.TryAdd("MoveSpeed", new AtomicLong(0));
                Tech.TryAdd("Carry", new AtomicLong(0));
                Tech.TryAdd("Price", new AtomicLong(0));
            }

            public bool TrySetTech(string key, int value)
            {
                if (value < 0 || value > 2)
                {
                    LogicLogging.logger.LogDebug($"Invalid tech level {value} for {key} of team {TeamId}");
                    return false;
                }
                if (!Tech.TryGetValue(key, out var atomic))
                {
                    LogicLogging.logger.LogDebug($"Tech {key} not found for team {TeamId}");
                    return false;
                }
                atomic.SetROri(value);
                return true;
            }

            public int GetTech(string key)
            {
                return Tech.TryGetValue(key, out var atomic) ? (int)atomic.Get() : 0;
            }
        }

        public readonly struct TeamSnapshot
        {
            public long TeamId { get; }
            public long Score { get; }

            public int CostTech { get; }
            public int EfficiencyTech { get; }
            public int MarketTech { get; }
            public int RobustTech { get; }
            public int WarriorTech { get; }

            public TeamSnapshot(long teamId, long score, int costTech, int efficiencyTech, int marketTech, int robustTech, int warriorTech)
            {
                TeamId = teamId; Score = score;
                CostTech = costTech; EfficiencyTech = efficiencyTech; MarketTech = marketTech; RobustTech = robustTech; WarriorTech = warriorTech;
            }
        }

        private void InitTeams()
        {
            // 工厂位置应该对应地图中 PlaceType.FACTORY 的位置
            // MapInfo 中 defaultMap 的工厂位置是 [3,3], [3,46], [46,3], [46,46]
            var corners = new (int cx, int cy)[]
            {
                (3, 3),      // Team 1 工厂位置
                (3, 46),     // Team 2 工厂位置
                (46, 3),     // Team 3 工厂位置
                (46, 46)     // Team 4 工厂位置
            };
            for (int i = 0; i < 4; i++)
            {
                long teamId = i + 1;
                var (cx, cy) = corners[i];
                XY pos = GameData.GetCellCenterPos(cx, cy);
                var fac = new Factory(pos);
                fac.TeamID.SetROri(teamId);
                var ts = new TeamState(teamId, fac);
                ts.TrySetTech("Cost", 0);
                ts.TrySetTech("Efficiency", 0);
                ts.TrySetTech("Market", 0);
                ts.TrySetTech("Robust", 0);
                ts.TrySetTech("Warrior", 0);
                teams.TryAdd(teamId, ts);
            }
        }


        public bool AddTeamScore(long teamId, long delta)
        {
            if (!teams.TryGetValue(teamId, out var t))
            {
                LogicLogging.logger.LogDebug($"Attempted to add score for non-existent team {teamId}");
                return false;
            }
            if (delta == 0)
            {
                LogicLogging.logger.LogDebug($"Attempted to add zero score for team {teamId}, no change made");
                return true;
            }
            if (delta > 0) t.Score.AddRNow(delta); else t.Score.SubRNow(-delta);
            return true;
        }


        public TeamSnapshot GetTeamSnapshot(long teamId)
        {
            if (!teams.TryGetValue(teamId, out var t)) return new TeamSnapshot(teamId, 0, 0, 0, 0, 0, 0);
            return new TeamSnapshot(teamId, t.Score.Get(), t.GetTech("Cost"), t.GetTech("Efficiency"), t.GetTech("Market"), t.GetTech("Robust"), t.GetTech("Warrior"));
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
                list.Add(new TeamSnapshot(t.TeamId, t.Score.Get(), t.GetTech("Cost"), t.GetTech("Efficiency"), t.GetTech("Market"), t.GetTech("Robust"), t.GetTech("Warrior")));
            }
            list.Sort((a, b) => a.TeamId.CompareTo(b.TeamId));
            return list;
        }
    }
}
