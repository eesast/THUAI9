# THUAI9 PvE-RL 强化学习训练框架

THUAI9 PvE 模式的完整强化学习训练框架，通过**迭代式规则演化**培养具有适当难度的 AI 挑战赛环境。

## 快速开始

```bash
# 安装依赖
pip install -r requirements.txt

# 运行测试（验证环境正常）
cd THUAI9-PvE-RL
python -m pytest tests/ -v

# 基础训练（简单难度，10 万步）
python TrainingDemo/train_basic.py --config easy --timesteps 100000

# 评测已保存模型
python TrainingDemo/evaluate.py --model models/ppo_thuai9_best --config easy --episodes 50
```

## 项目结构

```
THUAI9-PvE-RL/
├── GameLogic/           # 游戏规则与状态管理（算法不可直接修改）
│   ├── config.py        # 全局配置（难度参数化）
│   ├── board.py         # 地图、资源点、算力中心
│   ├── character.py     # 单位（HP、背包、移动）
│   ├── market.py        # 市场动态价格函数
│   ├── action_space.py  # 动作空间定义与动作掩码
│   ├── reward_calculator.py  # 奖励计算
│   └── game_env.py      # 主环境（Gymnasium 接口）
├── RLInterfaces/        # RL 算法接口层
│   ├── base_agent.py    # 抽象基类（强制接口隔离）
│   ├── ppo_agent.py     # PPO 实现（支持动作掩码）
│   └── training_loop.py # 训练循环（含突破回调）
├── TrainingDemo/        # 训练脚本与配置
│   ├── train_basic.py   # 基础训练入口
│   ├── evaluate.py      # 评测脚本
│   ├── visualization.py # ASCII 渲染 + 奖励曲线
│   └── configs/         # easy / medium / hard YAML
├── my_agent/            # 自定义 Agent 示例（手写 DQN）
│   ├── dqn_agent.py     # DQN 实现（Q-Network + ReplayBuffer）
│   ├── train.py         # 训练入口
│   └── evaluate.py      # 多 seed 评测
├── tests/               # 单元测试
└── ITERATIONS.md        # 迭代演化日志（核心文档）
```

## 游戏机制（Phase 1）

| 要素 | 说明 |
|------|------|
| 地图 | 可配置网格（默认 10×10），含障碍/市场/资源/算力中心 |
| 产品 | 5 类（半导体/药品/小商品/服饰/食品），正弦价格函数 |
| 动作 | 8 个离散动作：等待/移动×4/买入/卖出/采集 |
| 奖励 | 销售收益（主）+ 采集塑形 + 时间惩罚 |
| 得分 | 销售额 × 10 |

## 动作掩码

环境实现 `action_masks()` 接口，MaskablePPO 自动过滤无效动作（墙壁碰撞、无法交易等），加速早期探索。

## 迭代路线图

详见 [ITERATIONS.md](ITERATIONS.md)。框架在算法取得突破后自动触发难度升级回调。

## 难度对比

| 配置 | 地图 | 初始资金 | 价格波动 | 资源再生 |
|------|------|---------|---------|---------|
| easy   | 5×5   | $200 | 0.3× | 2.0× |
| medium | 10×10 | $50  | 1.0× | 1.0× |
| hard   | 15×15 | $30  | 2.0× | 0.5× |
