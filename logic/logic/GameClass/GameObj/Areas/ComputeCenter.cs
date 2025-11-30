using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameClass.GameObj.Areas
{
    class ComputeCenter (XY initPos)
    : Immovable(initPos, GameData.NumOfPosGridPerCell / 2, GameObjType.RESOURCE)
    {

    }
}
