using System;
using System.Collections.Concurrent;
using GameClass.GameObj.Occupations;
using GameClass.GameObj.Map;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using GameClass.GameObj;
using GameClass.GameObj.Areas;
using GameEngine;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;

namespace Gaming
{
    public partial class Game
    {
        private readonly CharacterManager characterManager;

        private sealed class CharacterManager(Game game, Map gameMap)
        {
            private readonly Game game = game;
            private readonly Map map = gameMap;
            private readonly ConcurrentDictionary<long, ConcurrentDictionary<long, Character>> teamCharacters = new(); // key: TeamID -> (PlayerID -> Character)

            public Character CreateCharacter(long teamId, long playerId, CharacterType type)
            {
                var ch = new Character(GameData.CharacterRadius, type);
                ch.TeamID.SetROri(teamId);
                ch.PlayerID.SetROri(playerId);
                CheckTech(teamId, ch);
                // store only in teamCharacters (use teamId + playerId as key)
                var teamDict = teamCharacters.GetOrAdd(teamId, _ => new ConcurrentDictionary<long, Character>());
                teamDict[playerId] = ch;
                // ensure the character is also added to the map collection
                map.Add(ch);
                LogicLogging.logger.LogInfo($"Character created: Team {teamId}, Player {playerId}, Type {type}");
                return ch;
            }

            public bool RecruitCharacter(long teamId, long playerId, CharacterType type, XY birthPos)
            {
                var factory = game.GetTeamFactory(teamId);
                if (factory == null)
                {
                    LogicLogging.logger.LogError($"Factory not found for Team {teamId} when recruiting character.");
                    return false;
                }
                if (!factory.CanRecruit.Get())
                {
                    LogicLogging.logger.LogWarning($"Factory for Team {teamId} cannot recruit at this time.");
                    return false;
                }
                if (teamCharacters.TryGetValue(teamId, out var dict) && dict.Count >= GameData.MaxCharactersPerTeam)
                {
                    LogicLogging.logger.LogWarning($"Team {teamId} has reached the maximum character limit ({GameData.MaxCharactersPerTeam}). Cannot recruit more.");
                    return false;
                }
                var occ = OccupationFactory.FindIOccupation(type);
                int cost = occ.Cost;
                if (factory.ComputingPower.Get() < cost)
                {
                    LogicLogging.logger.LogWarning($"Not enough computing power for Team {teamId} to recruit character. Required: {cost}, Available: {factory.ComputingPower.Get()}");
                    return false;
                }
                factory.ComputingPower.SubRNow(cost);
                var ch = CreateCharacter(teamId, playerId, type);
                ActivateCharacter(teamId, playerId, birthPos);
                return true;
            }

            public bool ActivateCharacter(long teamId, long playerId, XY pos)
            {
                if (!teamCharacters.TryGetValue(teamId, out var dict))
                {
                    LogicLogging.logger.LogError($"Team {teamId} not found when activating character.");
                    return false;
                }
                if (!dict.TryGetValue(playerId, out var ch))
                {
                    LogicLogging.logger.LogError($"Character for Team {teamId}, Player {playerId} not found when activating.");
                    return false;
                }
                ch.IsRemoved.SetROri(false);
                ch.CanMove.SetROri(true);

                // 如果建议出生位置与已有物体发生碰撞（例如工厂内部），尝试在工厂附近但在工厂碰撞范围之外寻找合法出生点
                XY spawnPos = pos;
                try
                {
                    if (IsPositionColliding(ch, spawnPos))
                    {
                        var factory = game.GetTeamFactory(teamId);
                        if (factory != null)
                        {
                            var found = TryFindNearbyFreePosition(ch, factory.Position, out var newPos);
                            if (found) spawnPos = newPos;
                        }
                        // 如果找不到合法点则仍使用原位置（可能会导致角色被卡住），但尽量避免该情况
                    }
                }
                catch
                {
                    LogicLogging.logger.LogError($"Error occurred while checking for collisions or finding spawn position for Team {teamId}, Player {playerId}. Defaulting to original position.");
                }

                ch.ReSetPos(spawnPos);
                ch.SetCharacterState(CharacterState.IDLE);
                return true;
            }

