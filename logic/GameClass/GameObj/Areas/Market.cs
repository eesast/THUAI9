using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.LockedValue;
using Preparation.Utility.Value.SafeValue.Atomic;

namespace GameClass.GameObj.Areas;

public class Market(XY initPos)
    : Immovable(initPos, GameData.NumOfPosGridPerCell / 2, GameObjType.MARKET)
{
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.SQUARE;

    protected readonly object actionLock = new();
    public object ActionLock => actionLock;

    private MarketType marketType = MarketType.NULL_MARKET_TYPE;
    public MarketType EMarketType
    {
        get
        {
            lock (actionLock)
                return marketType;
        }
        set
        {
            lock (actionLock)
                marketType = value;
        }
    }

    private readonly AtomicInt[] prices = new AtomicInt[6]
    {
        new(0), // NULL_GOODS_TYPE
        new(0), // SEMICONDUCTOR
        new(0), // MEDICINE
        new(0), // TOYS
        new(0), // CLOTHES
        new(0)  // FOOD
    };

    // 已交易数量记录（按 GoodsType 索引）
    private readonly AtomicInt[] traded = new AtomicInt[6]
    {
        new(0), // NULL_GOODS_TYPE
        new(0), // SEMICONDUCTOR
        new(0), // MEDICINE
        new(0), // TOYS
        new(0), // CLOTHES
        new(0)  // FOOD
    };

    public int GetPrice(GoodsType type) => prices[(int)type].Get();

    public void SetPrice(GoodsType type, int price)
    {
        if (price < 0) price = 0;
        prices[(int)type].SetROri(price);
    }

    public int GetTradedQuantity(GoodsType type) => traded[(int)type].Get();

    public int AddTradedQuantity(GoodsType type, int delta)
    {
        int idx = (int)type;
        return traded[idx].AddPositiveRNow(delta);
    }

    public System.Collections.Generic.IReadOnlyDictionary<GoodsType, int> SnapshotTraded()
    {
        var dict = new System.Collections.Generic.Dictionary<GoodsType, int>(5)
        {
            { GoodsType.SEMICONDUCTOR, traded[(int)GoodsType.SEMICONDUCTOR].Get() },
            { GoodsType.MEDICINE, traded[(int)GoodsType.MEDICINE].Get() },
            { GoodsType.TOYS, traded[(int)GoodsType.TOYS].Get() },
            { GoodsType.CLOTHES, traded[(int)GoodsType.CLOTHES].Get() },
            { GoodsType.FOOD, traded[(int)GoodsType.FOOD].Get() },
        };
        return dict;
    }

    public Market(XY initPos, MarketType type)
        : this(initPos)
    {
        marketType = type;
        double mul = type switch
        {
            MarketType.SMALL_MARKET => GameData.SmallMarketMultiplier,
            MarketType.MEDIUM_MARKET => GameData.MediumMarketMultiplier,
            MarketType.LARGE_MARKET => GameData.LargeMarketMultiplier,
            _ => GameData.MediumMarketMultiplier
        };

        SetPrice(GoodsType.SEMICONDUCTOR, (int)Math.Round(GameData.BasePriceSemiconductor * mul));
        SetPrice(GoodsType.MEDICINE, (int)Math.Round(GameData.BasePriceMedicine * mul));
        SetPrice(GoodsType.TOYS, (int)Math.Round(GameData.BasePriceToys * mul));
        SetPrice(GoodsType.CLOTHES, (int)Math.Round(GameData.BasePriceClothes * mul));
        SetPrice(GoodsType.FOOD, (int)Math.Round(GameData.BasePriceFood * mul));
    }

}
