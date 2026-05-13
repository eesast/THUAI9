using Avalonia.Media;
using Protobuf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using THUAI9_Avalonia.Models;

namespace THUAI9_Avalonia.ViewModels
{
    internal sealed class DynamicWorldSummary
    {
        public string BuildingSummary { get; set; } = "工厂：等待数据";
        public string ResourceSummary { get; set; } = "资源点：等待数据";
        public string ObjectiveSummary { get; set; } = "算力中心：等待数据";
        public string MarketSummary { get; set; } = "市场：等待数据";
    }

    /// <summary>
    /// 维护 THUAI9 动态地图对象的增量状态，并生成覆盖层与语义事件。
    /// </summary>
    internal sealed class MapDynamicStateManager
    {
        private readonly MapViewModel _mapViewModel;
        private readonly Dictionary<string, MessageOfFactory> _factories = new();
        private readonly Dictionary<string, MessageOfResource> _resources = new();
        private readonly Dictionary<string, MessageOfComputeCenter> _computeCenters = new();
        private readonly Dictionary<string, MessageOfMarket> _markets = new();
        private bool _hasPrimedFrame;

        public DynamicWorldSummary Summary { get; } = new();

        public MapDynamicStateManager(MapViewModel mapViewModel)
        {
            _mapViewModel = mapViewModel;
        }

        public void Reset(bool resetBaseMap = false)
        {
            _factories.Clear();
            _resources.Clear();
            _computeCenters.Clear();
            _markets.Clear();
            _mapViewModel.ClearDynamicOverlays();
            if (resetBaseMap)
            {
                _mapViewModel.InitializeMapCells();
            }

            Summary.BuildingSummary = "工厂：等待数据";
            Summary.ResourceSummary = "资源点：等待数据";
            Summary.ObjectiveSummary = "算力中心：等待数据";
            Summary.MarketSummary = "市场：等待数据";
            _hasPrimedFrame = false;
        }

        public void ApplyFrame(IEnumerable<MessageOfObj> objects, Action<string, string>? semanticLog = null)
        {
            var seenFactories = new HashSet<string>();
            var seenResources = new HashSet<string>();
            var seenCenters = new HashSet<string>();
            var seenMarkets = new HashSet<string>();

            foreach (var obj in objects)
            {
                if (obj.FactoryMessage != null)
                {
                    UpsertFactory(obj.FactoryMessage, seenFactories, semanticLog);
                }
                else if (obj.ResourceMessage != null)
                {
                    UpsertResource(obj.ResourceMessage, seenResources, semanticLog);
                }
                else if (obj.ComputeCenterMessage != null)
                {
                    UpsertComputeCenter(obj.ComputeCenterMessage, seenCenters, semanticLog);
                }
                else if (obj.MarketMessage != null)
                {
                    UpsertMarket(obj.MarketMessage, seenMarkets);
                }
            }

            RemoveMissing(_factories, seenFactories, factory => _mapViewModel.RemoveDynamicOverlay(BuildKey("factory", factory.FactoryId, factory.X, factory.Y)),
                factory =>
                {
                    if (_hasPrimedFrame && factory.Hp > 0 && IsKnownTeam(factory.TeamId))
                    {
                        semanticLog?.Invoke($"{GetTeamName(factory.TeamId)}在 ({factory.X / 1000},{factory.Y / 1000}) 的工厂不再上报", "WARNING");
                    }
                });

            RemoveMissing(_resources, seenResources, resource => _mapViewModel.RemoveDynamicOverlay(BuildKey("resource", resource.Id, resource.X, resource.Y)),
                resource =>
                {
                    if (_hasPrimedFrame && resource.RemainingAmount > 0)
                    {
                        semanticLog?.Invoke($"({resource.X / 1000},{resource.Y / 1000}) 的资源点已采尽", "INFO");
                    }
                });

            RemoveMissing(_computeCenters, seenCenters, center => _mapViewModel.RemoveDynamicOverlay(BuildKey("center", center.CenterId, center.X, center.Y)),
                center =>
                {
                    if (_hasPrimedFrame)
                    {
                        semanticLog?.Invoke($"({center.X / 1000},{center.Y / 1000}) 的算力中心不再上报", "WARNING");
                    }
                });

            RemoveMissing(_markets, seenMarkets, market => _mapViewModel.RemoveDynamicOverlay(BuildKey("market", market.MarketId, market.X, market.Y)), _ => { });

            RebuildSummary();
            _hasPrimedFrame = true;
        }

