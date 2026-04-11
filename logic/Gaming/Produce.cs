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
            private readonly Game game;
            private readonly Factory factory;
            private readonly GoodsType type;
            private readonly int amount;
            private readonly int costPerOriginal;
            private readonly int produceMsPerItemOriginal;

            public Production(Game game, Factory factory, GoodsType type, int amount)
            {
                this.game = game;
                this.factory = factory;
                this.type = type;
                this.amount = amount;

                this.costPerOriginal = type switch
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
                this.produceMsPerItemOriginal = baseSeconds * 1000;
            }

            private int GetAdjustedCostPer()
            {
                long teamId = factory.TeamID.Get();
                if (!game.teams.TryGetValue(teamId, out var t)) return costPerOriginal;
                int costTech = t.GetTech("Cost");
                int adjusted = costPerOriginal - costTech * GameData.TechCostDecreasePerLevel;
                return Math.Max(0, adjusted);
            }

            private int GetAdjustedProduceMsPerItem()
            {
                long teamId = factory.TeamID.Get();
                if (!game.teams.TryGetValue(teamId, out var t)) return produceMsPerItemOriginal;

                int prodLevel = t.GetTech("Production");

                double multiplier = 1.0;
                if (prodLevel > 0) multiplier /= (1.0 + prodLevel * GameData.TechProductionEfficiencyAddPerLevel);

                multiplier = Math.Max(multiplier, 0.2);

                return (int)Math.Max(1, Math.Round(produceMsPerItemOriginal * multiplier));
            }

            public bool Start()
            {
                int costPer = GetAdjustedCostPer();
                int produceMsPerItem = GetAdjustedProduceMsPerItem();

                long totalCost = (long)costPer * amount;
                while (true)
                {
                    long cur = factory.Source.Get();
                    if (cur < totalCost)
                    {
                        LogicLogging.logger.LogDebug(
                            LoggingFunctional.AutoLogInfo(factory) +
                            $" failed to start production of {amount} {type} due to insufficient source (current: {cur}, needed: {totalCost})");
                        return false;
                    }
                    if (factory.Source.CompareExROri(cur - totalCost, cur) == cur) break;
                }

                new Thread(() =>
                {
                    int produced = 0;
                    factory.CanProduce.SetROri(false);
                    try
                    {
                        for (int i = 0; i < amount; i++)
                        {
                            if (factory.IsRemoved.Get()) break;

                            Thread.Sleep(produceMsPerItem);

                            long storageNowMax = factory.Storage.GetValue();
                            int totalAfter = 0;
                            for (int j = 1; j <= 5; j++) totalAfter += factory.GetGoods((GoodsType)j);

                            if (totalAfter < storageNowMax)
                            {
                                factory.AddGoods(type, 1);
                                produced++;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        LogicLogging.logger.LogError(
                            LoggingFunctional.AutoLogInfo(factory) +
                            $" encountered an error during production of {amount} {type}, produced so far: {produced}");
                    }
                    finally
                    {
                        int remaining = amount - produced;
                        if (remaining > 0)
                        {
                            factory.AddSource((long)costPer * remaining);
                        }
                        factory.CanProduce.SetROri(true);
                    }
                })
                { IsBackground = true }.Start();

                return true;
            }
        }

        public bool Produce(long teamId, GoodsType type, int amount)
        {
            if (!EnsureGameStarted(nameof(Produce)))
                return false;
            if (amount <= 0)
            {
                LogicLogging.logger.LogDebug(
                    LoggingFunctional.AutoLogInfo(teamId) +
                    $" failed to start production of {amount} {type} due to non-positive amount");
                return false;
            }
            var factory = GetTeamFactory(teamId);
            if (factory == null)
            {
                LogicLogging.logger.LogDebug(
                    LoggingFunctional.AutoLogInfo(teamId) +
                    $" failed to start production of {amount} {type} due to missing factory");
                return false;
            }
            if (!factory.CanProduce.Get())
            {
                LogicLogging.logger.LogDebug(
                    LoggingFunctional.AutoLogInfo(factory) +
                    $" failed to start production of {amount} {type} because factory is currently busy");
                return false;
            }
            long storageMax = factory.Storage.GetValue();
            int currentTotal = 0;
            for (int i = 1; i <= 5; i++) currentTotal += factory.GetGoods((GoodsType)i);
            if (currentTotal >= storageMax)
            {
                LogicLogging.logger.LogDebug(
                    LoggingFunctional.AutoLogInfo(factory) +
                    $" failed to start production of {amount} {type} because factory storage is already full (current total: {currentTotal}, max: {storageMax})");
                return false;
            }
            var production = new Production(this, factory, type, amount);
            return production.Start();
        }
    }
}
