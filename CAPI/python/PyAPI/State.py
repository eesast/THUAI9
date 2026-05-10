from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional

import PyAPI.structures as THUAI9


@dataclass
class State:
    characterSelf: Optional[THUAI9.Character] = None
    teamSelf: Optional[THUAI9.Team] = None
    characters: List[THUAI9.Character] = field(default_factory=list)
    enemyCharacters: List[THUAI9.Character] = field(default_factory=list)
    gameMap: List[List[THUAI9.PlaceType]] = field(default_factory=list)
    mapInfo: THUAI9.GameMap = field(default_factory=THUAI9.GameMap)
    gameInfo: THUAI9.GameInfo = field(default_factory=THUAI9.GameInfo)
    guids: List[int] = field(default_factory=list)
    allGuids: List[int] = field(default_factory=list)
