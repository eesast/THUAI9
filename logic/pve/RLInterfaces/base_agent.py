"""
BaseAgent: abstract interface that all RL algorithms must implement.

Algorithms interact with GameEnvironment ONLY through this interface.
They must never access or modify env internals directly.
"""
from __future__ import annotations
from abc import ABC, abstractmethod
from typing import Any, Dict, Optional, Tuple

import numpy as np

from GameLogic import GameEnvironment


class RestrictedGameEnvironment:
    """
    Contest-facing environment facade.

    The official evaluator gives agents this object instead of the real
    GameEnvironment so submissions can only use the documented interaction
    methods.
    """

    __slots__ = ("__env",)

    _ALLOWED = frozenset({"reset", "step", "action_masks"})

    def __init__(self, env: GameEnvironment):
        object.__setattr__(self, "_RestrictedGameEnvironment__env", env)

    def reset(self, *args, **kwargs):
        env = object.__getattribute__(self, "_RestrictedGameEnvironment__env")
        return env.reset(*args, **kwargs)

    def step(self, *args, **kwargs):
        env = object.__getattribute__(self, "_RestrictedGameEnvironment__env")
        return env.step(*args, **kwargs)

    def action_masks(self) -> np.ndarray:
        env = object.__getattribute__(self, "_RestrictedGameEnvironment__env")
        return env.action_masks()

    def __getattribute__(self, name: str):
        if name.startswith("_") or name not in RestrictedGameEnvironment._ALLOWED:
            raise AttributeError(
                f"GameEnvironment attribute '{name}' is not available to agents; "
                "use only reset(), step(), and action_masks()."
            )
        return object.__getattribute__(self, name)

    def __setattr__(self, name: str, value) -> None:
        raise AttributeError("Contest agents cannot mutate the environment facade.")


class BaseAgent(ABC):
    """
    Standard agent interface.

    Subclasses implement `get_action` (policy) and `train`.
    The base class provides the reset/step wrappers so algorithms
    cannot bypass the env boundary.
    """

    def __init__(self, env: GameEnvironment):
        self.env = env
        self._episode_rewards: list = []
        self._current_ep_reward: float = 0.0

    # ── Env passthrough (read-only helpers) ───────────────────────────────────

    def reset(self) -> np.ndarray:
        """Reset environment; return initial observation."""
        obs, _ = self.env.reset()
        self._current_ep_reward = 0.0
        return obs

    def step(self, action: int) -> Tuple[np.ndarray, float, bool, bool, dict]:
        """Execute one step; accumulate episode reward."""
        obs, reward, terminated, truncated, info = self.env.step(action)
        self._current_ep_reward += reward
        if terminated or truncated:
            self._episode_rewards.append(self._current_ep_reward)
            self._current_ep_reward = 0.0
        return obs, reward, terminated, truncated, info

    # ── Policy interface ───────────────────────────────────────────────────────

    @abstractmethod
    def get_action(self, observation: np.ndarray) -> int:
        """
        Map observation → action id.
        Must not access self.env internals.
        """
        raise NotImplementedError

    @abstractmethod
    def train(self, total_timesteps: int, **kwargs) -> Dict[str, Any]:
        """
        Run training loop for `total_timesteps` env steps.
        Returns a dict of training metrics.
        """
        raise NotImplementedError

    def save(self, path: str):
        """Persist the policy to disk."""
        raise NotImplementedError

    @classmethod
    def load(cls, path: str, env: GameEnvironment) -> "BaseAgent":
        """Load a saved policy."""
        raise NotImplementedError

    # ── Stats ─────────────────────────────────────────────────────────────────

    def mean_episode_reward(self, last_n: int = 20) -> float:
        if not self._episode_rewards:
            return 0.0
        recent = self._episode_rewards[-last_n:]
        return sum(recent) / len(recent)

    def episode_count(self) -> int:
        return len(self._episode_rewards)
