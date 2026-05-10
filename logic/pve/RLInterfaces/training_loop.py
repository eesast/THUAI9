"""
Standard training loop with performance monitoring and difficulty progression.

The loop tracks episode rewards and fires a callback when a breakthrough
threshold is crossed (used to trigger game rule upgrades in ITERATIONS.md).
"""
from __future__ import annotations
import time
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Tuple

import numpy as np

from GameLogic import GameEnvironment, GameConfig
from .base_agent import BaseAgent


@dataclass
class TrainingMetrics:
    episode_rewards: List[float] = field(default_factory=list)
    episode_scores:  List[float] = field(default_factory=list)
    episode_lengths: List[int]   = field(default_factory=list)
    timesteps: int = 0
    wall_time: float = 0.0

    def mean_reward(self, last_n: int = 20) -> float:
        r = self.episode_rewards[-last_n:]
        return sum(r) / len(r) if r else 0.0

    def mean_score(self, last_n: int = 20) -> float:
        s = self.episode_scores[-last_n:]
        return sum(s) / len(s) if s else 0.0

    def summary(self) -> Dict:
        return {
            "episodes": len(self.episode_rewards),
            "timesteps": self.timesteps,
            "mean_reward_20": self.mean_reward(20),
            "mean_score_20": self.mean_score(20),
            "best_reward": max(self.episode_rewards, default=0.0),
            "best_score": max(self.episode_scores, default=0.0),
            "wall_time_s": self.wall_time,
        }


@dataclass
class BreakthroughEvent:
    episode: int
    timestep: int
    mean_reward: float
    mean_score: float
    threshold: float


class TrainingLoop:
    """
    Manual episode-level training loop that works with any BaseAgent.

    For SB3-backed agents (PPOAgent), prefer calling agent.train() directly;
    use this loop for custom agents or when fine-grained step callbacks matter.
    """

    def __init__(
        self,
        agent: BaseAgent,
        env: GameEnvironment,
        breakthrough_threshold: float = 5.0,
        breakthrough_window: int = 20,
        on_breakthrough: Optional[Callable[[BreakthroughEvent], None]] = None,
        log_every: int = 10,
    ):
        self.agent = agent
        self.env = env
        self.threshold = breakthrough_threshold
        self.window = breakthrough_window
        self.on_breakthrough = on_breakthrough
        self.log_every = log_every

        self.metrics = TrainingMetrics()
        self._breakthrough_triggered = False

    def run(self, max_episodes: int = 500) -> TrainingMetrics:
        start = time.time()

        for ep in range(max_episodes):
            obs = self.agent.reset()
            ep_reward = 0.0
            ep_score = 0.0
            ep_len = 0
            done = False

            while not done:
                action = self.agent.get_action(obs)
                obs, reward, terminated, truncated, info = self.agent.step(action)
                ep_reward += reward
                ep_score = info.get("score", 0.0)
                ep_len += 1
                self.metrics.timesteps += 1
                done = terminated or truncated

            self.metrics.episode_rewards.append(ep_reward)
            self.metrics.episode_scores.append(ep_score)
            self.metrics.episode_lengths.append(ep_len)

            if self.log_every > 0 and (ep + 1) % self.log_every == 0:
                self._log(ep + 1)

            self._check_breakthrough(ep + 1)

        self.metrics.wall_time = time.time() - start
        return self.metrics

    def _log(self, ep: int):
        m = self.metrics
        print(
            f"Ep {ep:5d} | "
            f"reward={m.mean_reward(20):+7.3f} | "
            f"score={m.mean_score(20):8.1f} | "
            f"ts={m.timesteps:,}"
        )

    def _check_breakthrough(self, ep: int):
        if self._breakthrough_triggered:
            return
        if len(self.metrics.episode_rewards) < self.window:
            return
        mean = self.metrics.mean_reward(self.window)
        if mean >= self.threshold:
            self._breakthrough_triggered = True
            event = BreakthroughEvent(
                episode=ep,
                timestep=self.metrics.timesteps,
                mean_reward=mean,
                mean_score=self.metrics.mean_score(self.window),
                threshold=self.threshold,
            )
            print(f"\n{'='*60}")
            print(f"BREAKTHROUGH at episode {ep}!")
            print(f"  mean reward (last {self.window}): {mean:.3f} >= {self.threshold}")
            print(f"  mean score  (last {self.window}): {event.mean_score:.1f}")
            print(f"{'='*60}\n")
            if self.on_breakthrough:
                self.on_breakthrough(event)
