"""
GameEnvironment: the main Gymnasium-compatible environment.

Observation (32 floats, all in [-1, 1] or [0, 1]):
  [0-1]   unit position (x/H, y/W)
  [2]     unit HP ratio
  [3]     unit raw inventory / capacity
  [4]     unit product inventory / capacity
  [5]     unit busy ticks / 10
  [6]     log10(money+1) / 5
  [7]     compute / 100
  [8]     time / max_time
  [9]     sin(2π t / period)
  [10]    cos(2π t / period)
  [11]    factory raw stock / storage_cap
  [12]    factory product stock / storage_cap
  [13]    factory production queue length / 10
  [14-15] resource 0 (dx/H, dy/W)
  [16]    resource 0 stock ratio
  [17-18] resource 1 (dx/H, dy/W)
  [19]    resource 1 stock ratio
  [20-21] compute center 0 (dx/H, dy/W)
  [22]    compute center 0 is_open
  [23-24] compute center 1 (dx/H, dy/W)
  [25]    compute center 1 is_open
  [26-27] market 0 (dx/H, dy/W)
  [28]    market 0 best price (normalized 0-1)
  [29-30] market 1 (dx/H, dy/W)
  [31]    market 1 best price (normalized 0-1)

Action space: Discrete(8)  → see action_space.py
"""
from __future__ import annotations

import math
import random
from typing import Any, Dict, List, Optional, Tuple

import numpy as np
import gymnasium as gym
from gymnasium import spaces

from .config import GameConfig, PRODUCT_DEFS, CELL_OBSTACLE
from .board import Board, ResourcePoint
from .character import Unit
from .market import Market, build_markets
from .action_space import Action, N_ACTIONS, MOVE_DELTAS, compute_action_mask
from .reward_calculator import RewardCalculator, RewardConfig

# Price normalisation constants (use semiconductor range as reference)
_PRICE_MIN, _PRICE_MAX = 4.0, 120.0   # global min/max across all products
_PRICE_RANGE = _PRICE_MAX - _PRICE_MIN


class Factory:
    """Manages factory storage, production queue, and computing."""

    def __init__(self, cfg: GameConfig):
        self.storage_cap = cfg.factory_storage_cap
        self.raw_stock: float = 0.0        # harvested raw resources
        self.products: Dict[int, float] = {}  # pid → qty in warehouse
        self.production_lines: int = cfg.initial_production_lines

        # Production queue: list of (product_id, remaining_ticks)
        self._queue: List[Tuple[int, float]] = []

        # Tech multipliers (modified by tech upgrades)
        self.time_multiplier: float = 1.0
        self.price_multiplier: float = 1.0
        self.cost_delta: int = 0           # flat reduction on product cost

    @property
    def total_product_stock(self) -> float:
        return sum(self.products.values())

    @property
    def queue_len(self) -> int:
        return len(self._queue)

    def enqueue(self, pid: int, time_step: float) -> bool:
        """Add one product to the production queue if capacity allows."""
        pdef = PRODUCT_DEFS[pid]
        if len(self._queue) >= self.production_lines * 5:
            return False   # queue full
        produce_time = pdef["produce_time"] * self.time_multiplier
        remaining_ticks = produce_time / time_step
        self._queue.append((pid, remaining_ticks))
        return True

    def tick(self, n_active_lines: int = None):
        """Advance active production lines by 1 tick."""
        if n_active_lines is None:
            n_active_lines = self.production_lines

        completed = []
        active = self._queue[:n_active_lines]
        rest   = self._queue[n_active_lines:]

        new_active = []
        for pid, remaining in active:
            remaining -= 1
            if remaining <= 0:
                completed.append(pid)
            else:
                new_active.append((pid, remaining))

        self._queue = new_active + rest

        for pid in completed:
            if self.total_product_stock < self.storage_cap:
                self.products[pid] = self.products.get(pid, 0.0) + 1.0

    def deposit_raw(self, amount: float) -> float:
        """Deposit raw material into factory; return amount actually stored."""
        space = self.storage_cap - self.raw_stock - self.total_product_stock
        stored = min(space, amount)
        self.raw_stock += stored
        return stored

    def load_products(self, unit: Unit) -> float:
        """Transfer products from warehouse to unit; return qty transferred."""
        can_take = unit.free_capacity
        total_loaded = 0.0
        for pid in list(self.products.keys()):
            if can_take <= 0:
                break
            qty = min(self.products[pid], can_take)
            unit.add_product(pid, qty)
            self.products[pid] -= qty
            can_take -= qty
            total_loaded += qty
        return total_loaded


