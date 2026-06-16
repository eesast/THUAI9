import os
import sys
from pathlib import Path

import pytest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from GameLogic import GameConfig, GameEnvironment
from RLInterfaces import BaseAgent, RestrictedGameEnvironment
from official_evaluator import SubmissionRuleError, load_agent, validate_agent_source


def _write_agent(tmp_path: Path, source: str) -> Path:
    agent_file = tmp_path / "agent.py"
    agent_file.write_text(source, encoding="utf-8")
    return agent_file


def test_agent_source_allows_documented_env_methods(tmp_path):
    agent_file = _write_agent(
        tmp_path,
        """
from GameLogic import Action, N_ACTIONS
from RLInterfaces import BaseAgent

class Agent(BaseAgent):
    def get_action(self, obs):
        mask = self.env.action_masks()
        assert N_ACTIONS > int(Action.WAIT)
        return int(mask.argmax())

    def train(self, total_timesteps, **kwargs):
        obs = self.reset()
        obs, reward, terminated, truncated, info = self.step(0)
        return {"score": info.get("score", 0.0)}
""",
    )

    validate_agent_source(agent_file)


def test_agent_source_rejects_direct_env_internal_access(tmp_path):
    agent_file = _write_agent(
        tmp_path,
        """
from RLInterfaces import BaseAgent

class Agent(BaseAgent):
    def get_action(self, obs):
        return 0 if self.env.money >= 0 else 1

    def train(self, total_timesteps, **kwargs):
        return {}
""",
    )

    with pytest.raises(SubmissionRuleError, match="env.money"):
        validate_agent_source(agent_file)


def test_agent_source_rejects_env_alias_internal_access(tmp_path):
    agent_file = _write_agent(
        tmp_path,
        """
from RLInterfaces import BaseAgent

class Agent(BaseAgent):
    def get_action(self, obs):
        env = self.env
        return 0 if env.unit.x == 0 else 1

    def train(self, total_timesteps, **kwargs):
        return {}
""",
    )

    with pytest.raises(SubmissionRuleError, match="env.unit"):
        validate_agent_source(agent_file)


def test_agent_source_rejects_gamelogic_import(tmp_path):
    agent_file = _write_agent(
        tmp_path,
        """
from RLInterfaces import BaseAgent
from GameLogic.market import Market

class Agent(BaseAgent):
    def get_action(self, obs):
        return 0

    def train(self, total_timesteps, **kwargs):
        return {}
""",
    )

    with pytest.raises(SubmissionRuleError, match="GameLogic"):
        validate_agent_source(agent_file)


def test_load_agent_passes_restricted_env_to_model_loader(tmp_path):
    agent_file = _write_agent(
        tmp_path,
        """
from RLInterfaces import BaseAgent

class Agent(BaseAgent):
    loaded_env = None

    @classmethod
    def load(cls, path, env):
        cls.loaded_env = env
        return cls(env)

    def get_action(self, obs):
        return 0

    def train(self, total_timesteps, **kwargs):
        return {}
""",
    )
    model_file = tmp_path / "model.pt"
    model_file.write_text("dummy", encoding="utf-8")

    env = GameEnvironment(cfg=GameConfig.easy(), seed=1)
    agent = load_agent(str(tmp_path), str(model_file), env)

    assert isinstance(agent, BaseAgent)
    assert isinstance(agent.env, RestrictedGameEnvironment)
    with pytest.raises(AttributeError):
        getattr(agent.env, "unit")
