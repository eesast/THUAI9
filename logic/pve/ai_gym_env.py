"""Gymnasium adapter for the game-rule layer."""

from __future__ import annotations

import math

import gymnasium as gym
import numpy as np
from gymnasium import spaces

from config.setting import *
from game_core import GameEnv


OBS_VECTOR_SIZE = 52


class AI9GymEnv(gym.Env):
    """RL-facing environment.

    The wrapper only consumes GameEnv's public observation and step-result
    interface. Algorithms should not depend on game internals.
    """

    metadata = {"render_modes": ["console"]}

    def __init__(self, randomize: bool = True, max_steps: int = 1000):
        super().__init__()
        self.game = GameEnv(randomize=randomize)
        self.action_space = spaces.Discrete(U_ACT_HARVEST + 1)
        self.observation_space = spaces.Box(low=-1.0, high=1.0, shape=(OBS_VECTOR_SIZE,), dtype=np.float32)
        self.max_steps = max_steps
        self.current_step = 0

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        public_obs = self.game.reset(seed=seed)
        self.current_step = 0
        return self._encode_obs(public_obs), {"public_observation": public_obs}

    def step(self, action):
        public_obs = self.game.step(int(action))
        self.current_step += 1

        last_step = public_obs["last_step"]
        reward = -0.002
        if not last_step["valid"]:
            reward -= 0.01
        reward += 0.1 * last_step["realized_profit"]

        terminated = False
        truncated = self.current_step >= self.max_steps
        if truncated:
            reward += 0.01 * (public_obs["net_worth"] - INITIAL_MONEY)

        info = {
            "public_observation": public_obs,
            "net_worth": public_obs["net_worth"],
            "valid_actions": public_obs["valid_actions"],
            "transactions": public_obs["transactions"],
        }
        return self._encode_obs(public_obs), reward, terminated, truncated, info

    def _encode_obs(self, public_obs: dict) -> np.ndarray:
        unit = public_obs["unit"]
        ux, uy = unit["pos"]
        features = [
            ux / max(1, MAP_HEIGHT - 1),
            uy / max(1, MAP_WIDTH - 1),
            min(1.0, unit["busy_ticks"] / 10.0),
            unit["total_load"] / UNIT_CAPACITY,
            min(1.0, np.log10(max(1.0, public_obs["money"])) / 5.0),
            unit["inventory"].get(PRODUCT_SEMICONDUCTOR, 0) / UNIT_CAPACITY,
            sum(unit["resources"].values()) / UNIT_CAPACITY,
            math.sin(2 * math.pi * (public_obs["time"] % 100.0) / 100.0),
            math.cos(2 * math.pi * (public_obs["time"] % 100.0) / 100.0),
        ]

        for market in public_obs["markets"][:MARKET_COUNT]:
            mx, my = market["pos"]
            buy_norm = self._norm_price(market["buy_price"])
            sell_norm = self._norm_price(market["sell_price"])
            can_buy = 1.0 if public_obs["valid_actions"][U_ACT_LOAD_0] and market["nearby"] else 0.0
            can_sell = 1.0 if public_obs["valid_actions"][U_ACT_SELL_ALL] and market["nearby"] else 0.0
            features.extend(
                [
                    (mx - ux) / MAP_HEIGHT,
                    (my - uy) / MAP_WIDTH,
                    buy_norm,
                    sell_norm,
                    market["origin_inventory"] / UNIT_CAPACITY,
                    market["stock"] / max(1.0, market["max_stock"]),
                    market["demand"] / max(1.0, market["max_demand"]),
                    can_buy,
                    can_sell,
                ]
            )

        while len(features) < 9 + MARKET_COUNT * 9:
            features.extend([0.0] * 9)

        for resource in public_obs["resources"][:RESOURCE_COUNT]:
            rx, ry = resource["pos"]
            features.extend(
                [
                    (rx - ux) / MAP_HEIGHT,
                    (ry - uy) / MAP_WIDTH,
                    min(1.0, resource["stock"] / max(1.0, resource["max_stock"])),
                    1.0 if resource["nearby"] and public_obs["valid_actions"][U_ACT_HARVEST] else 0.0,
                ]
            )

        while len(features) < 9 + MARKET_COUNT * 9 + RESOURCE_COUNT * 4:
            features.extend([0.0] * 4)

        features.extend(1.0 if is_valid else 0.0 for is_valid in public_obs["valid_actions"])

        if len(features) != OBS_VECTOR_SIZE:
            raise RuntimeError(f"Observation size mismatch: {len(features)} != {OBS_VECTOR_SIZE}")

        return np.array(features, dtype=np.float32)

    def _norm_price(self, price: float) -> float:
        base, top = PRODUCTS[PRODUCT_SEMICONDUCTOR]["val_range"]
        low = base * MARKET_PRICE_SCALE_MIN * (1 - MARKET_SPREAD_RATE)
        high = top * MARKET_PRICE_SCALE_MAX * (1 + MARKET_SPREAD_RATE)
        return float(np.clip((price - low) / (high - low), 0.0, 1.0))

    def render(self):
        obs = self.game.get_public_observation()
        print(
            f"Step: {self.current_step}, Money: {obs['money']:.2f}, "
            f"NetWorth: {obs['net_worth']:.2f}, Tx: {obs['transactions']}"
        )
