# THUAI9 Python API 接口文档

## 简介

本文档说明 THUAI9 Python 选手接口 `PyAPI` 的使用方式，并详细介绍所有公开接口的功能、参数、返回值和典型用法。

对参赛选手来说，最重要的文件是：

- `CAPI/python/PyAPI/main.py`：Python 选手入口
- `CAPI/python/PyAPI/AI.py`：选手应当修改的 AI 模板
- `CAPI/python/PyAPI/Interface.py`：接口定义
- `CAPI/python/PyAPI/structures.py`：客户端可见的数据结构定义
- `CAPI/python/PyAPI/DebugAPI.py`：带日志输出的调试接口

---

## 接口继承关系

```ascii
                IAPI
                  |
      -----------------------------
      |                           |
ICharacterAPI                ITeamAPI
```

- `IAPI`：公共基础接口，提供地图、消息、调试输出、全局信息查询等功能。
- `ICharacterAPI`：单位控制接口，由 `playerID != 0` 的角色进程使用。
- `ITeamAPI`：队伍控制接口，由 `playerID == 0` 的队伍进程使用。

选手只需要实现：

```python
CharacterPlay(self, api: ICharacterAPI) -> None
TeamPlay(self, api: ITeamAPI) -> None
```

---

## 使用方式

### 1. 生成 proto 代码

首次使用前，请先在 `CAPI/python` 目录运行：

```bash
python generate_proto.cmd
```

或：

```bash
python generate_proto.sh
```

该步骤会安装依赖并生成 gRPC 所需的 Python 文件。

### 2. 启动一个 Python 选手进程

在 `CAPI/python` 目录运行：

```bash
python -m PyAPI.main -I 127.0.0.1 -P 8888 -t 1 -p 0
```

参数说明：

- `-I/--serverIP`：服务器地址，默认 `127.0.0.1`
- `-P/--serverPort`：服务器端口，默认 `8888`
- `-t/--teamID`：队伍编号
- `-p/--playerID`：队内单位编号
- `-c/--characterType`：角色进程使用的单位类型编号；通常由队伍进程在 `BuildCharacter` 成功后自动传入，手动启动队伍进程时不需要填写
- `-d/--debug`：将接口日志保存到 `CAPI/python/logs`
- `-o/--output`：将接口日志输出到控制台
- `-w/--warning`：控制台仅显示 warning 及以上日志
- `-s/--side`：注册时的侧边标记，通常不需要手动指定
- `--aiModule`：AI 模块路径，默认 `PyAPI.AI`

### 3. `playerID` 的作用


- `playerID` 只用于区分队伍控制端与单位控制端，以及区分同队的不同单位
- 单位的真实类型由队伍控制端在创建时显式指定
- `playerID` 与单位类型没有固定对应关系；同一个 `playerID` 可以由 `BuildCharacter` 参数创建成任意合法 `CharacterType`
- `playerID == 0` 表示队伍控制进程，负责招募单位、生产货物、升级科技；`playerID > 0` 表示单位控制进程
- 角色进程如果需要判断自身类型，应以 `api.GetSelfInfo().characterType` 为准

创建单位时，由队伍控制端调用：

```python
api.BuildCharacter(characterType, playerID)
```

来决定新单位的类型与编号。Python API 与 C++ API 对齐：当该调用返回成功后，队伍进程会自动启动一个新的 Python 角色进程，并把 `teamID`、`playerID`、`characterType`、服务器地址、日志参数和 `--aiModule` 传给它。选手通常只需要启动 `playerID == 0` 的队伍进程，新增单位的控制进程由接口负责启动。

### 4. 编写 AI 的位置

选手应当修改：

- `CAPI/python/PyAPI/AI.py`

推荐做法：

- 在 `__init__` 中保存阶段变量、路径缓存、目标点等状态
- 在 `TeamPlay` / `CharacterPlay` 中写按帧推进的状态机
- 每帧只执行少量动作，而不是在回调函数里写死循环

