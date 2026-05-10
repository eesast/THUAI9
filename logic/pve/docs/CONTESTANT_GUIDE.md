# 选手介绍文档

## 比赛目标

你需要编写一个智能体，在 PvE 经济环境中通过买卖商品和采集资源，尽可能积累高分。

**得分 = 所有卖出收入之和 × 10**。比赛终止时（资金归零或时间耗尽），得分越高排名越靠前。

## 环境概览

每局游戏包含：

- 一个可配置地图（easy 5×5，medium 10×10，hard 15×15）。
- 3 个市场（hard 模式 4 个）。
- 2 个资源点（hard 模式 4 个）。
- 2 个算力中心（hard 模式 3 个）。
- 1 座工厂（初始位于 (0,0)，智能体出生点）。
- 1 个可控单位。

### 商品

共 5 类商品，市场价格由正弦函数驱动，不同市场的相位随机不同步：

| ID | 名称 | 购买成本 | 市场价格范围 | 生产时间 |
| ---: | --- | ---: | --- | ---: |
| 0 | 半导体 | 10 | 40–120 | 5.0 s |
| 1 | 药品 | 5 | 20–60 | 4.0 s |
| 2 | 小商品 | 1 | 4–12 | 2.0 s |
| 3 | 服饰 | 8 | 32–96 | 6.0 s |
| 4 | 食品 | 3 | 12–24 | 1.0 s |

价格公式：

```
price(t) = base + amplitude × (1 + sin(2π·t / period + phase)) / 2
```

不同市场的价格相位不同步，因此套利窗口会随时间移动。

## 动作空间

动作是离散整数，共 8 个：

| 动作编号 | 名称 | 含义 |
| ---: | --- | --- |
| `0` | WAIT | 等待一个 tick |
| `1` | MOVE_UP | 向上移动（x−1） |
| `2` | MOVE_DOWN | 向下移动（x+1） |
| `3` | MOVE_LEFT | 向左移动（y−1） |
| `4` | MOVE_RIGHT | 向右移动（y+1） |
| `5` | BUY | 在相邻市场购买当前最便宜的可负担商品 |
| `6` | SELL | 在相邻市场卖出背包内所有商品，获得当前市场价格 |
| `7` | HARVEST | 从附近资源点采集原材料 |

**动作有效性规则**：

- 移动：目标格必须可通行，且单位不处于 busy 状态。
- BUY：当前格 Manhattan 距离 ≤ 1 内有市场；背包有空余容量；现金 ≥ 最低商品成本。
- SELL：当前格 Manhattan 距离 ≤ 1 内有市场；背包内有成品。
- HARVEST：当前格 Manhattan 距离 ≤ 2 内有未耗尽资源点；背包有空余容量。

执行无效动作不会报错，但会受到惩罚并浪费步数。

## 公开接口

比赛算法只能通过标准 Gymnasium 接口与环境交互：

```python
from GameLogic import GameEnvironment, GameConfig

env = GameEnvironment(cfg=GameConfig.easy())
obs, info = env.reset(seed=0)
obs, reward, terminated, truncated, info = env.step(action)
mask = env.action_masks()  # shape (8,) bool 数组
```

`step()` 返回的 `info` 字典：

| 字段 | 类型 | 含义 |
| --- | --- | --- |
| `step` | `int` | 当前步数 |
| `time` | `float` | 游戏时间（秒） |
| `money` | `float` | 当前现金 |
| `score` | `float` | 当前累计得分 |
| `compute` | `float` | 当前算力点 |
| `action_valid` | `bool` | 上一步动作是否有效 |

## 观测向量

观测是长度为 **32** 的 `float32` 数组：

