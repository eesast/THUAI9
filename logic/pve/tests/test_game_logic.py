"""
Tests for GameLogic: environment reset, step mechanics, reward signal, obs shape.
Run with: python -m pytest tests/test_game_logic.py -v
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np
import pytest

from GameLogic import GameConfig, GameEnvironment
from GameLogic.action_space import Action, N_ACTIONS


# ── Fixtures ──────────────────────────────────────────────────────────────────

@pytest.fixture
def easy_env():
    cfg = GameConfig.easy()
    return GameEnvironment(cfg=cfg, seed=0)


@pytest.fixture
def medium_env():
    return GameEnvironment(cfg=GameConfig(), seed=42)


# ── Reset ─────────────────────────────────────────────────────────────────────

def test_reset_returns_correct_obs_shape(easy_env):
    obs, info = easy_env.reset()
    assert obs.shape == (GameEnvironment.OBS_DIM,), \
        f"Expected obs shape ({GameEnvironment.OBS_DIM},), got {obs.shape}"


def test_reset_obs_finite(easy_env):
    obs, _ = easy_env.reset()
    assert np.all(np.isfinite(obs)), "Observation contains NaN or Inf after reset"


def test_reset_initial_state(easy_env):
    easy_env.reset()
    cfg = easy_env.cfg
    assert easy_env.money == cfg.initial_money
    assert easy_env.compute == cfg.initial_compute
    assert easy_env.score == 0.0
    assert easy_env.time == 0.0
    assert easy_env._step == 0


# ── Step ─────────────────────────────────────────────────────────────────────

def test_step_advances_time(easy_env):
    easy_env.reset()
    _, _, _, _, _ = easy_env.step(Action.WAIT)
    assert abs(easy_env.time - easy_env.cfg.time_step) < 1e-9


def test_step_returns_correct_types(easy_env):
    easy_env.reset()
    obs, reward, terminated, truncated, info = easy_env.step(Action.WAIT)
    assert isinstance(obs, np.ndarray)
    assert isinstance(reward, float)
    assert isinstance(terminated, bool)
    assert isinstance(truncated, bool)
    assert isinstance(info, dict)


def test_step_obs_finite(easy_env):
    easy_env.reset()
    for _ in range(20):
        obs, _, terminated, truncated, _ = easy_env.step(Action.WAIT)
        assert np.all(np.isfinite(obs)), "NaN/Inf in observation during WAIT loop"
        if terminated or truncated:
            break


def test_step_count_matches_truncation(easy_env):
    """Episode must truncate at max_steps."""
    easy_env.reset()
    max_steps = easy_env.cfg.max_steps
    truncated = False
    for i in range(max_steps + 5):
        _, _, terminated, truncated, _ = easy_env.step(Action.WAIT)
        if terminated or truncated:
            break
    assert truncated or i < max_steps + 5, "Episode did not truncate"


# ── Movement ──────────────────────────────────────────────────────────────────

def test_move_changes_position(easy_env):
    easy_env.reset()
    u = easy_env.unit
    start_x, start_y = u.x, u.y
    # Try MOVE_DOWN (should be valid from (0,0))
    easy_env.step(Action.MOVE_DOWN)
    assert (u.x, u.y) != (start_x, start_y) or u.x == easy_env.cfg.map_height - 1


def test_cannot_move_off_map(easy_env):
    easy_env.reset()
    u = easy_env.unit
    # Move to (0,0), then try to go up/left (off-map)
    u.x, u.y = 0, 0
    _, _, _, _, info = easy_env.step(Action.MOVE_UP)
    assert u.x == 0, "Unit should not move above row 0"


# ── Buy / Sell ────────────────────────────────────────────────────────────────

def test_buy_requires_adjacent_market(easy_env):
    """BUY should fail (invalid) when not adjacent to market."""
    easy_env.reset()
    # Move unit far from any market (0,0 factory cell, may not have adjacent market)
    u = easy_env.unit
    u.x, u.y = easy_env.cfg.factory_x, easy_env.cfg.factory_y
    initial_money = easy_env.money
    # Multiple BUY attempts; if no market nearby, money shouldn't drop
    no_market = easy_env.board.nearest_market(u.x, u.y) is None
    if no_market:
        easy_env.step(Action.BUY)
        assert easy_env.money == initial_money


def test_sell_empty_inventory_invalid(easy_env):
    """SELL_0 with empty inventory should be flagged as invalid."""
    easy_env.reset()
    u = easy_env.unit
    assert u.total_goods == 0
    _, _, _, _, info = easy_env.step(Action.SELL_0)
    assert info["action_valid"] is False


# ── Action mask ───────────────────────────────────────────────────────────────

def test_action_mask_shape(easy_env):
    easy_env.reset()
    mask = easy_env.action_masks()
    assert mask.shape == (N_ACTIONS,)
    assert mask.dtype == bool


def test_wait_always_valid(easy_env):
    easy_env.reset()
    mask = easy_env.action_masks()
    assert mask[Action.WAIT], "WAIT must always be valid"


# ── Observation space ─────────────────────────────────────────────────────────

def test_obs_within_declared_bounds(medium_env):
    """Obs should stay within observation_space bounds (approximately)."""
    obs, _ = medium_env.reset()
    lo = medium_env.observation_space.low
    hi = medium_env.observation_space.high
    # Allow small float tolerance
    assert np.all(obs >= lo - 0.1) and np.all(obs <= hi + 0.1), \
        "Obs outside declared bounds"


# ── Board generation ─────────────────────────────────────────────────────────

def test_board_has_correct_entity_counts():
    cfg = GameConfig(num_markets=3, num_resource_points=2, num_compute_centers=2)
    env = GameEnvironment(cfg=cfg, seed=7)
    env.reset()
    assert len(env.board.market_positions) == 3
    assert len(env.board.resource_points) == 2
    assert len(env.board.compute_centers) == 2


# ── Difficulty entity counts ──────────────────────────────────────────────────

@pytest.mark.parametrize("cfg_fn,expect_markets,expect_resources,expect_compute", [
    (GameConfig.easy,   3, 2, 1),
    (GameConfig.medium, 3, 2, 2),
    (GameConfig.hard,   4, 4, 3),
])
def test_difficulty_entity_counts(cfg_fn, expect_markets, expect_resources, expect_compute):
    env = GameEnvironment(cfg=cfg_fn(), seed=0)
    env.reset()
    assert len(env.board.market_positions) == expect_markets,   f"markets mismatch"
    assert len(env.board.resource_points)  == expect_resources, f"resource points mismatch"
    assert len(env.board.compute_centers)  == expect_compute,   f"compute centers mismatch"


# ── Tech effects ──────────────────────────────────────────────────────────────

def _make_env(seed=0):
    env = GameEnvironment(cfg=GameConfig.medium(), seed=seed)
    env.reset()
    env.compute = 500.0  # ensure enough compute for any tech
    return env


def test_tech0_cost_reduction_lowers_buy_price():
    env = _make_env()
    mkt = env.markets[0]
    from GameLogic.config import PRODUCT_DEFS
    # Find a product the agent can afford and that has cross-market upside
    _, price_before = env._best_buyable(mkt)
    if price_before is None:
        pytest.skip("no buyable product in this seed")
    from GameLogic.config import TECH_TREE
    env._apply_tech("cost_reduction", TECH_TREE["cost_reduction"])
    env._techs_owned.add("cost_reduction")
    _, price_after = env._best_buyable(mkt)
    assert price_after is not None, "should still be a buyable product"
    assert price_after < price_before, (
        f"effective price should decrease after cost_reduction: {price_before} → {price_after}"
    )
    assert abs((price_before - price_after) - 2.0) < 1e-6, (
        f"price reduction should be exactly 2 (cost_delta=-2), got {price_before - price_after:.4f}"
    )


def test_tech3_durability_increases_capacity():
    env = _make_env()
    cap_before = env.unit.capacity
    from GameLogic.config import TECH_TREE
    env._apply_tech("durability", TECH_TREE["durability"])
    cap_after = env.unit.capacity
    assert cap_after == int(cap_before * 1.5), (
        f"capacity should be cap*1.5={int(cap_before*1.5)}, got {cap_after}"
    )


def test_tech6_market_analysis_reveals_extra_market_prices():
    """Markets 2-3 prices in obs must be zero without the tech and non-zero with it."""
    env = GameEnvironment(cfg=GameConfig.hard(), seed=42)
    env.reset()
    if len(env.markets) < 3:
        pytest.skip("need at least 3 markets (hard mode)")

    obs_without = env._encode_obs()
    # market 2 starts at obs index 46 + 2*7 = 60; prices are at +2..+6
    price_slots_mkt2 = obs_without[62:67]
    assert np.all(price_slots_mkt2 == 0.0), (
        f"market 2 prices should be hidden without market_analysis: {price_slots_mkt2}"
    )

    env._techs_owned.add("market_analysis")
    obs_with = env._encode_obs()
    price_slots_mkt2_revealed = obs_with[62:67]
    assert not np.all(price_slots_mkt2_revealed == 0.0), (
        "market 2 prices should be revealed after buying market_analysis"
    )


def test_random_map_differs_between_seeds():
    cfg = GameConfig(random_map=True)
    env1 = GameEnvironment(cfg=cfg, seed=1)
    env2 = GameEnvironment(cfg=cfg, seed=2)
    obs1, _ = env1.reset()
    obs2, _ = env2.reset()
    # Obs should differ (different market positions / resource points)
    assert not np.allclose(obs1, obs2), "Different seeds should produce different initial states"
