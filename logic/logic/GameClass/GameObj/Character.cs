using GameClass.GameObj.Occupations;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.Atomic;
using Preparation.Utility.Value.SafeValue.LockedValue;
using GameClass.GameObj.Areas;
using System.Timers;
using Microsoft.Extensions.Logging;

namespace GameClass.GameObj;

public class Character : Movable, ICharacter
{
    public AtomicLong TeamID { get; } = new(long.MaxValue);
    public AtomicLong PlayerID { get; } = new(long.MaxValue);
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.CIRCLE;
    public InVariableRange<long> HP { get; }
    public InVariableRange<long> AttackPower { get; }
    public InVariableRange<long> AttackSize { get; }
    public InVariableRange<long> Robust { get; }
    public InVariableRange<long> Efficiency { get; } //移速加成（注意是加成值，实际移速为基础移速+移速加成）
    public InVariableRange<long> Carry { get; }
    public Load GoodsLoad { get; }
    public CharacterType CharacterType { get; }
    private bool _visible = true;
    public bool Visible
    {
        get
        {
            lock (actionLock) return _visible;
        }
        set
        {
            lock (actionLock) _visible = value;
        }
    }
    private CharacterState characterState = CharacterState.NULL_CHARACTER_STATE;
    public CharacterState CharacterState
    {
        get
        {
            lock (actionLock)
                return characterState;
        }
    }
    public IOccupation Occupation { get; }
    private GameObj? InteractObj = null;
    public GameObj? GetInteractObj
    {
        get
        {
            lock (actionLock)
            {
                return InteractObj;
            }
        }
    }
    public override bool IgnoreCollideExecutor(IGameObj targetObj)
    {
        if (IsRemoved)
            return true;
        if (targetObj.Type == GameObjType.CHARACTER
            && XY.DistanceCeil3(targetObj.Position, Position)
            < Radius + targetObj.Radius - GameData.AdjustLength)
            return true;
        return false;
    }
    private long ChangeCharacterState(CharacterState value = CharacterState.NULL_CHARACTER_STATE, GameObj? gameobj = null)
    {
        //只能被SetCharacterState引用
        InteractObj = gameobj;
        characterState = value;
        return ++stateNum;
    }
    public long SetCharacterState(CharacterState value = CharacterState.NULL_CHARACTER_STATE, IGameObj? obj = null)
    {
        GameObj? gameobj = (GameObj?)obj;
        lock (actionLock)
        {
            CharacterState nowState = characterState;
            if (nowState == value) return -1;
            else return ChangeCharacterState(value, gameobj);
        }
    }
    //public bool ResetCharacterState(long state, CharacterState value = CharacterState.NULL_CHARACTER_STATE)
    //{
    //    lock (actionLock)
    //    {
    //        if (state != stateNum)
    //        {
    //            LogicLogging.logger.LogDebug(
    //                LoggingFunctional.CharacterLogInfo(this)
    //                + $" ResetCharacterState failed, input state {state}, StateNum {stateNum}");
    //            return false;
    //        }
    //        characterState = value;
    //        ++stateNum;
    //        LogicLogging.logger.LogDebug(
    //            LoggingFunctional.CharacterLogInfo(this)
    //            + $" ResetCharacterState succeeded {stateNum}");
    //        return true;
    //    }
    //}

    //public bool StartThread(long stateNum)
    //{
    //    lock (actionLock)
    //    {
    //        if (StateNum == stateNum)
    //        {
    //            LogicLogging.logger.LogDebug(
    //                LoggingFunctional.CharacterLogInfo(this)
    //                + " StartThread succeeded");
    //            return true;
    //        }
    //    }
    //    LogicLogging.logger.LogDebug(
    //        LoggingFunctional.CharacterLogInfo(this)
    //        + " StartThread failed");
    //    return false;
    //}

    public bool TryToRemoveFromGame(CharacterState state)
    {
        lock (actionLock)
        {
            if (SetCharacterState(state) == -1)
                return false;
            TryToRemove();
            CanMove.SetROri(false);
            position = GameData.PosNotInGame;
        }
        return true;
    }
    public void Init()
    {
        HP.SetMaxV(Occupation.MaxHp);
        HP.SetVToMaxV();
        MoveSpeed.SetROri(orgMoveSpeed = Occupation.MoveSpeed);
    }

