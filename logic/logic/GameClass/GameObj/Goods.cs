using Preparation.Interface;
using Preparation.Utility;

namespace GameClass.GameObj;

public class Goods : IGoods
{
    public int Cost { get; }
    public int Price { get; }
    public GoodsType goodsType { get; }
}
