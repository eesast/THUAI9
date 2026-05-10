"""
PPO agent backed by stable_baselines3.

Uses MaskablePPO (sb3-contrib) when action masking is available,
falls back to standard PPO otherwise.
"""
from __future__ import annotations
import os
from typing import Any, Dict, Optional

import numpy as np

from GameLogic import GameEnvironment
from .base_agent import BaseAgent

try:
    from sb3_contrib import MaskablePPO
    from sb3_contrib.common.wrappers import ActionMasker
    _HAS_MASK = True
except ImportError:
    _HAS_MASK = False

from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import (
    BaseCallback, EvalCallback, CheckpointCallback,
)
from stable_baselines3.common.monitor import Monitor


class PerformanceCallback(BaseCallback):
    """
    Fires `on_threshold_reached` when mean episode reward
    crosses a configurable threshold.
    """

    def __init__(self, threshold: float, window: int = 20, verbose: int = 0):
        super().__init__(verbose)
        self.threshold = threshold
        self.window = window
        self.threshold_reached = False
        self._ep_rewards: list = []

    def _on_step(self) -> bool:
        infos = self.locals.get("infos", [])
        for info in infos:
            if "episode" in info:
                self._ep_rewards.append(info["episode"]["r"])
        if len(self._ep_rewards) >= self.window:
            mean = sum(self._ep_rewards[-self.window:]) / self.window
            if mean >= self.threshold and not self.threshold_reached:
                self.threshold_reached = True
                if self.verbose:
                    print(f"[Callback] Threshold {self.threshold:.2f} reached! "
                          f"mean({self.window})={mean:.2f}")
        return True


def _mask_fn(env) -> np.ndarray:
    return env.unwrapped.action_masks()


class PPOAgent(BaseAgent):
    """
    Wraps stable_baselines3 (Maskable)PPO as a BaseAgent.

    Parameters
    ----------
    env          : GameEnvironment instance
    learning_rate: Adam LR
    n_steps      : rollout buffer size (steps between updates)
    batch_size   : minibatch size
    n_epochs     : PPO update epochs
    gamma        : discount factor
    ent_coef     : entropy coefficient
    use_masking  : whether to use action masking (requires sb3_contrib)
    """

    def __init__(
        self,
        env: GameEnvironment,
        learning_rate: float = 3e-4,
        n_steps: int = 2048,
        batch_size: int = 64,
        n_epochs: int = 10,
        gamma: float = 0.99,
        ent_coef: float = 0.01,
        use_masking: bool = True,
        verbose: int = 1,
    ):
        super().__init__(env)
        self._verbose = verbose
        self._use_masking = use_masking and _HAS_MASK

        wrapped_env = Monitor(env)

        ppo_kwargs = dict(
            policy="MlpPolicy",
            env=wrapped_env,
            learning_rate=learning_rate,
            n_steps=n_steps,
            batch_size=batch_size,
            n_epochs=n_epochs,
            gamma=gamma,
            ent_coef=ent_coef,
            verbose=verbose,
        )

        if self._use_masking:
            masked_env = ActionMasker(wrapped_env, _mask_fn)
            ppo_kwargs["env"] = masked_env
            self._model = MaskablePPO(**ppo_kwargs)
        else:
            self._model = PPO(**ppo_kwargs)

    # ── BaseAgent interface ────────────────────────────────────────────────────

    def get_action(self, observation: np.ndarray) -> int:
        action, _ = self._model.predict(observation, deterministic=True)
        return int(action)

    def train(
        self,
        total_timesteps: int,
        save_dir: str = "models/",
        eval_freq: int = 4096,
        performance_threshold: Optional[float] = None,
        **kwargs,
    ) -> Dict[str, Any]:
        os.makedirs(save_dir, exist_ok=True)

        callbacks = []

        perf_cb = PerformanceCallback(
            threshold=performance_threshold or float("inf"),
            verbose=self._verbose,
        )
        callbacks.append(perf_cb)

        ckpt_cb = CheckpointCallback(
            save_freq=max(eval_freq, 8192),
            save_path=save_dir,
            name_prefix="ppo_thuai9",
        )
        callbacks.append(ckpt_cb)

        self._model.learn(
            total_timesteps=total_timesteps,
            callback=callbacks,
            reset_num_timesteps=True,
        )

        best_path = os.path.join(save_dir, "ppo_thuai9_best")
        self._model.save(best_path)

        return {
            "total_timesteps": total_timesteps,
            "threshold_reached": perf_cb.threshold_reached,
            "episode_count": len(perf_cb._ep_rewards),
            "mean_reward_last20": (
                sum(perf_cb._ep_rewards[-20:]) / max(1, min(20, len(perf_cb._ep_rewards)))
                if perf_cb._ep_rewards else 0.0
            ),
        }

    def save(self, path: str):
        self._model.save(path)

    @classmethod
    def load(cls, path: str, env: GameEnvironment) -> "PPOAgent":
        agent = cls.__new__(cls)
        BaseAgent.__init__(agent, env)
        agent._use_masking = _HAS_MASK
        if _HAS_MASK:
            agent._model = MaskablePPO.load(path, env=env)
        else:
            agent._model = PPO.load(path, env=env)
        return agent
