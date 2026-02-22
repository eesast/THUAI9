using System;
using GameClass.GameObj;
using Preparation.Utility;

namespace Gaming
{
    public partial class Game
    {
        private sealed class UplevelManager
        {
            private readonly Game game;

            public UplevelManager(Game game)
            {
                this.game = game;
            }

            public bool UplevelTech(long playerId, TechType tech)
            {
                if (!game.characterManager.TryGetCharacter(playerId, out var character)) return false;
                return UplevelTech(character, tech);
            }

            public bool UplevelTech(Character character, TechType tech)
            {
                if (character == null || character.IsRemoved) return false;

                long teamId = character.TeamID.Get();
                if (!game.teams.TryGetValue(teamId, out var teamState)) return false;

                string key;
                int cost;
                switch (tech)
                {
                    case TechType.INCREASE_HP:
                    case TechType.INCREASE_ROBUST:
                        key = "Robust"; cost = 30; break;
                    case TechType.INCREASE_ATTACK_POWER:
                    case TechType.INCREASE_ATTACK_SIZE:
                        key = "Warrior"; cost = 60; break;
                    case TechType.INCREASE_MOVE_SPEED:
                        key = "MoveSpeed"; cost = 40; break;
                    case TechType.INCREASE_CARRY_CAPACITY:
                        key = "Carry"; cost = 50; break;
                    case TechType.INCREASE_EFFICIENCY:
                        key = "Efficiency"; cost = 40; break;
                    case TechType.INCREASE_PRODUCTION:
                        key = "Production"; cost = 60; break;
                    case TechType.INCREASE_STORAGE:
                        key = "Storage"; cost = 50; break;
                    case TechType.INCREASE_PRICE:
                        key = "Price"; cost = 80; break;
                    case TechType.DECREASE_COST:
                        key = "Cost"; cost = 50; break;
                    default:
                        return false;
                }

                int curLevel = teamState.GetTech(key);
                if (curLevel >= 2) return false;

                var factory = game.GetTeamFactory(teamId);
                if (factory == null) return false;

                while (true)
                {
                    long cur = factory.ComputingPower.Get();
                    if (cur < cost) return false;
                    if (factory.ComputingPower.CompareExROri(cur - cost, cur) == cur) break;
                }

                bool setOk = teamState.TrySetTech(key, curLevel + 1);
                if (!setOk)
                {
                    factory.AddComputingPower(cost);
                    return false;
                }

                int newLevel = curLevel + 1;
                switch (key)
                {
                    case "Efficiency":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            ch.Efficiency.AddPositiveV(newLevel - curLevel);
                        }
                        break;
                    case "Robust":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            long baseHp = ch.Occupation.MaxHp;
                            long newMaxHp = (long)(baseHp * (1.0 + 0.2 * newLevel));
                            ch.HP.SetMaxV(newMaxHp);
                            ch.HP.SetVToMaxV();
                            ch.Robust.AddPositiveV((newLevel - curLevel) * 2);
                        }
                        break;
                    case "Warrior":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            long baseAtk = ch.Occupation.AttackPower;
                            long extra = (long)(baseAtk * 0.3 * (newLevel - curLevel));
                            if (extra > 0)
                            {
                                ch.AttackPower.AddPositiveV(extra);
                                ch.AttackPower.SetMaxV(ch.AttackPower + extra);
                            }
                        }
                        break;
                    case "MoveSpeed":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            int delta = 200 * (newLevel - curLevel); // additive boost
                            ch.MoveSpeed.AddPositive(delta);
                        }
                        break;
                    case "Carry":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            long newMax = ch.Carry.GetMaxV() + 10 * (newLevel - curLevel);
                            ch.Carry.SetPositiveMaxV(newMax);
                            ch.Carry.SetVToMaxV();
                        }
                        break;
                    case "Storage":
                        var fac = game.GetTeamFactory(teamId);
                        if (fac != null)
                        {
                            long newMax = fac.Storage.GetMaxV() + 50 * (newLevel - curLevel);
                            fac.Storage.SetPositiveMaxV(newMax);
                        }
                        break;
                    case "Production":
                        var fac2 = game.GetTeamFactory(teamId);
                        if (fac2 != null)
                        {
                            fac2.Efficiency.AddPositiveV(newLevel - curLevel);
                        }
                        break;
                    case "Price":
                        // price tech is recorded; actual effect applied during trade
                        break;
                    case "Cost":
                        // cost tech recorded; production logic should consult team tech when calculating cost
                        break;
                }

                return true;
            }
        }

        private readonly UplevelManager uplevelManager;
    }
}
