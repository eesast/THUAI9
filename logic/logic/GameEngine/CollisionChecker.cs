using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue;
using System;

namespace GameEngine
{
    internal class CollisionChecker(IMap gameMap)
    {
        public IGameObj? CheckCollision(IMovable obj, XY Pos)
        {
            // 在列表中检查碰撞
            IGameObj? CheckCollisionInList(LockedClassList<IGameObj> lst)
            {
                return lst.Find(listObj => obj.WillCollideWith(listObj, Pos));
            }

            IGameObj? collisionObj;
            foreach (var list in lists)
            {
                if ((collisionObj = CheckCollisionInList(list)) != null)
                {
                    return collisionObj;
                }
            }

            return null;
        }
        /// <summary>
        /// 碰撞检测，如果这样行走是否会与之碰撞，返回与之碰撞的物体
        /// </summary>
        /// <param name="obj">移动的物体</param>
        /// <param name="moveVec">移动的位移向量</param>
        /// <returns>和它碰撞的物体</returns>
        public IGameObj? CheckCollisionWhenMoving(IMovable obj, XY moveVec)
        {
            XY nextPos = obj.Position + moveVec;
            if (!obj.IsRigid())
            {
                if (gameMap.IsOutOfBound(obj))
                    return gameMap.GetOutOfBound(nextPos);
                return null;
            }
            return CheckCollision(obj, nextPos);
        }
        public double FindMax(IMovable obj, XY moveVec)
        {
            XY nextPos = obj.Position + moveVec;
            double tmpMax = uint.MaxValue;  // 暂存最大值

            double maxDistance = uint.MaxValue;
            foreach (var lst in lists)
            {
                lst.ForEach(listObj =>
                {
                    // 如果再走一步发生碰撞
                    if (obj.WillCollideWith(listObj, nextPos))
                    {
                        switch (listObj.Shape)  // 默认obj为圆形
                        {
                            case ShapeType.CIRCLE:
                                {
                                    // 计算两者之间的距离
                                    double mod = XY.DistanceFloor3(listObj.Position, obj.Position);
                                    int orgDeltaX = listObj.Position.x - obj.Position.x;
                                    int orgDeltaY = listObj.Position.y - obj.Position.y;

                                    if (mod < listObj.Radius + obj.Radius)  // 如果两者已经重叠
                                    {
                                        tmpMax = 0;
                                    }
                                    else
                                    {
                                        double tmp = mod - obj.Radius - listObj.Radius;
                                        // 计算能走的最长距离，好像这么算有一点误差？
                                        tmp = (int)(tmp * 1000 / Math.Cos(Math.Atan2(orgDeltaY, orgDeltaX) - moveVec.Angle()));
                                        if (tmp < 0 || tmp > uint.MaxValue || double.IsNaN(tmp))
                                        {
                                            tmpMax = uint.MaxValue;
                                        }
                                        else
                                            tmpMax = tmp / 1000.0;
                                    }
                                    break;
                                }
                            case ShapeType.SQUARE:
                                {
                                    // if (obj.WillCollideWith(listObj, obj.Position))
                                    //     tmpMax = 0;
                                    // else tmpMax = MaxMoveToSquare(obj, listObj);
                                    // break;
                                    if (obj.WillCollideWith(listObj, obj.Position))
                                        tmpMax = 0;
                                    else
                                    {
                                        // 二分查找最大可能移动距离
                                        int left = 0, right = (int)moveVec.Length();
                                        while (left < right - 1)
                                        {
                                            int mid = (right - left) / 2 + left;
                                            if (obj.WillCollideWith(listObj, obj.Position + new XY(moveVec, mid)))
                                            {
                                                right = mid;
                                            }
                                            else
                                                left = mid;
                                        }
                                        tmpMax = (uint)left;
                                    }
                                    break;
                                }
                            default:
                                tmpMax = uint.MaxValue;
                                break;
                        }
                        if (tmpMax < maxDistance)
                            maxDistance = tmpMax;
                    }
                }
                );
            }
            return maxDistance;
        }

        readonly IMap gameMap = gameMap;
        private readonly LockedClassList<IGameObj>[] lists = [.. gameMap.GameObjDict.Values];
    }
}
