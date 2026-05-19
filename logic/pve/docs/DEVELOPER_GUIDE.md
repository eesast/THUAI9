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
env.action_masks()     -> np.ndarray[bool, (28,)]
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

得分计算：每次 SELL_pid 成功后 `score += revenue × cfg.score_factor`（默认 × 10）。

### 内部对象

以下对象可在规则层内部自由使用，但不应作为选手算法依赖的接口：

| 对象              | 来源文件         | 职责                                    |
| ----------------- | ---------------- | --------------------------------------- |
| `Unit`          | `character.py` | 单位背包（raw_inv + prod_inv[5]）、HP、busy 状态机 |
| `Market`        | `market.py`    | OU 随机游走价格，per-product per-market |
| `ResourcePoint` | `board.py`     | 资源库存与再生速率                      |
| `ComputeCenter` | `board.py`     | 算力中心占领进度（occupy_progress）     |
| `Board`         | `board.py`     | 地图网格、实体查询（nearest_market 等） |
| `Factory`       | `game_env.py`  | 仓库（raw_stock + products[5]）、生产队列、科技乘数 |

### 商品定义（`GameLogic/config.py`）

`PRODUCT_DEFS` 中定义 5 类商品，每条包含 `raw_cost`（生产时消耗的原材料数量）：

| ID | 名称   | 购买成本 | 原材料消耗 | 市场价格范围 | 生产时间 |
| -: | ------ | -------: | ---------: | ------------ | -------: |
|  0 | 半导体 |       10 |          5 | 40–120      |    5.0 s |
|  1 | 药品   |        5 |          3 | 20–60       |    4.0 s |
|  2 | 小商品 |        1 |          1 | 4–12        |    2.0 s |
|  3 | 服饰   |        8 |          4 | 32–96       |    6.0 s |
|  4 | 食品   |        3 |          2 | 12–24       |    1.0 s |

### 科技树（`GameLogic/config.py`）

`TECH_TREE` 中定义 8 项科技，由 TECH_x 动作在工厂处消耗算力购买：

| 键名               | 算力消耗 | 前置 | 持久 | 效果 |
| ------------------ | -------: | ---- | ---- | ---- |
| `cost_reduction`  |       50 | —   | ✓   | 市场购买商品时每件少付 2 金钱（factory.cost_delta -= 2） |
| `efficiency`      |       40 | —   | ✓   | factory.time_multiplier *= 0.5 |
| `marketing`       |       80 | —   | ✓   | factory.price_multiplier *= 1.1 |
| `durability`      |       30 | —   | ✓   | unit.capacity *= 1.5（携带上限 +50%） |
| `multi_line`      |       60 | —   | ✓   | factory.production_lines += 1 |
| `path_optimization` |     50 | efficiency | ✓ | 移动 busy_ticks 变为 0 |
| `market_analysis` |       40 | —   | ✗   | 非持久，可重复购买；obs 中市场 2-3 的价格信息开放 |
| `compute_expansion` |     70 | —   | ✓   | 算力积累速率 +30% |

持久科技（persistent=True）每种只能购买一次；`market_analysis` 为非持久，可多次购买。

### 生产链（`game_env.py`）

完整生产链由以下动作依次执行：

```
HARVEST → DEPOSIT → PRODUCE_pid → (工厂 tick 自动推进) → LOAD → SELL_pid
```

关键实现细节：
- `factory.tick()` 在每个 step 的被动更新阶段自动调用，与单位动作无关。
- `enqueue(pid)` 检查并扣除 `raw_stock`（`PRODUCT_DEFS[pid]["raw_cost"]`），然后按 `time_multiplier` 计算剩余 tick 数。
- `load_products(unit)` 将 `factory.products` 中的成品按先入先出顺序装入单位背包，受单位剩余容量限制。
- 工厂总库存（`raw_stock + total_product_stock`）不超过 `factory_storage_cap`。

### 算力系统（`game_env.py`）

算力由 `_accrue_compute(dt)` 在每个 step 积累：

