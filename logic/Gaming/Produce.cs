using System;
using System.Threading;
using GameClass.GameObj;
using Preparation.Utility;

namespace Gaming
{
    public partial class Game
    {
        private sealed class Production
        {
            private readonly Factory factory;
            private readonly GoodsType type;
            private readonly int amount;
            private readonly int costPer;
            private readonly int produceMsPerItem;

            public Production(Factory factory, GoodsType type, int amount)
            {
                this.factory = factory;
                this.type = type;
                this.amount = amount;
                this.costPer = type switch
                {
                    GoodsType.SEMICONDUCTOR => GameData.CostSemiconductor,
                    GoodsType.MEDICINE => GameData.CostMedicine,
                    GoodsType.TOYS => GameData.CostToys,
                    GoodsType.CLOTHES => GameData.CostClothes,
                    GoodsType.FOOD => GameData.CostFood,
                    _ => 1
                };
                int baseSeconds = type switch
                {
                    GoodsType.SEMICONDUCTOR => GameData.ProduceTimeSemiconductor,
                    GoodsType.MEDICINE => GameData.ProduceTimeMedicine,
                    GoodsType.TOYS => GameData.ProduceTimeToys,
                    GoodsType.CLOTHES => GameData.ProduceTimeClothes,
                    GoodsType.FOOD => GameData.ProduceTimeFood,
                    _ => 2
                };
                this.produceMsPerItem = baseSeconds * 1000;
            }

            public bool Start()
            {
                long totalCost = (long)costPer * amount;
                while (true)
                {
                    long cur = factory.Source.Get();
                    if (cur < totalCost) return false;
                    if (factory.Source.CompareExROri(cur - totalCost, cur) == cur) break;
                }

                new Thread(() =>
                {
                    factory.CanProduce.SetROri(false);
                    try
                    {
                        for (int i = 0; i < amount; i++)
                        {
                            Thread.Sleep(produceMsPerItem);

                            long storageNowMax = factory.Storage.GetValue();
                            int totalAfter = 0;
                            for (int j = 1; j <= 5; j++) totalAfter += factory.GetGoods((GoodsType)j);

                            if (totalAfter < storageNowMax)
                            {
                                factory.AddGoods(type, 1);
                            }
                            else
                            {
                                int remaining = amount - i;
                                if (remaining > 0)
                                {
                                    factory.AddSource((long)costPer * remaining);
                                }
                                break;
                            }
                        }
                    }
                    finally
                    {
                        factory.CanProduce.SetROri(true);
                    }
                })
                { IsBackground = true }.Start();

                return true;
            }
        }

        public bool Produce(long teamId, GoodsType type, int amount)
        {
            if (amount <= 0) return false;
            var factory = GetTeamFactory(teamId);
            if (factory == null) return false;

            if (!factory.CanProduce.Get()) return false;

            long storageMax = factory.Storage.GetValue();
            int currentTotal = 0;
            for (int i = 1; i <= 5; i++) currentTotal += factory.GetGoods((GoodsType)i);
            if (currentTotal >= storageMax) return false;

            var production = new Production(factory, type, amount);
            return production.Start();
        }
    }
}
