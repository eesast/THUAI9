from __future__ import annotations

from abc import ABCMeta, abstractmethod
from concurrent.futures import Future
from typing import List, Optional, Tuple, Union

import PyAPI.structures as THUAI9


MessagePayload = Union[str, bytes]


class ILogic(metaclass=ABCMeta):
    @abstractmethod
    def GetCharacters(self) -> List[THUAI9.Character]:
        raise NotImplementedError

    @abstractmethod
    def GetEnemyCharacters(self) -> List[THUAI9.Character]:
        raise NotImplementedError

    @abstractmethod
    def CharacterGetSelfInfo(self) -> Optional[THUAI9.Character]:
        raise NotImplementedError

    @abstractmethod
    def TeamGetSelfInfo(self) -> Optional[THUAI9.Team]:
        raise NotImplementedError

    @abstractmethod
    def GetFullMap(self) -> List[List[THUAI9.PlaceType]]:
        raise NotImplementedError

    @abstractmethod
    def GetGameInfo(self) -> THUAI9.GameInfo:
        raise NotImplementedError

    @abstractmethod
    def GetPlayerGUIDs(self) -> List[int]:
        raise NotImplementedError

    @abstractmethod
    def GetPlaceType(self, cellX: int, cellY: int) -> THUAI9.PlaceType:
        raise NotImplementedError

    @abstractmethod
    def GetResourceState(self, cellX: int, cellY: int) -> Optional[THUAI9.Resource]:
        raise NotImplementedError

    @abstractmethod
    def GetComputeCenterState(
        self, cellX: int, cellY: int
    ) -> Optional[THUAI9.ComputeCenter]:
        raise NotImplementedError

    @abstractmethod
    def GetMarketState(self, cellX: int, cellY: int) -> Optional[THUAI9.Market]:
        raise NotImplementedError

    @abstractmethod
    def GetFactoryState(self, cellX: int, cellY: int) -> Optional[THUAI9.Factory]:
        raise NotImplementedError

    @abstractmethod
    def GetComputingPower(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def GetMaterial(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def GetScore(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def Send(self, toPlayerID: int, message: MessagePayload, binary: bool) -> bool:
        raise NotImplementedError

    @abstractmethod
    def HaveMessage(self) -> bool:
        raise NotImplementedError

    @abstractmethod
    def GetMessage(self) -> Tuple[int, MessagePayload]:
        raise NotImplementedError

    @abstractmethod
    def WaitThread(self) -> bool:
        raise NotImplementedError

    @abstractmethod
    def GetCounter(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def EndAllAction(self) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Move(self, moveTimeInMilliseconds: int, angle: float) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Recover(self, recover: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Harvest(self, playerID: int, teamID: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Occupy(self, playerID: int, teamID: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Load(self, playerID: int, teamID: int, goodsType: THUAI9.GoodsType, amount: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Buy(self, playerID: int, teamID: int, goodsType: THUAI9.GoodsType, amount: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Sell(self, playerID: int, teamID: int, goodsType: THUAI9.GoodsType, amount: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def Common_Attack(
        self,
        teamID: int,
        playerID: int,
        attackedTeamID: int,
        attackedPlayerID: int,
    ) -> bool:
        raise NotImplementedError

    @abstractmethod
    def HaveView(
        self,
        x: int,
        y: int,
        newX: int,
        newY: int,
        viewRange: int,
        gameMap: List[List[THUAI9.PlaceType]],
    ) -> bool:
        raise NotImplementedError

    @abstractmethod
    def BuildCharacter(self, characterType: THUAI9.CharacterType, playerID: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def ProduceGoods(self, goodsType: THUAI9.GoodsType, maxProduceNum: int) -> bool:
        raise NotImplementedError

    @abstractmethod
    def UplevelTech(self, techType: THUAI9.TechType) -> bool:
        raise NotImplementedError


class IAPI(metaclass=ABCMeta):
    @abstractmethod
    def SendTextMessage(self, toPlayerID: int, message: str) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def SendBinaryMessage(self, toPlayerID: int, message: bytes) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def HaveMessage(self) -> bool:
        raise NotImplementedError

    @abstractmethod
    def GetMessage(self) -> Tuple[int, MessagePayload]:
        raise NotImplementedError

    @abstractmethod
    def GetFrameCount(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def Wait(self) -> bool:
        raise NotImplementedError

    @abstractmethod
    def EndAllAction(self) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def GetCharacters(self) -> List[THUAI9.Character]:
        raise NotImplementedError

    @abstractmethod
    def GetEnemyCharacters(self) -> List[THUAI9.Character]:
        raise NotImplementedError

    @abstractmethod
    def GetFullMap(self) -> List[List[THUAI9.PlaceType]]:
        raise NotImplementedError

    @abstractmethod
    def GetGameInfo(self) -> THUAI9.GameInfo:
        raise NotImplementedError

    @abstractmethod
    def GetPlaceType(self, cellX: int, cellY: int) -> THUAI9.PlaceType:
        raise NotImplementedError

    @abstractmethod
    def GetResourceState(self, cellX: int, cellY: int) -> Optional[THUAI9.Resource]:
        raise NotImplementedError

    @abstractmethod
    def GetComputeCenterState(
        self, cellX: int, cellY: int
    ) -> Optional[THUAI9.ComputeCenter]:
        raise NotImplementedError

    @abstractmethod
    def GetMarketState(self, cellX: int, cellY: int) -> Optional[THUAI9.Market]:
        raise NotImplementedError

    @abstractmethod
    def GetFactoryState(self, cellX: int, cellY: int) -> Optional[THUAI9.Factory]:
        raise NotImplementedError

    @abstractmethod
    def GetPlayerGUIDs(self) -> List[int]:
        raise NotImplementedError

    @abstractmethod
    def GetComputingPower(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def GetMaterial(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def GetScore(self) -> int:
        raise NotImplementedError

    @abstractmethod
    def Print(self, string: str) -> None:
        raise NotImplementedError

    @abstractmethod
    def PrintCharacter(self) -> None:
        raise NotImplementedError

    @abstractmethod
    def PrintSelfInfo(self) -> None:
        raise NotImplementedError


class ICharacterAPI(IAPI, metaclass=ABCMeta):
    @abstractmethod
    def Move(self, moveTimeInMilliseconds: int, angleInRadian: float) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def MoveRight(self, timeInMilliseconds: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def MoveUp(self, timeInMilliseconds: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def MoveLeft(self, timeInMilliseconds: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def MoveDown(self, timeInMilliseconds: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def Common_Attack(self, attackedPlayerID: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def Recover(self, recover: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def Harvest(self) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def Occupy(self) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def Load(self, goodsType: THUAI9.GoodsType, amount: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def Buy(self, goodsType: THUAI9.GoodsType, amount: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def Sell(self, goodsType: THUAI9.GoodsType, amount: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def GetSelfInfo(self) -> Optional[THUAI9.Character]:
        raise NotImplementedError

    @abstractmethod
    def HaveView(
        self,
        x: int,
        y: int,
        newX: int,
        newY: int,
        viewRange: int,
        gameMap: List[List[THUAI9.PlaceType]],
    ) -> bool:
        raise NotImplementedError


class ITeamAPI(IAPI, metaclass=ABCMeta):
    @abstractmethod
    def GetSelfInfo(self) -> Optional[THUAI9.Team]:
        raise NotImplementedError

    @abstractmethod
    def BuildCharacter(self, characterType: THUAI9.CharacterType, playerID: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def ProduceGoods(self, goodsType: THUAI9.GoodsType, maxProduceNum: int) -> Future[bool]:
        raise NotImplementedError

    @abstractmethod
    def UplevelTech(self, techType: THUAI9.TechType) -> Future[bool]:
        raise NotImplementedError


class IAI(metaclass=ABCMeta):
    @abstractmethod
    def CharacterPlay(self, api: ICharacterAPI) -> None:
        raise NotImplementedError

    @abstractmethod
    def TeamPlay(self, api: ITeamAPI) -> None:
        raise NotImplementedError


class IGameTimer(metaclass=ABCMeta):
    @abstractmethod
    def StartTimer(self) -> None:
        raise NotImplementedError

    @abstractmethod
    def EndTimer(self) -> None:
        raise NotImplementedError

    @abstractmethod
    def Play(self, ai: IAI) -> None:
        raise NotImplementedError
