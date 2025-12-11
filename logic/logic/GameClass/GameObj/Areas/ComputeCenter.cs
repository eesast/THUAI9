using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.LockedValue;

namespace GameClass.GameObj.Areas;

public class ComputeCenter(XY initPos)
    : Immovable(initPos, GameData.NumOfPosGridPerCell / 2, GameObjType.COMPUTE_CENTER)
{
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.SQUARE;

    protected readonly object actionLock = new();
    public object ActionLock => actionLock;

    private ComputeSenterState state = ComputeSenterState.OCCUPYABLE;
    public ComputeSenterState EState
    {
        get
        {
            lock (actionLock)
                return state;
        }
    }
    private ComputeCenterType centerType = ComputeCenterType.OLCF;
    public ComputeCenterType EComputeCenterType
    {
        get
        {
            lock (actionLock)
                return centerType;
        }
        set
        {
            lock (actionLock)
                centerType = value;
        }
    }

    // 允许在构造时设置算力中心类型
    public ComputeCenter(XY initPos, ComputeCenterType type)
        : this(initPos)
    {
        // 构造期设置，无需并发锁，但保持与属性一致性
        centerType = type;
    }

    public void SetState(ComputeSenterState newState)
    {
        lock (actionLock)
        {
            state = newState;
        }
    }

    // 是否被占领与占领者ID标志
    private bool isOccupied = false;
    private long occupiedByPlayerId = -1;

    public bool IsOccupied
    {
        get
        {
            lock (actionLock)
                return isOccupied;
        }
    }

    public long OccupiedByPlayerId
    {
        get
        {
            lock (actionLock)
                return occupiedByPlayerId;
        }
    }

    public void SetOccupied(long playerId)
    {
        lock (actionLock)
        {
            isOccupied = true;
            occupiedByPlayerId = playerId;
            state = ComputeSenterState.OCCUPIED;
        }
    }

    public void ClearOccupied()
    {
        lock (actionLock)
        {
            isOccupied = false;
            occupiedByPlayerId = -1;
            state = ComputeSenterState.OCCUPYABLE;
        }
    }
}
