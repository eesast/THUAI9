"""
Evaluation script: load a saved model, run N episodes, report statistics.

Usage:
    python TrainingDemo/evaluate.py --model models/ppo_thuai9_best --episodes 50
"""
from __future__ import annotations
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np

from GameLogic import GameConfig, GameEnvironment
from RLInterfaces import PPOAgent


def evaluate(
    model_path: str,
    cfg: GameConfig,
    n_episodes: int = 50,
    seed: int = 0,
    render: bool = False,
) -> dict:
    env = GameEnvironment(cfg=cfg, seed=seed)
    agent = PPOAgent.load(model_path, env)

    rewards, scores, lengths = [], [], []

    for ep in range(n_episodes):
        obs = agent.reset()
        ep_reward, ep_len = 0.0, 0
        ep_score = 0.0
        done = False

        while not done:
            action = agent.get_action(obs)
            obs, reward, terminated, truncated, info = agent.step(action)
            ep_reward += reward
            ep_len += 1
            ep_score = info.get("score", 0.0)
            done = terminated or truncated

            if render:
                print(env.render())

        rewards.append(ep_reward)
        scores.append(ep_score)
        lengths.append(ep_len)

    results = {
        "n_episodes": n_episodes,
        "mean_reward": float(np.mean(rewards)),
        "std_reward":  float(np.std(rewards)),
        "min_reward":  float(np.min(rewards)),
        "max_reward":  float(np.max(rewards)),
        "mean_score":  float(np.mean(scores)),
        "std_score":   float(np.std(scores)),
        "mean_length": float(np.mean(lengths)),
    }
    return results


def main():
    parser = argparse.ArgumentParser(description="Evaluate saved THUAI9 PPO model")
    parser.add_argument("--model", required=True, help="Path to saved model (without .zip)")
    parser.add_argument("--config", default="easy")
    parser.add_argument("--episodes", type=int, default=50)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--render", action="store_true")
    args = parser.parse_args()

    presets = {"easy": GameConfig.easy, "medium": GameConfig.medium, "hard": GameConfig.hard}
    cfg = presets.get(args.config, GameConfig.easy)()

    results = evaluate(args.model, cfg, args.episodes, args.seed, args.render)

    print("\n[Evaluation Results]")
    for k, v in results.items():
        print(f"  {k}: {v:.3f}" if isinstance(v, float) else f"  {k}: {v}")


if __name__ == "__main__":
    main()
