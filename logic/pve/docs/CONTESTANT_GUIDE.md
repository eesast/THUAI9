# 选手介绍文档

## 文件修改权限

以下规则强制约束你可以修改和不可修改的文件。评测机只会加载你的模型文件和你自建目录中的代码。

### 不可修改（只读）

以下文件/目录**只能读取，禁止修改**。任何修改将被评测系统忽略或判为违规：

| 路径 | 原因 |
|---|---|
| `GameLogic/**` (全部) | 游戏规则引擎，修改即作弊 |
| `RLInterfaces/base_agent.py` | Agent 接口契约，评测机依赖此基类 |
| `RLInterfaces/__init__.py` | 包导出声明 |
| `tests/test_game_logic.py` | 环境正确性验证 |
| `TrainingDemo/configs/*.yaml` | 官方难度配置 |

### 可参考但禁止直接使用

以下文件仅供学习参考，**提交时禁止直接使用其中的类或函数**。你必须自己实现 `BaseAgent` 的子类：

| 路径 | 用途 | 禁止行为 |
|---|---|---|
| `RLInterfaces/ppo_agent.py` | PPO 实现思路参考 | 禁止 `from RLInterfaces import PPOAgent` |
| `RLInterfaces/training_loop.py` | 训练循环逻辑参考 | 禁止直接使用 `TrainingLoop` |
| `my_agent/dqn_agent.py` | DQN 实现思路参考 | 禁止直接使用 `DQNAgent` |
| `TrainingDemo/train_basic.py` | 训练脚本结构参考 | 禁止照搬 |
| `TrainingDemo/evaluate.py` | 评测脚本结构参考 | 禁止照搬 |
| `TrainingDemo/visualization.py` | 可视化工具 | 可用于调试 |
| `tests/test_interfaces.py` | 接口测试参考 | 禁止照搬 |

### 可自由修改（你的工作区）

你需要在包根目录下自建目录（如 `my_agent/`）来放置你的代码：

```
logic/pve/
├── my_agent/          ← 你的代码放这里
│   ├── __init__.py
│   ├── my_model.py    ← 自定义模型
│   ├── train.py       ← 训练脚本
│   └── evaluate.py    ← 评测脚本
```

import 规则：你只能 import 以下公开符号，**禁止 import `GameLogic` 的子模块**（如 `from GameLogic.board import Board`），**禁止使用官方提供的任何 Agent 类**（如 `PPOAgent`、`DQNAgent`）：

```python
from GameLogic import GameConfig, GameEnvironment, N_ACTIONS  # ✅ 允许
from RLInterfaces import BaseAgent                             # ✅ 允许

from GameLogic.board import Board                              # ❌ 禁止
from GameLogic.market import Market                            # ❌ 禁止
from RLInterfaces import PPOAgent                              # ❌ 禁止（必须自己写）
from my_agent import DQNAgent                                  # ❌ 禁止（必须自己写）
```

---

## 比赛目标

你需要编写一个智能体，在 PvE 经济环境中通过买卖商品、采集原材料并生产高价值商品，尽可能积累高分。

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

| ID | 名称 | 购买成本 | 原材料消耗 | 市场价格范围 | 生产时间 |
| ---: | --- | ---: | ---: | --- | ---: |
| 0 | 半导体 | 10 | 5 | 40–120 | 5.0 s |
| 1 | 药品 | 5 | 3 | 20–60 | 4.0 s |
| 2 | 小商品 | 1 | 1 | 4–12 | 2.0 s |
| 3 | 服饰 | 8 | 4 | 32–96 | 6.0 s |
| 4 | 食品 | 3 | 2 | 12–24 | 1.0 s |

价格公式：

```
price(t) = base + amplitude × (1 + sin(2π·t / period + phase)) / 2
```

不同市场的价格相位不同步，因此套利窗口会随时间移动。

## 完整玩法流程

游戏支持两种盈利路线，可结合使用：

### 路线一：低买高卖（市场套利）