class GameEnvironment(gym.Env):
    """
    Single-agent PvE environment following the THUAI9 PvE rules.
    Implements gymnasium.Env interface.
    """

    metadata = {"render_modes": ["ansi"]}

    OBS_DIM = 32

    def __init__(
        self,
        cfg: Optional[GameConfig] = None,
        reward_cfg: Optional[RewardConfig] = None,
        seed: Optional[int] = None,
    ):
        super().__init__()
        self.cfg = cfg or GameConfig()
        self._reward_calc = RewardCalculator(reward_cfg)
        self._base_seed = seed

        self.observation_space = spaces.Box(
            low=-1.0, high=2.0, shape=(self.OBS_DIM,), dtype=np.float32
        )
        self.action_space = spaces.Discrete(N_ACTIONS)

        # Will be populated in reset()
        self.board: Board = None
        self.unit: Unit = None
        self.factory: Factory = None
        self.markets: List[Market] = []
        self.money: float = 0.0
        self.compute: float = 0.0
        self.score: float = 0.0
        self.time: float = 0.0
        self._step: int = 0
        self._techs_owned: set = set()

    # ── Gym interface ──────────────────────────────────────────────────────────

    def reset(self, seed: Optional[int] = None, options: Optional[dict] = None):
        super().reset(seed=seed)
        rng_seed = seed if seed is not None else self._base_seed
        self._rng = random.Random(rng_seed)

        cfg = self.cfg
        self.board = Board(cfg, seed=rng_seed)
        self.unit = Unit(uid=0, x=cfg.factory_x, y=cfg.factory_y,
                         max_hp=cfg.unit_hp, capacity=cfg.unit_capacity)
        self.factory = Factory(cfg)
        self.markets = build_markets(self.board.market_positions, cfg)

        self.money = cfg.initial_money
        self.compute = cfg.initial_compute
        self.score = 0.0
        self.time = 0.0
        self._step = 0
        self._techs_owned = set()

        self._reward_calc.reset(self)
        obs = self._encode_obs()
        return obs, {}

    def step(self, action: int) -> Tuple[np.ndarray, float, bool, bool, dict]:
        cfg = self.cfg
        dt = cfg.time_step

        harvested = 0.0
        action_valid = True

        # ── 1. Tick busy counter ───────────────────────────────────────────
        if self.unit.busy_ticks > 0:
            self.unit.busy_ticks -= 1
            if self.unit.busy_ticks == 0:
                self._complete_busy_action()
        else:
            # ── 2. Process action ──────────────────────────────────────────
            action_valid, harvested = self._execute_action(Action(action))

        # ── 3. Advance world time ──────────────────────────────────────────
        self.time += dt
        self._step += 1

        # ── 4. Passive: regen resources, tick production, accrue compute ──
        for rp in self.board.resource_points:
            rp.regen(dt, self.time)
        self.factory.tick()
        self._accrue_compute(dt)

        # ── 5. Compute reward ──────────────────────────────────────────────
        reward = self._reward_calc.compute(self, action_valid, harvested)

        # ── 6. Termination / truncation ────────────────────────────────────
        terminated = self.money < 0
        truncated  = self._step >= cfg.max_steps

        obs = self._encode_obs()
        info = {
            "step": self._step,
            "time": self.time,
            "money": self.money,
            "score": self.score,
            "compute": self.compute,
            "action_valid": action_valid,
        }
        return obs, reward, terminated, truncated, info

    # ── Action execution ───────────────────────────────────────────────────────

    def _execute_action(self, action: Action) -> Tuple[bool, float]:
        """Execute action, return (was_valid, harvested_amount)."""
        u = self.unit
        board = self.board
        cfg = self.cfg
        harvested = 0.0

        if action == Action.WAIT:
            u.state = "idle"
            return True, 0.0

        if action in MOVE_DELTAS:
            dx, dy = MOVE_DELTAS[action]
            nx, ny = u.x + dx, u.y + dy
            if board.is_passable(nx, ny):
                u.x, u.y = nx, ny
                u.state = "moving"
                return True, 0.0
            return False, 0.0

        if action == Action.BUY:
            mkt_pos = board.nearest_market(u.x, u.y)
            if mkt_pos is None:
                return False, 0.0
            mkt = self._market_at(*mkt_pos)
            if mkt is None or u.free_capacity < 1:
                return False, 0.0
            # Buy the product with highest profit margin (price - cost) we can afford
            best_pid, best_cost = self._best_buyable(mkt)
            if best_pid is None:
                return False, 0.0
            cost = best_cost
            self.money -= cost
            u.add_product(best_pid, 1.0)
            u.state = "loading"
            u.busy_ticks = max(1, int(0.25 / cfg.time_step))
            u.busy_action = "buy_done"
            return True, 0.0

        if action == Action.SELL:
            mkt_pos = board.nearest_market(u.x, u.y)
            if mkt_pos is None or u.total_goods <= 0:
                return False, 0.0
            mkt = self._market_at(*mkt_pos)
            if mkt is None:
                return False, 0.0
            revenue = 0.0
            mult = self._price_multiplier()
            for pid, qty in list(u.prod_inv.items()):
                if qty > 0:
                    price = mkt.get_price(pid, self.time, mult) * qty
                    revenue += price
                    u.prod_inv[pid] = 0.0
            self.money += revenue
            self.score += revenue * cfg.score_factor
            u.state = "selling"
            u.busy_ticks = max(1, int(0.25 / cfg.time_step))
            u.busy_action = "sell_done"
            return True, 0.0

        if action == Action.HARVEST:
            rp = board.nearest_resource(u.x, u.y)
            if rp is None or u.free_capacity < 1:
                return False, 0.0
            harvest_per_tick = cfg.unit_harvest_rate * cfg.time_step
            amount = min(harvest_per_tick, u.free_capacity)
            actual = rp.harvest(amount)
            if actual <= 0:
                return False, 0.0
            # Deposit immediately to factory if at factory, else carry
            if board.at_factory(u.x, u.y):
                self.factory.deposit_raw(actual)
            else:
                u.raw_inv += actual
            harvested = actual
            u.state = "harvesting"
            return True, harvested

        return False, 0.0

    def _complete_busy_action(self):
        pass   # busy_ticks already captured side-effects at action time

    # ── Helpers ────────────────────────────────────────────────────────────────

    def _market_at(self, x: int, y: int) -> Optional[Market]:
        for m in self.markets:
            if m.x == x and m.y == y:
                return m
        return None

    def _best_buyable(self, mkt: Market) -> Tuple[Optional[int], float]:
        """Return (pid, cost) of product with highest profit (price - cost) we can afford."""
        best_pid, best_cost = None, None
        best_profit = -float("inf")
        mult = self._price_multiplier()
        for pid, pdef in PRODUCT_DEFS.items():
            cost = max(0, pdef["cost"] + self.factory.cost_delta)
            if self.money < cost:
                continue
            price = mkt.get_price(pid, self.time, mult)
            profit = price - cost
            if profit > best_profit:
                best_profit, best_cost, best_pid = profit, cost, pid
        return best_pid, best_cost

    def _price_multiplier(self) -> float:
        return self.factory.price_multiplier

    def _accrue_compute(self, dt: float):
        bonus = 0.0
        if "compute_expansion" in self._techs_owned:
            bonus = 0.3
        for cc in self.board.compute_centers:
            if cc.is_open:
                self.compute += (self.cfg.base_compute_rate + bonus) * dt

    # ── Observation encoding ───────────────────────────────────────────────────

    def _encode_obs(self) -> np.ndarray:
        cfg = self.cfg
        u = self.unit
        f = self.factory
        obs = np.zeros(self.OBS_DIM, dtype=np.float32)

        # Unit (0-5)
        obs[0] = u.x / max(1, cfg.map_height)
        obs[1] = u.y / max(1, cfg.map_width)
        obs[2] = u.hp / max(1, u.max_hp)
        obs[3] = u.raw_inv / max(1, u.capacity)
        obs[4] = sum(u.prod_inv.values()) / max(1, u.capacity)
        obs[5] = min(u.busy_ticks / 10.0, 1.0)

        # Economy (6-7)
        obs[6] = math.log10(max(1, self.money + 1)) / 5.0
        obs[7] = min(self.compute / 100.0, 2.0)

        # Time (8-10)
        obs[8]  = self.time / max(1, cfg.max_game_time)
        obs[9]  = math.sin(2 * math.pi * self.time / cfg.market_period)
        obs[10] = math.cos(2 * math.pi * self.time / cfg.market_period)

        # Factory (11-13)
        obs[11] = f.raw_stock / max(1, f.storage_cap)
        obs[12] = f.total_product_stock / max(1, f.storage_cap)
        obs[13] = min(f.queue_len / 10.0, 1.0)

        # Resource points (14-19)
        for i in range(2):
            base = 14 + i * 3
            if i < len(self.board.resource_points):
                rp = self.board.resource_points[i]
                obs[base]   = (rp.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (rp.y - u.y) / max(1, cfg.map_width)
                obs[base+2] = rp.stock / max(1, rp.max_stock)
            # else stays 0

        # Compute centers (20-25)
        for i in range(2):
            base = 20 + i * 3
            if i < len(self.board.compute_centers):
                cc = self.board.compute_centers[i]
                obs[base]   = (cc.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (cc.y - u.y) / max(1, cfg.map_width)
                obs[base+2] = float(cc.is_open)

        # Markets (26-31): first 2 markets only
        for i in range(2):
            base = 26 + i * 3
            if i < len(self.markets):
                m = self.markets[i]
                obs[base]   = (m.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (m.y - u.y) / max(1, cfg.map_width)
                _, best_price = m.best_product_to_sell(self.time)
                obs[base+2] = (best_price - _PRICE_MIN) / max(1, _PRICE_RANGE)

        return obs

    # ── Action mask (for mask-PPO) ──────────────────────────────────────────
    def action_masks(self) -> np.ndarray:
        return compute_action_mask(self)

    # ── Render ────────────────────────────────────────────────────────────────
    def render(self) -> str:
        lines = [
            f"t={self.time:.1f}s  step={self._step}  "
            f"money={self.money:.1f}  score={self.score:.0f}  compute={self.compute:.1f}",
            f"unit=({self.unit.x},{self.unit.y})  "
            f"inv(raw={self.unit.raw_inv:.0f}, prod={sum(self.unit.prod_inv.values()):.0f})",
        ]
        return "\n".join(lines)
