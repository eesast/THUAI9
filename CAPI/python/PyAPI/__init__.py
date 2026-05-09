from __future__ import annotations

import sys
from pathlib import Path


_PROTO_DIR = Path(__file__).resolve().parent.parent / "proto"
if _PROTO_DIR.is_dir():
    proto_dir = str(_PROTO_DIR)
    if proto_dir not in sys.path:
        sys.path.append(proto_dir)
