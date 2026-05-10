#!/usr/bin/env python
"""Generate evaluation plots for the trained PPO policy."""

from __future__ import annotations

import argparse
import csv
from collections import Counter
from pathlib import Path

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np
from stable_baselines3 import PPO

from ai_gym_env import AI9GymEnv
from config.setting import PRODUCT_SEMICONDUCTOR


ACTION_NAMES = {
    0: "wait",
    1: "up",
    2: "down",
    3: "left",
    4: "right",
    5: "buy",
    6: "sell",
    7: "harvest",
}


def run_policy(model_path: str, steps: int) -> dict[str, np.ndarray | list[dict]]:
    model = PPO.load(model_path)
    env = AI9GymEnv()
    obs, info = env.reset()
    public_obs = info["public_observation"]

    rows = []
    total_reward = 0.0
    prev_money = public_obs["money"]

    for step in range(1, steps + 1):
        action, _ = model.predict(obs, deterministic=True)
        action = int(action)

        obs, reward, terminated, truncated, info = env.step(action)
        public_obs = info["public_observation"]
        unit = public_obs["unit"]
        total_reward += reward

        price = public_obs["markets"][0]["price"] if public_obs["markets"] else 0.0
        money_delta = public_obs["money"] - prev_money
        prev_money = public_obs["money"]

        rows.append(
            {
                "step": step,
                "time": public_obs["time"],
                "action": action,
                "action_name": ACTION_NAMES.get(action, str(action)),
                "x": unit["pos"][0],
                "y": unit["pos"][1],
                "money": public_obs["money"],
                "money_delta": money_delta,
                "reward": reward,
                "cum_reward": total_reward,
                "inventory": unit["inventory"].get(PRODUCT_SEMICONDUCTOR, 0),
                "busy_ticks": unit["busy_ticks"],
                "price": price,
            }
        )

        if terminated or truncated:
            break

    return {
        "rows": rows,
        "money": np.array([r["money"] for r in rows], dtype=float),
        "cum_reward": np.array([r["cum_reward"] for r in rows], dtype=float),
        "reward": np.array([r["reward"] for r in rows], dtype=float),
        "actions": np.array([r["action"] for r in rows], dtype=int),
        "prices": np.array([r["price"] for r in rows], dtype=float),
        "inventory": np.array([r["inventory"] for r in rows], dtype=float),
    }


def run_random_baseline(episodes: int, steps: int, seed: int) -> dict[str, np.ndarray]:
    money_curves = []
    reward_curves = []

    for episode in range(episodes):
        env = AI9GymEnv()
        env.action_space.seed(seed + episode)
        obs, info = env.reset(seed=seed + episode)
        public_obs = info["public_observation"]

        money = []
        rewards = []
        total_reward = 0.0

        for _ in range(steps):
            action = env.action_space.sample()
            obs, reward, terminated, truncated, info = env.step(action)
            public_obs = info["public_observation"]
            total_reward += reward
            money.append(public_obs["money"])
            rewards.append(total_reward)
            if terminated or truncated:
                break

        if len(money) < steps:
            money.extend([money[-1]] * (steps - len(money)))
            rewards.extend([rewards[-1]] * (steps - len(rewards)))

        money_curves.append(money)
        reward_curves.append(rewards)

    return {
        "money": np.array(money_curves, dtype=float),
        "cum_reward": np.array(reward_curves, dtype=float),
    }


