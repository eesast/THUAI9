import os
from stable_baselines3 import PPO
from stable_baselines3.common.env_checker import check_env
from stable_baselines3.common.callbacks import BaseCallback
from ai_gym_env import AI9GymEnv

class SimpleTestCallback(BaseCallback):
    def __init__(self, eval_env, check_freq=5000, verbose=1):
        super(SimpleTestCallback, self).__init__(verbose)
        self.check_freq = check_freq
        self.eval_env = eval_env

    def _on_step(self) -> bool:
        # self.n_calls 记录了 step 被调用的次数
        if self.n_calls % self.check_freq == 0:
            print(f"\n--- start test {self.n_calls} steps ---")
            
            # 运行一局完整的游戏
            obs, _ = self.eval_env.reset()
            total_reward = 0
            done = False
            
            # 这里简单跑一局
            while not done:
                # 使用当前模型预测
                action, _ = self.model.predict(obs, deterministic=True)
                obs, reward, terminated, truncated, _ = self.eval_env.step(action)
                total_reward += reward
                done = terminated or truncated
            
            # 获取环境里的一些具体信息 (需要你的Env支持访问 game 对象)
            # 注意：这里的 eval_env 可能是被 Monitor 包装过的，可能需要 .env 或 .unwrapped
            actual_env = self.eval_env.unwrapped 
            print(f"total Reward: {total_reward:.2f}, money: {actual_env.game.money:.2f}")
            print("------------------------------\n")
            
        return True

def train():
    # 1. 创建环境
    env = AI9GymEnv()
    eval_env = AI9GymEnv() # 单独的一个环境用于测试
    
    # 可选：检查环境是否符合 Gym 标准 (Debug 用)
    # check_env(env) 
    my_callback = SimpleTestCallback(eval_env, check_freq=4096) # 每4096步测一次

    # 2. 定义模型
    # MlpPolicy: 适用于向量输入的神经网络 (即我们 _encode_obs 返回的数组)
    model = PPO("MlpPolicy", env, verbose=1, 
                learning_rate=0.0003,
                n_steps=2048,
                batch_size=64,
                gamma=0.9,
                ent_coef=0.05,
                device="cpu")

    print("开始训练...")
    # 3. 训练 (time_steps 越大越强，建议 100,000 以上)
    model.learn(total_timesteps=50000, callback=my_callback)
    
    # 4. 保存模型
    model_path = "models/ppo_ai9_basic"
    os.makedirs(os.path.dirname(model_path), exist_ok=True)
    model.save(model_path)
    print(f"模型已保存至 {model_path}")

def test():
    # 测试训练好的模型
    env = AI9GymEnv()
    model = PPO.load("models/ppo_ai9_basic")
    
    obs, _ = env.reset()
    total_reward = 0
    
    for i in range(500):
        # deterministic=True 让模型输出最确定的动作，而不是随机探索
        action, _states = model.predict(obs, deterministic=True)
        print(f"Step {i+1}, Action: {action}, Market:{env.game._find_nearby_market(env.game.units[0])}, Money: {env.game.money:.2f}")
        obs, reward, terminated, truncated, info = env.step(action)
        total_reward += reward
        
        if (i+1) % 50 == 0:
            env.render()
            
        if terminated or truncated:
            break
            
    print(f"测试结束，总得分 (Reward): {total_reward:.2f}")

if __name__ == "__main__":
    train()
    # 训练完成后直接运行测试
    test()