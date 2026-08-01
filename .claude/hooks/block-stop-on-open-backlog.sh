#!/usr/bin/env bash
# Blocks Stop while assigned work is open and nothing is in flight to carry it.
#
# block-stop-on-unsettled-pr.sh returns 0 before looking at anything when the open-pull-request
# list is empty. That is the state this fires in, and it is the state the failure happened in:
# six issues open, a next action named in the closing paragraph, and the turn ended without it.
# Naming the next action is what the stall looks like from inside — it reads as a plan rather
# than as stopping — so the guard cannot key on intent. It keys on the backlog being non-empty
# with nothing carrying it.
#
# An open pull request means the other guard owns the decision; reporting the same stall from two
# hooks would double every message and let a fix in one be undone by silence in the other.
#
# Only issues assigned to the authenticated user count. A contributor with nothing assigned is
# not held by this repository's backlog.
#
# `blocked` is the exclusion, and it is a label rather than a deferral because the two say
# different things: a deferral is a claim that work will resume, and it expires so the claim gets
# re-read; a `blocked` label says the work cannot proceed for a reason outside this repository,
# which no amount of re-reading changes. Labelling it also puts that state in the issue list,
# where the next reader sees it without running anything.
#
# Exit 2 with output on stderr is what Stop reads as "do not stop, here is why".

set -uo pipefail

command -v gh >/dev/null 2>&1 || exit 0
command -v jq >/dev/null 2>&1 || exit 0

# Shared with the pull-request guard so a held item is re-read in one place, under the same
# expiry. `backlog` holds every issue at once, for a pause that is not about any one of them.
DEFERRALS="$HOME/.velvet-pr-deferrals"
DEFER_TTL=2700

deferred() {
  DEFER_REASON=""
  DEFER_AGE=""
  [ -f "$DEFERRALS" ] || return 1
  local line stamp
  line=$(grep "^$1 " "$DEFERRALS" 2>/dev/null | tail -1) || return 1
  [ -n "$line" ] || return 1
  stamp=${line##* }
  case "$stamp" in ''|*[!0-9]*) return 1 ;; esac
  [ $(( $(date +%s) - stamp )) -lt "$DEFER_TTL" ] || return 1
  DEFER_REASON=${line#"$1 "}
  DEFER_REASON=${DEFER_REASON% *}
  DEFER_AGE=$(( ($(date +%s) - stamp) / 60 ))
  return 0
}

deferred backlog && exit 0

prs=$(gh pr list --state open --json number --jq '.[].number' 2>/dev/null) || exit 0
[ -n "$prs" ] && exit 0

me=$(gh api user --jq .login 2>/dev/null) || exit 0
[ -n "$me" ] || exit 0

issues=$(gh issue list --state open --assignee "$me" --json number,title,labels 2>/dev/null) || exit 0

open_work=""
held=""
while IFS=$'\t' read -r number title; do
  [ -n "$number" ] || continue
  if deferred "$number"; then
    held="$held
  #$number — held ${DEFER_AGE}m ago because: $DEFER_REASON"
    continue
  fi
  open_work="$open_work
  #$number $title"
done < <(echo "$issues" | jq -r '.[] | select([.labels[].name] | index("blocked") | not)
  | [.number, .title] | @tsv' 2>/dev/null)

if [ -z "$open_work" ]; then
  if [ -n "$held" ]; then
    cat >&2 <<EOF
Held, not settled — check each reason is still true:
$held
EOF
  fi
  exit 0
fi

cat >&2 <<EOF
Do not stop: assigned work is open and no pull request is carrying any of it.
$open_work
${held:+
Held on purpose, and worth re-reading:}$held

Pick one and start it. If the next action is already named above this message, that naming is
what the stall looks like from inside — do the thing instead of announcing it again.

If the user asked something and is waiting on the answer, or the pause is deliberate, say so and
arm the deferral. It expires after 45 minutes so the reason gets re-examined rather than forgotten:

  echo "backlog <what clears it> \$(date +%s)" >> $HOME/.velvet-pr-deferrals

A single issue is deferred the same way, by its number in place of \`backlog\`.

Work that cannot proceed for a reason outside this repository is labelled \`blocked\` instead, which
stops it counting here and says so in the issue list:

  gh issue edit <n> --add-label blocked
EOF
exit 2
