#!/usr/bin/env python3
"""Run a Python test module and refuse a run that reported no test at all.

A `test_*.py` here ends in `unittest.main()` and is invoked directly by CI. The modules under test
end in `sys.exit(main())`, and a test module imports one at module level — so a mutation that flips
that guard kills the process during the import, `unittest.main()` is never reached, and the module
exits 0 having run nothing. The only output is the check's own stdout line, which reads like success.

No guard placed inside the test module can close it: the death is at import, before any of it runs.
So the count is read from outside, off unittest's own summary line.

The Python lane of `base_red_check.py` is not exposed and the reason is worth keeping: under
`python3 -m unittest` the same mutant makes argparse exit 2 with a usage message and no trailer, so
the outcome reads as an error rather than a pass. The direct-invocation form is what loses it.

    python3 scripts/test_quality/run_suite.py scripts/hooks/test_merge_target.py

Exits with the suite's own code where a test ran, and 1 where none did.
"""

import re
import subprocess
import sys
from pathlib import Path

# unittest writes this to stderr whatever the verbosity, and writes nothing like it when the module
# died before `unittest.main()`. `NO_TESTS` is what it prints for an empty but reached suite, which
# is a different failure with the same cost and is refused with the rest.
RAN = re.compile(r"^Ran (\d+) tests? in ", re.M)
NO_TESTS = re.compile(r"^NO TESTS RAN", re.M)


def counted(text):
    """How many tests the run reported, or None when it reported no count at all."""
    if NO_TESTS.search(text or ""):
        return 0
    found = RAN.search(text or "")
    return int(found.group(1)) if found else None


def main():
    if len(sys.argv) < 2:
        print("usage: run_suite.py <test module> [args...]", file=sys.stderr)
        return 1
    module = sys.argv[1]
    done = subprocess.run([sys.executable, module, *sys.argv[2:]],
                          capture_output=True, text=True)
    sys.stdout.write(done.stdout)
    sys.stderr.write(done.stderr)

    ran = counted(done.stderr)
    if ran is None:
        print(f"\n{module} reported no test count, so nothing here ran a test.\n"
              "A module that dies during its own imports exits 0 having measured nothing, and that "
              "reads\nthe same as a pass. Run it directly to see where it stopped.", file=sys.stderr)
        return 1
    if ran == 0:
        print(f"\n{module} ran 0 tests.", file=sys.stderr)
        return 1
    return done.returncode


if __name__ == "__main__":
    sys.exit(main())
