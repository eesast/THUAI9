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
            public AtomicLong Score { get; } = new(0); // 分数
            public Factory Factory { get; }

            // 新增：科技字典，键为固定五项，值为 AtomicLong，取值 0/1/2
            public ConcurrentDictionary<string, AtomicLong> Tech { get; } = new();

            public TeamState(long teamId, Factory factory)
            {
                TeamId = teamId; Factory = factory;
                // 初始化科技项为 0
                Tech.TryAdd("Cost", new AtomicLong(0));
                Tech.TryAdd("Efficiency", new AtomicLong(0));
                Tech.TryAdd("Market", new AtomicLong(0));
                Tech.TryAdd("Robust", new AtomicLong(0));
                Tech.TryAdd("Warrior", new AtomicLong(0));
            }

            // 尝试设置科技值，仅允许 0、1、2（线程安全)
            public bool TrySetTech(string key, int value)
            {
                if (value < 0 || value > 2) return false;
                if (!Tech.TryGetValue(key, out var atomic)) return false;
                atomic.SetROri(value);
                return true;
            }

            // 获取科技值（不存在则返回 0）
            public int GetTech(string key)
            {
                return Tech.TryGetValue(key, out var atomic) ? (int)atomic.Get() : 0;
            }
        }

        public readonly struct TeamSnapshot
        {
            public long TeamId { get; }
            public long Score { get; }

            // 新增：五项科技快照
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
                var ts = new TeamState(teamId, fac);
                // 明确初始化科技为 0（TeamState 构造已做，但显式设置以保证一致性）
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
            if (!teams.TryGetValue(teamId, out var t)) return false;
            if (delta == 0) return true;
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
