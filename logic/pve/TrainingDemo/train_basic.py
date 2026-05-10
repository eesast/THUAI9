"""
Basic training script: load config → create env → train PPO → report.

Usage:
    python TrainingDemo/train_basic.py --config easy --timesteps 100000
"""
from __future__ import annotations
import argparse
import os
import sys

# Allow imports from project root
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import yaml

from GameLogic import GameConfig, GameEnvironment
from RLInterfaces import PPOAgent


def load_config(name_or_path: str) -> GameConfig:
    # Built-in presets
    presets = {"easy": GameConfig.easy, "medium": GameConfig.medium, "hard": GameConfig.hard}
    if name_or_path in presets:
        return presets[name_or_path]()

    # YAML file
    if os.path.exists(name_or_path):
        with open(name_or_path) as f:
            data = yaml.safe_load(f)
        return GameConfig.from_dict(data)

    raise ValueError(f"Unknown config: {name_or_path}")


def main():
    parser = argparse.ArgumentParser(description="Train PPO on THUAI9 PvE")
    parser.add_argument("--config", default="easy",
                        help="Difficulty preset (easy/medium/hard) or path to YAML")
    parser.add_argument("--timesteps", type=int, default=100_000,
                        help="Total training timesteps")
    parser.add_argument("--save-dir", default="models/",
                        help="Directory to save model checkpoints")
    parser.add_argument("--seed", type=int, default=42)
    args = parser.parse_args()

    cfg = load_config(args.config)
    print(f"[Config] {args.config}: {cfg}")

    env = GameEnvironment(cfg=cfg, seed=args.seed)
    agent = PPOAgent(
        env,
        learning_rate=3e-4,
        n_steps=2048,
        batch_size=64,
        gamma=0.99,
        ent_coef=0.01,
        verbose=1,
    )

    print(f"\n[Training] {args.timesteps:,} timesteps  config={args.config}")
    metrics = agent.train(
        total_timesteps=args.timesteps,
        save_dir=args.save_dir,
        eval_freq=4096,
    )

    print("\n[Results]")
    for k, v in metrics.items():
        print(f"  {k}: {v}")


if __name__ == "__main__":
    main()
