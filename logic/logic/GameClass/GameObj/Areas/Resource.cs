using Preparation.Interface;
using Preparation.Utility;
using Preparation.Utility.Value;
using Preparation.Utility.Value.SafeValue.LockedValue;
using System.Threading.Tasks.Dataflow;

namespace GameClass.GameObj.Areas;

public class Resource(XY initPos)
    : Immovable(initPos, GameData.NumOfPosGridPerCell / 2, GameObjType.RESOURCE)
{
    public InVariableRange<long> HP { get; } = new(GameData.ResourceHP);
    public override bool IsRigid(bool args = false) => true;
    public override ShapeType Shape => ShapeType.SQUARE;
    protected readonly object actionLock = new();
    public object ActionLock => actionLock;

    private ResourceState State = ResourceState.HARVESTABLE;
    public ResourceState ERstate
    {
        get
        {
            lock (actionLock)
                return State;
        }
    }
    public ResourceType EResourceType = ResourceType.LARGE_RESOURCE;
    public void SetERState(ResourceState state)
    {
        State = state;
    }
}