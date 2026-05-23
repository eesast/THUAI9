"""
GameEnvironment: the main Gymnasium-compatible environment.

Observation (82 floats, normalized to bounded ranges; some features may reach 2.0):
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
  [16-20] factory product stock by type (pid 0-4) / storage_cap
  [21]    factory production queue length / 10
  [22-33] resource points 0-3: 4 × (dx/H, dy/W, stock_ratio); unused slots = 0
  [34-45] compute centers 0-2: 3 × (dx/H, dy/W, is_open, progress); unused = 0
  [46-73] markets 0-3: 4 × (dx/H, dy/W, price×5); markets 2-3 prices only revealed
          when market_analysis tech is owned (positions always visible); unused = 0
  [74-81] techs owned: one-hot for each of the 8 tech slots (0/1)

Action space: Discrete(28)  → see action_space.py
"""
from __future__ import annotations

import math
import random
from typing import Any, Dict, List, Optional, Tuple

import numpy as np
import gymnasium as gym
from gymnasium import spaces

from .config import GameConfig, PRODUCT_DEFS, TECH_TREE
from .board import Board, ResourcePoint
from .character import Unit
from .market import Market, build_markets
from .action_space import (
    Action, N_ACTIONS, MOVE_DELTAS,
    SELL_ACTIONS, PRODUCE_ACTIONS, TECH_ACTIONS, TECH_KEYS,
    compute_action_mask,
)
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
        """Add one product to the production queue, consuming raw_stock. Returns False if can't."""
        pdef = PRODUCT_DEFS[pid]
        raw_needed = pdef["raw_cost"]
        if self.raw_stock < raw_needed:
            return False
        if len(self._queue) >= self.production_lines * 5:
            return False
        self.raw_stock -= raw_needed
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

    OBS_DIM = 82

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
        self._market_by_pos: Dict[Tuple[int, int], Market] = {}
        self.money: float = 0.0
        self.compute: float = 0.0
        self.score: float = 0.0
        self.time: float = 0.0
        self._step: int = 0
        self._techs_owned: set = set()

    def set_random_map(self, enabled: bool = True) -> None:
        self.cfg.random_map = enabled

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
        self.markets = build_markets(self.board.market_positions, cfg, env_seed=rng_seed)
        self._market_by_pos = {(m.x, m.y): m for m in self.markets}

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
        for mkt in self.markets:
            mkt.tick(dt)
        self._accrue_compute(dt)

        # ── 5. Termination / truncation (evaluated before reward so terminal bonus fires)
        terminated = self.money < 0
        truncated  = self._step >= cfg.max_steps

        # ── 6. Compute reward ──────────────────────────────────────────────
        reward = self._reward_calc.compute(
            self, action_valid, harvested, terminated=(terminated or truncated)
        )

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
                # Default: 1 busy tick per move; path_optimization removes it
                if "path_optimization" not in self._techs_owned:
                    u.busy_ticks = 1
                return True, 0.0
            return False, 0.0

        if action == Action.BUY:
            mkt_pos = board.nearest_market(u.x, u.y)
            if mkt_pos is None:
                return False, 0.0
            mkt = self._market_at(*mkt_pos)
            if mkt is None or u.free_capacity < 1:
                return False, 0.0
            # Buy at market price; select product with most upside for cross-market resale
            best_pid, best_cost = self._best_buyable(mkt)
            if best_pid is None:
                return False, 0.0
            self.money -= best_cost
            u.add_product(best_pid, 1.0, origin_market=mkt.id)
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
            # Strict same-location arbitrage prevention: block selling at the market
            # where this product was purchased (requires cross-market movement to profit)
            blocked = u.origin_qty(pid, mkt.id)
            sell_qty = qty - blocked
            if sell_qty <= 0:
                return False, 0.0
            mult = self._price_multiplier()
            revenue = mkt.get_price(pid, mult) * sell_qty
            if blocked > 0:
                u.prod_inv[pid] = blocked
                u.prod_origin[pid] = {mkt.id: blocked}
            else:
                u.prod_inv.pop(pid, None)
                u.prod_origin.pop(pid, None)
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
            if board.at_factory(u.x, u.y):
                self.factory.deposit_raw(actual)
            else:
                u.raw_inv += actual
            harvested = actual
            u.state = "harvesting"
            return True, harvested

        if action == Action.DEPOSIT:
            if not board.at_factory(u.x, u.y) or u.raw_inv <= 0:
                return False, 0.0
            stored = self.factory.deposit_raw(u.raw_inv)
            u.raw_inv -= stored
            u.state = "depositing"
            return True, 0.0

        if action in PRODUCE_ACTIONS:
            pid = int(action) - int(Action.PRODUCE_0)
            if not board.at_factory(u.x, u.y):
                return False, 0.0
            if not self.factory.enqueue(pid, cfg.time_step):
                return False, 0.0
            u.state = "producing"
            return True, 0.0

        if action == Action.LOAD:
            if not board.at_factory(u.x, u.y):
                return False, 0.0
            loaded = self.factory.load_products(u)
            if loaded <= 0:
                return False, 0.0
            u.state = "loading"
            u.busy_ticks = max(1, int(0.25 / cfg.time_step))
            u.busy_action = "load_done"
            return True, 0.0

        if action == Action.OCCUPY:
            cc = board.nearest_compute_center(u.x, u.y)
            if cc is None or cc.is_open:
                return False, 0.0
            cc.occupy_progress += cfg.time_step
            if cc.occupy_progress >= cfg.unit_occupy_time:
                cc.is_open = True
            u.state = "occupying"
            return True, 0.0

        if action in TECH_ACTIONS:
            if not board.at_factory(u.x, u.y):
                return False, 0.0
            idx = int(action) - int(Action.TECH_0)
            key = TECH_KEYS[idx]
            tdef = TECH_TREE[key]
            # Persistent tech: can only buy once
            if tdef["persistent"] and key in self._techs_owned:
                return False, 0.0
            # Prerequisite check
            prereq = tdef.get("prereq")
            if prereq and prereq not in self._techs_owned:
                return False, 0.0
            if self.compute < tdef["cost"]:
                return False, 0.0
            self.compute -= tdef["cost"]
            self._techs_owned.add(key)
            self._apply_tech(key, tdef)
            u.state = "researching"
            return True, 0.0

        return False, 0.0

    def _complete_busy_action(self):
        pass   # busy_ticks already captured side-effects at action time

    def _apply_tech(self, key: str, tdef: dict):
        """Apply a tech upgrade's effect immediately."""
        effect = tdef.get("effect", {})
        if "product_cost_delta" in effect:
            self.factory.cost_delta += effect["product_cost_delta"]
        if "time_multiplier" in effect:
            self.factory.time_multiplier *= effect["time_multiplier"]
        if "price_multiplier" in effect:
            self.factory.price_multiplier *= effect["price_multiplier"]
        if "capacity_pct" in effect:
            self.unit.capacity = int(self.unit.capacity * (1.0 + effect["capacity_pct"]))
        if "extra_lines" in effect:
            self.factory.production_lines += effect["extra_lines"]
        if "move_factor" in effect:
            self._move_factor = getattr(self, "_move_factor", 1.0) * effect["move_factor"]
        # "reveal" (market_analysis) and "compute_rate_bonus" (compute_expansion)
        # are handled at read time in _encode_obs / _accrue_compute

    # ── Helpers ────────────────────────────────────────────────────────────────

    def market_at(self, x: int, y: int) -> Optional[Market]:
        return self._market_by_pos.get((x, y))

    def _market_at(self, x: int, y: int) -> Optional[Market]:
        return self.market_at(x, y)

    def _best_buyable(self, mkt: Market) -> Tuple[Optional[int], float]:
        """Return (pid, effective_buy_price) of affordable product with best cross-market upside.

        effective_buy_price applies factory.cost_delta (negative after cost_reduction tech).
        """
        best_pid, best_price = None, None
        best_upside = 0.0
        sell_mult = self._price_multiplier()
        other_markets = [om for om in self.markets if om.id != mkt.id]
        if not other_markets:
            return None, None
        for pid in PRODUCT_DEFS:
            raw_price = mkt.get_price(pid)
            effective_price = max(0.0, raw_price + self.factory.cost_delta)
            if self.money < effective_price:
                continue
            best_sell = max(om.get_price(pid, sell_mult) for om in other_markets)
            upside = best_sell - effective_price
            if upside > best_upside:
                best_upside = upside
                best_pid = pid
                best_price = effective_price
        return best_pid, best_price

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

        # Unit position + HP [0-2]
        obs[0] = u.x / max(1, cfg.map_height)
        obs[1] = u.y / max(1, cfg.map_width)
        obs[2] = u.hp / max(1, u.max_hp)

        # Unit inventory: raw + per-product [3-8]
        cap = max(1, u.capacity)
        obs[3] = u.raw_inv / cap
        for pid in range(5):
            obs[4 + pid] = u.prod_inv.get(pid, 0.0) / cap

        # Busy [9]
        obs[9] = min(u.busy_ticks / 10.0, 1.0)

        # Economy [10-11]
        obs[10] = math.log10(max(1, self.money + 1)) / 5.0
        obs[11] = min(self.compute / 100.0, 2.0)

        # Time [12-14]
        obs[12] = self.time / max(1, cfg.max_game_time)
        obs[13] = math.sin(2 * math.pi * self.time / cfg.market_period)
        obs[14] = math.cos(2 * math.pi * self.time / cfg.market_period)

        # Factory: raw + per-product stock + queue [15-21]
        scap = max(1, f.storage_cap)
        obs[15] = f.raw_stock / scap
        for pid in range(5):
            obs[16 + pid] = f.products.get(pid, 0.0) / scap
        obs[21] = min(f.queue_len / 10.0, 1.0)

        # Resource points [22-33]: up to 4 × (dx/H, dy/W, stock_ratio)
        for i in range(4):
            base = 22 + i * 3
            if i < len(self.board.resource_points):
                rp = self.board.resource_points[i]
                obs[base]   = (rp.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (rp.y - u.y) / max(1, cfg.map_width)
                obs[base+2] = rp.stock / max(1, rp.max_stock)

        # Compute centers [34-45]: up to 3 × (dx, dy, is_open, occupy_progress)
        occ_time = max(1.0, cfg.unit_occupy_time)
        for i in range(3):
            base = 34 + i * 4
            if i < len(self.board.compute_centers):
                cc = self.board.compute_centers[i]
                obs[base]   = (cc.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (cc.y - u.y) / max(1, cfg.map_width)
                obs[base+2] = float(cc.is_open)
                obs[base+3] = min(cc.occupy_progress / occ_time, 1.0)

        # Markets [46-73]: up to 4 × (dx, dy, price×5)
        # Markets 2-3 prices are only revealed when market_analysis tech is owned.
        mult = self._price_multiplier()
        analysis_owned = "market_analysis" in self._techs_owned
        for i in range(4):
            base = 46 + i * 7
            if i < len(self.markets):
                m = self.markets[i]
                obs[base]   = (m.x - u.x) / max(1, cfg.map_height)
                obs[base+1] = (m.y - u.y) / max(1, cfg.map_width)
                if i < 2 or analysis_owned:
                    for pid in range(5):
                        lo, rng = _PRICE_NORM[pid]
                        obs[base + 2 + pid] = (m.get_price(pid, mult) - lo) / rng

        # Techs owned [74-81]: one-hot per tech slot
        for i, key in enumerate(TECH_KEYS):
            obs[74 + i] = 1.0 if key in self._techs_owned else 0.0

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
