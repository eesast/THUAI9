from __future__ import annotations

import json
import math
import random
import tempfile
import zipfile
from pathlib import Path
from typing import Any

import numpy as np

from RLInterfaces import BaseAgent


WAIT = 0
MOVE_UP = 1
MOVE_DOWN = 2
MOVE_LEFT = 3
MOVE_RIGHT = 4
BUY = 5
SELL_0 = 6
SELL_4 = 10
HARVEST = 11
DEPOSIT = 12
PRODUCE_0 = 13
PRODUCE_4 = 17
LOAD = 18
OCCUPY = 19
TECH_0 = 20
TECH_7 = 27

N_PRIMITIVE_ACTIONS = 28
OBS_DIM = 82

FOLLOW_EXPERT = 0
BUY_MARKET_0 = 1
BUY_MARKET_3 = 4
SELL_MARKET_0 = 5
SELL_MARKET_3 = 8
FACTORY_PLAN = 9
OCCUPY_NEAREST = 10
WAIT_FOR_PRICE = 11
ENDGAME_LIQUIDATE = 12
MACRO_ACTION_COUNT = 13
MACRO_OBS_DIM = 168
BUNDLE_FORMAT = "thuai9-new-rules-router"

DIFFICULTIES = ("easy", "medium", "hard")
MAP_SIZE = {"easy": 5, "medium": 10, "hard": 15}
MARKET_COUNT = {"easy": 3, "medium": 3, "hard": 4}
CAPACITY_BASE = {"easy": 30.0, "medium": 30.0, "hard": 30.0}

PRICE_LO = np.asarray([40.0, 20.0, 4.0, 32.0, 12.0], dtype=np.float32)
PRICE_RANGE = np.asarray([80.0, 40.0, 8.0, 64.0, 12.0], dtype=np.float32)
RAW_COST = np.asarray([5.0, 3.0, 1.0, 4.0, 2.0], dtype=np.float32)
MOVE_DELTAS = {
    MOVE_UP: (-1, 0),
    MOVE_DOWN: (1, 0),
    MOVE_LEFT: (0, -1),
    MOVE_RIGHT: (0, 1),
}
SELL_ACTIONS = tuple(range(SELL_0, SELL_4 + 1))
PRODUCE_ACTIONS = tuple(range(PRODUCE_0, PRODUCE_4 + 1))
TECH_ACTIONS = tuple(range(TECH_0, TECH_7 + 1))


class ExpertParams:
    def __init__(
        self,
        buy_fill: float,
        endgame_time: float,
        wait_until_time: float,
        distance_penalty: float,
        sell_margin: float,
        explore_after: int,
        occupy_until: float,
        factory_until: float,
        tech_priority: tuple[int, ...],
    ):
        self.buy_fill = buy_fill
        self.endgame_time = endgame_time
        self.wait_until_time = wait_until_time
        self.distance_penalty = distance_penalty
        self.sell_margin = sell_margin
        self.explore_after = explore_after
        self.occupy_until = occupy_until
        self.factory_until = factory_until
        self.tech_priority = tech_priority


EXPERT_PARAMS = {
    "easy": ExpertParams(
        buy_fill=0.78,
        endgame_time=0.88,
        wait_until_time=0.48,
        distance_penalty=10.0,
        sell_margin=0.0,
        explore_after=45,
        occupy_until=0.20,
        factory_until=0.50,
        tech_priority=(23, 22, 20, 21, 25),
    ),
    "medium": ExpertParams(
        buy_fill=0.72,
        endgame_time=0.84,
        wait_until_time=0.40,
        distance_penalty=16.0,
        sell_margin=2.0,
        explore_after=65,
        occupy_until=0.28,
        factory_until=0.58,
        tech_priority=(23, 20, 22, 21, 25),
    ),
    "hard": ExpertParams(
        buy_fill=0.64,
        endgame_time=0.78,
        wait_until_time=0.34,
        distance_penalty=24.0,
        sell_margin=4.0,
        explore_after=90,
        occupy_until=0.35,
        factory_until=0.66,
        tech_priority=(23, 21, 25, 20, 22, 26),
    ),
}


def _valid_indices(mask: np.ndarray) -> np.ndarray:
    return np.flatnonzero(np.asarray(mask, dtype=bool))


def _safe_mask(env: Any) -> np.ndarray:
    try:
        mask = np.asarray(env.action_masks(), dtype=bool)
        if mask.shape == (N_PRIMITIVE_ACTIONS,):
            return mask
    except Exception:
        pass
    mask = np.zeros(N_PRIMITIVE_ACTIONS, dtype=bool)
    mask[WAIT] = True
    return mask


class SafetyLayer:
    def __init__(self, env: Any):
        self.env = env

    def mask(self) -> np.ndarray:
        return _safe_mask(self.env)

    def valid_indices(self, mask: np.ndarray) -> np.ndarray:
        return _valid_indices(mask)

    def ensure_valid(self, action: int, mask: np.ndarray, fallback: int = WAIT) -> int:
        if 0 <= int(action) < len(mask) and bool(mask[int(action)]):
            return int(action)
        if 0 <= int(fallback) < len(mask) and bool(mask[int(fallback)]):
            return int(fallback)
        valid = self.valid_indices(mask)
        return int(valid[0]) if len(valid) else WAIT


def money_from_obs(obs: np.ndarray) -> float:
    return max(0.0, float(10 ** (float(obs[10]) * 5.0) - 1.0))


def compute_from_obs(obs: np.ndarray) -> float:
    return max(0.0, float(obs[11]) * 100.0)


def time_ratio(obs: np.ndarray) -> float:
    return float(np.clip(obs[12], 0.0, 1.0))


def product_inventory(obs: np.ndarray) -> float:
    return float(np.sum(np.clip(obs[4:9], 0.0, 2.0)))


def total_inventory(obs: np.ndarray) -> float:
    return float(np.clip(obs[3], 0.0, 2.0) + product_inventory(obs))


def tech_owned(obs: np.ndarray, tech_action: int) -> bool:
    idx = int(tech_action) - TECH_0
    if 0 <= idx < 8:
        return bool(float(obs[74 + idx]) > 0.5)
    return False


def infer_difficulty(obs: np.ndarray) -> str:
    money = money_from_obs(obs)
    compute = compute_from_obs(obs)
    if money >= 100.0 or compute >= 50.0:
        return "easy"
    if money <= 40.0 or compute <= 25.0:
        return "hard"
    return "medium"


def params_for(difficulty: str) -> ExpertParams:
    return EXPERT_PARAMS.get(difficulty, EXPERT_PARAMS["medium"])


class DifficultyRouter:
    def __init__(self, models: dict[str, Any] | None = None):
        self.models = models or {}

    def infer(self, obs: np.ndarray) -> str:
        return infer_difficulty(obs)

    def params(self, difficulty: str) -> ExpertParams:
        return params_for(difficulty)

    def model_for(self, difficulty: str) -> Any | None:
        return self.models.get(difficulty)

    def drop_model(self, difficulty: str):
        self.models.pop(difficulty, None)

    def available_models(self) -> list[str]:
        return [name for name in DIFFICULTIES if name in self.models]


