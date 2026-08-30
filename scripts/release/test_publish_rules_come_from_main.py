#!/usr/bin/env python3
"""The release rules a publish runs are main's, whatever line is being published.

A release is dispatched on the line it belongs to -- measured, the v2.1.4 dispatch ran with
`ref=2.x` -- so `actions/checkout` gives it that line's tree, and the release checks it runs are
main's copy as of the cut. Every rule main adds afterwards is one the line can ship against, and one
did: main's `test_release_notes.py` forbids a `Breaking` highlight outside a major, the 2.x copy
carries no such case, and v2.1.4 published one.

Held on the workflow rather than on a line, because naming the lines is what stops scaling: a 3.x
and a 4.x are cut the same way and inherit the same staleness the day they are cut.

Run: python3 scripts/release/test_publish_rules_come_from_main.py
"""

import unittest
from pathlib import Path

import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = REPO_ROOT / ".github" / "workflows" / "upm.yml"
RULES = "scripts/release/test_release_notes.py"


def steps():
    job = yaml.safe_load(WORKFLOW.read_text(encoding="utf-8"))["jobs"]
    return next(iter(job.values()))["steps"]


def index_of(predicate):
    for at, step in enumerate(steps()):
        if predicate(step.get("run") or ""):
            return at
    return None


class PublishRulesTests(unittest.TestCase):
    def test_Given_TheDispatch_When_ItReadsTheReleaseRules_Then_ItTakesThemFromMain(self):
        # Arrange -- the line's own copy is whatever main held on the day it was cut.
        # Act / Assert
        self.assertIsNotNone(index_of(lambda run: "origin main" in run and "scripts/release" in run))

    def test_Given_TheDispatch_When_ItRunsTheReleaseRules_Then_TheyRunAfterTheOverlay(self):
        # Arrange -- run before it and they are the line's copy again, which is the whole defect.
        overlay = index_of(lambda run: "origin main" in run and "scripts/release" in run)
        rules = index_of(lambda run: RULES in run)

        # Act / Assert
        self.assertEqual((rules is not None, overlay is not None and rules > overlay), (True, True))

    def test_Given_TheOverlayAndTheRules_When_TheEventIsAPush_Then_NeitherRuns(self):
        # Arrange -- a push to main is the mirror split, not a release, and has no version to check.
        guarded = [step.get("if") for step in steps()
                   if RULES in (step.get("run") or "")
                   or ("origin main" in (step.get("run") or "")
                       and "scripts/release" in (step.get("run") or ""))]

        # Act / Assert -- the count with it, so an empty set cannot pass for two guarded steps.
        self.assertEqual((len(guarded),
                          [held for held in guarded if "workflow_dispatch" not in (held or "")]),
                         (2, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)
