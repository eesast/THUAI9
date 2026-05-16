# Iteration Notes (2026-05-16)

## Summary
- Fixed cross-market arbitrage enforcement by tracking per-product quantities per market origin, enabling mixed-origin inventories without bypassing sell restrictions.
- Cleared stale origin metadata on inventory reset and removed factory-load tainting so factory goods remain sellable anywhere even when mixed.
- Added fast market lookup via position→market map and reused it in action masking.
- Reworked buy selection to score against the best *other-market* sell price at current time (with marketing multiplier), avoiding theoretical maxima that may never occur.
- Restored a learnable default reward signal and made the adversarial "harvest trap" reward opt-in via `RewardConfig(mode="adversarial")`, while keeping `compute_center_bonus` and `tech_bonus` for backward compatibility.

## Files Changed
- `logic/pve/GameLogic/character.py`
- `logic/pve/GameLogic/game_env.py`
- `logic/pve/GameLogic/action_space.py`
- `logic/pve/GameLogic/reward_calculator.py`

## Notes
- PR metadata (title/description) should be updated separately to describe the reward redesign and market rule changes if applicable.
