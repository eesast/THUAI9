from __future__ import annotations

from dataclasses import dataclass, field
from enum import IntEnum
from typing import Dict, List, Tuple


class GameMode(IntEnum):
    NullGameMode = 0
    GameModePve = 1
    GameModePvp = 2


class GameState(IntEnum):
    NullGameState = 0
    GameStart = 1
    GameRunning = 2
    GameEnd = 3


class PlaceType(IntEnum):
    NullPlaceType = 0
    Factory = 1
    Space = 2
    Barrier = 3
    Bush = 4
    Resource = 5
    ComputeCenter = 6
    Market = 7


class ShapeType(IntEnum):
    NullShapeType = 0
    Circle = 1
    Square = 2


class PlayerType(IntEnum):
    NullPlayerType = 0
    Character = 1
    Team = 2


class CharacterType(IntEnum):
    NullCharacterType = 0
    Drone = 1
    Robot = 2
    AutonomousCar = 3


class CharacterState(IntEnum):
    NoneState = 0
    Idle = 1
    Harvesting = 2
    Attacking = 3
    Ocuppying = 4
    Trading = 5
    Moving = 6
    KnockedBack = 7
    Deceased = 8


class HomeState(IntEnum):
    NullHomeState = 0
    HomeStateIdle = 1
    HomeStateProducingProduct = 2
    HomeStateRepairing = 3
    HomeStateProducingCharacter = 4


class ComputeCenterState(IntEnum):
    NullComputeCenterState = 0
    Occupyable = 1
    Occupied = 2
    Robbed = 3


class ResourceState(IntEnum):
    NullResourceState = 0
    Harvestable = 1
    BeingHarvested = 2
    Harvested = 3


class ResourceType(IntEnum):
    NullResourceType = 0
    SmallResource = 1
    MediumResource = 2
    LargeResource = 3


class NewsType(IntEnum):
    NullNewsType = 0
    Text = 1
    Binary = 2


class GoodsType(IntEnum):
    NullGoodsType = 0
    Semiconductor = 1
    Medicine = 2
    Toys = 3
    Clothes = 4
    Food = 5


class MarketType(IntEnum):
    NullMarketType = 0
    SmallMarket = 1
    MediumMarket = 2
    LargeMarket = 3


class TechType(IntEnum):
    NullTechType = 0
    IncreaseHP = 1
    IncreaseAttackPower = 2
    IncreaseAttackSize = 3
    IncreaseRobust = 4
    IncreaseMoveSpeed = 5
    IncreaseCarryCapacity = 6
    IncreaseEfficiency = 7
    IncreaseProduction = 8
    IncreaseStorage = 9
    IncreasePrice = 10
    DecreaseCost = 11


class AIEventCategory(IntEnum):
    NullAIEventCategory = 0
    EconomicEvent = 1
    WeatherEvent = 2
    TechnologyEvent = 3
    CombatEvent = 4


class TaskType(IntEnum):
    NullTaskType = 0
    ProduceProduct = 1
    HarvestResource = 2
    OccupyCenter = 3
    RepairUnit = 4


class AIActionType(IntEnum):
    Unknown = 0
    Produce = 1
    Harvest = 2
    Move = 3
    Attack = 4
    Repair = 5
    Sell = 6
    Occupy = 7


class MessageOfObj(IntEnum):
    NullMessageOfObj = 0
    FactoryMessage = 1
    CharacterMessage = 2
    ResourceMessage = 3
    MarketMessage = 4
    ComputeCenterMessage = 5
    MapMessage = 6
    NewsMessage = 7
    TeamMessage = 8
    BarrierMessage = 9
    BushMessage = 10


@dataclass
class Character:
    guid: int = 0
    teamID: int = 0
    playerID: int = 0
    characterType: CharacterType = CharacterType.NullCharacterType
    characterActiveState: CharacterState = CharacterState.NoneState
    x: int = 0
    y: int = 0
    facingDirection: float = 0.0
    speed: int = 0
    viewRange: int = 0
    commonAttack: int = 0
    commonAttackCD: int = 0
    commonAttackRange: int = 0
    hp: int = 0
    carryCapacity: int = 0
    currentLoad: int = 0
    harvestRatePerSec: int = 0


@dataclass
class Team:
    teamID: int = 0
    playerID: int = 0
    score: int = 0
    material: int = 0
    computePower: int = 0
    factoryHP: int = 0
    techLevels: Dict[str, int] = field(default_factory=dict)


@dataclass
class Factory:
    factoryID: int = 0
    teamID: int = 0
    x: int = 0
    y: int = 0
    hp: int = 0
    robust: int = 0
    storage: int = 0
    efficiency: int = 0
    source: int = 0
    computingPower: int = 0
    canProduce: bool = False
    canRecruit: bool = False
    productInventory: Dict[GoodsType, int] = field(default_factory=dict)


@dataclass
class MarketGoodsInfo:
    price: int = 0
    tradedQuantity: int = 0


@dataclass
class Market:
    marketID: int = 0
    x: int = 0
    y: int = 0
    marketType: MarketType = MarketType.NullMarketType
    priceList: Dict[GoodsType, MarketGoodsInfo] = field(default_factory=dict)


@dataclass
class ComputeCenter:
    centerID: int = 0
    x: int = 0
    y: int = 0
    ownerTeamID: int = 0
    occupyProgress: int = 0
    state: ComputeCenterState = ComputeCenterState.NullComputeCenterState


@dataclass
class Resource:
    resourceID: int = 0
    resourceType: ResourceType = ResourceType.NullResourceType
    x: int = 0
    y: int = 0
    state: ResourceState = ResourceState.NullResourceState


CellXY = Tuple[int, int]


@dataclass
class GameMap:
    factories: Dict[CellXY, Factory] = field(default_factory=dict)
    markets: Dict[CellXY, Market] = field(default_factory=dict)
    computeCenters: Dict[CellXY, ComputeCenter] = field(default_factory=dict)
    resources: Dict[CellXY, Resource] = field(default_factory=dict)


@dataclass
class TeamGameInfo:
    teamID: int = 0
    score: int = 0
    material: int = 0
    computePower: int = 0
    factoryHP: int = 0
    techLevels: Dict[str, int] = field(default_factory=dict)


@dataclass
class GameInfo:
    gameTime: int = 0
    teams: List[TeamGameInfo] = field(default_factory=list)
