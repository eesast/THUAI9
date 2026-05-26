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

            public bool UplevelTech(long teamId, TechType tech)
            {
                if (!game.teams.TryGetValue(teamId, out var teamState))
                {
                    LogicLogging.logger.LogError($"Failed to uplevel tech for team {teamId} because team not found");
                    return false;
                }
                string key;
                int cost;
                switch (tech)
                {
                    case TechType.INCREASE_HP:
                        key = "HP"; cost = GameData.TechCostHP; break;
                    case TechType.INCREASE_ROBUST:
                        key = "Robust"; cost = GameData.TechCostRobust; break;
                    case TechType.INCREASE_ATTACK_POWER:
                        key = "Warrior"; cost = GameData.TechCostWarrior; break;
                    case TechType.INCREASE_ATTACK_SIZE:
                        key = "AttackSize"; cost = GameData.TechCostAttackSize; break;
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
                if (curLevel >= GameData.TechMaxLevel)
                {
                    LogicLogging.logger.LogDebug($"Failed to uplevel tech {key} for team {teamId} because current level {curLevel} is already at or above max level");
                    return false;
                }
                var factory = game.GetTeamFactory(teamId);
                if (factory == null)
                {
                    LogicLogging.logger.LogError($"Failed to uplevel tech {key} for team {teamId} because team factory not found");
                    return false;
                }
                while (true)
                {
                    long cur = factory.ComputingPower.Get();
                    if (cur < cost)
                    {
                        LogicLogging.logger.LogDebug($"Failed to uplevel tech {key} for team {teamId} because current computing power {cur} is less than cost {cost}");
                        return false;
                    }
                    if (factory.ComputingPower.CompareExROri(cur - cost, cur) == cur) break;
                }

                bool setOk = teamState.TrySetTech(key, curLevel + 1);
                if (!setOk)
                {
                    factory.AddComputingPower(cost);
                    LogicLogging.logger.LogError($"Failed to uplevel tech {key} for team {teamId} because failed to update tech level in team state");
                    return false;
                }

                int newLevel = curLevel + 1;
                switch (key)
                {
                    case "Efficiency":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            ch.Efficiency.AddPositiveV((newLevel - curLevel) * GameData.TechEfficiencyAddPerLevel);
                        }
                        break;
                    case "HP":
                        {
                            // 角色 HP
                            foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                            {
                                long baseHp = ch.Occupation.MaxHp;
                                long newMaxHp = (long)(baseHp * (1.0 + GameData.TechHpMultiplierPerLevel * newLevel));
                                ch.HP.SetMaxV(newMaxHp);
                                ch.HP.SetVToMaxV();
                            }
                            // 工厂 HP
                            var facHp = game.GetTeamFactory(teamId);
                            if (facHp != null)
                            {
                                long baseFacHp = GameData.FactoryHP;
                                long newFacMaxHp = (long)(baseFacHp * (1.0 + GameData.TechHpMultiplierPerLevel * newLevel));
                                if (newFacMaxHp > GameData.MaxHP) newFacMaxHp = GameData.MaxHP;
                                facHp.HP.SetMaxV(newFacMaxHp);
                                facHp.HP.SetVToMaxV();
                            }
                        }
                        break;
                    case "Robust":
                        {
                            // 角色 Robust（减伤）
                            foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                            {
                                ch.Robust.AddPositiveV((newLevel - curLevel) * GameData.TechRobustAddPerLevel);
                            }
                            // 工厂 Robust
                            var facRob = game.GetTeamFactory(teamId);
                            if (facRob != null)
                            {
                                facRob.Robust.AddPositiveV((newLevel - curLevel) * GameData.TechRobustAddPerLevel);
                            }
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
                    case "AttackSize":
                        foreach (var ch in game.characterManager.GetTeamCharacters(teamId))
                        {
                            long newSize = ch.AttackSize.GetMaxV() + GameData.TechAttackSizeAddPerLevel * (newLevel - curLevel);
                            ch.AttackSize.SetPositiveMaxV(newSize);
                            ch.AttackSize.AddPositiveV(GameData.TechAttackSizeAddPerLevel * (newLevel - curLevel));
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
