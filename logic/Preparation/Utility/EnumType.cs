namespace Preparation.Utility
{
    public enum PlaceType
    {
        NULL_PLACE_TYPE = 0,
        FACTORY = 1,           // 工厂（地图四个角落）
        SPACE = 2,             // 空地
        BARRIER = 3,           // 障碍
        BUSH = 4,              // 草丛
        RESOURCE = 5,          // 资源
        COMPUTE_CENTER = 6,    // 算力中心
        MARKET = 7,            // 市场
    }

    public enum GameObjType : uint
    {
        NULL = 0,
        CHARACTER = 1,
        BARRIER = 2,
        BUSH = 3,
        RESOURCE = 4,//资源
        COMPUTE_CENTER = 5,//算力中心
        MARKET = 6,
        FACTORY = 7,
        GOODS = 8,
        BRAIN = 9,
        OUTOFBOUNDBLOCK = 10,
        SPACE = 11
    }

    public enum ComputeCenterType
    {
        NULL = 0,
        OLCF = 1,
        ALCF = 2,
        LLNL_HPC = 3,
        NSCC_WUXI = 4,
        NSCC_FUANGZHOU = 5,
        EUROHPC_JU = 6,
    }
    public enum CharacterState //角色状态
    {
        NULL_CHARACTER_STATE = 0,
        IDLE = 1,
        HARVESTING = 2,
        ATTACKING = 3,
        OCUPPYING = 4,     //占领数据中心
        TRADING = 5,       // 交易中
        MOVING = 6,
        INVISIBLE = 7,
    }

    public enum ShapeType
    {
        NULL_SHAPE_TYPE = 0,
        CIRCLE = 1,
        SQUARE = 2,
    }

    public enum ResourceState //资源状态
    {
        NULL_RESOURCE_STATE = 0,
        HARVESTABLE = 1,
        BEING_HARVESTED = 2,
        HARVESTED = 3,
    }
    public enum ResourceType // 资源
    {
        NULL_RESOURCE_TYPE = 0,
        SMALL_RESOURCE = 1,
        MEDIUM_RESOURCE = 2,
        LARGE_RESOURCE = 3,
    }

    public enum ComputeSenterState //算力中心状态
    {
        NULL_COMPUTE_CENTER_STATE = 0,
        OCCUPYABLE = 1,
        OCCUPIED = 2,
        ROBBED = 3,
    }

    public enum GoodsType //产品类型
    {
        NULL_GOODS_TYPE = 0,
        SEMICONDUCTOR = 1,
        MEDICINE = 2,
        TOYS = 3,
        CLOTHES = 4,
        FOOD = 5,
    }

    public enum MarketType
    {
        NULL_MARKET_TYPE = 0,
        SMALL_MARKET = 1,
        MEDIUM_MARKET = 2,
        LARGE_MARKET = 3,
    }

    public enum CharacterType
    {
        Null = 0,
        DRONE = 1,
        ROBOT = 2,
        AUTONOMOUS_CAR = 3,
    }

    public enum TechType
    {
        NULL_TECH_TYPE = 0,
        INCREASE_HP = 1,
        INCREASE_ATTACK_POWER = 2,
        INCREASE_ATTACK_SIZE = 3,
        INCREASE_ROBUST = 4,
        INCREASE_MOVE_SPEED = 5,
        INCREASE_CARRY_CAPACITY = 6,
        INCREASE_HARVEST_EFFICIENCY = 7,
    }
}
