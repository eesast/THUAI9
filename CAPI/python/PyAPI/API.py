from __future__ import annotations

import math
from concurrent.futures import Future, ThreadPoolExecutor
from typing import List, Optional, Tuple

import PyAPI.structures as THUAI9
from PyAPI.Interface import IAI, ICharacterAPI, IGameTimer, ILogic, ITeamAPI, MessagePayload


class IAPI:
    @staticmethod
    def CellToGrid(cell: int) -> int:
        return cell * 1000 + 500

    @staticmethod
    def GridToCell(grid: int) -> int:
        return grid // 1000


class CharacterAPI(ICharacterAPI, IGameTimer):
    def __init__(self, logic: ILogic) -> None:
        self._logic = logic
        self._pool = ThreadPoolExecutor(max_workers=20)

    def _submit(self, fn, *args) -> Future[bool]:
        return self._pool.submit(fn, *args)

    def StartTimer(self) -> None:
        pass

    def EndTimer(self) -> None:
        pass

    def Play(self, ai: IAI) -> None:
        ai.CharacterPlay(self)

    def SendTextMessage(self, toPlayerID: int, message: str) -> Future[bool]:
        return self._submit(self._logic.Send, toPlayerID, message, False)

    def SendBinaryMessage(self, toPlayerID: int, message: bytes) -> Future[bool]:
        return self._submit(self._logic.Send, toPlayerID, message, True)

    def HaveMessage(self) -> bool:
        return self._logic.HaveMessage()

    def GetMessage(self) -> Tuple[int, MessagePayload]:
        return self._logic.GetMessage()

    def GetFrameCount(self) -> int:
        return self._logic.GetCounter()

    def Wait(self) -> bool:
        return self._logic.GetCounter() != -1 and self._logic.WaitThread()

    def EndAllAction(self) -> Future[bool]:
        return self._submit(self._logic.EndAllAction)

    def GetCharacters(self) -> List[THUAI9.Character]:
        return self._logic.GetCharacters()

    def GetEnemyCharacters(self) -> List[THUAI9.Character]:
        return self._logic.GetEnemyCharacters()

    def GetFullMap(self) -> List[List[THUAI9.PlaceType]]:
        return self._logic.GetFullMap()

    def GetGameInfo(self) -> THUAI9.GameInfo:
        return self._logic.GetGameInfo()

    def GetPlaceType(self, cellX: int, cellY: int) -> THUAI9.PlaceType:
        return self._logic.GetPlaceType(cellX, cellY)

    def GetResourceState(self, cellX: int, cellY: int) -> Optional[THUAI9.Resource]:
        return self._logic.GetResourceState(cellX, cellY)

    def GetComputeCenterState(
        self, cellX: int, cellY: int
    ) -> Optional[THUAI9.ComputeCenter]:
        return self._logic.GetComputeCenterState(cellX, cellY)

    def GetMarketState(self, cellX: int, cellY: int) -> Optional[THUAI9.Market]:
        return self._logic.GetMarketState(cellX, cellY)

    def GetFactoryState(self, cellX: int, cellY: int) -> Optional[THUAI9.Factory]:
        return self._logic.GetFactoryState(cellX, cellY)

    def GetPlayerGUIDs(self) -> List[int]:
        return self._logic.GetPlayerGUIDs()

    def GetComputingPower(self) -> int:
        return self._logic.GetComputingPower()

    def GetMaterial(self) -> int:
        return self._logic.GetMaterial()

    def GetScore(self) -> int:
        return self._logic.GetScore()

    def Move(self, moveTimeInMilliseconds: int, angleInRadian: float) -> Future[bool]:
        return self._submit(self._logic.Move, moveTimeInMilliseconds, angleInRadian)

    def MoveRight(self, timeInMilliseconds: int) -> Future[bool]:
        return self.Move(timeInMilliseconds, math.pi * 0.5)

    def MoveUp(self, timeInMilliseconds: int) -> Future[bool]:
        return self.Move(timeInMilliseconds, math.pi)

    def MoveLeft(self, timeInMilliseconds: int) -> Future[bool]:
        return self.Move(timeInMilliseconds, math.pi * 1.5)

    def MoveDown(self, timeInMilliseconds: int) -> Future[bool]:
        return self.Move(timeInMilliseconds, 0.0)

    def Common_Attack(self, attackedPlayerID: int) -> Future[bool]:
        selfInfo = self.GetSelfInfo()
        if selfInfo is None:
            return self._submit(lambda: False)
        return self._submit(
            self._logic.Common_Attack,
            selfInfo.teamID,
            selfInfo.playerID,
            0,
            attackedPlayerID,
        )

    def Recover(self, recover: int) -> Future[bool]:
        return self._submit(self._logic.Recover, recover)

    def Harvest(self) -> Future[bool]:
        selfInfo = self.GetSelfInfo()
        if selfInfo is None:
            return self._submit(lambda: False)
        return self._submit(self._logic.Harvest, selfInfo.playerID, selfInfo.teamID)

    def Occupy(self) -> Future[bool]:
        selfInfo = self.GetSelfInfo()
        if selfInfo is None:
            return self._submit(lambda: False)
        return self._submit(self._logic.Occupy, selfInfo.playerID, selfInfo.teamID)

    def Load(self, goodsType: THUAI9.GoodsType, amount: int) -> Future[bool]:
        selfInfo = self.GetSelfInfo()
        if selfInfo is None:
            return self._submit(lambda: False)
        return self._submit(
            self._logic.Load,
            selfInfo.playerID,
            selfInfo.teamID,
            goodsType,
            amount,
        )

    def Buy(self, goodsType: THUAI9.GoodsType, amount: int) -> Future[bool]:
        selfInfo = self.GetSelfInfo()
        if selfInfo is None:
            return self._submit(lambda: False)
        return self._submit(
            self._logic.Buy,
            selfInfo.playerID,
            selfInfo.teamID,
            goodsType,
            amount,
        )

    def Sell(self, goodsType: THUAI9.GoodsType, amount: int) -> Future[bool]:
        selfInfo = self.GetSelfInfo()
        if selfInfo is None:
            return self._submit(lambda: False)
        return self._submit(
            self._logic.Sell,
            selfInfo.playerID,
            selfInfo.teamID,
            goodsType,
            amount,
        )

    def GetSelfInfo(self) -> Optional[THUAI9.Character]:
        return self._logic.CharacterGetSelfInfo()

    def HaveView(
        self,
        x: int,
        y: int,
        newX: int,
        newY: int,
        viewRange: int,
        gameMap: List[List[THUAI9.PlaceType]],
    ) -> bool:
        return self._logic.HaveView(x, y, newX, newY, viewRange, gameMap)

    def Print(self, string: str) -> None:
        pass

    def PrintCharacter(self) -> None:
        pass

    def PrintSelfInfo(self) -> None:
        pass


