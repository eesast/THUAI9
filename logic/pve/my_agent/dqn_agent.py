"""
Custom DQN agent — built from scratch with PyTorch.

**REFERENCE ONLY — 选手禁止在提交中直接使用此类。**
你必须参考此实现的结构和思路，用你自己的方式重新实现。

Demonstrates how to extend BaseAgent without relying on SB3.
The agent ONLY interacts with the environment through the public
Gymnasium interface (reset / step / action_masks).
"""
from __future__ import annotations

import os
import random
from collections import deque
from typing import Any, Dict, List, Optional, Tuple

import numpy as np

import torch
import torch.nn as nn
import torch.optim as optim

from GameLogic import GameEnvironment, GameConfig, N_ACTIONS
from RLInterfaces import BaseAgent


# ── Q-Network ──────────────────────────────────────────────────────────────────

class QNetwork(nn.Module):
    """Small MLP: 32-dim obs → 128 → 128 → 8 (Q-values per action)."""

    def __init__(self, obs_dim: int = 32, n_actions: int = 8):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(obs_dim, 128),
            nn.ReLU(),
            nn.Linear(128, 128),
            nn.ReLU(),
            nn.Linear(128, n_actions),
        )

    def forward(self, obs: torch.Tensor) -> torch.Tensor:
        return self.net(obs)


# ── Replay Buffer ──────────────────────────────────────────────────────────────

