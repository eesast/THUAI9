using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GameClass.GameObj;
using GameClass.GameObj.Areas;
using GameEngine;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;

namespace Game
{
    public partial class Game
    {
        private readonly CharacterManager characterManager;

        private sealed class CharacterManager
        {
            private readonly Game game;
            private readonly MoveEngine moveEngine;
            // 索引1：按玩家ID检索
            private readonly ConcurrentDictionary<long, Character> characters = new(); // key: PlayerID
            // 索引2：按队伍ID分组
            private readonly ConcurrentDictionary<long, ConcurrentDictionary<long, Character>> teamCharacters = new(); // key: TeamID -> (PlayerID -> Character)

            public CharacterManager(Game game)
            {
                this.game = game;
                moveEngine = new MoveEngine(
                    game.Map,
                    OnCollision,
                    EndMove
                );
            }

            public Character CreateCharacter(long teamId, long playerId, CharacterType type)
            {
                var ch = new Character(GameData.CharacterRadius, type);
                ch.TeamID.SetROri(teamId);
                ch.PlayerID.SetROri(playerId);
                characters[playerId] = ch;
                var teamDict = teamCharacters.GetOrAdd(teamId, _ => new ConcurrentDictionary<long, Character>());
                teamDict[playerId] = ch;
                return ch;
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

            public bool Move(long playerId, int timeMs, double direction, long shoes = 0)
            {
                if (!characters.TryGetValue(playerId, out var ch)) return false;
                if (!ch.CanMove || ch.IsRemoved) return false;
                var state = ch.StateNum;
                moveEngine.MoveObj(ch, timeMs, direction, state, shoes);
                return true;
            }

            public bool TryGetCharacter(long playerId, out Character character)
                => characters.TryGetValue(playerId, out character!);

            public IReadOnlyCollection<Character> GetTeamCharacters(long teamId)
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
                return true;
            }

            private MoveEngine.AfterCollision OnCollision(IMovable mover, IGameObj target, XY moveVec)
            {
                // 默认尽量移动到可行的最远距离；必要时可调用 game 的其他管理器决定逻辑。
                return MoveEngine.AfterCollision.MoveMax;
            }

            private void EndMove(IMovable mover)
            {
                if (mover is not Character ch) return;
                // 移动收尾：仅恢复状态。可见性在 GetSnapshot 按观察者动态计算。
                ch.SetCharacterState(CharacterState.IDLE);
            }
        }
    }
}
