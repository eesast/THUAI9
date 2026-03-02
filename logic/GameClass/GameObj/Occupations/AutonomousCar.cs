using Preparation.Interface;
using Preparation.Utility;


namespace GameClass.GameObj.Occupations
{
    public class AutonomousCar : IOccupation
    {
        public int MoveSpeed { get; } = GameData.BaseCharacterSpeed;
        public int MaxHp { get; } = GameData.AutonomouCarHP;
        public int Cost { get; } = GameData.AutonomouCarCost;
        public int BaseAttackSize { get; } = GameData.AutonomouCarATKsize;
        public int MaxLoad { get; } = GameData.AutonomouCarLoad;
        public int AttackPower { get; } = GameData.AutonomouCarATKpower;
        public int ViewRange { get; } = GameData.AutonomouCarViewRange;
        public int Efficiency { get; } = GameData.AutonomouCarEfficiency;
        public int Robust { get; } = GameData.AutonomouCarRobust;

    }
}
