using GameClass.GameObj.Areas;
using GameClass.GameObj.Occupations;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.Atomic;
using Preparation.Utility.Value.SafeValue.LockedValue;
using System.Diagnostics;
using System.Threading.Tasks.Dataflow;
using static System.Formats.Asn1.AsnWriter;

namespace GameClass.GameObj;

public class Factory : Immovable, IFactory
{
    public AtomicLong TeamID { get; } = new(long.MaxValue);
    public AtomicLong PlayerID { get; } = new(long.MaxValue);
    public InVariableRange<long> HP { get; }
    public InVariableRange<long> Robust { get; }
    public InVariableRange<long> Storage { get; }
    public InVariableRange<long> Efficiency { get; }

    public AtomicLong Source { get; } = new(GameData.FactoryInitialSource);
    public AtomicLong ComputingPower { get; } = new(GameData.FactoryInitialComputingPower);

    public AtomicBool CanProduce { get; } = new(true);
    public AtomicBool CanRecruit { get; } = new(true);

    private readonly AtomicInt[] goodsCounts = new AtomicInt[6]
    {
        new(0), // NULL_GOODS_TYPE
        new(0), // SEMICONDUCTOR
        new(0), // MEDICINE
        new(0), // TOYS
        new(0), // CLOTHES
        new(0)  // FOOD
    };

    public int GetGoods(GoodsType type) => goodsCounts[(int)type].Get();

    public void SetGoods(GoodsType type, int value)
    {
        if (value < 0) value = 0;
        goodsCounts[(int)type].SetROri(value);
    }

    public int AddGoods(GoodsType type, int delta)
    {
        if (delta == 0) return goodsCounts[(int)type].Get();
        if (delta > 0)
        {
            goodsCounts[(int)type].AddPositive(delta);
        }
        else
        {
            var target = goodsCounts[(int)type];
            while (true)
            {
                int current = target.Get();
                int newV = current + delta;
                if (newV < 0) newV = 0;
                if (target.CompareExROri(newV, current) == current) break;
            }
        }
        return goodsCounts[(int)type].Get();
    }

    public AtomicInt GetGoodsAtomic(GoodsType type) => goodsCounts[(int)type];

    public Factory(XY initPos, long hpMax, long robustMax, long storageMax, long efficiency, long computingPower, long efficiencyMax = 0,
                int source = 0, int score = 0,
                IEnumerable<(GoodsType type, int quantity)>? goodsMap = null)
        : base(initPos, GameData.NumOfPosGridPerCell / 2, GameObjType.FACTORY)
    {
        HP = new(hpMax);
        HP.SetMaxV(GameData.MaxHP);
        Robust = new(robustMax);
        Robust.SetMaxV(GameData.MaxRobust);
        Storage = new(storageMax);
        Storage.SetMaxV(GameData.MaxStorage);
        Efficiency = new(efficiency);
        Efficiency.SetMaxV(efficiencyMax > 0 ? efficiencyMax : GameData.FactoryEfficiencyMax);
        Source.SetROri(source);
        ComputingPower.SetROri(computingPower);
        if (goodsMap != null)
            foreach (var (type, quantity) in goodsMap)
                SetGoods(type, quantity);
    }

    public Factory(XY initPos)
        : this(initPos, hpMax: GameData.FactoryHP, robustMax: GameData.FactoryRobust, storageMax: GameData.FactoryStorage, efficiency: GameData.FactoryEfficiency, computingPower: GameData.FactoryInitialComputingPower, source: GameData.FactoryInitialSource, score: GameData.FactoryInitialScore) { }

    public Factory() : this(GameData.PosNotInGame) { }
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.SQUARE;
    public long AddSource(long add)
    {
        return Source.AddRNow(add);
    }
    public long AddComputingPower(long add)
    {
        return ComputingPower.AddRNow(add);
    }
    public long SubSource(long sub)
    {
        return Source.SubRNow(sub);
    }
    public long SubComputingPower(long sub)
    {
        return ComputingPower.SubRNow(sub);
    }


    private double _computingAccumulator = 0;
    private int _occupiedComputeCenters = 0;

    public void SetOccupiedComputeCenters(int count)
    {
        _occupiedComputeCenters = count;
    }

    public void TickComputingPower(int elapsedMs)
    {
        if (elapsedMs <= 0) return;
        double perSecond = GameData.FactoryComputePowerPerSecond
            + _occupiedComputeCenters * GameData.FactoryComputePowerBonusPerCenterPerSecond;
        _computingAccumulator += perSecond * (elapsedMs / 1000.0);
        long add = (long)_computingAccumulator;
        if (add > 0)
        {
            ComputingPower.AddRNow(add);
            _computingAccumulator -= add;
        }
    }

    public void ProduceComputingPowerOneSecond()
    {
        ComputingPower.AddRNow(GameData.FactoryComputePowerPerSecond);
    }

    public void Interupt()
    {
        CanProduce.SetROri(false);
        CanRecruit.SetROri(false);
    }
}