### 5. 使用脚本调试 Python AI

在 Windows 上调试 Python AI 时，推荐直接运行仓库根目录的：

- `start_thuai9_python_1team.bat`：对齐 `start_thuai9_cpp_1team.bat`，启动 UI、2 队服务器、1 队活跃队伍进程和 2 队空转队伍进程
- `start_thuai9_python_4team.bat`：对齐 `start_thuai9_cpp_4team.bat`，启动 UI、4 队服务器和 4 个队伍进程


例如，在仓库根目录执行：

```bat
start_thuai9_python_1team.bat
```

该脚本会自动完成以下工作：

- 生成 Python proto 代码
- 启动游戏服务器
- 启动 Avalonia 调试界面
- 启动 `playerID == 0` 的队伍进程
- 当 `BuildCharacter` 成功时，由队伍进程自动启动对应 `playerID > 0` 的角色进程

`start_thuai9_python_1team.bat` 适合调试单队的 `TeamPlay` 逻辑；`start_thuai9_python_4team.bat` 适合调试四队同时运行。二者都不需要手动提前启动角色进程。

如果需要自定义仓库根目录、Python 解释器或 AI 模块，可以先设置环境变量，再运行脚本：

```bat
set PYTHON_EXE=python
set ACTIVE_AI_MODULE=PyAPI.AI
set DUMMY_AI_MODULE=PyAPI.IdleAI
start_thuai9_python_1team.bat
```

常用可选环境变量：

- `PYTHON_EXE`：Python 解释器命令或路径
- `SERVER_PORT`：服务器端口
- `ACTIVE_AI_MODULE`：活跃队伍使用的 AI 模块
- `DUMMY_AI_MODULE`：占位队伍使用的 AI 模块
- `PY_AI_MODULE`：四队脚本的默认 AI 模块
- `TEAM1_AI_MODULE`、`TEAM2_AI_MODULE`、`TEAM3_AI_MODULE`、`TEAM4_AI_MODULE`：四队脚本中各队使用的 AI 模块
- `PY_FLAGS`：传给 Python 客户端的额外参数，默认 `-o -d`

---

## 枚举与数据结构

下面列出选手最常接触的数据结构。

### Character

```python
class Character:
    guid: int
    teamID: int
    playerID: int
    characterType: CharacterType
    characterActiveState: CharacterState
    x: int
    y: int
    facingDirection: float
    speed: int
    viewRange: int
    commonAttack: int
    commonAttackCD: int
    commonAttackRange: int
    hp: int
    carryCapacity: int
    currentLoad: int
    goodsLoad: dict[GoodsType, int]
    harvestRatePerSec: int
```

字段说明：

- `guid`：全局唯一编号
- `teamID`：所属队伍编号
- `playerID`：队内编号
- `characterType`：角色类型，见 `CharacterType`
- `characterActiveState`：角色当前动作状态，见 `CharacterState`
- `x`, `y`：当前位置，单位是地图内部坐标，不是格子坐标
- `speed`：当前速度
- `viewRange`：视野范围
- `commonAttack`：普通攻击伤害
- `commonAttackCD`：普通攻击冷却
- `commonAttackRange`：普通攻击范围
- `hp`：当前生命值
- `carryCapacity`：负载上限
- `currentLoad`：当前总负载
- `harvestRatePerSec`：采集效率
- `goodsLoad`: 当前装载的货物类型

### Team

```python
class Team:
    teamID: int
    playerID: int
    score: int
    material: int
    computePower: int
    factoryHP: int
    techLevels: dict[str, int]
```

字段说明：

- `score`：当前分数
- `material`：当前工厂原料
- `computePower`：当前工厂算力
- `factoryHP`：工厂血量
- `techLevels`：当前科技等级表，键名例如：
  - `Cost`
  - `Efficiency`
  - `Market`
  - `Robust`
  - `Warrior`
  - `Production`
  - `Storage`
  - `MoveSpeed`
  - `Carry`
  - `Price`

### Factory

