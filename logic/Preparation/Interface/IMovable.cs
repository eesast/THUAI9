using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.Atomic;
using System;
using System.Threading;

namespace Preparation.Interface
{
    public interface IMovable : IGameObj
    {
        public XY FacingDirection { get; set; }
        object ActionLock { get; }
        public AtomicInt MoveSpeed { get; }
        public AtomicBool CanMove { get; }
        public AtomicBool IsMoving { get; }
        public bool IsAvailableForMove { get; }
        public long StateNum { get; }
        public Semaphore ThreadNum { get; }
        public long MovingSetPos(XY moveVec, long stateNum);
        public bool WillCollideWith(IGameObj? targetObj, XY nextPos, bool collideWithWormhole = false)  // 检查下一位置是否会和目标物碰撞
        {
            if (targetObj == null)
                return false;
            if (!targetObj.IsRigid(collideWithWormhole) || targetObj.ID == ID)
                return false;

            if (IgnoreCollideExecutor(targetObj) || targetObj.IgnoreCollideExecutor(this))
                return false;

            // Quick bounding-box filter using lock-free FastPosition
            int dx = Math.Abs(nextPos.x - targetObj.FastPosition.x);
            int dy = Math.Abs(nextPos.y - targetObj.FastPosition.y);
            int maxDist = targetObj.Radius + Radius;
            if (dx > maxDist || dy > maxDist)
                return false;

            if (targetObj.Shape == ShapeType.CIRCLE)
            {
                return XY.DistanceCeil3(nextPos, targetObj.FastPosition) < maxDist;
            }
            else  // Square
            {
                if (dx >= maxDist || dy >= maxDist)
                    return false;
                if (dx < targetObj.Radius || dy < targetObj.Radius)
                    return true;
                else
                    return ((long)(dx - targetObj.Radius) * (dx - targetObj.Radius)) + ((long)(dy - targetObj.Radius) * (dy - targetObj.Radius)) <= (long)Radius * (long)Radius;
            }
        }
    }
}
