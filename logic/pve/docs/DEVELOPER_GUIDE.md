# 开发者文档

本文档面向维护游戏规则、评测接口和训练脚本的开发者。

## 目录结构

```text
logic/pve/
├── GameLogic/               # 游戏规则层（算法不可直接依赖内部实现）
│   ├── config.py            # 全局配置（GameConfig、PRODUCT_DEFS、TECH_TREE）
│   ├── board.py             # 地图、ResourcePoint、ComputeCenter
│   ├── character.py         # 单位（HP、背包、状态机）
│   ├── market.py            # 市场动态价格函数
│   ├── action_space.py      # Action 枚举、动作掩码
│   ├── reward_calculator.py # RewardConfig / RewardCalculator
│   └── game_env.py          # GameEnvironment（Gymnasium 接口）、Factory
├── RLInterfaces/            # RL 算法接口层
│   ├── base_agent.py        # 抽象基类（强制接口隔离）
│   ├── ppo_agent.py         # PPO 实现（支持 MaskablePPO）
│   └── training_loop.py     # 手动训练循环（含突破回调）
├── TrainingDemo/            # 训练与评测脚本
│   ├── train_basic.py       # 基础训练入口
│   ├── evaluate.py          # 多 seed 评测
│   ├── visualization.py     # ASCII 渲染 + 奖励曲线
│   └── configs/             # easy / medium / hard YAML 配置
├── tests/                   # 单元测试
├── docs/
│   ├── CONTESTANT_GUIDE.md  # 选手介绍文档
│   └── DEVELOPER_GUIDE.md   # 本文档
├── requirements.txt
└── README.md
```

生成物不应提交：

- `.venv/`
- `__pycache__/`
- `models/`
- `plots/`

这些已写入 `.gitignore`。

## 分层原则

项目分为两层：

1. **游戏规则层**：`GameLogic/` 包内所有模块。
2. **算法层**：`RLInterfaces/`、`TrainingDemo/`、选手提交的 agent。

开发时必须保持这个边界：

- 游戏规则只在 `GameLogic/` 中定义。
- 算法代码只能通过 Gymnasium 标准接口（`reset`、`step`、`action_masks`）读取游戏状态。
- 不要让算法脚本读取 `Unit`、`Market`、`Factory` 等内部对象字段。

这保证了未来评测机只暴露公开接口，防止选手使用私有状态。

## 游戏规则层

### GameEnvironment（`GameLogic/game_env.py`）

主环境，继承 `gymnasium.Env`。

公开接口：

```python
env.reset(seed: int | None, options: dict | None) -> (obs, info)
env.step(action: int)  -> (obs, reward, terminated, truncated, info)
env.action_masks()     -> np.ndarray[bool, (8,)]
env.render()           -> str   # ANSI 单行状态摘要
```

`step()` 返回的 `info` 字典：

| 字段             | 类型      | 含义                                     |
| ---------------- | --------- | ---------------------------------------- |
| `step`         | `int`   | 当前步数                                 |
| `time`         | `float` | 游戏时间（秒）                           |
| `money`        | `float` | 当前现金                                 |
| `score`        | `float` | 当前累计得分（卖出收入 × score_factor） |
| `compute`      | `float` | 当前算力点                               |
| `action_valid` | `bool`  | 上一步是否有效                           |

终止条件：

- `terminated = True`：`money < 0`（破产）
- `truncated = True`：`step >= cfg.max_steps`（时间耗尽）

得分计算：每次 SELL 成功后 `score += revenue × cfg.score_factor`（默认 × 10）。

### 内部对象

以下对象可在规则层内部自由使用，但不应作为选手算法依赖的接口：

| 对象              | 来源文件         | 职责                                    |
| ----------------- | ---------------- | --------------------------------------- |
| `Unit`          | `character.py` | 单位背包、HP、busy 状态机               |
| `Market`        | `market.py`    | 正弦价格函数，per-product per-market    |
| `ResourcePoint` | `board.py`     | 资源库存与再生速率                      |
| `ComputeCenter` | `board.py`     | 算力中心占领进度                        |
| `Board`         | `board.py`     | 地图网格、实体查询（nearest_market 等） |
| `Factory`       | `game_env.py`  | 仓库（raw_stock + products）、生产队列  |

