using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.LockedValue;

namespace GameClass.GameObj.Areas;

public class ComputeCenter(XY initPos)
    : Immovable(initPos, GameData.ComputeCenterRadius, GameObjType.COMPUTE_CENTER)
{
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.SQUARE;

    protected readonly object actionLock = new();
    public object ActionLock => actionLock;

    private ComputeCenterState state = ComputeCenterState.OCCUPYABLE;
    public ComputeCenterState EState
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

    public ComputeCenter(XY initPos, ComputeCenterType type)
        : this(initPos)
    {
        centerType = type;
    }

    public void SetState(ComputeCenterState newState)
    {
        lock (actionLock)
        {
            state = newState;
        }
    }

    private bool isOccupied = false;
    // changed: track occupying team id instead of player id
    private long occupiedByTeamId = -1;

    public bool IsOccupied
    {
        get
        {
            lock (actionLock)
                return isOccupied;
        }
    }

    public long OccupiedByTeamId
    {
        get
        {
            lock (actionLock)
                return occupiedByTeamId;
        }
    }

    // Set occupied by team id
    public void SetOccupied(long teamId)
    {
        lock (actionLock)
        {
            isOccupied = true;
            occupiedByTeamId = teamId;
            state = ComputeCenterState.OCCUPIED;
        }
    }

    public void ClearOccupied()
    {
        lock (actionLock)
        {
            isOccupied = false;
            occupiedByTeamId = -1;
            state = ComputeCenterState.OCCUPYABLE;
        }
    }
}
