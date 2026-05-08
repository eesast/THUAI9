#!/usr/bin/env python
"""Public-interface scripted baseline for sanity-checking game difficulty."""

from __future__ import annotations

import argparse
import csv
from collections import deque
from pathlib import Path

import numpy as np

from ai_gym_env import AI9GymEnv
from config.setting import *


MOVE_ACTIONS = {
    U_ACT_MOVE_UP: (-1, 0),
    U_ACT_MOVE_DOWN: (1, 0),
    U_ACT_MOVE_LEFT: (0, -1),
    U_ACT_MOVE_RIGHT: (0, 1),
}


class ScriptedTrader:
    def __init__(self, buy_threshold: float = 78.0, sell_threshold: float = 94.0, target_inventory: int = 6):
        self.buy_threshold = buy_threshold
        self.sell_threshold = sell_threshold
        self.target_inventory = target_inventory

    def predict(self, public_obs: dict) -> int:
        valid = public_obs["valid_actions"]
        unit = public_obs["unit"]
        inventory = unit["inventory"].get(PRODUCT_SEMICONDUCTOR, 0)

        if unit["busy_ticks"] > 0:
            return U_ACT_WAIT

        nearby_markets = [market for market in public_obs["markets"] if market["nearby"]]
        if nearby_markets:
            market = nearby_markets[0]
            if valid[U_ACT_SELL_ALL] and inventory > 0 and market["sell_price"] >= self.sell_threshold:
                return U_ACT_SELL_ALL
            if valid[U_ACT_LOAD_0] and inventory < self.target_inventory and market["buy_price"] <= self.buy_threshold:
                return U_ACT_LOAD_0

        target = self._choose_target(public_obs, inventory)
        if target is None:
            return U_ACT_WAIT

        return self._next_move(public_obs, target)

    def _choose_target(self, public_obs: dict, inventory: int) -> tuple[int, int] | None:
        markets = public_obs["markets"]
        if not markets:
            return None

        if inventory > 0:
            sellable_markets = [m for m in markets if m["origin_inventory"] < inventory]
            if sellable_markets:
                best = max(sellable_markets, key=lambda m: m["sell_price"])
                return best["pos"]

        best = min(markets, key=lambda m: m["buy_price"])
        return best["pos"]

    def _next_move(self, public_obs: dict, target: tuple[int, int]) -> int:
        valid = public_obs["valid_actions"]
        start = tuple(public_obs["unit"]["pos"])
        grid = public_obs["map_grid"]
        height = public_obs["map_height"]
        width = public_obs["map_width"]

        if abs(start[0] - target[0]) + abs(start[1] - target[1]) <= 1:
            return U_ACT_WAIT

        queue = deque([(start, None)])
        seen = {start}

        while queue:
            (x, y), first_action = queue.popleft()
            if abs(x - target[0]) + abs(y - target[1]) <= 1:
                return first_action if first_action is not None and valid[first_action] else U_ACT_WAIT

            for action, (dx, dy) in MOVE_ACTIONS.items():
                nx, ny = x + dx, y + dy
                if not (0 <= nx < height and 0 <= ny < width):
                    continue
                if grid[nx][ny] == GRID_TYPE_OBSTACLE or (nx, ny) in seen:
                    continue
                seen.add((nx, ny))
                queue.append(((nx, ny), action if first_action is None else first_action))

        return U_ACT_WAIT


def evaluate(episodes: int, seed: int, max_steps: int) -> list[dict]:
    policy = ScriptedTrader()
    results = []

    for episode in range(episodes):
        env = AI9GymEnv(randomize=True, max_steps=max_steps)
        _obs, info = env.reset(seed=seed + episode)
        public_obs = info["public_observation"]
        total_reward = 0.0

        for _step in range(max_steps):
            action = policy.predict(public_obs)
            _obs, reward, terminated, truncated, info = env.step(action)
            public_obs = info["public_observation"]
            total_reward += reward
            if terminated or truncated:
                break

        results.append(
            {
                "episode": episode + 1,
                "money": public_obs["money"],
                "net_worth": public_obs["net_worth"],
                "reward": total_reward,
                "transactions": public_obs["transactions"],
            }
        )

    return results


def main() -> None:
    parser = argparse.ArgumentParser(description="Evaluate a public-interface scripted baseline.")
    parser.add_argument("--episodes", type=int, default=50)
    parser.add_argument("--seed", type=int, default=50_000)
    parser.add_argument("--max-steps", type=int, default=1000)
    parser.add_argument("--out", default="plots_random_rules_dqn_200k_multiseed/heuristic_summary.csv")
    args = parser.parse_args()

    results = evaluate(args.episodes, args.seed, args.max_steps)
    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    with out_path.open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=["episode", "money", "net_worth", "reward", "transactions"])
        writer.writeheader()
        writer.writerows(results)

    money = np.array([r["money"] for r in results], dtype=float)
    rewards = np.array([r["reward"] for r in results], dtype=float)
    tx = np.array([r["transactions"] for r in results], dtype=float)
    print(f"episodes={len(results)}")
    print(f"money={money.mean():.2f} +/- {money.std():.2f}")
    print(f"reward={rewards.mean():.2f} +/- {rewards.std():.2f}")
    print(f"transactions={tx.mean():.2f}")
    print(f"saved={out_path.resolve()}")


if __name__ == "__main__":
    main()
