using Preparation.Interface;
using Preparation.Utility;

namespace GameClass.GameObj.Occupations;

public class NullOccupation : IOccupation
{
    public static NullOccupation Instance { get; } = new();
    public int MoveSpeed => 0;
    public int MaxHp => 0;
    public int Cost => 0;
    public int MaxLoad { get; } = 0;
    public int BaseAttackSize { get; } = 0;
    public int AttackPower { get; } = 0;
    public int ViewRange { get; } = 0;
    public int Efficiency { get; } = 0;
    public int Robust { get; } = 0;
    public NullOccupation() { }
}