        private static void RemoveMissing<T>(Dictionary<string, T> cache, HashSet<string> seenKeys, Action<T> removeVisual, Action<T> onRemove)
        {
            var removedKeys = cache.Keys.Where(key => !seenKeys.Contains(key)).ToList();
            foreach (var key in removedKeys)
            {
                var removed = cache[key];
                onRemove(removed);
                removeVisual(removed);
                cache.Remove(key);
            }
        }

        private void UpsertFactory(MessageOfFactory factory, HashSet<string> seenFactories, Action<string, string>? semanticLog)
        {
            string key = BuildKey("factory", factory.FactoryId, factory.X, factory.Y);
            seenFactories.Add(key);

            if (_factories.TryGetValue(key, out var previous) && _hasPrimedFrame)
            {
                if (previous.Hp > 0 && factory.Hp <= 0)
                {
                    long collapsedTeamId = ResolveKnownFactoryTeamId(previous, factory);
                    if (collapsedTeamId > 0)
                    {
                        semanticLog?.Invoke($"{GetTeamName(collapsedTeamId)}在 ({factory.X / 1000},{factory.Y / 1000}) 的工厂已瘫痪", "WARNING");
                    }
                }
                else if (previous.TeamId != factory.TeamId)
                {
                    semanticLog?.Invoke($"({factory.X / 1000},{factory.Y / 1000}) 的工厂归属变为 {GetTeamName(factory.TeamId)}", "INFO");
                }
            }

            _factories[key] = factory;
            _mapViewModel.UpsertDynamicOverlay(new MapOverlayItem
            {
                Key = key,
                Kind = MapOverlayKind.Factory,
                CellX = factory.X / 1000,
                CellY = factory.Y / 1000,
                Label = factory.Hp.ToString(CultureInfo.InvariantCulture),
                Tooltip = BuildFactoryTooltip(factory),
                Background = GetTeamBrush(factory.TeamId),
                BorderBrush = Brushes.White,
                Foreground = Brushes.White,
                Opacity = 0.92
            });
        }

        private void UpsertResource(MessageOfResource resource, HashSet<string> seenResources, Action<string, string>? semanticLog)
        {
            string key = BuildKey("resource", resource.Id, resource.X, resource.Y);
            seenResources.Add(key);

            if (_resources.TryGetValue(key, out var previous) && _hasPrimedFrame)
            {
                if (previous.RemainingAmount > 0 && resource.RemainingAmount <= 0)
                {
                    semanticLog?.Invoke($"({resource.X / 1000},{resource.Y / 1000}) 的资源点已采尽", "INFO");
                }
            }

            _resources[key] = resource;
            _mapViewModel.UpsertDynamicOverlay(new MapOverlayItem
            {
                Key = key,
                Kind = MapOverlayKind.Resource,
                CellX = resource.X / 1000,
                CellY = resource.Y / 1000,
                Label = CompactNumber(resource.RemainingAmount),
                Tooltip = $"资源点 #{resource.Id}\n类型：{GetResourceTypeName(resource.ResourceType)}\n状态：{GetResourceStateName(resource.ResourceState)}\n剩余量：{resource.RemainingAmount}/{resource.MaxAmount}",
                Background = Brushes.Gold,
                BorderBrush = Brushes.DarkGoldenrod,
                Foreground = Brushes.Black,
                Opacity = 0.9
            });
        }

