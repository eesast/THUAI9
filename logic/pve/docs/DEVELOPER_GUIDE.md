# 开发者文档

本文档面向维护游戏规则、评测接口和基线算法的开发者。

## 目录结构

当前核心文件：

```text
.
├── game_core.py              # 游戏规则层
├── ai_gym_env.py             # Gymnasium 适配层
├── compare_algorithms.py     # PPO / DQN / A2C / Random 对比训练
├── heuristic_baseline.py     # 公开接口脚本化基线
├── visualize_policy.py       # 单模型轨迹可视化
├── train.py                  # 简单 PPO 训练脚本
├── advanced_train.py         # 多模式 PPO 训练脚本
├── config/
│   └── setting.py            # 游戏规则常量
├── docs/
│   ├── CONTESTANT_GUIDE.md   # 选手介绍文档
│   └── DEVELOPER_GUIDE.md    # 本文档
├── ITERATION_LOG.md          # 迭代记录
├── README.md
├── pyproject.toml
└── uv.lock
```

生成物不应提交：

- `.venv/`
- `__pycache__/`
- `models/`
- `plots/`
- `plots_*/`
- `*.zip`

这些已写入 `.gitignore`。

## 分层原则

项目分为两层：

1. 游戏规则层：`game_core.py`
2. 强化学习/算法层：`ai_gym_env.py`、训练脚本、可视化脚本

开发时必须保持这个边界：

- 游戏规则只在 `game_core.py` 和 `config/setting.py` 中定义。
- RL 代码只能通过公开接口读取游戏状态。
- 不要让算法脚本读取 `Unit`、`Market`、`ResourcePoint` 等内部对象。

这可以让未来评测机只暴露公开接口，从而避免选手使用私有状态作弊。

## 游戏规则层

`game_core.GameEnv` 是核心模拟器。

主要公开接口：

```python
reset(seed: Optional[int]) -> dict
step(action: int) -> dict
get_public_observation() -> dict
get_valid_actions() -> list[bool]
get_net_worth() -> float
```

内部对象：

- `Point`
- `Market`
- `ResourcePoint`
- `Unit`
- `Factory`

这些对象可以在规则层内部自由使用，但不应作为选手算法依赖的接口。

## 公开 Observation 契约

`get_public_observation()` 返回字典。

顶层字段：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `time` | `float` | 当前时间 |
| `money` | `float` | 当前现金 |
| `net_worth` | `float` | 估算净资产 |
| `map_width` | `int` | 地图宽度 |
| `map_height` | `int` | 地图高度 |
| `map_grid` | `list[list[int]]` | 地图网格 |
| `unit` | `dict` | 单位状态 |
| `markets` | `list[dict]` | 市场状态 |
| `resources` | `list[dict]` | 资源点状态 |
| `valid_actions` | `list[bool]` | 动作是否合法 |
| `transactions` | `int` | 已完成交易次数 |
| `last_step` | `dict` | 上一步结果 |

`last_step` 字段：

| 字段 | 含义 |
| --- | --- |
| `valid` | 上一步动作是否合法 |
| `action` | 上一步动作编号 |
| `money_delta` | 上一步现金变化 |
| `realized_profit` | 上一步实现利润 |
| `net_worth` | 上一步后的净资产 |
| `transactions` | 当前交易次数 |

如果修改公开 observation，必须同步更新：

- `ai_gym_env.py`
- `docs/CONTESTANT_GUIDE.md`
- `docs/DEVELOPER_GUIDE.md`
- 必要时更新 `ITERATION_LOG.md`

## Gymnasium 适配层

`AI9GymEnv` 位于 `ai_gym_env.py`。

职责：

- 将公开 observation 编码为固定长度向量。
- 定义 `action_space` 和 `observation_space`。
- 将游戏规则层的 step result 转换为 RL reward。
- 在 `info["public_observation"]` 中透传公开状态。

当前向量 observation 长度：

```python
OBS_VECTOR_SIZE = 52
```

如果增加市场数量、资源数量或 observation 特征，需要检查该值。

当前 reward 结构：

- 每步时间惩罚：`-0.002`
- 无效动作惩罚：`-0.01`
- 已实现交易利润奖励：`0.1 * realized_profit`
- episode 结束净资产奖励：`0.01 * (net_worth - INITIAL_MONEY)`

reward 是训练辅助信号，不一定等同最终比赛排名指标。评测更建议使用多 seed 下的最终资金或净资产。

## 规则参数

主要配置在 `config/setting.py`。

地图和随机场景：

- `MAP_WIDTH`
- `MAP_HEIGHT`
- `MARKET_COUNT`
- `RESOURCE_COUNT`
- `OBSTACLE_COUNT`

市场价格：