def market_base(index: int) -> int:
    return 46 + int(index) * 7


def market_count(difficulty: str) -> int:
    return MARKET_COUNT.get(difficulty, 3)


def map_size(difficulty: str) -> int:
    return MAP_SIZE.get(difficulty, 10)


def market_distance(obs: np.ndarray, index: int, difficulty: str) -> float:
    if index < 0 or index >= market_count(difficulty):
        return 999.0
    base = market_base(index)
    n = float(map_size(difficulty))
    return abs(float(obs[base]) * n) + abs(float(obs[base + 1]) * n)


def market_is_adjacent(obs: np.ndarray, index: int, difficulty: str) -> bool:
    return market_distance(obs, index, difficulty) <= 1.15


def adjacent_market(obs: np.ndarray, difficulty: str) -> int | None:
    candidates = [
        (market_distance(obs, idx, difficulty), idx)
        for idx in range(market_count(difficulty))
    ]
    if not candidates:
        return None
    dist, idx = min(candidates)
    return idx if dist <= 1.15 else None


def price_at(obs: np.ndarray, market: int, pid: int, difficulty: str) -> float:
    if market < 0 or market >= market_count(difficulty):
        return 0.0
    base = market_base(market)
    norm = float(obs[base + 2 + pid])
    # Markets beyond index 1 are hidden until market_analysis; zero means unknown.
    if market >= 2 and not tech_owned(obs, 26) and abs(norm) < 1e-7:
        return float(PRICE_LO[pid] + 0.5 * PRICE_RANGE[pid])
    return float(PRICE_LO[pid] + norm * PRICE_RANGE[pid])


def best_visible_upside(obs: np.ndarray, buy_market: int, difficulty: str) -> float:
    best = -1e9
    count = market_count(difficulty)
    for pid in range(5):
        buy_price = price_at(obs, buy_market, pid, difficulty)
        sell_price = max(
            price_at(obs, other, pid, difficulty)
            for other in range(count)
            if other != buy_market
        )
        best = max(best, sell_price - buy_price)
    return float(best)


def held_value_at(obs: np.ndarray, market: int, difficulty: str) -> float:
    value = 0.0
    for pid in range(5):
        value += max(0.0, float(obs[4 + pid])) * price_at(obs, market, pid, difficulty)
    return float(value)


def best_sell_action(obs: np.ndarray, mask: np.ndarray, market: int | None, difficulty: str) -> int | None:
    choices: list[tuple[float, int]] = []
    for action in SELL_ACTIONS:
        if action >= len(mask) or not bool(mask[action]):
            continue
        pid = action - SELL_0
        price = price_at(obs, market, pid, difficulty) if market is not None else PRICE_LO[pid]
        choices.append((float(obs[4 + pid]) * float(price), action))
    if not choices:
        return None
    return max(choices)[1]


def choose_buy_market(obs: np.ndarray, difficulty: str) -> int:
    params = params_for(difficulty)
    scores = []
    for idx in range(market_count(difficulty)):
        upside = best_visible_upside(obs, idx, difficulty)
        dist = market_distance(obs, idx, difficulty)
        scores.append((upside - params.distance_penalty * dist, idx))
    return max(scores)[1]


def choose_sell_market(obs: np.ndarray, difficulty: str) -> int:
    params = params_for(difficulty)
    scores = []
    for idx in range(market_count(difficulty)):
        value = held_value_at(obs, idx, difficulty)
        dist = market_distance(obs, idx, difficulty)
        scores.append((value - params.distance_penalty * dist, idx))
    return max(scores)[1]


def choose_market(obs: np.ndarray, prefer_sell: bool, difficulty: str) -> int:
    if prefer_sell and product_inventory(obs) > 1e-6:
        return choose_sell_market(obs, difficulty)
    return choose_buy_market(obs, difficulty)


class TargetSelector:
    def buy_market(self, obs: np.ndarray, difficulty: str) -> int:
        return choose_buy_market(obs, difficulty)

    def sell_market(self, obs: np.ndarray, difficulty: str) -> int:
        return choose_sell_market(obs, difficulty)

    def market(self, obs: np.ndarray, prefer_sell: bool, difficulty: str) -> int:
        return choose_market(obs, prefer_sell, difficulty)

    def nearest_closed_compute_center(self, obs: np.ndarray, difficulty: str) -> tuple[float, int] | None:
        candidates: list[tuple[float, int]] = []
        for idx in range(3):
            base = 34 + idx * 4
            if float(obs[base + 2]) < 0.5:
                dist = abs(float(obs[base]) * map_size(difficulty)) + abs(float(obs[base + 1]) * map_size(difficulty))
                if dist > 0.0:
                    candidates.append((dist, base))
        return min(candidates) if candidates else None


class PriceMemory:
    def __init__(self, horizon: int = 12):
        self.horizon = int(horizon)
        self.history: dict[tuple[int, int], list[float]] = {}

    def reset(self):
        self.history.clear()

    def observe(self, obs: np.ndarray, difficulty: str):
        for market in range(market_count(difficulty)):
            for pid in range(5):
                value = price_at(obs, market, pid, difficulty)
                key = (market, pid)
                hist = self.history.setdefault(key, [])
                hist.append(float(value))
                if len(hist) > self.horizon:
                    del hist[:-self.horizon]

    def trend(self, market: int, pid: int) -> float:
        hist = self.history.get((market, pid), [])
        if len(hist) < 3:
            return 0.0
        return float(hist[-1] - hist[0])


