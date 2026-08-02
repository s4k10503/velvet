# shellcheck shell=bash
# shellcheck disable=SC2034  # DEFER_REASON and DEFER_AGE are read by the guard that calls this.
#
# A merge or a pause held on purpose — a review in flight, a dependency on another change, a user
# waiting on an answer — is not the failure the Stop guards exist for, but "I am waiting" is exactly
# what a nine-hour stall said too. So a deferral is accepted and EXPIRES: it names what is held,
# states what clears it, and stops counting after DEFER_TTL. That cannot decay into permanent silence
# the way an unqualified exemption did, and re-stating it is the moment the reason gets re-examined,
# which is the only part that ever mattered.
#
#   echo "236 waiting on round-4 review $(date +%s)" >> ~/.velvet-pr-deferrals
#
# Both Stop guards read this one file under one expiry, so a held item is re-read in one place and a
# change to the format cannot land in one guard and not the other.

DEFERRALS="$HOME/.velvet-pr-deferrals"
DEFER_TTL=2700

# Sets DEFER_REASON and DEFER_AGE when a live deferral covers the given key. The reason is free text
# nothing can check, so a stale one silences a guard exactly as well as a true one — which is what
# happened: a pull request was held as "fix agent working" after the agent had finished and its work
# sat unpushed in a worktree. Suppression is therefore never silent; a caller still prints what was
# claimed and how long ago, so a reason that has stopped being true is in front of the reader instead
# of behind them.
deferred() {
  DEFER_REASON=""
  DEFER_AGE=""
  [ -f "$DEFERRALS" ] || return 1
  local line stamp
  line=$(grep "^$1 " "$DEFERRALS" 2>/dev/null | tail -1) || return 1
  [ -n "$line" ] || return 1
  stamp=${line##* }
  case "$stamp" in ''|*[!0-9]*) return 1 ;; esac
  # 10# because bash reads a leading zero as octal, and a stamp like 0900 then aborts arithmetic
  # evaluation — which discarded the whole backlog rather than one deferral.
  local age
  age=$(( $(date +%s) - 10#$stamp )) || return 1
  # Bounded below as well as above. A stamp in the future — a millisecond epoch, a typo, a backward
  # clock step — was live indefinitely, which is the permanent silence the expiry exists to prevent.
  [ "$age" -ge 0 ] || return 1
  [ "$age" -lt "$DEFER_TTL" ] || return 1
  DEFER_REASON=${line#"$1 "}
  DEFER_REASON=${DEFER_REASON% *}
  DEFER_AGE=$(( age / 60 ))
  return 0
}