### 商品定义（`GameLogic/config.py`）

`PRODUCT_DEFS` 中定义 5 类商品：

| ID | 名称   | 购买成本 | 市场价格范围 | 生产时间 |
| -: | ------ | -------: | ------------ | -------: |
|  0 | 半导体 |       10 | 40–120      |    5.0 s |
|  1 | 药品   |        5 | 20–60       |    4.0 s |
|  2 | 小商品 |        1 | 4–12        |    2.0 s |
|  3 | 服饰   |        8 | 32–96       |    6.0 s |
|  4 | 食品   |        3 | 12–24       |    1.0 s |

### 市场价格（`GameLogic/market.py`）

每个市场对每种商品维护独立的正弦参数：

```
price(t) = base + amplitude × (1 + sin(2π·t / period + phase)) / 2
```

- `base`：`val_range[0]`
- `amplitude`：`(val_range[1] - val_range[0]) × price_volatility`
- `phase`：每市场随机偏移，防止各市场同步
- `period`：`market_period × random(0.7, 1.5)`

BUY 动作执行时购买"当前成本最低的可负担商品"（见 `_cheapest_buyable`），不是利润最高的商品。

如果修改价格范围或新增商品，需同步更新 `game_env.py` 顶部的归一化常量：

```python
_PRICE_MIN, _PRICE_MAX = 4.0, 120.0
```

### 动作空间（`GameLogic/action_space.py`）

动作由 `Action` 枚举定义：

```python
class Action(IntEnum):
    WAIT       = 0
    MOVE_UP    = 1
    MOVE_DOWN  = 2
    MOVE_LEFT  = 3
    MOVE_RIGHT = 4
    BUY        = 5
    SELL       = 6
    HARVEST    = 7
```

动作掩码由 `compute_action_mask(env)` 生成，关键规则：

- BUY / SELL：需 Manhattan 距离 ≤ 1 的市场
- HARVEST：需 Manhattan 距离 ≤ 2 的未耗尽资源点
- 移动 / BUY / SELL / HARVEST：unit.busy_ticks 必须为 0

## 观测向量契约

当前维度 `OBS_DIM = 32`，由 `_encode_obs()` 生成：

|   索引 | 含义            | 归一化                           |
| -----: | --------------- | -------------------------------- |
|   0–1 | 单位位置 (x, y) | / (H, W)                         |
|      2 | 单位 HP         | / max_hp                         |
|      3 | 原材料背包      | raw_inv / capacity               |
|      4 | 成品背包        | prod_inv / capacity              |
|      5 | busy_ticks      | / 10，截断到 1                   |
|      6 | money           | log10(money+1) / 5               |
|      7 | compute         | / 100，截断到 2                  |
|      8 | time            | / max_game_time                  |
|  9–10 | 价格相位        | sin / cos（用于识别周期）        |
|     11 | 工厂原材料库存  | / storage_cap                    |
|     12 | 工厂成品库存    | / storage_cap                    |
|     13 | 生产队列长度    | / 10，截断到 1                   |
| 14–16 | 资源点 0        | dx/H, dy/W, stock ratio          |
| 17–19 | 资源点 1        | 同上                             |
| 20–22 | 算力中心 0      | dx/H, dy/W, is_open              |
| 23–25 | 算力中心 1      | 同上                             |
| 26–28 | 市场 0          | dx/H, dy/W, best_price（归一化） |
| 29–31 | 市场 1          | 同上                             |

当前只编码前 2 个市场和前 2 个资源点。如果修改观测结构，必须同步更新：

- `GameEnvironment.OBS_DIM`
- `observation_space` 定义
- `docs/CONTESTANT_GUIDE.md`

## 奖励计算（`GameLogic/reward_calculator.py`）

奖励由 `RewardCalculator` 按 `RewardConfig` 权重计算：