- `MARKET_SPREAD_RATE`
- `MARKET_PERIOD_MIN`
- `MARKET_PERIOD_MAX`
- `MARKET_PRICE_SCALE_MIN`
- `MARKET_PRICE_SCALE_MAX`

市场库存和需求：

- `MARKET_INITIAL_STOCK_MIN`
- `MARKET_INITIAL_STOCK_MAX`
- `MARKET_MAX_STOCK`
- `MARKET_STOCK_REPLENISH_PER_STEP`
- `MARKET_INITIAL_DEMAND_MIN`
- `MARKET_INITIAL_DEMAND_MAX`
- `MARKET_MAX_DEMAND`
- `MARKET_DEMAND_REPLENISH_PER_STEP`
- `MARKET_SCARCITY_PRICE_IMPACT`
- `MARKET_LOW_DEMAND_DISCOUNT`

动作定义：

- `U_ACT_WAIT`
- `U_ACT_MOVE_UP`
- `U_ACT_MOVE_DOWN`
- `U_ACT_MOVE_LEFT`
- `U_ACT_MOVE_RIGHT`
- `U_ACT_LOAD_0`
- `U_ACT_SELL_ALL`
- `U_ACT_HARVEST`

## 基线脚本

### `compare_algorithms.py`

用于训练和比较基础 RL 算法。

支持：

- PPO
- DQN
- A2C
- Random baseline

示例：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python compare_algorithms.py --timesteps 100000 --episodes 10 --random-episodes 30 --algorithms ppo dqn a2c --eval-episodes 5 --model-dir models/competition_compare_100k --out-dir plots_competition_compare_100k --force
```

输出：

- `algorithm_summary.csv`
- `evaluation_traces.csv`
- `algorithm_money_reward_curves.png`
- `algorithm_action_distribution.png`
- `algorithm_final_bars.png`

### `heuristic_baseline.py`

使用公开接口实现的脚本化交易策略。

该脚本用于验证环境不是无解，而不是官方最强 baseline。

示例：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python heuristic_baseline.py --episodes 50 --out plots_competition_ready/heuristic_after_market_impact.csv
```

### `visualize_policy.py`

用于对已训练的 SB3 模型生成轨迹图。

注意：该脚本默认加载 PPO 模型。如果要可视化 DQN/A2C，需要扩展模型加载逻辑。

## 推荐开发流程

1. 修改规则或算法。
2. 运行语法检查。
3. 运行 `check_env`。
4. 跑脚本化基线，确认规则没有变成无解。
5. 跑基础 RL baseline，确认规则没有被 vanilla 算法轻松解决。
6. 更新文档和迭代记录。

语法检查：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python -m py_compile game_core.py ai_gym_env.py compare_algorithms.py heuristic_baseline.py visualize_policy.py train.py advanced_train.py config/setting.py
```

Gym 检查：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python -c "from stable_baselines3.common.env_checker import check_env; from ai_gym_env import AI9GymEnv; check_env(AI9GymEnv()); print('ok')"
```

## 调整规则时的判断标准

一个适合比赛的规则版本应满足：

- 随机策略平均表现较差。
- vanilla PPO / DQN / A2C 不会在较小预算下轻松解决。
- 一个合理的脚本策略或规划策略可以稳定盈利。
- 不依赖固定地图路线。
- 不依赖私有状态。
- 多 seed 表现比单 seed 表现更重要。

当前停止点满足这些条件：

| 策略/算法 | 平均最终资金 | 说明 |
| --- | ---: | --- |
| Random | 903.58 | 平均亏损 |
| PPO 100k | 1000.00 | 基本不交易 |
| DQN 100k | 1000.00 | 基本不交易 |
| A2C 100k | 1000.00 | 基本不交易 |
| DQN 200k | 924.64 | 略好于随机但未解决 |
| Heuristic baseline | 2187.63 | 可解性验证 |

## 后续可扩展方向

规则方向：

- 引入更多商品。
- 增加生产链和工厂订单。
- 加入市场之间的商品偏好。
- 增加突发事件，如市场停摆或价格冲击。
- 增加燃料或移动成本。
- 引入多单位调度。

算法方向：

- Action mask PPO / DQN。
- 路径规划加交易决策。
- 模型预测控制。
- Recurrent policy。
- 离线规划加在线微调。
- 多阶段 curriculum。

## 注意事项

- 不要把训练产物提交到仓库。
- 不要让算法依赖 `game_core` 内部私有方法。
- 修改规则后必须重新跑 heuristic baseline。
- 修改 observation 后必须同步更新 `OBS_VECTOR_SIZE`。
- 比赛评测应使用固定的一组隐藏 seeds，而不是训练时使用的 seeds。