| 索引 | 含义 | 归一化方式 |
| ---: | --- | --- |
| 0–1 | 单位位置 (x, y) | / (地图高, 地图宽) |
| 2 | 单位 HP | / max_hp |
| 3 | 原材料背包占比 | raw_inv / capacity |
| 4 | 成品背包占比 | prod_inv / capacity |
| 5 | busy 倒计时 | busy_ticks / 10，截断到 1 |
| 6 | 现金（对数） | log10(money+1) / 5 |
| 7 | 算力点 | compute / 100，最大 2 |
| 8 | 游戏进度 | time / max_game_time |
| 9 | 价格相位正弦 | sin(2π·t / market_period) |
| 10 | 价格相位余弦 | cos(2π·t / market_period) |
| 11 | 工厂原材料库存 | / factory_storage_cap |
| 12 | 工厂成品库存 | / factory_storage_cap |
| 13 | 工厂生产队列长度 | / 10，截断到 1 |
| 14–16 | 资源点 0 | 相对位置 (dx/H, dy/W) + 库存比例 |
| 17–19 | 资源点 1 | 同上 |
| 20–22 | 算力中心 0 | 相对位置 (dx/H, dy/W) + 是否开放 |
| 23–25 | 算力中心 1 | 同上 |
| 26–28 | 市场 0 | 相对位置 (dx/H, dy/W) + 当前最高卖价（归一化） |
| 29–31 | 市场 1 | 同上 |

如果实体数量不足（如配置只有 2 个市场），多余索引保持 0。

如果你编写规则型策略，建议直接读取 `info` 字典中的字段，而不是尝试解析原始向量。

## 动作掩码

`env.action_masks()` 返回 `(8,)` 布尔数组，`True` 表示该动作当前有效。

使用 `sb3-contrib` 中的 `MaskablePPO` 可自动利用动作掩码，避免探索无效动作：

```python
from sb3_contrib import MaskablePPO
from sb3_contrib.common.wrappers import ActionMasker

def mask_fn(env):
    return env.unwrapped.action_masks()

masked_env = ActionMasker(env, mask_fn)
model = MaskablePPO("MlpPolicy", masked_env)
```

建议所有策略先查询 `action_masks()` 再决定动作，可显著减少无效步数。

## 奖励与评估

每步 Gym 奖励由以下部分叠加：

| 来源 | 默认值 |
| --- | --- |
| 现金变化 `Δmoney × 0.01` | 正负均有 |
| 得分变化 `Δscore × 0.01` | 仅卖出时为正 |
| 时间惩罚（每步） | `−0.002` |
| 采集奖励（每单位原材料） | `+0.001` |
| 算力中心解锁（一次性） | `+0.5` |
| 无效动作惩罚 | `−0.05` |
| 破产惩罚（现金 < 0 时） | `−10.0` |

奖励是训练辅助信号，最终**排名依据是多 seed 下的平均总得分**（`info["score"]`），不是累计奖励。

注意：
- 得分 = 每次卖出收入 × 10，只有 SELL 动作成功时才增加。
- `terminated=True`：现金 < 0（破产）；`truncated=True`：步数耗尽（正常结束）。

## 本地运行

安装依赖：

```bash
pip install -r requirements.txt
```

运行单元测试：

```bash
python -m pytest tests/ -v
```

基础训练（easy 难度，10 万步）：

```bash
python TrainingDemo/train_basic.py --config easy --timesteps 100000
```

评测已保存模型：

```bash
python TrainingDemo/evaluate.py --model models/ppo_thuai9_best --config easy --episodes 50
```

使用 YAML 自定义配置：

```bash
python TrainingDemo/train_basic.py --config TrainingDemo/configs/medium.yaml --timesteps 200000
```

## 建议方向

可尝试的方向：

- 使用 `action_masks()` 做动作掩码，过滤无效动作。
- 对地图做 BFS / A\* 路径规划，寻找最优路线。
- 建模市场价格正弦周期（利用观测向量中的 sin/cos 相位），预测高价时机再卖出。
- 选择高利润商品：半导体（40–120）和服饰（32–96）利润空间大，但购买成本也较高。
- 结合规则策略与强化学习（Hybrid Policy）。
- 用课程学习先在 easy 地图上训练，再迁移到 medium / hard。
- 使用 Recurrent Policy 识别价格周期相位。
- 使用 MaskablePPO（`sb3-contrib`）自动处理动作掩码。

## 禁止依赖的内容

算法只能通过 Gymnasium 标准接口（`reset`、`step`、`action_masks`）访问游戏状态，不得读取内部对象：

- `env.unit`（Unit 内部字段）
- `env.factory`（Factory 内部字段）
- `env.board`（Board / 地图内部字段）
- `env.markets`（Market 列表）
- `env.money`、`env.compute`、`env.score`（请通过 `info` 字典获取）
- 任何以下划线开头的方法或属性

后续评测机只会暴露标准 Gymnasium 接口，请确保算法不依赖私有状态。
