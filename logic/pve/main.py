"""
核心游戏环境 - 根据规则_PVE.md 实现

包含：
- 地图管理（市场、资源点、障碍）
- 单位管理（移动、采集、交易）
- 市场系统（动态价格）
- 工厂系统（生产、仓储）
"""

import math
import random
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple
import numpy as np
from config.setting import *


@dataclass
class Point:
    """二维坐标点"""
    x: int
    y: int

    def distance_to(self, other: 'Point') -> int:
        """曼哈顿距离"""
        return abs(self.x - other.x) + abs(self.y - other.y)

    def in_range(self, other: 'Point', radius: int) -> bool:
        """检查是否在范围内"""
        return self.distance_to(other) <= radius


class Market:
    """市场：提供产品交易和动态价格"""

    def __init__(self, x: int, y: int, market_id: int, name: str = "Market"):
        self.pos = Point(x, y)
        self.id = market_id
        self.name = name
        self.price_funcs: Dict[int, callable] = {}
        self._init_price_functions()

    def _init_price_functions(self):
        """初始化价格函数（时间相关的正弦波）"""
        for p_id, p_conf in PRODUCTS.items():
            base_price, max_price = p_conf["val_range"]

            def create_price_func(base, top):
                return lambda t: base + (top - base) * (0.5 * (math.sin(2 * math.pi * t / 100) + 1))

            self.price_funcs[p_id] = create_price_func(base_price, max_price)

    def get_price(self, product_id: int, t: float) -> float:
        """获取产品在时刻 t 的价格"""
        if product_id not in self.price_funcs:
            return 0
        return max(0, self.price_funcs[product_id](t))


class ResourcePoint:
    """资源点：可被采集的资源"""

    def __init__(self, x: int, y: int, resource_id: int, 
                 initial_stock: int, max_stock: int, 
                 production_func: callable):
        self.pos = Point(x, y)
        self.resource_id = resource_id
        self.initial_stock = initial_stock
        self.max_stock = max_stock
        self.stock = initial_stock
        self.total_extracted = 0
        self.production_func = production_func  # f(t) -> production_rate
        self.last_update_time = 0
        self.is_depleted = False

    def update(self, current_time: float):
        """根据生产函数更新库存"""
        if self.is_depleted:
            return

        time_delta = current_time - self.last_update_time
        if time_delta > 0:
            production_rate = self.production_func(current_time)
            production = production_rate * time_delta

            self.stock = min(self.stock + production, self.max_stock)
            self.last_update_time = current_time

            if self.stock >= self.max_stock:
                self.is_depleted = True

    def harvest(self, amount: float) -> float:
        """采集资源"""
        extracted = min(amount, self.stock)
        self.stock -= extracted
        self.total_extracted += extracted
        return extracted


class Unit:
    """游戏单位（机器人）"""

    def __init__(self, u_id: int, x: int, y: int):
        self.id = u_id
        self.pos = Point(x, y)
        self.inventory: Dict[int, float] = {p_id: 0 for p_id in PRODUCTS}
        self.carrying_resource: Dict[int, float] = {r_id: 0 for r_id in range(NUM_RESOURCES)}
        self.busy_ticks = 0
        self.state = "idle"  # idle, moving, harvesting, trading
        self.last_transaction_cost = 0
        self.hp = UNIT_HP
        self.max_hp = UNIT_HP

    def move(self, direction: int) -> bool:
        """移动单位"""
        new_x, new_y = self.pos.x, self.pos.y

        if direction == U_ACT_MOVE_UP:
            new_x = max(0, new_x - 1)
        elif direction == U_ACT_MOVE_DOWN:
            new_x = min(MAP_HEIGHT - 1, new_x + 1)
        elif direction == U_ACT_MOVE_LEFT:
            new_y = max(0, new_y - 1)
        elif direction == U_ACT_MOVE_RIGHT:
            new_y = min(MAP_WIDTH - 1, new_y + 1)
        else:
            return False

        self.pos = Point(new_x, new_y)
        return True

    def get_total_load(self) -> float:
        """获取当前总负荷"""
        product_load = sum(self.inventory.values())
        resource_load = sum(self.carrying_resource.values())
        return product_load + resource_load


class Factory:
    """工厂：生产、存储、修复单位"""

    def __init__(self, x: int, y: int):
        self.pos = Point(x, y)
        self.storage: Dict[int, float] = {p_id: 0 for p_id in PRODUCTS}
        self.storage_capacity = FACTORY_STORAGE_CAP
        self.production_queues: Dict[int, List[int]] = {i: [] for i in range(FACTORY_LINES)}
        self.compute_power = INITIAL_COMPUTE
        self.max_compute = 100

    def add_to_storage(self, product_id: int, amount: float) -> float:
        """添加产品到存储"""
        available_space = self.storage_capacity - sum(self.storage.values())
        stored = min(amount, available_space)
        self.storage[product_id] += stored
        return stored

    def remove_from_storage(self, product_id: int, amount: float) -> float:
        """从存储移除产品"""
        removed = min(amount, self.storage[product_id])
        self.storage[product_id] -= removed
        return removed


