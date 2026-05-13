"""
GameEnvironment: the main Gymnasium-compatible environment.

Observation (44 floats, all in [-1, 1] or [0, 1]):
  [0-1]   unit position (x/H, y/W)
  [2]     unit HP ratio
  [3]     unit raw inventory / capacity
  [4-8]   unit product inventory by type (pid 0-4) / capacity
  [9]     unit busy ticks / 10
  [10]    log10(money+1) / 5
  [11]    compute / 100
  [12]    time / max_time
  [13]    sin(2π t / period)
  [14]    cos(2π t / period)
  [15]    factory raw stock / storage_cap
  [16]    factory product stock / storage_cap
  [17]    factory production queue length / 10
  [18-19] resource 0 (dx/H, dy/W)
  [20]    resource 0 stock ratio
  [21-22] resource 1 (dx/H, dy/W)
  [23]    resource 1 stock ratio
  [24-25] compute center 0 (dx/H, dy/W)
  [26]    compute center 0 is_open
  [27-28] compute center 1 (dx/H, dy/W)
  [29]    compute center 1 is_open
  [30-31] market 0 (dx/H, dy/W)
  [32-36] market 0 prices for all 5 products (normalized by each product's val_range)
  [37-38] market 1 (dx/H, dy/W)
  [39-43] market 1 prices for all 5 products (normalized by each product's val_range)

Action space: Discrete(12)  → see action_space.py
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
from .action_space import Action, N_ACTIONS, MOVE_DELTAS, SELL_ACTIONS, compute_action_mask
from .reward_calculator import RewardCalculator, RewardConfig

# Per-product price normalisation: (lo, range) keyed by product id
_PRICE_NORM = {
    pid: (pdef["val_range"][0], max(1.0, pdef["val_range"][1] - pdef["val_range"][0]))
    for pid, pdef in PRODUCT_DEFS.items()
}


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

    OBS_DIM = 44

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
            # Buy the currently cheapest product that fits budget
            best_pid, best_cost = self._cheapest_buyable(mkt)
            if best_pid is None:
                return False, 0.0
            cost = best_cost
            self.money -= cost
            u.add_product(best_pid, 1.0)
            u.state = "loading"
            u.busy_ticks = max(1, int(0.25 / cfg.time_step))
            u.busy_action = "buy_done"
            return True, 0.0

        if action in SELL_ACTIONS:
            pid = int(action) - int(Action.SELL_0)
            mkt_pos = board.nearest_market(u.x, u.y)
            if mkt_pos is None:
                return False, 0.0
            mkt = self._market_at(*mkt_pos)
            if mkt is None:
                return False, 0.0
            qty = u.prod_inv.get(pid, 0.0)
            if qty <= 0:
                return False, 0.0
            mult = self._price_multiplier()
            revenue = mkt.get_price(pid, self.time, mult) * qty
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

    def _cheapest_buyable(self, mkt: Market) -> Tuple[Optional[int], float]:
        """Return (pid, cost) of cheapest product we can afford with space."""
        best_pid, best_cost = None, float("inf")
        for pid, pdef in PRODUCT_DEFS.items():
            cost = max(0, pdef["cost"] + self.factory.cost_delta)
            if self.money >= cost and cost < best_cost:
                best_cost, best_pid = cost, pid
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

        # Unit position + HP (0-2)
        obs[0] = u.x / max(1, cfg.map_height)
        obs[1] = u.y / max(1, cfg.map_width)
        obs[2] = u.hp / max(1, u.max_hp)

        # Unit inventory: raw + per-product (3-8)
        obs[3] = u.raw_inv / max(1, u.capacity)
        cap = max(1, u.capacity)
        for pid in range(5):
            obs[4 + pid] = u.prod_inv.get(pid, 0.0) / cap

        # Busy (9)
        obs[9] = min(u.busy_ticks / 10.0, 1.0)

        # Economy (10-11)
        obs[10] = math.log10(max(1, self.money + 1)) / 5.0
        obs[11] = min(self.compute / 100.0, 2.0)

        # Time (12-14)
        obs[12] = self.time / max(1, cfg.max_game_time)
        obs[13] = math.sin(2 * math.pi * self.time / cfg.market_period)
        obs[14] = math.cos(2 * math.pi * self.time / cfg.market_period)

        # Factory (15-17)
        obs[15] = f.raw_stock / max(1, f.storage_cap)
        obs[16] = f.total_product_stock / max(1, f.storage_cap)
        obs[17] = min(f.queue_len / 10.0, 1.0)

        # Resource points (18-23)
        for i in range(2):
            base = 18 + i * 3
            if i < len(self.board.resource_points):
                rp = self.board.resource_points[i]
                obs[base]   = (rp.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (rp.y - u.y) / max(1, cfg.map_width)
                obs[base+2] = rp.stock / max(1, rp.max_stock)

        # Compute centers (24-29)
        for i in range(2):
            base = 24 + i * 3
            if i < len(self.board.compute_centers):
                cc = self.board.compute_centers[i]
                obs[base]   = (cc.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (cc.y - u.y) / max(1, cfg.map_width)
                obs[base+2] = float(cc.is_open)

        # Markets (30-43): 2 markets × (2 pos + 5 prices) = 14 floats
        for i in range(2):
            base = 30 + i * 7
            if i < len(self.markets):
                m = self.markets[i]
                obs[base]   = (m.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (m.y - u.y) / max(1, cfg.map_width)
                mult = self._price_multiplier()
                for pid in range(5):
                    lo, rng = _PRICE_NORM[pid]
                    obs[base + 2 + pid] = (m.get_price(pid, self.time, mult) - lo) / rng

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
