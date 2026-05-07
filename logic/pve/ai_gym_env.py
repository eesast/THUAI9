import gymnasium as gym
import numpy as np
from gymnasium import spaces
from main import GameEnv
from config.setting import *

class AI9GymEnv(gym.Env):
    """
    改进后的 AI9 环境：
    - 状态包含所有市场信息 + 时间
    - 奖励直接使用 money_diff
    """
    metadata = {"render_modes": ["console"]}

    def __init__(self):
        super().__init__()
        self.game = GameEnv()
        
        # 动作空间
        self.action_space = spaces.Discrete(7)

        # 观察空间：自身 + 所有市场 + 时间相位
        # 2 (pos) + 2 (busy, holding) + 1 money + 3*3 (市场dx, dy, price) + 1 时间
        self.observation_space = spaces.Box(low=-1.0, high=1.0, shape=(15,), dtype=np.float32)
        
        self.max_steps = 1000
        self.prev_money = 0
        self.current_step = 0
        self.invest = 0

    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        obs_dict = self.game.reset()
        self.current_step = 0
        self.prev_money = self.game.money
        return self._encode_obs(obs_dict), {}

    def step(self, action):
        observation = self.game.step(action)
        self.current_step += 1
        
        reward = 0.0
        # --- reward ---
        money_diff = self.game.money - self.prev_money
        if money_diff < 0:
            self.invest += -money_diff
        if money_diff > 0:
            reward += (self.game.money - self.prev_money - self.invest) * 0.1 # 销售奖励
            self.invest = 0
        reward -= 0.005       # 小时间惩罚
        
        self.prev_money = self.game.money
        
        terminated = False
        truncated = self.current_step >= self.max_steps
        
        return self._encode_obs(observation), reward, terminated, truncated, {}

    def _encode_obs(self, obs_raw):
        """状态向量：自身 + 所有市场 + 时间"""
        u = self.game.units[0]
        features = []

        # 1. 自身位置
        features.append(u.pos.x / MAP_HEIGHT)
        features.append(u.pos.y / MAP_WIDTH)
        
        # 2. 状态
        features.append(u.busy_ticks / 10.0)
        item_count = sum(u.inventory.values())
        features.append(item_count / UNIT_CAPACITY)
        
        # 3. 资金 (0~1)
        money_norm = np.log10(max(1, self.game.money)) / 4.0
        features.append(money_norm)
        
        # 4. 所有市场信息 (dx, dy, price)
        for m in self.game.markets:
            dx = (m.pos.x - u.pos.x) / MAP_HEIGHT
            dy = (m.pos.y - u.pos.y) / MAP_WIDTH
            price = m.get_price(PRODUCT_SEMICONDUCTOR, self.game.time)
            norm_price = (price - 40) / (120 - 40)  # 归一化
            features.extend([dx, dy, norm_price])
        
        # 5. 时间相位（归一化）
        time_phase = (self.game.time % 100) / 100.0
        features.append(time_phase)
        
        return np.array(features, dtype=np.float32)

    def render(self):
        print(f"Step: {self.current_step}, Money: {self.game.money:.2f}")