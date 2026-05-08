import os
import sys
import msvcrt
import time
from main import GameEnv, Point
from config.setting import *

# 键位映射 (Bytes for msvcrt)
KEY_MAPPINGS = {
    b'w': U_ACT_MOVE_UP,
    b's': U_ACT_MOVE_DOWN,
    b'a': U_ACT_MOVE_LEFT,
    b'd': U_ACT_MOVE_RIGHT,
    b' ': U_ACT_WAIT,      # 空格: 待命/消耗时间
    b'b': U_ACT_LOAD_0,    # B: 购买
    b'n': U_ACT_SELL_ALL   # N: 全部卖出
}

def clear():
    """清屏命令，兼容 Windows"""
    os.system('cls' if os.name == 'nt' else 'clear')

def print_game(env, obs):
    clear()
    t = obs["time"]
    u = obs["units"][0]
    
    # --- 状态栏 ---
    print(f"=== TURN: {t} | CASH: ${env.money:.2f} ===")
    
    busy_status = f"(BUSY: {u.busy_ticks}t)" if u.busy_ticks > 0 else "(READY)"
    print(f"UNIT | Pos: ({u.pos.x}, {u.pos.y}) | State: {u.state} {busy_status}")
    
    inv_str = ", ".join([f"{PRODUCTS[pid]['name']}: {c}" for pid, c in u.inventory.items() if c > 0])
    print(f"BAG  | [{inv_str if inv_str else 'Empty'}]")
    
    # --- 市场信息 ---
    nearby_market = None
    for m in obs["markets"]:
        if u.pos.distance_to(m.pos) <= 1:
            nearby_market = m
            break
            
    if nearby_market:
        print(f"\n[MARKET NEARBY: {nearby_market.name}]")
        for pid, p_conf in PRODUCTS.items():
            price = nearby_market.get_price(pid, t)
            print(f"  > {p_conf['name']}: ${price:.2f}")
    else:
        print("\n[NO MARKET NEARBY]")

    # --- 操作指南 ---
    print("-" * 50)
    print("CONTROLS: [WASD] Move  [Space] Wait  [B] Buy  [N] Sell")
    print("SYSTEM  : [R] Reset    [Q] Quit")
    print("-" * 50)
    
    # --- 局部地图渲染 (15x15 视野) ---
    radius = 7
    min_r, max_r = max(0, u.pos.x - radius), min(MAP_HEIGHT, u.pos.x + radius + 1)
    min_c, max_c = max(0, u.pos.y - radius), min(MAP_WIDTH, u.pos.y + radius + 1)
    
    print(f"MAP VIEW ({min_r}-{max_r}, {min_c}-{max_c}):")
    for r in range(min_r, max_r):
        line = ""
        for c in range(min_c, max_c):
            char = ". "
            
            # 渲染市场
            for m in obs["markets"]:
                if m.pos.x == r and m.pos.y == c:
                    char = "M "
                    break
            
            # 渲染单位 (如果在市场上方则显示 *)
            if u.pos.x == r and u.pos.y == c:
                char = "U " if char == ". " else "U*" 
            
            line += char
        print(line)

def main():
    print("Initializing...")
    env = GameEnv()
    obs = env.reset()
    
    print_game(env, obs)
    
    while True:
        # 阻塞式读取按键
        key = msvcrt.getch().lower()
        cmd = None
        
        if key == b'q':
            print("Quitting...")
            break
        elif key == b'r':
            print("Resetting...")
            obs = env.reset()
            print_game(env, obs)
            continue
        elif key in KEY_MAPPINGS:
            cmd = KEY_MAPPINGS[key]
        
        # 执行指令（如果是无效按键则忽略，不消耗 tick）
        if cmd is not None:
            # 如果单位忙碌，任何指令都视为“等待/流逝时间”
            # GameEnv.step 内部会自动处理忙碌状态的倒计时
            obs = env.step(cmd)
            print_game(env, obs)

if __name__ == "__main__":
    main()