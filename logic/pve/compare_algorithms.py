#!/usr/bin/env python
"""Train and compare several RL algorithms on the current AI9 Gym env."""

from __future__ import annotations

import argparse
import csv
from collections import Counter
from pathlib import Path
from typing import Callable

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np
from stable_baselines3 import A2C, DQN, PPO
from stable_baselines3.common.base_class import BaseAlgorithm
from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.utils import set_random_seed

from ai_gym_env import AI9GymEnv


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


class QuietEvalCallback(BaseCallback):
    def __init__(
        self,
        name: str,
        eval_freq: int = 10_000,
        best_path: Path | None = None,
        eval_episodes: int = 3,
    ):
        super().__init__()
        self.name = name
        self.eval_freq = eval_freq
        self.best_path = best_path
        self.eval_episodes = eval_episodes
        self.best_money = -np.inf

    def _on_step(self) -> bool:
        if self.n_calls % self.eval_freq == 0:
            results = evaluate_policy(self.model, episodes=self.eval_episodes, deterministic=True)
            final_money = float(np.mean([result["final_money"] for result in results]))
            total_reward = float(np.mean([result["total_reward"] for result in results]))
            transactions = float(np.mean([result["transactions"] for result in results]))
            print(
                f"[{self.name} step={self.n_calls}] "
                f"reward={total_reward:.2f} money={final_money:.2f} tx={transactions:.2f}"
            )
            if self.best_path is not None and final_money > self.best_money:
                self.best_money = final_money
                self.model.save(str(self.best_path))
                print(f"[{self.name} step={self.n_calls}] saved best money={self.best_money:.2f}")
        return True


def make_env(seed: int | None = None) -> AI9GymEnv:
    env = AI9GymEnv()
    if seed is not None:
        env.reset(seed=seed)
        env.action_space.seed(seed)
    return env


def build_model(algo: str, seed: int) -> BaseAlgorithm:
    env = make_env(seed)

    if algo == "ppo":
        return PPO(
            "MlpPolicy",
            env,
            learning_rate=3e-4,
            n_steps=2048,
            batch_size=64,
            gamma=0.9,
            ent_coef=0.05,
            seed=seed,
            device="cpu",
            verbose=0,
        )

    if algo == "dqn":
        return DQN(
            "MlpPolicy",
            env,
            learning_rate=1e-3,
            buffer_size=50_000,
            learning_starts=1_000,
            batch_size=64,
            gamma=0.95,
            train_freq=4,
            gradient_steps=1,
            exploration_fraction=0.35,
            exploration_initial_eps=1.0,
            exploration_final_eps=0.05,
            target_update_interval=1_000,
            seed=seed,
            device="cpu",
            verbose=0,
        )

    if algo == "a2c":
        return A2C(
            "MlpPolicy",
            env,
            learning_rate=7e-4,
            n_steps=64,
            gamma=0.9,
            ent_coef=0.05,
            seed=seed,
            device="cpu",
            verbose=0,
        )

    raise ValueError(f"Unknown algorithm: {algo}")


def train_or_load(
    algo: str,
    timesteps: int,
    model_dir: Path,
    seed: int,
    force: bool,
    eval_episodes: int,
) -> BaseAlgorithm:
    model_path = model_dir / algo
    best_path = model_dir / f"{algo}_best"
    zip_path = model_path.with_suffix(".zip")
    best_zip_path = best_path.with_suffix(".zip")
    model_cls = {"ppo": PPO, "dqn": DQN, "a2c": A2C}[algo]

    if zip_path.exists() and not force:
        load_path = best_path if best_zip_path.exists() else model_path
        print(f"Loading existing {algo.upper()} model from {load_path.with_suffix('.zip')}")
        return model_cls.load(str(load_path), env=make_env(seed), device="cpu")

    print(f"Training {algo.upper()} for {timesteps} timesteps...")
    model = build_model(algo, seed)
    model.learn(
        total_timesteps=timesteps,
        callback=QuietEvalCallback(algo.upper(), best_path=best_path, eval_episodes=eval_episodes),
    )
    model.save(str(model_path))
    print(f"Saved {algo.upper()} to {zip_path}")

    if best_zip_path.exists():
        print(f"Using best {algo.upper()} checkpoint from {best_zip_path}")
        return model_cls.load(str(best_path), env=make_env(seed), device="cpu")

    return model


