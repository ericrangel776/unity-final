"""SessionStart hook - gives Claude a quick briefing when a session starts,
resumes, is cleared, or after compaction.

For SessionStart, whatever this script prints to stdout (exit 0) is added to
Claude's context. Keep it short: every line costs context in every session.
"""
import subprocess
import sys

sys.dont_write_bytecode = True  # keep __pycache__ out of the repo
from _common import project_dir, read_event

MAX_STATUS_LINES = 20
MAX_NOTES_CHARS = 2500


def git(root, *args: str) -> str:
    try:
        out = subprocess.run(
            ["git", *args], cwd=root, capture_output=True, check=False, text=True,
            encoding="utf-8", errors="replace", timeout=10,
        )
        return out.stdout.strip() if out.returncode == 0 else ""
    except (OSError, subprocess.TimeoutExpired):
        return ""


def main() -> int:
    event = read_event()
    root = project_dir(event)
    lines = ["## Session briefing (from .claude/hooks/session_context.py)"]

    branch = git(root, "branch", "--show-current")
    if branch:
        lines.append(f"- Branch: `{branch}`")
        if branch in ("main", "master"):
            lines.append("- You are on the main branch: create a feature branch before committing.")
        status = git(root, "status", "--short").splitlines()
        if status:
            shown = status[:MAX_STATUS_LINES]
            more = len(status) - len(shown)
            lines.append("- Uncommitted changes:\n" + "\n".join(f"    {s}" for s in shown)
                         + (f"\n    ... and {more} more" if more else ""))
        log = git(root, "log", "--oneline", "-5")
        if log:
            lines.append("- Recent commits:\n" + "\n".join(f"    {c}" for c in log.splitlines()))

    notes = root / "docs" / "notes" / "CURRENT.md"
    if notes.is_file():
        text = notes.read_text(encoding="utf-8", errors="replace").strip()
        if len(text) > MAX_NOTES_CHARS:
            text = text[:MAX_NOTES_CHARS] + "\n... (truncated, read the file for the rest)"
        lines.append(f"\n### In-progress notes (docs/notes/CURRENT.md)\n{text}")

    if len(lines) > 1:
        print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
