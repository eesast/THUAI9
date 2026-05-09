from __future__ import annotations

import math
from collections import deque
from typing import Callable, Dict, List, Optional, Tuple, Union

import PyAPI.structures as THUAI9
from PyAPI.Interface import IAI, ICharacterAPI, ITeamAPI


Cell = Tuple[int, int]

CELL_SIZE = 1000
CELL_CENTER = 500
MOVE_TIME_MS = 200
COMMAND_COOLDOWN_FRAMES = 2

WORKER_PLAYER_ID = 1
WORKER_TYPE = THUAI9.CharacterType.Robot

# Food costs less and produces faster, so the full collect -> load -> sell demo
# can be observed quickly on the default map.
PRODUCT_TYPE = THUAI9.GoodsType.Food
PRODUCT_COST = 3

PASSABLE_PLACES = {
    THUAI9.PlaceType.Space,
    THUAI9.PlaceType.Bush,
}


class Setting:
    @staticmethod
    def Asynchronous() -> bool:
        return False


class AI(IAI):
    def __init__(self, playerID: int):
        self.playerID = playerID
        self._next_command_frame = 0
        self._last_mode = ""
        self._force_delivery_until_frame = 0
        self._ignore_load_until_frame = 0

        self._cached_map: List[List[THUAI9.PlaceType]] = []
        self._resource_cells: List[Cell] = []
        self._resource_set: set[Cell] = set()
        self._market_cells: List[Cell] = []
        self._market_set: set[Cell] = set()
        self._factory_cells: List[Cell] = []
        self._own_factory_cell: Optional[Cell] = None

    def CharacterPlay(self, api: ICharacterAPI) -> None:
        if self.playerID != WORKER_PLAYER_ID:
            return

        selfInfo = api.GetSelfInfo()
        if selfInfo is None or selfInfo.characterActiveState == THUAI9.CharacterState.Deceased:
            return

        gameMap = api.GetFullMap()
        self._ensure_map_cache(gameMap)

        ownFactoryCell = self._find_own_factory_cell(api, selfInfo.teamID)
        if ownFactoryCell is None:
            self._set_mode(api, "waiting for own factory info")
            return

        factoryState = api.GetFactoryState(*ownFactoryCell)
        inventory = 0
        if factoryState is not None:
            inventory = factoryState.productInventory.get(PRODUCT_TYPE, 0)

        frame = api.GetFrameCount()
        myCell = self._grid_to_cell(selfInfo.x, selfInfo.y)
        currentLoad = selfInfo.currentLoad
        if frame < self._ignore_load_until_frame:
            currentLoad = 0
        if frame < self._force_delivery_until_frame:
            currentLoad = max(currentLoad, 1)

        if currentLoad > 0:
            if self._is_adjacent_to_market(myCell):
                self._set_mode(api, "selling at market")
                self._try_sell(api, frame)
            else:
                if self._stop_harvest_if_needed(api, selfInfo, frame):
                    return
                self._set_mode(api, "moving to market")
                self._move_to_nearest_market(api, selfInfo, myCell, frame)
            return

        if inventory > 0:
            if self._is_adjacent(myCell, ownFactoryCell):
                self._set_mode(api, "loading goods from factory")
                self._try_load(api, frame)
            else:
                if self._stop_harvest_if_needed(api, selfInfo, frame):
                    return
                self._set_mode(api, "returning to factory")
                self._move_to_adjacent_cell(
                    api,
                    selfInfo,
                    myCell,
                    frame,
                    lambda cell: self._is_adjacent(cell, ownFactoryCell),
                )
            return

        if self._has_available_resource_nearby(api, myCell):
            self._set_mode(api, "harvesting resource")
            if (
                selfInfo.characterActiveState != THUAI9.CharacterState.Harvesting
                and self._command_ready(frame)
            ):
                ok = api.Harvest().result()
                self._mark_command(frame)
                if ok:
                    api.Print("worker starts harvesting")
            return

        if self._stop_harvest_if_needed(api, selfInfo, frame):
            return

        self._set_mode(api, "moving to resource")
        self._move_to_resource(api, selfInfo, myCell, frame)

    def TeamPlay(self, api: ITeamAPI) -> None:
        teamInfo = api.GetSelfInfo()
        if teamInfo is None:
            return

        gameMap = api.GetFullMap()
        self._ensure_map_cache(gameMap)

        workerExists = any(character.playerID == WORKER_PLAYER_ID for character in api.GetCharacters())
        frame = api.GetFrameCount()

        if not workerExists and self._command_ready(frame):
            ok = api.BuildCharacter(WORKER_TYPE, WORKER_PLAYER_ID).result()
            self._mark_command(frame, extra=5)
            if ok:
                api.Print("team built worker player 1")
            return

        ownFactoryCell = self._find_own_factory_cell(api, teamInfo.teamID)
        if ownFactoryCell is None:
            self._set_mode(api, "waiting for team factory info")
            return

        factoryState = api.GetFactoryState(*ownFactoryCell)
        if factoryState is None:
            return

        inventory = factoryState.productInventory.get(PRODUCT_TYPE, 0)
        if (
            inventory == 0
            and teamInfo.material >= PRODUCT_COST
            and factoryState.canProduce
            and self._command_ready(frame)
        ):
            ok = api.ProduceGoods(PRODUCT_TYPE, 1).result()
            self._mark_command(frame, extra=5)
            if ok:
                api.Print(f"team starts producing {PRODUCT_TYPE.name} x1")

    def _ensure_map_cache(self, gameMap: List[List[THUAI9.PlaceType]]) -> None:
        if not gameMap or not gameMap[0]:
            return
        if self._cached_map:
            return

        self._cached_map = gameMap
        for x, row in enumerate(gameMap):
            for y, placeType in enumerate(row):
                cell = (x, y)
                if placeType == THUAI9.PlaceType.Resource:
                    self._resource_cells.append(cell)
                    self._resource_set.add(cell)
                elif placeType == THUAI9.PlaceType.Market:
                    self._market_cells.append(cell)
                    self._market_set.add(cell)
                elif placeType == THUAI9.PlaceType.Factory:
                    self._factory_cells.append(cell)

    def _find_own_factory_cell(
        self,
        api: Union[ICharacterAPI, ITeamAPI],
        teamID: int,
    ) -> Optional[Cell]:
        if self._own_factory_cell is not None:
            factory = api.GetFactoryState(*self._own_factory_cell)
            if factory is not None and factory.teamID == teamID:
                return self._own_factory_cell

        for cell in self._factory_cells:
            factory = api.GetFactoryState(*cell)
            if factory is not None and factory.teamID == teamID:
                self._own_factory_cell = cell
                return cell
        return None

    def _move_to_resource(
        self,
        api: ICharacterAPI,
        selfInfo: THUAI9.Character,
        myCell: Cell,
        frame: int,
    ) -> None:
        self._move_to_adjacent_cell(
            api,
            selfInfo,
            myCell,
            frame,
            lambda cell: self._has_available_resource_nearby(api, cell),
        )

    def _move_to_nearest_market(
        self,
        api: ICharacterAPI,
        selfInfo: THUAI9.Character,
        myCell: Cell,
        frame: int,
    ) -> None:
        self._move_to_adjacent_cell(
            api,
            selfInfo,
            myCell,
            frame,
            self._is_adjacent_to_market,
        )

    def _move_to_adjacent_cell(
        self,
        api: ICharacterAPI,
        selfInfo: THUAI9.Character,
        myCell: Cell,
        frame: int,
        goal: Callable[[Cell], bool],
    ) -> None:
        if not self._command_ready(frame):
            return

        path = self._find_path(myCell, goal)
        if path is None or len(path) <= 1:
            return

        nextCell = path[1]
        targetX, targetY = self._cell_center(nextCell)
        angle = math.atan2(targetY - selfInfo.y, targetX - selfInfo.x)
        ok = api.Move(MOVE_TIME_MS, angle).result()
        self._mark_command(frame)
        if not ok:
            api.Print("worker move command was rejected")

    def _find_path(self, start: Cell, goal: Callable[[Cell], bool]) -> Optional[List[Cell]]:
        if not self._cached_map or not self._cached_map[0]:
            return None
        if not self._in_bounds(start):
            return None
        if goal(start):
            return [start]

        prev: Dict[Cell, Cell] = {start: start}
        queue = deque()

        if self._is_passable(start):
            queue.append(start)
        else:
            for nextCell in self._neighbors4(start):
                if nextCell in prev:
                    continue
                if not self._in_bounds(nextCell) or not self._is_passable(nextCell):
                    continue
                prev[nextCell] = start
                queue.append(nextCell)

        while queue:
            cell = queue.popleft()
            if goal(cell):
                return self._reconstruct_path(start, cell, prev)

            for nextCell in self._neighbors4(cell):
                if nextCell in prev:
                    continue
                if not self._in_bounds(nextCell) or not self._is_passable(nextCell):
                    continue
                prev[nextCell] = cell
                queue.append(nextCell)

        return None

    def _reconstruct_path(
        self,
        start: Cell,
        end: Cell,
        prev: Dict[Cell, Cell],
    ) -> List[Cell]:
        path = [end]
        while path[-1] != start:
            path.append(prev[path[-1]])
        path.reverse()
        return path

    def _has_available_resource_nearby(
        self,
        api: ICharacterAPI,
        cell: Cell,
    ) -> bool:
        for target in self._neighbors8_with_self(cell):
            if target not in self._resource_set:
                continue
            resource = api.GetResourceState(*target)
            if resource is None or resource.state != THUAI9.ResourceState.Harvested:
                return True
        return False

    def _is_adjacent_to_market(self, cell: Cell) -> bool:
        return any(target in self._market_set for target in self._neighbors8_with_self(cell))

    def _try_load(self, api: ICharacterAPI, frame: int) -> None:
        if not self._command_ready(frame):
            return
        ok = api.Load(PRODUCT_TYPE, 1).result()
        self._mark_command(frame)
        if ok:
            self._force_delivery_until_frame = frame + 6
            self._ignore_load_until_frame = 0
            api.Print(f"worker loaded {PRODUCT_TYPE.name} x1")

    def _try_sell(self, api: ICharacterAPI, frame: int) -> None:
        if not self._command_ready(frame):
            return
        ok = api.Sell(PRODUCT_TYPE, 1).result()
        self._mark_command(frame)
        if ok:
            self._force_delivery_until_frame = 0
            self._ignore_load_until_frame = frame + 6
            api.Print(f"worker sold {PRODUCT_TYPE.name} x1")

    def _stop_harvest_if_needed(
        self,
        api: ICharacterAPI,
        selfInfo: THUAI9.Character,
        frame: int,
    ) -> bool:
        if selfInfo.characterActiveState != THUAI9.CharacterState.Harvesting:
            return False
        if not self._command_ready(frame):
            return True
        api.EndAllAction().result()
        self._mark_command(frame)
        api.Print("worker stops harvesting")
        return True

    def _command_ready(self, frame: int) -> bool:
        return frame >= self._next_command_frame

    def _mark_command(self, frame: int, extra: int = COMMAND_COOLDOWN_FRAMES) -> None:
        self._next_command_frame = frame + extra

    def _set_mode(self, api: Union[ICharacterAPI, ITeamAPI], mode: str) -> None:
        if mode == self._last_mode:
            return
        self._last_mode = mode
        api.Print(f"[player {self.playerID}] {mode}")

    def _in_bounds(self, cell: Cell) -> bool:
        return (
            0 <= cell[0] < len(self._cached_map)
            and 0 <= cell[1] < len(self._cached_map[0])
        )

    def _is_passable(self, cell: Cell) -> bool:
        return self._cached_map[cell[0]][cell[1]] in PASSABLE_PLACES

    @staticmethod
    def _grid_to_cell(x: int, y: int) -> Cell:
        return x // CELL_SIZE, y // CELL_SIZE

    @staticmethod
    def _cell_center(cell: Cell) -> Tuple[int, int]:
        return cell[0] * CELL_SIZE + CELL_CENTER, cell[1] * CELL_SIZE + CELL_CENTER

    @staticmethod
    def _is_adjacent(cellA: Cell, cellB: Cell) -> bool:
        return max(abs(cellA[0] - cellB[0]), abs(cellA[1] - cellB[1])) <= 1

    @staticmethod
    def _neighbors4(cell: Cell) -> List[Cell]:
        x, y = cell
        return [(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)]

    @staticmethod
    def _neighbors8_with_self(cell: Cell) -> List[Cell]:
        x, y = cell
        return [
            (x - 1, y - 1),
            (x - 1, y),
            (x - 1, y + 1),
            (x, y - 1),
            (x, y),
            (x, y + 1),
            (x + 1, y - 1),
            (x + 1, y),
            (x + 1, y + 1),
        ]