def evaluate_policy(
    model: BaseAlgorithm,
    episodes: int = 3,
    deterministic: bool = True,
) -> list[dict]:
    results = []

    for episode in range(episodes):
        env = make_env(seed=10_000 + episode)
        obs, info = env.reset(seed=10_000 + episode)
        public_obs = info["public_observation"]
        total_reward = 0.0
        rows = []
        actions = Counter()

        for step in range(1, env.max_steps + 1):
            action, _ = model.predict(obs, deterministic=deterministic)
            action = int(action)
            actions[action] += 1

            obs, reward, terminated, truncated, info = env.step(action)
            public_obs = info["public_observation"]
            total_reward += reward

            unit = public_obs["unit"]
            rows.append(
                {
                    "step": step,
                    "money": public_obs["money"],
                    "cum_reward": total_reward,
                    "action": action,
                    "x": unit["pos"][0],
                    "y": unit["pos"][1],
                    "inventory": unit["inventory"].get(0, 0),
                }
            )

            if terminated or truncated:
                break

        results.append(
            {
                "total_reward": total_reward,
                "final_money": public_obs["money"],
                "transactions": public_obs["transactions"],
                "actions": actions,
                "rows": rows,
            }
        )

    return results


def evaluate_random(episodes: int, seed: int) -> list[dict]:
    results = []

    for episode in range(episodes):
        env = make_env(seed + episode)
        obs, info = env.reset(seed=seed + episode)
        public_obs = info["public_observation"]
        total_reward = 0.0
        rows = []
        actions = Counter()

        for step in range(1, env.max_steps + 1):
            action = int(env.action_space.sample())
            actions[action] += 1
            obs, reward, terminated, truncated, info = env.step(action)
            public_obs = info["public_observation"]
            total_reward += reward

            unit = public_obs["unit"]
            rows.append(
                {
                    "step": step,
                    "money": public_obs["money"],
                    "cum_reward": total_reward,
                    "action": action,
                    "x": unit["pos"][0],
                    "y": unit["pos"][1],
                    "inventory": unit["inventory"].get(0, 0),
                }
            )

            if terminated or truncated:
                break

        results.append(
            {
                "total_reward": total_reward,
                "final_money": public_obs["money"],
                "transactions": public_obs["transactions"],
                "actions": actions,
                "rows": rows,
            }
        )

    return results


def mean_curve(results: list[dict], key: str) -> tuple[np.ndarray, np.ndarray]:
    max_len = max(len(result["rows"]) for result in results)
    curves = []

    for result in results:
        values = [row[key] for row in result["rows"]]
        if len(values) < max_len:
            values.extend([values[-1]] * (max_len - len(values)))
        curves.append(values)

    data = np.array(curves, dtype=float)
    return data.mean(axis=0), data.std(axis=0)


def write_summary(all_results: dict[str, list[dict]], out_dir: Path) -> None:
    with (out_dir / "algorithm_summary.csv").open("w", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=[
                "algorithm",
                "episodes",
                "mean_final_money",
                "std_final_money",
                "mean_total_reward",
                "std_total_reward",
                "mean_transactions",
            ],
        )
        writer.writeheader()

        for name, results in all_results.items():
            money = np.array([result["final_money"] for result in results], dtype=float)
            rewards = np.array([result["total_reward"] for result in results], dtype=float)
            tx = np.array([result["transactions"] for result in results], dtype=float)
            writer.writerow(
                {
                    "algorithm": name,
                    "episodes": len(results),
                    "mean_final_money": f"{money.mean():.6f}",
                    "std_final_money": f"{money.std():.6f}",
                    "mean_total_reward": f"{rewards.mean():.6f}",
                    "std_total_reward": f"{rewards.std():.6f}",
                    "mean_transactions": f"{tx.mean():.6f}",
                }
            )


def write_trace(all_results: dict[str, list[dict]], out_dir: Path) -> None:
    with (out_dir / "evaluation_traces.csv").open("w", newline="") as f:
        writer = csv.DictWriter(
            f,
            fieldnames=["algorithm", "episode", "step", "money", "cum_reward", "action", "x", "y", "inventory"],
        )
        writer.writeheader()
        for name, results in all_results.items():
            for episode, result in enumerate(results, 1):
                for row in result["rows"]:
                    writer.writerow({"algorithm": name, "episode": episode, **row})


def plot_money_reward(all_results: dict[str, list[dict]], out_dir: Path) -> None:
    colors = {
        "ppo": "#1f77b4",
        "dqn": "#d62728",
        "a2c": "#2ca02c",
        "random": "#7f7f7f",
    }

    fig, axes = plt.subplots(2, 1, figsize=(11, 8), sharex=True)

    for name, results in all_results.items():
        money_mean, money_std = mean_curve(results, "money")
        reward_mean, reward_std = mean_curve(results, "cum_reward")
        steps = np.arange(1, len(money_mean) + 1)
        color = colors.get(name, None)

        axes[0].plot(steps, money_mean, label=name.upper(), color=color, linewidth=2)
        axes[0].fill_between(steps, money_mean - money_std, money_mean + money_std, color=color, alpha=0.12)

        axes[1].plot(steps, reward_mean, label=name.upper(), color=color, linewidth=2)
        axes[1].fill_between(steps, reward_mean - reward_std, reward_mean + reward_std, color=color, alpha=0.12)

    axes[0].set_title("Money Curves")
    axes[0].set_ylabel("Money")
    axes[0].grid(alpha=0.25)
    axes[0].legend()

    axes[1].set_title("Cumulative Reward Curves")
    axes[1].set_xlabel("Step")
    axes[1].set_ylabel("Cumulative reward")
    axes[1].grid(alpha=0.25)
    axes[1].legend()

    fig.tight_layout()
    fig.savefig(out_dir / "algorithm_money_reward_curves.png", dpi=180)
    plt.close(fig)


