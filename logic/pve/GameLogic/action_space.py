"""
Action space definitions and validity masks.

Phase 1 action space (20 discrete actions):
  0  WAIT          – pass one tick
  1  MOVE_UP       – x - 1
  2  MOVE_DOWN     – x + 1
  3  MOVE_LEFT     – y - 1
  4  MOVE_RIGHT    – y + 1
  5  BUY           – buy cheapest product at adjacent market
  6  SELL_0        – sell all carried product-0 (semiconductor) at adjacent market
  7  SELL_1        – sell all carried product-1 (medicine)
  8  SELL_2        – sell all carried product-2 (small_goods)
  9  SELL_3        – sell all carried product-3 (clothing)
  10 SELL_4        – sell all carried product-4 (food)
  11 HARVEST       – harvest raw materials from nearby resource point
  12 DEPOSIT       – deposit unit's raw_inv into factory (must be at factory)
  13 PRODUCE_0     – queue product-0 for production (at factory, costs raw_stock)
  14 PRODUCE_1     – queue product-1
  15 PRODUCE_2     – queue product-2
  16 PRODUCE_3     – queue product-3
  17 PRODUCE_4     – queue product-4
  18 LOAD          – load produced goods from factory into unit inventory
  19 OCCUPY        – progress occupation of adjacent compute center

Phase 2 action space (8 tech upgrades, cost compute pts, must be at factory):
  20 TECH_0        – cost_reduction    (50 pts): product buy cost -2
  21 TECH_1        – efficiency        (40 pts): production time ×0.5
  22 TECH_2        – marketing         (80 pts): sell price ×1.1
  23 TECH_3        – durability        (30 pts): unit carry capacity +50%
  24 TECH_4        – multi_line        (60 pts): +1 production line
  25 TECH_5        – path_optimization (50 pts, needs TECH_1): move costs -1 busy tick
  26 TECH_6        – market_analysis   (40 pts, non-persistent): reveal market 2-3 prices in obs
  27 TECH_7        – compute_expansion (70 pts): compute accrual rate +30%
"""
from __future__ import annotations
from enum import IntEnum
from typing import List, TYPE_CHECKING

import numpy as np

from .config import PRODUCT_DEFS, TECH_TREE

if TYPE_CHECKING:
    from .game_env import GameEnvironment


class Action(IntEnum):
    WAIT       = 0
    MOVE_UP    = 1
    MOVE_DOWN  = 2
    MOVE_LEFT  = 3
    MOVE_RIGHT = 4
    BUY        = 5
    SELL_0     = 6
    SELL_1     = 7
    SELL_2     = 8
    SELL_3     = 9
    SELL_4     = 10
    HARVEST    = 11
    DEPOSIT    = 12
    PRODUCE_0  = 13
    PRODUCE_1  = 14
    PRODUCE_2  = 15
    PRODUCE_3  = 16
    PRODUCE_4  = 17
    LOAD       = 18
    OCCUPY     = 19
    TECH_0     = 20
    TECH_1     = 21
    TECH_2     = 22
    TECH_3     = 23
    TECH_4     = 24
    TECH_5     = 25
    TECH_6     = 26
    TECH_7     = 27


N_ACTIONS = len(Action)  # 28

# Ordered list of tech keys matching TECH_0..7
TECH_KEYS: List[str] = [
    "cost_reduction",    # TECH_0
    "efficiency",        # TECH_1
    "marketing",         # TECH_2
    "durability",        # TECH_3
    "multi_line",        # TECH_4
    "path_optimization", # TECH_5
    "market_analysis",   # TECH_6
    "compute_expansion", # TECH_7
]

# Convenience lists (index within list == product id)
SELL_ACTIONS: List[Action] = [
    Action.SELL_0, Action.SELL_1, Action.SELL_2, Action.SELL_3, Action.SELL_4
]
PRODUCE_ACTIONS: List[Action] = [
    Action.PRODUCE_0, Action.PRODUCE_1, Action.PRODUCE_2, Action.PRODUCE_3, Action.PRODUCE_4
]
TECH_ACTIONS: List[Action] = [
    Action.TECH_0, Action.TECH_1, Action.TECH_2, Action.TECH_3,
    Action.TECH_4, Action.TECH_5, Action.TECH_6, Action.TECH_7,
]

