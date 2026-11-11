#!/usr/bin/env python3
"""Refuse `gh stack merge`, whose merge none of the merge guards beside this one read.

Those guards match `gh pr merge`, so a stack merged through the extension is held to none of them.
The extension's own help says it checks only that each pull request is open and not a draft,
leaving the rest to the repository's rules when the merge runs, and that it merges every member up
to the one it is given in a single operation. So one command lands members that none of settle's
preconditions were asked of. `scripts/pr/settle.py merge` merges one member at a time, the lowest
one not yet merged, after reading what it reads for any other pull request.

What is refused is each spelling `SPELLINGS` holds; a shell alias or a script wrapping the extension
reaches the same merge under a name this does not see. The extension's other commands are not
merges and are left alone.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from pr_body import merges_nothing  # noqa: E402
from shell_commands import program_invocations


HOOK_TOOLS = {"Bash"}

# The verdict is the command's own text: every stack merge is refused, whatever it names, so an
# operand the shell has not expanded changes nothing about it.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh stack merge $PR --yes --squash'

UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "gh stack merge 1559 --yes --squash"}

# The extension's merge through gh, through gh's `extension exec` under each name gh gives that
# command, and as the binary gh installs.
SPELLINGS = (("gh", ("stack", "merge")),
             *(("gh", (name, "exec", "stack", "merge")) for name in ("extension", "extensions", "ext")),
             ("gh-stack", ("merge",)))


def stack_merges(command):
    """The operands of each stack merge in the command, leaving out one that only prints help."""
    return [operands for program, words in SPELLINGS
            for operands in program_invocations(command, program, words)
            if not merges_nothing(operands)]


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    if not stack_merges(event.get("tool_input", {}).get("command", "")):
        return 0

    sys.stderr.write(
        "Refusing `gh stack merge`: it merges every member of the stack up to the one it is given, "
        "and no merge guard here reads any of them.\n\n"
        "Merge the lowest member not yet merged, which settle reads as it reads any pull request:\n"
        "  python3 scripts/pr/settle.py merge <pr>\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
