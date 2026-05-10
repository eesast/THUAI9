"""
Tests for RLInterfaces: BaseAgent protocol, PPOAgent compatibility.
Run with: python -m pytest tests/test_interfaces.py -v
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np
import pytest

from GameLogic import GameConfig, GameEnvironment
from GameLogic.action_space import Action, N_ACTIONS
from RLInterfaces import BaseAgent, PPOAgent, TrainingLoop, TrainingMetrics


# ── Minimal concrete agent for testing BaseAgent protocol ─────────────────────

class RandomAgent(BaseAgent):
    """Selects actions uniformly at random."""

    def get_action(self, observation: np.ndarray) -> int:
        return int(np.random.randint(0, N_ACTIONS))

    def train(self, total_timesteps: int, **kwargs):
        # Minimal: just run random episodes
        obs = self.reset()
        for _ in range(total_timesteps):
            action = self.get_action(obs)
            obs, _, terminated, truncated, _ = self.step(action)
            if terminated or truncated:
                obs = self.reset()
        return {"total_timesteps": total_timesteps}


@pytest.fixture
def easy_env():
    return GameEnvironment(cfg=GameConfig.easy(), seed=1)


# ── BaseAgent protocol ────────────────────────────────────────────────────────

def test_random_agent_can_run_episode(easy_env):
    agent = RandomAgent(easy_env)
    obs = agent.reset()
    assert obs.shape == (GameEnvironment.OBS_DIM,)
    done = False
    steps = 0
    while not done and steps < 1000:
        action = agent.get_action(obs)
        obs, reward, terminated, truncated, info = agent.step(action)
        done = terminated or truncated
        steps += 1
    assert steps > 0, "Episode completed zero steps"
    assert agent.episode_count() >= 1


def test_random_agent_episode_rewards_recorded(easy_env):
    agent = RandomAgent(easy_env)
    for _ in range(3):
        obs = agent.reset()
        done = False
        while not done:
            obs, _, term, trunc, _ = agent.step(agent.get_action(obs))
            done = term or trunc
    assert agent.episode_count() == 3
    assert isinstance(agent.mean_episode_reward(), float)


def test_agent_cannot_access_env_internals(easy_env):
    """Verify BaseAgent wraps env cleanly - test is structural/doctest."""
    agent = RandomAgent(easy_env)
    # Agent step should mirror env step
    obs = agent.reset()
    _, reward_via_agent, _, _, _ = agent.step(Action.WAIT)
    # Re-run on fresh env for comparison
    easy_env.reset()
    _, reward_via_env, _, _, _ = easy_env.step(Action.WAIT)
    # Rewards should be of same type
    assert isinstance(reward_via_agent, float)
    assert isinstance(reward_via_env, float)


# ── TrainingLoop ──────────────────────────────────────────────────────────────

def test_training_loop_runs(easy_env):
    agent = RandomAgent(easy_env)
    loop = TrainingLoop(agent, easy_env, log_every=0)
    metrics = loop.run(max_episodes=5)
    assert isinstance(metrics, TrainingMetrics)
    assert metrics.timesteps > 0
    assert len(metrics.episode_rewards) == 5


def test_training_loop_breakthrough_callback(easy_env):
    """Breakthrough fires when mean reward exceeds threshold."""
    agent = RandomAgent(easy_env)
    events = []

    def on_breakthrough(event):
        events.append(event)

    # Set a very low threshold to guarantee it fires
    loop = TrainingLoop(
        agent, easy_env,
        breakthrough_threshold=-999.0,   # always trigger
        breakthrough_window=2,
        on_breakthrough=on_breakthrough,
        log_every=0,
    )
    loop.run(max_episodes=10)
    assert len(events) == 1, "Breakthrough callback should fire exactly once"


# ── PPOAgent (smoke test – no heavy training) ─────────────────────────────────

def test_ppo_agent_can_predict_without_training(easy_env):
    """PPOAgent should produce valid actions even before training."""
    agent = PPOAgent(easy_env, n_steps=64, verbose=0)
    obs, _ = easy_env.reset()
    action = agent.get_action(obs)
    assert 0 <= action < N_ACTIONS


def test_ppo_agent_short_train(easy_env):
    """PPOAgent.train() should complete without error on minimal timesteps."""
    agent = PPOAgent(easy_env, n_steps=64, batch_size=32, verbose=0)
    metrics = agent.train(total_timesteps=256, save_dir="models/test/")
    assert "total_timesteps" in metrics
    assert metrics["total_timesteps"] == 256
