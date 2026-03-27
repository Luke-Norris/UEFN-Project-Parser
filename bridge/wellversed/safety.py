"""Write protection and undo safety utilities.

All destructive operations in the bridge must go through the safety
utilities in this module to ensure:

1. Engine / Epic content mounts are never written to.
2. Every write operation is wrapped in a ``ScopedEditorTransaction``
   so the user can always Ctrl+Z.
"""

from __future__ import annotations

from contextlib import contextmanager

import unreal

# ---------------------------------------------------------------------------
# Protected mount points -- never write to these
# ---------------------------------------------------------------------------

BLOCKED_MOUNTS = frozenset({
    "/Engine/",
    "/FortniteGame/",
    "/Fortnite/",
    "/Script/",
})


def check_path_safety(path: str) -> tuple[bool, str]:
    """Check whether an asset path is safe to modify.

    Parameters
    ----------
    path:
        Unreal asset path, e.g. ``/Game/MyPlugin/MyAsset``.

    Returns
    -------
    tuple[bool, str]
        ``(True, "ok")`` if the path is safe, or
        ``(False, reason)`` if the path is protected.
    """
    if not path:
        return False, "Empty path"

    for mount in BLOCKED_MOUNTS:
        if path.startswith(mount):
            return False, f"Blocked: {mount} is a protected mount point"

    return True, "ok"


@contextmanager
def safe_transaction(label: str):
    """Context manager wrapping operations in a ``ScopedEditorTransaction``.

    All actor mutations (spawn, delete, property changes, transforms) should
    be performed inside a ``safe_transaction`` block so the user can undo the
    entire operation as one step.

    Usage::

        with safe_transaction("Place 5 walls"):
            for i in range(5):
                spawn_wall(...)

    Parameters
    ----------
    label:
        Human-readable description shown in UEFN's undo history.
    """
    txn = unreal.ScopedEditorTransaction(label)
    try:
        yield txn
    finally:
        del txn


def require_safe_path(path: str):
    """Raise ``ValueError`` if *path* points to a protected mount.

    Convenience wrapper around :func:`check_path_safety` for use in command
    handlers that want to fail fast.
    """
    safe, reason = check_path_safety(path)
    if not safe:
        raise ValueError(reason)