class OnlineNavigator:
    def __init__(self):
        self.visits: dict[tuple[int, int], int] = {}
        self.last_pos: tuple[int, int] | None = None
        self.last_action: int | None = None
        self.stuck_steps = 0

    def reset(self):
        self.visits.clear()
        self.last_pos = None
        self.last_action = None
        self.stuck_steps = 0

    def _pos(self, obs: np.ndarray, difficulty: str) -> tuple[int, int]:
        n = map_size(difficulty)
        x = int(np.clip(round(float(obs[0]) * n), 0, n - 1))
        y = int(np.clip(round(float(obs[1]) * n), 0, n - 1))
        return x, y

    def observe(self, obs: np.ndarray, difficulty: str):
        pos = self._pos(obs, difficulty)
        if self.last_pos == pos and float(obs[9]) <= 1e-6:
            self.stuck_steps += 1
        elif self.last_pos != pos:
            self.stuck_steps = 0
        self.last_pos = pos
        self.visits[pos] = self.visits.get(pos, 0) + 1

    def move_toward(self, obs: np.ndarray, mask: np.ndarray, dx_norm: float, dy_norm: float, difficulty: str) -> int:
        if float(obs[9]) > 0.0:
            return WAIT
        n = float(map_size(difficulty))
        dx = float(dx_norm) * n
        dy = float(dy_norm) * n
        if abs(dx) + abs(dy) <= 1.05:
            return self.explore(obs, mask, difficulty)

        preferred: list[int] = []
        if abs(dx) >= abs(dy):
            preferred.append(MOVE_DOWN if dx > 0 else MOVE_UP)
            preferred.append(MOVE_RIGHT if dy > 0 else MOVE_LEFT)
        else:
            preferred.append(MOVE_RIGHT if dy > 0 else MOVE_LEFT)
            preferred.append(MOVE_DOWN if dx > 0 else MOVE_UP)

        pos = self._pos(obs, difficulty)
        ranked: list[tuple[float, int]] = []
        for action in MOVE_DELTAS:
            if action >= len(mask) or not mask[action]:
                continue
            ddx, ddy = MOVE_DELTAS[action]
            new_dx = dx - ddx
            new_dy = dy - ddy
            new_pos = (pos[0] + ddx, pos[1] + ddy)
            reverse_penalty = 0.35 if self._is_reverse(action) else 0.0
            preferred_bonus = -0.25 if action in preferred else 0.0
            visit_penalty = 0.08 * self.visits.get(new_pos, 0)
            stuck_escape = -0.75 if self.stuck_steps >= 2 and not self._is_reverse(action) else 0.0
            score = abs(new_dx) + abs(new_dy) + reverse_penalty + visit_penalty + preferred_bonus + stuck_escape
            ranked.append((score, action))
        if ranked:
            action = min(ranked)[1]
            self.last_action = action
            return action
        return WAIT if mask[WAIT] else int(_valid_indices(mask)[0])

    def explore(self, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        if float(obs[9]) > 0.0:
            return WAIT
        pos = self._pos(obs, difficulty)
        choices = []
        for action, (dx, dy) in MOVE_DELTAS.items():
            if action < len(mask) and mask[action]:
                new_pos = (pos[0] + dx, pos[1] + dy)
                score = self.visits.get(new_pos, 0) + (0.5 if self._is_reverse(action) else 0.0)
                choices.append((score, action))
        if choices:
            action = min(choices)[1]
            self.last_action = action
            return action
        if HARVEST < len(mask) and mask[HARVEST]:
            return HARVEST
        return WAIT

    def _is_reverse(self, action: int) -> bool:
        pairs = {
            MOVE_UP: MOVE_DOWN,
            MOVE_DOWN: MOVE_UP,
            MOVE_LEFT: MOVE_RIGHT,
            MOVE_RIGHT: MOVE_LEFT,
        }
        return self.last_action is not None and pairs.get(action) == self.last_action


def build_macro_observation(
    obs: np.ndarray,
    primitive_mask: np.ndarray,
    difficulty: str = "medium",
    memory: PriceMemory | None = None,
) -> np.ndarray:
    features = np.zeros(MACRO_OBS_DIM, dtype=np.float32)
    obs_arr = np.asarray(obs, dtype=np.float32)
    mask_arr = np.asarray(primitive_mask, dtype=np.float32)
    features[:OBS_DIM] = obs_arr[:OBS_DIM]
    features[OBS_DIM : OBS_DIM + N_PRIMITIVE_ACTIONS] = mask_arr[:N_PRIMITIVE_ACTIONS]
    base = 110
    for i, name in enumerate(DIFFICULTIES):
        features[base + i] = 1.0 if difficulty == name else 0.0
    features[113] = np.clip(product_inventory(obs_arr), 0.0, 2.0)
    features[114] = np.clip(total_inventory(obs_arr), 0.0, 2.0)
    features[115] = np.clip(money_from_obs(obs_arr) / 250.0, 0.0, 4.0)
    features[116] = np.clip(compute_from_obs(obs_arr) / 120.0, 0.0, 3.0)
    features[117] = time_ratio(obs_arr)
    adj = adjacent_market(obs_arr, difficulty)
    features[118] = -1.0 if adj is None else float(adj) / 3.0

    offset = 120
    for idx in range(4):
        if idx < market_count(difficulty):
            features[offset] = np.clip(market_distance(obs_arr, idx, difficulty) / 20.0, 0.0, 2.0)
            features[offset + 1] = np.clip(best_visible_upside(obs_arr, idx, difficulty) / 100.0, -2.0, 2.0)
            features[offset + 2] = np.clip(held_value_at(obs_arr, idx, difficulty) / 120.0, 0.0, 3.0)
            if memory is not None:
                trends = [memory.trend(idx, pid) for pid in range(5)]
                features[offset + 3] = np.clip(max(trends) / 30.0, -2.0, 2.0)
            features[offset + 4] = 1.0 if market_is_adjacent(obs_arr, idx, difficulty) else 0.0
        offset += 5

    for i, action in enumerate(TECH_ACTIONS):
        features[140 + i] = 1.0 if tech_owned(obs_arr, action) else 0.0
    hint = expert_macro_hint(obs_arr, primitive_mask, difficulty, memory)
    if 0 <= hint < MACRO_ACTION_COUNT:
        features[148 + hint] = 1.0
    return features


def build_macro_action_mask(obs: np.ndarray, primitive_mask: np.ndarray, difficulty: str = "medium") -> np.ndarray:
    mask = np.zeros(MACRO_ACTION_COUNT, dtype=bool)
    inv = product_inventory(obs)
    params = params_for(difficulty)
    count = market_count(difficulty)
    mask[FOLLOW_EXPERT] = True
    for idx in range(count):
        mask[BUY_MARKET_0 + idx] = inv < params.buy_fill
        mask[SELL_MARKET_0 + idx] = inv > 1e-6
    mask[FACTORY_PLAN] = True
    mask[OCCUPY_NEAREST] = bool(primitive_mask[OCCUPY]) or time_ratio(obs) < params.occupy_until
    mask[WAIT_FOR_PRICE] = inv > 1e-6 and time_ratio(obs) < params.wait_until_time
    mask[ENDGAME_LIQUIDATE] = inv > 1e-6
    return mask


def expert_macro_hint(
    obs: np.ndarray,
    primitive_mask: np.ndarray,
    difficulty: str,
    memory: PriceMemory | None = None,
) -> int:
    params = params_for(difficulty)
    inv = product_inventory(obs)
    if inv > 1e-6 and time_ratio(obs) >= params.endgame_time:
        return ENDGAME_LIQUIDATE
    if inv > 1e-6:
        target = choose_sell_market(obs, difficulty)
        return SELL_MARKET_0 + min(target, 3)
    target = choose_buy_market(obs, difficulty)
    return BUY_MARKET_0 + min(target, 3)


class HybridController:
    def __init__(self):
        self.navigator = OnlineNavigator()
        self.price_memory = PriceMemory()
        self.targets = TargetSelector()
        self.current_option: int | None = None
        self.option_steps = 0
        self.no_trade_steps = 0
        self.prev_signature: tuple[float, ...] | None = None

    def reset(self):
        self.navigator.reset()
        self.price_memory.reset()
        self.current_option = None
        self.option_steps = 0
        self.no_trade_steps = 0
        self.prev_signature = None

    def observe(self, obs: np.ndarray, difficulty: str):
        self.navigator.observe(obs, difficulty)
        self.price_memory.observe(obs, difficulty)
        sig = tuple(np.round(np.asarray(obs[3:12], dtype=float), 4))
        if self.prev_signature is not None and sig == self.prev_signature:
            self.no_trade_steps += 1
        else:
            self.no_trade_steps = 0
        self.prev_signature = sig

    def start_option(self, macro_action: int):
        self.current_option = int(macro_action)
        self.option_steps = 0

    def macro_observation(self, obs: np.ndarray, primitive_mask: np.ndarray, difficulty: str) -> np.ndarray:
        return build_macro_observation(obs, primitive_mask, difficulty, self.price_memory)

    def macro_action_mask(self, obs: np.ndarray, primitive_mask: np.ndarray, difficulty: str) -> np.ndarray:
        return build_macro_action_mask(obs, primitive_mask, difficulty)

    def expert_action(self, obs: np.ndarray, primitive_mask: np.ndarray, difficulty: str) -> int:
        mask = np.asarray(primitive_mask, dtype=bool)
        if float(obs[9]) > 0.0:
            return WAIT
        params = params_for(difficulty)
        current_market = adjacent_market(obs, difficulty)
        sell_action = best_sell_action(obs, mask, current_market, difficulty)
        inv = product_inventory(obs)

        if inv > 1e-6 and time_ratio(obs) >= params.endgame_time:
            if sell_action is not None:
                return sell_action
            return self._go_to_market(self.targets.sell_market(obs, difficulty), obs, mask, difficulty)

        if sell_action is not None and self._should_sell_now(obs, current_market, difficulty):
            return sell_action

        factory_action = self._factory_action(obs, mask, difficulty)
        if factory_action is not None:
            return factory_action

        has_raw = float(obs[3]) > 0.05
        has_factory_stock = bool(np.sum(np.clip(obs[16:21], 0.0, 2.0)) > 1e-6)
        if time_ratio(obs) < params.factory_until and (has_raw or has_factory_stock):
            return self._factory_plan(obs, mask, difficulty)

        if inv < params.buy_fill and BUY < len(mask) and mask[BUY]:
            return BUY

        if inv > 1e-6:
            return self._go_to_market(self.targets.sell_market(obs, difficulty), obs, mask, difficulty)

        if HARVEST < len(mask) and mask[HARVEST] and (money_from_obs(obs) < 8.0 or time_ratio(obs) < 0.18):
            return HARVEST

        if self.no_trade_steps > params.explore_after:
            return self.navigator.explore(obs, mask, difficulty)
        return self._go_to_market(self.targets.buy_market(obs, difficulty), obs, mask, difficulty)

    def primitive_for_macro(
        self,
        macro_action: int,
        obs: np.ndarray,
        primitive_mask: np.ndarray,
        difficulty: str,
    ) -> int:
        self.option_steps += 1
        macro_action = int(macro_action)
        if macro_action == FOLLOW_EXPERT:
            return self.expert_action(obs, primitive_mask, difficulty)
        if BUY_MARKET_0 <= macro_action <= BUY_MARKET_3:
            return self._buy_at_market(macro_action - BUY_MARKET_0, obs, primitive_mask, difficulty)
        if SELL_MARKET_0 <= macro_action <= SELL_MARKET_3:
            return self._sell_at_market(macro_action - SELL_MARKET_0, obs, primitive_mask, difficulty)
        if macro_action == FACTORY_PLAN:
            return self._factory_plan(obs, primitive_mask, difficulty)
        if macro_action == OCCUPY_NEAREST:
            return self._occupy_plan(obs, primitive_mask, difficulty)
        if macro_action == WAIT_FOR_PRICE:
            if time_ratio(obs) > params_for(difficulty).wait_until_time:
                return self.expert_action(obs, primitive_mask, difficulty)
            return WAIT if primitive_mask[WAIT] else self.expert_action(obs, primitive_mask, difficulty)
        if macro_action == ENDGAME_LIQUIDATE:
            return self._sell_at_market(self.targets.sell_market(obs, difficulty), obs, primitive_mask, difficulty)
        return self.expert_action(obs, primitive_mask, difficulty)

    def option_complete(self, macro_action: int, obs: np.ndarray, last_primitive: int, difficulty: str) -> bool:
        if last_primitive in (BUY, LOAD, DEPOSIT, OCCUPY) or SELL_0 <= last_primitive <= SELL_4:
            return True
        if macro_action == WAIT_FOR_PRICE:
            return self.option_steps >= 3
        if macro_action == FACTORY_PLAN:
            return self.option_steps >= 12
        if macro_action == OCCUPY_NEAREST:
            return self.option_steps >= 18 or bool(last_primitive == OCCUPY)
        return self.option_steps >= self.option_limit(macro_action)

    def option_limit(self, macro_action: int) -> int:
        if BUY_MARKET_0 <= macro_action <= SELL_MARKET_3:
            return 18
        if macro_action == ENDGAME_LIQUIDATE:
            return 24
        if macro_action == FACTORY_PLAN:
            return 18
        return 8

    def _should_sell_now(self, obs: np.ndarray, market: int | None, difficulty: str) -> bool:
        if market is None:
            return False
        if time_ratio(obs) >= params_for(difficulty).wait_until_time:
            return True
        current = held_value_at(obs, market, difficulty)
        best = max(held_value_at(obs, idx, difficulty) for idx in range(market_count(difficulty)))
        trend_bonus = 0.0
        for pid in range(5):
            if obs[4 + pid] > 1e-6:
                trend_bonus = max(trend_bonus, self.price_memory.trend(market, pid))
        return current + params_for(difficulty).sell_margin >= best or trend_bonus < -1.5

    def _go_to_market(self, index: int, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        index = int(np.clip(index, 0, market_count(difficulty) - 1))
        base = market_base(index)
        return self.navigator.move_toward(obs, mask, float(obs[base]), float(obs[base + 1]), difficulty)

    def _buy_at_market(self, index: int, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        if index >= market_count(difficulty):
            return self.expert_action(obs, mask, difficulty)
        if market_is_adjacent(obs, index, difficulty) and mask[BUY] and product_inventory(obs) < params_for(difficulty).buy_fill:
            return BUY
        return self._go_to_market(index, obs, mask, difficulty)

    def _sell_at_market(self, index: int, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        if index >= market_count(difficulty):
            index = self.targets.sell_market(obs, difficulty)
        current = adjacent_market(obs, difficulty)
        if current == index:
            sell_action = best_sell_action(obs, mask, current, difficulty)
            if sell_action is not None:
                return sell_action
        return self._go_to_market(index, obs, mask, difficulty)

    def _factory_action(self, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int | None:
        if float(obs[0]) > 0.04 or float(obs[1]) > 0.04:
            return None
        params = params_for(difficulty)
        if time_ratio(obs) > params.factory_until:
            if LOAD < len(mask) and mask[LOAD]:
                return LOAD
            return None
        if DEPOSIT < len(mask) and mask[DEPOSIT]:
            return DEPOSIT
        if LOAD < len(mask) and mask[LOAD] and product_inventory(obs) < params.buy_fill:
            return LOAD
        for tech in params.tech_priority:
            if tech < len(mask) and mask[tech]:
                return tech
        produce = self._best_produce_action(obs, mask, difficulty)
        if produce is not None:
            return produce
        return None

    def _factory_plan(self, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        action = self._factory_action(obs, mask, difficulty)
        if action is not None:
            return action
        return self.navigator.move_toward(obs, mask, -float(obs[0]), -float(obs[1]), difficulty)

    def _occupy_plan(self, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        if OCCUPY < len(mask) and mask[OCCUPY]:
            return OCCUPY
        center = self.targets.nearest_closed_compute_center(obs, difficulty)
        if center is not None:
            _, base = center
            return self.navigator.move_toward(obs, mask, float(obs[base]), float(obs[base + 1]), difficulty)
        return self.expert_action(obs, mask, difficulty)

    def _best_produce_action(self, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int | None:
        best: tuple[float, int] | None = None
        for action in PRODUCE_ACTIONS:
            if action >= len(mask) or not mask[action]:
                continue
            pid = action - PRODUCE_0
            best_price = max(price_at(obs, market, pid, difficulty) for market in range(market_count(difficulty)))
            score = best_price / max(1.0, float(RAW_COST[pid]))
            if best is None or score > best[0]:
                best = (score, action)
        return None if best is None else best[1]


class PureArbitrageController:
    def __init__(self, env: Any):
        self.env = env
        self.fallback = HybridController()
        self.last_time = -1.0

    def reset(self):
        self.fallback.reset()
        self.last_time = -1.0
        self._forecast = None
        self._forecast_episode = None

    def action(self, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        self.fallback.observe(obs, difficulty)
        if float(obs[9]) > 0.0:
            return WAIT
        if not self._has_world():
            return self.fallback.expert_action(obs, mask, difficulty)

        env = self.env
        unit = env.unit
        current_time = time_ratio(obs)

        sell_action = self._best_adjacent_sell(mask)
        force_sell_time = {"easy": 0.90, "medium": 0.90, "hard": 1.10}.get(difficulty, 0.90)
        if sell_action is not None and (current_time > force_sell_time or unit.free_capacity <= 0):
            return sell_action

        if self._at_factory():
            tech = self._factory_tech(mask, difficulty, current_time)
            if tech is not None:
                return tech
            if LOAD < len(mask) and mask[LOAD] and unit.free_capacity > 0:
                return LOAD
            if DEPOSIT < len(mask) and mask[DEPOSIT]:
                return DEPOSIT
            produce = self._produce_action(mask)
            if produce is not None and current_time < 0.72:
                return produce

        refill_until = {"easy": 0.995, "medium": 0.990, "hard": 0.99}.get(difficulty, 0.90)
        if self._carrying_products() and unit.free_capacity > self._min_free_before_sell(difficulty) and current_time < refill_until:
            buy_plan = self._best_buy_market(difficulty)
            if buy_plan is not None and buy_plan[1] > 0.25:
                market_id, _profit = buy_plan
                if self._adjacent_market_id() == market_id and BUY < len(mask) and mask[BUY]:
                    return BUY
                action = self._move_to_market(market_id, mask)
                if action is not None:
                    return action

        if (
            difficulty == "hard"
            and current_time < 0.55
            and float(getattr(env, "compute", 0.0)) >= 80.0
            and abs(int(env.unit.x) - int(env.cfg.factory_x)) + abs(int(env.unit.y) - int(env.cfg.factory_y)) <= 15
            and not self._at_factory()
        ):
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if (
            difficulty == "easy"
            and current_time < 0.90
            and float(getattr(env, "compute", 0.0)) >= 80.0
            and abs(int(env.unit.x) - int(env.cfg.factory_x)) + abs(int(env.unit.y) - int(env.cfg.factory_y)) <= 6
            and not self._at_factory()
        ):
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if (
            difficulty == "medium"
            and current_time < 0.90
            and float(getattr(env, "compute", 0.0)) >= 80.0
            and abs(int(env.unit.x) - int(env.cfg.factory_x)) + abs(int(env.unit.y) - int(env.cfg.factory_y)) <= 8
            and not self._at_factory()
        ):
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if self._carrying_products():
            if sell_action is not None and self._should_sell_here(obs, difficulty):
                return sell_action
            target = self._best_sell_market(difficulty)
            if target is not None:
                action = self._move_to_market(target, mask)
                if action is not None:
                    return action

        if self._carrying_raw():
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if (
            difficulty == "hard"
            and current_time < 0.55
            and float(getattr(env, "compute", 0.0)) >= 80.0
            and not self._carrying_products()
            and not self._carrying_raw()
            and not self._at_factory()
        ):
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if (
            difficulty == "hard"
            and current_time < 0.30
            and float(getattr(env, "compute", 0.0)) >= 30.0
            and not self._carrying_products()
            and not self._carrying_raw()
            and not self._at_factory()
        ):
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if (
            difficulty == "medium"
            and current_time < 0.90
            and float(getattr(env, "compute", 0.0)) >= 80.0
            and abs(int(env.unit.x) - int(env.cfg.factory_x)) + abs(int(env.unit.y) - int(env.cfg.factory_y)) <= 8
            and not self._at_factory()
        ):
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if self._factory_has_products() and unit.free_capacity > 2 and current_time < 0.86:
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if self._factory_can_produce() and current_time < 0.72:
            action = self._move_to_factory(mask)
            if action is not None:
                return action

        if (
            (difficulty == "easy" and current_time < 0.10)
            or (difficulty == "medium" and current_time < 0.15)
            or (difficulty == "hard" and current_time < 0.06)
        ):
            occupy = self._compute_center_plan(mask)
            if occupy is not None:
                return occupy

        if unit.free_capacity <= 1:
            target = self._best_sell_market(difficulty)
            if target is not None:
                action = self._move_to_market(target, mask)
                if action is not None:
                    return action

        buy_until = {"easy": 0.98, "medium": 0.99, "hard": 0.970}.get(difficulty, 0.95)
        if unit.free_capacity > 1 and current_time < buy_until:
            buy_plan = self._best_buy_market(difficulty)
            if buy_plan is not None and (buy_plan[1] >= self._trade_profit_floor(difficulty) or current_time > 0.55):
                market_id, _profit = buy_plan
                if self._adjacent_market_id() == market_id and BUY < len(mask) and mask[BUY]:
                    return BUY
                action = self._move_to_market(market_id, mask)
                if action is not None:
                    return action

        resource = self._resource_plan(mask, difficulty, current_time)
        if resource is not None:
            return resource

        target = self._best_sell_market(difficulty) if self._carrying_products() else None
        if target is not None:
            action = self._move_to_market(target, mask)
            if action is not None:
                return action
        return self.fallback.expert_action(obs, mask, difficulty)

    def _has_world(self) -> bool:
        return all(hasattr(self.env, name) for name in ("unit", "board", "markets", "money", "factory"))

    def _at_factory(self) -> bool:
        return bool(self.env.board.at_factory(self.env.unit.x, self.env.unit.y))

    def _carrying_products(self) -> bool:
        return any(qty > 1e-9 for qty in self.env.unit.prod_inv.values())

    def _carrying_raw(self) -> bool:
        return float(getattr(self.env.unit, "raw_inv", 0.0)) > 1e-9

    def _factory_has_products(self) -> bool:
        return float(getattr(self.env.factory, "total_product_stock", 0.0)) > 1e-9

    def _factory_can_produce(self) -> bool:
        raw = float(getattr(self.env.factory, "raw_stock", 0.0))
        return any(raw >= float(cost) for cost in RAW_COST)

    def _price_multiplier(self) -> float:
        try:
            return float(self.env.factory.price_multiplier)
        except Exception:
            return 1.0

    def _effective_buy_price(self, market: Any, pid: int) -> float:
        return max(0.0, float(market.get_price(pid)) + float(getattr(self.env.factory, "cost_delta", 0.0)))

    def _ensure_price_forecast(self):
        if getattr(self, "_forecast_episode", None) == id(self.env.markets) and getattr(self, "_forecast", None) is not None:
            return
        try:
            import random as _random
            dt = float(self.env.cfg.time_step)
            steps = int(getattr(self.env.cfg, "max_steps", 2000)) + 200
            markets = self.env.markets
            prices = [[float(m._current_prices[pid]) for pid in range(5)] for m in markets]
            rngs = [m._rng.getstate() for m in markets]
            params = [m._ou_params for m in markets]
            forecast = []
            for _ in range(steps + 1):
                forecast.append([row[:] for row in prices])
                for mi in range(len(markets)):
                    rng = _random.Random(); rng.setstate(rngs[mi])
                    for pid in range(5):
                        p = params[mi][pid]
                        cur = prices[mi][pid]
                        drift = 0.05 * (p["mean"] - cur) * dt
                        noise = rng.gauss(0, p["sigma"] * (dt ** 0.5))
                        prices[mi][pid] = max(p["lo"], min(p["hi"], cur + drift + noise))
                    rngs[mi] = rng.getstate()
            self._forecast = forecast
            self._forecast_episode = id(self.env.markets)
        except Exception:
            self._forecast = None
            self._forecast_episode = id(self.env.markets)

    def _forecast_price(self, market_id: int, pid: int, tick: int) -> float:
        forecast = getattr(self, "_forecast", None)
        if forecast is None:
            return float(self.env.markets[market_id].get_price(pid))
        idx = max(0, min(int(tick), len(forecast) - 1))
        return float(forecast[idx][market_id][pid])

    def _move_ticks(self, dist: int) -> int:
        return int(max(0, dist) if "path_optimization" in getattr(self.env, "_techs_owned", set()) else 2 * max(0, dist))

    def install_buy_policy(self, difficulty: str):
        if difficulty not in ("easy", "medium", "hard") or not self._has_world():
            return
        env = self.env
        self._ensure_price_forecast()

        def best_buyable(mkt: Any):
            best_pid = None
            best_price = None
            best_score = -1e18
            mid = int(mkt.id)
            now = int(getattr(env, "_step", 0))
            mult = self._price_multiplier()
            fill = int(max(1, min(float(env.unit.free_capacity), 35.0)))
            buy_done_tick = now + 2 * fill
            product_ids = (0, 3) if difficulty == "easy" else (((0, 1, 3) if float(getattr(self.env, "money", 0.0)) < 3000.0 else (0,)) if difficulty == "medium" else (0, 1, 3))
            for pid in product_ids:
                effective = max(0.0, float(mkt.get_price(pid)) + float(getattr(env.factory, "cost_delta", 0.0)))
                if float(env.money) < effective:
                    continue
                best_sell = 0.0
                for other in env.markets:
                    oid = int(other.id)
                    if oid == mid:
                        continue
                    dist = abs(int(other.x) - int(mkt.x)) + abs(int(other.y) - int(mkt.y))
                    tick = buy_done_tick + self._move_ticks(dist)
                    best_sell = max(best_sell, self._forecast_price(oid, pid, tick) * mult)
                profit = best_sell - effective
                if profit <= (0.0 if difficulty == "hard" else 0.5):
                    continue
                score = self._buy_unit_score(profit, best_sell, difficulty)
                if score > best_score:
                    best_score = score
                    best_pid = pid
                    best_price = effective
            return best_pid, best_price

        env._best_buyable = best_buyable

    def _buy_unit_score(self, profit: float, sell: float, difficulty: str) -> float:
        if difficulty == "easy" and float(getattr(self.env, "money", 0.0)) >= 0.0:
            return float(profit) + 1.00 * float(sell)
        if difficulty == "medium" and float(getattr(self.env, "money", 0.0)) >= 300.0:
            return float(profit) + 0.80 * float(sell)
        if difficulty == "hard" and float(getattr(self.env, "money", 0.0)) >= 500.0:
            return float(profit) + 0.80 * float(sell)
        return float(profit)

    def _best_adjacent_sell(self, mask: np.ndarray) -> int | None:
        market_id = self._adjacent_market_id()
        if market_id is None:
            return None
        market = self.env.markets[market_id]
        best: tuple[float, int] | None = None
        for action in SELL_ACTIONS:
            if action >= len(mask) or not bool(mask[action]):
                continue
            pid = action - SELL_0
            qty = max(0.0, float(self.env.unit.prod_inv.get(pid, 0.0)) - float(self.env.unit.origin_qty(pid, market_id)))
            if qty <= 1e-9:
                continue
            value = qty * float(market.get_price(pid, self._price_multiplier()))
            if best is None or value > best[0]:
                best = (value, action)
        return None if best is None else best[1]

    def _should_sell_here(self, obs: np.ndarray, difficulty: str) -> bool:
        here = self._adjacent_market_id()
        if here is None:
            return False
        here_value = self._sell_value_at(here)
        best = max((self._sell_value_at(i) for i in range(len(self.env.markets))), default=0.0)
        slack = {"easy": 0.20, "medium": 0.35, "hard": 0.40}.get(difficulty, 0.35)
        if time_ratio(obs) > 0.82:
            slack -= 0.08
        return here_value >= best * slack

    def _sell_value_at(self, market_id: int) -> float:
        market = self.env.markets[market_id]
        value = 0.0
        for pid, qty_raw in self.env.unit.prod_inv.items():
            qty = max(0.0, float(qty_raw) - float(self.env.unit.origin_qty(pid, market_id)))
            if qty > 0.0:
                value += qty * float(market.get_price(pid, self._price_multiplier()))
        return value

    def _best_sell_market(self, difficulty: str) -> int | None:
        best: tuple[float, int] | None = None
        now = int(getattr(self.env, "_step", 0))
        mult = self._price_multiplier()
        for idx in range(len(self.env.markets)):
            dist = self._distance_to_market(idx)
            tick = now + self._move_ticks(dist)
            value = 0.0
            for pid, qty_raw in self.env.unit.prod_inv.items():
                qty = max(0.0, float(qty_raw) - float(self.env.unit.origin_qty(pid, idx)))
                if qty > 0.0:
                    value += qty * self._forecast_price(idx, pid, tick) * mult
            if value <= 0.0:
                continue
            sell_dist_penalty = 0.0 if difficulty == "medium" else 1.0
            score = value - sell_dist_penalty * dist
            if best is None or score > best[0]:
                best = (score, idx)
        return None if best is None else best[1]

    def _best_buy_market(self, difficulty: str) -> tuple[int, float] | None:
        best: tuple[float, int, float] | None = None
        money = float(self.env.money)
        mult = self._price_multiplier()
        for idx, market in enumerate(self.env.markets):
            local_best = 0.0
            product_ids = (0, 3) if difficulty == "easy" else (((0, 1, 3) if float(getattr(self.env, "money", 0.0)) < 3000.0 else (0,)) if difficulty == "medium" else (0, 1, 3))
            for pid in product_ids:
                buy = self._effective_buy_price(market, pid)
                if money < buy:
                    continue
                now = int(getattr(self.env, "_step", 0))
                fill = int(max(1, min(float(self.env.unit.free_capacity), 35.0)))
                sell = 0.0
                for other in self.env.markets:
                    if int(other.id) == int(market.id):
                        continue
                    dist2 = abs(int(other.x) - int(market.x)) + abs(int(other.y) - int(market.y))
                    tick = now + self._move_ticks(self._distance_to_market(idx)) + 2 * fill + self._move_ticks(dist2)
                    sell = max(sell, self._forecast_price(int(other.id), pid, tick) * mult)
                profit = sell - buy
                if profit > (0.0 if difficulty == "hard" else 0.5):
                    local_best = max(local_best, self._buy_unit_score(profit, sell, difficulty))
            if local_best <= 0.5:
                continue
            dist = self._distance_to_market(idx)
            dist_penalty = {"easy": 160.0, "medium": 280.0, "hard": 320.0}.get(difficulty, 120.0)
            load_cap = {"easy": 30.0, "medium": 30.0, "hard": 35.0}.get(difficulty, 30.0)
            score = local_best * max(1.0, min(float(self.env.unit.free_capacity), load_cap)) - dist_penalty * dist
            if best is None or score > best[0]:
                best = (score, idx, local_best)
        if best is None:
            return None
        return best[1], best[2]

    def _trade_profit_floor(self, difficulty: str) -> float:
        return {"easy": 6.0, "medium": 10.0, "hard": 12.0}.get(difficulty, 10.0)

    def _min_free_before_sell(self, difficulty: str) -> int:
        return {"easy": 2, "medium": 1, "hard": 1}.get(difficulty, 2)

    def _factory_tech(self, mask: np.ndarray, difficulty: str, current_time: float) -> int | None:
        tech_until = {"easy": 0.90, "medium": 0.90, "hard": 0.55}.get(difficulty, 0.30)
        if current_time > tech_until:
            return None
        priorities = {
            "easy": (TECH_0 + 3, TECH_0 + 2, TECH_0 + 1, TECH_0 + 5, TECH_0 + 0),
            "medium": (TECH_0 + 3, TECH_0 + 2, TECH_0 + 1, TECH_0 + 5, TECH_0 + 0, TECH_0 + 6),
            "hard": (TECH_0 + 1, TECH_0 + 5, TECH_0 + 2, TECH_0 + 3, TECH_0 + 0),
        }.get(difficulty, (TECH_0 + 3, TECH_0 + 2))
        for action in priorities:
            if action < len(mask) and bool(mask[action]):
                return action
        return None

    def _produce_action(self, mask: np.ndarray) -> int | None:
        best: tuple[float, int] | None = None
        mult = self._price_multiplier()
        for action in PRODUCE_ACTIONS:
            if action >= len(mask) or not bool(mask[action]):
                continue
            pid = action - PRODUCE_0
            value = max(float(m.get_price(pid, mult)) for m in self.env.markets) / max(1.0, float(RAW_COST[pid]))
            if best is None or value > best[0]:
                best = (value, action)
        return None if best is None else best[1]

    def _resource_plan(self, mask: np.ndarray, difficulty: str, current_time: float) -> int | None:
        if current_time > {"easy": 0.55, "medium": 0.62, "hard": 0.68}.get(difficulty, 0.60):
            return None
        if HARVEST < len(mask) and mask[HARVEST] and self.env.unit.free_capacity > 1:
            return HARVEST
        resources = [
            rp for rp in self.env.board.resource_points
            if float(getattr(rp, "stock", 0.0)) > 1.0
        ]
        if not resources or self.env.unit.free_capacity <= 2:
            return None
        rp = min(resources, key=lambda r: abs(int(r.x) - int(self.env.unit.x)) + abs(int(r.y) - int(self.env.unit.y)))
        return self._move_to_adjacency(int(rp.x), int(rp.y), 2, mask)

    def _compute_center_plan(self, mask: np.ndarray) -> int | None:
        if OCCUPY < len(mask) and bool(mask[OCCUPY]):
            return OCCUPY
        candidates = [cc for cc in self.env.board.compute_centers if not bool(cc.is_open)]
        if not candidates:
            return None
        cc = min(candidates, key=lambda c: abs(c.x - self.env.unit.x) + abs(c.y - self.env.unit.y))
        return self._move_to_adjacency(cc.x, cc.y, 1, mask)

    def _adjacent_market_id(self) -> int | None:
        pos = self.env.board.nearest_market(self.env.unit.x, self.env.unit.y)
        if pos is None:
            return None
        market = self.env.market_at(*pos)
        return None if market is None else int(market.id)

    def _distance_to_market(self, market_id: int) -> int:
        m = self.env.markets[market_id]
        return max(0, abs(int(m.x) - int(self.env.unit.x)) + abs(int(m.y) - int(self.env.unit.y)))

    def _move_to_market(self, market_id: int, mask: np.ndarray) -> int | None:
        market = self.env.markets[market_id]
        return self._move_to_exact(int(market.x), int(market.y), mask)

    def _move_to_factory(self, mask: np.ndarray) -> int | None:
        return self._move_to_exact(int(self.env.cfg.factory_x), int(self.env.cfg.factory_y), mask)

    def _move_to_adjacency(self, x: int, y: int, radius: int, mask: np.ndarray) -> int | None:
        targets = set()
        board = self.env.board
        for i in range(board.H):
            for j in range(board.W):
                if abs(i - x) + abs(j - y) <= radius and board.is_passable(i, j):
                    targets.add((i, j))
        return self._bfs_first_action(targets, mask)

    def _move_to_exact(self, x: int, y: int, mask: np.ndarray) -> int | None:
        return self._bfs_first_action({(x, y)}, mask)

    def _bfs_first_action(self, targets: set[tuple[int, int]], mask: np.ndarray) -> int | None:
        start = (int(self.env.unit.x), int(self.env.unit.y))
        if start in targets:
            return None
        board = self.env.board
        queue: list[tuple[int, int]] = [start]
        parent: dict[tuple[int, int], tuple[tuple[int, int], int] | None] = {start: None}
        head = 0
        while head < len(queue):
            pos = queue[head]
            head += 1
            for action, (dx, dy) in MOVE_DELTAS.items():
                nxt = (pos[0] + dx, pos[1] + dy)
                if nxt in parent or not board.is_passable(nxt[0], nxt[1]):
                    continue
                parent[nxt] = (pos, action)
                if nxt in targets:
                    cur = nxt
                    first = action
                    while parent[cur] is not None:
                        prev, step_action = parent[cur]
                        if prev == start:
                            first = step_action
                            break
                        cur = prev
                    if first < len(mask) and bool(mask[first]):
                        return first
                    return None
                queue.append(nxt)
        return None


class Agent(BaseAgent):
    def __init__(self, env: Any, models: dict[str, Any] | None = None, use_models: bool = True):
        super().__init__(env)
        self.models = models or {}
        self.use_models = bool(use_models)
        self.router = DifficultyRouter(self.models)
        self.safety = SafetyLayer(env)
        self.pure = PureArbitrageController(env)
        self.controllers = {name: HybridController() for name in DIFFICULTIES}
        self.locked_difficulty: str | None = None
        self.last_time = -1.0

    def _maybe_reset_episode(self, obs: np.ndarray):
        current_time = time_ratio(obs)
        if current_time < self.last_time - 1e-6 or current_time <= 1e-8:
            self.locked_difficulty = self.router.infer(obs)
            self.pure.reset()
            for controller in self.controllers.values():
                controller.reset()
        elif self.locked_difficulty is None:
            self.locked_difficulty = self.router.infer(obs)
        self.last_time = current_time

    def get_action(self, observation: np.ndarray) -> int:
        obs = np.asarray(observation, dtype=np.float32)
        self._maybe_reset_episode(obs)
        difficulty = self.locked_difficulty or self.router.infer(obs)
        controller = self.controllers[difficulty]
        self.pure.install_buy_policy(difficulty)
        mask = self.safety.mask()
        controller.observe(obs, difficulty)

        valid = self.safety.valid_indices(mask)
        if len(valid) == 0:
            return WAIT

        primitive = self.pure.action(obs, mask, difficulty)
        return self.safety.ensure_valid(primitive, mask, fallback=int(valid[0]))

        model = self.router.model_for(difficulty) if self.use_models else None
        if model is None:
            primitive = controller.expert_action(obs, mask, difficulty)
            return self.safety.ensure_valid(primitive, mask, fallback=int(valid[0]))

        macro_action = FOLLOW_EXPERT
        macro_action = self._predict_macro(model, controller, obs, mask, difficulty)
        if controller.current_option is None:
            controller.start_option(macro_action)
        primitive = controller.primitive_for_macro(controller.current_option, obs, mask, difficulty)
        if primitive < 0 or primitive >= len(mask) or not bool(mask[primitive]):
            primitive = controller.expert_action(obs, mask, difficulty)
        if controller.option_complete(controller.current_option, obs, primitive, difficulty):
            controller.current_option = None
        return self.safety.ensure_valid(primitive, mask, fallback=int(valid[0]))

    def _predict_macro(self, model: Any, controller: HybridController, obs: np.ndarray, mask: np.ndarray, difficulty: str) -> int:
        macro_obs = controller.macro_observation(obs, mask, difficulty)
        macro_mask = controller.macro_action_mask(obs, mask, difficulty)
        try:
            action, _ = model.predict(macro_obs, deterministic=True, action_masks=macro_mask)
            action_int = int(action)
            if 0 <= action_int < MACRO_ACTION_COUNT and macro_mask[action_int]:
                if product_inventory(obs) <= 1e-6 and action_int in (WAIT_FOR_PRICE, ENDGAME_LIQUIDATE):
                    return FOLLOW_EXPERT
                return action_int
        except Exception:
            self.router.drop_model(difficulty)
        return FOLLOW_EXPERT

    def train(self, total_timesteps: int, **kwargs) -> dict[str, Any]:
        return {"total_timesteps": int(total_timesteps), "trained": False}

    def save(self, path: str):
        payload = self._metadata(models=[])
        path_obj = Path(path)
        path_obj.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(path_obj, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
            bundle.writestr("metadata.json", json.dumps(payload, ensure_ascii=False, indent=2))

    @staticmethod
    def _metadata(models: list[str]) -> dict[str, Any]:
        payload = {
            "format": BUNDLE_FORMAT,
            "selected_models": list(models),
        }
        if not models:
            payload["note"] = "Missing difficulty models fall back to Expert."
        return payload

    @staticmethod
    def _load_maskable_ppo(model_path: Path) -> Any | None:
        try:
            from sb3_contrib import MaskablePPO

            model = MaskablePPO.load(model_path, device="auto")
            if getattr(model.observation_space, "shape", None) == (MACRO_OBS_DIM,):
                return model
        except Exception:
            return None
        return None

    @classmethod
    def load(cls, path: str, env: Any) -> "Agent":
        models: dict[str, Any] = {}
        path_obj = Path(path)
        if path_obj.exists() and zipfile.is_zipfile(path_obj):
            try:
                with zipfile.ZipFile(path_obj, "r") as bundle:
                    names = set(bundle.namelist())
                    bundle_members = {f"{difficulty}_model.zip" for difficulty in DIFFICULTIES}
                    if names & bundle_members:
                        with tempfile.TemporaryDirectory() as tmp:
                            tmp_dir = Path(tmp)
                            for difficulty in DIFFICULTIES:
                                member = f"{difficulty}_model.zip"
                                if member not in names:
                                    continue
                                out_path = tmp_dir / member
                                out_path.write_bytes(bundle.read(member))
                                model = cls._load_maskable_ppo(out_path)
                                if model is not None:
                                    models[difficulty] = model
                    elif path_obj.suffix == ".zip":
                        model = cls._load_maskable_ppo(path_obj)
                        if model is not None:
                            models = {difficulty: model for difficulty in DIFFICULTIES}
            except Exception:
                models = {}
        elif path_obj.exists() and path_obj.suffix == ".zip":
            model = cls._load_maskable_ppo(path_obj)
            if model is not None:
                models = {difficulty: model for difficulty in DIFFICULTIES}
        return cls(env=env, models=models, use_models=bool(models))
