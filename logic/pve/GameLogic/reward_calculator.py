"""Reward calculator: converts game state transitions into scalar RL rewards.

Default mode is a dense, learnable signal for PPO/DQN. The adversarial
"harvest trap" design remains available via RewardConfig(mode="adversarial").
"""
from __future__ import annotations
from dataclasses import dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from .game_env import GameEnvironment


@dataclass
class RewardConfig:
    # Reward mode: "standard" (learnable) or "adversarial" (harvest trap)
    mode: str = "standard"

    # Per-step money delta scale
    money_scale: float = 0.02

    # Per-step time cost (encourages doing *something*, but not enough to overcome
    # the harvest bonus on its own)
    time_penalty: float = -0.001

    # Harvest bonus per unit (standard default 0; adversarial uses small positive trap)
    harvest_bonus_per_unit: float = 0.0

    # Terminal bonus: final_score × scale, given only at episode end.
    terminal_score_scale: float = 0.0

    # Extra dense signal for selling: score_delta × scale
    sell_bonus_scale: float = 0.001

    # Penalty for attempting an invalid action
    invalid_action_penalty: float = -0.02

    # Terminal penalty for going bankrupt
    bankruptcy_penalty: float = -10.0

    # One-off bonuses for game progress (kept for backward compatibility)
    compute_center_bonus: float = 1.0
    tech_bonus: float = 1.0


class RewardCalculator:
    def __init__(self, cfg: RewardConfig = None):
        self.cfg = cfg or RewardConfig()
        self._prev_money = 0.0
        self._prev_score = 0.0
        self._prev_open_centers = 0
        self._prev_techs = 0

    def reset(self, env: "GameEnvironment"):
        self._prev_money = env.money
        self._prev_score = env.score
        self._prev_open_centers = sum(1 for cc in env.board.compute_centers if cc.is_open)
        self._prev_techs = len(env._techs_owned)

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

        # ── Harvest bonus ───────────────────────────────────────────────────
        reward += harvested * cfg.harvest_bonus_per_unit

        # ── Invalid action penalty ──────────────────────────────────────────
        if not action_was_valid:
            reward += cfg.invalid_action_penalty

        # ── Bankruptcy ──────────────────────────────────────────────────────
        if env.money < 0:
            reward += cfg.bankruptcy_penalty

        if cfg.mode == "adversarial":
            if terminated:
                reward += env.score * cfg.terminal_score_scale
            return float(reward)

        # ── Standard learnable signal ───────────────────────────────────────
        money_delta = env.money - self._prev_money
        reward += money_delta * cfg.money_scale

        score_delta = env.score - self._prev_score
        if score_delta > 0:
            reward += score_delta * cfg.sell_bonus_scale

        opened = sum(1 for cc in env.board.compute_centers if cc.is_open)
        if opened > self._prev_open_centers:
            reward += (opened - self._prev_open_centers) * cfg.compute_center_bonus

        techs = len(env._techs_owned)
        if techs > self._prev_techs:
            reward += (techs - self._prev_techs) * cfg.tech_bonus

        if terminated and cfg.terminal_score_scale != 0.0:
            reward += env.score * cfg.terminal_score_scale

        self._prev_money = env.money
        self._prev_score = env.score
        self._prev_open_centers = opened
        self._prev_techs = techs

        return float(reward)