            // 检查指定位置是否会和地图上现存的其它物体发生碰撞或超出边界
            private bool IsPositionColliding(Character ch, XY pos)
            {
                // 边界检查（确保整个角色在地图内）
                if (pos.x < ch.Radius || pos.x > GameData.MapLength - ch.Radius || pos.y < ch.Radius || pos.y > GameData.MapLength - ch.Radius)
                    return true;

                // 遍历地图上所有对象，使用 LockedClassList 提供的 ToNewList() 做安全的快照遍历
                foreach (var kv in map.GameObjDict)
                {
                    var listSnapshot = kv.Value.ToNewList();
                    if (listSnapshot == null) continue;
                    for (int i = 0; i < listSnapshot.Count; i++)
                    {
                        var obj = listSnapshot[i];
                        if (obj == null) continue;
                        if (obj.ID == ch.ID) continue; // 跳过自己
                        try
                        {
                            if (((IMovable)ch).WillCollideWith(obj, pos)) return true;
                        }
                        catch
                        {
                            LogicLogging.logger.LogError($"Error occurred while checking collision between Character (Team {ch.TeamID.Get()}, Player {ch.PlayerID.Get()}) and object ID {obj.ID}. Ignoring this object for collision check.");
                        }
                    }
                }
                LogicLogging.logger.LogDebug($"Position {pos} is free for Character (Team {ch.TeamID.Get()}, Player {ch.PlayerID.Get()}).");
                return false;
            }

            // 在基准位置附近按环形搜索一个非碰撞点
            private bool TryFindNearbyFreePosition(Character ch, XY center, out XY result)
            {
                // 从工厂半径 + 角色半径开始，保证与工厂外部至少间隔1
                int startDist = GameData.FactoryRadius + ch.Radius + 1;
                int maxDist = GameData.NumOfPosGridPerCell * 3; // 最多搜索 3 个格子的半径
                int distStep = Math.Max(1, GameData.NumOfPosGridPerCell / 8);
                int angleStepDeg = 20;

                for (int d = startDist; d <= maxDist; d += distStep)
                {
                    for (int ang = 0; ang < 360; ang += angleStepDeg)
                    {
                        double rad = Math.PI * ang / 180.0;
                        var cand = new XY(rad, d);
                        var candPos = new XY(center.x + cand.x, center.y + cand.y);

                        if (!IsPositionColliding(ch, candPos))
                        {
                            result = candPos;
                            return true;
                        }
                    }
                }

                result = center; // fallback
                return false;
            }

            private void CheckTech(long teamId, Character ch)
            {
                if (!game.teams.TryGetValue(teamId, out var t))
                {
                    LogicLogging.logger.LogError($"Team {teamId} not found when checking tech for character.");
                    return;
                }
                int effLevel = t.GetTech("Efficiency");
                int robustLevel = t.GetTech("Robust");
                int warriorLevel = t.GetTech("Warrior");

                if (effLevel > 0)
                {
                    ch.Efficiency.AddPositiveV(effLevel);
                }

                if (robustLevel > 0)
                {
                    long baseHp = ch.Occupation.MaxHp;
                    long newMaxHp = (long)(baseHp * (1.0 + GameData.TechHpMultiplierPerLevel * robustLevel));
                    ch.HP.SetMaxV(newMaxHp);
                    ch.HP.SetVToMaxV();
                    ch.Robust.AddPositiveV(robustLevel * GameData.TechRobustAddPerLevel);
                }

                if (warriorLevel > 0)
                {
                    long baseAtk = ch.Occupation.AttackPower;
                    long extra = (long)(baseAtk * GameData.TechWarriorAtkMultiplierPerLevel * warriorLevel);
                    if (extra > 0)
                    {
                        ch.AttackPower.AddPositiveV(extra);
                        ch.AttackPower.SetMaxV(ch.AttackPower + extra);
                    }
                }
            }

            public bool TryGetCharacter(long playerId, out Character character)
            {
                // legacy lookup: search all teams for the first match (keeps some backward compatibility)
                foreach (var teamDict in teamCharacters.Values)
                {
                    if (teamDict.TryGetValue(playerId, out character!)) return true;
                }
                character = null!;
                LogicLogging.logger.LogWarning($"Character for Player {playerId} not found in any team.");
                return false;
            }

