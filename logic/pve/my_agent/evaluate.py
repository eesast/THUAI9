"""
Evaluate a saved DQN agent across multiple seeds.

**REFERENCE ONLY — 正式比赛排名必须使用 official_evaluator.py。**
此脚本仅供学习参考，用于自测你的 DQN 模型训练效果。

Usage:
    python my_agent/evaluate.py --model models/dqn_custom.pt --config medium --episodes 100
    python my_agent/evaluate.py --model models/dqn_custom.pt --config easy --episodes 50 --seeds 0 42 123

For official ranking, use:
    python official_evaluator.py --submission ./submission --config hard --episodes 200
"""
from __future__ import annotations
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np

from GameLogic import GameConfig, GameEnvironment
from my_agent import DQNAgent


def evaluate(
    model_path: str,
    cfg: GameConfig,
    n_episodes: int = 100,
    seeds: list | None = None,
) -> dict:
    if seeds is None:
        seeds = [0]

    all_scores: list[float] = []
    all_rewards: list[float] = []
    all_lengths: list[int] = []

    for seed in seeds:
        env = GameEnvironment(cfg=cfg, seed=seed)
        agent = DQNAgent.load(model_path, env)
        # Disable exploration for evaluation
        agent.epsilon = 0.0

        for ep in range(n_episodes):
            obs = agent.reset()
            ep_reward = 0.0
            ep_score = 0.0
            ep_len = 0
            done = False

            while not done:
                action = agent.get_action(obs)
                obs, reward, terminated, truncated, info = agent.step(action)
                ep_reward += reward
                ep_len += 1
                ep_score = info.get("score", 0.0)
                done = terminated or truncated

            all_scores.append(ep_score)
            all_rewards.append(ep_reward)
            all_lengths.append(ep_len)

    scores = np.array(all_scores)
    rewards = np.array(all_rewards)

    return {
        "total_episodes": len(all_scores),
        "seeds_used": seeds,
        "score_mean": float(scores.mean()),
        "score_std": float(scores.std()),
        "score_min": float(scores.min()),
        "score_max": float(scores.max()),
        "reward_mean": float(rewards.mean()),
        "reward_std": float(rewards.std()),
        "mean_ep_len": float(np.mean(all_lengths)),
    }


def main():
    parser = argparse.ArgumentParser(description="Evaluate custom DQN agent")
    parser.add_argument("--model", required=True, help="Path to saved model (.pt)")
    parser.add_argument("--config", default="easy")
    parser.add_argument("--episodes", type=int, default=100)
    parser.add_argument("--seeds", type=int, nargs="+", default=[0, 42, 123, 999])
    args = parser.parse_args()

    presets = {"easy": GameConfig.easy, "medium": GameConfig.medium, "hard": GameConfig.hard}
    cfg = presets.get(args.config, GameConfig.easy)()

    print(f"Evaluating {args.model} on {args.config} "
          f"({args.episodes} eps × {len(args.seeds)} seeds)")

    results = evaluate(args.model, cfg, args.episodes, args.seeds)

    print("\n[Evaluation Results]")
    for k, v in results.items():
        if isinstance(v, float):
            print(f"  {k}: {v:.3f}")
        else:
            print(f"  {k}: {v}")


if __name__ == "__main__":
    main()
