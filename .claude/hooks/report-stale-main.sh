#!/usr/bin/env bash
# Reports when local main or the checked-out branch lags origin/main.
#
# Verification here reads the working copy. A checkout ten commits behind origin/main
# answered a guard question with the file absent — the guard had merged — and the
# near-miss was caught only because the same tree disagreed with a second fact
# already known to be true. Pull requests merge through the API, origin/main advances,
# and nothing pulls afterwards.
#
# Exit 0 always.

set -uo pipefail

command -v git >/dev/null 2>&1 || exit 0

tree="${CLAUDE_PROJECT_DIR:-}"
if [ -z "$tree" ]; then
  tree=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
fi
cd "$tree" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0
git remote get-url origin >/dev/null 2>&1 || exit 0

FETCH_TIMEOUT=15

fetch_origin_main() {
  git fetch -q origin main &
  local pid=$!
  local waited=0
  while kill -0 "$pid" 2>/dev/null; do
    if [ "$waited" -ge "$FETCH_TIMEOUT" ]; then
      kill "$pid" 2>/dev/null
      wait "$pid" 2>/dev/null
      return 1
    fi
    sleep 1
    waited=$((waited + 1))
  done
  wait "$pid"
}

fetch_origin_main || exit 0
git rev-parse --verify refs/remotes/origin/main >/dev/null 2>&1 || exit 0

main_report=""
branch_report=""
branch=$(git symbolic-ref --quiet --short HEAD 2>/dev/null || true)

if git rev-parse --verify refs/heads/main >/dev/null 2>&1; then
  main_behind=$(git rev-list --count main..origin/main 2>/dev/null || echo 0)
  if [ "${main_behind:-0}" -gt 0 ]; then
    commit_word=commits
    [ "$main_behind" -eq 1 ] && commit_word=commit
    main_report="Local main is $main_behind $commit_word behind origin/main.

git fetch origin main
git checkout main
git merge --ff-only origin/main"
  fi
fi

if [ -n "$branch" ] && [ "$branch" != "main" ]; then
  if ! git merge-base --is-ancestor origin/main HEAD 2>/dev/null; then
    branch_behind=$(git rev-list --count HEAD..origin/main 2>/dev/null || echo 0)
    if [ "${branch_behind:-0}" -gt 0 ]; then
      commit_word=commits
      [ "$branch_behind" -eq 1 ] && commit_word=commit
      branch_report="Branch $branch is $branch_behind $commit_word behind origin/main.

git fetch origin
git rebase origin/main
git push origin $branch --force-with-lease"
    fi
  fi
fi

[ -z "$main_report" ] && [ -z "$branch_report" ] && exit 0

if [ -n "$main_report" ] && [ -n "$branch_report" ]; then
  printf '%s\n\n%s\n' \
    "This checkout may not match origin/main.

$main_report" \
    "$branch_report"
else
  report="${main_report:-$branch_report}"
  printf 'This checkout may not match origin/main.

%s\n' "$report"
fi

exit 0
