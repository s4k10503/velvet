#!/usr/bin/env python3
"""The branch and base readings share a runner without sharing compiled assemblies."""

import os
import re
import subprocess
import textwrap
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = REPO_ROOT / ".github/workflows/test.yml"
ACTION = REPO_ROOT / ".github/actions/base-red-platform/action.yml"


def job(text, name):
    match = re.search(r"^  " + re.escape(name) + r":\s*\n(.*?)(?=^  [\w-]+:|\Z)",
                      text, re.MULTILINE | re.DOTALL)
    return match.group(1) if match else ""


def steps(text, indent):
    return re.findall(r"^" + " " * indent + r"- (.*?)(?=^" + " " * indent + r"- |\Z)",
                      text, re.MULTILINE | re.DOTALL)


def action_text():
    return ACTION.read_text() if ACTION.exists() else ""


class PlatformContinuationTests(unittest.TestCase):
    def test_Given_TheUnityMatrix_When_ItsContinuationIsRead_Then_BranchEvidencePrecedesBasePreparation(self):
        # Arrange
        unity = job(WORKFLOW.read_text(), "unity-tests")
        blocks = steps(unity, 6)
        continued = [i for i, block in enumerate(blocks) if "uses: ./.github/actions/base-red-platform" in block]
        evidence = [i for i, block in enumerate(blocks) if any(name in block for name in (
            "Assert no inconclusive results", "Assert the results are this checkout's",
            "name: Test results", "name: Code coverage"))]

        # Act
        ordered = len(continued) == 1 and len(evidence) == 4 and max(evidence) < continued[0]
        bounded = (len(continued) == 1 and "timeout-minutes: 30" in blocks[continued[0]]
                   and any("id: unity-tests" in block and "timeout-minutes: 25" in block for block in blocks))

        # Assert
        self.assertEqual((ordered, bounded, "fetch-depth: 0" in unity, "timeout-minutes: 55" in unity),
                         (True, True, True, True))

    def test_Given_TheContinuation_When_ItsGateIsRead_Then_ItRequiresSuccessfulBranchEvidenceOnAPullRequest(self):
        # Arrange
        blocks = steps(job(WORKFLOW.read_text(), "unity-tests"), 6)
        continued = next((block for block in blocks if "uses: ./.github/actions/base-red-platform" in block), "")

        # Act
        gated = "if: success() && github.event_name == 'pull_request'" in continued
        tolerated = "continue-on-error:" in continued or "always()" in continued

        # Assert
        self.assertEqual((gated, tolerated), (True, False))

    def test_Given_TheBaseAction_When_ItsRoundsAreRead_Then_FourRoundsEndInANonToleratedVerdict(self):
        # Arrange
        initial = steps(action_text(), 4)
        blocks = steps(job(WORKFLOW.read_text(), "unity-tests"), 6)

        # Act
        first = [block for block in initial if "uses: game-ci/unity-test-runner@" in block]
        retries = [block for block in blocks if "uses: game-ci/unity-test-runner@" in block
                   and "projectPath: base-tree" in block]
        prepared = [block for block in blocks if "uses: ./.github/actions/base-red-round" in block]
        verdicts = [block for block in blocks if "--verdict plan.json --results base-results" in block]
        gated = (len(first) == 1 and "if: steps.read.outputs.fixtures != ''" in first[0]
                 and all("if: steps.base-red-prepare{}.outputs.fixtures != ''".format(i + 2) in block
                         for i, block in enumerate(retries)))
        ordered = (len(verdicts) == 1 and len(retries) == 3
                   and blocks.index(verdicts[0]) > blocks.index(retries[-1])
                   and "continue-on-error:" not in verdicts[0]
                   and "if: success() && steps.base-red-initial.outputs.fixtures != ''" in verdicts[0])
        continuation = all("if: success() && steps.{}.outputs.fixtures != ''".format(
            "base-red-initial" if i == 0 else "base-red-prepare{}".format(i + 1)) in block
                           for i, block in enumerate(prepared))

        # Assert
        self.assertEqual((len(first) + len(retries), len(prepared), gated, ordered, continuation),
                         (4, 3, True, True, True))

    def test_Given_TheBaseAction_When_ItsCacheIsRead_Then_CompiledBranchAssembliesCannotEnterItsFirstRound(self):
        # Arrange
        text = action_text()
        blocks = steps(text, 4)
        cache = next((i for i, block in enumerate(blocks) if "uses: actions/cache@" in block), -1)
        purge = next((i for i, block in enumerate(blocks) if "rm -rf base-tree/Library/ScriptAssemblies" in block), -1)
        editor = next((i for i, block in enumerate(blocks) if "uses: game-ci/unity-test-runner@" in block), -1)

        # Act
        separated = (0 <= cache < purge < editor and "path: base-tree/Library" in text
                     and "BaseLibrary-${{ inputs.testMode }}-${{ github.event.pull_request.number }}" in text
                     and "projectPath: base-tree" in text and "artifactsPath: base-results" in text
                     and "BASE: ${{ steps.base.outputs.sha }}" in text
                     and "scripts/ci/change_base.py" in text and "--platform \"${{ inputs.testMode }}\"" in text)

        # Assert
        self.assertTrue(separated)

    def test_Given_ABaseAttemptThatFails_When_ArtifactsArePublished_Then_TheTraceAndPlanRemainAvailable(self):
        # Arrange
        blocks = steps(job(WORKFLOW.read_text(), "unity-tests"), 6)
        artifact = next((block for block in blocks if "name: Base results (" in block), "")

        # Act
        retained = ("if: always() && github.event_name == 'pull_request'" in artifact
                    and "steps.base-red-initial.outputs.fixtures != ''" in artifact
                    and all(path in artifact for path in ("base-results*", "plan.json", "reading.txt", "again.txt")))
        verdict = next((block for block in blocks if "--verdict plan.json" in block), "")
        ordered = bool(artifact) and bool(verdict) and blocks.index(artifact) > blocks.index(verdict)

        # Assert
        self.assertEqual((retained, ordered), (True, True))

    # GREEN_ON_BASE(characterization): the existing aggregate already rejects failed Unity jobs.
    # What shows the case can fail is removing `needs.unity-tests.result` from its RESULTS expression.
    def test_Given_AFailedPlatformReading_When_TheRequiredAggregateRuns_Then_ItRejectsTheUnityResult(self):
        # Arrange
        aggregate = job(WORKFLOW.read_text(), "required-checks")
        references = re.findall(r"needs\.([\w-]+)\.result", aggregate)
        command = textwrap.dedent(aggregate.partition("        run: |\n")[2])
        outcomes = ["failure" if name == "unity-tests" else "success" for name in references]

        # Act
        result = subprocess.run(["bash", "-c", command], capture_output=True, text=True,
                                env={**os.environ, "RESULTS": " ".join(outcomes)})
        control = subprocess.run(["bash", "-c", command], capture_output=True, text=True,
                                 env={**os.environ, "RESULTS": " ".join("success" for _ in references)})

        # Assert
        self.assertEqual((result.returncode, control.returncode), (1, 0))