```
移动到市场 → BUY（购买利润最高商品）→ 移动到高价市场 → SELL_pid（卖出特定商品）
```

### 路线二：生产链（原材料 → 成品）

```
HARVEST（采集原材料）→ 移动回工厂 → DEPOSIT（存入工厂）→ PRODUCE_pid（开始生产）
→ 等待工厂生产完成 → LOAD（装载成品）→ 移动到市场 → SELL_pid（卖出）
```

### 路线三：算力科技加速

```
OCCUPY（占领算力中心，每次调用推进进度）→ 算力中心开放后每 tick 积累算力
→ 移动回工厂 → TECH_x（购买科技升级，消耗算力）
```

## 动作空间

动作是离散整数，共 **28** 个（`N_ACTIONS = 28`）：

### 基础动作（0–5）

| 动作编号 | 名称 | 含义 |
| ---: | --- | --- |
| `0` | WAIT | 等待一个 tick |
| `1` | MOVE_UP | 向上移动（x−1），消耗 1 busy_tick |
| `2` | MOVE_DOWN | 向下移动（x+1），消耗 1 busy_tick |
| `3` | MOVE_LEFT | 向左移动（y−1），消耗 1 busy_tick |
| `4` | MOVE_RIGHT | 向右移动（y+1），消耗 1 busy_tick |
| `5` | BUY | 在相邻市场购买当前利润最高（市价−成本）的可负担商品 |

### 卖出动作（6–10，按商品类型分）

| 动作编号 | 名称 | 含义 |
| ---: | --- | --- |
| `6` | SELL_0 | 在相邻市场卖出背包内所有**半导体**（商品0） |
| `7` | SELL_1 | 在相邻市场卖出背包内所有**药品**（商品1） |
| `8` | SELL_2 | 在相邻市场卖出背包内所有**小商品**（商品2） |
| `9` | SELL_3 | 在相邻市场卖出背包内所有**服饰**（商品3） |
| `10` | SELL_4 | 在相邻市场卖出背包内所有**食品**（商品4） |

### 生产链动作（11–18）

| 动作编号 | 名称 | 含义 |
| ---: | --- | --- |
| `11` | HARVEST | 从附近资源点采集原材料，存入背包 |
| `12` | DEPOSIT | 将背包中的原材料存入工厂（需在工厂格） |
| `13` | PRODUCE_0 | 工厂开始生产半导体（消耗 5 原材料） |
| `14` | PRODUCE_1 | 工厂开始生产药品（消耗 3 原材料） |
| `15` | PRODUCE_2 | 工厂开始生产小商品（消耗 1 原材料） |
| `16` | PRODUCE_3 | 工厂开始生产服饰（消耗 4 原材料） |
| `17` | PRODUCE_4 | 工厂开始生产食品（消耗 2 原材料） |
| `18` | LOAD | 从工厂仓库装载已完成的成品到背包 |

### 算力动作（19–27）

| 动作编号 | 名称 | 含义 |
| ---: | --- | --- |
| `19` | OCCUPY | 推进相邻算力中心的占领进度（需持续执行直到开放） |
| `20` | TECH_0 | 购买**降低成本**科技（50 算力）：买商品每件便宜 2 |
| `21` | TECH_1 | 购买**效率提升**科技（40 算力）：生产时间 ×0.5 |
| `22` | TECH_2 | 购买**市场营销**科技（80 算力）：所有商品卖价 ×1.1 |
| `23` | TECH_3 | 购买**耐久强化**科技（30 算力）：单位最大 HP +50% |
| `24` | TECH_4 | 购买**多产线**科技（60 算力）：工厂增加 1 条生产线 |
| `25` | TECH_5 | 购买**路径优化**科技（50 算力，需先买效率提升）：移动变为即时（0 busy_tick） |
| `26` | TECH_6 | 购买**市场分析**科技（40 算力，可重复购买）：obs[50–57] 中标记已拥有 |
| `27` | TECH_7 | 购买**算力扩张**科技（70 算力）：算力积累速率 +30% |

