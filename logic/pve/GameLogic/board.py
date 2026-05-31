"""
Map / board management: grid state, placement of entities, obstacle checking.
"""
from __future__ import annotations
import math
import random
from collections import deque
from typing import List, Optional, Tuple

from .config import (
    GameConfig, CELL_EMPTY, CELL_OBSTACLE, CELL_MARKET,
    CELL_RESOURCE, CELL_COMPUTE, CELL_FACTORY,
)


class ResourcePoint:
    def __init__(self, x: int, y: int, rid: int, cfg: GameConfig):
        self.x = x
        self.y = y
        self.id = rid
        self.stock: float = float(cfg.initial_resource_stock)
        self.max_stock: float = float(cfg.initial_resource_stock) * 2.0
        self.total_harvested: float = 0.0
        self.regen_rate = cfg.resource_regen_rate  # multiplier
        self.period = cfg.market_period             # re-use oscillation period
        self.depleted = False

    def regen(self, dt: float, t: float):
        if self.depleted:
            return
        rate = self.regen_rate * (1.0 + math.sin(2 * math.pi * t / self.period)) / 2.0
        self.stock = min(self.max_stock, self.stock + rate * dt)

    def harvest(self, amount: float) -> float:
        """Harvest up to `amount` units; return actual harvested."""
        if self.depleted:
            return 0.0
        actual = min(self.stock, amount)
        self.stock -= actual
        self.total_harvested += actual
        if self.stock <= 0.0 and self.total_harvested >= self.max_stock:
            self.depleted = True
        return actual


class ComputeCenter:
    def __init__(self, x: int, y: int, cid: int):
        self.x = x
        self.y = y
        self.id = cid
        self.is_open = False
        self.occupy_progress: float = 0.0   # seconds; opens at cfg.unit_occupy_time


