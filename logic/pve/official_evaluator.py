"""
Official evaluator — the single script used to rank all submissions.

Every contestant's submission goes through this exact same pipeline.
Ranking is determined by mean score across multiple seeds.

Contract:
  Contestant submits a directory containing:
    agent.py          → must define class Agent(BaseAgent)
                        - NN-based: implement load(cls, path, env) classmethod
                        - rule-based: implement __init__(self, env), no model file needed
    model.pt (or .zip)→ trained weights (optional for rule-based agents)

Usage:
  # NN-based submission (auto-detects model file):
  python official_evaluator.py --submission ./submission --config hard --episodes 200

  # Explicit model file:
  python official_evaluator.py --submission ./submission --model model.pt --config hard

  # Rule-based bot (no model file):
  python official_evaluator.py --submission ./submission --config hard --episodes 200

Output:
  JSON with per-seed mean/std and overall score_mean / score_std.
"""

from __future__ import annotations

import argparse
import ast
import importlib
import json
import os
import sys
from pathlib import Path
from typing import List, Optional

import numpy as np

# Ensure the pve package root is on sys.path
HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))

from GameLogic import GameConfig, GameEnvironment
from RLInterfaces import BaseAgent, RestrictedGameEnvironment


_ALLOWED_ENV_ATTRS = frozenset({"reset", "step", "action_masks"})
_ALLOWED_RL_IMPORTS = {
    "RLInterfaces",
    "RLInterfaces.base_agent",
}
_ALLOWED_GAMELOGIC_IMPORTS = {
    "Action",
    "N_ACTIONS",
}


class SubmissionRuleError(RuntimeError):
    """Raised when an agent source file uses evaluator-forbidden interfaces."""


class _AgentRuleVisitor(ast.NodeVisitor):
    def __init__(self):
        self.errors: List[str] = []
        self._env_aliases = [{"env"}]

    @property
    def env_aliases(self) -> set:
        return self._env_aliases[-1]

    def _is_env_expr(self, node: ast.AST) -> bool:
        if isinstance(node, ast.Name):
            return node.id in self.env_aliases
        return (
            isinstance(node, ast.Attribute)
            and node.attr == "env"
            and isinstance(node.value, ast.Name)
            and node.value.id == "self"
        )

    def _error(self, node: ast.AST, message: str) -> None:
        self.errors.append(f"line {getattr(node, 'lineno', '?')}: {message}")

    def visit_ImportFrom(self, node: ast.ImportFrom) -> None:
        module = node.module or ""
        if module == "GameLogic":
            for alias in node.names:
                if alias.name not in _ALLOWED_GAMELOGIC_IMPORTS:
                    self._error(
                        node,
                        "only Action and N_ACTIONS may be imported from GameLogic by submissions",
                    )
        elif module.startswith("GameLogic"):
            self._error(node, "imports from GameLogic internal modules are not allowed in submissions")
        if module.startswith("RLInterfaces") and module not in _ALLOWED_RL_IMPORTS:
            self._error(
                node,
                "only BaseAgent may be imported from RLInterfaces by submissions",
            )
        if module in _ALLOWED_RL_IMPORTS:
            for alias in node.names:
                if alias.name != "BaseAgent":
                    self._error(
                        node,
                        "only BaseAgent may be imported from RLInterfaces by submissions",
                    )
        self.generic_visit(node)

    def visit_Import(self, node: ast.Import) -> None:
        for alias in node.names:
            if alias.name == "GameLogic" or alias.name.startswith("GameLogic."):
                self._error(
                    node,
                    "use 'from GameLogic import Action, N_ACTIONS' instead of importing GameLogic modules",
                )
            if alias.name == "RLInterfaces" or (
                alias.name.startswith("RLInterfaces.") and alias.name not in _ALLOWED_RL_IMPORTS
            ):
                self._error(
                    node,
                    "use 'from RLInterfaces import BaseAgent' instead of importing RLInterfaces modules",
                )
        self.generic_visit(node)

    def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
        self._env_aliases.append(set(self.env_aliases))
        self.generic_visit(node)
        self._env_aliases.pop()

    visit_AsyncFunctionDef = visit_FunctionDef

    def visit_Assign(self, node: ast.Assign) -> None:
        if self._is_env_expr(node.value):
            for target in node.targets:
                if isinstance(target, ast.Name):
                    self.env_aliases.add(target.id)
        self.generic_visit(node)

    def visit_AnnAssign(self, node: ast.AnnAssign) -> None:
        if node.value is not None and self._is_env_expr(node.value):
            if isinstance(node.target, ast.Name):
                self.env_aliases.add(node.target.id)
        self.generic_visit(node)

    def visit_Attribute(self, node: ast.Attribute) -> None:
        if self._is_env_expr(node.value) and node.attr not in _ALLOWED_ENV_ATTRS:
            self._error(
                node,
                f"env.{node.attr} is not allowed; use only reset(), step(), action_masks(), obs, and info",
            )
        self.generic_visit(node)

    def visit_Call(self, node: ast.Call) -> None:
        if (
            isinstance(node.func, ast.Name)
            and node.func.id == "getattr"
            and len(node.args) >= 2
            and self._is_env_expr(node.args[0])
        ):
            attr_arg = node.args[1]
            if not (
                isinstance(attr_arg, ast.Constant)
                and isinstance(attr_arg.value, str)
                and attr_arg.value in _ALLOWED_ENV_ATTRS
            ):
                self._error(
                    node,
                    "dynamic getattr on env is not allowed except for reset/step/action_masks",
                )
        self.generic_visit(node)


