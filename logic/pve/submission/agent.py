"""
THUAI9 PvE-RL Agent — 纯套利策略（v7）。

核心策略：
1. 开局抢占算力中心 → 累积 compute
2. 获取 TECH_1 + TECH_5（消除移动冷却）+ TECH_3（容量+50%）
3. 跨市场套利为主要（唯一）利润来源
4. 不采集不生产——在 volatility=2.0 下套利效率远超生产
5. A* 路径缓存 + 买入来源追踪
"""
from __future__ import annotations

import os
import math
import random
import heapq
from collections import deque
from typing import Any, Dict, List, Optional, Tuple

import numpy as np
import torch
import torch.nn as nn

from GameLogic import N_ACTIONS
from RLInterfaces import BaseAgent


class PolicyNet(nn.Module):
    def __init__(self, obs_dim=82, act_dim=28, hidden=512):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(obs_dim, hidden), nn.BatchNorm1d(hidden), nn.ReLU(inplace=True),
            nn.Dropout(0.15),
            nn.Linear(hidden, hidden), nn.BatchNorm1d(hidden), nn.ReLU(inplace=True),
            nn.Dropout(0.15),
            nn.Linear(hidden, hidden), nn.BatchNorm1d(hidden), nn.ReLU(inplace=True),
            nn.Dropout(0.15),
            nn.Linear(hidden, act_dim),
        )
    def forward(self, obs, mask):
        logits = self.net(obs)
        logits[~mask] = -1e9
        return logits


# ── 动作常量 ──────────────────────────────────────────────────────────────────
WAIT, MOVE_UP, MOVE_DOWN, MOVE_LEFT, MOVE_RIGHT = 0, 1, 2, 3, 4
BUY, HARVEST, DEPOSIT, LOAD, OCCUPY = 5, 11, 12, 18, 19
SELL_0, SELL_1, SELL_2, SELL_3, SELL_4 = 6, 7, 8, 9, 10
PRODUCE_0, PRODUCE_1, PRODUCE_2, PRODUCE_3, PRODUCE_4 = 13, 14, 15, 16, 17
TECH_0, TECH_1, TECH_2, TECH_3, TECH_4, TECH_5, TECH_6, TECH_7 = range(20, 28)

SELL_ACTIONS = [SELL_0, SELL_1, SELL_2, SELL_3, SELL_4]
PROD_ACTIONS = [PRODUCE_0, PRODUCE_1, PRODUCE_2, PRODUCE_3, PRODUCE_4]
TECH_ACTIONS = list(range(20, 28))

MOVE_DELTA = {MOVE_UP: (-1, 0), MOVE_DOWN: (1, 0), MOVE_LEFT: (0, -1), MOVE_RIGHT: (0, 1)}
MOVE_SET = {MOVE_UP, MOVE_DOWN, MOVE_LEFT, MOVE_RIGHT}

# 科技名 → TECH_x 映射（用于按需购买）
TECH_KEY_TO_ID = {
    "efficiency": TECH_1,
    "path_optimization": TECH_5,
    "durability": TECH_3,
    "marketing": TECH_2,
    "compute_expansion": TECH_7,
}

# 科技购买优先级：路径优化→扩容→提价（path_opt 消除移动冷却最优先）
TECH_PRIORITY = ["efficiency", "path_optimization", "durability", "marketing"]

PRODUCT = {
    0: {"cost": 10, "raw": 5, "lo": 40, "hi": 120, "time": 5.0},
    1: {"cost": 5, "raw": 3, "lo": 20, "hi": 60, "time": 4.0},
    2: {"cost": 1, "raw": 1, "lo": 4, "hi": 12, "time": 2.0},
    3: {"cost": 8, "raw": 4, "lo": 32, "hi": 96, "time": 6.0},
    4: {"cost": 3, "raw": 2, "lo": 12, "hi": 24, "time": 1.0},
}

