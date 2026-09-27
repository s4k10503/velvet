#!/usr/bin/env python3
"""Every job of a required workflow carries its own timeout-minutes.

A job with none that hangs holds its runner until something else stops it, while other pull
requests' jobs queue for that runner. A workflow is required where it carries a
job named `Required checks (...)`, the reading RequiredCheckAggregationTests takes, so a third one
is in scope for having been added.

Run: python3 scripts/ci/test_job_timeouts.py
"""

import re
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]

REQUIRED = re.compile(r"^\s*name:\s*Required checks \(", re.MULTILINE)
# The trailing comment is allowed for, as RequiredCheckAggregationTests' job-key pattern allows it.
JOB_KEY = re.compile(r"^  (?P<job>[A-Za-z_][A-Za-z0-9_-]*):\s*(?:#.*)?$")
# Four spaces is a job-level key; a step's own timeout-minutes sits deeper and bounds that step alone.
JOB_TIMEOUT = re.compile(r"^    timeout-minutes:\s*[1-9][0-9]*\s*(?:#.*)?$")


def job_timeouts(text):
    """Each job key in a workflow's `jobs:` block, mapped to whether it carries a job-level timeout."""
    jobs = {}
    current = None
    inside = False
    for line in text.splitlines():
        if not inside:
            inside = line.rstrip() == "jobs:"
            continue
        if re.match(r"^[A-Za-z]", line):
            break
        key = JOB_KEY.match(line)
        if key:
            current = key.group("job")
            jobs[current] = False
        elif current and JOB_TIMEOUT.match(line):
            jobs[current] = True
    return jobs


def required_workflows(root=REPO_ROOT):
    """Each workflow under root/.github/workflows carrying a required-checks aggregate, by path."""
    workflows = {}
    for path in sorted((root / ".github/workflows").glob("*.y*ml")):
        text = path.read_text(encoding="utf-8")
        if REQUIRED.search(text):
            workflows[path.relative_to(root).as_posix()] = text
    return workflows


class RequiredWorkflowTests(unittest.TestCase):

    def test_Given_EveryRequiredWorkflow_When_ItsJobsAreRead_Then_EachCarriesATimeout(self):
        # Arrange
        workflows = required_workflows()

        # Act
        read = {path: job_timeouts(text) for path, text in workflows.items()}
        missing = ["{} ▸ {}".format(path, job)
                   for path, jobs in read.items() for job, bounded in jobs.items() if not bounded]

        # Assert — the floor is on what was read, since a reader finding no workflow or no job in one
        # reports nothing missing.
        self.assertEqual((len(read) > 0 and all(read.values()), missing), (True, []))


class ReaderTests(unittest.TestCase):

    WORKFLOW = """on: push
jobs:
  bounded:  # a comment after the key
    runs-on: ubuntu-latest
    timeout-minutes: 10
    steps:
      - run: true
  step-bounded:
    runs-on: ubuntu-latest
    steps:
      - run: true
        timeout-minutes: 5
  unbounded:
    runs-on: ubuntu-latest
    steps:
      - run: true
"""

    # GREEN_ON_BASE(construction): `job_timeouts` is this file's own, so a base run takes its copy.
    # What shows the case can fail is loosening `JOB_TIMEOUT` to `^\s*timeout-minutes:`: measured,
    # the step's timeout is then read as the job's.
    def test_Given_AWorkflow_When_ItsJobsAreRead_Then_OnlyAJobLevelTimeoutCounts(self):
        # Act
        jobs = job_timeouts(self.WORKFLOW)

        # Assert
        self.assertEqual(jobs, {"bounded": True, "step-bounded": False, "unbounded": False})

    # GREEN_ON_BASE(construction): `job_timeouts` is this file's own, so a base run takes its copy.
    # What shows the case can fail is deleting the `break` at the first top-level key: measured, the
    # later key's line is then read as the last job's timeout.
    def test_Given_ATopLevelKeyAfterTheJobs_When_Read_Then_ItEndsTheJobsBlock(self):
        # Arrange — a timeout-shaped line under a later top-level key belongs to no job.
        workflow = self.WORKFLOW + "env:\n    timeout-minutes: 10\n"

        # Act
        jobs = job_timeouts(workflow)

        # Assert
        self.assertFalse(jobs["unbounded"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
