"""Game-rule layer for the AI9 PVE environment.

RL code should interact with this module through GameEnv.reset(),
GameEnv.step(), GameEnv.get_public_observation(), GameEnv.get_valid_actions(),
and GameEnv.get_net_worth().
"""

from __future__ import annotations

import math
import random
from dataclasses import dataclass
from typing import Callable, Dict, List, Optional, Tuple

from config.setting import *


@dataclass(frozen=True)
class Point:
    x: int
    y: int

    def distance_to(self, other: "Point") -> int:
        return abs(self.x - other.x) + abs(self.y - other.y)

    def in_range(self, other: "Point", radius: int) -> bool:
        return self.distance_to(other) <= radius


class Market:
    """Market with per-instance price phase and period."""

    def __init__(
        self,
        x: int,
        y: int,
        market_id: int,
        name: str = "Market",
        phase: float = 0.0,
        period: float = 100.0,
        price_scale: float = 1.0,
        stock: float = MARKET_MAX_STOCK,
        demand: float = MARKET_MAX_DEMAND,
    ):
        self.pos = Point(x, y)
        self.id = market_id
        self.name = name
        self.phase = phase
        self.period = period
        self.price_scale = price_scale
        self.stock = stock
        self.max_stock = MARKET_MAX_STOCK
        self.demand = demand
        self.max_demand = MARKET_MAX_DEMAND

    def update(self) -> None:
        self.stock = min(self.max_stock, self.stock + MARKET_STOCK_REPLENISH_PER_STEP)
        self.demand = min(self.max_demand, self.demand + MARKET_DEMAND_REPLENISH_PER_STEP)

    def get_price(self, product_id: int, t: float) -> float:
        if product_id not in PRODUCTS:
            return 0.0

        base_price, max_price = PRODUCTS[product_id]["val_range"]
        wave = 0.5 * (math.sin(2 * math.pi * (t + self.phase) / self.period) + 1)
        return max(0.0, (base_price + (max_price - base_price) * wave) * self.price_scale)

    def get_buy_price(self, product_id: int, t: float) -> float:
        scarcity = 1.0 - min(1.0, self.stock / max(1.0, self.max_stock))
        return self.get_price(product_id, t) * (1 + MARKET_SPREAD_RATE) * (1 + MARKET_SCARCITY_PRICE_IMPACT * scarcity)

    def get_sell_price(self, product_id: int, t: float) -> float:
        demand_ratio = min(1.0, self.demand / max(1.0, self.max_demand))
        demand_multiplier = 1.0 - MARKET_LOW_DEMAND_DISCOUNT * (1.0 - demand_ratio)
        return self.get_price(product_id, t) * (1 - MARKET_SPREAD_RATE) * demand_multiplier


class ResourcePoint:
    def __init__(
        self,
        x: int,
        y: int,
        resource_id: int,
        initial_stock: float,
        max_stock: float,
        production_func: Callable[[float], float],
    ):
        self.pos = Point(x, y)
        self.resource_id = resource_id
        self.initial_stock = initial_stock
        self.max_stock = max_stock
        self.stock = initial_stock
        self.total_extracted = 0.0
        self.production_func = production_func
        self.last_update_time = 0.0

    def update(self, current_time: float) -> None:
        time_delta = current_time - self.last_update_time
        if time_delta <= 0:
            return

        production_rate = self.production_func(current_time)
        self.stock = min(self.stock + production_rate * time_delta, self.max_stock)
        self.last_update_time = current_time

    def harvest(self, amount: float) -> float:
        extracted = min(amount, self.stock)
        self.stock -= extracted
        self.total_extracted += extracted
        return extracted