        private void UpsertComputeCenter(MessageOfComputeCenter center, HashSet<string> seenCenters, Action<string, string>? semanticLog)
        {
            string key = BuildKey("center", center.CenterId, center.X, center.Y);
            seenCenters.Add(key);

            if (_computeCenters.TryGetValue(key, out var previous) && _hasPrimedFrame)
            {
                if (previous.OwnerTeamId != center.OwnerTeamId)
                {
                    string stateText = center.OwnerTeamId > 0
                        ? $"{GetTeamName(center.OwnerTeamId)}占领了 ({center.X / 1000},{center.Y / 1000}) 的算力中心"
                        : $"({center.X / 1000},{center.Y / 1000}) 的算力中心回到中立";
                    semanticLog?.Invoke(stateText, center.OwnerTeamId > 0 ? "SUCCESS" : "WARNING");
                }
            }

            _computeCenters[key] = center;
            _mapViewModel.UpsertDynamicOverlay(new MapOverlayItem
            {
                Key = key,
                Kind = MapOverlayKind.ComputeCenter,
                CellX = center.X / 1000,
                CellY = center.Y / 1000,
                Label = center.OwnerTeamId > 0
                    ? center.OwnerTeamId.ToString(CultureInfo.InvariantCulture)
                    : center.OccupyProgress > 0 ? center.OccupyProgress.ToString(CultureInfo.InvariantCulture) : string.Empty,
                Tooltip = $"算力中心 #{center.CenterId}\n归属：{(center.OwnerTeamId > 0 ? GetTeamName(center.OwnerTeamId) : "中立")}\n占领进度：{center.OccupyProgress}",
                Background = center.OwnerTeamId > 0 ? GetTeamBrush(center.OwnerTeamId) : Brushes.LightBlue,
                BorderBrush = Brushes.White,
                Foreground = center.OwnerTeamId > 0 ? Brushes.White : Brushes.Black,
                Opacity = 0.9
            });
        }

        private void UpsertMarket(MessageOfMarket market, HashSet<string> seenMarkets)
        {
            string key = BuildKey("market", market.MarketId, market.X, market.Y);
            seenMarkets.Add(key);

            _markets[key] = market;
            _mapViewModel.UpsertDynamicOverlay(new MapOverlayItem
            {
                Key = key,
                Kind = MapOverlayKind.Market,
                CellX = market.X / 1000,
                CellY = market.Y / 1000,
                Label = string.Empty,
                Tooltip = BuildMarketTooltip(market),
                Background = Brushes.MediumPurple,
                BorderBrush = Brushes.White,
                Foreground = Brushes.White,
                Opacity = 0.86
            });
        }

        private void RebuildSummary()
        {
            Summary.BuildingSummary = BuildFactorySummary();
            Summary.ResourceSummary = BuildResourceSummary();
            Summary.ObjectiveSummary = BuildComputeCenterSummary();
            Summary.MarketSummary = BuildMarketSummary();
        }

        private string BuildFactorySummary()
        {
            if (_factories.Count == 0)
            {
                return "工厂：暂无上报";
            }

            var parts = Enumerable.Range(1, 4)
                .Select(teamId =>
                {
                    var teamFactories = _factories.Values.Where(factory => factory.TeamId == teamId).ToList();
                    if (teamFactories.Count == 0)
                    {
                        return $"队伍{teamId} 0 座";
                    }

                    int totalHp = teamFactories.Sum(factory => factory.Hp);
                    return $"队伍{teamId} {teamFactories.Count} 座（总血量 {totalHp}）";
                });

            return $"工厂：{string.Join(" | ", parts)}";
        }

        private string BuildResourceSummary()
        {
            if (_resources.Count == 0)
            {
                return "资源点：暂无上报";
            }

            int totalRemaining = _resources.Values.Sum(resource => resource.RemainingAmount);
            int totalMax = _resources.Values.Sum(resource => resource.MaxAmount);
            int harvestable = _resources.Values.Count(resource => resource.ResourceState == ResourceState.Harvestable);
            return $"资源点：{_resources.Count} 处 · 剩余 {totalRemaining}/{totalMax} · 可采集 {harvestable} 处";
        }

        private string BuildComputeCenterSummary()
        {
            if (_computeCenters.Count == 0)
            {
                return "算力中心：暂无上报";
            }

            int neutral = _computeCenters.Values.Count(center => center.OwnerTeamId <= 0);
            int contested = _computeCenters.Values.Count(center => center.OwnerTeamId <= 0 && center.OccupyProgress > 0);
            var owned = Enumerable.Range(1, 4)
                .Select(teamId => $"队伍{teamId} {_computeCenters.Values.Count(center => center.OwnerTeamId == teamId)} 座");
            return $"算力中心：{string.Join(" | ", owned)} | 中立 {neutral} 座 | 正在争夺 {contested} 座";
        }

        private string BuildMarketSummary()
        {
            if (_markets.Count == 0)
            {
                return "市场：暂无上报";
            }

            int totalPriceEntries = _markets.Values.Sum(market => market.PriceList.Count);
            return $"市场：{_markets.Count} 处 · 当前价目 {totalPriceEntries} 条";
        }