```python
class Factory:
    factoryID: int
    teamID: int
    x: int
    y: int
    hp: int
    robust: int
    storage: int
    efficiency: int
    source: int
    computingPower: int
    canProduce: bool
    canRecruit: bool
    productInventory: dict[GoodsType, int]
```

字段说明：

- `source`：工厂当前储存的原料
- `computingPower`：工厂当前算力
- `canProduce`：当前是否能生产商品
- `canRecruit`：当前是否能招募单位
- `productInventory`：工厂中每种商品的库存数量

### MarketGoodsInfo

```python
class MarketGoodsInfo:
    price: int
    tradedQuantity: int
```

字段说明：

- `price`：当前市场价格
- `tradedQuantity`：该商品在此市场累计交易量

### Market

```python
class Market:
    marketID: int
    x: int
    y: int
    marketType: MarketType
    priceList: dict[GoodsType, MarketGoodsInfo]
```

### ComputeCenter

```python
class ComputeCenter:
    centerID: int
    x: int
    y: int
    ownerTeamID: int
    occupyProgress: int
    state: ComputeCenterState
```

### Resource

```python
class Resource:
    resourceID: int
    resourceType: ResourceType
    x: int
    y: int
    state: ResourceState
```

### TeamGameInfo

```python
class TeamGameInfo:
    teamID: int
    score: int
    material: int
    computePower: int
    factoryHP: int
    techLevels: dict[str, int]
```

### GameInfo

```python
class GameInfo:
    gameTime: int
    teams: list[TeamGameInfo]
```

字段说明：

- `gameTime`：当前游戏时间
- `teams`：所有队伍的全局信息列表，其中 `teams[0]` 对应 1 队，`teams[1]` 对应 2 队，以此类推

---

## IAPI 基础接口

### 消息通信

#### SendTextMessage

```python
def SendTextMessage(self, toPlayerID: int, message: str) -> Future[bool]
```

- **参数**
  - `toPlayerID`：接收者玩家编号
  - `message`：文本消息内容
- **返回值**
  - `Future[bool]`
  - 使用 `.result()` 获取是否发送成功
- **说明**
  - 适合发送调度命令、阶段通知、简单协同信息

**示例**：

```python
ok = api.SendTextMessage(1, "go_market").result()
if ok:
    api.Print("message sent")
```

#### SendBinaryMessage

```python
def SendBinaryMessage(self, toPlayerID: int, message: bytes) -> Future[bool]
```

- **参数**
  - `toPlayerID`：接收者玩家编号
  - `message`：二进制消息内容
- **返回值**
  - `Future[bool]`
- **说明**
  - 适合自行编码更紧凑的数据

#### HaveMessage

```python
def HaveMessage(self) -> bool
```

- **返回值**
  - `bool`：是否存在未读取消息

#### GetMessage

```python
def GetMessage(self) -> tuple[int, str | bytes]
```

- **返回值**
  - 二元组 `(fromPlayerID, message)`
  - 若当前没有消息，返回 `(-1, "")`
- **说明**
  - 如果发送方调用的是文本接口，则 `message` 是 `str`
  - 如果发送方调用的是二进制接口，则 `message` 是 `bytes`

**示例**：

```python
while api.HaveMessage():
    from_id, msg = api.GetMessage()
    api.Print(f"recv from {from_id}: {msg}")
```

### 帧控制

#### GetFrameCount

```python
def GetFrameCount(self) -> int
```

- **返回值**
  - 当前帧编号
- **说明**
  - 可用于节流动作，例如每隔若干帧做一次判断

#### Wait

```python
def Wait(self) -> bool
```

- **返回值**
  - `bool`：是否成功等待到下一帧
- **说明**
  - 通常在同步模式下无需选手频繁手动调用

#### EndAllAction

```python
def EndAllAction(self) -> Future[bool]
```

- **返回值**
  - `Future[bool]`
- **说明**
  - 停止当前角色正在执行的动作
  - 队伍控制端和单位控制端都可以调用

### 地图与全局信息