def plot_final_bars(all_results: dict[str, list[dict]], out_dir: Path) -> None:
    names = list(all_results.keys())
    money_mean = [np.mean([r["final_money"] for r in all_results[name]]) for name in names]
    money_std = [np.std([r["final_money"] for r in all_results[name]]) for name in names]
    reward_mean = [np.mean([r["total_reward"] for r in all_results[name]]) for name in names]
    reward_std = [np.std([r["total_reward"] for r in all_results[name]]) for name in names]

    fig, axes = plt.subplots(1, 2, figsize=(12, 5))

    axes[0].bar([name.upper() for name in names], money_mean, yerr=money_std, color="#4e79a7", capsize=4)
    axes[0].set_title("Final Money")
    axes[0].grid(axis="y", alpha=0.25)

    axes[1].bar([name.upper() for name in names], reward_mean, yerr=reward_std, color="#59a14f", capsize=4)
    axes[1].set_title("Total Reward")
    axes[1].grid(axis="y", alpha=0.25)

    fig.tight_layout()
    fig.savefig(out_dir / "algorithm_final_bars.png", dpi=180)
    plt.close(fig)


def plot_actions(all_results: dict[str, list[dict]], out_dir: Path) -> None:
    labels = [ACTION_NAMES[i] for i in range(8)]
    x = np.arange(len(labels))
    width = 0.18

    fig, ax = plt.subplots(figsize=(12, 5))

    for idx, (name, results) in enumerate(all_results.items()):
        counts = Counter()
        for result in results:
            counts.update(result["actions"])
        values = np.array([counts.get(i, 0) for i in range(8)], dtype=float) / len(results)
        ax.bar(x + (idx - 1.5) * width, values, width, label=name.upper())

    ax.set_xticks(x)
    ax.set_xticklabels(labels)
    ax.set_ylabel("Mean count per episode")
    ax.set_title("Action Distribution")
    ax.grid(axis="y", alpha=0.25)
    ax.legend()

    fig.tight_layout()
    fig.savefig(out_dir / "algorithm_action_distribution.png", dpi=180)
    plt.close(fig)


def main() -> None:
    parser = argparse.ArgumentParser(description="Compare PPO, DQN, A2C, and random baseline.")
    parser.add_argument("--timesteps", type=int, default=50_000)
    parser.add_argument("--episodes", type=int, default=3)
    parser.add_argument("--random-episodes", type=int, default=20)
    parser.add_argument("--seed", type=int, default=2026)
    parser.add_argument("--model-dir", default="models/rl_compare")
    parser.add_argument("--out-dir", default="plots_rl_compare")
    parser.add_argument("--force", action="store_true", help="Retrain even if model files already exist.")
    parser.add_argument("--algorithms", nargs="+", choices=["ppo", "dqn", "a2c"], default=["ppo", "dqn", "a2c"])
    parser.add_argument("--eval-episodes", type=int, default=3, help="Episodes used by training-time best checkpoint selection.")
    args = parser.parse_args()

    set_random_seed(args.seed)
    model_dir = Path(args.model_dir)
    out_dir = Path(args.out_dir)
    model_dir.mkdir(parents=True, exist_ok=True)
    out_dir.mkdir(parents=True, exist_ok=True)

    all_results = {}

    for offset, algo in enumerate(args.algorithms):
        model = train_or_load(algo, args.timesteps, model_dir, args.seed + offset, args.force, args.eval_episodes)
        all_results[algo] = evaluate_policy(model, episodes=args.episodes, deterministic=True)

    all_results["random"] = evaluate_random(args.random_episodes, args.seed + 1_000)

    write_summary(all_results, out_dir)
    write_trace(all_results, out_dir)
    plot_money_reward(all_results, out_dir)
    plot_final_bars(all_results, out_dir)
    plot_actions(all_results, out_dir)

    print(f"Saved comparison outputs to {out_dir.resolve()}")
    print("Summary:")
    for name, results in all_results.items():
        money = np.array([result["final_money"] for result in results], dtype=float)
        rewards = np.array([result["total_reward"] for result in results], dtype=float)
        tx = np.array([result["transactions"] for result in results], dtype=float)
        print(
            f"  {name.upper():>6} "
            f"money={money.mean():8.2f} +/- {money.std():6.2f} "
            f"reward={rewards.mean():8.2f} +/- {rewards.std():6.2f} "
            f"tx={tx.mean():5.2f}"
        )


if __name__ == "__main__":
    main()