class Unit:
    def __init__(self, u_id: int, x: int, y: int):
        self.id = u_id
        self.pos = Point(x, y)
        self.inventory: Dict[int, int] = {p_id: 0 for p_id in PRODUCTS}
        self.product_lots: Dict[int, List[Tuple[int, float]]] = {p_id: [] for p_id in PRODUCTS}
        self.carrying_resource: Dict[int, float] = {r_id: 0.0 for r_id in range(NUM_RESOURCES)}
        self.busy_ticks = 0
        self.state = "idle"
        self.last_transaction_cost = 0.0
        self.hp = UNIT_HP
        self.max_hp = UNIT_HP

    def get_total_load(self) -> float:
        return sum(self.inventory.values()) + sum(self.carrying_resource.values())


class Factory:
    def __init__(self, x: int, y: int):
        self.pos = Point(x, y)
        self.storage: Dict[int, float] = {p_id: 0.0 for p_id in PRODUCTS}
        self.storage_capacity = FACTORY_STORAGE_CAP
        self.production_queues: Dict[int, List[int]] = {i: [] for i in range(FACTORY_LINES)}
        self.compute_power = INITIAL_COMPUTE
        self.max_compute = 100


class GameEnv:
    """Core game simulator.

    The environment is intentionally deterministic inside one episode, but can
    randomize terrain and market regimes on reset(seed). This keeps the rule
    layer suitable for competition evaluation while allowing RL training to
    generalize across maps.
    """

    def __init__(self, randomize: bool = True):
        self.randomize = randomize
        self.rng = random.Random()
        self.time = 0.0
        self.money = INITIAL_MONEY
        self.units: List[Unit] = []
        self.markets: List[Market] = []
        self.resource_points: List[ResourcePoint] = []
        self.factory: Optional[Factory] = None
        self.map_grid: List[List[int]] = []
        self.transaction_history: List[Dict] = []
        self.last_step_result: Dict = {}
        self.reset()

    def reset(self, seed: Optional[int] = None) -> Dict:
        if seed is not None:
            self.rng.seed(seed)

        self.time = 0.0
        self.money = INITIAL_MONEY
        self.units = [Unit(0, 0, 0)]
        self.markets = []
        self.resource_points = []
        self.factory = Factory(0, 0)
        self.transaction_history = []
        self.last_step_result = self._empty_step_result(valid=True)
        self.map_grid = [[GRID_TYPE_EMPTY for _ in range(MAP_WIDTH)] for _ in range(MAP_HEIGHT)]
        self.map_grid[0][0] = GRID_TYPE_FACTORY

        if self.randomize:
            self._init_random_map()
        else:
            self._init_fixed_map()

        return self.get_public_observation()

    def step(self, command: int) -> Dict:
        money_before = self.money
        transaction_count_before = len(self.transaction_history)
        self.time += TIME_STEP_DURATION

        for resource in self.resource_points:
            resource.update(self.time)
        for market in self.markets:
            market.update()

        valid = self._handle_command(command)
        money_delta = self.money - money_before
        realized_profit = 0.0
        if len(self.transaction_history) > transaction_count_before:
            realized_profit = self.transaction_history[-1].get("profit", 0.0)

        self.last_step_result = {
            "valid": valid,
            "action": command,
            "money_delta": money_delta,
            "realized_profit": realized_profit,
            "net_worth": self.get_net_worth(),
            "transactions": len(self.transaction_history),
        }
        return self.get_public_observation()

    def get_public_observation(self) -> Dict:
        unit = self.units[0]
        valid_actions = self.get_valid_actions()
        markets = []

        for market in self.markets:
            origin_count = sum(1 for origin_id, _ in unit.product_lots[PRODUCT_SEMICONDUCTOR] if origin_id == market.id)
            markets.append(
                {
                    "id": market.id,
                    "name": market.name,
                    "pos": (market.pos.x, market.pos.y),
                    "price": market.get_price(PRODUCT_SEMICONDUCTOR, self.time),
                    "buy_price": market.get_buy_price(PRODUCT_SEMICONDUCTOR, self.time),
                    "sell_price": market.get_sell_price(PRODUCT_SEMICONDUCTOR, self.time),
                    "period": market.period,
                    "phase": market.phase,
                    "stock": market.stock,
                    "max_stock": market.max_stock,
                    "demand": market.demand,
                    "max_demand": market.max_demand,
                    "origin_inventory": origin_count,
                    "nearby": unit.pos.distance_to(market.pos) <= 1,
                }
            )

        resources = [
            {
                "id": resource.resource_id,
                "pos": (resource.pos.x, resource.pos.y),
                "stock": resource.stock,
                "max_stock": resource.max_stock,
                "nearby": unit.pos.distance_to(resource.pos) <= 2,
            }
            for resource in self.resource_points
        ]

        return {
            "time": self.time,
            "money": self.money,
            "net_worth": self.get_net_worth(),
            "map_width": MAP_WIDTH,
            "map_height": MAP_HEIGHT,
            "map_grid": [row[:] for row in self.map_grid],
            "unit": {
                "pos": (unit.pos.x, unit.pos.y),
                "busy_ticks": unit.busy_ticks,
                "state": unit.state,
                "inventory": unit.inventory.copy(),
                "resources": unit.carrying_resource.copy(),
                "total_load": unit.get_total_load(),
                "hp": unit.hp,
            },
            "markets": markets,
            "resources": resources,
            "valid_actions": valid_actions,
            "transactions": len(self.transaction_history),
            "last_step": self.last_step_result.copy(),
        }

    def get_valid_actions(self) -> List[bool]:
        unit = self.units[0]
        valid = [False] * (U_ACT_HARVEST + 1)
        valid[U_ACT_WAIT] = True

        if unit.busy_ticks > 0:
            return valid

        for action in (U_ACT_MOVE_UP, U_ACT_MOVE_DOWN, U_ACT_MOVE_LEFT, U_ACT_MOVE_RIGHT):
            valid[action] = self._can_move(unit, action)

        market = self._find_nearby_market(unit)
        if market is not None:
            product_id = PRODUCT_SEMICONDUCTOR
            valid[U_ACT_LOAD_0] = (
                unit.get_total_load() < UNIT_CAPACITY
                and market.stock >= 1.0
                and self.money >= market.get_buy_price(product_id, self.time)
            )
            valid[U_ACT_SELL_ALL] = market.demand >= 1.0 and self._has_sellable_lots(unit, market)

        resource = self._find_nearby_resource(unit)
        valid[U_ACT_HARVEST] = resource is not None and unit.get_total_load() < UNIT_CAPACITY and resource.stock > 0
        return valid

    def get_net_worth(self) -> float:
        unit = self.units[0] if self.units else None
        if unit is None:
            return self.money

        product_value = 0.0
        for product_id, lots in unit.product_lots.items():
            for origin_id, _cost in lots:
                prices = [
                    market.get_sell_price(product_id, self.time)
                    for market in self.markets
                    if not BLOCK_SAME_MARKET_RESALE or market.id != origin_id
                ]
                if prices:
                    product_value += max(prices)

        resource_value = 0.25 * sum(unit.carrying_resource.values())
        return self.money + product_value + resource_value

    def _init_fixed_map(self) -> None:
        market_specs = [(1, 1, 0.0), (1, 3, 34.0), (3, 2, 67.0)]
        for i, (x, y, phase) in enumerate(market_specs):
            self._add_market(i, x, y, phase=phase, period=100.0, price_scale=1.0)

        for i, (x, y) in enumerate([(2, 1), (3, 3)]):
            self._add_resource(i, x, y)

    def _init_random_map(self) -> None:
        cells = [(x, y) for x in range(MAP_HEIGHT) for y in range(MAP_WIDTH) if (x, y) != (0, 0)]
        self.rng.shuffle(cells)

        market_cells = cells[:MARKET_COUNT]
        resource_cells = cells[MARKET_COUNT : MARKET_COUNT + RESOURCE_COUNT]
        obstacle_cells = cells[MARKET_COUNT + RESOURCE_COUNT : MARKET_COUNT + RESOURCE_COUNT + OBSTACLE_COUNT]

        for i, (x, y) in enumerate(market_cells):
            phase = self.rng.uniform(0.0, 100.0)
            period = self.rng.uniform(MARKET_PERIOD_MIN, MARKET_PERIOD_MAX)
            price_scale = self.rng.uniform(MARKET_PRICE_SCALE_MIN, MARKET_PRICE_SCALE_MAX)
            stock = self.rng.uniform(MARKET_INITIAL_STOCK_MIN, MARKET_INITIAL_STOCK_MAX)
            demand = self.rng.uniform(MARKET_INITIAL_DEMAND_MIN, MARKET_INITIAL_DEMAND_MAX)
            self._add_market(i, x, y, phase=phase, period=period, price_scale=price_scale, stock=stock, demand=demand)

        for i, (x, y) in enumerate(resource_cells):
            self._add_resource(i, x, y)

        for x, y in obstacle_cells:
            if self._keeps_map_reasonable(x, y):
                self.map_grid[x][y] = GRID_TYPE_OBSTACLE

    def _add_market(
        self,
        market_id: int,
        x: int,
        y: int,
        phase: float,
        period: float,
        price_scale: float,
        stock: float = MARKET_MAX_STOCK,
        demand: float = MARKET_MAX_DEMAND,
    ) -> None:
        self.map_grid[x][y] = GRID_TYPE_MARKET
        self.markets.append(Market(x, y, market_id, f"Market_{market_id}", phase, period, price_scale, stock, demand))

    def _add_resource(self, resource_id: int, x: int, y: int) -> None:
        self.map_grid[x][y] = GRID_TYPE_RESOURCE

        def production(t: float) -> float:
            return 5.0 + 5.0 * math.exp(-0.01 * t)

        self.resource_points.append(ResourcePoint(x, y, resource_id, 50.0, 200.0, production))

    def _handle_command(self, cmd: int) -> bool:
        unit = self.units[0]

        if unit.busy_ticks > 0:
            unit.busy_ticks -= 1
            if unit.busy_ticks == 0:
                unit.state = "idle"
            return cmd == U_ACT_WAIT

        if cmd == U_ACT_WAIT:
            unit.state = "idle"
            return True
        if cmd in (U_ACT_MOVE_UP, U_ACT_MOVE_DOWN, U_ACT_MOVE_LEFT, U_ACT_MOVE_RIGHT):
            return self._move(unit, cmd)
        if cmd == U_ACT_LOAD_0:
            return self._try_buy(unit)
        if cmd == U_ACT_SELL_ALL:
            return self._try_sell_all(unit)
        if cmd == U_ACT_HARVEST:
            return self._try_harvest(unit)
        return False

    def _move(self, unit: Unit, direction: int) -> bool:
        if not self._can_move(unit, direction):
            return False

        dx, dy = self._direction_delta(direction)
        unit.pos = Point(unit.pos.x + dx, unit.pos.y + dy)
        unit.state = "moving"
        return True

    def _can_move(self, unit: Unit, direction: int) -> bool:
        dx, dy = self._direction_delta(direction)
        x, y = unit.pos.x + dx, unit.pos.y + dy
        return 0 <= x < MAP_HEIGHT and 0 <= y < MAP_WIDTH and self.map_grid[x][y] != GRID_TYPE_OBSTACLE

    def _direction_delta(self, direction: int) -> Tuple[int, int]:
        if direction == U_ACT_MOVE_UP:
            return -1, 0
        if direction == U_ACT_MOVE_DOWN:
            return 1, 0
        if direction == U_ACT_MOVE_LEFT:
            return 0, -1
        if direction == U_ACT_MOVE_RIGHT:
            return 0, 1
        return 0, 0

    def _try_buy(self, unit: Unit) -> bool:
        market = self._find_nearby_market(unit)
        if market is None or unit.get_total_load() >= UNIT_CAPACITY or market.stock < 1.0:
            return False

        product_id = PRODUCT_SEMICONDUCTOR
        cost = market.get_buy_price(product_id, self.time)
        if self.money < cost:
            return False

        self.money -= cost
        market.stock -= 1.0
        unit.inventory[product_id] += 1
        unit.product_lots[product_id].append((market.id, cost))
        unit.last_transaction_cost = cost
        unit.state = "busy"
        unit.busy_ticks = int(PRODUCT_TRANSACTION_TIME / TIME_STEP_DURATION)
        return True

    def _try_sell_all(self, unit: Unit) -> bool:
        market = self._find_nearby_market(unit)
        if market is None:
            return False

        total_price = 0.0
        total_cost = 0.0
        sold_counts: Dict[int, int] = {}

        for product_id, lots in unit.product_lots.items():
            kept_lots = []
            sold_lots = []
            remaining_demand = int(market.demand)
            for origin_id, cost in lots:
                if BLOCK_SAME_MARKET_RESALE and origin_id == market.id:
                    kept_lots.append((origin_id, cost))
                elif remaining_demand <= 0:
                    kept_lots.append((origin_id, cost))
                else:
                    sold_lots.append((origin_id, cost))
                    remaining_demand -= 1

            if sold_lots:
                price = market.get_sell_price(product_id, self.time)
                total_price += price * len(sold_lots)
                total_cost += sum(cost for _origin_id, cost in sold_lots)
                sold_counts[product_id] = len(sold_lots)
                market.demand -= len(sold_lots)

            unit.product_lots[product_id] = kept_lots
            unit.inventory[product_id] = len(kept_lots)

        if total_price <= 0:
            return False

        self.money += total_price
        unit.state = "busy"
        unit.busy_ticks = int(PRODUCT_TRANSACTION_TIME / TIME_STEP_DURATION)
        self.transaction_history.append(
            {
                "time": self.time,
                "type": "sell",
                "market_id": market.id,
                "revenue": total_price,
                "profit": total_price - total_cost,
                "sold_counts": sold_counts,
            }
        )
        return True

    def _try_harvest(self, unit: Unit) -> bool:
        resource = self._find_nearby_resource(unit)
        if resource is None or unit.get_total_load() >= UNIT_CAPACITY:
            return False

        amount = min(10.0, UNIT_CAPACITY - unit.get_total_load())
        harvested = resource.harvest(amount)
        if harvested <= 0:
            return False

        unit.carrying_resource[resource.resource_id] += harvested
        unit.state = "busy"
        unit.busy_ticks = int(HARVESTING_TIME / TIME_STEP_DURATION)
        return True

    def _find_nearby_market(self, unit: Unit) -> Optional[Market]:
        return next((market for market in self.markets if unit.pos.distance_to(market.pos) <= 1), None)

    def _find_nearby_resource(self, unit: Unit) -> Optional[ResourcePoint]:
        return next((resource for resource in self.resource_points if unit.pos.distance_to(resource.pos) <= 2), None)

    def _has_sellable_lots(self, unit: Unit, market: Market) -> bool:
        for lots in unit.product_lots.values():
            for origin_id, _cost in lots:
                if not BLOCK_SAME_MARKET_RESALE or origin_id != market.id:
                    return True
        return False

    def _keeps_map_reasonable(self, x: int, y: int) -> bool:
        if (x, y) in ((0, 0), (0, 1), (1, 0)):
            return False
        return True

    def _empty_step_result(self, valid: bool) -> Dict:
        return {
            "valid": valid,
            "action": U_ACT_WAIT,
            "money_delta": 0.0,
            "realized_profit": 0.0,
            "net_worth": self.money,
            "transactions": 0,
        }
