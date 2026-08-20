#!/usr/bin/env python3
"""Report what past work left behind, once there is enough of it to be worth a sweep.

Nothing removes any of it, and that is deliberate: a branch with commits that never landed looks
exactly like one whose pull request squash-merged, and only the pull request's state or a commit
count tells them apart. A hook that deleted on a count would have destroyed a demo branch holding
1271 unlanded lines. So this counts and hands over the commands.

The scratch probe is bounded at depth 5 and looks for a marker directory rather than measuring size:
`du` over these trees took 7.5 s, which is too long to spend at the start of every session.

Exit 0 always.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))

from repository import git, project_tree  # noqa: E402

DEFAULT_BRANCH_LIMIT = 20
DEFAULT_CLONE_LIMIT = 3

GUIDANCE = """
A branch whose pull request merged is litter; one with commits that never landed is not, and the
two look identical from here. Ask the pull requests, then delete only what they name:

  gh pr list --state merged --limit 400 --json headRefName --jq '.[].headRefName' | sort -u > /tmp/merged-heads
  for b in $(git branch --format='%(refname:short)' | grep -v '^main$'); do
    grep -qx "$b" /tmp/merged-heads && git branch -D "$b"
  done

For each branch that survives that, `git rev-list --count origin/main..<branch>` says whether it
holds anything main does not — which is 0 for a maintenance line cut from main as much as for a
spent branch. `gh pr list --base <branch>` separates them: a branch other pull requests target is
somebody's base. A verification branch counts as spent once its finding is pinned by a guard on main.

  git worktree prune
  git for-each-ref --format='%(refname)' 'refs/remotes/pr/*' | xargs -n1 git update-ref -d
  git gc --prune=now

A project clone is a checkout plus its Library, so they are the large ones — five of them held
10 GB here. Those belonging to a finished session are spent; the one under this session's own
scratch directory is not:

  find /private/tmp/claude-* -maxdepth 5 -name ProjectSettings -type d | sed 's|/ProjectSettings$||'
"""


def limit(name, fallback):
    declared = os.environ.get(name, "")
    return int(declared) if declared.isdigit() else fallback


def lines(output):
    return [line for line in (output or "").splitlines() if line.strip()]


def clone_count():
    """Project clones under the session scratch roots, found by their ProjectSettings marker.

    Rooted at each scratch directory rather than at their parent, so the walk covers what the
    marker search needs and no more — the depth bound is what keeps this off a session's critical
    path, and widening the root widens the walk with it.
    """
    roots = sorted(str(path) for path in Path("/private/tmp").glob("claude-*"))
    if not roots:
        return 0
    try:
        found = subprocess.run(
            ["find", *roots, "-maxdepth", "5", "-name", "ProjectSettings", "-type", "d"],
            capture_output=True, text=True, timeout=30,
        ).stdout
    except (OSError, subprocess.SubprocessError):
        return 0
    return len(lines(found))


def main():
    if shutil.which("git") is None:
        return 0
    tree = project_tree()
    if tree is None:
        return 0

    branches = [b for b in lines(git(["branch", "--format=%(refname:short)"], tree)) if b != "main"]
    prunable = [w for w in lines(git(["worktree", "list", "--porcelain"], tree))
                if w.startswith("prunable")]
    refs = lines(git(["for-each-ref", "--format=%(refname)", "refs/remotes/pr/*"], tree))
    clones = clone_count()

    found = []
    if len(branches) > limit("VELVET_LITTER_BRANCHES", DEFAULT_BRANCH_LIMIT):
        found.append(f"  {len(branches)} local branches besides main")
    if prunable:
        found.append(f"  {len(prunable)} worktree(s) whose directory is gone")
    if refs:
        found.append(f"  {len(refs)} pr/* refs left by `gh pr checkout`")
    if clones > limit("VELVET_LITTER_CLONES", DEFAULT_CLONE_LIMIT):
        found.append(f"  {clones} project clones under /private/tmp/claude-*")

    if not found:
        return 0

    print("Past work left this behind:")
    print("\n" + "\n".join(found))
    print(GUIDANCE, end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