def base_runner_ids():
    """Expand composite calls with the top-level GITHUB_ACTION inherited by embedded steps."""
    found = []

    def expand(block, action_id):
        if "uses: game-ci/unity-test-runner@" in block and "projectPath: base-tree" in block:
            found.append(action_id)
        elif "uses: ./.github/actions/" in block:
            path = re.search(r"uses: (\./\.github/actions/[\w-]+)", block).group(1)
            for child in steps((REPO_ROOT / path / "action.yml").read_text(), 4):
                expand(child, action_id)

    for block in steps(job(WORKFLOW.read_text(), "unity-tests"), 6):
        identifier = re.search(r"(?:^|\n\s*)id: ([\w-]+)", block)
        expand(block, identifier.group(1) if identifier else "__self")
    return found


def base_red_test_helpers():
    import importlib.util
    path = REPO_ROOT / "scripts/test_quality/test_base_red_check.py"
    spec = importlib.util.spec_from_file_location("base_red_retry_helpers", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ContainerRetryTests(unittest.TestCase):
    def exercise(self, retry_fails=False):
        import contextlib
        import io
        import json
        import sys

        helpers = base_red_test_helpers()
        helper = helpers.ReplanTests()
        self.addCleanup(helper.doCleanups)
        canary = "Packages/p/Runtime/A/Tests/Editor/CanaryTests.cs"
        base = {helper.ENUM: "enum Status { Idle }\n",
                helper.BLAMED: helper.fixture("ProbeTests", "Assert.Pass()"),
                helper.BESIDE: helper.fixture("OtherTests", "Assert.Pass()"),
                canary: helper.fixture("CanaryTests", "Assert.Pass()")}
        branch = {**base,
                  helper.BLAMED: helper.fixture("ProbeTests", "Assert.That(1, Is.EqualTo(1))"),
                  helper.BESIDE: helper.fixture("OtherTests", "Assert.That(2, Is.EqualTo(2))")}
        root, since = helpers.two_commit_repo(self, base, branch)
        tree = helpers.worktree_beside(self, root)
        emitted = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(root), "--base", since, "--lane", "csharp",
             "--emit", str(root / "plan.json"), "--base-tree", str(tree)],
            capture_output=True, text=True)
        if emitted.returncode:
            raise RuntimeError(emitted.stdout + emitted.stderr)
        results = root / "results"
        results.mkdir()
        runner_temp = root / "runner-temp"
        runner_temp.mkdir()
        commands = []

        def docker_stub(action_id, writes_results):
            cidfile = runner_temp / ("container_" + action_id)
            commands.append((action_id, cidfile.name))
            try:
                with cidfile.open("x") as stream:
                    stream.write("a" * 64)
            except FileExistsError:
                return 125
            if writes_results:
                plan = json.loads((root / "plan.json").read_text())
                out = set(plan.get("withdrawn", {})) | set(plan.get("blamed", {}))
                cases = {case["name"]: "Failed" for case in plan["cases"] if case["path"] not in out}
                cases.update({case["name"]: "Passed" for case in plan["control"]})
                for names in plan["canaries"].values():
                    for name in names:
                        cases[name + ".Given_A_When_B_Then_C"] = "Passed"
                (results / "results.xml").write_text("<test-run>" + "".join(
                    '<test-case fullname="{}" result="{}" />'.format(name, outcome)
                    for name, outcome in cases.items()) + "</test-run>")
                (results / "editmode.log").write_text("Completed base tests\n")
                return 0
            (results / "editmode.log").write_text(helper.BLAMED + "(6,9): error CS0246: missing type\n")
            return 1

        ids = base_runner_ids()
        if len(ids) != 4:
            raise RuntimeError("The workflow must still pose four base rounds")
        first = docker_stub(ids[0], False)
        held = io.StringIO()
        with contextlib.redirect_stdout(held):
            helpers.base_red_check.replan(root, root / "plan.json", results, tree)
        results.mkdir()
        retry = docker_stub(ids[1], not retry_fails)
        verdict = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(root), "--verdict", str(root / "plan.json"), "--results", str(results)],
            capture_output=True, text=True)
        trace = (root / "results.round1/editmode.log").read_text()
        plan = json.loads((root / "plan.json").read_text())
        return (first, retry, verdict.returncode, "CS0246" in trace,
                helper.BLAMED in plan.get("blamed", {}),
                "fixtures=" in held.getvalue(), commands, verdict.stdout)

    def test_Given_EmbeddedBaseRounds_When_TheirRunnerContextsAreExpanded_Then_TheirCidfilesAreDistinct(self):
        # Arrange / Act
        identifiers = base_runner_ids()

        # Assert
        self.assertEqual((len(identifiers), len(set(identifiers))), (4, 4))

    def test_Given_ACompilerBlamedFirstRound_When_ReplannedAndRetried_Then_TheBaseVerdictIsMeasured(self):
        # Arrange / Act -- Docker retains its cidfile after --rm; Unity is stubbed, replan/verdict are real.
        reading = self.exercise()

        # Assert
        self.assertEqual(reading[:6], (1, 0, 0, True, True, True))

    def test_Given_ARetryThatAlsoFails_When_TheVerdictRuns_Then_ItFailsAndKeepsTheFirstTrace(self):
        # Arrange / Act
        reading = self.exercise(retry_fails=True)

        # Assert
        self.assertEqual(reading[:6], (1, 1, 1, True, True, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)
