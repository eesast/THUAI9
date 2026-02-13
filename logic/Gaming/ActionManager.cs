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
                new Thread
                (
                    () =>
                    {
                        characterToMove.ThreadNum.WaitOne();
                        if (!characterToMove.StartThread(stateNum))
                        {
                            characterToMove.ThreadNum.Release();
                            return;
                        }
                        moveEngine.MoveObj(characterToMove, moveTimeInMilliseconds, moveDirection, characterToMove.StateNum, characterToMove.Efficiency);
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
                new Thread
                (
                    () =>
                    {
                        characterToMove.ThreadNum.WaitOne();
                        if (!characterToMove.StartThread(stateNum))
                        {
                            characterToMove.ThreadNum.Release();
                            return;
                        }
                        moveEngine.MoveObj(characterToMove, GameData.KnockedBackTime, moveDirection, characterToMove.StateNum, GameData.KnockedBackSpeed);
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
                                var teamFactory = game.GetTeamFactory((long)character.TeamID.Get());
                                if (teamFactory != null)
                                {
                                    teamFactory.AddSource(addresource);
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
                return false;
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
                        int occupyTimeMs = GameData.ComputeCenterOccupyTimeMs;
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
                return false;
            }


        }
    }
}
