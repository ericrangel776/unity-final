"""PreToolUse hook (matcher: Edit|MultiEdit|Write|NotebookEdit).

Blocks edits to secrets, lockfiles, .git internals, and any extra globs listed
in config.json -> protected_paths. Exit code 2 blocks the edit and sends the
stderr message to Claude so it knows why and what to do instead.
"""
import fnmatch
import sys

sys.dont_write_bytecode = True  # keep __pycache__ out of the repo
from _common import load_config, project_dir, read_event, relpath

ALWAYS_PROTECTED = [
    # (glob, reason)
    (".env", "Secrets file. Edit .env.example and tell the user what to add."),
    ("**/.env", "Secrets file. Edit .env.example and tell the user what to add."),
    (".env.*", "Secrets file. Edit .env.example and tell the user what to add."),
    ("**/.env.*", "Secrets file. Edit .env.example and tell the user what to add."),
    ("**/*.pem", "Private key / certificate."),
    ("**/*.key", "Private key."),
    ("**/id_rsa*", "SSH key."),
    ("**/id_ed25519*", "SSH key."),
    (".git/**", "Git internals. Use git commands instead."),
    ("**/package-lock.json", "Lockfile. Change dependencies with the package manager."),
    ("**/pnpm-lock.yaml", "Lockfile. Change dependencies with the package manager."),
    ("**/yarn.lock", "Lockfile. Change dependencies with the package manager."),
    ("**/poetry.lock", "Lockfile. Change dependencies with the package manager."),
    ("**/uv.lock", "Lockfile. Change dependencies with the package manager."),
    ("**/packages.lock.json", "Lockfile. Change dependencies with the package manager."),
]
ALLOWED_EXCEPTIONS = (".env.example", ".env.sample", ".env.template")


def matches(rel: str, pattern: str) -> bool:
    # fnmatch's "*" already crosses "/", so "**/x" should also match a top-level "x".
    if fnmatch.fnmatch(rel, pattern):
        return True
    if pattern.startswith("**/") and fnmatch.fnmatch(rel, pattern[3:]):
        return True
    # Fallback when the path could not be made relative to the project root
    # (e.g. C:/Users/me/repo/migrations/x.sql): match the glob at any depth.
    is_absolute = ":" in rel[:3] or rel.startswith("/")
    return is_absolute and fnmatch.fnmatch(rel, "*/" + pattern)


def main() -> int:
    event = read_event()
    tool_input = event.get("tool_input") or {}
    path = tool_input.get("file_path") or tool_input.get("notebook_path")
    if not path:
        return 0

    rel = relpath(path, project_dir(event))
    if rel.rsplit("/", 1)[-1] in ALLOWED_EXCEPTIONS:
        return 0

    rules = list(ALWAYS_PROTECTED)
    for glob in load_config().get("protected_paths", []):
        rules.append((glob, "Listed in .claude/hooks/config.json protected_paths."))

    for pattern, reason in rules:
        if matches(rel, pattern):
            sys.stderr.write(
                f"Blocked by protect_files hook: '{rel}' is protected ({pattern}).\n"
                f"{reason}\n"
                "If this edit is really required, explain why and ask the user to make it.\n"
            )
            return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
