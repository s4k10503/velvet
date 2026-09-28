"""Whether a pull request's head is a branch that has to outlive its merge.

A merge through `scripts/pr/settle.py` squashes and then deletes the head. For a maintenance line
merged forward, the squash leaves the line out of `main`'s ancestry, which is what
CONTRIBUTING.md's maintenance-line section says is read to know it was merged, and the delete
removes the line. `settle.py` and `refuse/merge_unchecked_against_base.py` both decide from here.
"""

import re
from urllib.parse import quote

# Held by name, whatever targets them: `upm` is the published mirror, which no pull request targets.
NAMED = frozenset({"main", "upm"})

# The naming CONTRIBUTING.md's maintenance-line section owns, read the way
# `report/unreleased_maintenance_line.py` reads it.
LINE = re.compile(r"[0-9]+\.x")


def by_name(branch):
    """Whether the name alone makes the branch long-lived, so no listing needs reading."""
    return branch in NAMED or bool(LINE.fullmatch(branch))


def targeted_path(branch):
    """The listing, below `repos/<owner>/<name>/`, of a pull request in any state based on `branch`.

    One entry is enough to answer, so one is asked for.
    """
    return f"pulls?state=all&base={quote(branch, safe='')}&per_page=1"


def reason(branch):
    """What a refusal says about a long-lived head, and how to land it instead."""
    return (f"its head {branch} is a long-lived branch: squashed, it drops out of the base's "
            f"ancestry, and deleted, it is gone. Land it as a merge commit that keeps the branch "
            f"(the web interface's \"Create a merge commit\"), as CONTRIBUTING.md's maintenance-line "
            f"section says")
