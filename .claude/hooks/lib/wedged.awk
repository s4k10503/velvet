# Reports unreapable processes from `ps ax -o stat=,rss=,comm=`, above a memory gate in MB.
#
# Separated from its caller so the classification can be fed a process table rather than only the
# machine's own. WedgedProcessFilterTests pins which STAT values count: E is looked for anywhere
# among the flags, because anchoring it to the second character dropped every wedged process that
# carried another flag first.
#
# Exits 1 when there is nothing to report, which is how the caller stays silent.

$1 ~ /^U/ && $1 ~ /E/ {
    kilobytes += $2
    count += 1
    name = $3
    for (i = 4; i <= NF; i++) { name = name " " $i }
    sub(/.*\//, "", name)
    held[name] += 1
}

END {
    megabytes = kilobytes / 1024
    if (count == 0 || megabytes < limit) { exit 1 }
    printf "%d processes cannot be reaped, holding %.0f MB. Only a reboot clears them.\n\n", count, megabytes
    sorter = "sort -rn"
    fflush()
    for (name in held) { printf "  %4d  %s\n", held[name], name | sorter }
    close(sorter)
    printf "\nEach is in uninterruptible wait while exiting, where no signal reaches it. A run\n"
    printf "that is only slow is a different thing; CONTRIBUTING.md separates the two.\n"
}