MOVE_DELTAS = {
    Action.MOVE_UP:    (-1,  0),
    Action.MOVE_DOWN:  ( 1,  0),
    Action.MOVE_LEFT:  ( 0, -1),
    Action.MOVE_RIGHT: ( 0,  1),
}

ACTION_NAMES = {a: a.name for a in Action}


def compute_action_mask(env: "GameEnvironment") -> np.ndarray:
    """
    Return a boolean mask of shape (N_ACTIONS,) indicating which actions
    are currently valid for the primary unit.
    """
    mask = np.zeros(N_ACTIONS, dtype=bool)
    u = env.unit
    board = env.board
    factory = env.factory

    mask[Action.WAIT] = True

    if u.busy_ticks > 0:
        return mask  # busy: only WAIT is valid

    # Movement
    for act, (dx, dy) in MOVE_DELTAS.items():
        nx, ny = u.x + dx, u.y + dy
        if board.is_passable(nx, ny):
            mask[act] = True

    # BUY: adjacent market + capacity + money (affordability checked against market price)
    mkt_pos = board.nearest_market(u.x, u.y)
    mkt_obj = None
    if mkt_pos is not None:
        mkt_obj = env.market_at(*mkt_pos)
    if mkt_obj is not None:
        best_pid, best_price = env._best_buyable(mkt_obj)
        if best_pid is not None and u.free_capacity >= 1 and env.money >= best_price:
            mask[Action.BUY] = True

        # SELL_pid: carrying that product + not same-market-origin (arbitrage block)
        for sell_act in SELL_ACTIONS:
            pid = sell_act - Action.SELL_0
            if u.prod_inv.get(pid, 0.0) > 0:
                blocked = u.origin_qty(pid, mkt_obj.id)
                if u.prod_inv.get(pid, 0.0) - blocked > 0:
                    mask[sell_act] = True

    # HARVEST: nearby non-depleted resource + capacity
    rp = board.nearest_resource(u.x, u.y)
    if rp is not None and u.free_capacity >= 1:
        mask[Action.HARVEST] = True

    # Factory actions (must be standing on factory cell)
    if board.at_factory(u.x, u.y):
        # DEPOSIT: unit carrying raw materials
        if u.raw_inv > 0:
            mask[Action.DEPOSIT] = True

        # PRODUCE_pid: enough raw_stock + queue not full
        max_queue = factory.production_lines * 5
        if factory.queue_len < max_queue:
            for prod_act in PRODUCE_ACTIONS:
                pid = prod_act - Action.PRODUCE_0
                if factory.raw_stock >= PRODUCT_DEFS[pid]["raw_cost"]:
                    mask[prod_act] = True

        # LOAD: factory has products + unit has free capacity
        if factory.total_product_stock > 0 and u.free_capacity >= 1:
            mask[Action.LOAD] = True

        # TECH_x: at factory + enough compute + not already owned (or non-persistent)
        for i, tech_act in enumerate(TECH_ACTIONS):
            key = TECH_KEYS[i]
            tdef = TECH_TREE[key]
            # Persistent techs can only be bought once
            if tdef["persistent"] and key in env._techs_owned:
                continue
            # Check compute cost
            if env.compute < tdef["cost"]:
                continue
            # Check prerequisite
            prereq = tdef.get("prereq")
            if prereq and prereq not in env._techs_owned:
                continue
            mask[tech_act] = True

    # OCCUPY: adjacent to a compute center that is not yet open
    cc = board.nearest_compute_center(u.x, u.y)
    if cc is not None and not cc.is_open:
        mask[Action.OCCUPY] = True

    return mask


# Exported for backward compatibility
PRODUCT_DEFS_COSTS = [pdef["cost"] for pdef in PRODUCT_DEFS.values()]
