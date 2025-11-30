using GameClass.GameObj.Occupations;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.Atomic;
using Preparation.Utility.Value.SafeValue.LockedValue;
using GameClass.GameObj.Areas;

namespace GameClass.GameObj;
public class Factory : Immovable, IFactory
{
    public AtomicLong TeamID { get; } = new(long.MaxValue);
    public AtomicLong PlayerID { get; } = new(long.MaxValue);
    public InVariableRange<long> HP { get; }
    public InVariableRange<long> Robust { get; }
    public InVariableRange<long> Storage { get; }

    public AtomicInt Source { get; } = new(0);          // 资源数量
    public AtomicInt ComputingPower { get; } = new(0);  // 算力值
    public AtomicInt Score { get; } = new(0);           // 积分

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
                if (target.CompareExROri(newV, current) == current) break; // CAS成功
            }
        }
        return goodsCounts[(int)type].Get();
    }

    public AtomicInt GetGoodsAtomic(GoodsType type) => goodsCounts[(int)type];

    public Factory(XY initPos, long hpMax, long robustMax, long storageMax,
                int source = 0, int computingPower = 0, int score = 0,
                IEnumerable<(GoodsType type, int quantity)>? goodsMap = null)
        : base(initPos, GameData.NumOfPosGridPerCell / 2, GameObjType.FACTORY)
    {
        HP = new(hpMax);
        Robust = new(robustMax);
        Storage = new(storageMax);
        Source.SetROri(source);
        ComputingPower.SetROri(computingPower);
        Score.SetROri(score);
        if (goodsMap != null)
            foreach (var (type, quantity) in goodsMap)
                SetGoods(type, quantity);
    }

    public Factory(XY initPos)
        : this(initPos, hpMax: 100, robustMax: GameData.MaxRobust, storageMax: 1000) { }

    public Factory() : this(GameData.PosNotInGame) { }
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.SQUARE;
}
