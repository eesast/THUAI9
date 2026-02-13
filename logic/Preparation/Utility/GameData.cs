using Preparation.Utility.Value;
using System;
namespace Preparation.Utility
{
    public static class GameData
    {
        public const int NumOfStepPerSecond = 100;          // 每秒行走步数
        public const int BaseCharacterSpeed = 2500;         // 角色基础移动速度
        public const int FrameDuration = 50;                // 每帧时长
        public const int CheckInterval = 10;                // 检查间隔
        public const uint GameDurationInSecond = 60 * 10;   // 游戏时长
        public const int LimitOfStopAndMove = 15;           // 停止和移动的最大间隔
        public const int ProduceSpeedPerSecond = 200;       // 每秒生产值
        public const int KnockedBackTime = 50;
        public const int KnockedBackSpeed = 1500;           // 击退速度(额外速度，需加上基础移速）
        public const int AdditionResourceAttackRange = 2000;//加成资源攻击范围

        public const int NumOfPosGridPerCell = 1000;    // 每格的【坐标单位】数
        public const int MapLength = 50000;             // 地图长度
        public const int MapRows = 50;                  // 行数
        public const int MapCols = 50;                  // 列数

        public const int CharacterRadius = 200;         // 角色半径
        public const int AdjustLength = 3;                // 碰撞调整距离

        public const int MaxRobust = 10;
        public const int MaxEfficiency = 2;

        public const int ResourceHP = 500;              // 资源血量

        public const int DroneHP = 3;
        public const int DroneCost = 50;
        public const int DroneATKsize = 1000;
        public const int DroneATKpower = 1;
        public const int DroneMaxLoad = 5;

        public const int AutonomouCarHP = 3;
        public const int AutonomouCarCost = 50;
        public const int AutonomouCarATKsize = 1000;
        public const int AutonomouCarATKpower = 1;
        public const int AutonomouCarMaxLoad = 5;

        public const int RobotHP = 3;
        public const int RobotCost = 50;
        public const int RobotATKsize = 1000;
        public const int RobotATKpower = 1;
        public const int RobotMaxLoad = 5;

        public const int ComputeCenterRadius = 2;

        // 占领算力中心所需时间（毫秒）
        public const int ComputeCenterOccupyTimeMs = 10_000;

        public static XY PosNotInGame = new(-1, -1); // 不在游戏中的坐标
        public static XY GetCellCenterPos(int x, int y)  // 求格子的中心坐标
            => new(x * NumOfPosGridPerCell + NumOfPosGridPerCell / 2,
                   y * NumOfPosGridPerCell + NumOfPosGridPerCell / 2);
        public static int PosGridToCellX(XY pos)  // 求坐标所在的格子的x坐标
            => pos.x / NumOfPosGridPerCell;
        public static int PosGridToCellY(XY pos)  // 求坐标所在的格子的y坐标
            => pos.y / NumOfPosGridPerCell;
        public static CellXY PosGridToCellXY(XY pos)  // 求坐标所在的格子的xy坐标
            => new(PosGridToCellX(pos), PosGridToCellY(pos));

        public static bool IsInTheSameCell(XY pos1, XY pos2) => PosGridToCellXY(pos1) == PosGridToCellXY(pos2);
        public static bool PartInTheSameCell(XY pos1, XY pos2)
        {
            return Math.Abs((pos1 - pos2).x) < CharacterRadius + (NumOfPosGridPerCell / 2)
                && Math.Abs((pos1 - pos2).y) < CharacterRadius + (NumOfPosGridPerCell / 2);
        }
        public static bool ApproachToInteract(XY pos1, XY pos2)
        {
            return Math.Abs(PosGridToCellX(pos1) - PosGridToCellX(pos2)) <= 1
                && Math.Abs(PosGridToCellY(pos1) - PosGridToCellY(pos2)) <= 1;
        }
        public static bool ApproachToInteractInACross(XY pos1, XY pos2)
        {
            if (pos1 == pos2) return false;
            return (Math.Abs(PosGridToCellX(pos1) - PosGridToCellX(pos2))
                  + Math.Abs(PosGridToCellY(pos1) - PosGridToCellY(pos2))) <= 1;
        }
        public static bool IsInTheRange(XY pos1, XY pos2, int range)
        {
            return (pos1 - pos2).Length() <= range;
        }
        public static bool IsOnTheSameLine(XY pos1, XY pos2, double angle)//以pos1为基准，检测pos2是否在以pos1为端点、与x轴正方向呈angle角的射线上（逆时针为正方向）
        {
            double sinx = (pos2 - pos1).y / (pos2 - pos1).Length();
            double cosx = (pos2 - pos1).x / (pos2 - pos1).Length();
            if (Math.Abs(sinx - Math.Sin(angle)) < 0.01 && Math.Abs(cosx - Math.Cos(angle)) < 0.01)
            {
                return true;
            }
            else
                return false;
        }
        //public static bool NeedCopy(GameObjType gameObjType)
        //{
        //    return gameObjType != GameObjType.NULL &&
        //           gameObjType != GameObjType.BARRIER &&
        //           gameObjType != GameObjType.BUSH &&
        //           gameObjType != GameObjType.HOME &&
        //            gameObjType != GameObjType.OUTOFBOUNDBLOCK;
        //}

    }
}
