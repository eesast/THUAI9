using Preparation.Utility.Value;
using System;
namespace Preparation.Utility
{
    public static class GameData
    {
        public const int NumOfStepPerSecond = 100;          // 每秒行走步数
        public const int BaseCharacterSpeed = 5000;         // 角色基础移动速度
        public const int FrameDuration = 50;                // 每帧时长
        public const int CheckInterval = 10;                // 检查间隔
        public const uint GameDurationInSecond = 10 * 60;  // 游戏时长
        public const int LimitOfStopAndMove = 15;           // 停止和移动的最大间隔
        public const int ProduceSpeedPerSecond = 200;       // 每秒生产值
        public const int KnockedBackTime = 50;
        public const int KnockedBackSpeed = 1500;           // 击退速度(额外速度，需加上基础移速)
        public const int AdditionResourceAttackRange = 2000;//加成资源攻击范围

        public const int NumOfPosGridPerCell = 1000;    // 每格的【坐标单位】数
        public const int MapLength = 50000;             // 地图长度
        public const int MapRows = 50;                  // 行数
        public const int MapCols = 50;                  // 列数

        public const int CharacterRadius = 300;         // 角色半径
        public const int AdjustLength = 3;                // 碰撞调整距离

        public const int MaxCharactersPerTeam = 3;          // 每队最大存活角色数

        public const int MaxRobust = 50;
        public const int MaxEfficiency = 10;
        public const int MaxHP = 300;
        public const int MaxCost = 2;
        public const int MaxATKSize = 3 * NumOfPosGridPerCell;
        public const int MaxATKPower = 100;
        public const int MaxLoad = 30;
        public const int MaxViewRange = 10 * NumOfPosGridPerCell;
        public const int MaxStorage = 150;

        public const int ResourceHP = 500;              // 资源血量

        public const int FactoryScore = 10000;
        public const int FactoryDisableTimeMs = 10_00;
        public const int FactoryInvulnerableTimeMs = 30 * 1000;  // 前30秒工厂不掉血

        // Score multipliers for combat
        public const int CharacterDamageScoreMultiplier = 20;
        public const int CharacterKillScoreMultiplier = 40;

        public const int FactoryHP = 5000;
        public const int FactoryStorage = 5;
        public const int FactoryRobust = 20;
        public const int FactoryEfficiency = 1;
        // initial computing power for newly created factories
        public const int FactoryInitialComputingPower = 100;

        public const int FactoryEfficiencyMax = 2; // maximum efficiency for factory
        public const int FactoryInitialSource = 0; // initial source stored in factory
        public const int FactoryInitialScore = 0; // initial score award or baseline

        public const int DroneHP = 100;
        public const int DroneCost = 50;
        public const int DroneATKsize = 1000;
        public const int DroneATKpower = 40;
        public const int DroneLoad = 5;
        public const int DroneRobust = 10;
        public const int DroneViewRange = 7 * NumOfPosGridPerCell;
        public const int DroneEfficiency = 1;
        public const int DroneMoveSpeed = BaseCharacterSpeed;

        public const int AutonomouCarHP = 100;
        public const int AutonomouCarCost = 50;
        public const int AutonomouCarATKsize = 1000;
        public const int AutonomouCarATKpower = 18;
        public const int AutonomouCarLoad = 5;
        public const int AutonomouCarRobust = 8;
        public const int AutonomouCarViewRange = NumOfPosGridPerCell * 5;
        public const int AutonomouCarEfficiency = 2;
        public const int AutonomouCarMoveSpeed = BaseCharacterSpeed;

        public const int RobotHP = 150;
        public const int RobotCost = 50;
        public const int RobotATKsize = 1000;
        public const int RobotATKpower = 30;
        public const int RobotLoad = 5;
        public const int RobotRobust = 15;
        public const int RobotViewRange = NumOfPosGridPerCell * 5;
        public const int RobotEfficiency = 1;
        public const int RobotMoveSpeed = BaseCharacterSpeed;

        public const int ComputeCenterOccupyTimeMs = 10_000;

        // factory compute power production per second
        public const int FactoryComputePowerPerSecond = 1;
        public const int FactoryComputePowerBonusPerCenterPerSecond = 2;