```python
# 每个已开放的算力中心每 tick 贡献算力
rate = cfg.base_compute_rate + (0.3 if "compute_expansion" in _techs_owned else 0.0)
compute += rate * dt
```

消耗：TECH_x 动作在工厂处执行时扣除 `TECH_TREE[key]["cost"]` 算力。

科技效果由 `_apply_tech(key, tdef)` 立即写入 Factory / Unit 字段，无延迟。

### 市场价格（`GameLogic/market.py`）

每个市场对每种商品维护独立的 **Ornstein-Uhlenbeck 随机游走**价格：

```
dP = θ(μ − P)·dt + σ·√dt · N(0,1)
```

- `θ = 0.05` 均值回归速度（相关时间 ≈ 20s）
- `σ = amplitude × 0.12` 波动率（`_SIGMA_SCALE`）
- `μ = lo + amplitude × 0.5` 长期均值
- 价格被夹在 `[lo, lo + amplitude]` 之间
- 每个市场独立初始化随机起点
- `Market.tick(dt)` 在环境 step 的被动阶段被调用

BUY 动作执行时购买"跨市场套利空间最大"的可负担商品（见 `_best_buyable`），即在其他市场的卖价 − 当前市场买价差值最大的商品。

**套利防护**：单位追踪每件商品的购买来源市场（`prod_origin[pid][market_id]`）。卖出时阻止在同一市场卖出从该市场购买的商品——必须移动到其他市场才能套利。

卖价乘以 `factory.price_multiplier`（默认 1.0，购买 marketing 科技后变为 1.1）。

价格归一化使用每种商品各自的 val_range（`_PRICE_NORM` 字典）。

### 动作空间（`GameLogic/action_space.py`）

`N_ACTIONS = 28`，动作由 `Action` 枚举定义：

```python
class Action(IntEnum):
    WAIT=0, MOVE_UP=1, MOVE_DOWN=2, MOVE_LEFT=3, MOVE_RIGHT=4,
    BUY=5,
    SELL_0=6, SELL_1=7, SELL_2=8, SELL_3=9, SELL_4=10,
    HARVEST=11, DEPOSIT=12,
    PRODUCE_0=13, PRODUCE_1=14, PRODUCE_2=15, PRODUCE_3=16, PRODUCE_4=17,
    LOAD=18, OCCUPY=19,
    TECH_0=20, TECH_1=21, TECH_2=22, TECH_3=23,
    TECH_4=24, TECH_5=25, TECH_6=26, TECH_7=27
```

便利列表：
- `SELL_ACTIONS`：`[SELL_0, ..., SELL_4]`（index = pid）
- `PRODUCE_ACTIONS`：`[PRODUCE_0, ..., PRODUCE_4]`（index = pid）
- `TECH_ACTIONS`：`[TECH_0, ..., TECH_7]`（index = tech slot）
- `TECH_KEYS`：`["cost_reduction", "efficiency", ...]`（index = tech slot，与 TECH_ACTIONS 对齐）

动作掩码由 `compute_action_mask(env)` 生成，关键规则：

| 动作 | 有效条件 |
| --- | --- |
| 移动 | 目标格可通行，busy_ticks = 0 |
| BUY | Manhattan ≤ 1 有市场；容量 ≥ 1；money ≥ 最低成本 |
| SELL_pid | Manhattan ≤ 1 有市场；prod_inv[pid] > 0 |
| HARVEST | Manhattan ≤ 2 有未耗尽资源点；容量 ≥ 1 |
| DEPOSIT | 在工厂格；raw_inv > 0 |
| PRODUCE_pid | 在工厂格；raw_stock ≥ raw_cost；队列未满 |
| LOAD | 在工厂格；工厂有成品；容量 ≥ 1 |
| OCCUPY | 相邻有算力中心且 not is_open |
| TECH_x | 在工厂格；compute ≥ cost；持久科技未重购；前置已满足 |