#### GetCharacters

```python
def GetCharacters(self) -> list[Character]
```

- **返回值**
  - 己方可见角色列表
- **列表元素**
  - `Character`

#### GetEnemyCharacters

```python
def GetEnemyCharacters(self) -> list[Character]
```

- **返回值**
  - 敌方可见角色列表

#### GetFullMap

```python
def GetFullMap(self) -> list[list[PlaceType]]
```

- **返回值**
  - 二维数组形式的完整地图
- **说明**
  - 可用于预处理资源点、市场、工厂、障碍物的位置

**示例**：

```python
game_map = api.GetFullMap()
for x, row in enumerate(game_map):
    for y, place in enumerate(row):
        if place == THUAI9.PlaceType.Resource:
            api.Print(f"resource at ({x}, {y})")
```

#### GetGameInfo

```python
def GetGameInfo(self) -> GameInfo
```

- **返回值**
  - `GameInfo`
- **包含字段**
  - `gameTime`
  - `teams`
- **说明**
  - 适合查询所有队伍的整体状态和科技信息

**示例**：

```python
info = api.GetGameInfo()
api.Print(f"time = {info.gameTime}")
for team in info.teams:
    api.Print(f"team {team.teamID}: score={team.score}, tech={team.techLevels}")
```

#### GetPlaceType

```python
def GetPlaceType(self, cellX: int, cellY: int) -> PlaceType
```

- **参数**
  - `cellX`：格子横坐标
  - `cellY`：格子纵坐标
- **返回值**
  - `PlaceType`
- **说明**
  - 输入是格子坐标，不是 `x/y` 内部坐标

#### GetResourceState

```python
def GetResourceState(self, cellX: int, cellY: int) -> Resource | None
```

- **参数**
  - `cellX`：格子横坐标
  - `cellY`：格子纵坐标
- **返回值**
  - `Resource`
  - 或 `None`（该格子没有资源点）

#### GetComputeCenterState

```python
def GetComputeCenterState(self, cellX: int, cellY: int) -> ComputeCenter | None
```

- **返回值**
  - `ComputeCenter`
  - 或 `None`

#### GetMarketState

```python
def GetMarketState(self, cellX: int, cellY: int) -> Market | None
```

- **返回值**
  - `Market`
  - 或 `None`

#### GetFactoryState

```python
def GetFactoryState(self, cellX: int, cellY: int) -> Factory | None
```

- **返回值**
  - `Factory`
  - 或 `None`

**示例**：

```python
factory = api.GetFactoryState(3, 3)
if factory is not None:
    api.Print(f"factory source = {factory.source}")
    api.Print(f"inventory = {factory.productInventory}")
```

#### GetPlayerGUIDs

```python
def GetPlayerGUIDs(self) -> list[int]
```

- **返回值**
  - 本队角色的 GUID 列表

#### GetComputingPower

```python
def GetComputingPower(self) -> int
```

- **返回值**
  - 当前队伍工厂算力

#### GetMaterial

```python
def GetMaterial(self) -> int
```

- **返回值**
  - 当前队伍工厂原料

#### GetScore

```python
def GetScore(self) -> int
```

- **返回值**
  - 当前队伍分数

### 调试输出

#### Print

```python
def Print(self, string: str) -> None
```

- **参数**
  - `string`：要输出的调试信息

#### PrintCharacter

```python
def PrintCharacter(self) -> None
```

- **说明**
  - 打印当前可见角色信息

#### PrintSelfInfo

```python
def PrintSelfInfo(self) -> None
```

- **说明**
  - 打印自己的角色信息或队伍信息

---

## ICharacterAPI 接口

### 移动

#### Move

```python
def Move(self, timeInMilliseconds: int, angleInRadian: float) -> Future[bool]
```

- **参数**
  - `timeInMilliseconds`：移动持续时间，单位毫秒
  - `angleInRadian`：移动方向，单位弧度
- **返回值**
  - `Future[bool]`
