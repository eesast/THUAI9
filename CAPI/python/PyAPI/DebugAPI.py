from __future__ import annotations

import datetime
import logging
from pathlib import Path

from PyAPI.API import CharacterAPI, TeamAPI
from PyAPI.Interface import ILogic
import PyAPI.structures as THUAI9


def _create_api_logger(
    file: bool,
    screen: bool,
    warnOnly: bool,
    playerID: int,
    teamID: int,
) -> logging.Logger:
    logs_dir = Path(__file__).resolve().parent.parent / "logs"
    logs_dir.mkdir(parents=True, exist_ok=True)

    logger = logging.getLogger(f"api-{teamID}-{playerID}")
    logger.handlers.clear()
    logger.propagate = False
    logger.setLevel(logging.DEBUG)

    formatter = logging.Formatter(
        f"[api {teamID}-{playerID}] [%(asctime)s.%(msecs)03d] [%(levelname)s] %(message)s",
        "%H:%M:%S",
    )

    if file:
        file_handler = logging.FileHandler(
            logs_dir / f"api-{teamID}-{playerID}-log.txt",
            mode="w",
            encoding="utf-8",
        )
        file_handler.setLevel(logging.DEBUG)
        file_handler.setFormatter(formatter)
        logger.addHandler(file_handler)

    if screen:
        stream_handler = logging.StreamHandler()
        stream_handler.setLevel(logging.WARNING if warnOnly else logging.INFO)
        stream_handler.setFormatter(formatter)
        logger.addHandler(stream_handler)

    return logger


class CharacterDebugAPI(CharacterAPI):
    def __init__(
        self,
        logic: ILogic,
        file: bool,
        screen: bool,
        warnOnly: bool,
        playerID: int,
        teamID: int,
    ) -> None:
        super().__init__(logic)
        self._logger = _create_api_logger(file, screen, warnOnly, playerID, teamID)
        self._startPoint = datetime.datetime.now()

    def StartTimer(self) -> None:
        self._startPoint = datetime.datetime.now()
        self._logger.info("=== AI.play() ===")
        self._logger.info("StartTimer: %s", self._startPoint.isoformat(timespec="milliseconds"))

    def EndTimer(self) -> None:
        delta = datetime.datetime.now() - self._startPoint
        self._logger.info("Time elapsed: %.3fms", delta.total_seconds() * 1000)

    def SendTextMessage(self, toPlayerID: int, message: str):
        self._logger.info("SendTextMessage to %s", toPlayerID)
        return super().SendTextMessage(toPlayerID, message)

    def SendBinaryMessage(self, toPlayerID: int, message: bytes):
        self._logger.info("SendBinaryMessage to %s", toPlayerID)
        return super().SendBinaryMessage(toPlayerID, message)

    def Move(self, moveTimeInMilliseconds: int, angleInRadian: float):
        self._logger.info("Move %sms angle=%s", moveTimeInMilliseconds, angleInRadian)
        return super().Move(moveTimeInMilliseconds, angleInRadian)

    def Common_Attack(self, attackedPlayerID: int):
        self._logger.info("Common_Attack %s", attackedPlayerID)
        return super().Common_Attack(attackedPlayerID)

    def Recover(self, recover: int):
        self._logger.info("Recover %s", recover)
        return super().Recover(recover)

    def Harvest(self):
        self._logger.info("Harvest")
        return super().Harvest()

    def Occupy(self):
        self._logger.info("Occupy")
        return super().Occupy()

    def Load(self, goodsType: THUAI9.GoodsType, amount: int):
        self._logger.info("Load %s x%s", goodsType.name, amount)
        return super().Load(goodsType, amount)

    def Buy(self, goodsType: THUAI9.GoodsType, amount: int):
        self._logger.info("Buy %s x%s", goodsType.name, amount)
        return super().Buy(goodsType, amount)

    def Sell(self, goodsType: THUAI9.GoodsType, amount: int):
        self._logger.info("Sell %s x%s", goodsType.name, amount)
        return super().Sell(goodsType, amount)

    def EndAllAction(self):
        self._logger.info("EndAllAction")
        return super().EndAllAction()

    def Print(self, string: str) -> None:
        self._logger.info("%s", string)

    def PrintCharacter(self) -> None:
        for character in self._logic.GetCharacters():
            self._logger.info(
                "Character id=%s, team=%s, type=%s, pos=(%s, %s)",
                character.playerID,
                character.teamID,
                character.characterType.name,
                character.x,
                character.y,
            )

    def PrintSelfInfo(self) -> None:
        selfInfo = self._logic.CharacterGetSelfInfo()
        if selfInfo is None:
            return
        self._logger.info(
            "Self id=%s, team=%s, type=%s, pos=(%s, %s)",
            selfInfo.playerID,
            selfInfo.teamID,
            selfInfo.characterType.name,
            selfInfo.x,
            selfInfo.y,
        )


class TeamDebugAPI(TeamAPI):
    def __init__(
        self,
        logic: ILogic,
        file: bool,
        screen: bool,
        warnOnly: bool,
        playerID: int,
        teamID: int,
    ) -> None:
        super().__init__(logic)
        self._logger = _create_api_logger(file, screen, warnOnly, playerID, teamID)
        self._startPoint = datetime.datetime.now()

    def StartTimer(self) -> None:
        self._startPoint = datetime.datetime.now()
        self._logger.info("=== AI.play() ===")
        self._logger.info("StartTimer: %s", self._startPoint.isoformat(timespec="milliseconds"))

    def EndTimer(self) -> None:
        delta = datetime.datetime.now() - self._startPoint
        self._logger.info("Time elapsed: %.3fms", delta.total_seconds() * 1000)

    def SendTextMessage(self, toPlayerID: int, message: str):
        self._logger.info("SendTextMessage to %s", toPlayerID)
        return super().SendTextMessage(toPlayerID, message)

    def SendBinaryMessage(self, toPlayerID: int, message: bytes):
        self._logger.info("SendBinaryMessage to %s", toPlayerID)
        return super().SendBinaryMessage(toPlayerID, message)

    def BuildCharacter(self, characterType: THUAI9.CharacterType, playerID: int):
        self._logger.info("BuildCharacter %s for player %s", characterType.name, playerID)
        return super().BuildCharacter(characterType, playerID)

    def ProduceGoods(self, goodsType: THUAI9.GoodsType, maxProduceNum: int):
        self._logger.info("ProduceGoods %s x%s", goodsType.name, maxProduceNum)
        return super().ProduceGoods(goodsType, maxProduceNum)

    def UplevelTech(self, techType: THUAI9.TechType):
        self._logger.info("UplevelTech %s", techType.name)
        return super().UplevelTech(techType)

    def EndAllAction(self):
        self._logger.info("EndAllAction")
        return super().EndAllAction()

    def Print(self, string: str) -> None:
        self._logger.info("%s", string)

    def PrintSelfInfo(self) -> None:
        team = self._logic.TeamGetSelfInfo()
        if team is None:
            return
        self._logger.info(
            "Team id=%s, score=%s, material=%s, computePower=%s, factoryHP=%s, techLevels=%s",
            team.teamID,
            team.score,
            team.material,
            team.computePower,
            team.factoryHP,
            team.techLevels,
        )