class Board:
    """Holds the grid and all placed entities."""

    def __init__(self, cfg: GameConfig, seed: Optional[int] = None):
        self.cfg = cfg
        self.W = cfg.map_width
        self.H = cfg.map_height
        self._rng = random.Random(seed)

        # Grid[row][col] stores cell type
        self.grid: List[List[int]] = [
            [CELL_EMPTY for _ in range(self.W)] for _ in range(self.H)
        ]

        self.resource_points: List[ResourcePoint] = []
        self.compute_centers: List[ComputeCenter] = []
        self.market_positions: List[Tuple[int, int]] = []

        self._build()

    # ── Build ──────────────────────────────────────────────────────────────────
    def _build(self):
        cfg = self.cfg
        # Factory always at (factory_x, factory_y)
        fx, fy = cfg.factory_x, cfg.factory_y
        self.grid[fx][fy] = CELL_FACTORY

        used = {(fx, fy)}

        if cfg.random_map:
            self._place_random_obstacles(used)

        self.market_positions = self._place_entities(
            cfg.num_markets, CELL_MARKET, used
        )
        for i in range(cfg.num_resource_points):
            pos = self._place_one(CELL_RESOURCE, used)
            self.resource_points.append(ResourcePoint(pos[0], pos[1], i, cfg))
        for i in range(cfg.num_compute_centers):
            pos = self._place_one(CELL_COMPUTE, used)
            self.compute_centers.append(ComputeCenter(pos[0], pos[1], i))

        if cfg.random_map:
            # Ensure factory can reach all key locations even with random obstacles.
            self._ensure_connectivity()

    def _place_random_obstacles(self, used: set, density: float = 0.1):
        total = self.W * self.H
        n_obs = int(total * density)
        placed = 0
        attempts = 0
        while placed < n_obs and attempts < n_obs * 10:
            r = self._rng.randint(0, self.H - 1)
            c = self._rng.randint(0, self.W - 1)
            if (r, c) not in used:
                self.grid[r][c] = CELL_OBSTACLE
                used.add((r, c))
                placed += 1
            attempts += 1

    def _place_entities(self, n: int, cell_type: int, used: set) -> List[Tuple[int, int]]:
        positions = []
        for _ in range(n):
            pos = self._place_one(cell_type, used)
            positions.append(pos)
        return positions

    def _place_one(self, cell_type: int, used: set) -> Tuple[int, int]:
        for _ in range(10000):
            r = self._rng.randint(0, self.H - 1)
            c = self._rng.randint(0, self.W - 1)
            if (r, c) not in used:
                self.grid[r][c] = cell_type
                used.add((r, c))
                return (r, c)
        raise RuntimeError("Board too crowded to place entity")

    def _ensure_connectivity(self) -> None:
        start = (self.cfg.factory_x, self.cfg.factory_y)
        targets: List[Tuple[int, int]] = []
        targets.extend(self.market_positions)
        targets.extend((rp.x, rp.y) for rp in self.resource_points)
        targets.extend((cc.x, cc.y) for cc in self.compute_centers)

        for target in targets:
            if not self._is_reachable(start, target):
                self._carve_manhattan_path(start, target)

    def _is_reachable(self, start: Tuple[int, int], goal: Tuple[int, int]) -> bool:
        if start == goal:
            return True
        sx, sy = start
        gx, gy = goal
        if not self.is_passable(gx, gy):
            return False

        queue = deque([(sx, sy)])
        visited = [[False for _ in range(self.W)] for _ in range(self.H)]
        visited[sx][sy] = True

        while queue:
            x, y = queue.popleft()
            for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < self.H and 0 <= ny < self.W and not visited[nx][ny]:
                    if not self.is_passable(nx, ny):
                        continue
                    if (nx, ny) == (gx, gy):
                        return True
                    visited[nx][ny] = True
                    queue.append((nx, ny))
        return False

    def _carve_manhattan_path(self, start: Tuple[int, int], goal: Tuple[int, int]) -> None:
        x, y = start
        gx, gy = goal
        while (x, y) != (gx, gy):
            can_move_x = x != gx
            can_move_y = y != gy
            if can_move_x and can_move_y:
                move_axis = self._rng.choice(("x", "y"))
            else:
                move_axis = "x" if can_move_x else "y"

            if move_axis == "x":
                x += 1 if gx > x else -1
            else:
                y += 1 if gy > y else -1

            if self.grid[x][y] == CELL_OBSTACLE:
                self.grid[x][y] = CELL_EMPTY

    # ── Queries ──────────────────────────���─────────────────────────────────────
    def is_passable(self, x: int, y: int) -> bool:
        if not (0 <= x < self.H and 0 <= y < self.W):
            return False
        return self.grid[x][y] != CELL_OBSTACLE

    def manhattan(self, x1: int, y1: int, x2: int, y2: int) -> int:
        return abs(x1 - x2) + abs(y1 - y2)

    def nearest_market(self, x: int, y: int) -> Optional[Tuple[int, int]]:
        """Return nearest market within Manhattan distance ≤ 1, or None."""
        best = None
        best_dist = 9999
        for mx, my in self.market_positions:
            d = self.manhattan(x, y, mx, my)
            if d <= 1 and d < best_dist:
                best = (mx, my)
                best_dist = d
        return best

    def nearest_resource(self, x: int, y: int) -> Optional[ResourcePoint]:
        """Return nearest non-depleted resource within harvest range (≤ 2)."""
        best: Optional[ResourcePoint] = None
        best_dist = 9999
        for rp in self.resource_points:
            if rp.depleted:
                continue
            d = self.manhattan(x, y, rp.x, rp.y)
            if d <= 2 and d < best_dist:
                best = rp
                best_dist = d
        return best

    def nearest_compute_center(self, x: int, y: int) -> Optional[ComputeCenter]:
        """Return adjacent compute center, preferring unopened ones if any."""
        fallback = None
        for cc in self.compute_centers:
            if self.manhattan(x, y, cc.x, cc.y) <= 1:
                if not cc.is_open:
                    return cc
                if fallback is None:
                    fallback = cc
        return fallback

    def at_factory(self, x: int, y: int) -> bool:
        return x == self.cfg.factory_x and y == self.cfg.factory_y
