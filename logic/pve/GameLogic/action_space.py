"""
Action space definitions and validity masks.

Phase 1 action space (8 discrete actions):
  0  WAIT          – pass one tick
  1  MOVE_UP       – x - 1
  2  MOVE_DOWN     – x + 1
  3  MOVE_LEFT     – y - 1
  4  MOVE_RIGHT    – y + 1
  5  BUY           – buy best product at adjacent market
  6  SELL          – sell all carried products at adjacent market
  7  HARVEST       – harvest from nearby resource point

Future (Phase 2+):
  8  PRODUCE       – queue 1 product at factory (consumes raw_inv)
  9  OCCUPY        – start/continue occupying adjacent compute center
  10 RECRUIT       – recruit new unit (costs compute pts)
  11-18  TECH_x   – purchase tech upgrade
"""
from __future__ import annotations
from enum import IntEnum
from typing import TYPE_CHECKING
import numpy as np

if TYPE_CHECKING:
    from .game_env import GameEnvironment


class Action(IntEnum):
    WAIT       = 0
    MOVE_UP    = 1
    MOVE_DOWN  = 2
    MOVE_LEFT  = 3
    MOVE_RIGHT = 4
    BUY        = 5
    SELL       = 6
    HARVEST    = 7


N_ACTIONS = len(Action)

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

    Invalid action execution is still allowed (returns a small penalty),
    but the mask is used by action-masking PPO variants.
    """
    mask = np.zeros(N_ACTIONS, dtype=bool)
    u = env.unit
    board = env.board

    # WAIT is always valid
    mask[Action.WAIT] = True

    # Movement: target cell must be passable and unit must not be busy
    if u.busy_ticks == 0:
        for act, (dx, dy) in MOVE_DELTAS.items():
            nx, ny = u.x + dx, u.y + dy
            if board.is_passable(nx, ny):
                mask[act] = True

    # BUY: adjacent market + have capacity + have money
    if u.busy_ticks == 0:
        mkt = board.nearest_market(u.x, u.y)
        if mkt is not None:
            best_cost = min(PRODUCT_DEFS_COSTS)
            if u.free_capacity >= 1 and env.money >= best_cost:
                mask[Action.BUY] = True

    # SELL: adjacent market + carrying something
    if u.busy_ticks == 0:
        mkt = board.nearest_market(u.x, u.y)
        if mkt is not None and u.total_goods > 0:
            mask[Action.SELL] = True

    # HARVEST: nearby non-depleted resource + have capacity
    if u.busy_ticks == 0:
        rp = board.nearest_resource(u.x, u.y)
        if rp is not None and u.free_capacity >= 1:
            mask[Action.HARVEST] = True

    return mask


# Exported for use by the mask function without importing PRODUCT_DEFS
from .config import PRODUCT_DEFS
PRODUCT_DEFS_COSTS = [pdef["cost"] for pdef in PRODUCT_DEFS.values()]