> 科技动作必须在工厂格执行，且持久科技（TECH_0~5, TECH_7）每种只能购买一次。

**动作有效性规则**：

- **移动**：目标格可通行，且 busy_ticks = 0。
- **BUY**：相邻（Manhattan ≤ 1）有市场；背包有空余；现金 ≥ 最低商品成本。
- **SELL_pid**：相邻有市场；背包中该商品数量 > 0。
- **HARVEST**：相邻（≤ 2）有未耗尽资源点；背包有空余容量。
- **DEPOSIT**：在工厂格；背包 raw_inv > 0。
- **PRODUCE_pid**：在工厂格；工厂 raw_stock ≥ 该商品原材料消耗；生产队列未满。
- **LOAD**：在工厂格；工厂有已生产成品；背包有空余容量。
- **OCCUPY**：相邻有算力中心且尚未开放。
- **TECH_x**：在工厂格；算力 ≥ 科技成本；持久科技未重复购买；前置科技已满足。

执行无效动作不会报错，但会受到 −0.05 的奖励惩罚并浪费步数。

## 自定义模型开发（必须自己写）

**你必须自己从零实现模型**。官方提供了 `BaseAgent` 抽象基类作为唯一接口，以及 `my_agent/dqn_agent.py` 和 `RLInterfaces/ppo_agent.py` 作为**参考示例**，但你提交时不能直接使用这些示例中的类。

### 唯一接口：BaseAgent

你只能使用 `BaseAgent` 作为基类，然后自己实现全部逻辑：

```python
from RLInterfaces import BaseAgent
from GameLogic import N_ACTIONS
import numpy as np

class MyAgent(BaseAgent):
    def get_action(self, observation: np.ndarray) -> int:
        """
        observation → action id (0–27).
        不能访问 self.env.unit / self.env.board 等内部对象。
        """
        # ⚠️ 必须显式调用 action_masks()！
        # MaskablePPO（sb3-contrib）会���动应用掩码，但自定义 Agent 没有自动机制。
        mask = self.env.action_masks()       # (28,) bool 数组，True=有效
        valid = np.where(mask)[0]            # 当前允许的动作编号
        if len(valid) == 0:
            return 0                         # fallback: WAIT

        # 探索时从有效动作中随机选
        if np.random.random() < self.epsilon:
            return int(np.random.choice(valid))

        # 利用时屏蔽无效动作的 Q 值
        q_values = self._compute_q(observation)   # 你的推理逻辑
        q_values[~mask] = -1e9                    # 无效动作设为极小值
        return int(np.argmax(q_values))

    def train(self, total_timesteps: int, **kwargs) -> dict:
        obs = self.reset()
        for _ in range(total_timesteps):
            action = self.get_action(obs)
            obs, reward, terminated, truncated, info = self.step(action)
            if terminated or truncated:
                obs = self.reset()
            # 在这里更新你的网络……
        return {"total_timesteps": total_timesteps}

    def save(self, path: str):
        ...

    @classmethod
    def load(cls, path: str, env) -> "MyAgent":
        ...
```

### MaskablePPO 与自定义 Agent 的区别（重要）

| | MaskablePPO（SB3 方案） | 自定义 Agent（你的实现） |
|---|---|---|
| 谁调用 `action_masks()` | `ActionMasker` 包装器自动调用 | **你必须在 `get_action()` 里显式调用** |
| 掩码如何生效 | SB3 内部在 softmax 前将无效动作 logit 设为 `-inf` | 你自己设置 `q_values[~mask] = -1e9` |
| 探索时是否过滤 | 自动：策略采样只会从有效动作中选 | 你需要手动：`np.random.choice(valid)` |

一句话总结：**MaskablePPO 是自动档，自定义 Agent 是手动档——不手动调 `action_masks()` 就不会有任何掩码效果。**

### 实现要求

你自己实现的 Agent 类必须满足以下要求：

