"""
Market system: dynamic price functions for all 5 product types.
"""
from __future__ import annotations
import math
import random
from typing import Dict, List, Optional, Tuple

from .config import GameConfig, PRODUCT_DEFS


class Market:
    """
    Each market has an independent sinusoidal price function per product.
    price(t) = base + amplitude × (1 + sin(2π·t / period + phase)) / 2
    """

    def __init__(self, x: int, y: int, mid: int, cfg: GameConfig, seed: Optional[int] = None):
        self.x = x
        self.y = y
        self.id = mid
        rng = random.Random(seed if seed is not None else mid * 137)

        self._price_params: Dict[int, dict] = {}
        for pid, pdef in PRODUCT_DEFS.items():
            lo, hi = pdef["val_range"]
            amplitude = (hi - lo) * cfg.price_volatility
            base = lo
            # Each market has a random phase offset so markets are not synchronised
            phase = rng.uniform(0, 2 * math.pi)
            period = cfg.market_period * rng.uniform(0.7, 1.5)
            self._price_params[pid] = {
                "base": base, "amplitude": amplitude,
                "period": period, "phase": phase,
            }

    def get_price(self, pid: int, t: float, marketing_mult: float = 1.0) -> float:
        p = self._price_params[pid]
        raw = p["base"] + p["amplitude"] * (1 + math.sin(2 * math.pi * t / p["period"] + p["phase"])) / 2
        return raw * marketing_mult

    def best_product_to_sell(self, t: float, marketing_mult: float = 1.0) -> Tuple[int, float]:
        """Return (product_id, price) for the most valuable product right now."""
        best_pid, best_price = 0, -1.0
        for pid in PRODUCT_DEFS:
            price = self.get_price(pid, t, marketing_mult)
            if price > best_price:
                best_price, best_pid = price, pid
        return best_pid, best_price

    def price_info(self, t: float) -> Dict[int, float]:
        return {pid: self.get_price(pid, t) for pid in PRODUCT_DEFS}


def build_markets(positions: List[Tuple[int, int]], cfg: GameConfig) -> List[Market]:
    return [Market(x, y, i, cfg, seed=i * 31 + 7) for i, (x, y) in enumerate(positions)]
