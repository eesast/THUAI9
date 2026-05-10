# AI9 PVE 强化学习迭代记录

本文档记录本轮对游戏规则、强化学习算法和项目结构的主要迭代。

## 0. 初始代码阅读

初始项目包含：

- `main.py`：核心游戏模拟器。
- `ai_gym_env.py`：Gymnasium 环境封装。
- `train.py` / `advanced_train.py`：PPO 训练脚本。

初始规则特点：

- 固定 `5x5` 地图。
- 市场位置固定。
- 市场价格是确定性的正弦曲线。
- 买入半导体使用固定成本，卖出使用市场动态价格。

第一次训练后，PPO 很快学到了一个漏洞策略：

1. 走到最近市场附近。
2. 原地反复买入半导体。
3. 原地反复卖出。
4. 利用固定买入成本和动态卖出价格套利。

实验结果：

| 设置 | 最终资金 | 奖励 | 说明 |
| --- | ---: | ---: | --- |
| 原始规则 PPO | 30383.82 | 2937.38 | 利用了同市场原地套利 |

## 1. 修复动作空间和原地套利

规则与接口改动：

- 将 `action_space` 从 `Discrete(7)` 修为 `Discrete(8)`，使动作 `7`（采集）可以被采样。
- 买入价格从固定成本改为市场动态 ask 价格。
- 加入买卖价差：买入按 ask，卖出按 bid。
- 记录商品来源市场。
- 禁止将商品直接卖回购买来源市场。
- 在 observation 中加入“库存来源市场”信息。

结果：

- PPO 原来的原地套利策略失效。
- PPO 在 50k timesteps 内没有学到有效跨市场交易策略。
- DQN 开始出现弱正向信号。

实验结果：

| 算法 | 训练步数 | 最终资金 | 奖励 | 交易次数 |
| --- | ---: | ---: | ---: | ---: |
| PPO | 50k | 1000.00 | -5.00 | 0 |
| DQN | 50k | 1043.91 | -0.61 | 3 |
| A2C | 50k | 1000.00 | -5.00 | 0 |

## 2. 增加多算法对比和最佳模型保存

算法侧改动：

- 新增 `compare_algorithms.py`。
- 支持统一训练和评估：
  - PPO
  - DQN
  - A2C
  - Random baseline
- 增加训练过程中的周期性评估。
- 保存 best checkpoint，而不是只保存最终 checkpoint。

原因：

- DQN 训练过程不稳定。
- 它可能在中途学到有效策略，但继续训练后退化。
- 如果只看最终 checkpoint，会误判算法效果。

实验结果：

| 算法/模型 | 训练步数 | 最终资金 | 奖励 | 说明 |
| --- | ---: | ---: | ---: | --- |
| DQN final checkpoint | 200k | 9.10 | -5.00 | 严重退化 |
| DQN best checkpoint | 200k | 1134.12 | 8.41 | 最优点约在 150k 附近 |

## 3. 分离游戏规则逻辑和强化学习代码

结构改动：

- 新增 `game_core.py`，作为游戏规则层。
- `main.py` 改为兼容导出文件，保留旧代码中的 `from main import GameEnv` 用法。
- `AI9GymEnv` 改为只通过公开接口访问游戏逻辑。

游戏规则层公开接口：

- `reset(seed)`
- `step(action)`
- `get_public_observation()`
- `get_valid_actions()`
- `get_net_worth()`

强化学习代码不应依赖：

- `units[0]`
- `_find_nearby_market`
- `market` 对象内部实现
- 其他非公开模拟器细节

公开 observation 包含：

- 单位位置、载货、忙碌状态。
- 当前资金和估算净资产。
- 市场位置、买价、卖价、价格周期信息。
- 资源点位置和库存。
- 地图网格。
- 当前 valid action 向量。
- 上一步动作结果。

这一层隔离使项目更接近比赛评测结构：参赛算法只能调用公开接口，不能读取私有游戏状态。

## 4. 加入随机地图和随机市场条件

规则改动：

- 地图从 `5x5` 扩展到 `7x7`。
- 每个 episode 随机生成：
  - 市场位置。
  - 资源点位置。
  - 障碍位置。
  - 市场价格相位。
  - 市场价格周期。
  - 市场价格倍率。
- 移动开始受障碍影响。

目的：

- 防止模型背固定路线。
- 要求算法对不同地图和市场条件具备泛化能力。
- 让比赛不只是固定关卡的过拟合问题。

