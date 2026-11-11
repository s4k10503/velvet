#!/usr/bin/env python3
"""Unit tests for benchmark_steps.py: which runs of the benchmark history read as a step, and what the
issue for one says and does.

Run: python3 scripts/ci/test_benchmark_steps.py
"""

import importlib.util
import json
import unittest
from dataclasses import replace
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest import mock


def load_module(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(f"{name}.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


benchmark_steps = load_module("benchmark_steps")
benchmark_results = load_module("benchmark_results")

NAME = "A.One / Time"
COUNT = "A.One / Time.GC()"
UNITS = {NAME: "ms", COUNT: ""}
ONE, TWO, THREE = "AMD EPYC 9V45", "AMD EPYC 7763", "Intel Xeon 8370C"
START = datetime(2026, 11, 1, tzinfo=timezone.utc)


def commit(index):
    return f"c{index:02d}".ljust(40, "0")


def runs_of(*pairs, name=NAME):
    """One run per (processor, value), landing a minute apart in the order given."""
    return [benchmark_steps.Run(commit(index), START + timedelta(minutes=index), f"subject {index}", processor,
                                {name: value})
            for index, (processor, value) in enumerate(pairs)]


# Four runs at 10 on each of two processors, so the last of them already has a baseline to sit within.
BEFORE = [(ONE, 10), (TWO, 10)] * 4


def stepped(level=15, count=4):
    """BEFORE, then `count` runs at `level` from c08 on."""
    return runs_of(*BEFORE, *[(ONE, level), (TWO, level)] * (count // 2))


def steps(runs):
    return benchmark_steps.find_steps(runs, UNITS)


def groups_of(runs):
    return benchmark_steps.group(steps(runs))


def step(per_processor, last=7, first=8, ratio=1.5):
    runs = runs_of(*BEFORE, (ONE, 15))
    return benchmark_steps.Step(NAME, "ms", runs[last] if last is not None else None, runs[first], ratio,
                                tuple(per_processor))


def history_text(*benches):
    return "window.BENCHMARK_DATA = " + json.dumps({"entries": {"Velvet benchmarks": [{
        "commit": {"id": commit(0), "timestamp": "2026-11-01T00:00:00Z", "message": "subject\n\nbody"},
        "date": 0, "tool": "customSmallerIsBetter", "benches": list(benches)}]}})


class FakeGh:
    def __init__(self, issues):
        self.issues = issues
        self.calls = []

    def __call__(self, args, stdin=None):
        self.calls.append(args)
        return json.dumps(self.issues) if args[:2] == ["issue", "list"] else ""


class ReadHistoryTests(unittest.TestCase):

    def test_Given_ValuesWhoseExtraNamesTheirProcessor_When_TheHistoryIsRead_Then_TheRunCarriesThatProcessor(self):
        # Arrange
        text = history_text({"name": NAME, "unit": "ms", "value": 1.0, "extra": f"processor: {TWO}"})

        # Act
        runs, _ = benchmark_steps.read_history(text)

        # Assert
        self.assertEqual(runs[0].processor, TWO)

    def test_Given_AByteSeriesBesideATimeAndACount_When_TheHistoryIsRead_Then_TheByteSeriesIsLeftOut(self):
        # Arrange
        text = history_text({"name": NAME, "unit": "ms", "value": 1.0},
                            {"name": COUNT, "unit": "", "value": 7},
                            {"name": "A.One / E2E.GC", "unit": "b", "value": 8192})

        # Act
        runs, _ = benchmark_steps.read_history(text)

        # Assert
        self.assertEqual(sorted(runs[0].values), [NAME, COUNT])

    # GREEN_ON_BASE(construction): benchmark_results.py writes the `extra` and benchmark_steps.py reads
    # it, and both are this change's. Writing `cpu: ` for `processor: ` in `to_benchmark_json` is what
    # reddens it.
    def test_Given_TheConvertersOutput_When_TheDetectorReadsItsExtra_Then_ItFindsTheProcessorWritten(self):
        # Arrange
        bench = benchmark_results.to_benchmark_json([(NAME, "ms", 1.0)], processor=TWO)[0]

        # Act
        processor = benchmark_steps.processor_of(bench)

        # Assert
        self.assertEqual(processor, TWO)


class FindStepsTests(unittest.TestCase):

    def test_Given_TheFirstSlowCommitAppendedAfterALaterOne_When_StepsAreFound_Then_TheEarlierCommitStartsTheStep(self):
        # Arrange: c08 landed before c09, and its run was appended after.
        runs = stepped()
        runs[8], runs[9] = runs[9], runs[8]

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(found[0].first_slow.commit, commit(8))

    def test_Given_ARunAboveTheOldLevelAndUnderTheThreshold_When_AStepIsFound_Then_ItsRangeReachesPastThatRun(self):
        # Arrange: 11 is above every run before it and short of the 12 that would make it slow.
        runs = runs_of(*BEFORE, (ONE, 11), *[(TWO, 15), (ONE, 15)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(found[0].last_fast.commit, commit(7))

    def test_Given_TwoStepsInOneSeries_When_StepsAreFound_Then_TheEarlierOneIsReported(self):
        # Arrange
        runs = runs_of(*BEFORE, *[(ONE, 15), (TWO, 15)] * 2, *[(ONE, 40), (TWO, 40)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(found[0].first_slow.commit, commit(8))

    def test_Given_EightSlowRunsAndThenAFastOne_When_StepsAreFound_Then_TheStepStands(self):
        # Arrange
        runs = runs_of(*BEFORE, *[(ONE, 15), (TWO, 15)] * 4, (ONE, 10))

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AStepOnTheFasterProcessor_When_StepsAreFound_Then_ItIsMeasuredAgainstThatProcessorsOwnRuns(self):
        # Arrange: 15 on the first processor is slower than its own 10 and faster than the second's 30.
        runs = runs_of(*[(ONE, 10), (TWO, 30)] * 4, *[(ONE, 15), (TWO, 45)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ASlowRunOlderThanTheLastFiveOnItsProcessor_When_StepsAreFound_Then_ItRaisesNoBar(self):
        # Arrange: the first processor's 20 is its sixth run back when the step starts.
        runs = runs_of((ONE, 20), *BEFORE, (ONE, 10), *[(TWO, 15), (ONE, 15)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AProcessorWithTwoRunsBefore_When_StepsAreFound_Then_ItsRunsAreNotJudged(self):
        # Arrange: the third processor holds still, and two runs are too few to judge it by.
        runs = runs_of(*BEFORE, (THREE, 10), (THREE, 10),
                       (ONE, 15), (THREE, 10), (TWO, 15), (THREE, 10), (ONE, 15), (TWO, 15))

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ThreeSlowRunsSoFar_When_StepsAreFound_Then_ThereIsNoStepYet(self):
        # Arrange
        runs = stepped()[:-1]

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_SlowRunsOnOneProcessorOnly_When_StepsAreFound_Then_ThereIsNoStep(self):
        # Arrange
        runs = runs_of(*BEFORE, *[(TWO, 15)] * 4)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_RunsBelowTheSlowestBeforeThem_When_StepsAreFound_Then_ThereIsNoStep(self):
        # Arrange: one run per processor at 14, so 14.5 is 1.45x the median before it and 1.04x the slowest.
        runs = runs_of((ONE, 10), (TWO, 10), (ONE, 14), (TWO, 14), *BEFORE, *[(ONE, 14.5), (TWO, 14.5)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_RunsUnderTheThreshold_When_StepsAreFound_Then_ThereIsNoStep(self):
        # Arrange: 11.5 is 1.15x every run before it.
        runs = stepped(level=11.5)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ACountThatRoseOnProcessorsWithTooFewRunsEach_When_StepsAreFound_Then_ItIsAStep(self):
        # Arrange: no processor ran three times before the rise; counts are compared across all of them.
        runs = runs_of(*[(ONE, 6), (TWO, 6), (THREE, 6)] * 2, *[(ONE, 106)] * 4, name=COUNT)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ACountOneAboveEveryCountBefore_When_StepsAreFound_Then_ThereIsNoStep(self):
        # Arrange: 6 is 1.2x 5, and one allocation is the jitter a run's count carries.
        runs = runs_of(*[("", 5)] * 5, *[("", 6)] * 4, name=COUNT)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ACountTenPercentUp_When_StepsAreFound_Then_ThereIsNoStep(self):
        # Arrange
        runs = runs_of(*[("", 100)] * 5, *[("", 110)] * 4, name=COUNT)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_TwoBenchmarksSteppingAtOneRun_When_Grouped_Then_OneGroupHoldsBoth(self):
        # Arrange
        runs = [replace(run, values={NAME: run.values[NAME], "B / Time": run.values[NAME]}) for run in stepped()]

        # Act
        groups = benchmark_steps.group(benchmark_steps.find_steps(runs, {NAME: "ms", "B / Time": "ms"}))

        # Assert
        self.assertEqual(len(groups[commit(8)]), 2)

    def test_Given_TwoStepsWhoseRangesShareACommit_When_Clustered_Then_OneIssueHoldsBoth(self):
        # Arrange: both ranges start after c07; one ends at c08 and the other at c09.
        runs = runs_of(*BEFORE, (ONE, 15), (TWO, 15), (ONE, 15))
        groups = {commit(8): [step([], last=7, first=8)],
                  commit(9): [replace(step([], last=7, first=8), first_slow=runs[9])]}

        # Act
        merged = benchmark_steps.clusters(groups, runs)

        # Assert
        self.assertEqual(len(merged), 1)

    def test_Given_TwoStepsWhoseRangesOnlyMeet_When_Clustered_Then_EachHasItsOwnIssue(self):
        # Arrange: A's range ends at c08 and B's begins right after it.
        runs = runs_of(*BEFORE, (ONE, 15), (TWO, 15), (ONE, 15))
        groups = {commit(8): [step([], last=7, first=8)], commit(9): [replace(step([], last=8, first=8),
                                                                           first_slow=runs[9])]}

        # Act
        merged = benchmark_steps.clusters(groups, runs)

        # Assert
        self.assertEqual(len(merged), 2)

    def test_Given_AHistory_When_Replayed_Then_AnIssueOpensAtTheFirstPrefixHoldingItsStep(self):
        # Arrange
        runs = stepped(count=6)
        first = next(end for end in range(1, len(runs) + 1) if steps(runs[:end]))

        # Act
        opened = benchmark_steps.replay(runs, UNITS)

        # Assert
        self.assertEqual(opened[commit(8)][0], first)


class SyncIssuesTests(unittest.TestCase):

    def test_Given_AFreshStepNoIssueCarries_When_IssuesSync_Then_OneOpensLabelledAndAssigned(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(groups_of(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(" ".join(gh.calls[1][-6:]), "--assignee s4k10503 --label performance --label bug")

    def test_Given_AnOpenIssueForAnotherStep_When_IssuesSync_Then_ThisStepStillOpensOne(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([{"number": 3, "state": "OPEN", "body": benchmark_steps.MARKER.format("f" * 40)}])

        # Act
        benchmark_steps.sync_issues(groups_of(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(gh.calls[1][:2], ["issue", "create"])

    def test_Given_AnOpenIssueForTheOtherStepOfACluster_When_IssuesSync_Then_ThatIssueIsRewritten(self):
        # Arrange
        runs = runs_of(*BEFORE, (ONE, 15), (TWO, 15), (ONE, 15))
        members = {commit(8): [step([(ONE, 10, 4, 15, 2)], last=7, first=8)],
                   commit(9): [replace(step([(TWO, 10, 4, 15, 1)], last=7, first=8), first_slow=runs[9])]}
        gh = FakeGh([{"number": 3, "state": "OPEN", "body": benchmark_steps.MARKER.format(commit(9))}])

        # Act
        with mock.patch.object(benchmark_steps, "clusters", return_value=[members]):
            benchmark_steps.sync_issues(members, runs, "o/r", gh)

        # Assert
        self.assertEqual(gh.calls[1][:3], ["issue", "edit", "3"])

    def test_Given_AnOpenIssueAlreadySayingWhatTheStepSays_When_IssuesSync_Then_NothingIsWritten(self):
        # Arrange
        runs = stepped()
        groups = groups_of(runs)
        _, body = benchmark_steps.render(groups, "o/r")
        gh = FakeGh([{"number": 3, "state": "OPEN", "body": body}])

        # Act
        benchmark_steps.sync_issues(groups, runs, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AClosedIssueCarryingTheStepsMarker_When_IssuesSync_Then_NothingIsWritten(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([{"number": 3, "state": "CLOSED", "body": benchmark_steps.MARKER.format(commit(8))}])

        # Act
        benchmark_steps.sync_issues(groups_of(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AStepWhoseFirstSlowRunIsNoLongerFresh_When_IssuesSync_Then_NoneOpens(self):
        # Arrange: twelve runs at the stepped level after the four that make the step.
        runs = stepped(count=16)
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(groups_of(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)


class RenderTests(unittest.TestCase):

    def test_Given_AStep_When_Rendered_Then_TheBodyLinksTheCommitsFromItsLastFastToItsFirstSlowRun(self):
        # Arrange
        members = {commit(8): [step([(ONE, 10, 4, 15, 2)])]}

        # Act
        _, body = benchmark_steps.render(members, "o/r")

        # Assert
        self.assertIn(f"https://github.com/o/r/compare/{commit(7)}...{commit(8)}", body)

    def test_Given_AStepWithNoRunBeforeItAtTheOldLevel_When_Rendered_Then_TheBodyLinksItsFirstSlowCommit(self):
        # Arrange
        members = {commit(8): [step([(ONE, 10, 4, 15, 2)], last=None)]}

        # Act
        _, body = benchmark_steps.render(members, "o/r")

        # Assert
        self.assertIn(f"https://github.com/o/r/commit/{commit(8)}", body)

    def test_Given_AStep_When_Rendered_Then_EachProcessorsRowReadsBeforeThenAfter(self):
        # Arrange
        members = {commit(8): [step([(ONE, 10, 4, 15, 2)])]}

        # Act
        _, body = benchmark_steps.render(members, "o/r")

        # Assert
        self.assertIn(f"| {NAME} | `{commit(8)[:8]}` | {ONE} | 10 ms (4 runs) | 15 ms (2 runs) | 1.50x |", body)

    def test_Given_ACountRisingFromZero_When_Rendered_Then_ItsRatioSaysSo(self):
        # Arrange
        members = {commit(8): [replace(step([("", 0, 5, 6, 4)]), unit="")]}

        # Act
        _, body = benchmark_steps.render(members, "o/r")

        # Assert
        self.assertIn("| from 0 |", body)

    def test_Given_MoreRowsThanTheBodyLimitHolds_When_Rendered_Then_TheBodyIsCutToIt(self):
        # Arrange
        many = [replace(step([(ONE, 10, 4, 15, 2), (TWO, 10, 4, 15, 2)]), benchmark=f"B{index} / Time")
                for index in range(2000)]

        # Act
        _, body = benchmark_steps.render({commit(8): many}, "o/r")

        # Assert
        self.assertLessEqual(len(body), benchmark_steps.BODY_LIMIT)


if __name__ == "__main__":
    unittest.main(verbosity=2)
