using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.LockedValue;
using System.Threading.Tasks.Dataflow;

namespace GameClass.GameObj.Areas;

public class Resource(XY initPos)
    : Immovable(initPos, GameData.ResourceRadius, GameObjType.RESOURCE)
{
    public InVariableRange<long> HP { get; } = new(GameData.ResourceHP);
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.SQUARE;
    protected readonly object actionLock = new();
    public object ActionLock => actionLock;

    private ResourceState State = ResourceState.HARVESTABLE;
    public ResourceState Resourcestate
    {
        get
        {
            lock (actionLock)
                return State;
        }
    }
    private ResourceType resourceType = ResourceType.LARGE_RESOURCE;
    public ResourceType ResourceType
    {
        get
        {
            lock (actionLock)
                return resourceType;
        }
        set
        {
            lock (actionLock)
                resourceType = value;
        }
    }
    public void SetResourceState(ResourceState state)
    {
        lock (actionLock)
        {
            State = state;
        }
    }
    public long Harvest(int producespeed)
    {
        return -HP.SubRChange(producespeed);
    }
}