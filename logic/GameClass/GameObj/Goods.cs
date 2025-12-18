using Preparation.Interface;
using Preparation.Utility;

namespace GameClass.GameObj;

public class Goods : IGoods
{
    public int Cost { get; }
    public int Price { get; }
    public GoodsType GoodsType { get; }

    public Goods(int cost, int price, GoodsType goodsType)
    {
        Cost = cost;
        Price = price;
        GoodsType = goodsType;
    }
}
