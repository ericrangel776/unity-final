"""Shared helpers for the project hooks.

Every hook receives one JSON object on stdin describing the event. Common fields:
  session_id, cwd, hook_event_name, and for tool events tool_name + tool_input.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

HOOKS_DIR = Path(__file__).resolve().parent


def read_event() -> dict:
    """Parse the JSON event from stdin. Returns {} if it is missing or malformed."""
    try:
        raw = sys.stdin.buffer.read().decode("utf-8")
        return json.loads(raw) if raw.strip() else {}
    except (json.JSONDecodeError, UnicodeDecodeError):
        return {}


def project_dir(event: dict | None = None) -> Path:
    """Project root: $CLAUDE_PROJECT_DIR, else the event cwd, else two levels up."""
    env = os.environ.get("CLAUDE_PROJECT_DIR")
    if env:
        return Path(env)
    if event and event.get("cwd"):
        return Path(event["cwd"])
    return HOOKS_DIR.parent.parent


def load_config() -> dict:
    """Read config.json next to the hooks, ignoring keys that start with '_'."""
    try:
        data = json.loads((HOOKS_DIR / "config.json").read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    return {k: v for k, v in data.items() if not k.startswith("_")}


def relpath(path: str, root: Path) -> str:
    """Path relative to the project root with forward slashes (for glob matching)."""
    p = Path(path)
    try:
        p = p.resolve().relative_to(root.resolve())
    except (ValueError, OSError):
        pass
    return p.as_posix()
