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
                        key = "Robust"; cost = GameData.TechCostRobust; break;
                    case TechType.INCREASE_ATTACK_POWER:
                    case TechType.INCREASE_ATTACK_SIZE:
                        key = "Warrior"; cost = GameData.TechCostWarrior; break;
                    case TechType.INCREASE_MOVE_SPEED:
                        key = "MoveSpeed"; cost = GameData.TechCostMoveSpeed; break;
                    case TechType.INCREASE_CARRY_CAPACITY:
                        key = "Carry"; cost = GameData.TechCostCarry; break;
                    case TechType.INCREASE_EFFICIENCY:
                        key = "Efficiency"; cost = GameData.TechCostEfficiency; break;
                    case TechType.INCREASE_PRODUCTION:
                        key = "Production"; cost = GameData.TechCostProduction; break;
                    case TechType.INCREASE_STORAGE:
                        key = "Storage"; cost = GameData.TechCostStorage; break;
                    case TechType.INCREASE_PRICE:
                        key = "Price"; cost = GameData.TechCostPrice; break;
                    case TechType.DECREASE_COST:
                        key = "Cost"; cost = GameData.TechCostDecreaseCost; break;
                    default:
                        return false;
                }

                int curLevel = teamState.GetTech(key);
                if (curLevel >= GameData.TechMaxLevel) return false;

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
                            ch.Efficiency.AddPositiveV((newLevel - curLevel)*GameData.TechEfficiencyAddPerLevel);
                        }
                        break;
                    case "Robust":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            long baseHp = ch.Occupation.MaxHp;
                            long newMaxHp = (long)(baseHp * (1.0 + GameData.TechHpMultiplierPerLevel * newLevel));
                            ch.HP.SetMaxV(newMaxHp);
                            ch.HP.SetVToMaxV();
                            ch.Robust.AddPositiveV((newLevel - curLevel) * GameData.TechRobustAddPerLevel);
                        }
                        break;
                    case "Warrior":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            long baseAtk = ch.Occupation.AttackPower;
                            long extra = (long)(baseAtk * GameData.TechWarriorAtkMultiplierPerLevel * (newLevel - curLevel));
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
                            int delta = GameData.TechMoveSpeedAddPerLevel * (newLevel - curLevel);
                            ch.MoveSpeed.AddPositive(delta);
                        }
                        break;
                    case "Carry":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            long newMax = ch.Carry.GetMaxV() + GameData.TechCarryAddPerLevel * (newLevel - curLevel);
                            ch.Carry.SetPositiveMaxV(newMax);
                            ch.Carry.SetVToMaxV();
                        }
                        break;
                    case "Storage":
                        var fac = game.GetTeamFactory(teamId);
                        if (fac != null)
                        {
                            long newMax = fac.Storage.GetMaxV() + GameData.TechStorageAddPerLevel * (newLevel - curLevel);
                            fac.Storage.SetPositiveMaxV(newMax);
                        }
                        break;
                    case "Production":
                        var fac2 = game.GetTeamFactory(teamId);
                        if (fac2 != null)
                        {
                            fac2.Efficiency.AddPositiveV(GameData.TechProductionEfficiencyAddPerLevel * (newLevel - curLevel));
                        }
                        break;
                    case "Price":
                        // price tech is recorded; actual effect applied during trade using GameData.TechPriceMultiplierPerLevel
                        break;
                    case "Cost":
                        // cost tech recorded; production logic should consult team tech and GameData.TechCostDecreasePerLevel when calculating cost
                        break;
                }

                return true;
            }
        }

        private readonly UplevelManager uplevelManager;
    }
}
