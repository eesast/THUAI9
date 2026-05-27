using GameClass.GameObj;
using GameClass.GameObj.Map;
using GameClass.GameObj.Areas;
using GameEngine;
using Preparation.Utility;
using System;
using System.Threading;
using Timothy.FrameRateTask;
using Preparation.Utility.Value;
using Microsoft.Extensions.Logging;

namespace Gaming
{
    public partial class Game
    {
        private readonly ActionManager actionManager;
        private class ActionManager(Game game, Map gameMap, CharacterManager characterManager)
        {
            private readonly Game game = game;
            private readonly Map gameMap = gameMap;
            private readonly CharacterManager characterManager = characterManager;
            private readonly Random random = new();
            public readonly MoveEngine moveEngine = new(
                    gameMap: gameMap,
                    OnCollision: (obj, collisionObj, moveVec) =>
                    {
                        Character ship = (Character)obj;
                        return MoveEngine.AfterCollision.MoveMax;
                    },
                    EndMove: obj =>
                    {
                        obj.ThreadNum.Release();
                    }
                );
            public bool MoveCharacter(Character characterToMove, int moveTimeInMilliseconds, double moveDirection)
            {
                if (moveTimeInMilliseconds < 5)
                {
                    LogicLogging.logger.LogWarning("Move time is too short");
                    return false;
                }
                long stateNum = characterToMove.SetCharacterState(CharacterState.MOVING);
                if (stateNum == -1)
                {
                    LogicLogging.logger.LogWarning("Character is not commandable");
                    return false;
                }
                if (!characterToMove.ThreadNum.WaitOne(25))
                {
                    return true;
                }
                new Thread
                (
                    () =>
                    {
                        if (!characterToMove.StartThread(stateNum))
                        {
                            characterToMove.ThreadNum.Release();
                            return;
                        }

                        moveEngine.MoveObj(characterToMove, moveTimeInMilliseconds, moveDirection, stateNum, 0);
                        Thread.Sleep(moveTimeInMilliseconds);
                        characterToMove.ResetCharacterState(stateNum);
                    }
                )
                { IsBackground = true }.Start();
                return true;
            }
            public bool KnockBackCharacter(Character characterToMove, double moveDirection)
            {
                CharacterState tempState = characterToMove.CharacterState;
                long stateNum = characterToMove.SetCharacterState(CharacterState.KNOCKED_BACK);
                if (stateNum == -1)
                {
                    LogicLogging.logger.LogWarning("Character can not be knocked back");
                    return false;
                }
                if (!characterToMove.ThreadNum.WaitOne(0))
                {
                    return false;
                }
                new Thread
                (
                    () =>
                    {
                        if (!characterToMove.StartThread(stateNum))
                        {
                            characterToMove.ThreadNum.Release();
                            return;
                        }
                        moveEngine.MoveObj(characterToMove, GameData.KnockedBackTime, moveDirection, stateNum, GameData.KnockedBackSpeed);
                        Thread.Sleep(GameData.KnockedBackTime);
                        characterToMove.ResetCharacterState(stateNum);
                    }
                )
                { IsBackground = true }.Start();
                return true;
            }
            public static bool Stop(Character character)
            {
                lock (character.ActionLock)
                {
                    if (character.Commandable())
                    {
                        character.SetCharacterState(CharacterState.NULL_CHARACTER_STATE);
                        return true;
                    }
                }
                LogicLogging.logger.LogWarning("Character is not commandable");
                return false;
            }
            public bool Harvest(Character character)
            {
                Resource? resource = (Resource?)gameMap.OneForInteract(character.Position, GameObjType.RESOURCE);
                if (resource == null)
                {
                    return false;
                }
                if (resource.HP == 0)
                {
                    return false;
                }
                long stateNum = character.SetCharacterState(CharacterState.HARVESTING);
                if (stateNum == -1)
                {
                    return false;
                }
                new Thread
                (
                    () =>
                    {
                        character.ThreadNum.WaitOne();
                        if (!character.StartThread(stateNum))
                        {
                            character.ThreadNum.Release();
                            return;
                        }
                        Thread.Sleep(GameData.CheckInterval);
                        new FrameRateTaskExecutor<int>
                        (
                            loopCondition: () => stateNum == character.StateNum && gameMap.Timer.IsGaming,
                            loopToDo: () =>
                            {
                                long addresource = resource.Harvest(GameData.ProduceSpeedPerSecond / GameData.FrameDuration);

                                if (addresource <= 0)
                                {
                                    character.ResetCharacterState(stateNum);
                                    return false;
                                }

                                int effLevel = (int)character.Efficiency.GetValue();
                                double effMultiplier = 1.0 + effLevel * GameData.EfficiencyMultiplierPerLevel;
                                long adjustedAdd = (long)Math.Round(addresource * effMultiplier);

                                var teamFactory = game.GetTeamFactory((long)character.TeamID.Get());
                                if (teamFactory != null)
                                {
                                    teamFactory.AddSource(adjustedAdd);
                                }
                                if (resource.HP == 0)
                                {
                                    character.ResetCharacterState(stateNum);
                                    resource.SetResourceState(ResourceState.HARVESTED);
                                    return false;
                                }
                                return true;
                            },
                             timeInterval: GameData.CheckInterval,
                             finallyReturn: () => 0
                         ).Start();
                        character.ThreadNum.Release();

                    }
                )
                { IsBackground = true }.Start();
                LogicLogging.logger.LogInfo("Character starts harvesting resource");
                return true;
            }
            public bool Occupy(Character character)
            {
                ComputeCenter? center = (ComputeCenter?)gameMap.OneForInteract(character.Position, GameObjType.COMPUTE_CENTER);
                if (center == null) return false;
                if (character.CharacterType != CharacterType.DRONE && character.CharacterType != CharacterType.ROBOT) return false;
                long stateNum = character.SetCharacterState(CharacterState.OCUPPYING);
                if (stateNum == -1) return false;
                new Thread
                (
                    () =>
                    {
                        character.ThreadNum.WaitOne();
                        if (!character.StartThread(stateNum))
                        {
                            character.ThreadNum.Release();
                            return;
                        }
                        Thread.Sleep(GameData.CheckInterval);
                        int effLevel = (int)character.Efficiency.GetValue();
                        double effMultiplier = 1.0 + effLevel * GameData.EfficiencyMultiplierPerLevel;
                        int occupyTimeMs = Math.Max(1, (int)Math.Round(GameData.ComputeCenterOccupyTimeMs / effMultiplier));

                        int elapsed = 0;
                        new FrameRateTaskExecutor<int>
                        (
                            loopCondition: () => stateNum == character.StateNum && gameMap.Timer.IsGaming,
                            loopToDo: () =>
                            {
                                if (!GameData.ApproachToInteract(character.Position, center.Position))
                                {
                                    character.ResetCharacterState(stateNum);
                                    return false;
                                }
                                elapsed += GameData.CheckInterval;
                                if (elapsed >= occupyTimeMs)
                                {
                                    center.SetOccupied(character.TeamID.Get());
                                    character.ResetCharacterState(stateNum);
                                    return false;
                                }
                                return true;
                            },
                            timeInterval: GameData.CheckInterval,
                            finallyReturn: () => 0
                        ).Start();
                        character.ThreadNum.Release();
                    }
                )
                { IsBackground = true }.Start();
                return true;
            }

