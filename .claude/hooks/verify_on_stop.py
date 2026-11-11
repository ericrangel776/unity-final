"""Stop hook - Claude cannot finish while the project check is failing.

Flow when Claude tries to end its turn:
  1. No check_command configured in config.json  -> allow stop.
  2. Git repo with no uncommitted changes          -> allow stop (nothing to verify).
  3. Run check_command.
       passes -> allow stop, reset the retry counter.
       fails  -> exit 2: Claude is sent back to work with the failure output.
  4. After max_stop_retries consecutive failures, allow the stop anyway and tell
     Claude to report the failure to the user (prevents an endless loop).

This is the "give Claude a way to verify its work" tip, made deterministic.
"""
import subprocess
import sys
import tempfile
from pathlib import Path

sys.dont_write_bytecode = True  # keep __pycache__ out of the repo
from _common import load_config, project_dir, read_event

TAIL_LINES = 60


def has_uncommitted_changes(root: Path) -> bool:
    try:
        out = subprocess.run(
            ["git", "status", "--porcelain"], cwd=root,
            capture_output=True, check=False, text=True, timeout=15,
        )
    except (OSError, subprocess.TimeoutExpired):
        return True  # not sure -> verify
    if out.returncode != 0:
        return True  # not a git repo -> always verify
    return bool(out.stdout.strip())


def main() -> int:
    event = read_event()
    cfg = load_config()
    command = (cfg.get("check_command") or "").strip()
    if not command:
        return 0

    root = project_dir(event)
    if not has_uncommitted_changes(root):
        return 0

    session = event.get("session_id", "default")
    counter = Path(tempfile.gettempdir()) / f"claude-verify-{session}.count"
    failures = int(counter.read_text()) if counter.exists() else 0

    try:
        result = subprocess.run(
            command, shell=True, cwd=root, capture_output=True, check=False, text=True,
            encoding="utf-8", errors="replace",
            timeout=int(cfg.get("check_timeout_seconds", 300)),
        )
        passed = result.returncode == 0
        output = result.stdout + result.stderr
    except subprocess.TimeoutExpired:
        passed, output = False, f"Check timed out: {command}"

    if passed:
        counter.unlink(missing_ok=True)
        return 0

    failures += 1
    max_retries = int(cfg.get("max_stop_retries", 3))
    tail = "\n".join(output.strip().splitlines()[-TAIL_LINES:])

    if failures > max_retries:
        counter.unlink(missing_ok=True)
        # Exit 0 lets Claude stop; the message is only logged, so the rule in
        # CLAUDE.md ("report failures honestly") covers telling the user.
        return 0

    counter.write_text(str(failures))
    sys.stderr.write(
        f"Verification failed ({failures}/{max_retries}): `{command}`\n"
        f"--- last {TAIL_LINES} lines ---\n{tail}\n---\n"
        "Fix the cause, then finish again. If the failure is unrelated to your change "
        "or cannot be fixed, stop changing code and explain that to the user with the output.\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
