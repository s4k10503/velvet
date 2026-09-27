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
    python3 scripts/test_quality/run_suite.py --jobs 4 scripts/test_quality/test_base_red_check.py

`--jobs N` runs the module in N processes, each executing it as `__main__` and running its share of
the tests, and refuses unless their counts add up to the one every process loaded. Each process
still executes the module the way the direct invocation does, so a module that dies during its
imports is refused there the same way. A class with class-level fixtures stays in one process, so
they run once.

Exits with the suite's own code where a test ran, and 1 where none did.
"""

import os
import re
import subprocess
import sys
from pathlib import Path

# unittest writes this to stderr whatever the verbosity, and writes nothing like it when the module
# died before `unittest.main()`. `NO_TESTS` is what it prints for an empty but reached suite, which
# is a different failure with the same cost and is refused with the rest.
RAN = re.compile(r"^Ran (\d+) tests? in ", re.M)
NO_TESTS = re.compile(r"^NO TESTS RAN", re.M)
# What a `--jobs` process prints before its tests: how many the whole module loaded.
LOADED = re.compile(r"^run_suite: the module loaded (\d+) tests?$", re.M)
SHARD = "--shard-of-jobs"


def counted(text):
    """How many tests the run reported, or None when it reported no count at all."""
    if NO_TESTS.search(text or ""):
        return 0
    found = RAN.search(text or "")
    return int(found.group(1)) if found else None


def loaded(text):
    """How many tests a `--jobs` process said the whole module loaded, or None."""
    found = LOADED.search(text or "")
    return int(found.group(1)) if found else None


def flattened(suite):
    import unittest
    for test in suite:
        if isinstance(test, unittest.TestSuite):
            yield from flattened(test)
        else:
            yield test


def units(tests):
    """The tests in load order, grouped where they have to share a process: a class with class-level
    fixtures is one unit, so they run once, and every other test is a unit of its own."""
    import unittest
    grouped = {}
    for test in tests:
        kind = type(test)
        fixtures = any(getattr(kind, name, None) is not None
                       and getattr(getattr(kind, name), "__func__", None)
                       is not getattr(unittest.TestCase, name).__func__
                       for name in ("setUpClass", "tearDownClass"))
        grouped.setdefault(kind if fixtures else test, []).append(test)
    return list(grouped.values())


def run_shard(position, count, module):
    """Executes `module` as `__main__`, and where it calls `unittest.main` runs the units whose place
    in load order is `position` modulo `count`."""
    import runpy
    import unittest

    def shard_main(*_arguments, verbosity=1, **_keywords):
        tests = list(flattened(unittest.defaultTestLoader.loadTestsFromModule(sys.modules["__main__"])))
        sys.stderr.write("run_suite: the module loaded {} tests\n".format(len(tests)))
        chosen = [test for place, unit in enumerate(units(tests)) if place % count == position
                  for test in unit]
        result = unittest.TextTestRunner(verbosity=verbosity).run(unittest.TestSuite(chosen))
        sys.exit(0 if result.wasSuccessful() else 1)

    path = Path(module).resolve()
    unittest.main = shard_main
    sys.argv = [str(path)]
    sys.path[0] = str(path.parent)
    runpy.run_path(str(path), run_name="__main__")
    return 0


def run_parallel(module, jobs):
    processes = [subprocess.Popen([sys.executable, str(Path(__file__).resolve()), SHARD, str(position),
                                   str(jobs), module],
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                 for position in range(jobs)]
    finished = [(process, *process.communicate()) for process in processes]
    for _, out, err in finished:
        sys.stdout.write(out)
        sys.stderr.write(err)

    counts = [counted(err) for _, _, err in finished]
    totals = {loaded(err) for _, _, err in finished}
    # A process that died during the module's imports printed neither number, so it is refused here
    # with one that stopped part way: neither leaves the counts adding up to one loaded total.
    ran = sum(count or 0 for count in counts)
    if len(totals) != 1 or ran != next(iter(totals)):
        print(f"\n{module} ran {counts} tests in its {jobs} processes, which loaded "
              f"{sorted(totals, key=str)}, so some ran nowhere. A process with no count ran no test:\n"
              "a module that dies during its own imports exits 0 having measured nothing, and that "
              "reads\nthe same as a pass. Run it directly to see where it stopped.", file=sys.stderr)
        return 1
    if ran == 0:
        print(f"\n{module} ran 0 tests.", file=sys.stderr)
        return 1
    return 1 if any(process.returncode for process, _, _ in finished) else 0


def main():
    if len(sys.argv) == 5 and sys.argv[1] == SHARD:
        return run_shard(int(sys.argv[2]), int(sys.argv[3]), sys.argv[4])
    arguments = sys.argv[1:]
    jobs = 1
    if arguments[:1] == ["--jobs"] and len(arguments) >= 2:
        jobs = max(1, int(arguments[1]) if arguments[1] != "auto" else (os.cpu_count() or 1))
        arguments = arguments[2:]
    if not arguments:
        print("usage: run_suite.py [--jobs N|auto] <test module> [args...]", file=sys.stderr)
        return 1
    module = arguments[0]
    if jobs > 1 and len(arguments) == 1:
        return run_parallel(module, jobs)
    done = subprocess.run([sys.executable, module, *arguments[1:]],
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
