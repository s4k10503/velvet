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
# A PR whose checks have all passed and that nobody has merged is the state this exists for as
# much as a pending one. It reads as "settled", and settled is what an assistant stops on — eight
# green PRs sat unmerged for nine hours behind a watcher that was alive, emitting nothing, because
# it only reported checks CHANGING state and every check had finished changing. A live watcher with
# nothing left to say is indistinguishable from progress.
#
# A pending check is forgiven while a watcher is demonstrably alive. The heartbeat is written by
# the watching process itself on each poll, never by the assistant, so it cannot be satisfied by
# intending to watch — if the watcher dies the file goes stale within one poll and this blocks
# again. The watcher must re-enumerate open PRs every cycle, or a PR opened after it started would
# be unwatched while the heartbeat still looked fresh. Zero checks is judged without the heartbeat:
# there, nothing is running for a watcher to observe.
#
# Exit 2 with output on stderr is what Stop reads as "do not stop, here is why".

set -uo pipefail

command -v gh >/dev/null 2>&1 || exit 0
command -v jq >/dev/null 2>&1 || exit 0

HEARTBEAT="$HOME/.velvet-pr-watch.heartbeat"
# Three polls of the 60s cycle, so one slow `gh` call does not read as a dead watcher.
STALE_AFTER=180

# shellcheck source=lib/deferrals.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/deferrals.sh"

watcher_is_alive() {
  [ -f "$HEARTBEAT" ] || return 1
  local beat
  beat=$(cat "$HEARTBEAT" 2>/dev/null) || return 1
  case "$beat" in ''|*[!0-9]*) return 1 ;; esac
  [ $(( $(date +%s) - beat )) -lt "$STALE_AFTER" ]
}

prs=$(gh pr list --state open --json number --jq '.[].number' 2>/dev/null) || exit 0
[ -z "$prs" ] && exit 0

blocked=""
held=""
for pr in $prs; do
  if deferred "$pr"; then
    held="$held
  PR #$pr — held ${DEFER_AGE}m ago because: $DEFER_REASON"
    continue
  fi
  checks=$(gh pr checks "$pr" --json name,bucket 2>/dev/null || echo "[]")

  count=$(echo "$checks" | jq 'length' 2>/dev/null || echo 0)
  if [ "$count" = "0" ]; then
    # Zero checks is legitimate when nothing the PR touches matches a workflow's path filter —
    # a docs-only or .claude/-only change reports none and is ready to merge. It is a problem
    # when the head never got a run it should have, and the tell is the merge state: a
    # conflicting PR reports DIRTY (or UNKNOWN while GitHub is still computing) and never
    # starts CI at all, which is the shape that went unwatched for seven hours.
    state=$(gh pr view "$pr" --json mergeStateStatus --jq '.mergeStateStatus' 2>/dev/null || echo "")
    if [ "$state" = "CLEAN" ]; then
      # Ready, and ready is the state that reads as finished. A docs-only or .claude/-only change
      # reports no checks at all and so never reached the merge reminder below, which is the same
      # hole one level down from the one that left eight green PRs sitting.
      blocked="$blocked
  PR #$pr — no checks apply to it and it is unmerged. Merge it, or say what it is waiting on and arm
    something that brings you back when that arrives."
      continue
    fi
    case "$state" in
      UNSTABLE|BEHIND|BLOCKED|"")
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
  if [ "$pending" = "0" ]; then
    # Nothing is running, so a watcher has nothing to observe and its heartbeat vouches for nothing.
    state=$(gh pr view "$pr" --json mergeStateStatus --jq '.mergeStateStatus' 2>/dev/null || echo "")
    fails=$(echo "$checks" | jq '[.[] | select(.bucket == "fail")] | length' 2>/dev/null || echo 0)
    if [ "$fails" != "0" ]; then
      blocked="$blocked
  PR #$pr — $fails check(s) failed. Read the run, fix or say why it is not yours to fix."
    elif [ "$state" = "CLEAN" ]; then
      blocked="$blocked
  PR #$pr — every check passed and it is unmerged. Merge it, or say what it is waiting on and arm
    something that brings you back when that arrives."
    fi
    continue
  fi

  watcher_is_alive && continue

  if [ "$pending" != "0" ]; then
    names=$(echo "$checks" | jq -r '[.[] | select(.bucket == "pending") | .name] | join(", ")' 2>/dev/null)
    blocked="$blocked
  PR #$pr — $pending check(s) still pending: $names"
  fi
done

if [ -z "$blocked" ]; then
  # A held PR still gets said out loud on the way past, so the claim is re-read rather than trusted.
  if [ -n "$held" ]; then
    cat >&2 <<EOF
Held, not settled — check each reason is still true:
$held
EOF
  fi
  exit 0
fi

cat >&2 <<EOF
Do not stop: an open PR has not settled.
$blocked
${held:+
Held on purpose, and worth re-reading:}$held

Holding one on purpose is allowed and expires after 45 minutes, so the reason gets re-examined
rather than forgotten:

  echo "<pr> <what clears it> $(date +%s)" >> $HOME/.velvet-pr-deferrals

Otherwise: wait for it with a Monitor that emits on both pass and fail, or keep working on
something that is itself on the critical path. Work that is off the critical path satisfies
"do not idle" while the thing you are actually waiting on goes unwatched.

A pending check stops blocking once a watcher writes $HOME/.velvet-pr-watch.heartbeat on each
poll. It must re-enumerate open PRs every cycle — a watcher pinned to one PR number leaves the
next one unwatched behind a fresh heartbeat:

  while true; do
    date +%s > "\$HOME/.velvet-pr-watch.heartbeat"
    for pr in \$(gh pr list --state open --json number --jq '.[].number'); do
      : # emit each check that has reached a terminal state, once
    done
    sleep 60
  done
EOF
exit 2
