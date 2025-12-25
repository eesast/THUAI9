using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GameClass.GameObj;
using Preparation.Utility;

namespace Game
{
    public partial class Game
    {
        private readonly ConcurrentDictionary<(long teamId, TechType tech), int> teamTechLevels = new();

        internal bool UplevelTechInternal(long playerId, TechType tech)
        {
            if (tech == TechType.NULL_TECH_TYPE) return false;

            if (!characterManager.TryGetCharacter(playerId, out var ch)) return false;
            long teamId = ch.TeamID.Get();

            // Get tech cost
            int cost = GetTechCost(tech);
            if (cost <= 0) return false;

            // Check team power
            if (!teams.TryGetValue(teamId, out var teamState)) return false;
            long currentPower = teamState.Power.Get();
            if (currentPower < cost) return false;

            // Deduct power
            if (!AddTeamPower(teamId, -cost)) return false;

            // Increment tech level
            var key = (teamId, tech);
            int newLevel = teamTechLevels.AddOrUpdate(key, 1, (k, old) => old + 1);

            // Apply tech buffs to all team characters
            ApplyTechBuff(teamId, tech);

            return true;
        }

        private int GetTechCost(TechType tech)
        {
            // Based on the rules
            switch (tech)
            {
                case TechType.INCREASE_HP:
                    return 30; // 增加耐久
                case TechType.INCREASE_ATTACK_POWER:
                    return 10; // 战斗文明
                case TechType.INCREASE_MOVE_SPEED:
                    return 40; // 提高效率 (affects task time, treated as speed)
                case TechType.INCREASE_CARRY_CAPACITY:
                    return 50; // 节约成本 (we'll map this to other tech)
                case TechType.INCREASE_HARVEST_EFFICIENCY:
                    return 40; // 提高效率
                default:
                    return 0;
            }
        }

        private void ApplyTechBuff(long teamId, TechType tech)
        {
            var characters = characterManager.GetTeamCharacters(teamId);

            foreach (var ch in characters)
            {
                switch (tech)
                {
                    case TechType.INCREASE_HP:
                        // Increase HP by 50%
                        long currentMaxHP = ch.HP.GetMaxV();
                        long newMaxHP = (long)(currentMaxHP * 1.5);
                        ch.HP.SetMaxV(newMaxHP);
                        // Also heal to match percentage
                        long currentHP = ch.HP.GetValue();
                        long newCurrentHP = Math.Min((long)(currentHP * 1.5), newMaxHP);
                        ch.HP.SetVToMaxV();
                        break;

                    case TechType.INCREASE_ATTACK_POWER:
                        // Increase attack power by 30%
                        long currentATK = ch.AttackPower.GetMaxV();
                        long newATK = (long)(currentATK * 1.3);
                        ch.AttackPower.SetMaxV(newATK);
                        ch.AttackPower.SetVToMaxV();
                        break;

                    case TechType.INCREASE_MOVE_SPEED:
                        // Increase move speed (efficiency)
                        ch.Efficiency.AddPositiveV(1);
                        break;

                    case TechType.INCREASE_CARRY_CAPACITY:
                        // Increase carry capacity
                        long currentCarry = ch.Carry.GetMaxV();
                        long newCarry = (long)(currentCarry * 1.5);
                        ch.Carry.SetMaxV(newCarry);
                        break;

                    case TechType.INCREASE_HARVEST_EFFICIENCY:
                        // Increase harvest efficiency (add to efficiency stat)
                        ch.Efficiency.AddPositiveV(1);
                        break;

                    case TechType.INCREASE_ROBUST:
                        // Increase robust
                        ch.Robust.AddPositiveV(1);
                        break;
                }
            }
        }

        public int GetTeamTechLevel(long teamId, TechType tech)
        {
            var key = (teamId, tech);
            return teamTechLevels.GetOrAdd(key, 0);
        }

        public bool HasTech(long teamId, TechType tech, int minLevel = 1)
        {
            return GetTeamTechLevel(teamId, tech) >= minLevel;
        }

        public IReadOnlyDictionary<TechType, int> GetTeamTechs(long teamId)
        {
            var result = new Dictionary<TechType, int>();
            foreach (TechType tech in Enum.GetValues(typeof(TechType)))
            {
                if (tech == TechType.NULL_TECH_TYPE) continue;
                int level = GetTeamTechLevel(teamId, tech);
                if (level > 0)
                {
                    result[tech] = level;
                }
            }
            return result;
        }
    }
}
