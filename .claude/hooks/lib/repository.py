"""Locating the tree a session-start guard reports on, and reading git without raising.

A guard that cannot answer says nothing rather than failing: a hook writing a traceback into a
session start is noise a reader cannot act on, and every caller here already treats an unavailable
answer as "no report".
"""

import os
import subprocess
from pathlib import Path


def git(args, cwd, timeout=15):
    """Run git and return its stdout, or None when it could not answer."""
    try:
        result = subprocess.run(
            ["git", *args], cwd=cwd, capture_output=True, text=True, timeout=timeout,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    return result.stdout if result.returncode == 0 else None


def project_tree():
    """The checkout to report on, or None when there is no git repository to read.

    CLAUDE_PROJECT_DIR names the session's own project. Falling back to the working directory's
    toplevel keeps a guard useful when it is run by hand.
    """
    declared = os.environ.get("CLAUDE_PROJECT_DIR", "")
    tree = Path(declared) if declared else None
    if tree is None:
        toplevel = git(["rev-parse", "--show-toplevel"], cwd=Path.cwd())
        if toplevel is None:
            return None
        tree = Path(toplevel.strip())
    if not tree.is_dir() or git(["rev-parse", "--git-dir"], cwd=tree) is None:
        return None
    return tree
