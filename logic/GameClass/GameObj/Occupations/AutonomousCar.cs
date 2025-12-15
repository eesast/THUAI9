using Preparation.Interface;
using Preparation.Utility;


namespace GameClass.GameObj.Occupations
{
    public class AutonomousCar : IOccupation
    {
        public int MoveSpeed { get; } = GameData.BaseCharacterSpeed;
        public int MaxHp { get; } = GameData.DroneHP;
        public int Cost { get; } = GameData.DroneCost;
        public int BaseAttackSize { get; } = GameData.DroneATKsize;
        public int MaxLoad { get; } = GameData.DroneMaxLoad;
        public int AttackPower { get; } = GameData.DroneATKpower;

    }
}
