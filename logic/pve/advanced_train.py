#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Advanced training script with multiple options
支持快速测试、标准训练、强化训练等多种模式
"""

import os
import sys
import argparse
import numpy as np
from datetime import datetime
from pathlib import Path

from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import BaseCallback
from stable_baselines3.common.env_util import make_vec_env
from ai_gym_env import AI9GymEnv


class TrainingCallback(BaseCallback):
    """Enhanced callback with better monitoring"""

    def __init__(self, eval_env, check_freq=5000, save_freq=10000):
        super().__init__()
        self.check_freq = check_freq
        self.save_freq = save_freq
        self.eval_env = eval_env
        self.best_reward = -np.inf
        self.episode_count = 0

    def _on_step(self) -> bool:
        # Periodic evaluation
        if self.n_calls % self.check_freq == 0:
            self._evaluate()

        # Periodic saving
        if self.n_calls % self.save_freq == 0:
            self._save_checkpoint()

        return True

    def _evaluate(self):
        """Run evaluation and print stats"""
        obs, _ = self.eval_env.reset()
        total_reward = 0
        done = False
        steps = 0

        while not done and steps < 1000:
            action, _ = self.model.predict(obs, deterministic=True)
            obs, reward, terminated, truncated, _ = self.eval_env.step(action)
            total_reward += reward
            done = terminated or truncated
            steps += 1

        # Get environment info
        actual_env = self.eval_env.unwrapped
        money = actual_env.game.money

        # Print statistics
        print(f"\n[Evaluation at step {self.n_calls}]")
        print(f"  Reward: {total_reward:.4f}")
        print(f"  Money: {money:.2f}")
        print(f"  Steps: {steps}")

        # Track best model
        if total_reward > self.best_reward:
            self.best_reward = total_reward
            self._save_best()
            print(f"  ✓ New best model saved! (reward: {total_reward:.4f})")

    def _save_checkpoint(self):
        """Save checkpoint"""
        checkpoint_dir = Path("models/checkpoints")
        checkpoint_dir.mkdir(parents=True, exist_ok=True)
        checkpoint_path = checkpoint_dir / f"checkpoint_{self.n_calls}"
        self.model.save(str(checkpoint_path))

    def _save_best(self):
        """Save best model"""
        best_dir = Path("models")
        best_dir.mkdir(parents=True, exist_ok=True)
        best_path = best_dir / "ppo_ai9_best"
        self.model.save(str(best_path))


def train_model(args):
    """Train the model with specified configuration"""

    print("="*60)
    print(f"Training Configuration: {args.mode}")
    print("="*60)

    # Create environments
    print("\nCreating environments...")
    env = AI9GymEnv()
    eval_env = AI9GymEnv()

    # Configure hyperparameters based on mode
    config = get_config(args.mode)

    print("\nHyperparameters:")
    for key, value in config.items():
        if key != "device":
            print(f"  {key}: {value}")

    # Create callback
    callback = TrainingCallback(
        eval_env,
        check_freq=args.check_freq,
        save_freq=args.save_freq
    )

    # Create model
    print("\nCreating PPO model...")
    model = PPO("MlpPolicy", env, verbose=1, **config)

    # Train
    print(f"\nTraining for {args.timesteps} timesteps...")
    start_time = datetime.now()

    try:
        model.learn(
            total_timesteps=args.timesteps,
            callback=callback,
            progress_bar=True
        )
    except KeyboardInterrupt:
        print("\n\n⚠ Training interrupted by user")

    # Calculate training time
    elapsed = datetime.now() - start_time
    print(f"\nTraining completed in {elapsed}")

    # Save final model
    print("\nSaving final model...")
    final_dir = Path("models")
    final_dir.mkdir(parents=True, exist_ok=True)
    final_path = final_dir / f"ppo_ai9_{args.mode}"
    model.save(str(final_path))
    print(f"✓ Model saved to {final_path}.zip")

    return str(final_path)


def test_model(model_path, num_episodes=5):
    """Test the trained model"""

    print("="*60)
    print("Testing Model")
    print("="*60)

    # Load model
    print(f"\nLoading model from {model_path}...")
    model = PPO.load(model_path)

    # Create environment
    env = AI9GymEnv()

    episode_rewards = []
    episode_money = []

    for episode in range(num_episodes):
        print(f"\n[Episode {episode + 1}/{num_episodes}]")

        obs, _ = env.reset()
        total_reward = 0
        done = False
        steps = 0

        while not done and steps < 1000:
            action, _ = model.predict(obs, deterministic=True)
            obs, reward, terminated, truncated, _ = env.step(action)
            total_reward += reward
            done = terminated or truncated
            steps += 1

        final_money = env.game.money
        episode_rewards.append(total_reward)
        episode_money.append(final_money)

        print(f"  Reward: {total_reward:.4f}")
        print(f"  Final Money: {final_money:.2f}")
        print(f"  Steps: {steps}")

    # Print statistics
    print("\n" + "="*60)
    print("Test Statistics")
    print("="*60)
    print(f"Average Reward: {np.mean(episode_rewards):.4f} "
          f"(±{np.std(episode_rewards):.4f})")
    print(f"Average Final Money: {np.mean(episode_money):.2f} "
          f"(±{np.std(episode_money):.2f})")
    print(f"Best Reward: {np.max(episode_rewards):.4f}")
    print(f"Worst Reward: {np.min(episode_rewards):.4f}")


def get_config(mode):
    """Get hyperparameter configuration for different modes"""

    configs = {
        "fast": {
            "learning_rate": 0.001,
            "n_steps": 512,
            "batch_size": 32,
            "gamma": 0.9,
            "ent_coef": 0.1,
            "device": "cpu"
        },
        "standard": {
            "learning_rate": 0.0003,
            "n_steps": 2048,
            "batch_size": 64,
            "gamma": 0.9,
            "ent_coef": 0.05,
            "device": "cpu"
        },
        "strong": {
            "learning_rate": 0.00015,
            "n_steps": 4096,
            "batch_size": 128,
            "gamma": 0.95,
            "ent_coef": 0.02,
            "device": "cpu"
        },
        "gpu": {
            "learning_rate": 0.0003,
            "n_steps": 2048,
            "batch_size": 64,
            "gamma": 0.9,
            "ent_coef": 0.05,
            "device": "cuda"
        }
    }

    if mode not in configs:
        print(f"Warning: Unknown mode '{mode}', using 'standard'")
        mode = "standard"

    return configs[mode]


def compare_models(model_paths, num_episodes=5):
    """Compare multiple models"""

    print("="*60)
    print("Model Comparison")
    print("="*60)

    results = {}

    for model_path in model_paths:
        print(f"\nTesting {Path(model_path).name}...")

        try:
            model = PPO.load(model_path)
            env = AI9GymEnv()

            episode_rewards = []
            for _ in range(num_episodes):
                obs, _ = env.reset()
                total_reward = 0
                done = False
                while not done:
                    action, _ = model.predict(obs, deterministic=True)
                    obs, reward, terminated, truncated, _ = env.step(action)
                    total_reward += reward
                    done = terminated or truncated
                episode_rewards.append(total_reward)

            avg_reward = np.mean(episode_rewards)
            results[Path(model_path).name] = avg_reward
            print(f"  Average Reward: {avg_reward:.4f}")

        except Exception as e:
            print(f"  Error: {e}")

    # Show ranking
    print("\n" + "="*60)
    print("Ranking")
    print("="*60)
    sorted_results = sorted(results.items(), key=lambda x: x[1], reverse=True)
    for rank, (name, reward) in enumerate(sorted_results, 1):
        print(f"{rank}. {name}: {reward:.4f}")


def main():
    parser = argparse.ArgumentParser(
        description="Advanced training script for AI9 RL environment"
    )

    # Main command
    subparsers = parser.add_subparsers(dest="command", help="Command to run")

    # Train command
    train_parser = subparsers.add_parser("train", help="Train a new model")
    train_parser.add_argument(
        "--mode", choices=["fast", "standard", "strong", "gpu"],
        default="standard", help="Training mode"
    )
    train_parser.add_argument(
        "--timesteps", type=int, default=50000,
        help="Total timesteps to train"
    )
    train_parser.add_argument(
        "--check-freq", type=int, default=4096,
        help="Evaluation frequency"
    )
    train_parser.add_argument(
        "--save-freq", type=int, default=10000,
        help="Checkpoint save frequency"
    )

    # Test command
    test_parser = subparsers.add_parser("test", help="Test a trained model")
    test_parser.add_argument(
        "model", help="Path to model file (without .zip)"
    )
    test_parser.add_argument(
        "--episodes", type=int, default=5,
        help="Number of test episodes"
    )

    # Compare command
    compare_parser = subparsers.add_parser("compare", help="Compare models")
    compare_parser.add_argument(
        "models", nargs="+", help="Model paths to compare"
    )
    compare_parser.add_argument(
        "--episodes", type=int, default=5,
        help="Number of test episodes per model"
    )

    args = parser.parse_args()

    if args.command == "train":
        model_path = train_model(args)

        # Ask to test
        if input("\nTest the model? (y/n): ").lower() == "y":
            test_model(model_path)

    elif args.command == "test":
        test_model(args.model, args.episodes)

    elif args.command == "compare":
        compare_models(args.models, args.episodes)

    else:
        parser.print_help()


if __name__ == "__main__":
    main()