        public const int BasePriceSemiconductor = 180;
        public const int BasePriceMedicine = 110;
        public const int BasePriceToys = 25;
        public const int BasePriceClothes = 70;
        public const int BasePriceFood = 20;

        public const double SmallMarketMultiplier = 1.1;
        public const double MediumMarketMultiplier = 1.3;
        public const double LargeMarketMultiplier = 1.5;

        // Market decay configuration
        public const int MarketDecayThreshold = 50; // traded quantity per step
        public const double MarketDecayStep = 0.05; // reduce multiplier by 5% per step
        public const double MarketMinMultiplier = 0.5; // floor multiplier

        public const int CostSemiconductor = 10;
        public const int CostMedicine = 5;
        public const int CostToys = 1;
        public const int CostClothes = 8;
        public const int CostFood = 3;

        public const int ProduceTimeSemiconductor = 5;
        public const int ProduceTimeMedicine = 4;
        public const int ProduceTimeToys = 2;
        public const int ProduceTimeClothes = 6;
        public const int ProduceTimeFood = 1;

        public const int TechMaxLevel = 2;
        public const int TechCostHP = 30;
        public const int TechCostRobust = 30;
        public const int TechCostWarrior = 60;
        public const int TechCostAttackSize = 60;
        public const int TechCostMoveSpeed = 40;
        public const int TechCostCarry = 50;
        public const int TechCostEfficiency = 40;
        public const int TechCostProduction = 60;
        public const int TechCostStorage = 50;
        public const int TechCostPrice = 80;
        public const int TechCostDecreaseCost = 50;

        public const double TechHpMultiplierPerLevel = 0.2;
        public const int TechRobustAddPerLevel = 5;
        public const int TechEfficiencyAddPerLevel = 2;
        public const double TechWarriorAtkMultiplierPerLevel = 0.3;
        public const int TechAttackSizeAddPerLevel = 500;
        public const int TechMoveSpeedAddPerLevel = 200;
        public const int TechCarryAddPerLevel = 10;
        public const int TechStorageAddPerLevel = 50;
        public const int TechProductionEfficiencyAddPerLevel = 1;
        public const double TechPriceMultiplierPerLevel = 0.1;
        public const int TechCostDecreasePerLevel = 2;

        // radii for buildings (in position-grid units). Defaults use half a cell.
        public const int FactoryRadius = NumOfPosGridPerCell / 2;
        public const int MarketRadius = NumOfPosGridPerCell / 2;
        public const int ResourceRadius = NumOfPosGridPerCell / 2;
        public const int ComputeCenterRadius = NumOfPosGridPerCell / 2;
        public const int SpaceRadius = NumOfPosGridPerCell / 2;
        public const int BarrierRadius = NumOfPosGridPerCell / 2;

        public static string API_key = "Mzg2MDU0MmEtNjcxZS00NjVkLTkxY2QtYTI3NzdjY2NhODU4";
        public static string API_url = "https://api.eesast.com/llm/chat";
        public static string ModelName = "deepseek-v4-pro";
        public const int AskAICostComputingPower = 10;
        public const int AskAIPromptMaxLength = 512;
        public const int AskAITimeoutMs = 60_000;

        // Event system
        public const int MarketEventIntervalMs = 30 * 1000; // 测试用 30 秒，正式应为 3*60*1000

        // Efficiency conversion: how much each efficiency unit multiplies task speed
        public const double EfficiencyMultiplierPerLevel = 0.2; // each efficiency point => +20% speed

        public static XY PosNotInGame = new(-1, -1);
        public static XY GetCellCenterPos(int x, int y)
            => new(x * NumOfPosGridPerCell + NumOfPosGridPerCell / 2,
                   y * NumOfPosGridPerCell + NumOfPosGridPerCell / 2);
        public static int PosGridToCellX(XY pos)
            => pos.x / NumOfPosGridPerCell;
        public static int PosGridToCellY(XY pos)
            => pos.y / NumOfPosGridPerCell;
        public static CellXY PosGridToCellXY(XY pos)
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
        public static bool IsOnTheSameLine(XY pos1, XY pos2, double angle)
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