**移动 busy_tick**：默认每次移动设置 `unit.busy_ticks = 1`，购买 `path_optimization` 后跳过（即时移动）。

## 观测向量契约

当前维度 `OBS_DIM = 82`，由 `_encode_obs()` 生成：

| 索引 | 含义 | 归一化 |
| ---: | --- | --- |
| 0–1 | 单位位置 (x, y) | / (H, W) |
| 2 | 单位 HP | / max_hp |
| 3 | 原材料背包 | raw_inv / capacity |
| 4–8 | 成品背包（pid 0–4） | prod_inv[pid] / capacity |
| 9 | busy_ticks | / 10，截断到 1 |
| 10 | money | log10(money+1) / 5 |
| 11 | compute | / 100，截断到 2 |
| 12 | time | / max_game_time |
| 13–14 | 价格相位 | sin / cos（用于识别周期） |
| 15 | 工厂原材料库存 | / storage_cap |
| 16–20 | 工厂成品库存（pid 0–4） | products[pid] / storage_cap |
| 21 | 生产队列长度 | / 10，截断到 1 |
| 22–33 | 资源点 0–3（最多 4 个） | 每个：dx/H, dy/W, stock_ratio；未使用槽为 0 |
| 34–45 | 算力中心 0–2（最多 3 个） | 每个：dx/H, dy/W, is_open, progress/occ_time；未使用槽为 0 |
| 46–73 | 市场 0–3（最多 4 个） | 每个：dx/H, dy/W, 5 种价格；市场 2-3 价格仅在购买 `market_analysis` 后可见 |
| 74–81 | 科技 one-hot（8 个科技槽） | 0 或 1 |

如果修改观测结构，必须同步更新：

- `GameEnvironment.OBS_DIM`
- `observation_space` 定义
- `docs/CONTESTANT_GUIDE.md`

## 奖励计算（`GameLogic/reward_calculator.py`）

奖励由 `RewardCalculator` 按 `RewardConfig` 权重计算（默认 `mode="standard"`）：

| 来源                   | 默认参数                   | 默认值              |
| ---------------------- | -------------------------- | ------------------- |
| 时间惩罚（每步）       | `time_penalty`           | `−0.001`         |
| 采集奖励（每单位）     | `harvest_bonus_per_unit` | `+0.0`（默认关闭） |
| 无效动作               | `invalid_action_penalty` | `−0.02`          |
| 破产（终止时）         | `bankruptcy_penalty`     | `−10.0`          |
| 现金变化 `Δmoney`   | `money_scale = 0.02`     | `Δmoney × 0.02`（仅 standard） |
| 卖出得分 `Δscore`   | `sell_bonus_scale = 0.001` | `Δscore × 0.001`（仅 standard） |
| 算力中心解锁（每个）   | `compute_center_bonus`   | `+1.0`（仅 standard） |
| 科技购买（每个）       | `tech_bonus`             | `+1.0`（仅 standard） |

`RewardConfig` 支持 `"standard"` 和 `"adversarial"` 两种模式。奖励是训练辅助信号，比赛排名以 `score` 为准。调整 `RewardConfig` 权重时不影响 `score` 的计算。

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

## 扩展动作空间注意事项

增加/删除动作时，需同步修改：

1. `action_space.py`：`Action` 枚举、`N_ACTIONS`、便利列表、`compute_action_mask()`
2. `game_env.py`：`_execute_action()` 的 if/elif 分支、`action_space = spaces.Discrete(N_ACTIONS)`
3. `tests/test_game_logic.py`：所有引用具体动作编号的测试
4. 两份文档（动作表、掩码描述）

## 调整规则时的判断标准

一个适合比赛的规则版本应满足：

- 随机策略平均表现较差。
- vanilla PPO 在较短训练内无法轻松达到高分。
- 合理的规则策略或规划策略可以稳定盈利。
- 不依赖固定地图路线（支持 random_map 或多 seed）。
- 不依赖私有状态即可实现合理策略。
- 多 seed 下的方差小于单 seed 表现差异。