- **说明**
  - 若不想手写方向角，建议优先使用四个快捷移动接口

**示例**：

```python
import math

api.Move(200, math.pi / 2).result()
```

#### MoveRight

```python
def MoveRight(self, timeInMilliseconds: int) -> Future[bool]
```

- **参数**
  - `timeInMilliseconds`：持续时间
- **返回值**
  - `Future[bool]`

#### MoveUp

```python
def MoveUp(self, timeInMilliseconds: int) -> Future[bool]
```

#### MoveLeft

```python
def MoveLeft(self, timeInMilliseconds: int) -> Future[bool]
```

#### MoveDown

```python
def MoveDown(self, timeInMilliseconds: int) -> Future[bool]
```

### 战斗与回复

#### Common_Attack

```python
def Common_Attack(self, attackedPlayerID: int) -> Future[bool]
```

- **参数**
  - `attackedPlayerID`：目标玩家编号
- **返回值**
  - `Future[bool]`
- **说明**
  - 用于普通攻击
  - 目标编号应来自可见角色信息

#### Recover

```python
def Recover(self, recover: int) -> Future[bool]
```

- **参数**
  - `recover`：尝试恢复的数值
- **返回值**
  - `Future[bool]`

### 采集、占领与交易

#### Harvest

```python
def Harvest(self) -> Future[bool]
```

- **返回值**
  - `Future[bool]`
- **说明**
  - 需要单位站在资源点附近
  - 采集到的资源会转化为工厂原料

**示例**：

```python
me = api.GetSelfInfo()
if me is not None:
    ok = api.Harvest().result()
    api.Print(f"harvest = {ok}")
```

#### Occupy

```python
def Occupy(self) -> Future[bool]
```

- **返回值**
  - `Future[bool]`
- **说明**
  - 用于占领算力中心

#### Load

```python
def Load(self, goodsType: GoodsType, amount: int) -> Future[bool]
```

- **参数**
  - `goodsType`：装载货物类型
  - `amount`：装载数量
- **返回值**
  - `Future[bool]`
- **说明**
  - 从己方工厂把商品装到角色身上
  - 需要站在己方工厂附近
  - 工厂库存不足时会失败

**示例**：

```python
ok = api.Load(THUAI9.GoodsType.Food, 1).result()
if ok:
    api.Print("load success")
```

#### Buy

```python
def Buy(self, goodsType: GoodsType, amount: int) -> Future[bool]
```

- **参数**
  - `goodsType`：商品类型
  - `amount`：购买数量
- **返回值**
  - `Future[bool]`
- **说明**
  - 需要站在市场附近

#### Sell

```python
def Sell(self, goodsType: GoodsType, amount: int) -> Future[bool]
```

- **参数**
  - `goodsType`：商品类型
  - `amount`：出售数量
- **返回值**
  - `Future[bool]`
- **说明**
  - 需要站在市场附近
  - 单位身上没有足够货物时会失败

**示例**：

```python
ok = api.Sell(THUAI9.GoodsType.Semiconductor, 1).result()
api.Print(f"sell = {ok}")
```

### 自身信息与可视判断

#### GetSelfInfo

```python
def GetSelfInfo(self) -> Character | None
```

- **返回值**
  - `Character`
  - 或 `None`

#### HaveView

```python
def HaveView(
    self,
    x: int,
    y: int,
    newX: int,
    newY: int,
    viewRange: int,
    gameMap: list[list[PlaceType]],
) -> bool
```

- **参数**
  - `x`, `y`：观察者当前位置
  - `newX`, `newY`：目标位置
  - `viewRange`：观察者视野范围
  - `gameMap`：完整地图
- **返回值**
  - `bool`：目标是否可见
- **说明**
  - 适合做视野判断、遮挡判断

---

## ITeamAPI 接口

### GetSelfInfo

```python
def GetSelfInfo(self) -> Team | None
```

- **返回值**
  - `Team`
  - 或 `None`

### BuildCharacter

```python
def BuildCharacter(self, characterType: CharacterType, playerID: int) -> Future[bool]
```

