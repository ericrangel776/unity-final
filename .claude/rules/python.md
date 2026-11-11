---
paths:
  - "**/*.py"
---

# Python rules (loaded only when touching .py files)

<!-- Example of a path-scoped rule. Edit to match your project, or delete. -->
- Type hints on all public functions. Use `X | None`, not `Optional[X]`.
- Use `pathlib.Path`, not `os.path`.
- Use `logging`, not `print`, outside of CLI entry points and scripts.
- Read secrets and config from environment variables via one config module; never hard-code keys.
- Raise specific exceptions; never `except Exception: pass`.
