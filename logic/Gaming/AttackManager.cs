using System;
using GameClass.GameObj;
using Preparation.Utility;

namespace Game
{
    public partial class Game
    {
        private readonly AttackManager attackManager;

        private sealed class AttackManager
        {
            private readonly Game game;

            public AttackManager(Game game)
            {
                this.game = game;
            }

            public bool Attack(long attackerPlayerId, long targetPlayerId)
            {
                if (!game.characterManager.TryGetCharacter(attackerPlayerId, out var attacker)) return false;
                if (!game.characterManager.TryGetCharacter(targetPlayerId, out var target)) return false;
                if (attacker.TeamID.Get() == target.TeamID.Get()) return false;
                long damage = attacker.AttackPower.GetValue();
                target.HP.SubPositiveV(damage);
                if (target.HP.GetValue() == 0)
                {
                    game.characterManager.Destroy(targetPlayerId, CharacterState.NULL_CHARACTER_STATE);
                }
                return true;
            }

            public bool BeAttacked(long targetPlayerId, long damage)
            {
                if (!game.characterManager.TryGetCharacter(targetPlayerId, out var target)) return false;
                target.HP.SubPositiveV(damage);
                if (target.HP.GetValue() == 0)
                {
                    game.characterManager.Destroy(targetPlayerId, CharacterState.NULL_CHARACTER_STATE);
                }
                return true;
            }
        }
    }
}
