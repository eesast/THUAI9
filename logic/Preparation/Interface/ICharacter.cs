using Preparation.Utility.Value.SafeValue.LockedValue;
using Preparation.Utility;
namespace Preparation.Interface
{
    public interface ICharacter : IMovable, IPlayer
    {
        public InVariableRange<long> HP { get; }
        public InVariableRange<long> AttackPower { get; }
        public InVariableRange<long> AttackSize { get; }
        public InVariableRange<long> Robust { get; }
        public InVariableRange<long> Carry { get; }

        public CharacterType CharacterType { get; }
        public CharacterState CharacterState { get; }//状态
        public long SetCharacterState(CharacterState value = CharacterState.NULL_CHARACTER_STATE, IGameObj? obj = null);
    }
}
