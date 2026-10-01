"""The command line of a running editor test run, as the busy counts of mutation_check.py,
neuter_check.py and base_red_check.py read it off `ps`."""

# The editor binary has to be the line's first word. A line that only names it further along is
# something else running it: a shell spelling its path, or mutation_check.py's watchdog, whose script
# `ps` prints on one line ahead of the editor's command. Counting those has a waiting shell report a
# busy machine forever on an idle one, and every campaign launch report a neighbour it never had.
UNITY_RUNNING = r"^(?:/Applications/\S*/MacOS|/opt/unity/Editor)/Unity -runTests"