观察到的问题：

- 单一评估种子的 best checkpoint 选择不可靠。
- DQN 可以在某一个随机种子上得到很高收益，但泛化到多个随机种子后表现较差。

因此，best checkpoint 的选择改为多 seed 平均评估。

加入市场库存/需求前的结果：

| 策略 | 评估方式 | 平均最终资金 |
| --- | --- | ---: |
| DQN，单 seed 选择 best | 10 个随机 seed | 877.53 |
| DQN，多 seed 选择 best | 20 个随机 seed | 766.89 |
| 脚本化阈值交易策略 | 50 个随机 seed | 7760.19 |

这个结果说明：随机地图能削弱简单 RL，但脚本化阈值策略仍然过强，规则还不够有挑战。

## 5. 加入市场库存、需求和价格冲击

规则改动：

- 每个市场有随机初始库存。
- 每个市场有随机初始需求。
- 买入会消耗市场库存。
- 卖出会消耗市场需求。
- 库存和需求随时间缓慢恢复。
- 库存低时，买价上升。
- 需求低时，卖价下降。
- observation 中暴露市场库存比例和需求比例。

目的：

- 阻止简单策略无限重复利用同一市场。
- 迫使算法考虑：
  - 去哪个市场买。
  - 去哪个市场卖。
  - 买多少。
  - 什么时候等市场恢复。
  - 是否值得移动到更远市场。

加入该规则后，脚本化策略仍然能盈利，但不再碾压环境。

最终规则下的结果：

| 策略/算法 | 评估局数 | 平均最终资金 | 平均奖励 | 平均交易次数 |
| --- | ---: | ---: | ---: | ---: |
| 脚本化阈值交易策略 | 50 | 2187.63 | 144.05 | 11.96 |
| DQN 200k | 20 | 924.64 | -9.79 | 0.05 |
| PPO 100k | 10 | 1000.00 | -11.49 | 0 |
| DQN 100k | 10 | 1000.00 | -11.92 | 0 |
| A2C 100k | 10 | 1000.00 | -3.16 | 0 |
| Random | 30 | 903.58 | -15.81 | 2.57 |

结论：

- 当前规则对 vanilla PPO / DQN / A2C 不再容易。
- 随机策略平均会亏损。
- 只使用公开接口的非平凡脚本策略仍能稳定盈利。
- 这说明环境不是无解，但需要规划、泛化和更好的探索。

## 6. 当前比赛初版状态

关键文件：

- `game_core.py`：游戏规则模拟器和公开 API。
- `ai_gym_env.py`：Gymnasium 强化学习封装。
- `compare_algorithms.py`：PPO / DQN / A2C / Random 训练与对比。
- `heuristic_baseline.py`：只使用公开接口的脚本化基线。
- `visualize_policy.py`：训练模型轨迹可视化。
- `ITERATION_LOG.md`：当前迭代记录。

主要输出目录：

- `plots_competition_compare_100k/`
- `plots_competition_dqn_200k/`
- `plots_competition_ready/`

常用命令：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python compare_algorithms.py --timesteps 100000 --episodes 10 --random-episodes 30 --algorithms ppo dqn a2c --eval-episodes 5 --model-dir models/competition_compare_100k --out-dir plots_competition_compare_100k --force
```

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python heuristic_baseline.py --episodes 50 --out plots_competition_ready/heuristic_after_market_impact.csv
```

## 7. 当前停止点判断

当前项目已经适合作为比赛初版：

- 游戏规则和强化学习算法已经分层。
- 游戏逻辑提供明确公开接口。
- 规则包含随机地图和随机市场条件。
- 规则包含库存、需求和价格冲击，避免简单套利。
- 普通 PPO / DQN / A2C 在当前预算下不能轻松解决。
- 脚本化公开接口策略可以稳定盈利，证明环境仍然可解。
- 参赛者有明确优化空间：
  - 动作掩码。
  - 路径规划。
  - 市场建模。
  - 课程学习。
  - 更强探索。
  - 模型预测控制。
  - 混合规则策略和强化学习。
  - recurrent policy 或历史特征建模。

由于现在同学们会使用 AI 编程辅助，简单漏洞和固定路线很容易被快速发现。因此当前版本刻意要求算法具备跨随机地图、随机市场和有限库存/需求条件下的泛化能力。
