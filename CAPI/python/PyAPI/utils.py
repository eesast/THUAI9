from __future__ import annotations

from typing import Dict, List, Optional, Union

import Message2Clients_pb2 as Message2Clients
import Message2Server_pb2 as Message2Server
import MessageType_pb2 as MessageType
import PyAPI.structures as THUAI9


numOfGridPerCell = 1000


class AssistFunction:
    @staticmethod
    def CellToGrid(cell: int) -> int:
        return cell * numOfGridPerCell + numOfGridPerCell // 2

    @staticmethod
    def GridToCell(grid: Union[int, float]) -> int:
        return int(grid) // numOfGridPerCell

    @staticmethod
    def HaveView(
        x: int,
        y: int,
        newX: int,
        newY: int,
        viewRange: int,
        gameMap: List[List[THUAI9.PlaceType]],
    ) -> bool:
        if not gameMap or not gameMap[0]:
            return False

        my_cell_x = AssistFunction.GridToCell(x)
        my_cell_y = AssistFunction.GridToCell(y)
        new_cell_x = AssistFunction.GridToCell(newX)
        new_cell_y = AssistFunction.GridToCell(newY)

        if (
            my_cell_x < 0
            or my_cell_y < 0
            or new_cell_x < 0
            or new_cell_y < 0
            or my_cell_x >= len(gameMap)
            or new_cell_x >= len(gameMap)
            or my_cell_y >= len(gameMap[0])
            or new_cell_y >= len(gameMap[0])
        ):
            return False

        delta_x = float(newX - x)
        delta_y = float(newY - y)
        distance = delta_x * delta_x + delta_y * delta_y

        my_place = gameMap[my_cell_x][my_cell_y]
        new_place = gameMap[new_cell_x][new_cell_y]

        if new_place == THUAI9.PlaceType.Bush and my_place != THUAI9.PlaceType.Bush:
            return False
        if distance > float(viewRange * viewRange):
            return False

        divide = int(max(abs(delta_x), abs(delta_y)) / 100)
        if divide == 0:
            return True

        dx = delta_x / divide
        dy = delta_y / divide
        current_x = float(x)
        current_y = float(y)

        for _ in range(divide):
            current_x += dx
            current_y += dy
            cell_x = AssistFunction.GridToCell(current_x)
            cell_y = AssistFunction.GridToCell(current_y)
            if (
                cell_x < 0
                or cell_y < 0
                or cell_x >= len(gameMap)
                or cell_y >= len(gameMap[0])
            ):
                return False

            place = gameMap[cell_x][cell_y]
            if new_place == THUAI9.PlaceType.Bush and my_place == THUAI9.PlaceType.Bush:
                if place != THUAI9.PlaceType.Bush:
                    return False
            elif place == THUAI9.PlaceType.Barrier:
                return False

        return True


def _map_lookup(mapping: Dict[int, object], key: int, default: object) -> object:
    return mapping.get(key, default)


