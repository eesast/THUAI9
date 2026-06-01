# THUAI9 C++ API 文档

## 决赛数值调整明细

以下数值为本届决赛特化调整，与初赛版本不同：

| 角色 | 属性 | 初赛值 | 决赛值 |
|:----:|:----:|:------:|:------:|
| 无人机 (DRONE) | HP | 100 | **80** |
| 机器人 (ROBOT) | 攻击力 | 30 | **35** |
| 机器人 (ROBOT) | 负载 | 5 | **10** |
| 自动驾驶汽车 (AUTONOMOUS_CAR) | 攻击力 | 18 | **20** |
| 自动驾驶汽车 (AUTONOMOUS_CAR) | 负载 | 5 | **20** |

**回血消耗调整**：由 1 点算力回复 1 点血量 → **1 点算力回复 2 点血量**（向上取整）。

---

## 1. 接入方式

选手代码需要实现 `IAI`，并在两个入口中分别编写逻辑：

```cpp
void play(ICharacterAPI& api) override;
void play(ITeamAPI& api) override;
```

`ICharacterAPI` 适用于角色玩家，`ITeamAPI` 适用于队伍玩家。

## 2. 通用规则

- 所有行动类接口都返回 `std::future<bool>`，建议立刻 `.get()` 获取结果。
- `true` 表示服务器接受并执行，`false` 表示失败。
- `GetSelfInfo()` 返回当前对象信息，角色模式下是 `THUAI9::Character`，队伍模式下是 `THUAI9::Team`。
- `GetFrameCount()` 返回当前帧数，结束后可能为 `-1`。
- 坐标分两种：
  - `cellX/cellY` 是格子坐标。
  - `x/y` 是网格坐标，`1` 格 = `1000` 网格单位。
- 辅助转换函数：

```cpp
IAPI::CellToGrid(cell); // 格子中心网格坐标
IAPI::GridToCell(grid); // 网格坐标转格子坐标
```

## 3. `ICharacterAPI`

### 信息获取

- `GetSelfInfo()`：当前角色信息
- `GetCharacters()`：本队所有角色
- `GetEnemyCharacters()`：敌方角色
- `GetFullMap()`：全图地形
- `GetGameInfo()`：游戏时间、各队分数/材料/算力
- `GetPlaceType(x, y)`：指定格子地形
- `GetResourceState(x, y)` / `GetComputeCenterState(x, y)` / `GetMarketState(x, y)` / `GetFactoryState(x, y)`：查询格子上的对象
- `GetPlayerGUIDs()`：本队角色 GUID 列表
- `GetComputingPower()` / `GetMaterial()` / `GetScore()`：当前队伍资源

### 通信

- `SendTextMessage(toPlayerID, msg)`
- `SendBinaryMessage(toPlayerID, msg)`
- `HaveMessage()`
- `GetMessage()`

`GetMessage()` 在无消息时返回 `(-1, "")`。

### 行动

- `Move(timeMs, angle)`：按角度移动
- `MoveRight/MoveUp/MoveLeft/MoveDown(timeMs)`：四向移动
- `Common_Attack(attackedPlayerID)`：普通攻击
- `Recover(recover)`：回复生命
- `Harvest()`：采集
- `Occupy()`：占领
- `Load(goodsType, amount)`：装载
- `Buy(goodsType, amount)`：购买
- `Sell(goodsType, amount)`：出售
- `EndAllAction()`：立刻结束当前动作

### 角度约定

- `0`：向下
- `pi / 2`：向右
- `pi`：向上
- `3 * pi / 2`：向左

### `HaveView`

判断目标点是否可见。常用来做视野和遮挡判断。

- 距离超出 `viewRange` 返回 `false`
- `Barrier` 会挡视野
- `Bush` 有特殊规则，目标在草丛中时，通常要求路径也在草丛中

## 4. `ITeamAPI`

除通用接口外，队伍玩家还可以使用：

- `GetSelfInfo()`：当前队伍信息
- `BuildCharacter(CharacterType, playerID)`：建造角色
- `ProduceGoods(goodsType, maxProduceNum)`：生产货物
- `UplevelTech(techType)`：升级科技

## 5. 常用数据结构

- `THUAI9::CharacterType`：`Drone`、`Robot`、`AutonomousCar`
- `THUAI9::GoodsType`：`Semiconductor`、`Medicine`、`Toys`、`Clothes`、`Food`
- `THUAI9::TechType`：各类科技升级
- `THUAI9::PlaceType`：`Factory`、`Space`、`Barrier`、`Bush`、`Resource`、`ComputeCenter`、`Market`
- `THUAI9::Character`：角色位置、血量、视野、攻击、携带量等
- `THUAI9::Team`：队伍分数、材料、算力等
- `THUAI9::GameInfo`：全局时间与队伍信息

## 6. 最小示例

```cpp
void play(ICharacterAPI& api) override
{
    auto self = api.GetSelfInfo();
    if (!self) return;

    api.MoveDown(200).get();
    api.Common_Attack(0).get();
}

void play(ITeamAPI& api) override
{
    auto team = api.GetSelfInfo();
    if (!team) return;

    api.BuildCharacter(THUAI9::CharacterType::Robot, 1).get();
    api.UplevelTech(THUAI9::TechType::IncreaseMoveSpeed).get();
}
```
