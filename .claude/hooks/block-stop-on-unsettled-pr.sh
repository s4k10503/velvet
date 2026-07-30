#!/usr/bin/env bash
# Blocks Stop while an open PR still has a check that has not reached a terminal state.
#
# A run that is already green is not the failure this guards. The failure is the assistant
# reading a check list once, seeing "pending", turning to something else, and never looking
# again — twice in one session, for 7h45m and for 33m, the second time after writing the rule
# that was supposed to prevent it. An intention to re-check is not a mechanism; a Stop that
# refuses to return is.
#
# The judgement is two-stage on purpose. An empty check list is NOT "still running": it means
# no workflow was ever triggered for that SHA, which is what a cancelled run followed by a
# force-push leaves behind, and reading it as pending is how the 7h45m gap started. So a PR
# with zero checks blocks too, with a different reason.
#
# Exit 2 with output on stderr is what Stop reads as "do not stop, here is why".

set -uo pipefail

command -v gh >/dev/null 2>&1 || exit 0
command -v jq >/dev/null 2>&1 || exit 0

prs=$(gh pr list --state open --json number --jq '.[].number' 2>/dev/null) || exit 0
[ -z "$prs" ] && exit 0

blocked=""
for pr in $prs; do
  checks=$(gh pr checks "$pr" --json name,bucket 2>/dev/null || echo "[]")

  count=$(echo "$checks" | jq 'length' 2>/dev/null || echo 0)
  if [ "$count" = "0" ]; then
    # Zero checks is legitimate when nothing the PR touches matches a workflow's path filter —
    # a docs-only or .claude/-only change reports none and is ready to merge. It is a problem
    # when the head never got a run it should have, and the tell is the merge state: a
    # conflicting PR reports DIRTY (or UNKNOWN while GitHub is still computing) and never
    # starts CI at all, which is the shape that went unwatched for seven hours.
    state=$(gh pr view "$pr" --json mergeStateStatus --jq '.mergeStateStatus' 2>/dev/null || echo "")
    case "$state" in
      CLEAN|UNSTABLE|BEHIND|BLOCKED|"")
        continue
        ;;
    esac
    blocked="$blocked
  PR #$pr — no checks reported and merge state is $state. A conflicting PR never starts CI, so
    this is not 'still running'. Rebase it, or check the head SHA against
    'gh run list --branch <b> --json headSha' if you expected a run."
    continue
  fi

  pending=$(echo "$checks" | jq '[.[] | select(.bucket == "pending")] | length' 2>/dev/null || echo 0)
  if [ "$pending" != "0" ]; then
    names=$(echo "$checks" | jq -r '[.[] | select(.bucket == "pending") | .name] | join(", ")' 2>/dev/null)
    blocked="$blocked
  PR #$pr — $pending check(s) still pending: $names"
  fi
done

[ -z "$blocked" ] && exit 0

cat >&2 <<EOF
Do not stop: an open PR has not settled.
$blocked

Either wait for it with a Monitor that emits on both pass and fail, or keep working on
something that is itself on the critical path. Work that is off the critical path satisfies
"do not idle" while the thing you are actually waiting on goes unwatched.
EOF
exit 2