            public bool Attack(Character character, Character gameobj)
            {
                if (!gameMap.CanSee(character, gameobj))
                {
                    LogicLogging.logger.LogDebug("Can't see target obj!");
                    return false;
                }
                if (!gameMap.InAttackSize(character, gameobj))
                {
                    LogicLogging.logger.LogDebug("Obj is not in attacksize!");
                    return false;
                }
                if (gameobj.Visible == false)
                {
                    LogicLogging.logger.LogDebug(
                        "Can't see target because it's invisible!"
                    );
                    return false;
                }
                long nowtime = Environment.TickCount64;
                int atkFreq = character.ATKFrequency;
                if (atkFreq > 0 && nowtime - character.LastAttackTime < 1000 / atkFreq)
                {
                    LogicLogging.logger.LogDebug("Common_attack is still in cd!");
                    return false;
                }
                long stateNum = character.SetCharacterState(CharacterState.ATTACKING);
                if (stateNum == -1)
                {
                    LogicLogging.logger.LogDebug("Character is not commandable!");
                    return false;
                }
                characterManager.BeAttacked(gameobj, character);
                character.LastAttackTime = nowtime;
                character.ResetCharacterState(stateNum);
                if (character.Visible == false)
                {
                    character.Visible = true;
                    character.SetCharacterState(character.CharacterState);
                }
                return true;
            }

