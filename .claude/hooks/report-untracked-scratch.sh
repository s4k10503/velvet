#!/usr/bin/env bash
# Reports what a subagent left in the working tree, into the parent's context.
#
# Two failures this guards. A read-only agent creating a probe fixture and not
# removing it — self-reported cleanup has been wrong before, and the leftover
# reads as production code to the next session. And a source file .gitignore
# excludes, which stays untracked while every local run passes because the file
# is there.
set -uo pipefail

cd "${CLAUDE_PROJECT_DIR:-.}" 2>/dev/null || exit 0
command -v git >/dev/null 2>&1 || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

untracked=$(git status --porcelain --untracked-files=all 2>/dev/null \
    | awk '/^\?\?/ { print substr($0, 4) }' \
    | grep -v '^Library/' \
    | head -20)

# An ignored source file is the dangerous case; build output is ignored on purpose.
ignored=$(git status --porcelain --ignored=matching --untracked-files=all 2>/dev/null \
    | awk '/^!!/ { print substr($0, 4) }' \
    | grep -E '\.(cs|uss|uxml|asmdef|csproj|sln|md)$' \
    | grep -vE '^(Library|Temp|obj|Logs)/' \
    | head -20)

[ -z "$untracked" ] && [ -z "$ignored" ] && exit 0

python3 - "$untracked" "$ignored" <<'PY'
import json, sys
untracked, ignored = sys.argv[1], sys.argv[2]
parts, headline = [], []
if untracked:
    n = len(untracked.splitlines())
    headline.append("{} untracked".format(n))
    parts.append(
        "Untracked files remain in the working tree. Read each before deciding "
        "it is harmless — a probe fixture left behind reads as production code "
        "to the next session, and an agent's own report that it cleaned up has "
        "been wrong before:\n" + untracked
    )
if ignored:
    n = len(ignored.splitlines())
    headline.append("{} gitignored source".format(n))
    parts.append(
        "Source files here are EXCLUDED by .gitignore, so they cannot reach CI "
        "while every local run stays green because the files are present "
        "locally:\n" + ignored
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