1. **继承 `BaseAgent`** — 这是评测机加载的唯一入口。
2. **实现 `get_action()`** — 给定 58 维 observation 向量，返回一个 `int` 动作编号（0–27）。
3. **实现 `train()`** — 完整的训练循环，内部只能通过 `self.reset()` / `self.step()` 与环境交互。
4. **实现 `save(path)` 和 `load(cls, path, env)`** — 保存/加载模型权重。评测机会调用 `YourAgent.load(path, env)` 加载你的模型。
5. **不能访问 `self.env` 的内部属性** — `self.env.unit`、`self.env.board`、`self.env.money` 等都是禁止的。状态信息只能从 `observation` 和 `info` 字典中获得。
6. **必须用 `self.reset()` / `self.step()`** — 不要直接调用 `self.env.reset()` 或 `self.env.step()`，用 `BaseAgent` 提供的包装方法。
7. **必须在 `get_action()` 中显式调用 `self.env.action_masks()`** — MaskablePPO 会自动应用掩码，但自定义 Agent 不会。不调用则没有任何无效动作过滤，训练效率极低。

### 参考示例（学习用，禁止直接使用）

`my_agent/` 目录和 `RLInterfaces/ppo_agent.py` 分别展示了手写 DQN 和 SB3 PPO 两种实现思路：

```
my_agent/                     # 手写 DQN 参考
├── dqn_agent.py              # Q-Network + ReplayBuffer + 训练循环
├── train.py                  # 训练入口
└── evaluate.py               # 多 seed 评测入口

RLInterfaces/ppo_agent.py     # SB3 PPO 参考
```

你可以参考这些代码的结构和思路，然后**用你自己的方式重新实现**——例如：
- 改网络结构（CNN、RNN、Transformer、更大的 MLP）
- 换算法（DQN → PPO、A3C、SAC、Rainbow）
- 换框架（PyTorch → JAX、TensorFlow）
- 加入规则策略（如 A* 路径规划 + RL 买卖决策）
- 设计自定义奖励塑形

只要能跑通 `YourAgent.load(path, env)` 并正常工作即可。

### 训练你的模型

```bash
# 建立你的工作目录
mkdir my_submission
# 在里面写 my_model.py（继承 BaseAgent）、train.py、evaluate.py

# 训练
python my_submission/train.py --config easy --timesteps 200000

# 评测
python my_submission/evaluate.py --model models/my_model.pt --config medium --episodes 100 --seeds 0 42 123 999 7777
```

评测输出 `score_mean` 和 `score_std`——最终排名依据**多 seed 下的平均得分**。

---

## 公开接口

比赛算法只能通过标准 Gymnasium 接口与环境交互：

