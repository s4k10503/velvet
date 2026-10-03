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
        blocks = steps(action_text(), 4)

        # Act
        editors = [block for block in blocks if "uses: game-ci/unity-test-runner@" in block
                   or "uses: ./.github/actions/base-red-round" in block]
        verdicts = [block for block in blocks if "--verdict plan.json --results base-results" in block]
        guarded = ["if: steps.read.outputs.fixtures != ''" in block if i < 2
                   else "if: steps.round{}.outputs.fixtures != ''".format(i) in block
                   for i, block in enumerate(editors)]

        # Assert
        self.assertEqual((len(editors), guarded, len(verdicts),
                          bool(verdicts) and "continue-on-error:" not in verdicts[0]
                          and blocks.index(verdicts[0]) > blocks.index(editors[-1])),
                         (4, [True] * 4, 1, True))

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


if __name__ == "__main__":
    unittest.main(verbosity=2)
