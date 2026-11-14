#!/usr/bin/env python3
"""Unit tests for benchmark_steps.py: which runs of the benchmark history read as a step or a drift, and
what the issue for one says and does.

Run: python3 scripts/ci/test_benchmark_steps.py
"""

import importlib.util
import json
import unittest
from dataclasses import replace
from datetime import datetime, timedelta, timezone
from pathlib import Path


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
ONE, TWO, THREE, FOUR = "AMD EPYC 9V45", "AMD EPYC 7763", "Intel Xeon 8370C", "Intel Xeon 8573C"
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


RUNS = runs_of(*[(ONE, 10)] * 40)


def step(per_processor=((ONE, 10, 4, 15, 2),), last=7, first=8, since=0, ratio=1.5, name=NAME, unit="ms",
         decided=None):
    """A step built directly over RUNS' commits, `last` None for one with no run at the old level before it."""
    return benchmark_steps.Step(name, unit, RUNS[since], RUNS[last] if last is not None else None, RUNS[first],
                                RUNS[first if decided is None else decided], ratio, tuple(per_processor))


def history_text(*benches):
    return "window.BENCHMARK_DATA = " + json.dumps({"entries": {"Velvet benchmarks": [{
        "commit": {"id": commit(0), "timestamp": "2026-11-01T00:00:00Z", "message": "subject\n\nbody"},
        "date": 0, "tool": "customSmallerIsBetter", "benches": list(benches)}]}})


class FakeGh:
    """Answers the REST listing with `issues` and records every call."""

    def __init__(self, issues):
        self.issues = issues
        self.calls = []
        self.bodies = []

    def __call__(self, args, stdin=None):
        self.calls.append(args)
        self.bodies.append(stdin)
        return "".join(json.dumps(issue) + "\n" for issue in self.issues) if args[0] == "api" else ""


