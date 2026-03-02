using Preparation.Interface;
using Preparation.Utility;


namespace GameClass.GameObj.Occupations
{
    public class Robot : IOccupation
    {
        public int MoveSpeed { get; } = GameData.BaseCharacterSpeed;
        public int MaxHp { get; } = GameData.RobotHP;
        public int Cost { get; } = GameData.RobotCost;
        public int BaseAttackSize { get; } = GameData.RobotATKsize;
        public int MaxLoad { get; } = GameData.RobotLoad;
        public int AttackPower { get; } = GameData.RobotATKpower;
        public int ViewRange { get; } = GameData.RobotViewRange;
        public int Efficiency { get; } = GameData.RobotEfficiency;
        public int Robust { get; } = GameData.RobotRobust;

    }
}