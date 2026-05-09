# THUAI9 Python API 接口文档

## 1. 这套接口是做什么的

`CAPI/python/PyAPI` 是给参赛者写 Python 选手 AI 的接口层。选手只需要关心下面两件事：

- `TeamPlay(self, api: ITeamAPI)`：队伍级策略，负责造单位、生产货物、升级科技。
- `CharacterPlay(self, api: ICharacterAPI)`：单位级策略，负责移动、采集、搬运、售卖、战斗。

`IAPI` 是这两类接口的公共基础，提供地图、角色、队伍、消息和调试输出等能力。

---

## 2. 如何运行

先在 `CAPI/python` 目录生成 proto 代码：

```bash
python generate_proto.cmd
```

再启动一个选手进程：

```bash
python -m PyAPI.main -I 127.0.0.1 -P 8888 -t 1 -p 0
```

参数说明：

- `-I/--serverIP`：服务器地址，默认 `127.0.0.1`
- `-P/--serverPort`：服务器端口，默认 `8888`
- `-t/--teamID`：队伍编号，通常为 `1~4`
- `-p/--playerID`：控制身份
  - `0` 表示队伍控制端，调用 `TeamPlay`
  - `1~3` 表示单位控制端，调用 `CharacterPlay`
- 默认 `main.py` 会把 `1/2/3` 映射为 `Robot/Drone/AutonomousCar`，但真正造什么单位仍由 `BuildCharacter()` 决定
- `-d/--debug`：把调试日志写入 `CAPI/python/logs`
- `-o/--output`：把调试日志输出到控制台
- `-w/--warning`：控制台只显示 warning 及以上日志
- `-s/--side`：注册时的侧边标记，默认由队伍编号推导
- `--aiModule`：自定义 AI 模块名，默认 `PyAPI.AI`

建议直接参考 `CAPI/python/PyAPI/AI.py` 里的模板写策略。

---

## 3. 编写选手代码的方式

选手只需要修改 `CAPI/python/PyAPI/AI.py`。

推荐写法：

- 在 `__init__` 里保存阶段变量，例如 `self._team_phase`、`self._character_phase`
- 在 `TeamPlay` 和 `CharacterPlay` 里写“状态机”
- 每次只下发少量动作，不要在函数里写死循环
- 动作执行后用阶段变量记录进度，下一帧继续

不要这样写：

- 在 `CharacterPlay` / `TeamPlay` 里写长时间 `while True`
- 在一帧里连续狂发很多动作
- 依赖固定地图坐标，不看 `GetFullMap()`

---

## 4. 接口层次

```ascii
                IAPI
                  |
      -----------------------------
      |                           |
ICharacterAPI                ITeamAPI
```

- `ICharacterAPI`：控制一个单位
- `ITeamAPI`：控制本队工厂和队伍策略
- `IGameTimer`：内部计时器接口，普通选手一般不用直接处理

---

## 5. 常用数据结构

### Character

单位信息。常用字段：

- `teamID`、`playerID`
- `characterType`
- `characterActiveState`
- `x`、`y`
- `speed`
- `viewRange`
- `hp`
- `carryCapacity`
- `currentLoad`
- `harvestRatePerSec`

### Team

队伍信息。常用字段：

- `teamID`
- `score`
- `material`
- `computePower`
- `factoryHP`
- `techLevels`

### Factory

工厂信息。常用字段：

- `teamID`
- `x`、`y`
- `hp`
- `robust`
- `storage`
- `efficiency`
- `source`
- `computingPower`
- `canProduce`
- `canRecruit`
- `productInventory`

### Market

市场信息。常用字段：

- `marketType`
- `priceList`

### ComputeCenter

算力中心信息。常用字段：

- `ownerTeamID`
- `occupyProgress`
- `state`

### Resource

资源点信息。常用字段：

- `resourceType`
- `state`

### GameInfo

全局比赛信息：

- `gameTime`
- `teams`

其中 `teams[i]` 对应第 `i+1` 队。

### 科技信息

当前对外可见的科技 key 为：

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

`techLevels` 就是这些 key 的当前等级字典。

---

## 6. IAPI 常用方法

### 消息