def write_trace(rows: list[dict], path: Path) -> None:
    if not rows:
        return

    with path.open("w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)


def save_money_reward_plot(policy: dict, random_baseline: dict, out_dir: Path) -> None:
    steps = np.arange(1, len(policy["money"]) + 1)
    random_money = random_baseline["money"]
    random_reward = random_baseline["cum_reward"]

    fig, axes = plt.subplots(2, 1, figsize=(11, 8), sharex=True)

    axes[0].plot(steps, policy["money"], label="PPO deterministic", color="#1f77b4", linewidth=2.2)
    axes[0].plot(steps, random_money.mean(axis=0), label="Random mean", color="#7f7f7f", linewidth=1.8)
    axes[0].fill_between(
        steps,
        random_money.mean(axis=0) - random_money.std(axis=0),
        random_money.mean(axis=0) + random_money.std(axis=0),
        color="#7f7f7f",
        alpha=0.18,
        label="Random +/- 1 std",
    )
    axes[0].set_ylabel("Money")
    axes[0].set_title("Money Curve")
    axes[0].legend(loc="upper left")
    axes[0].grid(alpha=0.25)

    axes[1].plot(steps, policy["cum_reward"], label="PPO deterministic", color="#2ca02c", linewidth=2.2)
    axes[1].plot(steps, random_reward.mean(axis=0), label="Random mean", color="#7f7f7f", linewidth=1.8)
    axes[1].fill_between(
        steps,
        random_reward.mean(axis=0) - random_reward.std(axis=0),
        random_reward.mean(axis=0) + random_reward.std(axis=0),
        color="#7f7f7f",
        alpha=0.18,
    )
    axes[1].set_xlabel("Step")
    axes[1].set_ylabel("Cumulative reward")
    axes[1].set_title("Cumulative Reward Curve")
    axes[1].legend(loc="upper left")
    axes[1].grid(alpha=0.25)

    fig.tight_layout()
    fig.savefig(out_dir / "money_reward_curves.png", dpi=180)
    plt.close(fig)


def save_trade_plot(policy: dict, out_dir: Path) -> None:
    steps = np.arange(1, len(policy["money"]) + 1)
    actions = policy["actions"]
    money_delta = np.array([row["money_delta"] for row in policy["rows"]], dtype=float)

    buy_steps = steps[(actions == 5) & (money_delta < 0)]
    sell_steps = steps[(actions == 6) & (money_delta > 0)]

    fig, axes = plt.subplots(2, 1, figsize=(11, 8), sharex=True)

    axes[0].plot(steps, policy["prices"], color="#9467bd", linewidth=2)
    axes[0].scatter(buy_steps, policy["prices"][buy_steps - 1], color="#d62728", s=18, label="Executed buy")
    axes[0].scatter(sell_steps, policy["prices"][sell_steps - 1], color="#2ca02c", s=18, label="Executed sell")
    axes[0].set_ylabel("Semiconductor price")
    axes[0].set_title("Price and Executed Trades")
    axes[0].legend(loc="upper right")
    axes[0].grid(alpha=0.25)

    axes[1].plot(steps, policy["inventory"], color="#ff7f0e", linewidth=1.8)
    axes[1].set_xlabel("Step")
    axes[1].set_ylabel("Inventory")
    axes[1].set_title("Inventory Held by Unit")
    axes[1].grid(alpha=0.25)

    fig.tight_layout()
    fig.savefig(out_dir / "trade_timeline.png", dpi=180)
    plt.close(fig)


def save_action_plot(policy: dict, out_dir: Path) -> None:
    counts = Counter(int(a) for a in policy["actions"])
    labels = [ACTION_NAMES[i] for i in range(8)]
    values = [counts.get(i, 0) for i in range(8)]

    fig, ax = plt.subplots(figsize=(9, 5))
    bars = ax.bar(
        labels,
        values,
        color=["#8c8c8c", "#4e79a7", "#4e79a7", "#4e79a7", "#4e79a7", "#f28e2b", "#59a14f", "#b07aa1"],
    )
    ax.set_ylabel("Count")
    ax.set_title("Action Distribution in One PPO Evaluation Episode")
    ax.grid(axis="y", alpha=0.25)

    for bar in bars:
        height = bar.get_height()
        ax.annotate(
            f"{int(height)}",
            xy=(bar.get_x() + bar.get_width() / 2, height),
            xytext=(0, 3),
            textcoords="offset points",
            ha="center",
            va="bottom",
            fontsize=9,
        )

    fig.tight_layout()
    fig.savefig(out_dir / "action_distribution.png", dpi=180)
    plt.close(fig)


def save_summary_plot(policy: dict, random_baseline: dict, out_dir: Path) -> None:
    steps = np.arange(1, len(policy["money"]) + 1)
    random_money = random_baseline["money"]
    actions = policy["actions"]
    counts = Counter(int(a) for a in actions)

    fig, axes = plt.subplots(2, 2, figsize=(13, 9))

    axes[0, 0].plot(steps, policy["money"], color="#1f77b4", linewidth=2.2, label="PPO")
    axes[0, 0].plot(steps, random_money.mean(axis=0), color="#7f7f7f", linewidth=1.7, label="Random mean")
    axes[0, 0].fill_between(
        steps,
        random_money.mean(axis=0) - random_money.std(axis=0),
        random_money.mean(axis=0) + random_money.std(axis=0),
        color="#7f7f7f",
        alpha=0.18,
    )
    axes[0, 0].set_title("Money")
    axes[0, 0].grid(alpha=0.25)
    axes[0, 0].legend()

    axes[0, 1].plot(steps, policy["cum_reward"], color="#2ca02c", linewidth=2)
    axes[0, 1].set_title("Cumulative Reward")
    axes[0, 1].grid(alpha=0.25)

    axes[1, 0].plot(steps, policy["prices"], color="#9467bd", linewidth=2)
    axes[1, 0].set_title("Market Price")
    axes[1, 0].set_xlabel("Step")
    axes[1, 0].grid(alpha=0.25)

    labels = [ACTION_NAMES[i] for i in range(8)]
    values = [counts.get(i, 0) for i in range(8)]
    axes[1, 1].bar(labels, values, color="#f28e2b")
    axes[1, 1].set_title("Action Counts")
    axes[1, 1].set_xlabel("Action")
    axes[1, 1].grid(axis="y", alpha=0.25)

    fig.suptitle("PPO Evaluation Summary", fontsize=15)
    fig.tight_layout(rect=(0, 0, 1, 0.96))
    fig.savefig(out_dir / "evaluation_summary.png", dpi=180)
    plt.close(fig)


def main() -> None:
    parser = argparse.ArgumentParser(description="Visualize a trained PPO policy.")
    parser.add_argument("--model", default="models/ppo_ai9_basic", help="Model path without .zip")
    parser.add_argument("--out-dir", default="plots", help="Directory for generated plots")
    parser.add_argument("--steps", type=int, default=1000)
    parser.add_argument("--random-episodes", type=int, default=20)
    parser.add_argument("--seed", type=int, default=2026)
    args = parser.parse_args()

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    policy = run_policy(args.model, args.steps)
    random_baseline = run_random_baseline(args.random_episodes, len(policy["money"]), args.seed)

    write_trace(policy["rows"], out_dir / "ppo_eval_trace.csv")
    save_money_reward_plot(policy, random_baseline, out_dir)
    save_trade_plot(policy, out_dir)
    save_action_plot(policy, out_dir)
    save_summary_plot(policy, random_baseline, out_dir)

    print(f"Saved plots to {out_dir.resolve()}")
    print(f"Final money: {policy['money'][-1]:.2f}")
    print(f"Final cumulative reward: {policy['cum_reward'][-1]:.2f}")
    print(f"Trace CSV: {(out_dir / 'ppo_eval_trace.csv').resolve()}")


if __name__ == "__main__":
    main()
