using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.Atomic;
using System.Threading;

namespace GameClass.GameObj
{
    /// <summary>
    /// 一切游戏元素的总基类。
    /// </summary>
    public abstract class GameObj(XY initPos, int initRadius, GameObjType initType) : IGameObj
    {
        private readonly ReaderWriterLockSlim gameObjReaderWriterLock = new();
        public ReaderWriterLockSlim GameObjReaderWriterLock => gameObjReaderWriterLock;
        protected readonly object gameObjLock = new();
        public object GameLock => gameObjLock;
        protected readonly XY birthPos = initPos;
        private readonly GameObjType type = initType;
        public GameObjType Type => type;
        private static long currentMaxID = 0;         // 目前游戏对象的最大ID
        public const long invalidID = long.MaxValue;  // 无效的ID
        public long ID { get; } = Interlocked.Increment(ref currentMaxID);

        protected XY position = initPos;
        protected XY fastPosition = initPos;
        public abstract XY Position { get; }
        /// <summary>
        /// Lock-free position snapshot for collision detection (no actionLock).
        /// Updated together with position under the same lock.
        /// </summary>
        public XY FastPosition => fastPosition;
        public abstract bool IsRigid(bool args = false);
        public abstract ShapeType Shape { get; }

        private readonly AtomicBool isRemoved = new(false);
        public AtomicBool IsRemoved { get => isRemoved; }
        public virtual bool TryToRemove()
        {
            return IsRemoved.TrySet(true);
        }
        public int Radius { get; } = initRadius;
        public virtual bool IgnoreCollideExecutor(IGameObj targetObj) => false;
    }
}
