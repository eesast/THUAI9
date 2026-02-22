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
            private readonly ConcurrentDictionary<long, Character> characters = new();
            private readonly ConcurrentDictionary<long, ConcurrentDictionary<long, Character>> teamCharacters = new(); // key: TeamID -> (PlayerID -> Character)

            public Character CreateCharacter(long teamId, long playerId, CharacterType type)
            {
                var ch = new Character(GameData.CharacterRadius, type);
                ch.TeamID.SetROri(teamId);
                ch.PlayerID.SetROri(playerId);
                CheckTech(teamId, ch);
                characters[playerId] = ch;
                var teamDict = teamCharacters.GetOrAdd(teamId, _ => new ConcurrentDictionary<long, Character>());
                teamDict[playerId] = ch;
                return ch;
            }

            public bool RecruitCharacter(long teamId, long playerId, CharacterType type, XY birthPos)
            {
                var factory = game.GetTeamFactory(teamId);
                if (factory == null) return false;
                if (!factory.CanRecruit.Get()) return false;
                var occ = OccupationFactory.FindIOccupation(type);
                int cost = occ.Cost;
                if (factory.ComputingPower.Get() < cost) return false;
                factory.ComputingPower.SubRNow(cost);
                var ch = CreateCharacter(teamId, playerId, type);
                ActivateCharacter(playerId, birthPos);
                return true;
            }

            public bool ActivateCharacter(long playerId, XY pos)
            {
                if (!characters.TryGetValue(playerId, out var ch)) return false;
                ch.IsRemoved.SetROri(false);
                ch.CanMove.SetROri(true);
                ch.ReSetPos(pos);
                ch.SetCharacterState(CharacterState.IDLE);
                return true;
            }

            public bool TryGetCharacter(long playerId, out Character character)
                => characters.TryGetValue(playerId, out character!);

            public IEnumerable<Character> GetTeamCharacters(long teamId)
            {
                if (teamCharacters.TryGetValue(teamId, out var dict))
                    return dict.Values;
                return Array.Empty<Character>();
            }

            public bool Destroy(long playerId, CharacterState state = CharacterState.NULL_CHARACTER_STATE)
            {
                if (!characters.TryGetValue(playerId, out var ch)) return false;
                if (!ch.TryToRemoveFromGame(state)) return false;
                ch.CanMove.SetROri(false);
                characters.TryRemove(playerId, out _);
                var teamId = ch.TeamID.Get();
                if (teamCharacters.TryGetValue(teamId, out var dict))
                {
                    dict.TryRemove(playerId, out _);
                }
                map.Remove(ch);
                return true;
            }

            public void BeAttacked(Character character, Character obj)
            {
                if (obj.TeamID.Get() == character.TeamID.Get())
                {
                    return;
                }
                long subHP = (long)(obj.AttackPower - character.Robust);
                var team0 = game.teams[(long)obj.TeamID.Get()];
                game.AddTeamScore((long)obj.TeamID.Get(), subHP * 20);
                character.HP.SubPositiveV(subHP);
                if (character.HP == 0)
                {
                    long score = 0;
                    switch (character.CharacterType)
                    {
                        case CharacterType.DRONE:
                            score = GameData.DroneCost * 40;
                            break;
                        case CharacterType.ROBOT:
                            score = GameData.RobotCost * 40;
                            break;
                        case CharacterType.AUTONOMOUS_CAR:
                            score = GameData.AutonomouCarCost * 40;
                            break;
                    }
                    game.AddTeamScore((long)obj.TeamID.Get(), score);
                    character.SetCharacterState(CharacterState.NULL_CHARACTER_STATE);
                    Destroy((long)character.PlayerID.Get());
                }
            }

            public bool Recover(Character character, long recover)
            {
                if (recover <= 0)
                    return false;
                character.HP.AddPositiveV(recover);
                return true;
            }

            public bool ImproveATK(Character character, long ATK)
            {
                if (ATK <= 0)
                    return false;
                character.AttackPower.SetMaxV(character.AttackPower + ATK);
                character.AttackPower.AddPositiveV(ATK);
                return true;
            }

            public bool ImproveEfficiency(Character character, long efficiency)
            {
                if (efficiency <= 0)
                    return false;
                character.Efficiency.AddPositiveV(efficiency);
                return true;
            }

            private void CheckTech(long teamId, Character ch)
            {
                if (!game.teams.TryGetValue(teamId, out var t)) return;

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
                    long newMaxHp = (long)(baseHp * (1.0 + 0.2 * robustLevel));
                    ch.HP.SetMaxV(newMaxHp);
                    ch.HP.SetVToMaxV();
                    ch.Robust.AddPositiveV(robustLevel * 2);
                }

                if (warriorLevel > 0)
                {
                    long baseAtk = ch.Occupation.AttackPower;
                    long extra = (long)(baseAtk * 0.3 * warriorLevel);
                    if (extra > 0)
                    {
                        ch.AttackPower.AddPositiveV(extra);
                        ch.AttackPower.SetMaxV(ch.AttackPower + extra);
                    }
                }
            }

        }
    }
}