        private static string BuildKey(string prefix, long id, int x, int y)
        {
            return id > 0 ? $"{prefix}:{id}" : $"{prefix}:{x}:{y}";
        }

        private static IBrush GetTeamBrush(long teamId)
        {
            return teamId switch
            {
                1 => Brushes.Red,
                2 => Brushes.Blue,
                3 => Brushes.Green,
                4 => Brushes.Orange,
                _ => Brushes.Gray
            };
        }

        private static string GetTeamName(long teamId)
        {
            return teamId switch
            {
                1 => "队伍 1",
                2 => "队伍 2",
                3 => "队伍 3",
                4 => "队伍 4",
                0 => "未归属",
                _ => "未知队伍"
            };
        }

        private static bool IsKnownTeam(long teamId)
        {
            return teamId is >= 1 and <= 4;
        }

        private static long ResolveKnownFactoryTeamId(MessageOfFactory previous, MessageOfFactory current)
        {
            if (IsKnownTeam(current.TeamId))
            {
                return current.TeamId;
            }

            if (IsKnownTeam(previous.TeamId))
            {
                return previous.TeamId;
            }

            return 0;
        }

        private static string CompactNumber(int value)
        {
            if (value >= 10000)
            {
                return $"{value / 10000.0:0.#}万";
            }

            if (value >= 1000)
            {
                return $"{value / 1000.0:0.0}千";
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildMarketTooltip(MessageOfMarket market)
        {
            var entries = market.PriceList
                .OrderBy(entry => (int)entry.GoodsType)
                .Select(entry => $"· {GetGoodsTypeName(entry.GoodsType)}：{entry.Price}（成交 {entry.TradedQuantity}）");
            string lines = string.Join("\n", entries);

            return $"市场 #{market.MarketId}\n类型：{GetMarketTypeName(market.MarketType)}\n全部商品当前卖价：" +
                (string.IsNullOrWhiteSpace(lines) ? string.Empty : $"\n{lines}");
        }

        private static string BuildFactoryTooltip(MessageOfFactory factory)
        {
            return $"工厂 #{factory.FactoryId}\n" +
                $"归属：{GetTeamName(factory.TeamId)}\n" +
                $"生命值：{factory.Hp}\n" +
                $"仓储：{factory.Storage}\n" +
                $"算力：{factory.ComputingPower}\n" +
                $"可生产：{(factory.CanProduce ? "是" : "否")}\n" +
                $"可招募：{(factory.CanRecruit ? "是" : "否")}\n" +
                $"库存：\n{FormatFactoryInventory(factory)}";
        }

        private static string FormatFactoryInventory(MessageOfFactory factory)
        {
            if (factory.ProductInventory == null || factory.ProductInventory.Count == 0)
            {
                return "空";
            }

            var entries = factory.ProductInventory
                .OrderBy(entry => (int)entry.ProductType)
                .Select(entry => $"· {GetGoodsTypeName(entry.ProductType)}：{entry.Quantity}");
            return string.Join("\n", entries);
        }

        private static string GetResourceTypeName(ResourceType type)
        {
            return type switch
            {
                ResourceType.NullResourceType => "未知资源",
                ResourceType.SmallResource => "小型资源",
                ResourceType.MediumResource => "中型资源",
                ResourceType.LargeResource => "大型资源",
                _ => "未知资源"
            };
        }

        private static string GetResourceStateName(ResourceState state)
        {
            return state switch
            {
                ResourceState.NullEconomyResourceStste => "未知状态",
                ResourceState.Harvestable => "可采集",
                ResourceState.BeingHarvested => "采集中",
                ResourceState.Harvested => "已采尽",
                _ => "未知状态"
            };
        }

        private static string GetMarketTypeName(MarketType type)
        {
            return type switch
            {
                MarketType.NullMarketType => "未知市场",
                MarketType.SmallMarket => "小型市场",
                MarketType.MediumMarket => "中型市场",
                MarketType.LargeMarket => "大型市场",
                _ => "未知市场"
            };
        }

        private static string GetGoodsTypeName(GoodsType type)
        {
            return type switch
            {
                GoodsType.NullGoodsType => "未知商品",
                GoodsType.Semiconductor => "半导体",
                GoodsType.Medicine => "药品",
                GoodsType.Toys => "玩具",
                GoodsType.Clothes => "服饰",
                GoodsType.Food => "食品",
                _ => "未知商品"
            };
        }
    }
}