            public bool TryGetCharacter(long teamId, long playerId, out Character character)
            {
                character = null!;
                if (teamCharacters.TryGetValue(teamId, out var dict))
                {
                    return dict.TryGetValue(playerId, out character!);
                }
                LogicLogging.logger.LogWarning($"Team {teamId} not found when trying to get character for Player {playerId}.");
                return false;
            }

            public IEnumerable<Character> GetTeamCharacters(long teamId)
            {
                if (teamCharacters.TryGetValue(teamId, out var dict))
                    return dict.Values;
                return Array.Empty<Character>();
            }

            public bool Destroy(long teamId, long playerId, CharacterState state = CharacterState.NULL_CHARACTER_STATE)
            {
                if (!teamCharacters.TryGetValue(teamId, out var dict))
                {
                    LogicLogging.logger.LogError($"Team {teamId} not found when trying to destroy character for Player {playerId}.");
                    return false;
                }
                if (!dict.TryGetValue(playerId, out var ch))
                {
                    LogicLogging.logger.LogError($"Character for Team {teamId}, Player {playerId} not found when trying to destroy.");
                    return false;
                }
                if (!ch.TryToRemoveFromGame(state))
                {
                    LogicLogging.logger.LogWarning($"Failed to remove Character for Team {teamId}, Player {playerId} from game.");
                    return false;
                }
                ch.CanMove.SetROri(false);
                dict.TryRemove(playerId, out _);
                map.Remove(ch);
                return true;
            }

            public void BeAttacked(Character character, Character obj)
            {
                if (obj.TeamID.Get() == character.TeamID.Get())
                {
                    LogicLogging.logger.LogWarning($"Character (Team {character.TeamID.Get()}, Player {character.PlayerID.Get()}) is being attacked by an ally (Team {obj.TeamID.Get()}, Player {obj.PlayerID.Get()}). No damage applied.");
                    return;
                }
                long subHP = (long)(obj.AttackPower - character.Robust);
                if (subHP < 0) subHP = 0;
                game.AddTeamScore((long)obj.TeamID.Get(), subHP * GameData.CharacterDamageScoreMultiplier);
                character.HP.SubPositiveV(subHP);
                if (character.HP == 0)
                {
                    long score = 0;
                    switch (character.CharacterType)
                    {
                        case CharacterType.DRONE:
                            score = GameData.DroneCost * GameData.CharacterKillScoreMultiplier;
                            break;
                        case CharacterType.ROBOT:
                            score = GameData.RobotCost * GameData.CharacterKillScoreMultiplier;
                            break;
                        case CharacterType.AUTONOMOUS_CAR:
                            score = GameData.AutonomouCarCost * GameData.CharacterKillScoreMultiplier;
                            break;
                    }
                    game.AddTeamScore((long)obj.TeamID.Get(), score);
                    character.SetCharacterState(CharacterState.NULL_CHARACTER_STATE);
                    Destroy((long)character.TeamID.Get(), (long)character.PlayerID.Get());
                }
            }

            public bool Recover(Character character, long recover)
            {
                if (recover <= 0)
                {
                    LogicLogging.logger.LogWarning($"Attempted to recover non-positive HP for Character (Team {character.TeamID.Get()}, Player {character.PlayerID.Get()}). No HP recovered.");
                    return false;
                }
                character.HP.AddPositiveV(recover);
                return true;
            }

            public bool ImproveATK(Character character, long ATK)
            {
                if (ATK <= 0)
                {
                    LogicLogging.logger.LogWarning($"Attempted to improve attack power by non-positive value for Character (Team {character.TeamID.Get()}, Player {character.PlayerID.Get()}). No attack power improved.");
                    return false;
                }
                character.AttackPower.SetMaxV(character.AttackPower + ATK);
                character.AttackPower.AddPositiveV(ATK);
                return true;
            }

            public bool ImproveEfficiency(Character character, long efficiency)
            {
                if (efficiency <= 0)
                {
                    LogicLogging.logger.LogWarning($"Attempted to improve efficiency by non-positive value for Character (Team {character.TeamID.Get()}, Player {character.PlayerID.Get()}). No efficiency improved.");
                    return false;
                }
                character.Efficiency.AddPositiveV(efficiency);
                return true;
            }

        }
    }
}
