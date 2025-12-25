using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameClass.GameObj;
using Preparation.Utility;

namespace Game
{
    public partial class Game
    {
        private readonly ConcurrentDictionary<long, ProductionQueue> productionQueues = new();

        private sealed class ProductionTask
        {
            public GoodsType Type { get; }
            public int Quantity { get; }
            public int ProductionTimeMs { get; }
            public int ResourceCost { get; }

            public ProductionTask(GoodsType type, int quantity, int productionTimeMs, int resourceCost)
            {
                Type = type;
                Quantity = quantity;
                ProductionTimeMs = productionTimeMs;
                ResourceCost = resourceCost;
            }
        }

        private sealed class ProductionQueue
        {
            private readonly long teamId;
            private readonly Game game;
            private readonly Queue<ProductionTask> tasks = new();
            private readonly object queueLock = new();
            private CancellationTokenSource? currentTaskCts;
            private bool isProcessing = false;

            public ProductionQueue(long teamId, Game game)
            {
                this.teamId = teamId;
                this.game = game;
            }

            public bool Enqueue(ProductionTask task)
            {
                lock (queueLock)
                {
                    tasks.Enqueue(task);
                    if (!isProcessing)
                    {
                        StartProcessing();
                    }
                    return true;
                }
            }

            private void StartProcessing()
            {
                isProcessing = true;
                Task.Run(ProcessQueue);
            }

            private async Task ProcessQueue()
            {
                while (true)
                {
                    ProductionTask? task;
                    lock (queueLock)
                    {
                        if (tasks.Count == 0)
                        {
                            isProcessing = false;
                            return;
                        }
                        task = tasks.Dequeue();
                    }

                    if (task != null)
                    {
                        await ProcessTask(task);
                    }
                }
            }

            private async Task ProcessTask(ProductionTask task)
            {
                var cts = new CancellationTokenSource();
                currentTaskCts = cts;

                try
                {
                    // Check resource availability and deduct atomically
                    var factory = game.GetTeamFactory(teamId);
                    if (factory == null) return;

                    int totalResourceNeeded = task.ResourceCost * task.Quantity;
                    
                    // Atomic check and deduction using CAS loop
                    bool resourcesDeducted = false;
                    while (!resourcesDeducted)
                    {
                        int currentResources = factory.Source.Get();
                        if (currentResources < totalResourceNeeded)
                        {
                            // Not enough resources
                            return;
                        }
                        int newResources = currentResources - totalResourceNeeded;
                        if (factory.Source.CompareExROri(newResources, currentResources) == currentResources)
                        {
                            // CAS succeeded, resources deducted
                            resourcesDeducted = true;
                        }
                        // If CAS failed, loop and retry
                    }

                    // Simulate production time
                    int elapsed = 0;
                    int totalTime = task.ProductionTimeMs * task.Quantity;
                    int step = 100; // 100ms tick

                    while (!cts.IsCancellationRequested && elapsed < totalTime)
                    {
                        if (!game.Map.Timer.IsGaming)
                        {
                            await Task.Delay(step, cts.Token);
                            continue;
                        }
                        await Task.Delay(step, cts.Token);
                        elapsed += step;
                    }

                    if (!cts.IsCancellationRequested && elapsed >= totalTime)
                    {
                        // Production complete, add goods to factory
                        factory.AddGoods(task.Type, task.Quantity);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Task was cancelled, nothing to do
                }
                finally
                {
                    currentTaskCts = null;
                    cts.Dispose();
                }
            }

            public void Cancel()
            {
                currentTaskCts?.Cancel();
                lock (queueLock)
                {
                    tasks.Clear();
                }
            }
        }

        private void InitProductionQueues()
        {
            for (int i = 1; i <= 4; i++)
            {
                long teamId = i;
                productionQueues.TryAdd(teamId, new ProductionQueue(teamId, this));
            }
        }

        public bool Produce(long teamId, GoodsType type, int quantity)
        {
            if (quantity <= 0) return false;
            if (type == GoodsType.NULL_GOODS_TYPE) return false;

            // Get production parameters based on goods type
            int costPerUnit, productionTimeSeconds;
            switch (type)
            {
                case GoodsType.SEMICONDUCTOR:
                    costPerUnit = 10;
                    productionTimeSeconds = 5;
                    break;
                case GoodsType.MEDICINE:
                    costPerUnit = 5;
                    productionTimeSeconds = 4;
                    break;
                case GoodsType.TOYS:
                    costPerUnit = 1;
                    productionTimeSeconds = 2;
                    break;
                case GoodsType.CLOTHES:
                    costPerUnit = 8;
                    productionTimeSeconds = 6;
                    break;
                case GoodsType.FOOD:
                    costPerUnit = 3;
                    productionTimeSeconds = 1;
                    break;
                default:
                    return false;
            }

            int productionTimeMs = productionTimeSeconds * 1000;
            var task = new ProductionTask(type, quantity, productionTimeMs, costPerUnit);

            if (productionQueues.TryGetValue(teamId, out var queue))
            {
                return queue.Enqueue(task);
            }

            return false;
        }

        public bool CancelProduction(long teamId)
        {
            if (productionQueues.TryGetValue(teamId, out var queue))
            {
                queue.Cancel();
                return true;
            }
            return false;
        }
    }
}