def validate_agent_source(agent_file: Path) -> None:
    tree = ast.parse(agent_file.read_text(encoding="utf-8-sig"), filename=str(agent_file))
    visitor = _AgentRuleVisitor()
    visitor.visit(tree)
    if visitor.errors:
        detail = "\n  ".join(visitor.errors)
        raise SubmissionRuleError(
            "Submission uses forbidden PvE interfaces. "
            "Agents may only interact with the environment through "
            "reset(), step(), action_masks(), and the returned obs/info.\n  "
            f"{detail}"
        )


def load_agent(submission_dir: str, model_path: Optional[str], env: GameEnvironment) -> BaseAgent:
    """
    Dynamically load contestant's Agent class from submission_dir/agent.py.

    Expected structure:
      submission_dir/
        agent.py        ← class Agent(BaseAgent)
        model.pt        ← trained weights (optional for rule-based agents)

    The agent module must NOT import from GameLogic sub-modules.
    """
    sub = Path(submission_dir).resolve()
    agent_file = sub / "agent.py"
    if not agent_file.exists():
        raise FileNotFoundError(
            f"{agent_file} not found. "
            "Your submission must contain agent.py with class Agent(BaseAgent)."
        )
    validate_agent_source(agent_file)

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

    # ── Load or instantiate agent ───────────────────────────────────────────
    if model_path is not None:
        # NN-based agent with saved weights
        load_fn = getattr(AgentClass, "load", None)
        if load_fn is None:
            raise AttributeError(
                "Agent class must implement load(cls, path, env) classmethod "
                "when a model file is provided."
            )
        return load_fn(model_path, RestrictedGameEnvironment(env))

    # No model file → rule-based bot or agent that doesn't need weight loading
    # Try classmethod load with None first, then fall back to constructor
    load_fn = getattr(AgentClass, "load", None)
    if load_fn is not None:
        try:
            return load_fn(None, RestrictedGameEnvironment(env))
        except (TypeError, ValueError, NotImplementedError, FileNotFoundError):
            pass
    return AgentClass(RestrictedGameEnvironment(env))


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
        "--model", default=None,
        help="Filename of model weights inside submission directory. "
             "Auto-detected if not specified (model.pt / model.zip / *.pt / *.zip). "
             "Omit for rule-based agents."
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

    # ── Resolve model path ──────────────────────────────────────────────────
    model_path: Optional[str] = None
    if args.model is not None:
        # Explicitly specified: must exist
        candidate = str(sub / args.model)
        if not os.path.exists(candidate):
            print(f"[ERROR] Model file not found: {candidate}")
            sys.exit(1)
        model_path = candidate
    else:
        # Auto-detect: try common names, then glob
        for name in ("model.pt", "model.zip"):
            candidate = sub / name
            if candidate.exists():
                model_path = str(candidate)
                break
        if model_path is None:
            pt_files = sorted(sub.glob("*.pt"))
            zip_files = sorted(sub.glob("*.zip"))
            if pt_files:
                model_path = str(pt_files[0])
            elif zip_files:
                model_path = str(zip_files[0])

    # ── Config ──────────────────────────────────────────────────────────────
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
    print(f"  model      : {model_path if model_path else '(none — rule-based agent)'}")
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
