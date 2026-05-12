"""
Training script for the custom DQN agent.

Usage:
    python my_agent/train.py --config easy --timesteps 200000 --save models/dqn_custom.pt
    python my_agent/train.py --config medium --timesteps 500000 --lr 5e-5 --gamma 0.98
"""
from __future__ import annotations
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import yaml

from GameLogic import GameConfig, GameEnvironment
from my_agent import DQNAgent


def load_config(name_or_path: str) -> GameConfig:
    presets = {
        "easy": GameConfig.easy,
        "medium": GameConfig.medium,
        "hard": GameConfig.hard,
    }
    if name_or_path in presets:
        return presets[name_or_path]()

    if os.path.exists(name_or_path):
        with open(name_or_path) as f:
            data = yaml.safe_load(f)
        return GameConfig.from_dict(data)

    raise ValueError(f"Unknown config: {name_or_path}")


def main():
    parser = argparse.ArgumentParser(description="Train custom DQN on THUAI9 PvE")
    parser.add_argument("--config", default="easy")
    parser.add_argument("--timesteps", type=int, default=200_000)
    parser.add_argument("--save", default="models/dqn_custom.pt")
    parser.add_argument("--seed", type=int, default=42)
    # DQN hyperparams
    parser.add_argument("--lr", type=float, default=1e-4)
    parser.add_argument("--gamma", type=float, default=0.99)
    parser.add_argument("--batch-size", type=int, default=128)
    parser.add_argument("--buffer-capacity", type=int, default=100_000)
    parser.add_argument("--target-update", type=int, default=2000)
    parser.add_argument("--epsilon-decay", type=int, default=50_000)
    parser.add_argument("--learning-starts", type=int, default=5_000)
    args = parser.parse_args()

    cfg = load_config(args.config)
    print(f"[Config] {args.config}")
    print(f"  map={cfg.map_width}x{cfg.map_height}  money={cfg.initial_money}  "
          f"volatility={cfg.price_volatility}")

    env = GameEnvironment(cfg=cfg, seed=args.seed)
    agent = DQNAgent(
        env,
        lr=args.lr,
        gamma=args.gamma,
        batch_size=args.batch_size,
        buffer_capacity=args.buffer_capacity,
        target_update_freq=args.target_update,
        epsilon_decay=args.epsilon_decay,
        learning_starts=args.learning_starts,
    )

    print(f"\n[Training] {args.timesteps:,} timesteps on {args.config}")
    print(f"  device={agent.device}  lr={args.lr}  gamma={args.gamma}")
    metrics = agent.train(total_timesteps=args.timesteps)

    agent.save(args.save)

    print("\n[Results]")
    for k, v in metrics.items():
        print(f"  {k}: {v}")


if __name__ == "__main__":
    main()
