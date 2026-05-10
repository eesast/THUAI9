"""
Reward calculator: converts game state transitions into scalar RL rewards.

Design principles:
  - Primary: sales revenue (direct score signal)
  - Secondary: small shaping bonuses for progress sub-goals
  - Penalties: time cost, invalid actions, bankruptcy
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from .game_env import GameEnvironment


@dataclass
class RewardConfig:
    # Scale factor applied to money gained/lost
    money_scale: float = 0.01

    # Per-step time penalty (encourages efficient routes)
    time_penalty: float = -0.002

    # Reward for harvesting resources (normalized)
    harvest_bonus_per_unit: float = 0.001

    # Reward for opening a compute center
    compute_center_bonus: float = 0.5

    # Reward for buying a tech upgrade (one-time)
    tech_bonus: float = 1.0

    # Penalty for attempting an invalid action
    invalid_action_penalty: float = -0.05

    # Terminal rewards
    bankruptcy_penalty: float = -10.0


class RewardCalculator:
    def __init__(self, cfg: RewardConfig = None):
        self.cfg = cfg or RewardConfig()

        # Tracked across steps for shaping
        self._prev_money: float = 0.0
        self._prev_compute: float = 0.0
        self._prev_score: float = 0.0
        self._prev_open_centers: int = 0

    def reset(self, env: "GameEnvironment"):
        self._prev_money = env.money
        self._prev_compute = env.compute
        self._prev_score = env.score
        self._prev_open_centers = sum(1 for cc in env.board.compute_centers if cc.is_open)

    def compute(
        self,
        env: "GameEnvironment",
        action_was_valid: bool,
        harvested: float,
    ) -> float:
        cfg = self.cfg
        reward = 0.0

        # ── Money delta ─────────────────────────────────────────────────────
        money_delta = env.money - self._prev_money
        reward += money_delta * cfg.money_scale
        self._prev_money = env.money

        # ── Score delta (direct optimization target) ────────────────────────
        score_delta = env.score - self._prev_score
        reward += score_delta * cfg.money_scale
        self._prev_score = env.score

        # ── Time penalty ────────────────────────────────────────────────────
        reward += cfg.time_penalty

        # ── Harvest shaping ─────────────────────────────────────────────────
        reward += harvested * cfg.harvest_bonus_per_unit

        # ── Compute center unlocked ─────────────────────────────────────────
        open_centers = sum(1 for cc in env.board.compute_centers if cc.is_open)
        if open_centers > self._prev_open_centers:
            reward += cfg.compute_center_bonus
        self._prev_open_centers = open_centers

        # ── Invalid action ──────────────────────────────────────────────────
        if not action_was_valid:
            reward += cfg.invalid_action_penalty

        # ── Bankruptcy ──────────────────────────────────────────────────────
        if env.money < 0:
            reward += cfg.bankruptcy_penalty

        return float(reward)
