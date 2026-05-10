"""
Visualization utilities: ASCII game board rendering and reward curve plotting.
"""
from __future__ import annotations
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from GameLogic import GameEnvironment
from GameLogic.config import (
    CELL_EMPTY, CELL_OBSTACLE, CELL_MARKET,
    CELL_RESOURCE, CELL_COMPUTE, CELL_FACTORY,
)

_CELL_CHARS = {
    CELL_EMPTY:    ".",
    CELL_OBSTACLE: "#",
    CELL_MARKET:   "M",
    CELL_RESOURCE: "R",
    CELL_COMPUTE:  "C",
    CELL_FACTORY:  "F",
}


def render_board(env: GameEnvironment) -> str:
    """Render the game board as ASCII art."""
    board = env.board
    unit = env.unit
    grid = [row[:] for row in board.grid]  # shallow copy for display

    # Overlay depleted resource marks
    for rp in board.resource_points:
        if rp.depleted:
            grid[rp.x][rp.y] = CELL_OBSTACLE

    lines = []
    header = (
        f"t={env.time:.1f}s  step={env._step}  "
        f"money=${env.money:.0f}  score={env.score:.0f}  "
        f"compute={env.compute:.0f}"
    )
    lines.append(header)
    lines.append("  " + " ".join(str(c % 10) for c in range(board.W)))

    for r in range(board.H):
        row_chars = []
        for c in range(board.W):
            if r == unit.x and c == unit.y:
                row_chars.append("U")
            else:
                row_chars.append(_CELL_CHARS.get(grid[r][c], "?"))
        lines.append(f"{r % 10} " + " ".join(row_chars))

    inv = f"  inv: raw={unit.raw_inv:.0f}  prod={sum(unit.prod_inv.values()):.0f}"
    lines.append(inv)
    return "\n".join(lines)


def plot_rewards(rewards: list, title: str = "Episode Rewards", window: int = 20):
    """Plot training rewards (requires matplotlib)."""
    try:
        import matplotlib.pyplot as plt
        import numpy as np
    except ImportError:
        print("[plot_rewards] matplotlib not installed; skipping plot")
        return

    fig, axes = plt.subplots(1, 2, figsize=(12, 4))
    axes[0].plot(rewards, alpha=0.4, label="raw")

    if len(rewards) >= window:
        smoothed = np.convolve(rewards, np.ones(window) / window, mode="valid")
        x = range(window - 1, len(rewards))
        axes[0].plot(x, smoothed, label=f"MA{window}")
    axes[0].set_title(title)
    axes[0].set_xlabel("Episode")
    axes[0].set_ylabel("Reward")
    axes[0].legend()

    axes[1].hist(rewards[-200:], bins=30)
    axes[1].set_title("Reward distribution (last 200 eps)")
    axes[1].set_xlabel("Reward")

    plt.tight_layout()
    plt.savefig("training_rewards.png", dpi=120)
    print("[plot_rewards] saved to training_rewards.png")
    plt.show()
