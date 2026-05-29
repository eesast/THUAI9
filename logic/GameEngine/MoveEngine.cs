using Microsoft.Extensions.Logging;
using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Logging;
using Preparation.Utility.Value;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameEngine
{
    /// <summary>
    /// Constrctor
    /// </summary>
    /// <param name="gameMap">游戏地图</param>
    /// <param name="OnCollision">
    /// <para>发生碰撞时要做的事情</para>
    /// <para>- 第一个参数为移动的物体</para>
    /// <para>- 第二个参数为撞到的物体</para>
    /// <para>- 第三个参数为移动的位移向量</para>
    /// <para>返回值见AfterCollision的定义</para>
    /// </param>
    /// <param name="EndMove">结束碰撞时要做的事情</param>
    public class MoveEngine(
        IMap gameMap,
        Func<IMovable, IGameObj, XY, MoveEngine.AfterCollision> OnCollision,
        Action<IMovable> EndMove
        )
    {
        /// <summary>
        /// 碰撞结束后要做的事情
        /// </summary>
        public enum AfterCollision
        {
            ContinueCheck = 0,  // 碰撞后继续检查其他碰撞,暂时没用
            MoveMax = 1,        // 行走最远距离
            Destroyed = 2,       // 物体已经毁坏
            Demage = 3
        }

        private readonly IMyTimer gameTimer = gameMap.Timer;
        private readonly Action<IMovable> EndMove = EndMove;

        public IGameObj? CheckCollision(IMovable obj, XY Pos)
        {
            return collisionChecker.CheckCollision(obj, Pos);
        }

        private readonly CollisionChecker collisionChecker = new(gameMap);
        private readonly Func<IMovable, IGameObj, XY, AfterCollision> OnCollision = OnCollision;

        /// <summary>
        /// 在无碰撞的前提下行走最远的距离
        /// </summary>
        /// <param name="obj">移动物体，默认obj.Rigid为true</param>
        /// <param name="moveVec">移动的位移向量</param>
        private bool MoveMax(IMovable obj, XY moveVec, long stateNum, long speedIncrease = 0)
        {
            /*由于四周是墙，所以人物永远不可能与越界方块碰撞*/
            double maxLen = collisionChecker.FindMax(obj, moveVec);
            maxLen = Math.Min(maxLen, (obj.MoveSpeed + speedIncrease) / GameData.NumOfStepPerSecond);
            if (maxLen <= 0) return false; // Blocked by obstacle, bail out early
            return (obj.MovingSetPos(new XY(moveVec, maxLen), stateNum)) >= 0;
        }

        private bool LoopDo(IMovable obj, double direction, ref double deltaLen, long stateNum, long speedIncrease = 0)
        {
            double moveVecLength = (obj.MoveSpeed + speedIncrease) / GameData.NumOfStepPerSecond;
            XY res = new(direction, moveVecLength);

            // 碰撞检测与解决
            bool flag;
            bool alreadyMoved = false;
            do
            {
                flag = false;
                IGameObj? collisionObj = collisionChecker.CheckCollisionWhenMoving(obj, res);
                if (collisionObj == null)
                    break;

                switch (OnCollision(obj, collisionObj, res))
                {
                    case AfterCollision.ContinueCheck:
                        flag = true;
                        break;
                    case AfterCollision.Destroyed:
                        return false;
                    case AfterCollision.MoveMax:
                        if (MoveMax(obj, res, stateNum, speedIncrease))
                        {
                            // 部分移动成功，消耗全部步长
                            deltaLen += moveVecLength;
                            alreadyMoved = true;
                            moveVecLength = 0;
                            res = new XY(direction, 0);
                        }
                        else
                        {
                            // 正前方被完全阻挡，尝试沿障碍物边缘滑行
                            // 从接近原方向的角开始逐步搜索
                            double[] offsets = [0, Math.PI / 2, -Math.PI / 2,
                                                Math.PI / 4, -Math.PI / 4,
                                                3 * Math.PI / 4, -3 * Math.PI / 4,
                                                Math.PI];
                            bool slid = false;
                            foreach (double off in offsets)
                            {
                                double sa = direction + off;
                                XY sv = new(sa, moveVecLength);
                                if (collisionChecker.CheckCollisionWhenMoving(obj, sv) == null)
                                {
                                    long ml = obj.MovingSetPos(sv, stateNum);
                                    if (ml >= 0)
                                    {
                                        deltaLen += Math.Sqrt(ml);
                                        alreadyMoved = true;
                                        slid = true;
                                        break;
                                    }
                                }
                            }
                            if (!slid) return false; // 所有方向均被阻挡，完全卡死
                        }
                        break;
                }
            } while (flag);

            if (!alreadyMoved)
            {
                long moveL = obj.MovingSetPos(res, stateNum);
                if (moveL == -1) return false;
                deltaLen = deltaLen + moveVecLength - Math.Sqrt(moveL);
            }
            return true;
        }

        public void MoveObj(IMovable obj, int moveTime, double direction, long stateNum,
            long speedIncrease = 0, Action? onComplete = null)
        {
            LogicLogging.logger.LogDebug(
                LogUtility.GetObjectInfo(obj)
                + $" position {obj.Position}, start moving in direction {direction}, with speed {obj.MoveSpeed + speedIncrease}");
            if (!gameTimer.IsGaming) { EndMove(obj); onComplete?.Invoke(); return; }
            lock (obj.ActionLock)
            {
                if (!obj.IsAvailableForMove) { EndMove(obj); onComplete?.Invoke(); return; }
                obj.IsMoving.SetROri(true);
            }
            // LongRunning: 移动任务会阻塞(Sleep)数百ms，不应占用ThreadPool线程
            XY startPos = obj.FastPosition;
            Task.Factory.StartNew(
                () =>
                {
                try
                {
                    double deltaLen = 0.0;
                    XY res = new(direction, 0.0);
                    IGameObj? collisionObj = null;
                    bool isEnded = false;
                    bool flag;

                    // 初始碰撞解决：如果角色当前位置已经和其他物体重叠，推开角色
                    do
                    {
                        flag = false;
                        collisionObj = collisionChecker.CheckCollision(obj, obj.Position);
                        if (collisionObj == null) break;

                        switch (OnCollision(obj, collisionObj, res))
                        {
                            case AfterCollision.ContinueCheck:
                                // OnCollision 已执行推离，重新检查
                                flag = true; break;
                            case AfterCollision.Destroyed:
                                isEnded = true; break;
                            case AfterCollision.MoveMax:
                                // 角色与障碍物重叠，沿最近方向推开（加随机扰动避免振荡）
                                int sx = obj.FastPosition.x - collisionObj.FastPosition.x;
                                int sy = obj.FastPosition.y - collisionObj.FastPosition.y;
                                double sd = Math.Sqrt((long)sx * sx + (long)sy * sy);
                                if (sd > 0)
                                {
                                    double overlap = obj.Radius + collisionObj.Radius - sd;
                                    if (overlap > 0)
                                    {
                                        // 随机扰动 ±15° 避免两个角色互相推开后再次撞回
                                        double baseAngle = Math.Atan2(sy, sx);
                                        double perturb = (Random.Shared.NextDouble() - 0.5) * Math.PI / 6;
                                        obj.MovingSetPos(new XY(baseAngle + perturb, overlap + 1.0), stateNum);
                                        flag = true; // 推开后重新检查是否还与其他物体重叠
                                    }
                                }
                                break;
                        }
                    } while (flag);

                    if (isEnded)
                    {
                        obj.IsMoving.SetROri(false);
                        EndMove(obj);
                        onComplete?.Invoke();
                        return;
                    }

                    // 帧步进移动循环（替代 FrameRateTaskExecutor，消除定时器开销）
                    int stepMs = GameData.NumOfPosGridPerCell / GameData.NumOfStepPerSecond;
                    int totalMs = 0;
                    bool stoppedDueToBlock = false;

                    while (totalMs < moveTime && gameTimer.IsGaming
                           && obj.StateNum == stateNum && obj.CanMove && !obj.IsRemoved)
                    {
                        if (totalMs + stepMs > moveTime)
                            stepMs = moveTime - totalMs;

                        Thread.Sleep(stepMs);
                        totalMs += stepMs;

                        if (!gameTimer.IsGaming) break;
                        if (obj.StateNum != stateNum || !obj.CanMove || obj.IsRemoved) break;

                        if (!LoopDo(obj, direction, ref deltaLen, stateNum, speedIncrease))
                        {
                            stoppedDueToBlock = true;
                            break;
                        }
                    }

                    // 剩余微小位移（如果前面被阻塞则跳过）
                    if (!stoppedDueToBlock && obj.StateNum == stateNum && obj.CanMove && !obj.IsRemoved)
                    {
                        int leftTime = moveTime - totalMs;
                        if (leftTime > 0)
                        {
                            Thread.Sleep(leftTime);
                            do
                            {
                                flag = false;
                                double moveVecLength = (double)deltaLen + leftTime * (obj.MoveSpeed + speedIncrease) / GameData.NumOfPosGridPerCell;
                                res = new XY(direction, moveVecLength);
                                collisionObj = collisionChecker.CheckCollisionWhenMoving(obj, res);
                                if (collisionObj == null)
                                {
                                    obj.MovingSetPos(res, stateNum);
                                }
                                else
                                {
                                    switch (OnCollision(obj, collisionObj, res))
                                    {
                                        case AfterCollision.ContinueCheck:
                                            flag = true; break;
                                        case AfterCollision.Destroyed:
                                            break;
                                        case AfterCollision.MoveMax:
                                            MoveMax(obj, res, stateNum, speedIncrease);
                                            break;
                                    }
                                }
                            } while (flag);
                        }
                    }

                    XY endPos = obj.FastPosition;
                    if (startPos.x == endPos.x && startPos.y == endPos.y)
                        LogicLogging.logger.LogWarning(
                            LogUtility.GetObjectInfo(obj)
                            + $" moved ZERO distance! start=({startPos.x},{startPos.y}) dir={direction:F3} blocked={stoppedDueToBlock}");

                    obj.IsMoving.SetROri(false);
                    EndMove(obj);
                    onComplete?.Invoke();
                }
                catch (Exception ex)
                {
                    LogicLogging.logger.LogError(
                        LogUtility.GetObjectInfo(obj)
                        + $" MoveObj inner task crashed: {ex}");
                    obj.IsMoving.SetROri(false);
                    EndMove(obj);
                    onComplete?.Invoke();
                }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            );
        }
    }
}
