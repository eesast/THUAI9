from __future__ import annotations

import argparse
import subprocess
import sys
import time
from pathlib import Path
from typing import List


def main() -> None:
    parser = argparse.ArgumentParser(description="Launch multiple THUAI9 Python clients")
    parser.add_argument("-I", "--serverIP", type=str, default="127.0.0.1")
    parser.add_argument("-P", "--serverPort", type=str, default="8888")
    parser.add_argument("--teams", nargs="+", type=int, default=[1, 2, 3, 4])
    parser.add_argument("--players", nargs="+", type=int, default=[0, 1, 2, 3])
    parser.add_argument("--python", type=str, default=sys.executable)
    parser.add_argument("--delay", type=float, default=0.5)
    parser.add_argument("-d", "--debug", action="store_true")
    parser.add_argument("-o", "--output", action="store_true")
    parser.add_argument("-w", "--warning", action="store_true")
    args = parser.parse_args()

    root = Path(__file__).resolve().parent
    main_py = root / "PyAPI" / "main.py"

    extra_flags: List[str] = []
    if args.debug:
        extra_flags.append("-d")
    if args.output:
        extra_flags.append("-o")
    if args.warning:
        extra_flags.append("-w")

    processes: List[subprocess.Popen] = []
    for team in args.teams:
        for player in args.players:
            cmd = [
                args.python,
                str(main_py),
                "-I",
                args.serverIP,
                "-P",
                args.serverPort,
                "-t",
                str(team),
                "-p",
                str(player),
                *extra_flags,
            ]
            processes.append(subprocess.Popen(cmd, cwd=root))
            time.sleep(args.delay)

    try:
        for process in processes:
            process.wait()
    finally:
        for process in processes:
            if process.poll() is None:
                process.terminate()


if __name__ == "__main__":
    main()
