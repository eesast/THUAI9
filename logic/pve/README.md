# AI9 PVE 强化学习环境

这是一个面向比赛的 PVE 经济决策环境。参赛算法需要在随机地图、随机市场价格、有限库存和有限需求下进行移动、买入、卖出和采集决策。

核心文档：

- [选手介绍文档](docs/CONTESTANT_GUIDE.md)
- [开发者文档](docs/DEVELOPER_GUIDE.md)
- [迭代记录](ITERATION_LOG.md)

常用验证命令：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python -m py_compile game_core.py ai_gym_env.py compare_algorithms.py heuristic_baseline.py visualize_policy.py
```

运行基线评估：

```bash
env UV_CACHE_DIR=/private/tmp/uv-cache UV_PYTHON_INSTALL_DIR=/private/tmp/uv-python MPLCONFIGDIR=/private/tmp/matplotlib \
  uv run python heuristic_baseline.py --episodes 50 --out plots_competition_ready/heuristic_after_market_impact.csv
```
