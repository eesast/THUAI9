using GameClass.GameObj;
using GameClass.GameObj.Areas;
using Gaming;
using Preparation.Utility;
using Protobuf;
using Utility = Preparation.Utility;

namespace Server
{
    public static class CopyInfo
    {
        public static MessageOfObj? Auto(GameObj gameObj, long time)
        {
            if (gameObj.IsRemoved == true)
                return null;
            switch (gameObj.Type)
            {
                case GameObjType.CHARACTER:
                    return CHARACTER((Character)gameObj, time);
                case GameObjType.RESOURCE:
                    return RESOURCE((Resource)gameObj);
                case GameObjType.BARRIER:
                    return BARRIER((Barriers)gameObj);
                case GameObjType.BUSH:
                    return BUSH((Bush)gameObj);
                case GameObjType.COMPUTE_CENTER:
                    return COMPUTE_CENTER((ComputeCenter)gameObj);
                case GameObjType.FACTORY:
                    return FACTORY((Factory)gameObj, time);
                case GameObjType.MARKET:
                    return MARKET((Market)gameObj);
                // case GameObjType.BRAIN:
                default: return null;
            }
        }

        public static MessageOfObj? Auto(MessageOfNews news)
        {
            MessageOfObj objMsg = new()
            {
                NewsMessage = news
            };
            return objMsg;
        }
        public static MessageOfAll.Types.TeamInfo TeamInfo(Game.TeamStatus team)
        {
            var info = new MessageOfAll.Types.TeamInfo
            {
                Score = (int)team.Score,
                Material = (int)team.FactorySource,
                ComputePower = (int)team.FactoryComputingPower,
                FactoryHp = (int)team.FactoryHP
            };

            foreach (var kv in team.TechLevels)
            {
                info.TechLevels[kv.Key] = kv.Value;
            }

            return info;
        }

        public static MessageOfTeam TeamMessage(Game.TeamStatus team, long playerId)
        {
            var msg = new MessageOfTeam
            {
                TeamId = team.TeamId,
                PlayerId = playerId,
                Score = (int)team.Score,
                Material = (int)team.FactorySource,
                ComputePower = (int)team.FactoryComputingPower
            };

            foreach (var kv in team.TechLevels)
            {
                msg.TechLevels[kv.Key] = kv.Value;
            }

            return msg;
        }

        private static MessageOfObj? CHARACTER(Character player, long time)
        {
            var msg = new MessageOfObj
            {
                CharacterMessage = new MessageOfCharacter
                {
                    Guid = player.ID,

                    TeamId = player.TeamID.Get(),
                    PlayerId = player.PlayerID.Get(),

                    CharacterType = Transformation.CharacterTypeToProto(player.CharacterType),

                    CharacterActiveState = Transformation.CharacterStateToProto(player.CharacterState),

                    X = player.Position.x,
                    Y = player.Position.y,

                    FacingDirection = player.FacingDirection.Angle(),

                    Speed = (int)player.MoveSpeed.Get(),

                    // Character.cs 里没有 ViewRange
                    ViewRange = 0,

                    CommonAttack = (int)player.AttackPower.GetValue(),

                    // 当前版本没有冷却时间
                    CommonAttackCd = 0,

                    CommonAttackRange = (int)player.AttackSize.GetValue(),

                    Hp = (int)player.HP.GetValue(),

                    CarryCapacity = (int)player.Carry.GetValue(),

                    CurrentLoad = player.GoodsLoad.Total(),

                    HarvestRatePerSec = (int)player.Efficiency.GetValue()
                }
            };
            return msg;
        }

        private static MessageOfObj? RESOURCE(Resource resource)
        {
            return new MessageOfObj
            {
                ResourceMessage = new MessageOfResource
                {
                    ResourceType = Transformation.ResourceTypeToProto(resource.ResourceType),

                    ResourceState = Transformation.ResourceStateToProto(resource.Resourcestate),

                    X = resource.Position.x,
                    Y = resource.Position.y,

                    RemainingAmount = (int)resource.HP.GetValue(),

                    Id = (int)resource.ID,

                    MaxAmount = (int)GameData.ResourceHP
                }
            };
        }

        private static MessageOfObj? BARRIER(Barriers barrier)
        {
            return new MessageOfObj
            {
                BarrierMessage = new MessageOfBarrier
                {
                    BarrierId = barrier.ID,
                    X = barrier.Position.x,
                    Y = barrier.Position.y
                }
            };
        }

        private static MessageOfObj? BUSH(Bush bush)
        {
            return new MessageOfObj
            {
                BushMessage = new MessageOfBush
                {
                    BushId = bush.ID,
                    X = bush.Position.x,
                    Y = bush.Position.y,
                    Radius = bush.Radius
                }
            };
        }
        private static MessageOfObj? COMPUTE_CENTER(ComputeCenter center)
        {
            int occupyProgress = center.IsOccupied ? 100 : 0;   // 当前版本没有占领进度，只有占领与否两个状态

            return new MessageOfObj
            {
                ComputeCenterMessage = new MessageOfComputeCenter
                {
                    CenterId = center.ID,
                    X = center.Position.x,
                    Y = center.Position.y,
                    OwnerTeamId = center.IsOccupied ? center.OccupiedByTeamId : 0,
                    OccupyProgress = occupyProgress
                }
            };
        }

        private static MessageOfObj? FACTORY(Factory factory, long time)
        {
            var factoryMsg = new MessageOfFactory
            {
                FactoryId = factory.ID,
                TeamId = factory.TeamID.Get(),

                X = factory.Position.x,
                Y = factory.Position.y,

                Hp = (int)factory.HP.GetValue(),
                Robust = (int)factory.Robust.GetValue(),

                Storage = (int)factory.Storage.GetValue(),
                Efficiency = (int)factory.Efficiency.GetValue(),

                Source = factory.Source.Get(),
                ComputingPower = factory.ComputingPower.Get(),

                CanProduce = factory.CanProduce.Get(),
                CanRecruit = factory.CanRecruit.Get()
            };

            // 填充库存
            for (int i = 1; i <= 5; i++)
            {
                Utility.GoodsType type = (Utility.GoodsType)i;
                int quantity = factory.GetGoods(type);

                factoryMsg.ProductInventory.Add(
                new MessageOfFactory.Types.GoodsStack
                {
                    ProductType = (Protobuf.GoodsType)i,
                    Quantity = quantity
                }
                );
            }

            return new MessageOfObj
            {
                FactoryMessage = factoryMsg
            };
        }

        private static MessageOfObj? MARKET(Market market)
        {
            var marketMsg = new MessageOfMarket
            {
                MarketId = market.ID,
                X = market.Position.x,
                Y = market.Position.y,

                MarketType = Transformation.MarketTypeToProto(market.EMarketType)
            };

            for (int i = 1; i <= 5; i++)
            {
                Utility.GoodsType type = (Utility.GoodsType)i;

                marketMsg.PriceList.Add(
                    new MessageOfMarket.Types.PriceEntry
                    {
                        GoodsType = (Protobuf.GoodsType)i,
                        Price = market.GetPrice(type),
                        TradedQuantity = market.GetTradedQuantity(type)
                    }
                );
            }

            return new MessageOfObj
            {
                MarketMessage = marketMsg
            };
        }
    }
}