class Proto2THUAI9:
    gameStateDict = {
        MessageType.NULL_GAME_STATE: THUAI9.GameState.NullGameState,
        MessageType.GAME_START: THUAI9.GameState.GameStart,
        MessageType.GAME_RUNNING: THUAI9.GameState.GameRunning,
        MessageType.GAME_END: THUAI9.GameState.GameEnd,
    }

    messageOfObjDict = {
        "character_message": THUAI9.MessageOfObj.CharacterMessage,
        "factory_message": THUAI9.MessageOfObj.FactoryMessage,
        "resource_message": THUAI9.MessageOfObj.ResourceMessage,
        "market_message": THUAI9.MessageOfObj.MarketMessage,
        "compute_center_message": THUAI9.MessageOfObj.ComputeCenterMessage,
        "map_message": THUAI9.MessageOfObj.MapMessage,
        "news_message": THUAI9.MessageOfObj.NewsMessage,
        "team_message": THUAI9.MessageOfObj.TeamMessage,
        "barrier_message": THUAI9.MessageOfObj.BarrierMessage,
        "bush_message": THUAI9.MessageOfObj.BushMessage,
        None: THUAI9.MessageOfObj.NullMessageOfObj,
    }

    placeTypeDict = {
        MessageType.NULL_PLACE_TYPE: THUAI9.PlaceType.NullPlaceType,
        MessageType.FACTORY: THUAI9.PlaceType.Factory,
        MessageType.SPACE: THUAI9.PlaceType.Space,
        MessageType.BARRIER: THUAI9.PlaceType.Barrier,
        MessageType.BUSH: THUAI9.PlaceType.Bush,
        MessageType.RESOURCE: THUAI9.PlaceType.Resource,
        MessageType.COMPUTE_CENTER: THUAI9.PlaceType.ComputeCenter,
        MessageType.MARKET: THUAI9.PlaceType.Market,
    }

    characterTypeDict = {
        MessageType.NULL_CHARACTER_TYPE: THUAI9.CharacterType.NullCharacterType,
        MessageType.DRONE: THUAI9.CharacterType.Drone,
        MessageType.ROBOT: THUAI9.CharacterType.Robot,
        MessageType.AUTONOMOUS_CAR: THUAI9.CharacterType.AutonomousCar,
    }

    characterStateDict = {
        MessageType.CHARACTER_STATE_NONE: THUAI9.CharacterState.NoneState,
        MessageType.CHARACTER_STATE_IDLE: THUAI9.CharacterState.Idle,
        MessageType.CHARACTER_STATE_HARVESTING: THUAI9.CharacterState.Harvesting,
        MessageType.CHARACTER_STATE_ATTACKING: THUAI9.CharacterState.Attacking,
        MessageType.CHARACTER_STATE_OCUPPYING: THUAI9.CharacterState.Ocuppying,
        MessageType.CHARACTER_STATE_TRADING: THUAI9.CharacterState.Trading,
        MessageType.CHARACTER_STATE_MOVING: THUAI9.CharacterState.Moving,
        MessageType.CHARACTER_STATE_KNOCKED_BACK: THUAI9.CharacterState.KnockedBack,
        MessageType.CHARACTER_STATE_DECEASED: THUAI9.CharacterState.Deceased,
    }

    resourceTypeDict = {
        MessageType.NULL_RESOURCE_TYPE: THUAI9.ResourceType.NullResourceType,
        MessageType.SMALL_RESOURCE: THUAI9.ResourceType.SmallResource,
        MessageType.MEDIUM_RESOURCE: THUAI9.ResourceType.MediumResource,
        MessageType.LARGE_RESOURCE: THUAI9.ResourceType.LargeResource,
    }

    resourceStateDict = {
        MessageType.NULL_ECONOMY_RESOURCE_STSTE: THUAI9.ResourceState.NullResourceState,
        MessageType.HARVESTABLE: THUAI9.ResourceState.Harvestable,
        MessageType.BEING_HARVESTED: THUAI9.ResourceState.BeingHarvested,
        MessageType.HARVESTED: THUAI9.ResourceState.Harvested,
    }

    goodsTypeDict = {
        MessageType.NULL_GOODS_TYPE: THUAI9.GoodsType.NullGoodsType,
        MessageType.SEMICONDUCTOR: THUAI9.GoodsType.Semiconductor,
        MessageType.MEDICINE: THUAI9.GoodsType.Medicine,
        MessageType.TOYS: THUAI9.GoodsType.Toys,
        MessageType.CLOTHES: THUAI9.GoodsType.Clothes,
        MessageType.FOOD: THUAI9.GoodsType.Food,
    }

    marketTypeDict = {
        MessageType.NULL_MARKET_TYPE: THUAI9.MarketType.NullMarketType,
        MessageType.SMALL_MARKET: THUAI9.MarketType.SmallMarket,
        MessageType.MEDIUM_MARKET: THUAI9.MarketType.MediumMarket,
        MessageType.LARGE_MARKET: THUAI9.MarketType.LargeMarket,
    }

    techTypeDict = {
        MessageType.NULL_TECH_TYPE: THUAI9.TechType.NullTechType,
        MessageType.INCREASE_HP: THUAI9.TechType.IncreaseHP,
        MessageType.INCREASE_ATTACK_POWER: THUAI9.TechType.IncreaseAttackPower,
        MessageType.INCREASE_ATTACK_SIZE: THUAI9.TechType.IncreaseAttackSize,
        MessageType.INCREASE_ROBUST: THUAI9.TechType.IncreaseRobust,
        MessageType.INCREASE_MOVE_SPEED: THUAI9.TechType.IncreaseMoveSpeed,
        MessageType.INCREASE_CARRY_CAPACITY: THUAI9.TechType.IncreaseCarryCapacity,
        MessageType.INCREASE_EFFICIENCY: THUAI9.TechType.IncreaseEfficiency,
        MessageType.INCREASE_PRODUCTION: THUAI9.TechType.IncreaseProduction,
        MessageType.INCREASE_STORAGE: THUAI9.TechType.IncreaseStorage,
        MessageType.INCREASE_PRICE: THUAI9.TechType.IncreasePrice,
        MessageType.DECREASE_COST: THUAI9.TechType.DecreaseCost,
    }

    newsTypeDict = {
        None: THUAI9.NewsType.NullNewsType,
        "text_message": THUAI9.NewsType.Text,
        "binary_message": THUAI9.NewsType.Binary,
    }

    @staticmethod
    def Protobuf2THUAI9Character(
        characterMsg: Message2Clients.MessageOfCharacter,
    ) -> THUAI9.Character:
        return THUAI9.Character(
            guid=characterMsg.guid,
            teamID=characterMsg.team_id,
            playerID=characterMsg.player_id,
            characterType=_map_lookup(
                Proto2THUAI9.characterTypeDict,
                characterMsg.character_type,
                THUAI9.CharacterType.NullCharacterType,
            ),
            characterActiveState=_map_lookup(
                Proto2THUAI9.characterStateDict,
                characterMsg.character_active_state,
                THUAI9.CharacterState.NoneState,
            ),
            x=characterMsg.x,
            y=characterMsg.y,
            facingDirection=characterMsg.facing_direction,
            speed=characterMsg.speed,
            viewRange=characterMsg.view_range,
            commonAttack=characterMsg.common_attack,
            commonAttackCD=characterMsg.common_attack_cd,
            commonAttackRange=characterMsg.common_attack_range,
            hp=characterMsg.hp,
            carryCapacity=characterMsg.carry_capacity,
            currentLoad=characterMsg.current_load,
            harvestRatePerSec=characterMsg.harvest_rate_per_sec,
        )

    @staticmethod
    def Protobuf2THUAI9Team(teamMsg: Message2Clients.MessageOfTeam) -> THUAI9.Team:
        return THUAI9.Team(
            teamID=teamMsg.team_id,
            playerID=teamMsg.player_id,
            score=teamMsg.score,
            material=teamMsg.material,
            computePower=teamMsg.compute_power,
            techLevels=dict(teamMsg.tech_levels),
        )

    @staticmethod
    def Protobuf2THUAI9GameInfo(
        gameInfoMsg: Message2Clients.MessageOfAll,
    ) -> THUAI9.GameInfo:
        teams: List[THUAI9.TeamGameInfo] = []
        for index, teamMsg in enumerate(gameInfoMsg.teams, start=1):
            teams.append(
                THUAI9.TeamGameInfo(
                    teamID=index,
                    score=teamMsg.score,
                    material=teamMsg.material,
                    computePower=teamMsg.compute_power,
                    factoryHP=teamMsg.factory_hp,
                    techLevels=dict(teamMsg.tech_levels),
                )
            )
        return THUAI9.GameInfo(gameTime=gameInfoMsg.game_time, teams=teams)

    @staticmethod
    def Protobuf2THUAI9Resource(
        resourceMsg: Message2Clients.MessageOfResource,
    ) -> THUAI9.Resource:
        return THUAI9.Resource(
            resourceID=resourceMsg.id,
            resourceType=_map_lookup(
                Proto2THUAI9.resourceTypeDict,
                resourceMsg.resource_type,
                THUAI9.ResourceType.NullResourceType,
            ),
            x=resourceMsg.x,
            y=resourceMsg.y,
            state=_map_lookup(
                Proto2THUAI9.resourceStateDict,
                resourceMsg.resource_state,
                THUAI9.ResourceState.NullResourceState,
            ),
        )

    @staticmethod
    def Protobuf2THUAI9Factory(
        factoryMsg: Message2Clients.MessageOfFactory,
    ) -> THUAI9.Factory:
        factory = THUAI9.Factory(
            factoryID=factoryMsg.factory_id,
            teamID=factoryMsg.team_id,
            x=factoryMsg.x,
            y=factoryMsg.y,
            hp=factoryMsg.hp,
            robust=factoryMsg.robust,
            storage=factoryMsg.storage,
            efficiency=factoryMsg.efficiency,
            source=factoryMsg.source,
            computingPower=factoryMsg.computing_power,
            canProduce=factoryMsg.can_produce,
            canRecruit=factoryMsg.can_recruit,
        )
        for goods in factoryMsg.product_inventory:
            goodsType = _map_lookup(
                Proto2THUAI9.goodsTypeDict,
                goods.product_type,
                THUAI9.GoodsType.NullGoodsType,
            )
            factory.productInventory[goodsType] = goods.quantity
        return factory

    @staticmethod
    def Protobuf2THUAI9Market(
        marketMsg: Message2Clients.MessageOfMarket,
    ) -> THUAI9.Market:
        market = THUAI9.Market(
            marketID=marketMsg.market_id,
            x=marketMsg.x,
            y=marketMsg.y,
            marketType=_map_lookup(
                Proto2THUAI9.marketTypeDict,
                marketMsg.market_type,
                THUAI9.MarketType.NullMarketType,
            ),
        )
        for entry in marketMsg.price_list:
            goodsType = _map_lookup(
                Proto2THUAI9.goodsTypeDict,
                entry.goods_type,
                THUAI9.GoodsType.NullGoodsType,
            )
            market.priceList[goodsType] = THUAI9.MarketGoodsInfo(
                price=entry.price,
                tradedQuantity=entry.traded_quantity,
            )
        return market

    @staticmethod
    def Protobuf2THUAI9ComputeCenter(
        centerMsg: Message2Clients.MessageOfComputeCenter,
    ) -> THUAI9.ComputeCenter:
        return THUAI9.ComputeCenter(
            centerID=centerMsg.center_id,
            x=centerMsg.x,
            y=centerMsg.y,
            ownerTeamID=centerMsg.owner_team_id,
            occupyProgress=centerMsg.occupy_progress,
            state=(
                THUAI9.ComputeCenterState.Occupyable
                if centerMsg.owner_team_id == 0
                else THUAI9.ComputeCenterState.Occupied
            ),
        )


