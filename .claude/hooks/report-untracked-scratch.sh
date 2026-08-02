#!/usr/bin/env bash
# Reports what a subagent left in the tree IT worked in, into the parent's context.
#
# Two failures this guards. A read-only agent creating a probe fixture and not
# removing it — self-reported cleanup has been wrong before, and the leftover
# reads as production code to the next session. And a source file .gitignore
# excludes, which stays untracked while every local run passes because the file
# is there.
#
# The tree comes from the session's own cwd, not from CLAUDE_PROJECT_DIR. An agent
# running in a git worktree has its own checkout, and reporting the MAIN checkout's
# untracked files to it produced a non-terminating loop: it was told about files it
# had not created, could not clear them without destroying another agent's in-flight
# work, and was told again on every stop because nothing it could do changed the
# condition. Three agents hit it in one session, and one was instructed to delete the
# three source files of an open PR. The wording below carries the same lesson: this
# is a report, and whoever owns a tree decides what is left in it.
set -uo pipefail

payload=$(cat 2>/dev/null || echo '')
tree=$(printf '%s' "$payload" | python3 -c 'import json,sys
try: print(json.load(sys.stdin).get("cwd") or "")
except Exception: print("")' 2>/dev/null)
[ -n "$tree" ] || tree="${CLAUDE_PROJECT_DIR:-.}"

cd "$tree" 2>/dev/null || exit 0
command -v git >/dev/null 2>&1 || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

# Counted before the truncation, not after: the headline read "20 untracked" for a tree holding
# thirty-seven, and a reader who clears the twenty believes they are done.
untracked_all=$(git status --porcelain --untracked-files=all 2>/dev/null \
    | awk '/^\?\?/ { print substr($0, 4) }' \
    | grep -v '^Library/')
untracked_total=$(printf '%s' "$untracked_all" | grep -c . || true)
untracked=$(printf '%s' "$untracked_all" | head -20)

# An ignored source file is the dangerous case; build output is ignored on purpose.
ignored_all=$(git status --porcelain --ignored=matching --untracked-files=all 2>/dev/null \
    | awk '/^!!/ { print substr($0, 4) }' \
    | grep -E '\.(cs|uss|uxml|asmdef|csproj|sln|md)$' \
    | grep -vE '^(Library|Temp|obj|Logs)/')
ignored_total=$(printf '%s' "$ignored_all" | grep -c . || true)
ignored=$(printf '%s' "$ignored_all" | head -20)

[ -z "$untracked" ] && [ -z "$ignored" ] && exit 0

python3 - "$untracked" "$ignored" "$tree" "$untracked_total" "$ignored_total" <<'PY'
import json, sys
untracked, ignored, tree = sys.argv[1], sys.argv[2], sys.argv[3]
untracked_total, ignored_total = int(sys.argv[4] or 0), int(sys.argv[5] or 0)


def shown(listing, total):
    listed = len(listing.splitlines())
    return listing if total <= listed else listing + "\n... and {} more".format(total - listed)


parts, headline = [], []
if untracked:
    n = untracked_total
    headline.append("{} untracked".format(n))
    parts.append(
        "Untracked files remain in {}. Read each before deciding it is harmless — a probe "
        "fixture left behind reads as production code to the next session, and an agent's own "
        "report that it cleaned up has been wrong before. What stays is for whoever owns this "
        "tree to decide; nothing here asks for a deletion:\n{}".format(tree, shown(untracked, untracked_total))
    )
if ignored:
    n = ignored_total
    headline.append("{} gitignored source".format(n))
    parts.append(
        "Source files here are EXCLUDED by .gitignore, so they cannot reach CI "
        "while every local run stays green because the files are present "
        "locally:\n" + shown(ignored, ignored_total)
    )
# systemMessage is the transcript channel; additionalContext is the model's and does not display.
print(json.dumps({
    "systemMessage": "subagent left files behind — " + ", ".join(headline),
    "hookSpecificOutput": {
        "hookEventName": "SubagentStop",
        "additionalContext": "\n\n".join(parts),
    },
}))
PY
exit 0
