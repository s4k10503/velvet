#!/usr/bin/env bash
# Blocks Stop while assigned work is open and nothing is in flight to carry it.
#
# block-stop-on-unsettled-pr.sh returns 0 before looking at anything when the open-pull-request
# list is empty. That is the state this fires in, and it is the state the failure happened in: a
# backlog of assigned issues, the next action named in a closing paragraph, and the turn ending
# without it. Naming the next action is what the stall looks like from inside — it reads as a plan
# rather than as stopping — so the guard cannot key on intent. It keys on the backlog being
# non-empty with nothing carrying it.
#
# An open pull request hands the decision to the other guard, so one stall is never reported twice.
#
# Only issues assigned to the authenticated user count, so a contributor is not held by somebody
# else's backlog.
#
# Two labels are the exclusion, and they are labels rather than deferrals because the two forms say
# different things: a deferral is a claim that work will resume, and it expires so the claim gets
# re-read; a label says the work is not the assistant's to advance, which no amount of re-reading
# changes. Labelling also puts that state in the issue list, where the next reader sees it without
# running anything.
#
# `blocked` — cannot proceed for a reason outside this repository.
# `needs-decision` — measured to the point where what remains is a call only the owner can make.
#
# The second was added after an issue reached that state and this guard kept naming it, because the
# only alternative was a deferral that expires every forty-five minutes on a reason that does not
# change. Both labels are visible in the issue list, which is what stops either being a quiet way to
# silence this: applying one is on the record.
#
# Exit 2 with output on stderr is what Stop reads as "do not stop, here is why".

set -uo pipefail

command -v gh >/dev/null 2>&1 || exit 0
command -v jq >/dev/null 2>&1 || exit 0

# shellcheck source=lib/deferrals.sh
. "$(dirname "${BASH_SOURCE[0]}")/lib/deferrals.sh"

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
done < <(echo "$issues" | jq -r '.[]
  | select([.labels[].name] | (index("blocked") // index("needs-decision")) | not)
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

Work that is not yours to advance is labelled instead, which stops it counting here and says so in
the issue list. \`blocked\` is for a reason outside this repository; \`needs-decision\` is for work
measured to the point where what remains is a call only the owner can make:

  gh issue edit <n> --add-label blocked
  gh issue edit <n> --add-label needs-decision
EOF
exit 2
