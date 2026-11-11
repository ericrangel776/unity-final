"""PostToolUse hook (matcher: Edit|MultiEdit|Write).

After every edit:
  1. Format the file (config.json -> format_commands). Silent; never blocks.
  2. Lint the file (config.json -> lint_commands). If the linter fails, exit 2
     so its output is fed back to Claude, which then fixes the issue immediately.
     (PostToolUse cannot undo the edit; exit 2 just reports to Claude.)

Tools that are not installed are skipped, so the same config works everywhere.
"""
from __future__ import annotations

import shlex
import shutil
import subprocess
import sys
from pathlib import Path

sys.dont_write_bytecode = True  # keep __pycache__ out of the repo
from _common import load_config, project_dir, read_event

MAX_OUTPUT_CHARS = 3000


def build_cmd(template: str, file_path: str) -> list[str] | None:
    """Turn 'ruff format {file}' into an argv list. None if the tool is missing."""
    parts = shlex.split(template)
    if not parts:
        return None
    exe = shutil.which(parts[0])  # resolves npx -> npx.cmd on Windows
    if exe is None:
        return None
    return [exe] + [p.replace("{file}", file_path) for p in parts[1:]]


def run(cmd: list[str], cwd: Path) -> subprocess.CompletedProcess | None:
    try:
        return subprocess.run(
            cmd, cwd=cwd, capture_output=True, check=False, text=True,
            encoding="utf-8", errors="replace", timeout=45,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None


def main() -> int:
    event = read_event()
    file_path = (event.get("tool_input") or {}).get("file_path")
    if not file_path or not Path(file_path).is_file():
        return 0

    cfg = load_config()
    ext = Path(file_path).suffix.lower()
    root = project_dir(event)

    fmt = cfg.get("format_commands", {}).get(ext)
    if fmt and (cmd := build_cmd(fmt, file_path)):
        run(cmd, root)  # best effort

    lint = cfg.get("lint_commands", {}).get(ext)
    if lint and (cmd := build_cmd(lint, file_path)):
        result = run(cmd, root)
        if result is not None and result.returncode != 0:
            output = (result.stdout + result.stderr).strip()[-MAX_OUTPUT_CHARS:]
            sys.stderr.write(
                f"Lint failed for {file_path} ({lint}). Fix these before moving on:\n{output}\n"
            )
            return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
