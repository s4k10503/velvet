#!/usr/bin/env bash
# Reports what past work left behind, once there is enough of it to be worth a sweep.
#
# Nothing removes any of it, and that is deliberate: a branch with commits that never landed looks
# exactly like one whose pull request squash-merged, and only the pull request's state or a commit
# count tells them apart. A hook that deleted on a count would have destroyed a demo branch holding
# 1271 unlanded lines. So this counts and hands over the commands.
#
# The scratch probe is bounded at depth 5 and looks for a marker directory rather than measuring
# size: `du` over these trees took 7.5 s, which is too long to spend at the start of every session.
#
# Exit 0 always.

set -uo pipefail

BRANCH_LIMIT="${VELVET_LITTER_BRANCHES:-20}"
CLONE_LIMIT="${VELVET_LITTER_CLONES:-3}"

command -v git >/dev/null 2>&1 || exit 0

tree="${CLAUDE_PROJECT_DIR:-}"
if [ -z "$tree" ]; then
  tree=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
fi
cd "$tree" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

branches=$(git branch --format='%(refname:short)' 2>/dev/null | grep -cv '^main$' || true)
prunable=$(git worktree list --porcelain 2>/dev/null | grep -c '^prunable' || true)
refs=$(git for-each-ref --format='%(refname)' 'refs/remotes/pr/*' 2>/dev/null | grep -c . || true)

clones=$(find /private/tmp/claude-* -maxdepth 5 -name ProjectSettings -type d 2>/dev/null | grep -c . || true)

report=""
[ "${branches:-0}" -gt "$BRANCH_LIMIT" ] && report="$report
  $branches local branches besides main"
[ "${prunable:-0}" -gt 0 ] && report="$report
  $prunable worktree(s) whose directory is gone"
[ "${refs:-0}" -gt 0 ] && report="$report
  $refs pr/* refs left by \`gh pr checkout\`"
[ "${clones:-0}" -gt "$CLONE_LIMIT" ] && report="$report
  $clones project clones under /private/tmp/claude-*"

[ -n "$report" ] || exit 0

cat <<EOF
Past work left this behind:
$report

A branch whose pull request merged is litter; one with commits that never landed is not, and the
two look identical from here. Ask the pull requests, then delete only what they name:

  gh pr list --state merged --limit 400 --json headRefName --jq '.[].headRefName' | sort -u > /tmp/merged-heads
  for b in \$(git branch --format='%(refname:short)' | grep -v '^main\$'); do
    grep -qx "\$b" /tmp/merged-heads && git branch -D "\$b"
  done

For each branch that survives that, \`git rev-list --count origin/main..<branch>\` says whether it
holds anything. A verification branch counts as spent once its finding is pinned by a guard on main.

  git worktree prune
  git for-each-ref --format='%(refname)' 'refs/remotes/pr/*' | xargs -n1 git update-ref -d
  git gc --prune=now

A project clone is a checkout plus its Library, so they are the large ones — five of them held
10 GB here. Those belonging to a finished session are spent; the one under this session's own
scratch directory is not:

  find /private/tmp/claude-* -maxdepth 5 -name ProjectSettings -type d | sed 's|/ProjectSettings\$||'
EOF
exit 0
