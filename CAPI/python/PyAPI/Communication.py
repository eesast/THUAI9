from __future__ import annotations

import threading
from typing import Optional, Union

import grpc
import Message2Clients_pb2 as Message2Clients
import PyAPI.structures as THUAI9
import Services_pb2_grpc as Services
from PyAPI.utils import THUAI9Proto


class Communication:
    def __init__(self, serverIP: str, serverPort: str) -> None:
        aim = f"{serverIP}:{serverPort}"
        self.__channel = grpc.insecure_channel(aim)
        self.__stub = Services.AvailableServiceStub(self.__channel)

        self.__haveNewMessage = False
        self.__message2Client: Optional[Message2Clients.MessageToClient] = None
        self.__cvMessage = threading.Condition()

        self.__mtxLimit = threading.Lock()
        self.__counter = 0
        self.__counterMove = 0
        self.__limit = 50
        self.__moveLimit = 10

    def __consume_action_quota(self, countAsMove: bool) -> bool:
        with self.__mtxLimit:
            if self.__counter >= self.__limit:
                return False
            if countAsMove and self.__counterMove >= self.__moveLimit:
                return False
            self.__counter += 1
            if countAsMove:
                self.__counterMove += 1
            return True

    def __consume_quota(self) -> bool:
        with self.__mtxLimit:
            if self.__counter >= self.__limit:
                return False
            self.__counter += 1
            return True

    def TryConnection(self, playerID: int, teamID: int) -> bool:
        try:
            response = self.__stub.TryConnection(
                THUAI9Proto.THUAI92ProtobufIDMsg(playerID, teamID)
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def GetMessage2Client(self) -> Message2Clients.MessageToClient:
        with self.__cvMessage:
            self.__cvMessage.wait_for(lambda: self.__haveNewMessage)
            self.__haveNewMessage = False
            if self.__message2Client is None:
                return Message2Clients.MessageToClient()
            return self.__message2Client

    def AddPlayer(
        self,
        playerID: int,
        teamID: int,
        characterType: THUAI9.CharacterType,
        sideFlag: bool,
    ) -> None:
        del characterType

        def stream_messages() -> None:
            try:
                request = THUAI9Proto.THUAI92ProtobufRegisterFactoryMsg(
                    playerID, teamID, sideFlag
                )
                for message in self.__stub.RegisterFactory(request):
                    with self.__cvMessage:
                        self.__message2Client = message
                        self.__haveNewMessage = True
                        with self.__mtxLimit:
                            self.__counter = 0
                            self.__counterMove = 0
                        self.__cvMessage.notify()
            except grpc.RpcError:
                return

        threading.Thread(target=stream_messages, daemon=True).start()

    def EndAllAction(self, playerID: int, teamID: int) -> bool:
        if not self.__consume_action_quota(countAsMove=True):
            return False
        try:
            response = self.__stub.EndAllAction(
                THUAI9Proto.THUAI92ProtobufIDMsg(playerID, teamID)
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Move(
        self,
        playerID: int,
        teamID: int,
        moveTimeInMilliseconds: int,
        angle: float,
    ) -> bool:
        if not self.__consume_action_quota(countAsMove=True):
            return False
        try:
            response = self.__stub.Move(
                THUAI9Proto.THUAI92ProtobufMoveMsg(
                    teamID, playerID, moveTimeInMilliseconds, angle
                )
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Recover(self, playerID: int, recover: int, teamID: int) -> bool:
        if not self.__consume_action_quota(countAsMove=True):
            return False
        try:
            response = self.__stub.Recover(
                THUAI9Proto.THUAI92ProtobufRecoverMsg(playerID, recover, teamID)
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Harvest(self, playerID: int, teamID: int) -> bool:
        if not self.__consume_action_quota(countAsMove=True):
            return False
        try:
            response = self.__stub.Harvest(
                THUAI9Proto.THUAI92ProtobufHarvestMsg(playerID, teamID)
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Occupy(self, playerID: int, teamID: int) -> bool:
        if not self.__consume_action_quota(countAsMove=True):
            return False
        try:
            response = self.__stub.Occupy(
                THUAI9Proto.THUAI92ProtobufOccupyMsg(playerID, teamID)
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Load(
        self, playerID: int, teamID: int, goodsType: THUAI9.GoodsType, amount: int
    ) -> bool:
        if not self.__consume_action_quota(countAsMove=True):
            return False
        try:
            response = self.__stub.Load(
                THUAI9Proto.THUAI92ProtobufLoadMsg(playerID, teamID, goodsType, amount)
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Trade(
        self,
        playerID: int,
        teamID: int,
        goodsType: THUAI9.GoodsType,
        amount: int,
        isBuy: bool,
    ) -> bool:
        if not self.__consume_action_quota(countAsMove=True):
            return False
        try:
            response = self.__stub.Trade(
                THUAI9Proto.THUAI92ProtobufTradeMsg(
                    playerID, teamID, goodsType, amount, isBuy
                )
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Common_Attack(
        self,
        teamID: int,
        playerID: int,
        attackedTeamID: int,
        attackedPlayerID: int,
    ) -> bool:
        if not self.__consume_quota():
            return False
        try:
            response = self.__stub.Attack(
                THUAI9Proto.THUAI92ProtobufAttackMsg(
                    teamID, playerID, attackedTeamID, attackedPlayerID
                )
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def Send(
        self,
        playerID: int,
        toPlayerID: int,
        teamID: int,
        message: Union[str, bytes],
        binary: bool,
    ) -> bool:
        if not self.__consume_quota():
            return False
        try:
            response = self.__stub.Send(
                THUAI9Proto.THUAI92ProtobufSendMsg(
                    playerID, toPlayerID, teamID, message, binary
                )
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def BuildCharacter(
        self, teamID: int, playerID: int, characterType: THUAI9.CharacterType
    ) -> bool:
        if not self.__consume_quota():
            return False
        try:
            response = self.__stub.CreateCharacter(
                THUAI9Proto.THUAI92ProtobufCreateCharacterMsg(
                    teamID, playerID, characterType
                )
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def ProduceGoods(
        self, teamID: int, goodsType: THUAI9.GoodsType, maxProduceNum: int
    ) -> bool:
        if not self.__consume_quota():
            return False
        try:
            response = self.__stub.Produce(
                THUAI9Proto.THUAI92ProtobufProduceGoodsMsg(
                    teamID, goodsType, maxProduceNum
                )
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)

    def UplevelTech(self, teamID: int, techType: THUAI9.TechType) -> bool:
        if not self.__consume_quota():
            return False
        try:
            response = self.__stub.UplevelTech(
                THUAI9Proto.THUAI92ProtobufUplevelTechMsg(teamID, techType)
            )
        except grpc.RpcError:
            return False
        return bool(response.act_success)
