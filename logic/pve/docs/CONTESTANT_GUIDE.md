# 选手介绍文档

## 文件修改权限

### 不可修改（只读）

| 路径 | 原因 |
|---|---|
| `GameLogic/**` | 游戏规则引擎，修改即作弊 |
| `RLInterfaces/base_agent.py` | Agent 接口契约 |
| `RLInterfaces/__init__.py` | 包导出声明 |
| `tests/test_game_logic.py` | 环境正确性验证 |
| `TrainingDemo/configs/*.yaml` | 官方难度配置 |

### 可参考但禁止直接使用

禁止 import 以下类/函数直接使用，必须自己从 `BaseAgent` 实现：

| 路径 | 禁止行为 |
|---|---|
| `RLInterfaces/ppo_agent.py` | 禁止 `from RLInterfaces import PPOAgent` |
| `RLInterfaces/training_loop.py` | 禁止使用 `TrainingLoop` |
| `my_agent/dqn_agent.py` | 禁止使用 `DQNAgent` |
| `TrainingDemo/*.py` | 仅作参考，禁止照搬 |
| `tests/test_interfaces.py` | 同上 |

### 可自由修改

自建目录（如 `my_agent/`）编写模型，允许的 import：

```python
from GameLogic import GameConfig, GameEnvironment, N_ACTIONS  # ✅
from RLInterfaces import BaseAgent                             # ✅
from GameLogic.board import Board                              # ❌ 禁止
from RLInterfaces import PPOAgent                              # ❌ 禁止
```

---

## 比赛目标

在 PvE 经济环境中买卖商品、采集原材料、生产高价值商品，最大化累计得分。**得分 = 卖出收入 × 10**。资金<0 或时间耗尽时终止，多 seed 平均分排名。

## 环境概览

| 难度 | 地图 | 市场 | 资源 | 算力中心 | 初始资金 | 初始算力 | 时长 |
|:----:|:----:|:----:|:----:|:--------:|:--------:|:--------:|:----:|
| easy | 5×5 | 3 | 2 | 1 | 200 | 60 | 300s |
| medium | 10×10 | 3 | 2 | 2 | 50 | 30 | 300s |
| hard | 15×15 | 4 | 4 | 3 | 30 | 20 | 500s |

工厂位于 (0,0)，每 tick 0.25s。

## 商品

| ID | 名称 | 买价 | 原料 | 市价范围 | 生产 |
|:--:|:----:|:----:|:----:|:--------:|:----:|
| 0 | 半导体 | 10 | 5 | 40–120 | 5.0s |
| 1 | 药品 | 5 | 3 | 20–60 | 4.0s |
| 2 | 小商品 | 1 | 1 | 4–12 | 2.0s |
| 3 | 服饰 | 8 | 4 | 32–96 | 6.0s |
| 4 | 食品 | 3 | 2 | 12–24 | 1.0s |

## 市场

OU 随机游走价格，每 tick 更新：`dP = θ(μ−P)·dt + σ·√dt·N(0,1)`。θ=0.05，σ=amplitude×0.12，价格夹在 [lo, lo+amplitude]。

**套利规则**：BUY 选跨市场卖价最高的可负担商品。禁止同市场原地套利（商品追踪 `prod_origin`，卖出时排除当前市场来源部分）。卖价受 TECH_2(×1.1) 影响。

`price_volatility` 控制振幅：easy=0.3, medium=1.0, hard=2.0。

## 玩法路线

1. **套利**：市场A BUY → 市场B SELL_pid
2. **生产链**：HARVEST → DEPOSIT → PRODUCE_pid → LOAD → 市场 SELL_pid
3. **科技**：OCCUPY（开算力中心）→ TECH_x（在工厂购买科技，消耗算力）

## 动作空间（28 个，`N_ACTIONS = 28`）

| 编号 | 动作 | 要点 |
|:----:|:----:|------|
| 0 | WAIT | 始终有效 |
| 1–4 | MOVE_UP/DOWN/LEFT/RIGHT | 目标可通行，busy_ticks=0 |
| 5 | BUY | Manhattan≤1 有市场，跨市场套利最优 |
| 6–10 | SELL_0~4 | 卖出非当前市场来源商品，成功时 score+=revenue×10 |
| 11 | HARVEST | Manhattan≤2 有资源 |
| 12 | DEPOSIT | 在工厂格，背包 raw_inv>0 |
| 13–17 | PRODUCE_0~4 | 在工厂格，raw_stock≥原料消耗(5/3/1/4/2) |
| 18 | LOAD | 在工厂格，工厂有成品 |
| 19 | OCCUPY | 相邻有未开算力中心 |
| 20–27 | TECH_0~7 | 在工厂格，消耗算力(50/40/80/30/60/50/40/70) |

