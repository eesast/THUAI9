"""
Game configuration constants and the GameConfig dataclass.
All game parameters are centralized here for easy difficulty tuning.
"""
from dataclasses import dataclass, field
from typing import Dict, Tuple
import math

# ── Product definitions ────────────────────────────────────────────────────────
PRODUCT_IDS = list(range(5))

PRODUCT_DEFS: Dict[int, dict] = {
    # raw_cost: units of raw material consumed per item produced at factory
    0: {"name": "semiconductor", "zh": "半导体", "cost": 10, "raw_cost": 5, "val_range": (40, 120), "produce_time": 5.0},
    1: {"name": "medicine",      "zh": "药品",   "cost": 5,  "raw_cost": 3, "val_range": (20, 60),  "produce_time": 4.0},
    2: {"name": "small_goods",   "zh": "小商品", "cost": 1,  "raw_cost": 1, "val_range": (4,  12),  "produce_time": 2.0},
    3: {"name": "clothing",      "zh": "服饰",   "cost": 8,  "raw_cost": 4, "val_range": (32, 96),  "produce_time": 6.0},
    4: {"name": "food",          "zh": "食品",   "cost": 3,  "raw_cost": 2, "val_range": (12, 24),  "produce_time": 1.0},
}

# ── Tech tree ──────────────────────────────────────────────────────────────────
TECH_TREE: Dict[str, dict] = {
    "cost_reduction":    {"cost": 50, "prereq": None,         "persistent": True,
                          "effect": {"product_cost_delta": -2}},
    "efficiency":        {"cost": 40, "prereq": None,         "persistent": True,
                          "effect": {"time_multiplier": 0.5}},
    "marketing":         {"cost": 80, "prereq": None,         "persistent": True,
                          "effect": {"price_multiplier": 1.1}},
    "durability":        {"cost": 30, "prereq": None,         "persistent": True,
                          "effect": {"capacity_pct": 0.5}},
    "multi_line":        {"cost": 60, "prereq": None,         "persistent": True,
                          "effect": {"extra_lines": 1}},
    "path_optimization": {"cost": 50, "prereq": "efficiency", "persistent": True,
                          "effect": {"move_factor": 0.9}},
    "market_analysis":   {"cost": 40, "prereq": None,         "persistent": False,
                          "effect": {"reveal": True}},
    "compute_expansion": {"cost": 70, "prereq": None,         "persistent": True,
                          "effect": {"compute_rate_bonus": 0.3}},
}

# Grid cell type codes
CELL_EMPTY         = 0
CELL_OBSTACLE      = 1
CELL_MARKET        = 2
CELL_RESOURCE      = 3
CELL_COMPUTE       = 4
CELL_FACTORY       = 5


@dataclass
class GameConfig:
    # Map
    map_width: int = 10
    map_height: int = 10
    num_markets: int = 3
    num_resource_points: int = 2
    num_compute_centers: int = 2
    random_map: bool = True

    # Time
    time_step: float = 0.25          # seconds per tick
    max_game_time: float = 300.0     # 300 s = 5 min

    # Unit
    unit_hp: int = 300
    unit_capacity: int = 30          # max goods carried
    unit_harvest_rate: float = 10.0  # resources/second at resource point
    unit_occupy_time: float = 10.0   # seconds to unlock compute center

    # Factory
    factory_x: int = 0
    factory_y: int = 0
    initial_money: float = 50.0
    initial_compute: float = 30.0
    factory_storage_cap: int = 300
    initial_production_lines: int = 3

    # Compute / recruit
    base_compute_rate: float = 1.0   # compute pts/second per open center
    recruit_cost: int = 40           # compute pts to recruit a new unit
    max_units: int = 5

    # Difficulty knobs (iterated over time)
    price_volatility: float = 1.0    # multiplies price amplitude
    resource_regen_rate: float = 1.0
    initial_resource_stock: int = 100
    market_period: float = 100.0     # seconds for one price oscillation

    # Scoring
    score_factor: float = 10.0

    @property
    def max_steps(self) -> int:
        return int(self.max_game_time / self.time_step)

    # ── Preset difficulty levels ───────────────────────────────────────────────
    @classmethod
    def easy(cls) -> "GameConfig":
        return cls(
            map_width=5, map_height=5,
            num_markets=3, num_resource_points=2, num_compute_centers=1,
            initial_money=200.0, initial_compute=60.0,
            price_volatility=0.3, resource_regen_rate=2.0,
            initial_resource_stock=200,
        )

    @classmethod
    def medium(cls) -> "GameConfig":
        return cls()

    @classmethod
    def hard(cls) -> "GameConfig":
        return cls(
            map_width=15, map_height=15,
            num_markets=4, num_resource_points=4, num_compute_centers=3,
            initial_money=30.0, initial_compute=20.0,
            price_volatility=2.0, resource_regen_rate=0.5,
            initial_resource_stock=50, max_game_time=500.0,
        )

    @classmethod
    def from_dict(cls, d: dict) -> "GameConfig":
        return cls(**{k: v for k, v in d.items() if k in cls.__dataclass_fields__})
