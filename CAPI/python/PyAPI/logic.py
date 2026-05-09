from __future__ import annotations

import copy
import logging
import threading
import time
from collections import deque
from pathlib import Path
from typing import Callable, Deque, Optional, Tuple

import PyAPI.structures as THUAI9
from PyAPI.AI import Setting
from PyAPI.API import CharacterAPI, TeamAPI
from PyAPI.Communication import Communication
from PyAPI.DebugAPI import CharacterDebugAPI, TeamDebugAPI
from PyAPI.Interface import IAI, IGameTimer, ILogic, MessagePayload
from PyAPI.State import State
from PyAPI.utils import AssistFunction, Proto2THUAI9


class Logic(ILogic):
    def __init__(
        self,
        playerID: int,
        teamID: int,
        playerType: THUAI9.PlayerType,
        characterType: THUAI9.CharacterType,
        sideFlag: Optional[int] = None,
    ) -> None:
        self.__playerID = playerID
        self.__teamID = teamID
        self.__playerType = playerType
        self.__characterType = characterType
        self.__sideFlag = bool(teamID % 2 == 1) if sideFlag is None else bool(sideFlag)

        self.__comm: Optional[Communication] = None
        self.__currentState = State()
        self.__bufferState = State()
        self.__timer: Optional[IGameTimer] = None
        self.__threadAI: Optional[threading.Thread] = None

        self.__mtxAI = threading.Lock()
        self.__mtxState = threading.Lock()
        self.__mtxMessage = threading.Lock()

        self.__cvBuffer = threading.Condition()
        self.__cvAI = threading.Condition()

        self.__messageQueue: Deque[Tuple[int, MessagePayload]] = deque()

        self.__counterState = 0
        self.__counterBuffer = 0
        self.__gameState = THUAI9.GameState.NullGameState
        self.__AILoop = True
        self.__bufferUpdated = False
        self.__AIStart = False
        self.__freshed = False

        self.__logger = logging.getLogger(f"logic-{playerID}-{teamID}")

    def GetCharacters(self):
        with self.__mtxState:
            return copy.deepcopy(self.__currentState.characters)

    def GetEnemyCharacters(self):
        with self.__mtxState:
            return copy.deepcopy(self.__currentState.enemyCharacters)

    def CharacterGetSelfInfo(self):
        with self.__mtxState:
            return copy.deepcopy(self.__currentState.characterSelf)

    def TeamGetSelfInfo(self):
        with self.__mtxState:
            return copy.deepcopy(self.__currentState.teamSelf)

    def GetFullMap(self):
        with self.__mtxState:
            return copy.deepcopy(self.__currentState.gameMap)

    def GetPlaceType(self, cellX: int, cellY: int):
        with self.__mtxState:
            if not self.__currentState.gameMap or not self.__currentState.gameMap[0]:
                return THUAI9.PlaceType.NullPlaceType
            if cellX < 0 or cellY < 0:
                return THUAI9.PlaceType.NullPlaceType
            if cellX >= len(self.__currentState.gameMap):
                return THUAI9.PlaceType.NullPlaceType
            if cellY >= len(self.__currentState.gameMap[0]):
                return THUAI9.PlaceType.NullPlaceType
            return self.__currentState.gameMap[cellX][cellY]

    def GetResourceState(self, cellX: int, cellY: int):
        with self.__mtxState:
            resource = self.__currentState.mapInfo.resources.get((cellX, cellY))
            return copy.deepcopy(resource)

    def GetComputeCenterState(self, cellX: int, cellY: int):
        with self.__mtxState:
            center = self.__currentState.mapInfo.computeCenters.get((cellX, cellY))
            return copy.deepcopy(center)

    def GetMarketState(self, cellX: int, cellY: int):
        with self.__mtxState:
            market = self.__currentState.mapInfo.markets.get((cellX, cellY))
            return copy.deepcopy(market)

    def GetFactoryState(self, cellX: int, cellY: int):
        with self.__mtxState:
            factory = self.__currentState.mapInfo.factories.get((cellX, cellY))
            return copy.deepcopy(factory)

    def GetComputingPower(self) -> int:
        with self.__mtxState:
            if self.__currentState.teamSelf is not None:
                return int(self.__currentState.teamSelf.computePower)
            if 0 < self.__teamID <= len(self.__currentState.gameInfo.teams):
                return self.__currentState.gameInfo.teams[self.__teamID - 1].computePower
            return -1

    def GetMaterial(self) -> int:
        with self.__mtxState:
            if self.__currentState.teamSelf is not None:
                return int(self.__currentState.teamSelf.material)
            if 0 < self.__teamID <= len(self.__currentState.gameInfo.teams):
                return self.__currentState.gameInfo.teams[self.__teamID - 1].material
            return -1

    def GetScore(self) -> int:
        with self.__mtxState:
            if self.__currentState.teamSelf is not None:
                return int(self.__currentState.teamSelf.score)
            if 0 < self.__teamID <= len(self.__currentState.gameInfo.teams):
                return self.__currentState.gameInfo.teams[self.__teamID - 1].score
            return -1

    def GetGameInfo(self):
        with self.__mtxState:
            return copy.deepcopy(self.__currentState.gameInfo)

    def GetPlayerGUIDs(self):
        with self.__mtxState:
            return copy.deepcopy(self.__currentState.guids)

    def Send(self, toPlayerID: int, message: MessagePayload, binary: bool) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Send(self.__playerID, toPlayerID, self.__teamID, message, binary)

    def HaveMessage(self) -> bool:
        with self.__mtxMessage:
            return bool(self.__messageQueue)

    def GetMessage(self):
        with self.__mtxMessage:
            if not self.__messageQueue:
                return -1, ""
            return self.__messageQueue.popleft()

    def WaitThread(self) -> bool:
        if Setting.Asynchronous():
            self.__Wait()
        return True

    def GetCounter(self) -> int:
        with self.__mtxState:
            return self.__counterState

    def EndAllAction(self) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.EndAllAction(self.__playerID, self.__teamID)

    def Move(self, moveTimeInMilliseconds: int, angle: float) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Move(
            self.__playerID, self.__teamID, moveTimeInMilliseconds, angle
        )

    def Recover(self, recover: int) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Recover(self.__playerID, recover, self.__teamID)

    def Harvest(self, playerID: int, teamID: int) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Harvest(playerID, teamID)

    def Occupy(self, playerID: int, teamID: int) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Occupy(playerID, teamID)

    def Load(
        self, playerID: int, teamID: int, goodsType: THUAI9.GoodsType, amount: int
    ) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Load(playerID, teamID, goodsType, amount)

    def Buy(
        self, playerID: int, teamID: int, goodsType: THUAI9.GoodsType, amount: int
    ) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Trade(playerID, teamID, goodsType, amount, True)

    def Sell(
        self, playerID: int, teamID: int, goodsType: THUAI9.GoodsType, amount: int
    ) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Trade(playerID, teamID, goodsType, amount, False)

    def Common_Attack(
        self,
        teamID: int,
        playerID: int,
        attackedTeamID: int,
        attackedPlayerID: int,
    ) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.Common_Attack(
            teamID, playerID, attackedTeamID, attackedPlayerID
        )

    def HaveView(
        self,
        x: int,
        y: int,
        newX: int,
        newY: int,
        viewRange: int,
        gameMap,
    ) -> bool:
        return AssistFunction.HaveView(x, y, newX, newY, viewRange, gameMap)

    def BuildCharacter(self, characterType: THUAI9.CharacterType, playerID: int) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.BuildCharacter(self.__teamID, playerID, characterType)

    def ProduceGoods(self, goodsType: THUAI9.GoodsType, maxProduceNum: int) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.ProduceGoods(self.__teamID, goodsType, maxProduceNum)

    def UplevelTech(self, techType: THUAI9.TechType) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.UplevelTech(self.__teamID, techType)

    def TryConnection(self) -> bool:
        if self.__comm is None:
            return False
        return self.__comm.TryConnection(self.__playerID, self.__teamID)

    def __LoadBufferSelf(self, message) -> None:
        if self.__playerType == THUAI9.PlayerType.Character:
            for item in message.obj_message:
                if item.WhichOneof("message_of_obj") != "character_message":
                    continue
                msg = item.character_message
                if msg.player_id == self.__playerID and msg.team_id == self.__teamID:
                    self.__bufferState.characterSelf = Proto2THUAI9.Protobuf2THUAI9Character(
                        msg
                    )
                    self.__bufferState.characters.append(self.__bufferState.characterSelf)
                    break
            return

        if 0 < self.__teamID <= len(message.all_message.teams):
            teamInfo = message.all_message.teams[self.__teamID - 1]
            self.__bufferState.teamSelf = THUAI9.Team(
                teamID=self.__teamID,
                playerID=self.__playerID,
                score=teamInfo.score,
                material=teamInfo.material,
                computePower=teamInfo.compute_power,
                factoryHP=teamInfo.factory_hp,
            )

    def __LoadBufferCase(self, item) -> None:
        field_name = item.WhichOneof("message_of_obj")

        if field_name == "character_message":
            character = Proto2THUAI9.Protobuf2THUAI9Character(item.character_message)
            if item.character_message.team_id == self.__teamID:
                if (
                    item.character_message.player_id == self.__playerID
                    and self.__playerType == THUAI9.PlayerType.Character
                ):
                    self.__bufferState.characterSelf = character
                else:
                    self.__bufferState.characters.append(character)
            else:
                self.__bufferState.enemyCharacters.append(character)
            return

        if field_name == "team_message":
            if item.team_message.team_id == self.__teamID:
                team = Proto2THUAI9.Protobuf2THUAI9Team(item.team_message)
                if 0 < self.__teamID <= len(self.__bufferState.gameInfo.teams):
                    team.factoryHP = self.__bufferState.gameInfo.teams[
                        self.__teamID - 1
                    ].factoryHP
                self.__bufferState.teamSelf = team
            return

        if field_name == "factory_message":
            factory = Proto2THUAI9.Protobuf2THUAI9Factory(item.factory_message)
            pos = (
                AssistFunction.GridToCell(item.factory_message.x),
                AssistFunction.GridToCell(item.factory_message.y),
            )
            self.__bufferState.mapInfo.factories[pos] = factory
            return

        if field_name == "resource_message":
            resource = Proto2THUAI9.Protobuf2THUAI9Resource(item.resource_message)
            pos = (
                AssistFunction.GridToCell(item.resource_message.x),
                AssistFunction.GridToCell(item.resource_message.y),
            )
            self.__bufferState.mapInfo.resources[pos] = resource
            return

        if field_name == "market_message":
            market = Proto2THUAI9.Protobuf2THUAI9Market(item.market_message)
            pos = (
                AssistFunction.GridToCell(item.market_message.x),
                AssistFunction.GridToCell(item.market_message.y),
            )
            self.__bufferState.mapInfo.markets[pos] = market
            return

        if field_name == "compute_center_message":
            center = Proto2THUAI9.Protobuf2THUAI9ComputeCenter(
                item.compute_center_message
            )
            pos = (
                AssistFunction.GridToCell(item.compute_center_message.x),
                AssistFunction.GridToCell(item.compute_center_message.y),
            )
            self.__bufferState.mapInfo.computeCenters[pos] = center
            return

        if field_name == "news_message":
            news = item.news_message
            if news.to_id == self.__playerID and news.team_id == self.__teamID:
                newsField = news.WhichOneof("news")
                with self.__mtxMessage:
                    if newsField == "text_message":
                        self.__messageQueue.append((news.from_id, news.text_message))
                    elif newsField == "binary_message":
                        self.__messageQueue.append((news.from_id, news.binary_message))

    def __LoadBuffer(self, message) -> None:
        with self.__cvBuffer:
            self.__bufferState.characters.clear()
            self.__bufferState.enemyCharacters.clear()
            self.__bufferState.guids.clear()
            self.__bufferState.allGuids.clear()
            self.__bufferState.characterSelf = None
            self.__bufferState.teamSelf = None
            self.__bufferState.mapInfo = THUAI9.GameMap()
            self.__bufferState.gameInfo = Proto2THUAI9.Protobuf2THUAI9GameInfo(
                message.all_message
            )

            self.__LoadBufferSelf(message)

            for obj in message.obj_message:
                if obj.WhichOneof("message_of_obj") == "character_message":
                    self.__bufferState.allGuids.append(obj.character_message.guid)
                    if obj.character_message.team_id == self.__teamID:
                        self.__bufferState.guids.append(obj.character_message.guid)
                self.__LoadBufferCase(obj)

            if Setting.Asynchronous():
                with self.__mtxState:
                    self.__currentState, self.__bufferState = (
                        self.__bufferState,
                        self.__currentState,
                    )
                    self.__counterState = self.__counterBuffer
                self.__freshed = True
            else:
                self.__bufferUpdated = True

            self.__counterBuffer += 1
            self.__cvBuffer.notify_all()

    def __ProcessMessage(self) -> None:
        def messageThread() -> None:
            try:
                assert self.__comm is not None
                self.__comm.AddPlayer(
                    self.__playerID,
                    self.__teamID,
                    self.__characterType,
                    self.__sideFlag,
                )

                while self.__gameState != THUAI9.GameState.GameEnd:
                    clientMsg = self.__comm.GetMessage2Client()
                    self.__gameState = Proto2THUAI9.gameStateDict.get(
                        clientMsg.game_state,
                        THUAI9.GameState.NullGameState,
                    )

                    if self.__gameState == THUAI9.GameState.GameStart:
                        for item in clientMsg.obj_message:
                            if item.WhichOneof("message_of_obj") != "map_message":
                                continue

                            gameMap = []
                            for row in item.map_message.rows:
                                gameMap.append(
                                    [
                                        Proto2THUAI9.placeTypeDict.get(
                                            place,
                                            THUAI9.PlaceType.NullPlaceType,
                                        )
                                        for place in row.cols
                                    ]
                                )
                            with self.__mtxState:
                                self.__currentState.gameMap = gameMap
                                self.__bufferState.gameMap = copy.deepcopy(gameMap)
                            break

                        self.__LoadBuffer(clientMsg)
                        self.__AILoop = True
                        self.__UnBlockAI()
                    elif self.__gameState == THUAI9.GameState.GameRunning:
                        self.__LoadBuffer(clientMsg)

                with self.__cvBuffer:
                    self.__bufferUpdated = True
                    self.__counterBuffer = -1
                    self.__cvBuffer.notify_all()
                self.__AILoop = False
            except Exception:
                self.__logger.exception("Message thread crashed")
                self.__AILoop = False

        threading.Thread(target=messageThread, daemon=True).start()

    def __UnBlockAI(self) -> None:
        with self.__cvAI:
            self.__AIStart = True
            self.__cvAI.notify_all()

    def __Update(self) -> None:
        if Setting.Asynchronous():
            return
        with self.__cvBuffer:
            self.__cvBuffer.wait_for(lambda: self.__bufferUpdated)
            with self.__mtxState:
                self.__currentState, self.__bufferState = (
                    self.__bufferState,
                    self.__currentState,
                )
                self.__counterState = self.__counterBuffer
            self.__bufferUpdated = False

    def __Wait(self) -> None:
        self.__freshed = False
        with self.__cvBuffer:
            self.__cvBuffer.wait_for(lambda: self.__freshed)

    def Main(
        self,
        createAI: Callable[[int], IAI],
        IP: str,
        port: str,
        file: bool,
        screen: bool,
        warnOnly: bool,
    ) -> None:
        self.__logger.handlers.clear()
        self.__logger.propagate = False
        self.__logger.setLevel(logging.DEBUG)

        logs_dir = Path(__file__).resolve().parent.parent / "logs"
        logs_dir.mkdir(parents=True, exist_ok=True)

        formatter = logging.Formatter(
            "[logic] [%(asctime)s.%(msecs)03d] [%(levelname)s] %(message)s",
            "%H:%M:%S",
        )

        if file:
            fileHandler = logging.FileHandler(
                logs_dir / f"logic-{self.__playerID}-{self.__teamID}-log.txt",
                mode="w",
                encoding="utf-8",
            )
            fileHandler.setLevel(logging.DEBUG)
            fileHandler.setFormatter(formatter)
            self.__logger.addHandler(fileHandler)

        if screen:
            screenHandler = logging.StreamHandler()
            screenHandler.setLevel(logging.WARNING if warnOnly else logging.INFO)
            screenHandler.setFormatter(formatter)
            self.__logger.addHandler(screenHandler)

        self.__comm = Communication(IP, port)

        if self.__playerType == THUAI9.PlayerType.Character:
            if not file and not screen:
                self.__timer = CharacterAPI(self)
            else:
                self.__timer = CharacterDebugAPI(
                    self, file, screen, warnOnly, self.__playerID, self.__teamID
                )
        else:
            if not file and not screen:
                self.__timer = TeamAPI(self)
            else:
                self.__timer = TeamDebugAPI(
                    self, file, screen, warnOnly, self.__playerID, self.__teamID
                )

        def aiThread() -> None:
            try:
                with self.__cvAI:
                    self.__cvAI.wait_for(lambda: self.__AIStart)

                ai = createAI(self.__playerID)
                while self.__AILoop:
                    if Setting.Asynchronous():
                        self.__Wait()
                    else:
                        self.__Update()

                    assert self.__timer is not None
                    self.__timer.StartTimer()
                    self.__timer.Play(ai)
                    self.__timer.EndTimer()
            except Exception:
                self.__logger.exception("AI thread crashed")

        retryCount = 0
        while not self.TryConnection():
            retryCount += 1
            self.__logger.warning(
                "Failed to connect to server %s:%s (attempt %s). Retrying in 1 second...",
                IP,
                port,
                retryCount,
            )
            time.sleep(1)

        if retryCount > 0:
            self.__logger.info(
                "Connected to server %s:%s after %s retries.", IP, port, retryCount
            )

        self.__threadAI = threading.Thread(target=aiThread)
        self.__threadAI.start()
        self.__ProcessMessage()
        self.__threadAI.join()
