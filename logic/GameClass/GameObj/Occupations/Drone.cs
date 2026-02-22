using Preparation.Interface;
using Preparation.Utility;


namespace GameClass.GameObj.Occupations
{
    public class Drone : IOccupation
    {
        public int MoveSpeed { get; } = GameData.BaseCharacterSpeed;
        public int MaxHp { get; } = GameData.DroneHP;
        public int Cost { get; } = GameData.DroneCost;
        public int BaseAttackSize { get; } = GameData.DroneATKsize;
        public int MaxLoad { get; } = GameData.DroneLoad;
        public int AttackPower { get; } = GameData.DroneATKpower;
        public int ViewRange { get; } = GameData.DroneViewRange;
        public int Efficiency { get; } = GameData.DroneEfficiency;
        public int Robust { get; } = GameData.DroneRobust;

    }
}
