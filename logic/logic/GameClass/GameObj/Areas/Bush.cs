using Preparation.Utility;
using Preparation.Utility.Value;

namespace GameClass.GameObj.Areas;

public class Bush(XY initPos)
    : Immovable(initPos, GameData.NumOfPosGridPerCell / 2, GameObjType.BUSH)
{
    public override bool IsRigid(bool args = false) => false;
    public override ShapeType Shape => ShapeType.NULL_SHAPE_TYPE;
    public void Hide(Character character)
    {
        // 若角色进入草丛范围则隐身（基于半径判断）
        if (XY.DistanceCeil3(character.Position, Position) <= Radius)
        {
            character.Visible = false;
        }
    }

}
