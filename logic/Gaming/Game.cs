using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;
using System.Collections.Generic;
using Preparation.Utility;
using Preparation.Interface;
using GameEngine;
using GameClass.GameObj.Areas;

namespace Game
{
    /// <summary>
    /// 面向选手的公开 API。请在其它 partial 文件中实现具体逻辑。
    /// 这些方法应是线程安全的入口，内部应走命令队列/权威循环执行。
    /// </summary>
    public partial class Game
    {
        private readonly IMap gameMap;
        public IMap Map => gameMap;

        public Game(IMap map)
        {
            gameMap = map;
            InitTeams();
            characterManager = new CharacterManager(this);
            actionManager = new ActionManager(this);
            attackManager = new AttackManager(this);
        }
        /// <summary>
        /// 请求让指定玩家的单位移动一段时间，朝某个方向（弧度）。
        /// </summary>
        /// <param name="playerId">玩家或单位 ID</param>
        /// <param name="direction">移动方向，单位弧度，0 为 +X 方向</param>
        /// <param name="timeMs">移动持续时间，毫秒</param>
        /// <returns>是否成功受理（不代表一定完成）</returns>
        public bool Move(long playerId, double direction, int timeMs)
            => characterManager != null && characterManager.Move(playerId, timeMs, direction);

        /// <summary>
        /// 发起一次攻击（可按方向或目标判定，按实际规则实现）。
        /// </summary>
        /// <param name="playerId">发起者 ID</param>
        /// <param name="direction">攻击方向，弧度；如按目标实现，可忽略</param>
        /// <returns>是否成功受理</returns>
        public bool Attack(long playerId, double direction)
            => throw new NotImplementedException();

        // 额外提供一个按目标攻击的 API（便于测试与管理器分工）。
        public bool AttackTarget(long attackerPlayerId, long targetPlayerId)
            => attackManager != null && attackManager.Attack(attackerPlayerId, targetPlayerId);

        /// <summary>
        /// 采集指定资源点，持续一定时间。
        /// </summary>
        /// <param name="playerId">采集者 ID</param>
        /// <param name="resourceId">资源点对象 ID</param>
        /// <param name="timeMs">采集时间（毫秒）</param>
        /// <returns>是否成功受理</returns>
        public bool Harvest(long playerId, long resourceId, int timeMs)
            => throw new NotImplementedException();

        /// <summary>
        /// 进行交易（购买/出售）某种商品。
        /// </summary>
        /// <param name="playerId">操作者 ID</param>
        /// <param name="type">商品类型</param>
        /// <param name="amount">数量（正数）</param>
        /// <param name="buy">true 表示购买，false 表示出售</param>
        /// <returns>是否成功受理</returns>
        public bool Trade(long playerId, Preparation.Utility.GoodsType type, int amount, bool buy)
            => throw new NotImplementedException();

        /// <summary>
        /// 占领指定算力中心，一般需要持续占领一段时间。
        /// </summary>
        /// <param name="playerId">操作者 ID</param>
        /// <param name="computeCenterId">算力中心对象 ID</param>
        /// <param name="timeMs">占领时间（毫秒）</param>
        /// <returns>是否成功受理</returns>
        public bool Occupy(long playerId, long computeCenterId, int timeMs)
            => throw new NotImplementedException();

        /// <summary>
        /// 使用/研发一项科技（根据实际赛题约束实现）。
        /// </summary>
        /// <param name="playerId">操作者 ID</param>
        /// <param name="tech">科技类型</param>
        /// <returns>是否成功受理</returns>
        public bool UplevelTech(long playerId, Preparation.Utility.TechType tech)
            => throw new NotImplementedException();

        /// <summary>
        /// 获取当前帧/时刻对该玩家可见的世界快照。
        /// </summary>
        /// <param name="playerId">玩家 ID</param>
        /// <returns>只读快照，用于决策/显示</returns>
        public WorldSnapshot GetSnapshot(long playerId)
        {
            // 视野半径：5 格
            int visionRadius = 5 * GameData.NumOfPosGridPerCell;

            if (!characterManager.TryGetCharacter(playerId, out var observer))
                return new WorldSnapshot(NowTime(), Array.Empty<object>());

            bool observerIsDrone = observer.CharacterType == CharacterType.DRONE;
            bool observerInBush = IsInBush(observer.Position);

            var visible = new List<object>(64);

            foreach (var kv in gameMap.GameObjDict)
            {
                var list = kv.Value;
                list.ForEach(obj =>
                {
                    // 距离判定（统一半径）
                    if (XY.DistanceCeil3(obj.Position, observer.Position) > visionRadius) return;

                    // 草丛规则仅对“单位间”可见性生效；无人机忽略草丛遮蔽
                    if (!observerIsDrone && obj.Type == GameObjType.CHARACTER)
                    {
                        bool targetInBush = IsInBush(obj.Position);
                        if (observerInBush != targetInBush) return; // 一个在草丛，一个不在草丛 → 不可见
                    }

                    visible.Add(obj);
                });
            }

            return new WorldSnapshot(NowTime(), visible);
        }

        private bool IsInBush(XY pos)
        {
            if (!gameMap.GameObjDict.TryGetValue(GameObjType.BUSH, out var bushes)) return false;
            bool inBush = false;
            bushes.ForEach(obj =>
            {
                if (inBush) return;
                if (obj is Bush b && XY.DistanceCeil3(b.Position, pos) <= b.Radius)
                    inBush = true;
            });
            return inBush;
        }

        /// <summary>
        /// 获取当前游戏进行的时间（毫秒）。
        /// </summary>
        public int NowTime()
            => gameMap?.Timer.NowTime() ?? 0;

        /// <summary>
        /// 只读世界快照（示例占位类型，可在其它 partial 中扩展实际字段）。
        /// </summary>
        public readonly struct WorldSnapshot
        {
            public WorldSnapshot(int nowTimeMs, IReadOnlyList<object> objects)
            {
                NowTimeMs = nowTimeMs;
                Objects = objects;
            }
            public int NowTimeMs { get; }
            public IReadOnlyList<object> Objects { get; }
        }
    }
}
