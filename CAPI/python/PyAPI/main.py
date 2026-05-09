from __future__ import annotations

import argparse
import importlib
import platform
import sys
from pathlib import Path
from typing import Callable, List


CURRENT_DIR = Path(__file__).resolve().parent
ROOT_DIR = CURRENT_DIR.parent
PROTO_DIR = ROOT_DIR / "proto"

for path in (ROOT_DIR, PROTO_DIR):
    path_str = str(path)
    if path_str not in sys.path:
        sys.path.append(path_str)

import PyAPI.structures as THUAI9  # noqa: E402
from PyAPI.Interface import IAI  # noqa: E402
from PyAPI.logic import Logic  # noqa: E402


def PrintWelcomeString() -> None:
    welcomeString = r"""
______________ ___  ____ ___  _____  .___ ________
\__    ___/   |   \|    |   \/  _  \ |   /   __   \
  |    | /    ~    \    |   /  /_\  \|   \____    /
  |    | \    Y    /    |  /    |    \   |  /    /
  |____|  \___|_  /|______/\____|__  /___| /____/
                \/                 \/
"""
    print(welcomeString)


def _infer_character_type(playerID: int) -> THUAI9.CharacterType:
    return {
        1: THUAI9.CharacterType.Robot,
        2: THUAI9.CharacterType.Drone,
        3: THUAI9.CharacterType.AutonomousCar,
    }.get(playerID, THUAI9.CharacterType.NullCharacterType)


def _build_ai_builder(ai_module_name: str) -> Callable[[int], IAI]:
    module = importlib.import_module(ai_module_name)
    ai_class = getattr(module, "AI", None)
    if ai_class is None:
        raise AttributeError(f"Module {ai_module_name!r} does not define class 'AI'")

    def create_ai(playerID: int) -> IAI:
        return ai_class(playerID)

    return create_ai


def THUAI9Main(argv: List[str], AIBuilder: Callable[[int], IAI]) -> None:
    parser = argparse.ArgumentParser(
        description="THUAI9 Python Interface Command Line Parameters"
    )
    parser.add_argument("-I", "--serverIP", type=str, default="127.0.0.1")
    parser.add_argument("-P", "--serverPort", type=str, default="8888")
    parser.add_argument("-t", "--teamID", type=int, required=True)
    parser.add_argument("-p", "--playerID", type=int, required=True)
    parser.add_argument(
        "-d",
        "--debug",
        action="store_true",
        help="Save debug log to ./logs",
        dest="file",
    )
    parser.add_argument(
        "-o",
        "--output",
        action="store_true",
        help="Print debug log to stdout",
        dest="screen",
    )
    parser.add_argument(
        "-w",
        "--warning",
        action="store_true",
        help="Only print warnings to stdout",
        dest="warnOnly",
    )
    parser.add_argument(
        "-s",
        "--side",
        type=int,
        required=False,
        choices=[0, 1],
        default=None,
        help="Optional side flag used by RegisterFactory",
    )
    parser.add_argument(
        "--aiModule",
        type=str,
        default="PyAPI.AI",
        help="Import path of the AI module, which must define class AI",
    )
    args = parser.parse_args(argv[1:])

    playerType = (
        THUAI9.PlayerType.Team
        if args.playerID == 0
        else THUAI9.PlayerType.Character
    )
    characterType = (
        THUAI9.CharacterType.NullCharacterType
        if playerType == THUAI9.PlayerType.Team
        else _infer_character_type(args.playerID)
    )

    if platform.system().lower() == "windows":
        PrintWelcomeString()

    aiBuilder = AIBuilder
    if args.aiModule != "PyAPI.AI":
        aiBuilder = _build_ai_builder(args.aiModule)

    logic = Logic(
        args.playerID,
        args.teamID,
        playerType,
        characterType,
        sideFlag=args.side,
    )
    logic.Main(
        aiBuilder,
        args.serverIP,
        args.serverPort,
        args.file,
        args.screen,
        args.warnOnly,
    )


def CreateAI(playerID: int) -> IAI:
    from PyAPI.AI import AI

    return AI(playerID)


if __name__ == "__main__":
    THUAI9Main(sys.argv, CreateAI)