class GameEnv:
    """核心游戏环境"""

    def __init__(self):
        self.time = 0.0
        self.money = INITIAL_MONEY
        self.units: List[Unit] = []
        self.markets: List[Market] = []
        self.resource_points: List[ResourcePoint] = []
        self.factory: Optional[Factory] = None
        self.map_grid = []
        self.transaction_history: List[Dict] = []
        self.reset()

    def reset(self):
        """重置游戏状态"""
        self.time = 0.0
        self.money = INITIAL_MONEY
        self.units = [Unit(0, 0, 0)]  # 起点在(0,0)
        self.markets = []
        self.resource_points = []
        self.factory = Factory(0, 0)
        self.transaction_history = []

        self.map_grid = [
            [GRID_TYPE_EMPTY for _ in range(MAP_WIDTH)]
            for _ in range(MAP_HEIGHT)
        ]

        self._init_map()
        return self._get_observation()

    def _init_map(self):
        """初始化地图"""
        # 生成3个市场
        market_positions = [(1, 1), (1, 3), (3, 2)]
        for i, (x, y) in enumerate(market_positions):
            if x < MAP_HEIGHT and y < MAP_WIDTH:
                self.map_grid[x][y] = GRID_TYPE_MARKET
                self.markets.append(Market(x, y, i, f"Market_{i}"))

        # 生成2个资源点
        resource_positions = [(2, 1), (3, 3)]
        for i, (x, y) in enumerate(resource_positions):
            if x < MAP_HEIGHT and y < MAP_WIDTH:
                self.map_grid[x][y] = GRID_TYPE_RESOURCE

                # 简单的生产函数示例
                def default_production(t):
                    return 10 * math.exp(-0.01 * t)  # 衰减模型

                resource_point = ResourcePoint(
                    x, y, i, 
                    initial_stock=50,
                    max_stock=200,
                    production_func=default_production
                )
                self.resource_points.append(resource_point)

    def step(self, command: int) -> Dict:
        """执行一步游戏"""
        self.time += TIME_STEP_DURATION

        # 更新资源点
        for rp in self.resource_points:
            rp.update(self.time)

        self._handle_command(command)
        return self._get_observation()

    def _handle_command(self, cmd: int):
        """处理单位命令"""
        u = self.units[0]

        if u.busy_ticks > 0:
            u.busy_ticks -= 1
            if u.busy_ticks == 0:
                u.state = "idle"
            return

        if cmd == U_ACT_WAIT:
            u.state = "idle"
        elif cmd >= U_ACT_MOVE_UP and cmd <= U_ACT_MOVE_RIGHT:
            u.move(cmd)
            u.state = "moving"
        elif cmd == U_ACT_LOAD_0:
            self._try_buy(u)
        elif cmd == U_ACT_SELL_ALL:
            self._try_sell_all(u)
        elif cmd == U_ACT_HARVEST:
            self._try_harvest(u)

    def _try_buy(self, u: Unit) -> bool:
        """尝试购买"""
        nearby_market = self._find_nearby_market(u)
        if not nearby_market:
            return False

        if u.get_total_load() >= UNIT_CAPACITY:
            return False

        product_id = PRODUCT_SEMICONDUCTOR
        cost = PRODUCTS[product_id]["cost"]

        if self.money < cost:
            return False

        self.money -= cost
        u.inventory[product_id] += 1
        u.last_transaction_cost = cost
        u.state = "busy"
        u.busy_ticks = int(PRODUCT_TRANSACTION_TIME / TIME_STEP_DURATION)

        return True

    def _try_sell_all(self, u: Unit) -> bool:
        """尝试卖出所有产品"""
        nearby_market = self._find_nearby_market(u)
        if not nearby_market:
            return False

        total_price = 0
        for product_id, count in u.inventory.items():
            if count > 0:
                price = nearby_market.get_price(product_id, self.time)
                total_price += price * count
                u.inventory[product_id] = 0

        if total_price > 0:
            self.money += total_price
            u.state = "busy"
            u.busy_ticks = int(PRODUCT_TRANSACTION_TIME / TIME_STEP_DURATION)

            profit = total_price - u.last_transaction_cost
            self.transaction_history.append({
                'time': self.time,
                'type': 'sell',
                'revenue': total_price,
                'profit': profit,
            })

            return True

        return False

    def _try_harvest(self, u: Unit) -> bool:
        """尝试采集资源"""
        nearby_resource = self._find_nearby_resource(u)
        if not nearby_resource:
            return False

        if u.get_total_load() >= UNIT_CAPACITY:
            return False

        # 采集资源
        amount_to_harvest = min(10, UNIT_CAPACITY - u.get_total_load())
        harvested = nearby_resource.harvest(amount_to_harvest)
        u.carrying_resource[nearby_resource.resource_id] += harvested

        u.state = "busy"
        u.busy_ticks = int(HARVESTING_TIME / TIME_STEP_DURATION)

        return harvested > 0

    def _find_nearby_market(self, u: Unit) -> Optional[Market]:
        """找到附近的市场"""
        for market in self.markets:
            if u.pos.distance_to(market.pos) <= 1:
                return market
        return None

    def _find_nearby_resource(self, u: Unit) -> Optional[ResourcePoint]:
        """找到附近的资源点"""
        for resource in self.resource_points:
            if u.pos.distance_to(resource.pos) <= 2:  # 3x3范围
                return resource
        return None

    def _get_observation(self) -> Dict:
        """获取当前游戏状态观察"""
        u = self.units[0]
        return {
            "time": self.time,
            "money": self.money,
            "unit_pos": (u.pos.x, u.pos.y),
            "unit_inventory": u.inventory.copy(),
            "unit_resources": u.carrying_resource.copy(),
            "unit_state": u.state,
            "unit_hp": u.hp,
            "markets": self.markets,
            "resources": self.resource_points,
            "factory_storage": self.factory.storage.copy(),
        }