            public bool Attack(Character character, Factory gameobj)
            {
                if (!gameMap.CanSee(character, gameobj))
                {
                    LogicLogging.logger.LogDebug("Can't see target obj!");
                    return false;
                }
                if (!gameMap.InAttackSize(character, gameobj))
                {
                    LogicLogging.logger.LogDebug("Obj is not in attacksize!");
                    return false;
                }
                long nowtime = Environment.TickCount64;
                int atkFreq = character.ATKFrequency;
                if (atkFreq > 0 && nowtime - character.LastAttackTime < 1000 / atkFreq)
                {
                    LogicLogging.logger.LogDebug("Common_attack is still in cd!");
                    return false;
                }
                long stateNum = character.SetCharacterState(CharacterState.ATTACKING);
                if (stateNum == -1)
                {
                    LogicLogging.logger.LogDebug("Character is not commandable!");
                    return false;
                }

                // 已摧毁的工厂不再受攻击、不再加分
                if (gameobj.HP <= 0)
                {
                    LogicLogging.logger.LogDebug("Factory is already destroyed!");
                    return false;
                }

                // 前7分钟工厂不掉血
                if (game.NowTime() < GameData.FactoryInvulnerableTimeMs)
                {
                    LogicLogging.logger.LogDebug("Factory is invulnerable in the first 7 minutes!");
                    return false;
                }

                long damage = (long)(character.AttackPower - gameobj.Robust);
                if (damage <= 0) damage = 1;
                long actualSub = gameobj.HP.SubPositiveVRChange(damage);
                game.AddTeamScore((long)character.TeamID.Get(), actualSub);
                gameobj.Interupt();
                new Thread(() =>
                {
                    Thread.Sleep(GameData.FactoryDisableTimeMs);
                    gameobj.CanProduce.SetROri(true);
                    gameobj.CanRecruit.SetROri(true);
                })
                { IsBackground = true }.Start();
                if (gameobj.HP <= 0)
                {
                    game.AddTeamScore(character.TeamID.Get(), GameData.FactoryScore);
                }
                character.LastAttackTime = nowtime;
                character.ResetCharacterState(stateNum);
                if (character.Visible == false)
                {
                    character.Visible = true;
                    character.SetCharacterState(character.CharacterState);
                }
                return true;
            }

            public bool Load(Character character, GoodsType type, int amount)
            {
                if (amount <= 0) return false;
                Factory? factory = (Factory?)gameMap.OneForInteract(character.Position, GameObjType.FACTORY);
                if (factory == null) return false;
                if (!GameData.ApproachToInteract(character.Position, factory.Position)) return false;

                var atomic = factory.GetGoodsAtomic(type);
                while (true)
                {
                    int current = atomic.Get();
                    if (current < amount) return false;
                    if (atomic.CompareExROri(current - amount, current) == current) break;
                }

                if (!character.GoodsLoad.Add(type, amount))
                {
                    factory.AddGoods(type, amount);
                    return false;
                }
                return true;
            }

        }
    }
}