class ReplayBuffer:
    def __init__(self, capacity: int = 100_000):
        self.buffer = deque(maxlen=capacity)

    def push(
        self,
        obs: np.ndarray,
        action: int,
        reward: float,
        next_obs: np.ndarray,
        done: bool,
    ):
        self.buffer.append((obs, action, reward, next_obs, done))

    def sample(self, batch_size: int) -> Tuple[Dict[str, np.ndarray], np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
        batch = random.sample(self.buffer, min(batch_size, len(self.buffer)))
        obs, act, rew, nxt, don = zip(*batch)
        return (
            np.stack(obs), np.array(act), np.array(rew, dtype=np.float32),
            np.stack(nxt), np.array(don, dtype=np.float32),
        )

    def __len__(self) -> int:
        return len(self.buffer)


# ── DQN Agent ──────────────────────────────────────────────────────────────────

class DQNAgent(BaseAgent):
    """
    Double DQN agent with action masking and epsilon-greedy exploration.

    Inherits from BaseAgent: all env access goes through self.reset() /
    self.step().  Never touches self.env internals.
    """

    def __init__(
        self,
        env: GameEnvironment,
        # Network
        hidden_dim: int = 128,
        # RL hyperparams
        lr: float = 1e-4,
        gamma: float = 0.99,
        epsilon_start: float = 1.0,
        epsilon_end: float = 0.05,
        epsilon_decay: int = 50_000,
        target_update_freq: int = 2000,
        # Replay
        buffer_capacity: int = 100_000,
        batch_size: int = 128,
        # Training
        learning_starts: int = 5000,
        train_freq: int = 4,
        # Misc
        device: Optional[str] = None,
    ):
        super().__init__(env)
        self.obs_dim = env.observation_space.shape[0]
        self.n_actions = N_ACTIONS

        # Networks
        self.device = device or ("cuda" if torch.cuda.is_available() else "cpu")
        self.q_net = QNetwork(self.obs_dim, self.n_actions).to(self.device)
        self.target_net = QNetwork(self.obs_dim, self.n_actions).to(self.device)
        self.target_net.load_state_dict(self.q_net.state_dict())
        self.target_net.eval()

        self.optimizer = optim.Adam(self.q_net.parameters(), lr=lr)
        self.loss_fn = nn.SmoothL1Loss()

        # Hyperparams
        self.gamma = gamma
        self.epsilon_start = epsilon_start
        self.epsilon = epsilon_start
        self.epsilon_end = epsilon_end
        self.epsilon_decay = epsilon_decay
        self.target_update_freq = target_update_freq
        self.batch_size = batch_size
        self.learning_starts = learning_starts
        self.train_freq = train_freq

        # Replay buffer
        self.buffer = ReplayBuffer(buffer_capacity)

        # Counters
        self._total_steps: int = 0
        self._train_steps: int = 0

    # ── Policy ──────────────────────────────────────────────────────────────

    def get_action(self, observation: np.ndarray) -> int:
        """Epsilon-greedy with action masking."""
        mask = self.env.action_masks()
        valid_actions = np.where(mask)[0]

        if len(valid_actions) == 0:
            return 0  # fallback: WAIT

        if random.random() < self.epsilon:
            return int(random.choice(valid_actions))

        obs_t = torch.as_tensor(observation, dtype=torch.float32, device=self.device).unsqueeze(0)
        with torch.no_grad():
            q_values = self.q_net(obs_t).cpu().numpy().flatten()
        # Mask invalid actions
        q_values[~mask] = -1e9
        return int(np.argmax(q_values))

    # ── Training loop ───────────────────────────────────────────────────────

    def train(self, total_timesteps: int, **kwargs) -> Dict[str, Any]:
        obs = self.reset()
        episode_rewards: List[float] = []
        ep_reward = 0.0

        for t in range(total_timesteps):
            action = self.get_action(obs)
            next_obs, reward, terminated, truncated, info = self.step(action)
            done = terminated or truncated

            self.buffer.push(obs, action, reward, next_obs, done)
            ep_reward += reward
            self._total_steps += 1

            # Decay epsilon
            self.epsilon = max(
                self.epsilon_end,
                self.epsilon_start - (self.epsilon_start - self.epsilon_end)
                * self._total_steps / self.epsilon_decay,
            )

            obs = next_obs

            if done:
                episode_rewards.append(ep_reward)
                ep_reward = 0.0
                obs = self.reset()

            # Train step
            if len(self.buffer) >= self.learning_starts and self._total_steps % self.train_freq == 0:
                self._train_step()

            # Update target network
            if self._total_steps % self.target_update_freq == 0:
                self.target_net.load_state_dict(self.q_net.state_dict())

            # Log
            if (t + 1) % 10000 == 0:
                mean_r = sum(episode_rewards[-20:]) / max(1, len(episode_rewards[-20:]))
                print(
                    f"Step {t + 1:>8,} | "
                    f"eps={self.epsilon:.3f} | "
                    f"buffer={len(self.buffer):,} | "
                    f"mean_reward(20)={mean_r:+.3f} | "
                    f"episodes={len(episode_rewards)}"
                )

        return {
            "total_timesteps": total_timesteps,
            "episodes": len(episode_rewards),
            "mean_reward_last20": (
                sum(episode_rewards[-20:]) / max(1, min(20, len(episode_rewards)))
                if episode_rewards else 0.0
            ),
            "epsilon_final": self.epsilon,
            "train_steps": self._train_steps,
        }

    def _train_step(self):
        obs_b, act_b, rew_b, nxt_b, don_b = self.buffer.sample(self.batch_size)

        obs_t = torch.as_tensor(obs_b, dtype=torch.float32, device=self.device)
        act_t = torch.as_tensor(act_b, dtype=torch.int64, device=self.device).unsqueeze(1)
        rew_t = torch.as_tensor(rew_b, dtype=torch.float32, device=self.device).unsqueeze(1)
        nxt_t = torch.as_tensor(nxt_b, dtype=torch.float32, device=self.device)
        don_t = torch.as_tensor(don_b, dtype=torch.float32, device=self.device).unsqueeze(1)

        # Current Q
        q_curr = self.q_net(obs_t).gather(1, act_t)

        # Double DQN target
        with torch.no_grad():
            q_next_online = self.q_net(nxt_t)
            best_actions = q_next_online.argmax(dim=1, keepdim=True)
            q_next_target = self.target_net(nxt_t).gather(1, best_actions)
            q_target = rew_t + self.gamma * (1.0 - don_t) * q_next_target

        loss = self.loss_fn(q_curr, q_target)
        self.optimizer.zero_grad()
        loss.backward()
        torch.nn.utils.clip_grad_norm_(self.q_net.parameters(), 10.0)
        self.optimizer.step()
        self._train_steps += 1

    # ── Persistence ─────────────────────────────────────────────────────────

    def save(self, path: str):
        os.makedirs(os.path.dirname(path) if os.path.dirname(path) else ".", exist_ok=True)
        data = {
            "q_net_state": self.q_net.state_dict(),
            "target_net_state": self.target_net.state_dict(),
            "optimizer_state": self.optimizer.state_dict(),
            "epsilon": self.epsilon,
            "total_steps": self._total_steps,
            "train_steps": self._train_steps,
            "hyperparams": {
                "obs_dim": self.obs_dim,
                "hidden_dim": 128,
                "gamma": self.gamma,
                "epsilon_decay": self.epsilon_decay,
            },
        }
        torch.save(data, path)
        print(f"[DQNAgent] saved to {path}")

    @classmethod
    def load(cls, path: str, env: GameEnvironment) -> "DQNAgent":
        data = torch.load(path, map_location="cpu", weights_only=False)
        agent = cls(
            env,
            hidden_dim=data["hyperparams"]["hidden_dim"],
            gamma=data["hyperparams"]["gamma"],
            epsilon_decay=data["hyperparams"]["epsilon_decay"],
        )
        agent.q_net.load_state_dict(data["q_net_state"])
        agent.target_net.load_state_dict(data["target_net_state"])
        agent.optimizer.load_state_dict(data["optimizer_state"])
        agent.epsilon = data["epsilon"]
        agent._total_steps = data["total_steps"]
        agent._train_steps = data["train_steps"]
        agent.q_net.eval()
        return agent
