"""Compatibility exports for the game-rule layer.

New code should import from game_core directly. This module exists so older
scripts that used `from main import GameEnv` keep working.
"""

from game_core import Factory, GameEnv, Market, Point, ResourcePoint, Unit

__all__ = ["Factory", "GameEnv", "Market", "Point", "ResourcePoint", "Unit"]