科技列表：

| TECH | 效果 | 消耗 | 前置 |
|:----:|------|:----:|:----:|
| 0 | 买价 −2 | 50 | — |
| 1 | 生产时间 ×0.5 | 40 | — |
| 2 | 卖价 ×1.1 | 80 | — |
| 3 | HP +50% | 30 | — |
| 4 | 产线 +1 | 60 | — |
| 5 | 移动无 busy_tick | 50 | TECH_1 |
| 6 | obs 标记（可重复） | 40 | — |
| 7 | 算力 +30% | 70 | — |

- `busy_ticks > 0` 时仅 WAIT 有效；移动消耗 1 tick（TECH_5 后为 0）
- 无效动作不报错但受 −0.02 奖励惩罚
- `action_masks()` 返回 `(28,)` bool，True=有效

## 观测向量（58 维 float32）

| 索引 | 内容 |
|:----:|------|
| 0–1 | 单位坐标 / (H,W) |
| 2 | HP / max_hp |
| 3 | raw_inv / capacity |
| 4–8 | prod_inv[0–4] / capacity |
| 9 | busy_ticks / 10 |
| 10 | log10(money+1) / 5 |
| 11 | compute / 100 |
| 12 | time / max_time |
| 13–14 | sin/cos(2π·t / period) |
| 15 | 工厂 raw / storage_cap |
| 16–20 | 工厂 prod[0–4] / storage_cap |
| 21 | 队列长度 / 10 |
| 22–27 | 资源0,1: dx/H, dy/W, stock/max |
| 28–35 | 算力中心0,1: dx/H, dy/W, is_open, progress |
| 36–49 | 市场0,1: dx/H, dy/W, 5种价格(val_range归一化) |
| 50–57 | 科技 one-hot（TECH_0~7 顺序） |

不足实体数量的索引保持 0。编写规则策略建议直接读 `info` 而非解析 obs。

## 奖励（默认 mode="standard"）

| 来源 | 默认值 |
|------|:--:|
| Δmoney × 0.02 | 正负均有 |
| Δscore × 0.001 | 仅卖出时 |
| 时间惩罚 | −0.001/步 |
| 采集奖励 | 0.0（默认关闭） |
| 算力中心解锁 / 科技购买 | +1.0/个 |
| 无效动作 | −0.02 |
| 破产 | −10.0 |

奖励是训练辅助信号，排名以 `info["score"]` 为准。

## 模型实现（必须自己写）

继承 `BaseAgent`，实现 `get_action` / `train` / `save` / `load`。**自定义 Agent 必须在 `get_action()` 中显式调用 `action_masks()`**（MaskablePPO 会自动调，自定义 Agent 不会）。

合约要点：
- 文件名 `agent.py`，类名 `Agent`，继承 `BaseAgent`
- `get_action(obs)` → int (0–27)
- `load(cls, path, env)` → Agent 实例
- 禁止访问 `env.unit` / `env.board` / `env.money` 等内部对象

```python
from RLInterfaces import BaseAgent
from GameLogic import N_ACTIONS
import numpy as np

class Agent(BaseAgent):
    def get_action(self, observation: np.ndarray) -> int:
        mask = self.env.action_masks()  # 必须显式调用！
        valid = np.where(mask)[0]
        # ... 你的推理逻辑（在屏蔽无效动作后选 argmax 或 采样）
        return action

    def train(self, total_timesteps: int, **kwargs) -> dict:
        # 使用 self.reset() / self.step() 与环境交互
        ...

    def save(self, path: str): ...
    
    @classmethod
    def load(cls, path: str, env) -> "Agent": ...
```

## 本地运行与评测

```bash
pip install -r requirements.txt
python -m pytest tests/ -v

# 训练
python TrainingDemo/train_basic.py --config easy --timesteps 100000

# 自测
python official_evaluator.py --submission ./submission --config medium --episodes 50 --seeds 0 42 123
```

## 提交格式

```
submission/
├── agent.py        # 文件名=agent.py, 类名=Agent
├── model.pt        # 训练权重
└── ...             # 辅助模块
```
