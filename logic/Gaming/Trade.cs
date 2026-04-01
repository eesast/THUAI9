using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameClass.GameObj;
using GameClass.GameObj.Areas;
using Preparation.Utility;
using GameClass.GameObj.Map;

namespace Gaming
{
    public partial class Game
    {
        private sealed class TradeManager
        {
            private readonly Game game;
            private readonly Map gameMap;

            public TradeManager(Game game, Map gameMap)
            {
                this.game = game;
                this.gameMap = gameMap;
            }

            public bool Sell(Character character, GoodsType type, int amount)
            {
                if (character == null)
                {
                    LogicLogging.logger.LogError(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to sell {amount} {type} due to null character reference");
                    return false;
                }
                long teamId = character.TeamID.Get();
                if (amount <= 0)
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to sell {amount} {type} due to non-positive amount");
                    return false;
                }
                if (character.IsRemoved)
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to sell {amount} {type} because character is removed from the game");
                    return false;
                }
                Market? market = (Market?)gameMap.OneForInteract(character.Position, GameObjType.MARKET);
                if (market == null)
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to sell {amount} {type} because no market is in interaction range");
                    return false;
                }
                if (!GameData.ApproachToInteract(character.Position, market.Position))
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to sell {amount} {type} because character is not close enough to the market");
                    return false;
                }
                int have = character.GoodsLoad.Get(type);
                if (have < amount)
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to sell {amount} {type} because character only has {have}");
                    return false;
                }
                if (!character.GoodsLoad.Add(type, -amount))
                {
                    LogicLogging.logger.LogError(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to sell {amount} {type} due to an unexpected error when updating goods load");
                    return false;
                }

                int price = market.GetPrice(type);
                long revenue = (long)price * amount;
                game.AddTeamScore(teamId, revenue);

                market.AddTradedQuantity(type, amount);

                return true;
            }

            public bool Buy(Character character, GoodsType type, int amount)
            {
                if (character == null)
                {
                    LogicLogging.logger.LogError(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to buy {amount} {type} due to null character reference");
                    return false;
                }
                long teamId = character.TeamID.Get();
                if (amount <= 0)
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to buy {amount} {type} due to non-positive amount");
                    return false;
                }
                if (character.IsRemoved)
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to buy {amount} {type} because character is removed from the game");
                    return false;
                }
                Market? market = (Market?)gameMap.OneForInteract(character.Position, GameObjType.MARKET);
                if (market == null)
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to buy {amount} {type} because no market is in interaction range");
                    return false;
                }
                if (!GameData.ApproachToInteract(character.Position, market.Position))
                {
                    LogicLogging.logger.LogDebug(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to buy {amount} {type} because character is not close enough to the market");
                    return false;
                }
                if (!game.teams.TryGetValue(teamId, out var teamState))
                {
                    LogicLogging.logger.LogError(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to buy {amount} {type} because team with ID {teamId} does not exist");
                    return false;
                }
                long totalCost = (long)market.GetPrice(type) * amount;
                while (true)
                {
                    long curScore = teamState.Score.Get();
                    if (curScore < totalCost)
                    {
                        LogicLogging.logger.LogDebug(
                            LoggingFunctional.TradeLogInfo(character) +
                            $" failed to buy {amount} {type} because team score {curScore} is less than total cost {totalCost}");
                        return false;
                    }
                    if (teamState.Score.CompareExROri(curScore - totalCost, curScore) == curScore) break;
                }

                if (!character.GoodsLoad.Add(type, amount))
                {
                    teamState.Score.AddRNow(totalCost);
                    LogicLogging.logger.LogError(
                        LoggingFunctional.TradeLogInfo(character) +
                        $" failed to buy {amount} {type} due to an unexpected error when updating goods load");
                    return false;
                }

                market.AddTradedQuantity(type, amount);
                return true;
            }

            public bool Sell(long teamId, long playerId, GoodsType type, int amount)
            {
                var character = gameMap.FindCharacterInPlayerID(teamId, playerId);
                if (character == null)
                {
                    LogicLogging.logger.LogError(
                        LoggingFunctional.TradeLogInfo(teamId, playerId) +
                        $" failed to sell {amount} {type} due to null character reference");
                    return false;
                }
                return Sell(character, type, amount);
            }

            public bool Buy(long teamId, long playerId, GoodsType type, int amount)
            {
                var character = gameMap.FindCharacterInPlayerID(teamId, playerId);
                if (character == null)
                {
                    LogicLogging.logger.LogError(
                        LoggingFunctional.TradeLogInfo(teamId, playerId) +
                        $" failed to buy {amount} {type} due to null character reference");
                    return false;
                }
                return Buy(character, type, amount);
            }
        }

        private readonly TradeManager tradeManager;

    }
}
