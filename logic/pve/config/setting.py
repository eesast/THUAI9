# ==========================================
# 1. 地图与环境设置 (Map & Environment)
# ==========================================

# 地图尺寸
MAP_WIDTH = 7
MAP_HEIGHT = 7

# 随机场景规模
MARKET_COUNT = 3
RESOURCE_COUNT = 2
OBSTACLE_COUNT = 6

# 地块类型编码 (Grid Types)
GRID_TYPE_EMPTY = 0    # 空地
GRID_TYPE_OBSTACLE = 1 # 障碍
GRID_TYPE_MARKET = 2   # 市场
GRID_TYPE_RESOURCE = 3 # 资源点
GRID_TYPE_FACTORY = 4  # 工厂
GRID_TYPE_COMPUTE_CENTER = 5  # 算力中心

# 时间设置
TIME_STEP_DURATION = 0.25  # 秒

# ==========================================
# 2. 玩家与单位设置 (Player & Units)
# ==========================================

# 初始资源
INITIAL_MONEY = 1000
INITIAL_COMPUTE = 30

# 移动单位属性
UNIT_SPEED = 2  # 格/s
UNIT_CAPACITY = 30  # 件 (扩大容量)
UNIT_HP = 300  # 单位耐久度

# ==========================================
# 3. 工厂设置 (Factory)
# ==========================================

FACTORY_STORAGE_CAP = 300  # 工厂仓储容量
FACTORY_LINES = 3  # 生产线数量

# ==========================================
# 4. 产品属性 (Products)
# ==========================================

# 产品ID定义
PRODUCT_SEMICONDUCTOR = 0
PRODUCT_MEDICINE = 1
PRODUCT_COMMODITY = 2
PRODUCT_CLOTHING = 3
PRODUCT_FOOD = 4

# 买入、卖出时间
PRODUCT_TRANSACTION_TIME = 0.25  # 秒

# 市场交易价差：买入按 ask = 中间价 * (1 + rate)，卖出按 bid = 中间价 * (1 - rate)
MARKET_SPREAD_RATE = 0.03

# 禁止把从某个市场买来的产品直接卖回同一个市场，避免原地无风险套利
BLOCK_SAME_MARKET_RESALE = True

# 每局市场随机价格条件
MARKET_PERIOD_MIN = 70.0
MARKET_PERIOD_MAX = 130.0
MARKET_PRICE_SCALE_MIN = 0.85
MARKET_PRICE_SCALE_MAX = 1.15

# 市场库存与需求冲击
MARKET_INITIAL_STOCK_MIN = 3.0
MARKET_INITIAL_STOCK_MAX = 8.0
MARKET_MAX_STOCK = 10.0
MARKET_STOCK_REPLENISH_PER_STEP = 0.015
MARKET_INITIAL_DEMAND_MIN = 3.0
MARKET_INITIAL_DEMAND_MAX = 8.0
MARKET_MAX_DEMAND = 10.0
MARKET_DEMAND_REPLENISH_PER_STEP = 0.02
MARKET_SCARCITY_PRICE_IMPACT = 0.15
MARKET_LOW_DEMAND_DISCOUNT = 0.25

# 产品详细配置表
# 按规则：| 产品   | 成本 | 价值（基础） | 耗时 |
PRODUCTS = {
    PRODUCT_SEMICONDUCTOR: {
        "name": "半导体",
        "cost": 10,
        "val_range": (40, 120),
        "duration": 5,
    },
    PRODUCT_MEDICINE: {
        "name": "药品",
        "cost": 5,
        "val_range": (20, 60),
        "duration": 4,
    },
    PRODUCT_COMMODITY: {
        "name": "小商品",
        "cost": 1,
        "val_range": (4, 12),
        "duration": 2,
    },
    PRODUCT_CLOTHING: {
        "name": "服饰",
        "cost": 8,
        "val_range": (32, 96),
        "duration": 6,
    },
    PRODUCT_FOOD: {
        "name": "食品",
        "cost": 3,
        "val_range": (12, 24),
        "duration": 1,
    },
}

# ==========================================
# 5. 资源设置 (Resources)
# ==========================================

NUM_RESOURCES = 3
HARVESTING_TIME = 1.0  # 采集时间（秒）

# ==========================================
# 6. 动作空间定义 (Action Space)
# ==========================================

U_ACT_WAIT = 0

# 移动指令
U_ACT_MOVE_UP = 1
U_ACT_MOVE_DOWN = 2
U_ACT_MOVE_LEFT = 3
U_ACT_MOVE_RIGHT = 4

# 装载指令
U_ACT_LOAD_0 = 5

# 出售指令
U_ACT_SELL_ALL = 6

# 采集指令
U_ACT_HARVEST = 7

# ==========================================
# 7. 积分规则 (Scoring)
# ==========================================
SCORE_SALES_FACTOR = 10.0  # 销售额 x 10