def issue(number, state, *markers, user=benchmark_steps.AUTHOR):
    return {"number": number, "state": state, "body": "\n".join(markers) + "\nbody", "user": user}


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

    def test_Given_FourSlowRunsOnTwoProcessors_When_AStepIsFound_Then_ItIsDecidedAtTheFourth(self):
        # Arrange
        runs = stepped()

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(found[0].decided.commit, commit(11))

    def test_Given_ARunAboveTheOldLevelAndUnderTheThreshold_When_AStepIsFound_Then_ItsRangeReachesPastThatRun(self):
        # Arrange: 11 is above every run before it and short of the 12 that would make it slow.
        runs = runs_of(*BEFORE, (ONE, 11), *[(TWO, 15), (ONE, 15)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(found[0].last_fast.commit, commit(7))

    def test_Given_TwoStepsInOneSeries_When_StepsAreFound_Then_BothAreReported(self):
        # Arrange
        runs = runs_of(*BEFORE, *[(ONE, 15), (TWO, 15)] * 2, *[(ONE, 40), (TWO, 40)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual([s.first_slow.commit for s in found], [commit(8), commit(12)])

    def test_Given_ASecondStepStartingAtTheNextRunOfTheFirstsProcessor_When_StepsAreFound_Then_ItStartsThere(self):
        # Arrange: the first processor's only run since the first step is that step's own first slow run.
        runs = runs_of(*BEFORE, (ONE, 15), (TWO, 15), (TWO, 15), (TWO, 15), *[(ONE, 40), (TWO, 40)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertIn(commit(12), [s.first_slow.commit for s in found])

    def test_Given_AStepAndProcessorsThatHaveNotRunSince_When_TheyRunAtTheNewLevel_Then_ItIsNoFurtherStep(self):
        # Arrange: the third and fourth processors last ran before the step, so their baselines are the old level.
        runs = runs_of(*BEFORE, *[(THREE, 10), (FOUR, 10)] * 3, *[(ONE, 15), (TWO, 15)] * 2,
                       *[(THREE, 15), (FOUR, 15)] * 2)

        # Act
        found = steps(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_FourSlowRunsAndThenOnesAtTheOldLevel_When_StepsAreFound_Then_TheStepStands(self):
        # Arrange
        runs = runs_of(*BEFORE, *[(ONE, 15), (TWO, 15)] * 2, (ONE, 10), (TWO, 10))

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

    def test_Given_StepsWhoseRangesOverlapOnlyThroughAThird_When_Clustered_Then_OneIssueHoldsAll(self):
        # Arrange: (10, 20] and (25, 30] share nothing; (5, 35] shares commits with both.
        found = [step(last=10, first=20), step(last=25, first=30, name="B / Time"),
                 step(last=5, first=35, name="C / Time")]

        # Act
        merged = benchmark_steps.clusters(found, RUNS)

        # Assert
        self.assertEqual(len(merged), 1)

    def test_Given_TwoStepsWhoseRangesOnlyMeet_When_Clustered_Then_EachHasItsOwnIssue(self):
        # Arrange: one range ends at c08 and the other begins right after it.
        found = [step(last=7, first=8), step(last=8, first=9, name="B / Time")]

        # Act
        merged = benchmark_steps.clusters(found, RUNS)

        # Assert
        self.assertEqual(len(merged), 2)

    def test_Given_ABenchmarkThatSteppedBeforeAnyRunSettled_When_Clustered_Then_ItsRangeStartsAtItsFirstRun(self):
        # Arrange: an old step ending at c05; a benchmark added at c26 that stepped at c29; and a step in
        # (c25, c26], which shares a commit with the young one's range only if that range holds c26.
        found = [step(last=4, first=5), step(last=None, first=29, since=26, name="N / Time"),
                 step(last=25, first=26, name="M / Time")]

        # Act
        merged = benchmark_steps.clusters(found, RUNS)

        # Assert
        self.assertEqual(len(merged), 2)

    def test_Given_AHistory_When_Replayed_Then_AnIssueOpensAtTheFirstPrefixHoldingItsStep(self):
        # Arrange
        runs = stepped(count=6)
        first = next(end for end in range(1, len(runs) + 1) if steps(runs[:end]))

        # Act
        issues = benchmark_steps.replay(runs, UNITS)

        # Assert
        self.assertEqual(issues.issues[0]["opened"], first)


def reopened(number, *markers):
    return {**issue(number, "open", *markers), "state_reason": "reopened"}


def created(gh):
    """The bodies of the issues the sync created."""
    return [body for call, body in zip(gh.calls, gh.bodies) if call[:2] == ["issue", "create"]]


class SyncIssuesTests(unittest.TestCase):

    def test_Given_AnyHistory_When_IssuesSync_Then_TheyAreListedThroughTheRestIssuesEndpoint(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(gh.calls[0][2], "repos/o/r/issues?labels=performance&state=all&per_page=100")

    def test_Given_AnyHistory_When_IssuesSync_Then_EachIssueIsReadWithItsStateReason(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertIn("state_reason", gh.calls[0][-1])

    def test_Given_AFreshStepNoIssueNames_When_IssuesSync_Then_OneOpensLabelledAndAssigned(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(" ".join(gh.calls[1][-6:]), "--assignee s4k10503 --label performance --label bug")

    def test_Given_TwoNewStepsWhoseRangesShareACommit_When_IssuesSync_Then_OneIssueOpensForBoth(self):
        # Arrange
        found = [step(last=29, first=30), step(last=29, first=31, name="B / Time")]
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(found, RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_ANewStepInTheRangeOfAnOpenIssuesStep_When_IssuesSync_Then_ItsNewIssueNamesItAlone(self):
        # Arrange
        found = [step(last=29, first=30), step(last=29, first=31, name="B / Time")]
        gh = FakeGh([issue(3, "open", benchmark_steps.marker(found[0]))])

        # Act
        benchmark_steps.sync_issues(found, RUNS, "o/r", gh)

        # Assert
        self.assertEqual(benchmark_steps.MARKER_PATTERN.findall(created(gh)[0]), [("B / Time", commit(31))])

    def test_Given_AnOpenIssueNamingThisStep_When_IssuesSync_Then_ItIsRewritten(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([issue(3, "open", benchmark_steps.marker(steps(runs)[0]))])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(gh.calls[1][:3], ["issue", "edit", "3"])

    def test_Given_AnOpenIssueNamingTwoStepsTheHistoryHolds_When_IssuesSync_Then_ItIsRewrittenWithBoth(self):
        # Arrange
        found = [step(last=7, first=8), step(last=20, first=21, name="B / Time")]
        gh = FakeGh([issue(3, "open", *map(benchmark_steps.marker, found))])

        # Act
        benchmark_steps.sync_issues(found, RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(benchmark_steps.MARKER_PATTERN.findall(gh.bodies[1])), 2)

    def test_Given_AnOpenIssueAlreadySayingWhatTheStepSays_When_IssuesSync_Then_NothingIsWritten(self):
        # Arrange
        runs = stepped()
        _, body = benchmark_steps.render(steps(runs), "o/r")
        gh = FakeGh([{"number": 3, "state": "open", "body": body, "user": benchmark_steps.AUTHOR}])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AnOpenIssueNamingAStepTheHistoryNoLongerHolds_When_IssuesSync_Then_ItIsLeftAsItWas(self):
        # Arrange: the issue names this step and B's, and B's is no longer in the history.
        gh = FakeGh([issue(3, "open", benchmark_steps.marker(step()),
                           benchmark_steps.marker(step(last=20, first=21, name="B / Time")))])

        # Act
        benchmark_steps.sync_issues([step()], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AnIssueAPersonOpenedWithThisStepsMarker_When_IssuesSync_Then_ItIsNotWritten(self):
        # Arrange: a write-up a person opened, carrying the job's marker line copied from its issue.
        runs = stepped()
        gh = FakeGh([issue(7, "open", benchmark_steps.marker(steps(runs)[0]), user="someone")])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AnyHistory_When_IssuesSync_Then_EachIssueIsReadWithWhoOpenedIt(self):
        # Arrange
        runs = stepped()
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertIn("user: .user.login", gh.calls[0][-1])

    def test_Given_AReopenedIssueNamingAFreshStep_When_IssuesSync_Then_NothingIsWritten(self):
        # Arrange
        fresh = step(last=29, first=30)
        gh = FakeGh([reopened(3, benchmark_steps.marker(fresh))])

        # Act
        benchmark_steps.sync_issues([fresh], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AClosedIssueNamingAFreshStep_When_IssuesSync_Then_NothingIsWritten(self):
        # Arrange
        fresh = step(last=29, first=30)
        gh = FakeGh([issue(3, "closed", benchmark_steps.marker(fresh))])

        # Act
        benchmark_steps.sync_issues([fresh], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AnIssueNamingAnotherBenchmarksStepAtThisCommit_When_IssuesSync_Then_ThisStepOpensOne(self):
        # Arrange
        fresh = step(last=29, first=30)
        gh = FakeGh([issue(3, "closed", benchmark_steps.marker(replace(fresh, benchmark="B / Time")))])

        # Act
        benchmark_steps.sync_issues([fresh], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_AClosedIssueAndALaterStepOfThatBenchmarkWhoseRangeReachesBack_When_IssuesSync_Then_ItOpensOne(self):
        # Arrange: the closed issue names a step first slow at c30; the later one is first slow at c37 and
        # its range reaches back over c30.
        accepted, later = step(last=29, first=30), step(last=29, first=37)
        gh = FakeGh([issue(3, "closed", benchmark_steps.marker(accepted))])

        # Act
        benchmark_steps.sync_issues([accepted, later], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_AnIssueNamingAStepWhoseFirstSlowRunHasMoved_When_IssuesSync_Then_ANewIssueOpens(self):
        # Arrange: the issue was written when this step's first slow run was c31; it is c30 now.
        moved = step(last=29, first=30)
        gh = FakeGh([issue(3, "open", benchmark_steps.marker(replace(moved, first_slow=RUNS[31])))])

        # Act
        benchmark_steps.sync_issues([moved], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_AStepDecidedFurtherBackThanTheLatestRuns_When_IssuesSync_Then_NoneOpens(self):
        # Arrange: twelve runs at the stepped level after the four that make the step.
        runs = stepped(count=16)
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues(steps(runs), runs, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AStepDecidedAmongTheLatestRunsWhoseFirstSlowRunIsOlder_When_IssuesSync_Then_ItOpensOne(self):
        # Arrange: RUNS' twelve latest are c28..c39; the step starts at c21 and is decided at c30.
        gh = FakeGh([])

        # Act
        benchmark_steps.sync_issues([step(last=20, first=21, decided=30)], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_AnOpenIssueThatNamesNoStep_When_IssuesSync_Then_NothingIsWritten(self):
        # Arrange: an issue the performance label shares with this job's, written by hand.
        gh = FakeGh([{"number": 9, "state": "open", "body": "Initial mount allocates per element."}])

        # Act
        benchmark_steps.sync_issues([], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)


class RenderTests(unittest.TestCase):

    def test_Given_AStep_When_Rendered_Then_TheBodyLinksTheCommitsFromItsLastFastToItsFirstSlowRun(self):
        # Arrange
        found = [step()]

        # Act
        _, body = benchmark_steps.render(found, "o/r")

        # Assert
        self.assertIn(f"https://github.com/o/r/compare/{commit(7)}...{commit(8)}", body)

    def test_Given_AStepWithNoRunBeforeItAtTheOldLevel_When_Rendered_Then_TheBodyLinksItsFirstSlowCommit(self):
        # Arrange
        found = [step(last=None)]

        # Act
        _, body = benchmark_steps.render(found, "o/r")

        # Assert
        self.assertIn(f"https://github.com/o/r/commit/{commit(8)}", body)

    def test_Given_AStep_When_Rendered_Then_EachProcessorsRowReadsBeforeThenAfter(self):
        # Arrange
        found = [step()]

        # Act
        _, body = benchmark_steps.render(found, "o/r")

        # Assert
        self.assertIn(f"| {NAME} | `{commit(8)[:8]}` | {ONE} | 10 ms (4 runs) | 15 ms (2 runs) | 1.50x |", body)

    def test_Given_ACountRisingFromZero_When_Rendered_Then_ItsTitleSaysSo(self):
        # Arrange: the step's ratio is infinite and its row keeps a baseline to divide by.
        found = [step([("", 1, 5, 6, 4)], ratio=float("inf"), unit="")]

        # Act
        title, _ = benchmark_steps.render(found, "o/r")

        # Assert
        self.assertTrue(title.endswith(f"{NAME} from 0"))

    def test_Given_ACountRisingFromZero_When_Rendered_Then_ItsRowsRatioSaysSo(self):
        # Arrange
        found = [step([("", 0, 5, 6, 4)], ratio=float("inf"), unit="")]

        # Act
        _, body = benchmark_steps.render(found, "o/r")

        # Assert
        self.assertIn("| from 0 |", body)

    def test_Given_ACommitSubjectNamingAUser_When_Rendered_Then_TheSubjectIsInCode(self):
        # Arrange
        found = [replace(step(), first_slow=replace(RUNS[8], subject="as Tailwind's @theme does"))]

        # Act
        _, body = benchmark_steps.render(found, "o/r")

        # Assert
        self.assertIn("(`as Tailwind's @theme does`)", body)

    def test_Given_MoreRowsThanTheBodyLimitHolds_When_Rendered_Then_TheBodyIsCutToIt(self):
        # Arrange
        many = [step([(ONE, 10, 4, 15, 2), (TWO, 10, 4, 15, 2)], name=f"B{index} / Time") for index in range(2000)]

        # Act
        _, body = benchmark_steps.render(many, "o/r")

        # Assert
        self.assertLessEqual(len(body), benchmark_steps.BODY_LIMIT)


def drift_commit(index):
    return f"d{index:03d}".ljust(40, "0")


def daily(levels, name=NAME, every=timedelta(days=1)):
    """One run per processor at each `every`, a minute apart, at the level `levels` gives that processor
    there; a level of None leaves that run out."""
    width = len(levels)
    return [benchmark_steps.Run(drift_commit(day * width + offset), START + day * every + timedelta(minutes=offset),
                                f"subject {day * width + offset}", processor, {name: series[day]})
            for day in range(max(map(len, levels.values())))
            for offset, (processor, series) in enumerate(levels.items())
            if day < len(series) and series[day] is not None]


def beside(first, second):
    """Two histories laid out alike as one, each run measuring the benchmarks of both."""
    return [replace(one, values={**one.values, **other.values}) for one, other in zip(first, second)]


OTHER = "B / Time"
BOTH = {**UNITS, OTHER: "ms"}

# Three rises of about 1.15x each, every one short of a step, and 1.52x in all.
CREEP = [10] * 6 + [11.5] * 5 + [13.2] * 5 + [15.2] * 5
FLAT = [10] * 21
# 1.145x by creep to day 13, a step there, then 1.17x by creep again.
AROUND = ([10] * 5 + [10.45] * 2 + [10.95] * 2 + [11.45] * 2 + [12] * 2
          + [14.5] * 3 + [15.2] * 3 + [15.9] * 3 + [16.5] * 3 + [17] * 4)
# A 1.5x step on day 6 undone on day 9, then 1.4x by creep.
UNDONE = [10] * 6 + [15] * 3 + [10] * 3 + [10.6] * 4 + [11.3] * 4 + [12] * 4 + [12.8] * 4 + [14] * 4


def ramp(low, high, days):
    """`days` levels rising from `low` by a constant factor to `high`, `low` itself left out."""
    return [low * (high / low) ** ((day + 1) / days) for day in range(days)]


def drifts(runs, units=UNITS):
    return benchmark_steps.find_drifts(runs, units)


class FindDriftsTests(unittest.TestCase):

    def test_Given_StepsEachUnderTheStepThresholdThatTogetherCrossTheDriftThreshold_When_RegressionsAreFiled_Then_OneIssueOpens(self):
        # Arrange
        runs = daily({ONE: CREEP, TWO: CREEP})
        gh = FakeGh([])

        # Act
        benchmark_steps.file_regressions(runs, UNITS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_StepsEachUnderTheStepThresholdThatTogetherCrossTheDriftThreshold_When_RegressionsAreFiled_Then_ItNamesTheDriftAtTheLatestRun(self):
        # Arrange
        runs = daily({ONE: CREEP, TWO: CREEP})
        gh = FakeGh([])

        # Act
        benchmark_steps.file_regressions(runs, UNITS, "o/r", gh)

        # Assert
        self.assertEqual(benchmark_steps.DRIFT_MARKER_PATTERN.findall(created(gh)[0]), [(NAME, runs[-1].commit)])

    def test_Given_AFlatHistory_When_RegressionsAreFiled_Then_NoneOpens(self):
        # Arrange
        runs = daily({ONE: FLAT, TWO: FLAT})
        gh = FakeGh([])

        # Act
        benchmark_steps.file_regressions(runs, UNITS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 0)

    def test_Given_AStepAmongTheLatestRuns_When_RegressionsAreFiled_Then_OnlyTheStepOpensOne(self):
        # Arrange: 15 is 1.5x 10, a step on both processors at day 22 of 28.
        stepped_up = [10] * 22 + [15] * 6
        runs = daily({ONE: stepped_up, TWO: stepped_up})
        gh = FakeGh([])

        # Act
        benchmark_steps.file_regressions(runs, UNITS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_ARiseUnderTheDriftThreshold_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: 12.5 is 1.25x 10.
        under = [10] * 6 + [11] * 5 + [12.5] * 10
        runs = daily({ONE: under, TWO: under})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ACreepOnOneProcessorAlone_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange
        runs = daily({ONE: CREEP, TWO: FLAT})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ACreepOnTwoProcessorsAndOnAThirdWithNineRuns_When_DriftsAreFound_Then_OnlyTheTwoAreJudged(self):
        # Arrange: the third processor's nine runs span the whole creep.
        runs = daily({ONE: CREEP, TWO: CREEP, THREE: [10] * 4 + [None] * 12 + [15.2] * 5})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found[0].per_processor), 2)

    def test_Given_ACreepOverTenDays_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: two runs a day on each processor.
        runs = daily({ONE: CREEP, TWO: CREEP}, every=timedelta(hours=12))

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ACreepThatEndedMoreThanThirtyDaysBack_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: the last rise is on day 16 of 51.
        old = CREEP + [15.2] * 30
        runs = daily({ONE: old, TWO: old})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_AStepAndACreepAfterItThatEndedMoreThanThirtyDaysBack_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: a 1.5x step on day 4, then 1.33x by creep up to day 18 of 51.
        old = [10] * 4 + [15] * 4 + [16.5] * 5 + [18] * 5 + [20] * 33
        runs = daily({ONE: old, TWO: old})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_AStepFollowedByAFlatLevel_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange
        stepped_up = [10] * 8 + [15] * 20
        runs = daily({ONE: stepped_up, TWO: stepped_up})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_TwoStepsAndNoCreep_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: 13 is 1.3x 10, a step at day 6, and 17 is 1.31x 13, a step at day 16.
        two_steps = [10] * 6 + [13] * 10 + [17] * 14
        runs = daily({ONE: two_steps, TWO: two_steps})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ACreepEitherSideOfAStep_When_DriftsAreFound_Then_OneIsFound(self):
        # Arrange
        runs = daily({ONE: AROUND, TWO: AROUND})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ACreepEitherSideOfAStep_When_DriftsAreFound_Then_ItsRatioLeavesTheStepOut(self):
        # Arrange
        runs = daily({ONE: AROUND, TWO: AROUND})

        # Act
        found = drifts(runs)

        # Assert
        self.assertAlmostEqual(found[0].ratio, 11.45 / 10 * 17 / 14.5)

    def test_Given_ACreepAfterAStepThatWasUndone_When_DriftsAreFound_Then_OneIsFound(self):
        # Arrange
        runs = daily({ONE: UNDONE, TWO: UNDONE})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AStepUndoneTo15PercentAboveItsOldLevelAndACreepAfter_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: a 1.5x step on day 8 undone after three days to 11.5, 1.15x the 10 before it, then a creep
        # to 13.8, 1.38x 10.
        partly = [10] * 8 + [15] * 3 + [11.5] * 9 + [12.2] * 4 + [13] * 4 + [13.8] * 4
        runs = daily({ONE: partly, TWO: partly})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_AFallAndACreepFromTheNextRun_When_DriftsAreFound_Then_OneIsFound(self):
        # Arrange: a fix halves the time on day 6, and from day 7 it creeps 1.4x over eight days.
        fixed = [20] * 6 + [10] + ramp(10, 14, 8) + [14] * 5
        runs = daily({ONE: fixed, TWO: fixed})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AFallAWeekBeforeACreep_When_DriftsAreFound_Then_OneIsFound(self):
        # Arrange: a fix halves the time on day 6, and from day 12 it creeps 1.4x over sixteen days.
        fixed = [20] * 6 + [10] * 6 + ramp(10, 14, 16) + [14]
        runs = daily({ONE: fixed, TWO: fixed})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AStepAndACreepFromTheNextRun_When_DriftsAreFound_Then_OneIsFound(self):
        # Arrange: a 1.5x step on day 10, and from day 11 a 1.4x creep over eight days.
        stepped_up = [10] * 10 + [15] + ramp(15, 21, 8) + [21] * 5
        runs = daily({ONE: stepped_up, TWO: stepped_up})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AFallUndoneTwoDaysLater_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange
        dipped = [20] * 10 + [10] * 2 + [20] * 16
        runs = daily({ONE: dipped, TWO: dipped})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_AStepUndoneByAFall_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: a 1.5x step on day 8, and a fall back to 10 on day 18.
        undone = [10] * 8 + [15] * 10 + [10] * 10
        runs = daily({ONE: undone, TWO: undone})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ARiseOnTwoProcessorsThatAThirdKeepsFromBeingAStep_When_DriftsAreFound_Then_ThereIsNoneYet(self):
        # Arrange: three runs each at 15 on two processors, while the third stays at 10.
        risen = [10] * 20 + [15] * 3
        runs = daily({ONE: risen, TWO: risen, THREE: [10] * 23})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ARiseOnTwoProcessorsThatAThirdKeepsFromBeingAStepFiveRunsOn_When_DriftsAreFound_Then_OneIsFound(self):
        # Arrange: eight runs each at 15 on two processors, while the third stays at 10.
        risen = [10] * 20 + [15] * 8
        runs = daily({ONE: risen, TWO: risen, THREE: [10] * 28})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ACreepOfEightRunsBeforeAStep_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange: 1.45x by creep over the eight runs before a 1.38x step on day 8.
        short = [10, 10, 10, 11.5, 13, 14.5, 14.5, 14.5] + [20] * 20
        runs = daily({ONE: short, TWO: short})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_ACreepWhileAnotherBenchmarkSteps_When_DriftsAreFound_Then_TheCreepIsFound(self):
        # Arrange: the other benchmark steps 1.5x at day 20, when the creep is at 12.2 of its 13.5.
        creep = [10] * 12 + [10.7] * 3 + [11.4] * 3 + [12.2] * 3 + [13.5] * 9
        stepped_up = [10] * 20 + [15] * 10
        runs = beside(daily({ONE: creep, TWO: creep}), daily({ONE: stepped_up, TWO: stepped_up}, name=OTHER))

        # Act
        found = drifts(runs, BOTH)

        # Assert
        self.assertEqual([drift.benchmark for drift in found], [NAME])

    def test_Given_ACreepOnTwoProcessorsAndOnRunsThatRecordNone_When_DriftsAreFound_Then_OnlyTheTwoAreJudged(self):
        # Arrange
        runs = daily({ONE: CREEP, TWO: CREEP, "": CREEP})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found[0].per_processor), 2)

    def test_Given_AnAllocationCountThatCreeps_When_DriftsAreFound_Then_ThereIsNone(self):
        # Arrange
        runs = daily({ONE: CREEP, TWO: CREEP}, name=COUNT)

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(len(found), 0)

    def test_Given_TheFirstRunAppendedLast_When_DriftsAreFound_Then_TheDriftIsDecidedAtTheLatestRunToLand(self):
        # Arrange
        runs = daily({ONE: CREEP, TWO: CREEP})
        runs = runs[1:] + runs[:1]

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(found[0].decided.commit, drift_commit(41))

    def test_Given_AProcessorThatStartedTwoDaysLater_When_DriftsAreFound_Then_TheFirstRunComparedIsTheOthersFirst(self):
        # Arrange
        runs = daily({ONE: [None] * 2 + CREEP[2:], TWO: CREEP})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(found[0].earliest.commit, drift_commit(1))

    # Five runs at the start have a median of 10 and six of 10.95; five at the end, 16 and six, 15.
    EDGES = [10, 10, 10, 11.9, 11.9] + [11.9] * 8 + [14] * 5 + [16] * 3

    def test_Given_ACreep_When_DriftsAreFound_Then_TheEarlierMedianIsOverAProcessorsFirstFiveRuns(self):
        # Arrange
        runs = daily({ONE: self.EDGES, TWO: self.EDGES})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(found[0].per_processor[0][2], 10)

    def test_Given_ACreep_When_DriftsAreFound_Then_TheLatestMedianIsOverAProcessorsLastFiveRuns(self):
        # Arrange
        runs = daily({ONE: self.EDGES, TWO: self.EDGES})

        # Act
        found = drifts(runs)

        # Assert
        self.assertEqual(found[0].per_processor[0][3], 16)

    def test_Given_ACreepWhoseFirstSixRunsHaveAHigherMedianThanItsFirstFive_When_DriftsAreFound_Then_ItsRatioIsOverTheFive(self):
        # Arrange: the first five have a median of 10 and the first six, 10.75; the last six are all 15.2.
        start = [10, 10, 10, 11.5, 11.5] + [11.5] * 6 + [13.2] * 5 + [15.2] * 6
        runs = daily({ONE: start, TWO: start})

        # Act
        found = drifts(runs)

        # Assert
        self.assertAlmostEqual(found[0].ratio, 1.52)

    def test_Given_ACreepWhoseLastSixRunsHaveALowerMedianThanItsLastFive_When_DriftsAreFound_Then_ItsRatioIsOverTheFive(self):
        # Arrange: the first six are all 10; the last five have a median of 15.5 and the last six, 14.5.
        end = [10] * 6 + [11.5] * 6 + [13.5] * 6 + [15.5] * 3
        runs = daily({ONE: end, TWO: end})

        # Act
        found = drifts(runs)

        # Assert
        self.assertAlmostEqual(found[0].ratio, 1.55)

    def test_Given_ACreep_When_Replayed_Then_AnIssueOpensAtTheFirstPrefixHoldingTheDrift(self):
        # Arrange
        runs = daily({ONE: CREEP, TWO: CREEP})
        first = next(end for end in range(1, len(runs) + 1) if drifts(runs[:end]))

        # Act
        issues = benchmark_steps.replay(runs, UNITS)

        # Assert
        self.assertEqual(issues.issues[0]["opened"], first)


class HeldTests(unittest.TestCase):

    def test_Given_ThreeRunsSinceAStep_When_ItIsAskedWhetherTheStepHolds_Then_ItDoes(self):
        # Arrange
        since, before = [15] * 3, [10] * 5

        # Act
        holds = benchmark_steps.held(since, before, benchmark_steps.DRIFT)

        # Assert
        self.assertTrue(holds)

    def test_Given_ThreeRunsBeforeAStep_When_ItIsAskedWhetherTheStepHolds_Then_ItDoes(self):
        # Arrange: five runs since at the old level would undo a step with five before it.
        since, before = [10] * 5, [10] * 3

        # Act
        holds = benchmark_steps.held(since, before, benchmark_steps.DRIFT)

        # Assert
        self.assertTrue(holds)

    def test_Given_TenRunsBeforeAStepThatFellFrom20To10_When_ItIsAskedWhetherTheStepHolds_Then_TheBarIsFromTheLatestFive(self):
        # Arrange: 12 is 1.2x the latest five before the step and 0.6x the five before those.
        since, before = [12] * 5, [20] * 5 + [10] * 5

        # Act
        holds = benchmark_steps.held(since, before, benchmark_steps.DRIFT)

        # Assert
        self.assertTrue(holds)

    def test_Given_RunsSinceAStepWhoseLatestFiveAloneHaveALowMedian_When_ItIsAskedWhetherTheStepHolds_Then_ItDoesNot(self):
        # Arrange
        since, before = [15] * 7 + [10] * 3, [10] * 5

        # Act
        holds = benchmark_steps.held(since, before, benchmark_steps.DRIFT)

        # Assert
        self.assertFalse(holds)

    def test_Given_FiveRunsSinceAStepWithOneFarBelowTheRest_When_ItIsAskedWhetherTheStepHolds_Then_ItDoes(self):
        # Arrange: their median is 12, 1.2x the 10 before; their mean is 9.8.
        since, before = [12, 12, 12, 12, 1], [10] * 5

        # Act
        holds = benchmark_steps.held(since, before, benchmark_steps.DRIFT)

        # Assert
        self.assertTrue(holds)


CREEPING = daily({ONE: CREEP, TWO: CREEP})


def drift_issue(number, state, index, name=NAME):
    """An issue a drift of `name` was filed in at the run of CREEPING's layout numbered `index`."""
    return issue(number, state, benchmark_steps.DRIFT_MARKER.format(series=name, decided=drift_commit(index)))


def sync_drifts(runs, gh, units=UNITS):
    return benchmark_steps.sync_drift_issues(runs, units, "o/r", gh)


class SyncDriftIssuesTests(unittest.TestCase):

    def test_Given_ADriftNoIssueNames_When_DriftIssuesSync_Then_OneOpensLabelledAndAssigned(self):
        # Arrange
        gh = FakeGh([])

        # Act
        sync_drifts(CREEPING, gh)

        # Assert
        self.assertEqual(" ".join(gh.calls[1][-6:]), "--assignee s4k10503 --label performance --label bug")

    def test_Given_TwoBenchmarksThatDrift_When_DriftIssuesSync_Then_OneIssueOpensForBoth(self):
        # Arrange
        runs = beside(CREEPING, daily({ONE: CREEP, TWO: CREEP}, name=OTHER))
        gh = FakeGh([])

        # Act
        sync_drifts(runs, gh, BOTH)

        # Assert
        self.assertEqual(len(benchmark_steps.DRIFT_MARKER_PATTERN.findall(created(gh)[0])), 2)

    def test_Given_AClosedIssueFiledAtTheLatestRun_When_DriftIssuesSync_Then_NothingIsWritten(self):
        # Arrange
        gh = FakeGh([drift_issue(3, "closed", 41)])

        # Act
        sync_drifts(CREEPING, gh)

        # Assert
        self.assertEqual(len(gh.calls), 1)

    def test_Given_AnIssueFiledAtTheFifthRun_When_DriftIssuesSync_Then_TheDriftComparesFromTheSixth(self):
        # Arrange
        gh = FakeGh([drift_issue(3, "closed", 4)])

        # Act
        sync_drifts(CREEPING, gh)

        # Assert
        self.assertIn(f"/compare/{drift_commit(5)}...", created(gh)[0])

    def test_Given_AFurtherCreepAfterADriftWasFiled_When_DriftIssuesSync_Then_OneOpens(self):
        # Arrange: 13 is 1.3x 10, filed at day 17; 17.2 is a further 1.32x, reached at day 33.
        twice = ([10] * 6 + [10.7] * 3 + [11.4] * 3 + [12.2] * 3 + [13] * 9
                 + [13.9] * 3 + [14.8] * 3 + [15.8] * 3 + [17.2] * 6)
        gh = FakeGh([drift_issue(3, "closed", 35)])

        # Act
        sync_drifts(daily({ONE: twice, TWO: twice}), gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_AnIssueFiledAtACommitTheHistoryDoesNotHold_When_DriftIssuesSync_Then_OneOpens(self):
        # Arrange
        gh = FakeGh([issue(3, "closed", benchmark_steps.DRIFT_MARKER.format(series=NAME, decided="e" * 40))])

        # Act
        sync_drifts(CREEPING, gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_AStepIssueNamingThisBenchmarkAtTheLatestRun_When_DriftIssuesSync_Then_OneOpens(self):
        # Arrange
        gh = FakeGh([issue(3, "open", benchmark_steps.MARKER.format(series=NAME, first=drift_commit(41)))])

        # Act
        sync_drifts(CREEPING, gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_ADriftIssueNamingThisBenchmarkAtAFreshStepsFirstSlowCommit_When_IssuesSync_Then_TheStepOpensOne(self):
        # Arrange
        fresh = step(last=29, first=30)
        gh = FakeGh([issue(3, "open", benchmark_steps.DRIFT_MARKER.format(series=NAME, decided=commit(30)))])

        # Act
        benchmark_steps.sync_issues([fresh], RUNS, "o/r", gh)

        # Assert
        self.assertEqual(len(created(gh)), 1)

    def test_Given_TwoDriftIssuesListedNewestFirst_When_TheFiledCommitsAreRead_Then_TheLaterOneIsTheBenchmarks(self):
        # Arrange
        issues = [drift_issue(4, "open", 30), drift_issue(3, "closed", 10)]

        # Act
        filed = benchmark_steps.filed_drifts(issues, CREEPING)

        # Assert
        self.assertEqual(filed[NAME], drift_commit(30))

    def test_Given_AnIssueAtARunAppendedLastThatLandedEarlier_When_TheFiledCommitsAreRead_Then_TheLaterToLandIsTheBenchmarks(self):
        # Arrange: the run at d030 was appended after every other run.
        runs = CREEPING[:30] + CREEPING[31:] + CREEPING[30:31]
        issues = [drift_issue(3, "closed", 30), drift_issue(4, "open", 35)]

        # Act
        filed = benchmark_steps.filed_drifts(issues, runs)

        # Assert
        self.assertEqual(filed[NAME], drift_commit(35))


DRIFT_RUNS = daily({ONE: [10] * 40})


def drift(earliest=10, decided=39, name=NAME):
    """A drift built directly over DRIFT_RUNS, from 10 to 15 on two processors and 1.3x with its steps left out."""
    first = DRIFT_RUNS[earliest]
    return benchmark_steps.Drift(name, "ms", first, DRIFT_RUNS[decided], 1.3,
                                 ((ONE, first, 10, 15, 1.3), (TWO, first, 10, 15, 1.3)))


class RenderDriftsTests(unittest.TestCase):

    def test_Given_ADrift_When_Rendered_Then_TheBodyLinksTheCommitsFromItsFirstRunComparedToItsLatest(self):
        # Arrange
        found = [drift()]

        # Act
        _, body = benchmark_steps.render_drifts(found, "o/r")

        # Assert
        self.assertIn(f"https://github.com/o/r/compare/{drift_commit(10)}...{drift_commit(39)}", body)

    def test_Given_ADrift_When_Rendered_Then_EachProcessorsRowReadsEarlierLatestAndTheRatioWithoutSteps(self):
        # Arrange
        found = [drift()]

        # Act
        _, body = benchmark_steps.render_drifts(found, "o/r")

        # Assert
        self.assertIn(f"| {NAME} | {ONE} | `{drift_commit(10)[:8]}` | 10 ms | 15 ms | 1.30x |", body)

    def test_Given_MoreDriftsThanTheBodyLimitHolds_When_Rendered_Then_TheBodyIsCutToIt(self):
        # Arrange
        many = [drift(name=f"B{index} / Time") for index in range(2000)]

        # Act
        _, body = benchmark_steps.render_drifts(many, "o/r")

        # Assert
        self.assertLessEqual(len(body), benchmark_steps.BODY_LIMIT)


if __name__ == "__main__":
    unittest.main(verbosity=2)
