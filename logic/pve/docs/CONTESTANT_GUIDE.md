# 选手介绍文档

## 比赛目标

你需要编写一个智能体，在随机生成的 PVE 经济环境中尽可能提高最终资金和净资产。

智能体控制一个单位，在地图上移动，与市场交易，并可以采集资源。环境每局都会随机生成地图和市场条件，因此不能只记固定路线或固定交易时刻。

## 环境概览

每局游戏包含：

- 一个 `7x7` 地图。
- 3 个市场。
- 2 个资源点。
- 若干障碍。
- 1 个玩家单位。
- 随机市场价格曲线。
- 市场库存和市场需求。

市场不是无限流动性的：

- 买入会消耗市场库存。
- 卖出会消耗市场需求。
- 库存低时，买价会上升。
- 需求低时，卖价会下降。
- 库存和需求会随时间缓慢恢复。

这意味着简单地在一个市场反复交易不会稳定获利。

## 动作空间

动作是离散整数：

| 动作编号 | 含义 |
| ---: | --- |
| `0` | 等待 |
| `1` | 向上移动 |
| `2` | 向下移动 |
| `3` | 向左移动 |
| `4` | 向右移动 |
| `5` | 买入半导体 |
| `6` | 卖出可卖商品 |
| `7` | 采集资源 |

不是所有动作在任意时刻都有效。环境会在公开 observation 中提供 `valid_actions`。

建议算法优先使用 `valid_actions` 做动作掩码。大量无效动作会浪费步数并降低表现。

## 公开接口

比赛算法应只依赖游戏公开接口，不应读取内部对象。

核心接口由 `game_core.GameEnv` 提供：

```python
env.reset(seed)
env.step(action)
env.get_public_observation()
env.get_valid_actions()
env.get_net_worth()
```

如果使用 Gymnasium 训练，可以直接使用：

```python
from ai_gym_env import AI9GymEnv

env = AI9GymEnv()
obs, info = env.reset(seed=0)
obs, reward, terminated, truncated, info = env.step(action)
```

其中 `info["public_observation"]` 是规则层暴露的完整公开状态。

## 公开 Observation

公开 observation 是一个字典，主要字段包括：

| 字段 | 含义 |
| --- | --- |
| `time` | 当前游戏时间 |
| `money` | 当前现金 |
| `net_worth` | 估算净资产 |
| `map_grid` | 地图网格 |
| `unit` | 单位公开状态 |
| `markets` | 市场公开状态列表 |
| `resources` | 资源点公开状态列表 |
| `valid_actions` | 当前可用动作列表 |
| `last_step` | 上一步动作结果 |

`unit` 包含：

- `pos`
- `busy_ticks`
- `state`
- `inventory`
- `resources`
- `total_load`
- `hp`

每个 `market` 包含：

- `id`
- `pos`
- `price`
- `buy_price`
- `sell_price`
- `period`
- `phase`
- `stock`
- `max_stock`
- `demand`
- `max_demand`
- `origin_inventory`
- `nearby`

每个 `resource` 包含：

- `id`
- `pos`
- `stock`
- `max_stock`
- `nearby`

## Gym 向量 Observation

`AI9GymEnv` 会将公开 observation 编码为固定长度向量，方便 PPO、DQN、A2C 等算法训练。

当前向量长度为 `52`，包含：

- 单位位置和载货。
- 当前资金。
- 时间相位。
- 各市场相对位置、买价、卖价、库存、需求、来源库存、是否可买卖。
- 各资源点相对位置和库存。
- `valid_actions`。

如果你写规则型或混合策略，建议直接读取 `info["public_observation"]`，信息更清晰。

## 奖励和评估

当前 Gym reward 主要由以下部分组成：

- 每步小时间惩罚。
- 无效动作惩罚。
- 已实现交易利润奖励。
- episode 结束时的净资产奖励。

比赛排名最终更适合使用多随机 seed 下的平均表现，例如：

- 平均最终资金。
- 平均净资产。
- 平均奖励。
- 稳定性，即不同 seed 的方差。

## 本地运行

使用 `uv` 运行。

语法检查：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python -m py_compile game_core.py ai_gym_env.py compare_algorithms.py heuristic_baseline.py
```

运行脚本化基线：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python heuristic_baseline.py --episodes 50 --out plots_competition_ready/heuristic_after_market_impact.csv
```

训练并对比基础 RL 算法：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python compare_algorithms.py --timesteps 100000 --episodes 10 --random-episodes 30 --algorithms ppo dqn a2c --eval-episodes 5 --model-dir models/competition_compare_100k --out-dir plots_competition_compare_100k --force
```

## 基线表现

当前版本下的参考结果：

| 策略/算法 | 评估局数 | 平均最终资金 | 平均奖励 | 平均交易次数 |
| --- | ---: | ---: | ---: | ---: |
| 脚本化阈值交易策略 | 50 | 2187.63 | 144.05 | 11.96 |
| DQN 200k | 20 | 924.64 | -9.79 | 0.05 |
| PPO 100k | 10 | 1000.00 | -11.49 | 0 |
| DQN 100k | 10 | 1000.00 | -11.92 | 0 |
| A2C 100k | 10 | 1000.00 | -3.16 | 0 |
| Random | 30 | 903.58 | -15.81 | 2.57 |

这说明：

- 随机策略平均亏损。
- 朴素 RL 不容易直接学会。
- 只使用公开接口的脚本化策略可以盈利。
- 比赛仍有较大优化空间。

## 建议方向

可尝试的方向：

- 使用 `valid_actions` 做动作掩码。
- 对地图做 BFS / A* 路径规划。
- 建模市场库存和需求恢复。
- 预测未来价格，而不是只看当前价格。
- 结合规则策略和强化学习。
- 用课程学习先训练固定地图，再训练随机地图。
- 使用 recurrent policy 或历史特征，识别价格周期。
- 使用搜索或模型预测控制规划买卖路线。

## 禁止依赖的内容

为了保证公平，算法不应依赖模拟器私有内部状态，例如：

- `env.game.units`
- `env.game.markets`
- `_find_nearby_market`
- `_try_buy`
- `_try_sell_all`
- 任何以下划线开头的内部方法

请只使用公开 observation 和公开接口。后续评测机可以只暴露这些接口。