# 观测索引
OFF_X, OFF_Y, OFF_RAW, OFF_PROD = 0, 1, 3, 4
OFF_BUSY, OFF_MONEY, OFF_COMP, OFF_TIME = 9, 10, 11, 12
OFF_RES, OFF_CENTER, OFF_MARKET, OFF_TECH = 22, 34, 46, 74

CAP = 30.0


def _price(pid, norm):
    p = PRODUCT[pid]
    rng = max(1.0, p["hi"] - p["lo"])
    return norm * rng + p["lo"]


class Agent(BaseAgent):
    def __init__(self, env, mode="rule"):
        super().__init__(env)
        self.mode = mode
        self.policy_net = None

        self.x = self.y = 0
        self.H = self.W = 0
        self._map_ok = False

        # 实体位置（obs 每步刷新）
        self.mkts = {}     # mid → (x, y)
        self.res = {}      # rid → (x, y)
        self.ctrs = {}     # cid → (x, y)

        # 经济
        self.money = 0.0
        self.compute = 0.0

        # 记忆
        self._techs = set()
        self._obs = set()        # 已知障碍
        self._black = set()      # 黑名单（不可达目标）
        self._black_t = {}
        self._cells = set()      # 已访问
        self._fail = {}

        # 目标系统
        self._goal = None        # (gx, gy)
        self._goal_act = None    # 到达后执行的动作
        self._goal_steps = 0
        self._goal_start_dist = 0
        self._state = "EXPLORE"

        # 卡住检测
        self._pos_hist = []

        # Episode 检测
        self._last_t = -1.0
        self._eps_steps = 0

        # 重规划
        self._replan = True

        # ── 新增：路径缓存 ──
        self._path_cache = {}    # (sx,sy,gx,gy) → [actions] or None
        self._path_hits = 0
        self._path_misses = 0

        # ── 新增：买入追踪 ──
        self._last_buy_mid = None   # 最近买入的市场 id
        self._has_bought = False    # 是否持有通过 BUY 获得的产品

        # ── 新增：算力中心追踪 ──
        self._occupied = set()      # 已占领的算力中心 id

        # ── 新增：科技购买追踪（防止反复尝试）──
        self._tech_attempted = {}   # tech_key → fail_count

    # ═══════════════════════════════════════════════════════════════════════════
    # 观测
    # ═══════════════════════════════════════════════════════════════════════════

    def _reset(self):
        self._obs.clear()
        self._black.clear()
        self._black_t.clear()
        self._cells.clear()
        self._fail.clear()
        self._pos_hist.clear()
        self._techs.clear()
        self._goal = None
        self._goal_act = None
        self._goal_steps = 0
        self._goal_start_dist = 0
        self._state = "EXPLORE"
        self._replan = True
        self._eps_steps = 0
        self._path_cache.clear()
        self._last_buy_mid = None
        self._has_bought = False
        self._occupied.clear()
        self._tech_attempted.clear()

    def _read_obs(self, obs):
        # 地图检测
        if not self._map_ok:
            m = 10 ** (obs[OFF_MONEY] * 5) - 1
            if m > 150:
                self.H = self.W = 5
            elif m > 40:
                self.H = self.W = 10
            else:
                self.H = self.W = 15
            self._map_ok = True

        # Episode 重置检测
        t = obs[OFF_TIME]
        if self._last_t >= 0 and t < self._last_t - 0.05:
            self._reset()
        self._last_t = t
        self._eps_steps += 1

        H, W = max(1, self.H), max(1, self.W)

        # 位置
        self.x = int(round(obs[OFF_X] * H))
        self.y = int(round(obs[OFF_Y] * W))

        # 经济
        self.compute = obs[OFF_COMP] * 100.0
        self.money = 10 ** (obs[OFF_MONEY] * 5) - 1

        # 资源
        self.res.clear()
        for i in range(4):
            b = OFF_RES + i * 3
            dx, dy = obs[b], obs[b + 1]
            if abs(dx) + abs(dy) < 1e-6:
                continue
            rx = self.x + int(round(dx * H))
            ry = self.y + int(round(dy * W))
            if 0 <= rx < H and 0 <= ry < W:
                self.res[i] = (rx, ry)

        # 中心
        self.ctrs.clear()
        for i in range(3):
            b = OFF_CENTER + i * 4
            dx, dy = obs[b], obs[b + 1]
            if abs(dx) + abs(dy) < 1e-6:
                continue
            cx = self.x + int(round(dx * H))
            cy = self.y + int(round(dy * W))
            if 0 <= cx < H and 0 <= cy < W:
                self.ctrs[i] = (cx, cy)

        # 市场
        self.mkts.clear()
        for i in range(4):
            b = OFF_MARKET + i * 7
            dx, dy = obs[b], obs[b + 1]
            if abs(dx) + abs(dy) < 1e-6:
                # 站在市场上：位置就是自身
                mx, my = self.x, self.y
            else:
                mx = self.x + int(round(dx * H))
                my = self.y + int(round(dy * W))
            if 0 <= mx < H and 0 <= my < W:
                self.mkts[i] = (mx, my)

        # 检测新占领的算力中心
        self._check_new_centers(obs)

        # 更新买入追踪：如果身上没有产品了，清除标记
        if not self._has_prod(obs):
            self._has_bought = False
            self._last_buy_mid = None

    def _check_new_centers(self, obs):
        """检测是否有新占领的算力中心。"""
        for i in range(3):
            b = OFF_CENTER + i * 4
            if b + 2 < len(obs) and obs[b + 2] > 0.5:
                self._occupied.add(i)

    # ═══════════════════════════════════════════════════════════════════════════
    # 辅助
    # ═══════════════════════════════════════════════════════════════════════════

    def _prices(self, obs, mid):
        b = OFF_MARKET + mid * 7 + 2
        return {pid: _price(pid, obs[b + pid]) for pid in range(5)}

    def _all_prices(self, obs):
        return {mid: self._prices(obs, mid) for mid in self.mkts}

    def _techs_owned(self, obs):
        return [obs[OFF_TECH + i] > 0.5 for i in range(8)]

    def _busy(self, obs):
        return obs[OFF_BUSY] * 10.0 > 0.5

    def _has_raw(self, obs):
        return obs[OFF_RAW] * CAP > 0.5

    def _has_prod(self, obs):
        return any(obs[OFF_PROD + i] * CAP > 0.01 for i in range(5))

    def _raw_qty(self, obs):
        return obs[OFF_RAW] * CAP

    def _prod_inv(self, obs):
        return {pid: obs[OFF_PROD + pid] * CAP for pid in range(5)
                if obs[OFF_PROD + pid] * CAP > 0.01}

    def _at_factory(self):
        return self.x == 0 and self.y == 0

    def _capacity(self, obs):
        return 45.0 if self._techs_owned(obs)[3] else 30.0

    # ═══════════════════════════════════════════════════════════════════════════
    # 障碍物学习
    # ═══════════════════════════════════════════════════════════════════════════

    def _learn_obs(self, mask):
        """标记被封堵的移动方向。"""
        for act, (dx, dy) in MOVE_DELTA.items():
            if not mask[act]:
                nx, ny = self.x + dx, self.y + dy
                if 0 <= nx < self.H and 0 <= ny < self.W:
                    if (nx, ny) not in self._cells:
                        self._obs.add((nx, ny))

    def _check_stuck(self):
        self._pos_hist.append((self.x, self.y))
        if len(self._pos_hist) > 30:
            self._pos_hist.pop(0)
        if len(self._pos_hist) >= 25:
            if len(set(self._pos_hist)) <= 3:
                self._obs.clear()
                self._black.clear()
                self._black_t.clear()
                self._pos_hist.clear()
                self._path_cache.clear()
                self._goal = None
                self._goal_act = None
                self._replan = True

    # ═══════════════════════════════════════════════════════════════════════════
    # A*（带缓存）
    # ═══════════════════════════════════════════════════════════════════════════

    def _astar(self, gx, gy):
        if (self.x, self.y) == (gx, gy):
            return []
        if not (0 <= gx < self.H and 0 <= gy < self.W):
            return None
        if (gx, gy) in self._obs:
            return None

        # 检查缓存
        cache_key = (self.x, self.y, gx, gy)
        if cache_key in self._path_cache:
            self._path_hits += 1
            return self._path_cache[cache_key]

        self._path_misses += 1

        start = (self.x, self.y)
        goal = (gx, gy)
        open_set = [(abs(start[0] - gx) + abs(start[1] - gy), 0, start, None, -1)]
        came_from = {}
        cost = {start: 0}
        found = None

        while open_set:
            _, c, pos, prev, act = heapq.heappop(open_set)
            if pos in came_from:
                continue
            came_from[pos] = (prev, act)
            if pos == goal:
                found = pos
                break
            for a, (dx, dy) in MOVE_DELTA.items():
                nx, ny = pos[0] + dx, pos[1] + dy
                if not (0 <= nx < self.H and 0 <= ny < self.W):
                    continue
                if (nx, ny) in self._obs:
                    continue
                nc = c + 1
                if (nx, ny) not in cost or nc < cost[(nx, ny)]:
                    cost[(nx, ny)] = nc
                    heapq.heappush(open_set, (nc + abs(nx - gx) + abs(ny - gy), nc, (nx, ny), pos, a))

        if found is None:
            self._path_cache[cache_key] = None
            return None

        acts = []
        cur = found
        while cur != start:
            prev, a = came_from[cur]
            acts.append(a)
            cur = prev
        acts.reverse()

        # 缓存路径（限制缓存大小）
        if len(self._path_cache) < 5000:
            self._path_cache[cache_key] = acts

        return acts

    # ═══════════════════════════════════════════════════════════════════════════
    # 目标超时检测
    # ═══════════════════════════════════════════════════════════════════════════

    def _goal_timed_out(self):
        if self._goal is None:
            return False
        gx, gy = self._goal
        dist = abs(self.x - gx) + abs(self.y - gy)

        if self._goal_steps == 0:
            self._goal_start_dist = dist

        self._goal_steps += 1
        timeout = 120 + self._goal_start_dist * 5

        if self._goal_steps > timeout:
            return True
        if self._goal_steps > 40 and dist >= self._goal_start_dist:
            return True
        return False

    # ═══════════════════════════════════════════════════════════════════════════
    # 黑名单维护
    # ═══════════════════════════════════════════════════════════════════════════

    def _blacklist_maintain(self):
        expired = [p for p, t in self._black_t.items() if self._eps_steps - t > 800]
        for p in expired:
            self._black.discard(p)
            del self._black_t[p]
        while len(self._black) > 12:
            old = min((p for p in self._black if p != (0, 0)),
                      key=lambda p: self._black_t.get(p, 0), default=None)
            if old is None:
                break
            self._black.discard(old)
            del self._black_t[old]

    def _blacklist(self, pos):
        if pos and pos != (0, 0):
            self._black.add(pos)
            self._black_t[pos] = self._eps_steps

    # ═══════════════════════════════════════════════════════════════════════════
    # 导航
    # ═══════════════════════════════════════════════════════════════════════════

    def _move_to(self, mask):
        """向 _goal 移动一步。"""
        if self._goal is None:
            return None
        gx, gy = self._goal

        # 已到达
        if (self.x, self.y) == (gx, gy):
            return None

        # 超时检测
        if self._goal_timed_out():
            self._blacklist((gx, gy))
            self._goal = None
            self._goal_act = None
            return None

        # A*
        path = self._astar(gx, gy)
        if path and len(path) > 0 and mask[path[0]]:
            return path[0]

        # 贪心兜底
        best, best_d = None, 999
        for a, (dx, dy) in MOVE_DELTA.items():
            if mask[a]:
                nx, ny = self.x + dx, self.y + dy
                if 0 <= nx < self.H and 0 <= ny < self.W:
                    d = abs(nx - gx) + abs(ny - gy)
                    if d < best_d:
                        best_d = d
                        best = a

        if best is None:
            fc = self._fail.get((gx, gy), 0) + 1
            self._fail[(gx, gy)] = fc
            if fc >= 3:
                self._blacklist((gx, gy))
                self._fail.pop((gx, gy), None)
            self._goal = None
            self._goal_act = None
        return best

    def _set_goal(self, gx, gy, action=None):
        """设置新目标并重置计时器。"""
        self._goal = (gx, gy) if gx is not None else None
        self._goal_act = action
        self._goal_steps = 0
        if gx is not None:
            self._goal_start_dist = abs(self.x - gx) + abs(self.y - gy)
        self._replan = False

    # ═══════════════════════════════════════════════════════════════════════════
    # 探索（BFS 最近未访问格）
    # ═══════════════════════════════════════════════════════════════════════════

    def _explore(self):
        self._cells.add((self.x, self.y))
        q = deque([(self.x, self.y)])
        vis = {(self.x, self.y)}
        while q:
            cx, cy = q.popleft()
            if (cx, cy) not in self._cells and (cx, cy) not in self._obs:
                return (cx, cy)
            for dx, dy in [(0, 1), (0, -1), (1, 0), (-1, 0)]:
                nx, ny = cx + dx, cy + dy
                if 0 <= nx < self.H and 0 <= ny < self.W and (nx, ny) not in vis:
                    vis.add((nx, ny))
                    q.append((nx, ny))
        return None

    # ═══════════════════════════════════════════════════════════════════════════
    # 套利系统（核心）
    # ═══════════════════════════════════════════════════════════════════════════

    def _best_arbitrage(self, obs):
        """找最佳套利机会。(buy_mid, sell_mid, pid, margin, profit_est) 或 None。"""
        if len(self.mkts) < 2:
            return None
        prices = self._all_prices(obs)

        best = None
        best_score = float("-inf")

        for bmid, bp in prices.items():
            bx, by = self.mkts[bmid]
            if (bx, by) in self._black:
                continue
            for smid, sp in prices.items():
                if bmid == smid:
                    continue
                sx, sy = self.mkts[smid]
                if (sx, sy) in self._black:
                    continue
                for pid in range(5):
                    buy_p = bp.get(pid, 0)
                    sell_p = sp.get(pid, 0)
                    if buy_p <= 0 or sell_p <= buy_p * 1.02:
                        continue
                    margin = sell_p - buy_p
                    # 综合评分：利润率 - 距离惩罚
                    profit_rate = margin / max(buy_p, 1)
                    travel = abs(self.x - bx) + abs(self.y - by) + abs(bx - sx) + abs(by - sy)
                    # 有效的单步利润率（travel cost per unit is amortized over capacity）
                    cap = self._capacity(obs)
                    # 能买多少单位
                    affordable = int(self.money / max(buy_p, 1)) if self.money > 0 else 0
                    units = min(affordable, int(cap))
                    if units <= 0:
                        continue
                    total_profit = units * margin
                    # 评分：单步期望收益（buy: units*2 tick, sell: 一次卖全部仅2 tick）
                    steps_est = travel + units * 2 + 2
                    if steps_est <= 0:
                        continue
                    score = total_profit / steps_est
                    if score > best_score:
                        best_score = score
                        best = (bmid, smid, pid, margin, total_profit, buy_p, sell_p, units)

        return best

    def _best_sell_market(self, obs, avoid_mid=None):
        """为身上产品找到最佳卖出市场。"""
        prod_inv = self._prod_inv(obs)
        if not prod_inv:
            return None

        prices = self._all_prices(obs)
        best = None
        best_total = 0

        for mid, mp in prices.items():
            if mid == avoid_mid:
                continue
            mx, my = self.mkts[mid]
            if (mx, my) in self._black:
                continue
            total = 0
            sellable = 0
            for pid, qty in prod_inv.items():
                price = mp.get(pid, 0)
                if price > 0:
                    total += qty * price
                    sellable += qty
            if sellable > 0 and total > best_total:
                best_total = total
                d = abs(self.x - mx) + abs(self.y - my)
                best = (mid, total, sellable, d)

        return best

    # ═══════════════════════════════════════════════════════════════════════════
    # 算力中心与科技
    # ═══════════════════════════════════════════════════════════════════════════

    def _nearest_closed_center(self):
        """返回最近的未占领算力中心。"""
        best_cid = None
        best_d = 999
        for cid, (cx, cy) in self.ctrs.items():
            if cid in self._occupied:
                continue
            if (cx, cy) in self._black:
                continue
            path = self._astar(cx, cy)
            if path is None:
                self._blacklist((cx, cy))
                continue
            d = abs(self.x - cx) + abs(self.y - cy)
            if d < best_d:
                best_d = d
                best_cid = cid
        return best_cid

    def _next_tech_to_buy(self, obs):
        """返回下一个应该购买的科技 TECH_x ID（mask 已检查），或 None。"""
        techs = self._techs_owned(obs)
        for key in TECH_PRIORITY:
            tid = TECH_KEY_TO_ID[key]
            idx = tid - TECH_0
            if techs[idx]:
                continue  # 已拥有
            # 检查失败次数（避免死循环）
            fails = self._tech_attempted.get(key, 0)
            if fails >= 5:
                continue
            return tid
        return None

    def _should_get_techs(self, obs, mask):
        """判断是否应该去工厂买科技。"""
        # 已经有 path_optimization → 不需要
        techs = self._techs_owned(obs)
        if techs[5] and techs[3]:
            return False
        if self._tech_attempted.get("path_optimization", 0) >= 5:
            return False
        # 在工厂且 mask 允许科技购买
        if not self._at_factory():
            return False
        tid = self._next_tech_to_buy(obs)
        if tid is None or not mask[tid]:
            return False
        return True

    # ═══════════════════════════════════════════════════════════════════════════
    # 主策略
    # ═══════════════════════════════════════════════════════════════════════════

    def _plan(self, obs, mask):
        self._blacklist_maintain()

        techs = self._techs_owned(obs)
        has_prod = self._has_prod(obs)
        prod_inv = self._prod_inv(obs)
        prod_qty = sum(prod_inv.values())
        has_path_opt = techs[5]
        cap = self._capacity(obs)

        # 站哪个市场旁？
        at_mid = self._which_market_adjacent()

        # ═══════════════════════════════════════════════════════════════════════
        # P0: 在买入市场 → 继续批量买入（直到满了或没钱了再走）
        # ═══════════════════════════════════════════════════════════════════════
        if has_prod and at_mid is not None and at_mid == self._last_buy_mid:
            # 在买入市场，检查能否继续买
            if mask[BUY] and prod_qty < cap and self.money >= 1.0:
                self._state = "ARB_BUY"
                self._set_goal(None, None, BUY)
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P1: 在非买入市场旁 → 卖出手持产品
        # ═══════════════════════════════════════════════════════════════════════
        if has_prod and at_mid is not None and at_mid != self._last_buy_mid:
            for pid in list(prod_inv.keys()):
                if mask[SELL_0 + pid]:
                    self._state = "SELL"
                    self._set_goal(None, None, SELL_0 + pid)
                    return
            best_sell = self._best_sell_market(obs, avoid_mid=self._last_buy_mid)
            if best_sell:
                self._state = "GO_SELL"
                self._set_goal(*self.mkts[best_sell[0]], None)
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P1.5: 在工厂 → 买科技或等待算力
        # ═══════════════════════════════════════════════════════════════════════
        if self._at_factory():
            tid = self._next_tech_to_buy(obs)
            if tid is not None and mask[tid]:
                self._state = "TECH"
                self._set_goal(None, None, tid)
                return
            # 还需要科技但算力不够→在工厂等待算力积累（避免第二趟往返）
            if tid is not None and self._occupied and not has_prod:
                self._state = "WAIT_COMPUTE"
                self._goal = None
                self._goal_act = None
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P2: 需要核心科技且有足够算力 → 回工厂（仅一次）
        # ═══════════════════════════════════════════════════════════════════════
        need_tech = (not techs[1]) or (not techs[5]) or (not techs[3])
        if need_tech and self.compute >= 90 and not self._at_factory():
            path = self._astar(0, 0)
            if path is not None and len(path) <= 20:
                self._state = "GO_FACTORY"
                self._set_goal(0, 0, None)
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P3: 有产品但不在市场 → 去最佳卖出市场
        # ═══════════════════════════════════════════════════════════════════════
        if has_prod and at_mid is None:
            avoid = self._last_buy_mid if self._has_bought else None
            best_sell = self._best_sell_market(obs, avoid_mid=avoid)
            if best_sell:
                self._state = "GO_SELL"
                self._set_goal(*self.mkts[best_sell[0]], None)
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P3: 在市场旁且空手 → 开始批量买入
        # ═══════════════════════════════════════════════════════════════════════
        if not has_prod and at_mid is not None and mask[BUY] and self.money >= 1.0:
            self._state = "ARB_BUY"
            self._last_buy_mid = at_mid
            self._has_bought = True
            self._set_goal(None, None, BUY)
            return

        # ═══════════════════════════════════════════════════════════════════════
        # P4: 未占领任何中心 → 主动去最近的计算中心（开局最关键！）
        # ═══════════════════════════════════════════════════════════════════════
        if not self._occupied and self.ctrs and not has_prod:
            cid = self._nearest_closed_center()
            if cid is not None:
                path = self._astar(*self.ctrs[cid])
                if path is not None:
                    self._state = "GO_CTR"
                    self._set_goal(*self.ctrs[cid], None)
                    return

        # ═══════════════════════════════════════════════════════════════════════
        # P6: 空手 → 找最佳套利对 → 去买入市场
        # ═══════════════════════════════════════════════════════════════════════
        if not has_prod and len(self.mkts) >= 2:
            arb = self._best_arbitrage(obs)
            if arb and arb[4] > 2:  # 降低门槛，微小利润也能积累
                bmid = arb[0]
                bx, by = self.mkts[bmid]
                self._state = "ARB_GO_BUY"
                self._set_goal(bx, by, None)
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P7: 有产品但没找到卖点 → 去最近非买入市场（兜底）
        # ═══════════════════════════════════════════════════════════════════════
        if has_prod and self.mkts:
            best_mid = None
            best_d = 999
            for mid, (mx, my) in self.mkts.items():
                if mid == self._last_buy_mid:
                    continue
                if (mx, my) in self._black:
                    continue
                d = abs(self.x - mx) + abs(self.y - my)
                if d < best_d:
                    best_d = d
                    best_mid = mid
            if best_mid is not None:
                self._state = "GO_SELL"
                self._set_goal(*self.mkts[best_mid], None)
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P8: 探索/去最近市场
        # ═══════════════════════════════════════════════════════════════════════
        if self.mkts:
            best_mid = None
            best_score = -999
            for mid, (mx, my) in self.mkts.items():
                if (mx, my) in self._black:
                    continue
                d = max(1, abs(self.x - mx) + abs(self.y - my))
                score = 50.0 / d
                if score > best_score:
                    best_score = score
                    best_mid = mid
            if best_mid is not None:
                self._state = "GO_MKT"
                self._set_goal(*self.mkts[best_mid], None)
                return

        # ═══════════════════════════════════════════════════════════════════════
        # P11: 探索未知区域
        # ═══════════════════════════════════════════════════════════════════════
        ep = self._explore()
        if ep:
            self._state = "EXPLORE"
            self._set_goal(*ep, None)
            return

        self._state = "IDLE"
        self._goal = None

    def _which_market_adjacent(self):
        """返回当前相邻的市场 id。"""
        for mid, (mx, my) in self.mkts.items():
            if abs(self.x - mx) + abs(self.y - my) <= 1:
                return mid
        return None

    # ═══════════════════════════════════════════════════════════════════════════
    # 主入口
    # ═══════════════════════════════════════════════════════════════════════════

    def get_action(self, observation):
        mask = self.env.action_masks()

        if self.mode == "rl" and self.policy_net is not None:
            self._read_obs(observation)
            self._check_stuck()
            if self._busy(observation):
                return WAIT
            device = next(self.policy_net.parameters()).device
            ot = torch.tensor(observation, dtype=torch.float32, device=device).unsqueeze(0)
            mt = torch.tensor(mask, dtype=torch.bool, device=device).unsqueeze(0)
            with torch.no_grad():
                return int(self.policy_net(ot, mt).argmax(dim=1).item())

        # 规则模式
        valid = np.where(mask)[0]
        self._read_obs(observation)
        self._check_stuck()

        if self._busy(observation):
            return WAIT

        # 只在非 busy 时学习障碍物
        self._learn_obs(mask)

        # ══ OCCUPY：最多占2个中心加速算力 ══
        if mask[OCCUPY] and len(self._occupied) < 2:
            self._state = "OCCUPY"
            self._set_goal(None, None, OCCUPY)
            self._replan = False
            self._goal_act = OCCUPY

        # 需要 replan？
        if self._goal_act is not None:
            if not mask[self._goal_act]:
                # 动作不再可用
                key = None
                for k, v in TECH_KEY_TO_ID.items():
                    if v == self._goal_act:
                        key = k
                        break
                if key:
                    self._tech_attempted[key] = self._tech_attempted.get(key, 0) + 1
                self._plan(observation, mask)
        elif self._replan or self._goal is None:
            self._plan(observation, mask)
        elif self._goal is not None and (self.x, self.y) == self._goal:
            self._plan(observation, mask)

        # 执行动作
        if self._goal_act is not None and mask[self._goal_act]:
            a = self._goal_act
            self._goal_act = None
            self._replan = True
        elif self._goal is not None:
            a = self._move_to(mask)
            if a is None:
                if (self.x, self.y) == self._goal:
                    # 到达目标但没本地动作 → replan
                    pass
                self._goal = None
                self._replan = True
                a = (list(set(valid) & MOVE_SET) or [WAIT])[0]
        else:
            # 无目标，尝试移动或等待
            a = (list(set(valid) & MOVE_SET) or [WAIT])[0]

        if a is None:
            a = WAIT
        self._cells.add((self.x, self.y))
        return a

    # ═══════════════════════════════════════════════════════════════════════════
    # 训练 + 持久化
    # ═══════════════════════════════════════════════════════════════════════════

    def train(self, total_timesteps=50000, save_dir="submission/", eval_freq=4096, **kwargs):
        os.makedirs(save_dir, exist_ok=True)
        scores = []
        best = -float("inf")
        obs = self.reset()
        for t in range(total_timesteps):
            a = self.get_action(obs)
            obs, r, term, trunc, info = self.step(a)
            if term or trunc:
                scores.append(info.get("score", 0))
                obs = self.reset()
            if (t + 1) % eval_freq == 0 and len(scores) >= 3:
                m = np.mean(scores[-3:])
                if m > best:
                    best = m
                    self.save(os.path.join(save_dir, "model_best.pt"))
        self.save(os.path.join(save_dir, "model_final.pt"))
        return {"total_timesteps": total_timesteps, "episodes": len(scores),
                "mean_score_last10": np.mean(scores[-10:]) if scores else 0,
                "best_mean_score": best}

    def save(self, path):
        d = {"mode": self.mode}
        if self.policy_net is not None:
            d["policy_net"] = self.policy_net.state_dict()
        torch.save(d, path)

    @classmethod
    def load(cls, path, env):
        d = torch.load(path, map_location="cpu", weights_only=False)
        a = cls(env, mode=d.get("mode", "rule"))
        if "policy_net" in d:
            a.policy_net = PolicyNet()
            a.policy_net.load_state_dict(d["policy_net"])
            a.policy_net.eval()
            a.policy_net.to("cuda" if torch.cuda.is_available() else "cpu")
            a.mode = "rl"
        return a