| 来源                   | 默认参数                   | 默认值              |
| ---------------------- | -------------------------- | ------------------- |
| 现金变化 `Δmoney`   | `money_scale = 0.01`     | `Δmoney × 0.01` |
| 得分变化 `Δscore`   | `money_scale = 0.01`     | `Δscore × 0.01` |
| 时间惩罚（每步）       | `time_penalty`           | `−0.002`         |
| 采集奖励（每单位）     | `harvest_bonus_per_unit` | `+0.001`          |
| 算力中心解锁（一次性） | `compute_center_bonus`   | `+0.5`            |
| 无效动作               | `invalid_action_penalty` | `−0.05`          |
| 破产（终止时）         | `bankruptcy_penalty`     | `−10.0`          |

奖励是训练辅助信号，比赛排名以 `score` 为准。调整 `RewardConfig` 权重时不影响 `score` 的计算。

## 难度配置（`GameLogic/config.py`）

所有规则参数集中在 `GameConfig` dataclass：

| 参数                       |  easy | medium（默认） |    hard |
| -------------------------- | ----: | -------------: | ------: |
| `map_width / map_height` | 5 / 5 |        10 / 10 | 15 / 15 |
| `num_markets`            |     3 |              3 |       4 |
| `num_resource_points`    |     2 |              2 |       4 |
| `num_compute_centers`    |     1 |              2 |       3 |
| `initial_money`          |   200 |             50 |      30 |
| `initial_compute`        |    60 |             30 |      20 |
| `price_volatility`       |   0.3 |            1.0 |     2.0 |
| `resource_regen_rate`    |   2.0 |            1.0 |     0.5 |
| `initial_resource_stock` |   200 |            100 |      50 |
| `max_game_time (s)`      |   300 |            300 |     500 |

通过 YAML 自定义参数：

```yaml
# TrainingDemo/configs/custom.yaml
map_width: 8
map_height: 8
num_markets: 4
initial_money: 100.0
price_volatility: 1.5
```

加载方式：

```python
GameConfig.from_dict(yaml.safe_load(open("custom.yaml")))
```

## RL 接口层（`RLInterfaces/`）

### BaseAgent（`base_agent.py`）

所有算法必须继承此类，只能通过 `BaseAgent.step()` 包装器与环境交互。子类实现 `get_action` 和 `train` 两个抽象方法。

### PPOAgent（`ppo_agent.py`）

封装 SB3 的 MaskablePPO（若 `sb3-contrib` 可用）或标准 PPO。自动检测并启用动作掩码。训练完成后在 `models/` 目录保存最佳权重。

### TrainingLoop（`training_loop.py`）

手动 episode 训练循环，支持突破回调（可用于触发难度升级）。SB3 封装的算法建议直接调用 `agent.train()`，而非 TrainingLoop。

## 训练脚本（`TrainingDemo/`）

### `train_basic.py`

基础训练入口，支持内置难度预设或 YAML 路径：

```bash
python TrainingDemo/train_basic.py --config easy --timesteps 100000 --seed 42
python TrainingDemo/train_basic.py --config TrainingDemo/configs/hard.yaml --timesteps 500000
```

### `evaluate.py`

多 seed 评测，输出平均得分和方差：

```bash
python TrainingDemo/evaluate.py --model models/ppo_thuai9_best --config medium --episodes 50
```

### `visualization.py`

ASCII 渲染轨迹和奖励曲线，用于调试策略行为。

## 推荐开发流程

1. 修改 `GameLogic/` 中的规则或 `RewardConfig`。
2. 运行单元测试：

   ```bash
   python -m pytest tests/ -v
   ```
3. 用 easy 配置快速验证环境可解：

   ```bash
   python TrainingDemo/train_basic.py --config easy --timesteps 50000
   ```
4. 确认规则没有变成无解（简单规则策略仍能盈利）。
5. 确认规则没有被 vanilla PPO 轻松解决。
6. 更新本文档和 `docs/CONTESTANT_GUIDE.md`。

## 调整规则时的判断标准

一个适合比赛的规则版本应满足：

- 随机策略平均表现较差。
- vanilla PPO 在较短训练内无法轻松达到高分。
- 合理的规则策略或规划策略可以稳定盈利。
- 不依赖固定地图路线（支持 random_map 或多 seed）。
- 不依赖私有状态即可实现合理策略。
- 多 seed 下的方差小于单 seed 表现差异。
