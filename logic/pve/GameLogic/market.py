"""
Market system: Ornstein-Uhlenbeck random walk prices for all 5 product types.

Each market maintains stateful prices updated each tick via:
  dP = θ(μ - P)dt + σ√dt · N(0,1)
  clamped to [lo, lo + amplitude]
"""
from __future__ import annotations
import random
from typing import Dict, List, Optional, Tuple

from .config import GameConfig, PRODUCT_DEFS

# Mean-reversion speed (per second). Correlation time ≈ 1/θ ≈ 20 seconds.
_THETA = 0.05
# Noise scale relative to price amplitude (per √second).
_SIGMA_SCALE = 0.12


class Market:
    """
    Stateful market: prices follow an OU random walk.
    Call tick(dt) each environment step to advance prices.
    """

    def __init__(self, x: int, y: int, mid: int, cfg: GameConfig, seed: Optional[int] = None):
        self.x = x
        self.y = y
        self.id = mid
        self._init_seed = seed if seed is not None else mid * 137

        self._ou_params: Dict[int, dict] = {}
        for pid, pdef in PRODUCT_DEFS.items():
            lo, hi = pdef["val_range"]
            amplitude = (hi - lo) * cfg.price_volatility
            self._ou_params[pid] = {
                "lo": lo,
                "hi": lo + amplitude,
                "mean": lo + amplitude * 0.5,
                "sigma": amplitude * _SIGMA_SCALE,
            }

        self._current_prices: Dict[int, float] = {}
        self._rng: random.Random = random.Random(self._init_seed)
        self._init_prices()

    def _init_prices(self):
        for pid, p in self._ou_params.items():
            self._current_prices[pid] = self._rng.uniform(p["lo"], p["hi"])

    def reset(self, seed: Optional[int] = None):
        self._rng = random.Random(seed if seed is not None else self._init_seed)
        self._init_prices()

    def tick(self, dt: float):
        """Advance prices by one time step using the OU process."""
        for pid, p in self._ou_params.items():
            cur = self._current_prices[pid]
            drift = _THETA * (p["mean"] - cur) * dt
            noise = self._rng.gauss(0, p["sigma"] * (dt ** 0.5))
            self._current_prices[pid] = max(p["lo"], min(p["hi"], cur + drift + noise))

    def get_price(self, pid: int, marketing_mult: float = 1.0) -> float:
        return self._current_prices[pid] * marketing_mult

    def best_product_to_sell(self, marketing_mult: float = 1.0) -> Tuple[int, float]:
        best_pid, best_price = 0, -1.0
        for pid in PRODUCT_DEFS:
            price = self._current_prices[pid] * marketing_mult
            if price > best_price:
                best_price, best_pid = price, pid
        return best_pid, best_price

    def price_info(self) -> Dict[int, float]:
        return dict(self._current_prices)


def build_markets(
    positions: List[Tuple[int, int]],
    cfg: GameConfig,
    env_seed: Optional[int] = None,
) -> List[Market]:
    base = env_seed if env_seed is not None else 0
    return [
        Market(x, y, i, cfg, seed=base * 13 + i * 31 + 7)
        for i, (x, y) in enumerate(positions)
    ]