class THUAI9Proto:
    characterTypeDict = {
        THUAI9.CharacterType.NullCharacterType: MessageType.NULL_CHARACTER_TYPE,
        THUAI9.CharacterType.Drone: MessageType.DRONE,
        THUAI9.CharacterType.Robot: MessageType.ROBOT,
        THUAI9.CharacterType.AutonomousCar: MessageType.AUTONOMOUS_CAR,
    }

    goodsTypeDict = {
        THUAI9.GoodsType.NullGoodsType: MessageType.NULL_GOODS_TYPE,
        THUAI9.GoodsType.Semiconductor: MessageType.SEMICONDUCTOR,
        THUAI9.GoodsType.Medicine: MessageType.MEDICINE,
        THUAI9.GoodsType.Toys: MessageType.TOYS,
        THUAI9.GoodsType.Clothes: MessageType.CLOTHES,
        THUAI9.GoodsType.Food: MessageType.FOOD,
    }

    techTypeDict = {
        THUAI9.TechType.NullTechType: MessageType.NULL_TECH_TYPE,
        THUAI9.TechType.IncreaseHP: MessageType.INCREASE_HP,
        THUAI9.TechType.IncreaseAttackPower: MessageType.INCREASE_ATTACK_POWER,
        THUAI9.TechType.IncreaseAttackSize: MessageType.INCREASE_ATTACK_SIZE,
        THUAI9.TechType.IncreaseRobust: MessageType.INCREASE_ROBUST,
        THUAI9.TechType.IncreaseMoveSpeed: MessageType.INCREASE_MOVE_SPEED,
        THUAI9.TechType.IncreaseCarryCapacity: MessageType.INCREASE_CARRY_CAPACITY,
        THUAI9.TechType.IncreaseEfficiency: MessageType.INCREASE_EFFICIENCY,
        THUAI9.TechType.IncreaseProduction: MessageType.INCREASE_PRODUCTION,
        THUAI9.TechType.IncreaseStorage: MessageType.INCREASE_STORAGE,
        THUAI9.TechType.IncreasePrice: MessageType.INCREASE_PRICE,
        THUAI9.TechType.DecreaseCost: MessageType.DECREASE_COST,
    }

    @staticmethod
    def THUAI92ProtobufMoveMsg(
        teamID: int, playerID: int, timeInMilliseconds: int, angle: float
    ) -> Message2Server.MoveMsg:
        moveMsg = Message2Server.MoveMsg()
        moveMsg.player_id = playerID
        moveMsg.team_id = teamID
        moveMsg.time_in_milliseconds = timeInMilliseconds
        moveMsg.angle = angle
        return moveMsg

    @staticmethod
    def THUAI92ProtobufIDMsg(playerID: int, teamID: int) -> Message2Server.IDMsg:
        idMsg = Message2Server.IDMsg()
        idMsg.player_id = playerID
        idMsg.team_id = teamID
        return idMsg

    @staticmethod
    def THUAI92ProtobufSendMsg(
        playerID: int,
        toPlayerID: int,
        teamID: int,
        message: Union[str, bytes],
        binary: bool,
    ) -> Message2Server.SendMsg:
        sendMsg = Message2Server.SendMsg()
        sendMsg.player_id = playerID
        sendMsg.to_player_id = toPlayerID
        sendMsg.team_id = teamID
        if binary:
            if isinstance(message, bytes):
                sendMsg.binary_message = message
            else:
                sendMsg.binary_message = str(message).encode("utf-8")
        else:
            if isinstance(message, bytes):
                sendMsg.text_message = message.decode("utf-8", errors="replace")
            else:
                sendMsg.text_message = message
        return sendMsg

    @staticmethod
    def THUAI92ProtobufRecoverMsg(
        playerID: int, recoveredHp: int, teamID: int
    ) -> Message2Server.RecoverMsg:
        recoverMsg = Message2Server.RecoverMsg()
        recoverMsg.player_id = playerID
        recoverMsg.team_id = teamID
        recoverMsg.recovered_hp = recoveredHp
        return recoverMsg

    @staticmethod
    def THUAI92ProtobufAttackMsg(
        teamID: int,
        playerID: int,
        attackedTeamID: int,
        attackedPlayerID: int,
    ) -> Message2Server.AttackMsg:
        attackMsg = Message2Server.AttackMsg()
        attackMsg.player_id = playerID
        attackMsg.team_id = teamID
        attackMsg.attack_range = 0
        attackMsg.attacked_player_id = attackedPlayerID
        attackMsg.attacked_team_id = attackedTeamID
        return attackMsg

    @staticmethod
    def THUAI92ProtobufCreateCharacterMsg(
        teamID: int,
        playerID: int,
        characterType: THUAI9.CharacterType,
    ) -> Message2Server.CreateCharacterMsg:
        createCharacterMsg = Message2Server.CreateCharacterMsg()
        createCharacterMsg.team_id = teamID
        createCharacterMsg.player_id = playerID
        createCharacterMsg.character_type = THUAI9Proto.characterTypeDict.get(
            characterType,
            MessageType.NULL_CHARACTER_TYPE,
        )
        return createCharacterMsg

    @staticmethod
    def THUAI92ProtobufHarvestMsg(
        playerID: int, teamID: int
    ) -> Message2Server.ResourceMsg:
        resourceMsg = Message2Server.ResourceMsg()
        resourceMsg.player_id = playerID
        resourceMsg.team_id = teamID
        resourceMsg.resource_id = 0
        resourceMsg.target_x = 0
        resourceMsg.target_y = 0
        resourceMsg.amount = 0
        return resourceMsg

    @staticmethod
    def THUAI92ProtobufOccupyMsg(
        playerID: int, teamID: int
    ) -> Message2Server.OccupyMsg:
        occupyMsg = Message2Server.OccupyMsg()
        occupyMsg.player_id = playerID
        occupyMsg.team_id = teamID
        occupyMsg.target_x = 0
        occupyMsg.target_y = 0
        occupyMsg.target_compute_center_id = 0
        return occupyMsg

    @staticmethod
    def THUAI92ProtobufLoadMsg(
        playerID: int,
        teamID: int,
        goodsType: THUAI9.GoodsType,
        amount: int,
    ) -> Message2Server.LoadMsg:
        loadMsg = Message2Server.LoadMsg()
        loadMsg.team_id = teamID
        loadMsg.player_id = playerID
        loadMsg.product_type = THUAI9Proto.goodsTypeDict.get(
            goodsType,
            MessageType.NULL_GOODS_TYPE,
        )
        loadMsg.product_amount = amount
        return loadMsg

    @staticmethod
    def THUAI92ProtobufTradeMsg(
        playerID: int,
        teamID: int,
        goodsType: THUAI9.GoodsType,
        amount: int,
        isBuy: bool,
    ) -> Message2Server.TradeMsg:
        tradeMsg = Message2Server.TradeMsg()
        tradeMsg.team_id = teamID
        tradeMsg.player_id = playerID
        tradeMsg.product_type = THUAI9Proto.goodsTypeDict.get(
            goodsType,
            MessageType.NULL_GOODS_TYPE,
        )
        tradeMsg.product_amount = amount
        tradeMsg.is_buy = isBuy
        return tradeMsg

    @staticmethod
    def THUAI92ProtobufProduceGoodsMsg(
        teamID: int,
        goodsType: THUAI9.GoodsType = THUAI9.GoodsType.NullGoodsType,
        maxProduceNum: int = 1,
    ) -> Message2Server.ProduceGoodsMsg:
        produceMsg = Message2Server.ProduceGoodsMsg()
        produceMsg.team_id = teamID
        produceMsg.product_type = THUAI9Proto.goodsTypeDict.get(
            goodsType,
            MessageType.NULL_GOODS_TYPE,
        )
        produceMsg.max_produce_num = maxProduceNum
        return produceMsg

    @staticmethod
    def THUAI92ProtobufUplevelTechMsg(
        teamID: int, techType: THUAI9.TechType
    ) -> Message2Server.UplevelTechMsg:
        techMsg = Message2Server.UplevelTechMsg()
        techMsg.team_id = teamID
        techMsg.tech_type = THUAI9Proto.techTypeDict.get(
            techType,
            MessageType.NULL_TECH_TYPE,
        )
        return techMsg

    @staticmethod
    def THUAI92ProtobufRegisterFactoryMsg(
        playerID: int, teamID: int, sideFlag: bool
    ) -> Message2Server.RegisterFactoryMsg:
        registerMsg = Message2Server.RegisterFactoryMsg()
        registerMsg.player_id = playerID
        registerMsg.team_id = teamID
        registerMsg.side_flag = 1 if sideFlag else 0
        return registerMsg
