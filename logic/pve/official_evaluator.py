"""
Official evaluator — the single script used to rank all submissions.

Every contestant's submission goes through this exact same pipeline.
Ranking is determined by mean score across multiple seeds.

Contract:
  Contestant submits a directory containing:
    agent.py          → must define class Agent(BaseAgent) with load(cls, path, env)
    model.pt (or .zip)→ trained weights, loaded by Agent.load(path, env)

Usage:
  python official_evaluator.py --submission ./submission --config hard --episodes 200 --seeds 0 42 123 999 7777

Output:
  JSON with per-seed mean/std and overall score_mean / score_std.
"""

from __future__ import annotations

import argparse
import importlib
import json
import os
import sys
from pathlib import Path
from typing import List

import numpy as np

# Ensure the pve package root is on sys.path
HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

from GameLogic import GameConfig, GameEnvironment
from RLInterfaces import BaseAgent


def load_agent(submission_dir: str, model_path: str, env: GameEnvironment) -> BaseAgent:
    """
    Dynamically load contestant's Agent class from submission_dir/agent.py.

    Expected structure:
      submission_dir/
        agent.py        ← class Agent(BaseAgent)
        model.pt        ← trained weights

    The agent module must NOT import from GameLogic sub-modules.
    """
    sub = Path(submission_dir).resolve()
    agent_file = sub / "agent.py"
    if not agent_file.exists():
        raise FileNotFoundError(
            f"{agent_file} not found. "
            "Your submission must contain agent.py with class Agent(BaseAgent)."
        )

    if str(sub) not in sys.path:
        sys.path.insert(0, str(sub))

    spec = importlib.util.spec_from_file_location("contestant_agent", agent_file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)

    AgentClass = getattr(module, "Agent", None)
    if AgentClass is None:
        raise AttributeError(
            "agent.py must define a class named 'Agent' that inherits BaseAgent."
        )
    if not issubclass(AgentClass, BaseAgent):
        raise TypeError(f"{AgentClass.__name__} must inherit from RLInterfaces.BaseAgent.")

    load_fn = getattr(AgentClass, "load", None)
    if load_fn is None:
        raise AttributeError(
            "Agent class must implement load(cls, path, env) classmethod."
        )

    agent = load_fn(model_path, env)
    return agent


def evaluate(
    agent: BaseAgent,
    env: GameEnvironment,
    n_episodes: int,
    seed: int,
) -> dict:
    """Run N episodes with one seed group; return per-seed stats."""
    scores: List[float] = []
    lengths: List[int] = []

    for ep in range(n_episodes):
        # Each episode gets a distinct seed so maps differ even in the same group
        obs, _ = env.reset(seed=seed * 1000 + ep)
        ep_score = 0.0
        ep_len = 0
        done = False

        while not done:
            action = agent.get_action(obs)
            obs, _reward, terminated, truncated, info = env.step(action)
            ep_len += 1
            ep_score = info.get("score", 0.0)
            done = terminated or truncated

        scores.append(ep_score)
        lengths.append(ep_len)

    arr = np.array(scores)
    return {
        "seed": seed,
        "episodes": n_episodes,
        "score_mean": float(arr.mean()),
        "score_std": float(arr.std()),
        "score_min": float(arr.min()),
        "score_max": float(arr.max()),
        "mean_length": float(np.mean(lengths)),
    }


def main():
    parser = argparse.ArgumentParser(
        description="THUAI9 PvE-RL Official Evaluator"
    )
    parser.add_argument(
        "--submission", required=True,
        help="Path to submission directory (must contain agent.py + model file)."
    )
    parser.add_argument(
        "--model", default="model.pt",
        help="Filename of model weights inside submission directory (default: model.pt)."
    )
    parser.add_argument(
        "--config", default="hard",
        choices=["easy", "medium", "hard"],
        help="Difficulty to evaluate on (default: hard)."
    )
    parser.add_argument(
        "--episodes", type=int, default=200,
        help="Number of episodes per seed (default: 200)."
    )
    parser.add_argument(
        "--seeds", type=int, nargs="+",
        default=[0, 42, 123, 999, 7777],
        help="Seeds for evaluation (default: 0 42 123 999 7777)."
    )
    parser.add_argument(
        "--random-map", action="store_true", default=True,
        help="Use random map layout (default: True)."
    )
    parser.add_argument(
        "--output", default=None,
        help="Optional path to save results as JSON."
    )
    args = parser.parse_args()

    sub = Path(args.submission).resolve()
    if not sub.is_dir():
        print(f"[ERROR] Submission directory not found: {sub}")
        sys.exit(1)

    model_path = str(sub / args.model)
    if not os.path.exists(model_path):
        print(f"[ERROR] Model file not found: {model_path}")
        sys.exit(1)

    presets = {
        "easy": GameConfig.easy,
        "medium": GameConfig.medium,
        "hard": GameConfig.hard,
    }
    cfg = presets[args.config]()
    cfg.random_map = args.random_map

    print(f"{'='*60}")
    print(f"THUAI9 PvE-RL Official Evaluator")
    print(f"{'='*60}")
    print(f"  submission : {sub}")
    print(f"  config     : {args.config}")
    print(f"  random_map : {cfg.random_map}")
    print(f"  episodes   : {args.episodes}")
    print(f"  seeds      : {args.seeds}")
    print(f"{'='*60}")

    per_seed_results = []
    all_scores = []

    for seed in args.seeds:
        env = GameEnvironment(cfg=cfg, seed=seed)
        agent = load_agent(str(sub), model_path, env)

        result = evaluate(agent, env, args.episodes, seed)
        per_seed_results.append(result)
        all_scores.extend(
            [result["score_mean"]] * args.episodes  # approximation for global stats
        )

        print(
            f"  seed={seed:>4} | "
            f"score_mean={result['score_mean']:>10.2f} | "
            f"score_std={result['score_std']:>8.2f} | "
            f"score_min={result['score_min']:>10.2f} | "
            f"score_max={result['score_max']:>10.2f}"
        )

    # Aggregate across seeds
    seed_means = np.array([r["score_mean"] for r in per_seed_results])
    overall_score = float(seed_means.mean())
    overall_std = float(seed_means.std())

    print(f"{'='*60}")
    print(f"  FINAL SCORE: {overall_score:.2f} +/- {overall_std:.2f}")
    print(f"{'='*60}")

    output = {
        "submission": str(sub),
        "config": args.config,
        "random_map": cfg.random_map,
        "episodes_per_seed": args.episodes,
        "seeds": args.seeds,
        "per_seed": per_seed_results,
        "score_mean": overall_score,
        "score_std": overall_std,
    }

    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            json.dump(output, f, indent=2, ensure_ascii=False)
        print(f"\nResults saved to {args.output}")

    return output


if __name__ == "__main__":
    main()