    private void InitStatsFromOccupation()
    {
        Efficiency.SetMaxV(GameData.MaxEfficiency);
        Robust.SetMaxV(GameData.MaxRobust);
        AttackSize.SetMaxV(Occupation.BaseAttackSize);
        AttackPower.SetMaxV(Occupation.AttackPower);
        AttackPower.SetVToMaxV();
        AttackSize.SetVToMaxV();
    }
    public Character(int radius, CharacterType type) :
        base(GameData.PosNotInGame, radius, GameObjType.CHARACTER)
    {
        CanMove.SetROri(false);
        IsRemoved.SetROri(true);
        Occupation = OccupationFactory.FindIOccupation(CharacterType = type);
        Efficiency = new(0);
        Robust = new(0);
        AttackSize = new(Occupation.BaseAttackSize);
        HP = new(Occupation.MaxHp);
        AttackPower = new(Occupation.AttackPower);
        Carry = new(Occupation.MaxLoad);
        GoodsLoad = new Load(this);
        InitStatsFromOccupation();
        Init();
    }
    public bool InSquare(XY pos, int range)
    {
        return pos.x >= Position.x - range && pos.x <= Position.x + range && pos.y >= Position.y - range && pos.y <= Position.y + range;
    }

    internal bool SetLoad(GoodsType type, int newValue)
    {
        if (newValue < 0) newValue = 0;
        lock (actionLock)
        {
            long maxCarry = Carry.GetValue();
            int currentTotal = GoodsLoad.Total();
            int oldVal = GoodsLoad.Get(type);
            int proposedTotal = currentTotal - oldVal + newValue;
            if (proposedTotal > maxCarry) return false;
            GoodsLoad.SetInternal(type, newValue);
            return true;
        }
    }
    internal bool AddLoad(GoodsType type, int delta)
    {
        if (delta == 0) return true;
        lock (actionLock)
        {
            int current = GoodsLoad.Get(type);
            int target = current + delta;
            if (target < 0) target = 0;
            long maxCarry = Carry.GetValue();
            int currentTotal = GoodsLoad.Total();
            int proposedTotal = currentTotal - current + target;
            if (proposedTotal > maxCarry) return false;
            GoodsLoad.SetInternal(type, target);
            return true;
        }
    }
}

public sealed class Load
{
    private readonly Preparation.Utility.Value.SafeValue.Atomic.AtomicInt[] counts = new Preparation.Utility.Value.SafeValue.Atomic.AtomicInt[6]
    {
        new(0), // NULL_GOODS_TYPE
        new(0), // SEMICONDUCTOR
        new(0), // MEDICINE
        new(0), // TOYS
        new(0), // CLOTHES
        new(0)  // FOOD
    };

    private readonly Character owner;
    public Load(Character owner)
    {
        this.owner = owner;
    }

    public int Get(GoodsType type) => counts[(int)type].Get();
    public int Total()
    {
        int sum = 0;
        for (int i = 1; i <= 5; i++) sum += counts[i].Get();
        return sum;
    }
    public bool Set(GoodsType type, int value) => owner.SetLoad(type, value);
    public bool Add(GoodsType type, int delta) => owner.AddLoad(type, delta);

    public System.Collections.Generic.IReadOnlyDictionary<GoodsType, int> Snapshot()
    {
        var dict = new System.Collections.Generic.Dictionary<GoodsType, int>(5)
        {
            { GoodsType.SEMICONDUCTOR, counts[(int)GoodsType.SEMICONDUCTOR].Get() },
            { GoodsType.MEDICINE, counts[(int)GoodsType.MEDICINE].Get() },
            { GoodsType.TOYS, counts[(int)GoodsType.TOYS].Get() },
            { GoodsType.CLOTHES, counts[(int)GoodsType.CLOTHES].Get() },
            { GoodsType.FOOD, counts[(int)GoodsType.FOOD].Get() },
        };
        return dict;
    }

    internal void SetInternal(GoodsType type, int value)
    {
        counts[(int)type].SetROri(value < 0 ? 0 : value);
    }
}