```python
from GameLogic import GameEnvironment, GameConfig

env = GameEnvironment(cfg=GameConfig.easy())
obs, info = env.reset(seed=0)
obs, reward, terminated, truncated, info = env.step(action)
mask = env.action_masks()  # shape (28,) bool 数组
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

观测是长度为 **58** 的 `float32` 数组：

### 单位状态（[0–9]）

| 索引 | 含义 | 归一化方式 |
| ---: | --- | --- |
| 0–1 | 单位位置 (x, y) | / (地图高, 地图宽) |
| 2 | 单位 HP | / max_hp |
| 3 | 原材料背包 | raw_inv / capacity |
| 4 | 半导体背包数量 | prod_inv[0] / capacity |
| 5 | 药品背包数量 | prod_inv[1] / capacity |
| 6 | 小商品背包数量 | prod_inv[2] / capacity |
| 7 | 服饰背包数量 | prod_inv[3] / capacity |
| 8 | 食品背包数量 | prod_inv[4] / capacity |
| 9 | busy 倒计时 | busy_ticks / 10，截断到 1 |

### 经济状态（[10–14]）

| 索引 | 含义 | 归一化方式 |
| ---: | --- | --- |
| 10 | 现金（对数） | log10(money+1) / 5 |
| 11 | 算力点 | compute / 100，最大 2 |
| 12 | 游戏进度 | time / max_game_time |
| 13 | 价格相位正弦 | sin(2π·t / market_period) |
| 14 | 价格相位余弦 | cos(2π·t / market_period) |

### 工厂状态（[15–21]）

| 索引 | 含义 | 归一化方式 |
| ---: | --- | --- |
| 15 | 工厂原材料库存 | / factory_storage_cap |
| 16 | 工厂半导体库存 | / factory_storage_cap |
| 17 | 工厂药品库存 | / factory_storage_cap |
| 18 | 工厂小商品库存 | / factory_storage_cap |
| 19 | 工厂服饰库存 | / factory_storage_cap |
| 20 | 工厂食品库存 | / factory_storage_cap |
| 21 | 工厂生产队列长度 | / 10，截断到 1 |

### 资源点（[22–27]）

| 索引 | 含义 | 归一化方式 |
| ---: | --- | --- |
| 22–23 | 资源点 0 相对位置 (dx, dy) | / (H, W) |
| 24 | 资源点 0 库存比例 | stock / max_stock |
| 25–26 | 资源点 1 相对位置 (dx, dy) | / (H, W) |
| 27 | 资源点 1 库存比例 | stock / max_stock |

### 算力中心（[28–35]）

| 索引 | 含义 | 归一化方式 |
| ---: | --- | --- |
| 28–29 | 算力中心 0 相对位置 (dx, dy) | / (H, W) |
| 30 | 算力中心 0 是否开放 | 0 或 1 |
| 31 | 算力中心 0 占领进度 | / unit_occupy_time |
| 32–33 | 算力中心 1 相对位置 (dx, dy) | / (H, W) |
| 34 | 算力中心 1 是否开放 | 0 或 1 |
| 35 | 算力中心 1 占领进度 | / unit_occupy_time |

### 市场（[36–49]）

| 索引 | 含义 | 归一化方式 |
| ---: | --- | --- |
| 36–37 | 市场 0 相对位置 (dx, dy) | / (H, W) |
| 38 | 市场 0 当前半导体价格 | 按各商品 val_range 归一化 |
| 39 | 市场 0 当前药品价格 | 同上 |
| 40 | 市场 0 当前小商品价格 | 同上 |
| 41 | 市场 0 当前服饰价格 | 同上 |
| 42 | 市场 0 当前食品价格 | 同上 |
| 43–44 | 市场 1 相对位置 (dx, dy) | / (H, W) |
| 45–49 | 市场 1 五种商品当前价格 | 同上 |

### 科技状态（[50–57]）

| 索引 | 含义 |
| ---: | --- |
| 50 | 是否已购买 cost_reduction |
| 51 | 是否已购买 efficiency |
| 52 | 是否已购买 marketing |
| 53 | 是否已购买 durability |
| 54 | 是否已购买 multi_line |
| 55 | 是否已购买 path_optimization |
| 56 | 是否已购买 market_analysis（非持久，可重复） |
| 57 | 是否已购买 compute_expansion |

如果实体数量不足（如配置只有 1 个算力中心），多余索引保持 0。

如果你编写规则型策略，建议直接读取 `info` 字典中的字段，而不是尝试解析原始向量。

## 动作掩码

`env.action_masks()` 返回 `(28,)` 布尔数组，`True` 表示该动作当前有效。

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
- 得分 = 每次卖出收入 × 10，只有 SELL_pid 动作成功时才增加。
- `terminated=True`：现金 < 0（破产）；`truncated=True`：步数耗尽（正常结束）。

## 本地运行

安装依赖：

```bash
pip install -r requirements.txt
```

运行单元测试（验证环境正常）：

```bash
python -m pytest tests/ -v
```

参考训练脚本（学习用，提交时需用自己的代码）：

```bash
# 官方 PPO 参考（RLInterfaces/ppo_agent.py）
python TrainingDemo/train_basic.py --config easy --timesteps 100000