- **参数**
  - `characterType`：要创建的单位类型
  - `playerID`：新单位的队内编号
- **返回值**
  - `Future[bool]`
- **说明**
  - 队伍控制端用于招募单位
  - 需要工厂允许招募，且算力足够
  - `playerID` 只是新单位的队内编号，不决定单位类型
  - 调用成功后，Python API 会自动启动对应的角色控制进程；角色进程启动参数中会携带 `-c/--characterType`

**示例**：

```python
ok = api.BuildCharacter(THUAI9.CharacterType.Robot, 1).result()
if ok:
    api.Print("robot created for playerID 1")
```

### ProduceGoods

```python
def ProduceGoods(self, goodsType: GoodsType, maxProduceNum: int) -> Future[bool]
```

- **参数**
  - `goodsType`：要生产的货物类型
  - `maxProduceNum`：最大生产数量
- **返回值**
  - `Future[bool]`
- **说明**
  - 需要工厂有足够原料、容量，并且当前可生产

**示例**：

```python
if api.GetMaterial() >= 3:
    api.ProduceGoods(THUAI9.GoodsType.Food, 1).result()
```

### UplevelTech

```python
def UplevelTech(self, techType: TechType) -> Future[bool]
```

- **参数**
  - `techType`：科技类型
- **返回值**
  - `Future[bool]`
- **说明**
  - 使用工厂算力升级科技
  - 当前客户端可通过 `team_info.techLevels` 查看升级后的等级

**示例**：

```python
team = api.GetSelfInfo()
if team is not None:
    api.Print(f"before = {team.techLevels}")
api.UplevelTech(THUAI9.TechType.IncreaseEfficiency).result()
```

---

## IAI 接口

选手必须实现下面两个函数：

### CharacterPlay

```python
def CharacterPlay(self, api: ICharacterAPI) -> None
```

- **参数**
  - `api`：单位控制接口
- **说明**
  - 每一帧会被调用一次
  - 在这里编写单个单位的移动、采集、装货、卖货、战斗逻辑

### TeamPlay

```python
def TeamPlay(self, api: ITeamAPI) -> None
```

- **参数**
  - `api`：队伍控制接口
- **说明**
  - 每一帧会被调用一次
  - 在这里编写造单位、生产货物、升级科技等队伍级逻辑

---

## IGameTimer 接口

该接口由框架内部使用，参赛者一般不需要直接调用。

```python
StartTimer(self) -> None
EndTimer(self) -> None
Play(self, ai: IAI) -> None
```

---

## 调试接口

`CharacterDebugAPI` 和 `TeamDebugAPI` 在普通 API 基础上增加了日志功能。

日志文件位置：

- `CAPI/python/logs/api-{teamID}-{playerID}-log.txt`
- `CAPI/python/logs/logic-{playerID}-{teamID}-log.txt`

如果启动时带 `-o`，日志也会输出到控制台。

---

## 最小模板

```python
from PyAPI.Interface import IAI, ICharacterAPI, ITeamAPI

class AI(IAI):
    def __init__(self, playerID: int):
        self.playerID = playerID
        self._team_phase = 0
        self._character_phase = 0

    def TeamPlay(self, api: ITeamAPI) -> None:
        team = api.GetSelfInfo()
        if team is None:
            return

        # 这里写造单位、生产、升级科技逻辑

    def CharacterPlay(self, api: ICharacterAPI) -> None:
        me = api.GetSelfInfo()
        if me is None:
            return

        # 这里写移动、采集、装货、售卖、战斗逻辑
```

---

## 建议阅读顺序

初次使用时，推荐按这个顺序阅读：

1. 本文档
2. `CAPI/python/PyAPI/AI.py`
3. `CAPI/python/PyAPI/structures.py`
4. `CAPI/python/PyAPI/main.py`
5. `CAPI/python/PyAPI/DebugAPI.py`

这样通常就足够开始编写一个基础可运行的 Python 程序。
