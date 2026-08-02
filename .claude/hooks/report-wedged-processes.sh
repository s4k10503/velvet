#!/usr/bin/env bash
# Reports unreapable processes once they hold enough memory for a reboot to be worth it.
#
# Gated on resident memory rather than on the count, and silent below the gate. No signal
# recovers one of these, so the only response is a reboot, and a report that cannot be acted
# on except by rebooting has to stay quiet until rebooting is the right call. The count goes
# in the report because it is what a reader sees in `ps`, but it is not what decides.
#
# Darwin only: the state letters this reads are that platform's.
#
# Exit 0 always.

set -uo pipefail

[ "$(uname -s 2>/dev/null)" = "Darwin" ] || exit 0
command -v ps >/dev/null 2>&1 || exit 0

THRESHOLD_MB="${VELVET_WEDGE_REPORT_MB:-500}"
case "$THRESHOLD_MB" in
  ''|*[!0-9]*) THRESHOLD_MB=500 ;;
esac

report=$(ps ax -o stat=,rss=,comm= 2>/dev/null | awk -v limit="$THRESHOLD_MB" '
  $1 ~ /^UE/ {
    kb += $2
    n += 1
    name = $3
    for (i = 4; i <= NF; i++) { name = name " " $i }
    sub(/.*\//, "", name)
    held[name] += 1
  }
  END {
    mb = kb / 1024
    if (n == 0 || mb < limit) { exit 1 }
    printf "%d processes cannot be reaped, holding %.0f MB. Only a reboot clears them.\n\n", n, mb
    sorter = "sort -rn"
    fflush()
    for (name in held) { printf "  %4d  %s\n", held[name], name | sorter }
    close(sorter)
    printf "\nEach is in state UE, where no signal reaches it. A run that is only slow is a\n"
    printf "different thing; CONTRIBUTING.md separates the two.\n"
  }
')

[ -n "$report" ] || exit 0
printf '%s\n' "$report"
exit 0
