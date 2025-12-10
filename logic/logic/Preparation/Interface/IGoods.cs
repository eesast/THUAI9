using Preparation.Utility;


namespace Preparation.Interface
{
    public interface IGoods
    {
        public int Cost { get; }
        public int Price { get; }
        public GoodsType goodsType { get; }
    }
}


