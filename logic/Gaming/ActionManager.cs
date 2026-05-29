using GameClass.GameObj;
using GameClass.GameObj.Map;
using GameClass.GameObj.Areas;
using GameEngine;
using Preparation.Utility;
using System;
using System.Threading;
using System.Threading.Tasks;
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
                        try { obj.ThreadNum.Release(); } catch (SemaphoreFullException) { }
                    }
                );
            public bool MoveCharacter(Character characterToMove, int moveTimeInMilliseconds, double moveDirection)
            {
                if (moveTimeInMilliseconds < 5)
                {
                    LogicLogging.logger.LogWarning("Move time is too short");
                    return false;
                }
                // Acquire semaphore first — don't change state if character is busy
                if (!characterToMove.ThreadNum.WaitOne(0))
                {
                    return false; // Character already executing another action
                }
                long stateNum = characterToMove.SetCharacterState(CharacterState.MOVING);
                if (stateNum == -1)
                {
                    characterToMove.ThreadNum.Release();
                    LogicLogging.logger.LogWarning("Character is not commandable");
                    return false;
                }
                // StartThread 验证 stateNum 后直接启动 MoveObj，由 MoveObj 内部 LongRunning 任务负责
                // 完成后回调 ResetCharacterState，不再需要额外的 wrapper 线程
                if (!characterToMove.StartThread(stateNum))
                {
                    characterToMove.ThreadNum.Release();
                    characterToMove.ResetCharacterState(stateNum);
                    return false;
                }
                try
                {
                    moveEngine.MoveObj(characterToMove, moveTimeInMilliseconds, moveDirection, stateNum, 0,
                        onComplete: () => characterToMove.ResetCharacterState(stateNum));
                }
                catch (Exception ex)
                {
                    // MoveObj 内部异常不应泄漏信号量，必须在此恢复角色状态
                    LogicLogging.logger.LogError(
                        LoggingFunctional.CharacterLogInfo(characterToMove) +
                        $" MoveObj threw an exception: {ex}");
                    characterToMove.ThreadNum.Release();
                    characterToMove.ResetCharacterState(stateNum);
                    return false;
                }
                return true;
            }
            public bool KnockBackCharacter(Character characterToMove, double moveDirection)
            {
                if (!characterToMove.ThreadNum.WaitOne(0))
                {
                    return false;
                }
                long stateNum = characterToMove.SetCharacterState(CharacterState.KNOCKED_BACK);
                if (stateNum == -1)
                {
                    characterToMove.ThreadNum.Release();
                    LogicLogging.logger.LogWarning("Character can not be knocked back");
                    return false;
                }
                if (!characterToMove.StartThread(stateNum))
                {
                    characterToMove.ThreadNum.Release();
                    characterToMove.ResetCharacterState(stateNum);
                    return false;
                }
                try
                {
                    moveEngine.MoveObj(characterToMove, GameData.KnockedBackTime, moveDirection, stateNum, GameData.KnockedBackSpeed,
                        onComplete: () => characterToMove.ResetCharacterState(stateNum));
                }
                catch (Exception ex)
                {
                    LogicLogging.logger.LogError(
                        LoggingFunctional.CharacterLogInfo(characterToMove) +
                        $" MoveObj (knockback) threw an exception: {ex}");
                    characterToMove.ThreadNum.Release();
                    characterToMove.ResetCharacterState(stateNum);
                    return false;
                }
                return true;
            }
            public static bool Stop(Character character)
            {
                lock (character.ActionLock)
                {
                    if (character.Commandable())
                    {
                        character.SetCharacterState(CharacterState.NULL_CHARACTER_STATE);
                        // Release semaphore in case the running task's finally hasn't executed yet
                        try { character.ThreadNum.Release(); } catch (SemaphoreFullException) { }
                        return true;
                    }
                }
                LogicLogging.logger.LogWarning("Character is not commandable");
                return false;
            }
            public bool Harvest(Character character)
            {
                Resource? resource = (Resource?)gameMap.OneForInteract(character.Position, GameObjType.RESOURCE);
                if (resource == null) return false;
                if (resource.HP == 0) return false;

                // Acquire semaphore first, same pattern as MoveCharacter
                if (!character.ThreadNum.WaitOne(0))
                    return false;
                long stateNum = character.SetCharacterState(CharacterState.HARVESTING);
                if (stateNum == -1)
                {
                    character.ThreadNum.Release();
                    return false;
                }
                if (!character.StartThread(stateNum))
                {
                    character.ThreadNum.Release();
                    character.ResetCharacterState(stateNum);
                    return false;
                }
                Task.Factory.StartNew(() =>
                    {
                        try
                        {
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
                        }
                        catch (Exception ex)
                        {
                            LogicLogging.logger.LogError(
                                LoggingFunctional.CharacterLogInfo(character) +
                                $" Harvest task crashed: {ex}");
                        }
                        finally
                        {
                            character.ResetCharacterState(stateNum);
                            try { character.ThreadNum.Release(); } catch (SemaphoreFullException) { }
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                LogicLogging.logger.LogInfo("Character starts harvesting resource");
                return true;
            }
            public bool Occupy(Character character)
            {
                ComputeCenter? center = (ComputeCenter?)gameMap.OneForInteract(character.Position, GameObjType.COMPUTE_CENTER);
                if (center == null) return false;
                if (character.CharacterType != CharacterType.DRONE && character.CharacterType != CharacterType.ROBOT) return false;
                // 拒绝占领已被己方占领的中心，避免白白浪费 10 秒
                if (center.IsOccupied && center.OccupiedByTeamId == character.TeamID.Get())
                    return false;

                if (!character.ThreadNum.WaitOne(0))
                    return false;
                long stateNum = character.SetCharacterState(CharacterState.OCUPPYING);
                if (stateNum == -1)
                {
                    character.ThreadNum.Release();
                    return false;
                }
                if (!character.StartThread(stateNum))
                {
                    character.ThreadNum.Release();
                    character.ResetCharacterState(stateNum);
                    return false;
                }
                Task.Factory.StartNew(() =>
                    {
                        try
                        {
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
                        }
                        catch (Exception ex)
                        {
                            LogicLogging.logger.LogError(
                                LoggingFunctional.CharacterLogInfo(character) +
                                $" Occupy task crashed: {ex}");
                        }
                        finally
                        {
                            character.ResetCharacterState(stateNum);
                            try { character.ThreadNum.Release(); } catch (SemaphoreFullException) { }
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
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
                double atkFreq = character.ATKFrequency;
                if (atkFreq > 0 && nowtime - character.LastAttackTime < 1000.0 / atkFreq)
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
                double atkFreq = character.ATKFrequency;
                if (atkFreq > 0 && nowtime - character.LastAttackTime < 1000.0 / atkFreq)
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
                    LogicLogging.logger.LogDebug("Factory is invulnerable in the first 20 seconds!");
                    return false;
                }

                long damage = (long)(character.AttackPower - gameobj.Robust);
                if (damage <= 0) damage = 1;
                long actualSub = gameobj.HP.SubPositiveVRChange(damage);
                game.AddTeamScore((long)character.TeamID.Get(), actualSub);
                gameobj.Interupt();
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(GameData.FactoryDisableTimeMs);
                    if (gameobj.HP > 0)
                    {
                        gameobj.CanProduce.SetROri(true);
                        gameobj.CanRecruit.SetROri(true);
                    }
                },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
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
