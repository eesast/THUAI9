"""
Reward calculator: converts game state transitions into scalar RL rewards.

Design intent (anti-PPO/DQN):
  - NO per-step money or score signal: removes the dense credit-assignment ladder
    that standard PPO/DQN relies on to learn buy→sell chains.
  - Harvest trap: a small positive reward for harvesting raw materials creates a
    deceptive local optimum. PPO/DQN converges to a harvest-loop policy (stable,
    positive reward per episode) that never produces or sells anything, yielding
    zero game score.
  - Terminal-only score bonus: the true objective is only revealed at episode end.
    With γ=0.99 and ~1200 steps, 0.99^1200 ≈ 0, so gradients from the terminal
    bonus do not propagate back to early actions — PPO/DQN cannot learn from it.
  - Agents with hierarchical reward decomposition, curiosity-driven exploration,
    or hand-crafted sub-goal rewards can still solve the environment.
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from .game_env import GameEnvironment


@dataclass
class RewardConfig:
    # Per-step money / score delta scale — set to 0 to remove dense sell signal
    money_scale: float = 0.0

    # Per-step time cost (encourages doing *something*, but not enough to overcome
    # the harvest bonus on its own)
    time_penalty: float = -0.003

    # Harvest trap: gives a positive per-unit signal that PPO/DQN latches onto.
    # Harvesting alone never produces game score, making this a deceptive optimum.
    harvest_bonus_per_unit: float = 0.01

    # Sparse terminal bonus: final_score × scale, given only at episode end.
    # Too sparse for standard PPO/DQN to credit-assign across ~1200 steps.
    terminal_score_scale: float = 0.001

    # Penalty for attempting an invalid action
    invalid_action_penalty: float = -0.02

    # Terminal penalty for going bankrupt
    bankruptcy_penalty: float = -10.0


class RewardCalculator:
    def __init__(self, cfg: RewardConfig = None):
        self.cfg = cfg or RewardConfig()

    def reset(self, _env: "GameEnvironment"):
        pass  # no per-step state to initialise

    def compute(
        self,
        env: "GameEnvironment",
        action_was_valid: bool,
        harvested: float,
        terminated: bool = False,
    ) -> float:
        cfg = self.cfg
        reward = 0.0

        # ── Time cost (always) ──────────────────────────────────────────────
        reward += cfg.time_penalty

        # ── Harvest trap (deceptive local optimum for PPO/DQN) ──────────────
        reward += harvested * cfg.harvest_bonus_per_unit

        # ── Invalid action penalty ──────────────────────────────────────────
        if not action_was_valid:
            reward += cfg.invalid_action_penalty

        # ── Bankruptcy ──────────────────────────────────────────────────────
        if env.money < 0:
            reward += cfg.bankruptcy_penalty

        # ── Terminal score bonus (sparse; unreachable by standard credit assign)
        if terminated:
            reward += env.score * cfg.terminal_score_scale

        return float(reward)