class TeamAPI(ITeamAPI, IGameTimer):
    def __init__(self, logic: ILogic) -> None:
        self._logic = logic
        self._pool = ThreadPoolExecutor(max_workers=20)

    def _submit(self, fn, *args) -> Future[bool]:
        return self._pool.submit(fn, *args)

    def StartTimer(self) -> None:
        pass

    def EndTimer(self) -> None:
        pass

    def Play(self, ai: IAI) -> None:
        ai.TeamPlay(self)

    def SendTextMessage(self, toPlayerID: int, message: str) -> Future[bool]:
        return self._submit(self._logic.Send, toPlayerID, message, False)

    def SendBinaryMessage(self, toPlayerID: int, message: bytes) -> Future[bool]:
        return self._submit(self._logic.Send, toPlayerID, message, True)

    def HaveMessage(self) -> bool:
        return self._logic.HaveMessage()

    def GetMessage(self) -> Tuple[int, MessagePayload]:
        return self._logic.GetMessage()

    def GetFrameCount(self) -> int:
        return self._logic.GetCounter()

    def Wait(self) -> bool:
        return self._logic.GetCounter() != -1 and self._logic.WaitThread()

    def EndAllAction(self) -> Future[bool]:
        return self._submit(self._logic.EndAllAction)

    def GetCharacters(self) -> List[THUAI9.Character]:
        return self._logic.GetCharacters()

    def GetEnemyCharacters(self) -> List[THUAI9.Character]:
        return self._logic.GetEnemyCharacters()

    def GetFullMap(self) -> List[List[THUAI9.PlaceType]]:
        return self._logic.GetFullMap()

    def GetGameInfo(self) -> THUAI9.GameInfo:
        return self._logic.GetGameInfo()

    def GetPlaceType(self, cellX: int, cellY: int) -> THUAI9.PlaceType:
        return self._logic.GetPlaceType(cellX, cellY)

    def GetResourceState(self, cellX: int, cellY: int) -> Optional[THUAI9.Resource]:
        return self._logic.GetResourceState(cellX, cellY)

    def GetComputeCenterState(
        self, cellX: int, cellY: int
    ) -> Optional[THUAI9.ComputeCenter]:
        return self._logic.GetComputeCenterState(cellX, cellY)

    def GetMarketState(self, cellX: int, cellY: int) -> Optional[THUAI9.Market]:
        return self._logic.GetMarketState(cellX, cellY)

    def GetFactoryState(self, cellX: int, cellY: int) -> Optional[THUAI9.Factory]:
        return self._logic.GetFactoryState(cellX, cellY)

    def GetPlayerGUIDs(self) -> List[int]:
        return self._logic.GetPlayerGUIDs()

    def GetComputingPower(self) -> int:
        return self._logic.GetComputingPower()

    def GetMaterial(self) -> int:
        return self._logic.GetMaterial()

    def GetScore(self) -> int:
        return self._logic.GetScore()

    def GetSelfInfo(self) -> Optional[THUAI9.Team]:
        return self._logic.TeamGetSelfInfo()

    def BuildCharacter(
        self, characterType: THUAI9.CharacterType, playerID: int
    ) -> Future[bool]:
        return self._submit(self._logic.BuildCharacter, characterType, playerID)

    def ProduceGoods(
        self, goodsType: THUAI9.GoodsType, maxProduceNum: int
    ) -> Future[bool]:
        return self._submit(self._logic.ProduceGoods, goodsType, maxProduceNum)

    def UplevelTech(self, techType: THUAI9.TechType) -> Future[bool]:
        return self._submit(self._logic.UplevelTech, techType)

    def Print(self, string: str) -> None:
        pass

    def PrintCharacter(self) -> None:
        pass

    def PrintSelfInfo(self) -> None:
        pass
