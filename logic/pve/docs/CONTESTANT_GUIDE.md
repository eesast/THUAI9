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
| `5` | BUY | 在相邻市场购买当前利润最高（市价−成本）的可负担商品 |
| `6` | SELL | 在相邻市场卖出背包内所有商品，获得当前市场价格 |
| `7` | HARVEST | 从附近资源点采集原材料 |

**动作有效性规则**：

- 移动：目标格必须可通行，且单位不处于 busy 状态。
- BUY：当前格 Manhattan 距离 ≤ 1 内有市场；背包有空余容量；现金 ≥ 最低商品成本。
- SELL：当前格 Manhattan 距离 ≤ 1 内有市场；背包内有成品。
- HARVEST：当前格 Manhattan 距离 ≤ 2 内有未耗尽资源点；背包有空余容量。

执行无效动作不会报错，但会受到惩罚并浪费步数。

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
        observation → action id.
        不能访问 self.env.unit / self.env.board 等内部对象。
        """
        # ⚠️ 必须显式调用 action_masks()！
        # MaskablePPO（sb3-contrib）会自动应用掩码，但自定义 Agent 没有自动机制。
        # 不调用此方法 → 无掩码过滤 → 网络会执行大量无效动作，训练效率极低。
        mask = self.env.action_masks()       # (8,) bool 数组，True=有效
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
        """
        训练循环。只能通过 self.reset() / self.step() 与环境交互。
        get_action() 内部已经调用了 action_masks()，这里无需额外处理。
        """
        obs = self.reset()
        for _ in range(total_timesteps):
            action = self.get_action(obs)
            obs, reward, terminated, truncated, info = self.step(action)
            if terminated or truncated:
                obs = self.reset()
            # 在这里更新你的网络……
        return {"total_timesteps": total_timesteps, ...}

    def save(self, path: str):
        """持久化模型权重。"""
        ...

    @classmethod
    def load(cls, path: str, env) -> "MyAgent":
        """从文件加载模型。评测机将调用此方法。"""
        ...
```

### MaskablePPO 与自定义 Agent 的区别（重要）

这一点经常被忽略，请务必理解：

| | MaskablePPO（SB3 方案） | 自定义 Agent（你的实现） |
|---|---|---|
| 谁调用 `action_masks()` | `ActionMasker` 包装器自动调用 | **你必须在 `get_action()` 里显式调用** |
| 掩码如何生效 | SB3 内部在 softmax 前将无效动作 logit 设为 `-inf` | 你自己设置 `q_values[~mask] = -1e9` |
| 探索时是否过滤 | 自动：策略采样只会从有效动作中选 | 你需要手动：`np.random.choice(valid)` |

一句话总结：**MaskablePPO 是自动档，自定义 Agent 是手动档——不手动调 `action_masks()` 就不会有任何掩码效果。**

### 实现要求

你自己实现的 Agent 类必须满足以下要求：

1. **继承 `BaseAgent`** — 这是评测机加载的唯一入口。
2. **实现 `get_action()`** — 给定 32 维 observation 向量，返回一个 `int` 动作编号（0–7）。
3. **实现 `train()`** — 完整的训练循环，内部只能通过 `self.reset()` / `self.step()` 与环境交互。
4. **实现 `save(path)` 和 `load(cls, path, env)`** — 保存/加载模型权重。评测机会调用 `YourAgent.load(path, env)` 加载你的模型。
5. **不能访问 `self.env` 的内部属性** — `self.env.unit`、`self.env.board`、`self.env.money` 等都是禁止的。状态信息只能从 `observation` 和 `info` 字典中获得。
6. **必须用 `self.reset()` / `self.step()`** — 不要直接调用 `self.env.reset()` 或 `self.env.step()`，用 `BaseAgent` 提供的包装方法。
7. **必须在 `get_action()` 中显式调用 `self.env.action_masks()`** — MaskablePPO 会自动应用掩码，但自定义 Agent 不会。不调用则没有任何无效动作过滤，网络会频繁执行无效动作，训练效率极低。

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
        ...                            # 你的推理逻辑

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