- `SendTextMessage(toPlayerID, message)`：发送文本消息
- `SendBinaryMessage(toPlayerID, message)`：发送二进制消息
- `HaveMessage()`：是否有待取消息
- `GetMessage()`：读取一条消息，返回 `(fromPlayerID, message)`
- `GetFrameCount()`：当前帧号
- `Wait()`：等待下一帧更新，通常不需要手动频繁调用
- `EndAllAction()`：立即停止当前所有动作

### 信息获取

- `GetCharacters()`：己方可见角色
- `GetEnemyCharacters()`：敌方可见角色
- `GetFullMap()`：完整地图格子
- `GetGameInfo()`：全局比赛信息
- `GetPlaceType(cellX, cellY)`：某格子的地形类型
- `GetResourceState(cellX, cellY)`：资源点状态
- `GetComputeCenterState(cellX, cellY)`：算力中心状态
- `GetMarketState(cellX, cellY)`：市场状态
- `GetFactoryState(cellX, cellY)`：工厂状态
- `GetPlayerGUIDs()`：本队角色 GUID 列表
- `GetComputingPower()`：工厂当前算力
- `GetMaterial()`：工厂当前原料
- `GetScore()`：当前分数

### 调试输出

- `Print(string)`：输出日志
- `PrintCharacter()`：打印当前可见角色
- `PrintSelfInfo()`：打印自己信息

注意：调试输出通常需要启动时带 `-d` 或 `-o` 才看得到。

---

## 7. ICharacterAPI 常用方法

### 移动

- `Move(timeInMilliseconds, angleInRadian)`
- `MoveRight(timeInMilliseconds)`
- `MoveUp(timeInMilliseconds)`
- `MoveLeft(timeInMilliseconds)`
- `MoveDown(timeInMilliseconds)`

建议优先使用 `MoveRight/Up/Left/Down`，更不容易写错方向。

### 战斗与回复

- `Common_Attack(attackedPlayerID)`：普通攻击
- `Recover(recover)`：回复血量

### 资源与交易

- `Harvest()`：采集资源
- `Load(goodsType, amount)`：从己方工厂装货到自己身上
- `Buy(goodsType, amount)`：在市场买货
- `Sell(goodsType, amount)`：在市场卖货

使用前提：

- `Harvest()` 需要站在资源点附近
- `Load()` 需要站在己方工厂附近，且工厂里确实有货
- `Buy()` / `Sell()` 需要站在市场附近

### 其它

- `Occupy()`：占领算力中心
- `EndAllAction()`：立刻停止当前所有动作
- `GetSelfInfo()`：获取自己的单位信息
- `HaveView(...)`：判断是否可视，适合做视野或路径判断

---

## 8. ITeamAPI 常用方法

- `GetSelfInfo()`：获取本队信息
- `BuildCharacter(characterType, playerID)`：造单位
- `ProduceGoods(goodsType, maxProduceNum)`：生产货物
- `UplevelTech(techType)`：升级科技

使用说明：

- `BuildCharacter()` 里的 `playerID` 是新单位的队内编号
- `ProduceGoods()` 和 `UplevelTech()` 只能由队伍控制端调用
- 选手一般会在 `playerID = 0` 的进程里写这些逻辑

---

## 9. 开发建议

- 用 `GetFullMap()` 先找资源、工厂、市场、算力中心的位置
- 用状态变量记录“当前阶段”
- 每帧只做一步，比如“寻路一步”“采集一次”“装货一次”
- `Future[bool]` 类型的动作，通常用 `.result()` 读取执行结果
- 如果你要写搬运策略，建议自己记录当前目标货物种类

---

## 10. 最小模板

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
        # TODO: 造人、生产、升级科技

    def CharacterPlay(self, api: ICharacterAPI) -> None:
        me = api.GetSelfInfo()
        if me is None:
            return
        # TODO: 移动、采集、装货、售卖、战斗
```

如果想看完整启动方式和调试日志位置，请参考 [CAPI/python/PyAPI/main.py](../CAPI/python/PyAPI/main.py) 和 [CAPI/python/PyAPI/DebugAPI.py](../CAPI/python/PyAPI/DebugAPI.py)。