# 手写 DQN 参考（my_agent/dqn_agent.py）
python my_agent/train.py --config easy --timesteps 200000
```

评测：

```bash
python TrainingDemo/evaluate.py --model models/ppo_thuai9_best --config easy --episodes 50
python my_agent/evaluate.py --model models/dqn_custom.pt --config easy --episodes 50
```

## 建议方向

可尝试的方向：

- 使用 `action_masks()` 做动作掩码，过滤无效动作（强烈建议）。
- 对地图做 BFS / A\* 路径规划，寻找最优路线。
- 建模市场价格正弦周期（利用观测向量中的 sin/cos 相位），预测高价时机再卖出。
- 选择高利润商品：半导体（40–120）和服饰（32–96）利润空间大，但购买成本也较高。
- 利用生产链：食品（1.0 s 生产，成本 2 原材料）适合快速周转；半导体利润率最高但生产慢。
- 优先占领算力中心以积累算力，然后购买 efficiency（生产×0.5）或 marketing（卖价×1.1）科技。
- 购买 path_optimization 科技后移动变为即时，大幅提升地图探索效率。
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

此外，**禁止 import `GameLogic` 的子模块**。评测环境可能不提供这些模块，或使用不同的内部实现。你只能 import 以下公开符号：

```python
from GameLogic import GameConfig, GameEnvironment, N_ACTIONS  # ✅
from RLInterfaces import BaseAgent                             # ✅
```

## 提交格式

提交目录**必须**符合以下结构，否则官方评测器无法加载：

```
submission/
├── agent.py              # ⚠️ 文件名必须是 agent.py，类名必须是 Agent
├── model.pt              # 训练好的权重（默认文件名 model.pt）
└── ...                   # 其他辅助模块（可选，由 agent.py import）
```

### agent.py 合约（必须满足）

```python
# agent.py
from RLInterfaces import BaseAgent

class Agent(BaseAgent):               # ← 类名必须是 Agent
    def get_action(self, observation: np.ndarray) -> int:
        ...                            # 你的推理逻辑（返回 0–27）

    def train(self, total_timesteps: int, **kwargs) -> dict:
        ...                            # 你的训练逻辑

    def save(self, path: str):
        ...                            # 保存权重

    @classmethod
    def load(cls, path: str, env) -> "Agent":   # ← 必须实现
        ...                            # 加载权重，返回 Agent 实例
```

**合约要点**：
- 文件名必须是 `agent.py`，类名必须是 `Agent`
- 必须继承 `BaseAgent`
- 必须实现 `load(cls, path, env)` 类方法
- `agent.py` 可以 import 同目录下的其他 Python 文件作为辅助模块
- `agent.py` 只能 import `GameLogic` 和 `RLInterfaces` 的公开符号（见上方 import 规则）

### 官方评测器

排名完全由 `official_evaluator.py` 决定。所有选手的提交通过同一个评测脚本跑分：

```bash
python official_evaluator.py \
    --submission ./submission \
    --config hard \
    --episodes 200 \
    --seeds 0 42 123 999 7777
```

评测器做的事：
1. 加载 `submission/agent.py` 中的 `Agent` 类
2. 调用 `Agent.load(model.pt, env)` 获取 agent 实例
3. 在**你不知道的 seed** 和 **`random_map=True`** 的环境下跑 N 个 episode
4. 以 `info["score"]` 为准输出每个 seed 的平均分
5. 最终排名 = 所有 seed 的 score 总平均

### 自测命令

提交前，用同样的官方评测器自测：

```bash
# 用 easy 快速验证加载和推理是否正常
python official_evaluator.py --submission ./submission --config easy --episodes 10 --seeds 0

# 用 medium 预估成绩
python official_evaluator.py --submission ./submission --config medium --episodes 50 --seeds 0 42 123

# 用 hard 做最终自测
python official_evaluator.py --submission ./submission --config hard --episodes 100 --seeds 0 42 123 999 7777 --output results.json
```

**注意**：比赛评测会使用更多 seed 和更多 episode，且种子值不会提前公开。
