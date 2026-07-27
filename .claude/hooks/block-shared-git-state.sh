#!/usr/bin/env bash
# Blocks git commands that move state shared across worktrees.
#
# Several agents hold worktrees of this repository at once. `checkout`, `switch`
# and `stash` act on state they share, so one agent running them silently
# retargets another's branch — this has cost recovery work twice. Reading the
# rule in a prompt did not prevent it; refusing the command does.
set -uo pipefail

payload=$(cat)

command=$(printf '%s' "$payload" | python3 -c '
import json, sys
try:
    d = json.load(sys.stdin)
except Exception:
    sys.exit(0)
inp = d.get("tool_input") or {}
sys.stdout.write(inp.get("command", "") if isinstance(inp, dict) else "")
' 2>/dev/null) || exit 0

[ -n "$command" ] || exit 0

# `git checkout -- <path>` and `git checkout <path>` restore files without
# moving HEAD, and are how an agent reverts a probe. Only the branch-moving
# forms are refused.
if printf '%s' "$command" | grep -Eq '(^|[;&|]|[[:space:]])git[[:space:]]+(switch|stash)([[:space:]]|$)'; then
    printf 'Refused: `git switch` and `git stash` move state other worktrees share. Work in the worktree you were given; if you need a different base, say so and stop.\n' >&2
    exit 2
fi

if printf '%s' "$command" | grep -Eq '(^|[;&|]|[[:space:]])git[[:space:]]+checkout([[:space:]]|$)' \
   && ! printf '%s' "$command" | grep -Eq 'git[[:space:]]+checkout[[:space:]]+(--[[:space:]]|[^-][^[:space:]]*\.[[:alnum:]]|[A-Za-z0-9_./-]+/)'; then
    printf 'Refused: `git checkout` of a branch moves state other worktrees share. Restoring a file (`git checkout -- <path>`) is allowed; changing branch is not.\n' >&2
    exit 2
fi

exit 0
