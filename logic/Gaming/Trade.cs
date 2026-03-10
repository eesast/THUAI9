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
                if (character == null) return false;
                long teamId = character.TeamID.Get();
                if (amount <= 0) return false;
                if (character.IsRemoved) return false;

                Market? market = (Market?)gameMap.OneForInteract(character.Position, GameObjType.MARKET);
                if (market == null) return false;
                if (!GameData.ApproachToInteract(character.Position, market.Position)) return false;

                int have = character.GoodsLoad.Get(type);
                if (have < amount) return false;

                if (!character.GoodsLoad.Add(type, -amount))
                {
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
                if (character == null) return false;
                long teamId = character.TeamID.Get();
                if (amount <= 0) return false;
                if (character.IsRemoved) return false;

                Market? market = (Market?)gameMap.OneForInteract(character.Position, GameObjType.MARKET);
                if (market == null) return false;
                if (!GameData.ApproachToInteract(character.Position, market.Position)) return false;

                if (!game.teams.TryGetValue(teamId, out var teamState)) return false;
                long totalCost = (long)market.GetPrice(type) * amount;
                while (true)
                {
                    long curScore = teamState.Score.Get();
                    if (curScore < totalCost) return false;
                    if (teamState.Score.CompareExROri(curScore - totalCost, curScore) == curScore) break;
                }

                if (!character.GoodsLoad.Add(type, amount))
                {
                    teamState.Score.AddRNow(totalCost);
                    return false;
                }

                market.AddTradedQuantity(type, amount);
                return true;
            }

            public bool Sell(long teamId, long playerId, GoodsType type, int amount)
            {
                var character = gameMap.FindCharacterInPlayerID(teamId, playerId);
                if (character == null) return false;
                return Sell(character, type, amount);
            }

            public bool Buy(long teamId, long playerId, GoodsType type, int amount)
            {
                var character = gameMap.FindCharacterInPlayerID(teamId, playerId);
                if (character == null) return false;
                return Buy(character, type, amount);
            }
        }

        private readonly TradeManager tradeManager;

    }
}
