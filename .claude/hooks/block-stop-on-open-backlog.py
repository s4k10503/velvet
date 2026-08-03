#!/usr/bin/env python3
"""Block Stop while assigned work is open and nothing is in flight to carry it.

block-stop-on-unsettled-pr.py returns 0 before looking at anything when the open-pull-request list
is empty. That is the state this fires in, and it is the state the failure happened in: a backlog of
assigned issues, the next action named in a closing paragraph, and the turn ending without it.
Naming the next action is what the stall looks like from inside — it reads as a plan rather than as
stopping — so the guard cannot key on intent. It keys on the backlog being non-empty with nothing
carrying it.

An open pull request hands the decision to the other guard, so one stall is never reported twice.

Only issues assigned to the authenticated user count, so a contributor is not held by somebody
else's backlog.

Two labels are the exclusion, and they are labels rather than deferrals because the two forms say
different things: a deferral is a claim that work will resume, and it expires so the claim gets
re-read; a label says the work is not the assistant's to advance, which no amount of re-reading
changes. Labelling also puts that state in the issue list, where the next reader sees it without
running anything.

`blocked` — cannot proceed for a reason outside this repository.
`needs-decision` — measured to the point where what remains is a call only the owner can make.

The second was added after an issue reached that state and this guard kept naming it, because the
only alternative was a deferral that expires every forty-five minutes on a reason that does not
change. Both labels are visible in the issue list, which is what stops either being a quiet way to
silence this: applying one is on the record.

Exit 2 with output on stderr is what Stop reads as "do not stop, here is why".
"""

import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

from deferrals import DEFERRALS, deferred  # noqa: E402

EXCLUDING_LABELS = {"blocked", "needs-decision"}


def gh(args):
    """Return (stdout, combined output, exit code) for a gh call."""
    try:
        result = subprocess.run(["gh", *args], capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.SubprocessError) as error:
        return "", str(error), 1
    return result.stdout.strip(), (result.stdout + result.stderr).strip(), result.returncode


def unreachable(call, code, output):
    """A gh that cannot answer is not an empty answer.

    Every one of these used to take `|| exit 0` with stderr discarded, so an unauthenticated,
    offline or rate-limited run reported exactly what a cleared backlog reports. Refusing instead is
    what puts the difference in front of the reader; the deferral is the way past it when the
    network is the thing that is wrong.
    """
    print(f"""Do not stop: the backlog could not be read, so nothing here says it is clear.

  gh {call} exited {code}
{output}

If gh is unauthenticated or the network is down, say so and arm the deferral rather than treating
an unanswered question as a settled one:

  echo "backlog <what clears it> $(date +%s)" >> {DEFERRALS}""", file=sys.stderr)
    return 2


def main():
    if shutil.which("gh") is None:
        return 0

    # Printed rather than exited on quietly: this is the one key that suppresses the whole guard,
    # and lib/deferrals.py states the invariant that suppression names what was claimed and how
    # long ago.
    holding = deferred("backlog")
    if holding is not None:
        reason, minutes = holding
        print("The whole backlog is held — check the reason is still true:", file=sys.stderr)
        print(f"  held {minutes}m ago because: {reason}", file=sys.stderr)
        return 0

    listing, combined, code = gh(["pr", "list", "--state", "open", "--json", "number",
                                  "--jq", ".[].number"])
    if code != 0:
        return unreachable("pr list", code, combined)
    if listing:
        return 0

    me, combined, code = gh(["api", "user", "--jq", ".login"])
    if code != 0:
        return unreachable("api user", code, combined)
    if not me:
        return unreachable("api user", 0, "  it named no login")

    raw, combined, code = gh(["issue", "list", "--state", "open", "--assignee", me,
                              "--json", "number,title,labels"])
    if code != 0:
        return unreachable("issue list", code, combined)
    try:
        issues = json.loads(raw or "[]")
    except ValueError:
        issues = []

    open_work, held = [], []
    for issue in issues:
        if {label.get("name") for label in issue.get("labels", [])} & EXCLUDING_LABELS:
            continue
        number = str(issue.get("number", ""))
        if not number:
            continue
        holding = deferred(number)
        if holding is not None:
            reason, minutes = holding
            held.append(f"  #{number} — held {minutes}m ago because: {reason}")
            continue
        open_work.append(f"  #{number} {issue.get('title', '')}")

    if not open_work:
        if held:
            print("Held, not settled — check each reason is still true:", file=sys.stderr)
            print("\n" + "\n".join(held), file=sys.stderr)
        return 0

    held_block = ("\nHeld on purpose, and worth re-reading:\n" + "\n".join(held)) if held else ""
    print(f"""Do not stop: assigned work is open and no pull request is carrying any of it.

{chr(10).join(open_work)}
{held_block}

Pick one and start it. If the next action is already named above this message, that naming is
what the stall looks like from inside — do the thing instead of announcing it again.

If the user asked something and is waiting on the answer, or the pause is deliberate, say so and
arm the deferral. It expires after 45 minutes so the reason gets re-examined rather than forgotten:

  echo "backlog <what clears it> $(date +%s)" >> {DEFERRALS}

A single issue is deferred the same way, by its number in place of `backlog`.

Work that is not yours to advance is labelled instead, which stops it counting here and says so in
the issue list. `blocked` is for a reason outside this repository; `needs-decision` is for work
measured to the point where what remains is a call only the owner can make:

  gh issue edit <n> --add-label blocked
  gh issue edit <n> --add-label needs-decision""", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
