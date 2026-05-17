"""
Unit / character class: represents the controllable agent on the map.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Dict


@dataclass
class Unit:
    uid: int
    x: int
    y: int
    max_hp: int = 300
    capacity: int = 30

    # Runtime state
    hp: int = field(init=False)
    # raw_inv: raw harvested resources
    # prod_inv: finished products (by product_id)
    raw_inv: float = field(default=0.0, init=False)
    prod_inv: Dict[int, float] = field(default_factory=dict, init=False)

    # Tracks per-product quantities purchased at each market (factory-produced not tracked).
    # Used to enforce cross-market arbitrage: can't sell units bought at the same market.
    prod_origin: Dict[int, Dict[int, float]] = field(default_factory=dict, init=False)

    # Busy system: while busy_ticks > 0 the unit ignores new commands
    busy_ticks: int = field(default=0, init=False)
    busy_action: str = field(default="", init=False)  # tag for what's completing

    state: str = field(default="idle", init=False)

    def __post_init__(self):
        self.hp = self.max_hp
        self.prod_inv = {}
        self.prod_origin = {}

    # ── Inventory helpers ──────────────────────────────────────────────────────
    @property
    def total_goods(self) -> float:
        return self.raw_inv + sum(self.prod_inv.values())

    @property
    def free_capacity(self) -> float:
        return max(0.0, self.capacity - self.total_goods)

    def add_product(self, pid: int, qty: float, origin_market: int | None = None):
        self.prod_inv[pid] = self.prod_inv.get(pid, 0.0) + qty
        if origin_market is not None:
            origins = self.prod_origin.setdefault(pid, {})
            origins[origin_market] = origins.get(origin_market, 0.0) + qty

    def take_product(self, pid: int, qty: float) -> float:
        have = self.prod_inv.get(pid, 0.0)
        actual = min(have, qty)
        self.prod_inv[pid] = have - actual
        return actual

    def origin_qty(self, pid: int, market_id: int) -> float:
        return self.prod_origin.get(pid, {}).get(market_id, 0.0)

    def all_products(self) -> Dict[int, float]:
        return {pid: qty for pid, qty in self.prod_inv.items() if qty > 0}

    def clear_products(self):
        self.prod_inv.clear()
        self.raw_inv = 0.0
        self.prod_origin.clear()

    # ── Movement ───────────────────────────────────────────────────────────────
    def move(self, dx: int, dy: int, max_h: int, max_w: int) -> bool:
        nx, ny = self.x + dx, self.y + dy
        if 0 <= nx < max_h and 0 <= ny < max_w:
            self.x, self.y = nx, ny
            return True
        return False
