using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using GameClass.GameObj;
using Preparation.Utility;

namespace Game
{
    public partial class Game
    {
        private readonly ConcurrentDictionary<(long marketId, GoodsType type), int> marketSoldCounts = new();

        internal bool TradeInternal(long playerId, GoodsType type, int amount, bool buy)
        {
            if (amount <= 0) return false;
            if (type == GoodsType.NULL_GOODS_TYPE) return false;

            if (!characterManager.TryGetCharacter(playerId, out var ch)) return false;
            long teamId = ch.TeamID.Get();

            if (buy)
            {
                return BuyFromFactory(teamId, playerId, type, amount);
            }
            else
            {
                return SellToMarket(teamId, playerId, type, amount);
            }
        }

        private bool BuyFromFactory(long teamId, long playerId, GoodsType type, int amount)
        {
            // Character buys goods from their team's factory
            var factory = GetTeamFactory(teamId);
            if (factory == null) return false;

            if (!characterManager.TryGetCharacter(playerId, out var ch)) return false;

            // Check if factory has enough goods
            int available = factory.GetGoods(type);
            if (available < amount) return false;

            // Check if character has capacity
            int currentLoad = ch.GoodsLoad.Total();
            int maxCapacity = (int)ch.Carry.GetValue();
            if (currentLoad + amount > maxCapacity) return false;

            // Transfer goods from factory to character
            factory.AddGoods(type, -amount);
            if (!ch.GoodsLoad.Add(type, amount))
            {
                // Rollback if failed
                factory.AddGoods(type, amount);
                return false;
            }

            return true;
        }

        private bool SellToMarket(long teamId, long playerId, GoodsType type, int amount)
        {
            // Character sells goods to market
            if (!characterManager.TryGetCharacter(playerId, out var ch)) return false;

            // Check if character has enough goods
            int currentGoods = ch.GoodsLoad.Get(type);
            if (currentGoods < amount) return false;

            // For simplicity, assume market is accessible if character is near a market location
            // In a full implementation, we'd check character position vs market positions

            // Track sold amount for depreciation (before calculating price to apply to future sales)
            var key = (0L, type); // marketId=0
            int soldCountBeforeSale = marketSoldCounts.GetOrAdd(key, 0);

            // Calculate selling price using sold count BEFORE this sale
            int baseValue = GetGoodsBaseValue(type);
            int price = CalculateMarketPrice(soldCountBeforeSale, type, baseValue);

            // Remove goods from character
            if (!ch.GoodsLoad.Add(type, -amount)) return false;

            // Update sold count after successful sale
            marketSoldCounts.AddOrUpdate(key, amount, (k, old) => old + amount);

            // Add revenue to team (convert to score)
            // According to rules: Score = Sales × 10
            long revenue = price * amount;
            AddTeamScore(teamId, revenue * 10); // Multiply by 10 as per scoring rules

            return true;
        }

        private int GetGoodsBaseValue(GoodsType type)
        {
            // Base value from the middle of the range in the rules
            switch (type)
            {
                case GoodsType.SEMICONDUCTOR:
                    return 80; // 40~120, use middle value
                case GoodsType.MEDICINE:
                    return 40; // 20~60
                case GoodsType.TOYS:
                    return 8; // 4~12
                case GoodsType.CLOTHES:
                    return 64; // 32~96
                case GoodsType.FOOD:
                    return 18; // 12~24
                default:
                    return 0;
            }
        }

        private int CalculateMarketPrice(int soldCount, GoodsType type, int baseValue)
        {
            // Market multipliers: cheap market ×2, premium market ×10
            // For simplicity, use ×2 (cheap market)
            int multiplier = 2;

            // Simple depreciation: every 100 units sold reduces price by 10%
            int depreciationLevel = soldCount / 100;
            double depreciationFactor = Math.Max(0.5, 1.0 - depreciationLevel * 0.1);

            int finalPrice = (int)(baseValue * multiplier * depreciationFactor);
            return finalPrice;
        }

        public bool TransferGoods(long fromPlayerId, long toPlayerId, GoodsType type, int amount)
        {
            if (amount <= 0) return false;
            if (type == GoodsType.NULL_GOODS_TYPE) return false;

            if (!characterManager.TryGetCharacter(fromPlayerId, out var fromChar)) return false;
            if (!characterManager.TryGetCharacter(toPlayerId, out var toChar)) return false;

            // Check same team
            if (fromChar.TeamID.Get() != toChar.TeamID.Get()) return false;

            // Check if sender has enough
            if (fromChar.GoodsLoad.Get(type) < amount) return false;

            // Check if receiver has capacity
            int toLoad = toChar.GoodsLoad.Total();
            int toCapacity = (int)toChar.Carry.GetValue();
            if (toLoad + amount > toCapacity) return false;

            // Transfer
            if (!fromChar.GoodsLoad.Add(type, -amount)) return false;
            if (!toChar.GoodsLoad.Add(type, amount))
            {
                // Rollback
                fromChar.GoodsLoad.Add(type, amount);
                return false;
            }

            return true;
        }
    }
}
