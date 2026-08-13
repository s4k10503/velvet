#!/usr/bin/env python3
"""Unit tests for mutation_check.py's mutant generation, its verdict, and what it holds while it runs.

Generation is separable from the Unity runs that kill or spare a mutant, so it is tested here and runs
without a licence. What a run then reports rests entirely on this half being right: a mutant that comes
back uncompilable is indistinguishable from one nothing tested, so an operator that emits broken C# does
not report a false survivor — it hides a real one behind noise.

Two halves need no editor either. Which survivors the run signs off is decided from the declarations in
the tree, and a campaign's hold on the working tree is a question about ordering rather than about
anything Unity does — so the cases below drive `main` with the editor stubbed out, which is the only way
to ask what the tree holds at a moment the campaign is inside.

Run: python3 scripts/test_quality/test_mutation_check.py
"""

import importlib.util
import os
import signal
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RUNTIME = REPO_ROOT / "Packages/com.velvet.core/Runtime"
GUIDE = REPO_ROOT / "CONTRIBUTING.md"


def load_module():
    """Imports mutation_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "mutation_check", Path(__file__).with_name("mutation_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


mutation_check = load_module()

GREEN_RESULTS = '<test-run total="1" passed="1" failed="0" inconclusive="0" />'


def mutants_of(text, operator=None):
    lines = set(range(1, len(text.splitlines()) + 1))
    found = mutation_check.mutations_for(Path("probe.cs"), text, lines)
    return [mutant for mutant in found if operator is None or mutant.operator == operator]


def applied(text, mutant):
    return mutation_check.apply_mutation(text, mutant).splitlines()[mutant.line - 1].strip()


def survivor(path, line, verdict=None):
    mutant = mutation_check.Mutant(path, line, 0, "a", "b", "equality")
    mutant.verdict = verdict or mutation_check.SURVIVED
    return mutant


def declaration(line, category="equivalent", reason="a reason of four words", written_here=True):
    return mutation_check.Declaration(category, reason, line, written_here)


class CodeMaskTests(unittest.TestCase):
    """Which offsets count as code. Everything downstream of the mask reads only what it leaves.

    Nothing reports an offset wrongly blanked: a mutant is never generated there, a brace never counted,
    and both halves come back looking like a file with less in it than it has.
    """

    def masked_out(self, text, fragment):
        mask = mutation_check.code_mask(text)
        start = text.index(fragment)
        return [text[offset] for offset in range(start, start + len(fragment)) if not mask[offset]]

    def test_Given_AnApostropheInAPreprocessorDirective_When_TheMaskIsRead_Then_TheCodeBelowIsCode(self):
        # Arrange -- `#region Boundary's own tree` is free-form text after the directive, but reading it
        # as code opens a character literal that runs to the next apostrophe anywhere in the file.
        text = "#region Boundary's own tree\nif (a <= b) { }\n#endregion\n"

        # Act / Assert
        self.assertEqual(self.masked_out(text, "if (a <= b) { }"), [])

    def test_Given_ALoneApostropheInCode_When_TheMaskIsRead_Then_ItSwallowsNoLaterLine(self):
        # Arrange -- twelve characters is the longest literal C# can spell, `'\\U0001F600'`, so an
        # apostrophe with no closing one inside that reach is not one and consumes nothing.
        text = "var x = a ' b;\nif (c <= d) { }\n"

        # Act / Assert
        self.assertEqual(self.masked_out(text, "if (c <= d) { }"), [])

    # GREEN_ON_BASE(characterization): the masking the two cases above are read against.
    def test_Given_ARealCharacterLiteral_When_TheMaskIsRead_Then_ItIsStillNotCode(self):
        # Arrange -- the counterpart, so the two above are not passing for a mask that blanks nothing.
        text = "var separator = ';';\n"

        # Act / Assert
        self.assertEqual(self.masked_out(text, "';'"), ["'", ";", "'"])


class ClauseRemovalTests(unittest.TestCase):
    def test_Given_AThreeClauseCondition_When_MutantsAreGenerated_Then_EachTrailingClauseIsRemovable(self):
        # Arrange
        text = "if (a == null || b <= 0 || b > c.Count)\n"

        # Act
        cuts = sorted(applied(text, mutant) for mutant in mutants_of(text, "clause removed"))

        # Assert
        self.assertEqual(cuts, ["if (a == null || b <= 0)", "if (a == null || b > c.Count)"])

    def test_Given_AClauseHoldingItsOwnParentheses_When_ItIsRemoved_Then_TheLineStaysBalanced(self):
        # Arrange
        text = "if (a || Ready(x, y) && Live(z))\n"

        # Act
        balances = {applied(text, m).count("(") - applied(text, m).count(")")
                    for m in mutants_of(text, "clause removed")}

        # Assert
        self.assertEqual(balances, {0})

    def test_Given_AJoinInsideANestedGroup_When_ItIsRemoved_Then_TheOuterGroupIsUntouched(self):
        # Arrange
        text = "if (a && (b || c))\n"

        # Act
        cuts = sorted(applied(text, mutant) for mutant in mutants_of(text, "clause removed"))

        # Assert
        self.assertEqual(cuts, ["if (a && (b))", "if (a)"])

    def test_Given_AJoinInsideAStringLiteral_When_MutantsAreGenerated_Then_ItIsNotACut(self):
        # Arrange
        text = 'var message = "a && b";\n'

        # Act
        cuts = mutants_of(text, "clause removed")

        # Assert
        self.assertEqual(cuts, [])

    def test_Given_AChainsFirstClause_When_MutantsAreGenerated_Then_ItIsNotOffered(self):
        # Arrange — where the first clause begins cannot be read off the line, so it is left alone.
        text = "if (a == null || b <= 0)\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["if (a == null)"])


class GuardRemovalTests(unittest.TestCase):
    def test_Given_ASingleLineReturnGuard_When_ItIsRemoved_Then_OnlyTheGuardGoes(self):
        # Arrange
        text = "        if (ownCts != _cts) return;\n"

        # Act
        results = [applied(text, mutant) for mutant in mutants_of(text, "guard removed")]

        # Assert
        self.assertEqual(results, [""])

    def test_Given_AGuardWhoseBodyIsABlock_When_MutantsAreGenerated_Then_ItIsNotOffered(self):
        # Arrange — deleting the condition of a multi-line guard would leave its block dangling.
        text = "if (ownCts != _cts)\n{\n    return;\n}\n"

        # Act
        guards = mutants_of(text, "guard removed")

        # Assert
        self.assertEqual(guards, [])

    def test_Given_AnAssignmentThatMerelyMentionsReturn_When_MutantsAreGenerated_Then_ItIsNotAGuard(self):
        # Arrange
        text = "var returned = Compute(a);\n"

        # Act
        guards = mutants_of(text, "guard removed")

        # Assert
        self.assertEqual(guards, [])


class RepositoryReachTests(unittest.TestCase):
    """The operators exist because two real defects survived every other one. Pin that they reach them."""

    def test_Given_TheNestedOutletGuard_When_MutantsAreGenerated_Then_ItsDepthClauseIsRemovable(self):
        # Arrange — a route depth past the matched chain must return null rather than index past its end.
        source = (RUNTIME / "Hooks/Hooks.cs").read_text()
        line = next(number for number, text in enumerate(source.splitlines(), 1)
                    if "depth > location.Matches.Count" in text)

        # Act
        cuts = [mutant.before.strip() for mutant
                in mutation_check.mutations_for(Path("Hooks.cs"), source, {line})
                if mutant.operator == "clause removed"]

        # Assert
        self.assertIn("|| depth > location.Matches.Count", cuts)

    def test_Given_TheSupersessionChecks_When_MutantsAreGenerated_Then_EachIsRemovable(self):
        # Arrange — a superseded loader round must not report into the live one.
        source = (RUNTIME / "Routing/RouteLoaderRunner.cs").read_text()

        # Act
        guards = {mutant.before for mutant in mutants_of(source, "guard removed")}

        # Assert
        self.assertEqual(guards, {"if (ownCts != _cts) return;"})


class GenerationHealthTests(unittest.TestCase):
    def test_Given_EveryRuntimeSource_When_MutantsAreGenerated_Then_NoCutSpansAnUnbalancedParenthesis(self):
        # Arrange — an unbalanced cut compiles nowhere, and uncompilable noise hides real survivors.
        sources = [path for path in RUNTIME.rglob("*.cs") if "/Tests/" not in path.as_posix()]

        # Act
        unbalanced = []
        for path in sources:
            text = path.read_text()
            for mutant in mutants_of(text, "clause removed"):
                if mutant.before.count("(") != mutant.before.count(")"):
                    unbalanced.append(f"{path.name}:{mutant.line} {mutant.before.strip()}")

        # Assert — the source count rides along because an empty scan satisfies "none unbalanced".
        self.assertEqual((len(sources) > 200, unbalanced), (True, []))

    def test_Given_EveryRuntimeSource_When_MutantsAreGenerated_Then_NoRemovalLeavesADanglingJoin(self):
        # Arrange
        sources = [path for path in RUNTIME.rglob("*.cs") if "/Tests/" not in path.as_posix()]

        # Act
        dangling = []
        for path in sources:
            text = path.read_text()
            for mutant in mutants_of(text, "clause removed"):
                remainder = applied(text, mutant)
                if remainder.endswith(("&&", "||")) or "&&)" in remainder or "||)" in remainder:
                    dangling.append(f"{path.name}:{mutant.line} {remainder}")

        # Assert
        self.assertEqual((len(sources) > 200, dangling), (True, []))


class MaskDefectTests(unittest.TestCase):
    """A construct read as one it is not blanks code, and a blanked offset generates no mutant silently.

    The mask cannot be proved right about a file. What it can be held to is that it never reads a
    one-line construct as running past its line, which is the shape every miscount it has had took.
    """

    def test_Given_AnUnterminatedStringLiteral_When_TheFileIsRead_Then_TheBlankingIsReported(self):
        # Arrange — the quote runs to the end of the file, taking every line under it out of the mask.
        text = 'var greeting = "unterminated;\nif (a <= b) { }\nif (c == d) { }\n'

        # Act
        kinds = [kind for _, _, kind in mutation_check.mask_defects(text)]

        # Assert
        self.assertEqual(kinds, [mutation_check.STRING])

    def test_Given_ABlockCommentOverSeveralLines_When_TheFileIsRead_Then_NothingIsReported(self):
        # Arrange — the counterpart, so the case above is not passing for a check that reports any span.
        text = "/* a comment\n   over three\n   lines */\nif (a <= b) { }\n"

        # Act — the blanking rides along, because a report of nothing is also what a scanner that read
        # no span at all would produce, and that would pass this on an arrangement doing nothing.
        blanked = "".join("." if seen else "#" for seen in mutation_check.code_mask(text)[:12])
        defects = mutation_check.mask_defects(text)

        # Assert
        self.assertEqual((blanked, defects), ("############", []))

    def test_Given_EveryPackageSource_When_TheMaskIsRead_Then_NoneOfThemIsBlanked(self):
        # Arrange — the campaign refuses a target file the mask misreads, so one reaching main would
        # stop every branch that touches it rather than mis-measuring quietly.
        sources = [path for path in (REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs")]

        # Act
        blinded = ["{}: {}".format(path.name, mutation_check.mask_defects(path.read_text()))
                   for path in sources if mutation_check.mask_defects(path.read_text())]

        # Assert — the source count rides along because an empty scan satisfies "none blanked".
        self.assertEqual((len(sources) > 200, blinded), (True, []))


class BuildErrorTests(unittest.TestCase):
    """A build this repository's own analyzers stop writes no results file and no CS diagnostic.

    Read for CS alone it lands on the verdict for a run that wrote nothing, which reads as an editor
    that crashed and sends whoever is debugging it to the wrong half of the harness.
    """

    def blame(self, line):
        holder = Path(tempfile.mkdtemp(prefix="mutation-check-")) / "run.log"
        holder.write_text(line)
        return mutation_check.build_error(holder)

    def test_Given_AnAnalyzerErrorInTheLog_When_ItIsRead_Then_TheSourceIsBlamed(self):
        # Arrange — VEL501 is this repository's branching-complexity limit, reported as an error.
        line = "Packages/com.velvet.core/Runtime/Probe.cs(12,9): error VEL501: too many branches\n"

        # Act / Assert
        self.assertEqual(self.blame(line), "Packages/com.velvet.core/Runtime/Probe.cs")

    def test_Given_ACompilerErrorInTheLog_When_ItIsRead_Then_TheSourceIsBlamed(self):
        # Arrange — the case the check already covered, so widening it did not drop the old one.
        line = "Assets/Probe.cs(3,1): error CS1002: ; expected\n"

        # Act / Assert
        self.assertEqual(self.blame(line), "Assets/Probe.cs")

    def test_Given_AnAssertionMessageQuotingTheWords_When_ItIsRead_Then_NothingIsBlamed(self):
        # Arrange — a failing test whose message reads as a build error would turn a kill into noise.
        line = "  Expected: no error CS1002 anywhere\n  But was: Probe.cs(3,1): error CS1002\n"

        # Act / Assert
        self.assertEqual(self.blame(line), None)


class DeclarationReadingTests(unittest.TestCase):
    def test_Given_ADeclarationOverALine_When_ItIsRead_Then_ItAnswersForThatLine(self):
        # Arrange
        text = "// MUTANT_SURVIVES(equivalent): both spellings agree here.\nif (a <= b) { }\n"

        # Act
        found = [(subject, held.category) for subject, held in mutation_check.declarations_in(text)]

        # Assert
        self.assertEqual(found, [(2, "equivalent")])

    def test_Given_ABlankLineUnderADeclaration_When_ItIsRead_Then_ItAnswersForNothing(self):
        # Arrange — prose over a gap belongs to whatever sits under it, and reaching past the gap
        # would let one line's answer cover a neighbour nobody wrote it for.
        text = "// MUTANT_SURVIVES(equivalent): both spellings agree here.\n\nif (a <= b) { }\n"

        # Act
        found = mutation_check.declarations_in(text)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADeclarationHeadingACommentBlock_When_ItIsRead_Then_ItReachesPastTheRestOfIt(self):
        # Arrange
        text = ("// MUTANT_SURVIVES(equivalent): both spellings agree here.\n"
                "// A second line of the same block.\n"
                "if (a <= b) { }\n")

        # Act
        found = [subject for subject, _ in mutation_check.declarations_in(text)]

        # Assert
        self.assertEqual(found, [3])


class VerdictTests(unittest.TestCase):
    """Which survivors the run signs off. Every reading here is one `base_red_check.py` takes too."""

    def decide(self, mutants, declared, deferred=frozenset()):
        unanswered, stale = mutation_check.answered(mutants, set(deferred), declared)
        return ([complaint for _, complaint in unanswered],
                [held.line for _, _, held in stale])

    def test_Given_ASurvivorNothingDeclares_When_TheRunIsDecided_Then_ItIsUnanswered(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12)]

        # Act
        unanswered, _ = self.decide(mutants, {})

        # Assert
        self.assertEqual(unanswered, ["nothing above this line answers for it"])

    def test_Given_ADeclaredSurvivor_When_TheRunIsDecided_Then_NothingIsUnanswered(self):
        # Arrange — the counterpart: without it every case here passes for a gate that refuses always.
        mutants = [survivor(Path("probe.cs"), 12)]

        # Act
        unanswered, _ = self.decide(mutants, {(Path("probe.cs"), 12): declaration(11)})

        # Assert
        self.assertEqual(unanswered, [])

    def test_Given_ASurvivorDeclaredWithACategoryTheScriptRefuses_When_ItIsDecided_Then_ItIsUnanswered(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12)]
        declared = {(Path("probe.cs"), 12): declaration(11, category="fine")}

        # Act
        unanswered, _ = self.decide(mutants, declared)

        # Assert
        self.assertEqual(unanswered, ["category 'fine' is not one of equivalent, unreachable"])

    def test_Given_ASurvivorDeclaredWithoutAReason_When_ItIsDecided_Then_ItIsUnanswered(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12)]
        declared = {(Path("probe.cs"), 12): declaration(11, reason="see above")}

        # Act
        unanswered, _ = self.decide(mutants, declared)

        # Assert
        self.assertEqual(unanswered, ["the reason is under 4 words"])

    def test_Given_ASurvivorDeclaredByTheBaseRatherThanThisBranch_When_ItIsDecided_Then_ItIsUnanswered(self):
        # Arrange — a declaration answers for the change under it. One the branch did not write
        # answers for a change the base already carries.
        mutants = [survivor(Path("probe.cs"), 12)]
        declared = {(Path("probe.cs"), 12): declaration(11, written_here=False)}

        # Act
        unanswered, _ = self.decide(mutants, declared)

        # Assert
        self.assertEqual(unanswered,
                         ["its declaration is the base's own; restate it for this change"])

    def test_Given_ADeclarationOverALineWhoseMutantsAllDied_When_ItIsDecided_Then_ItIsStale(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED)]

        # Act
        _, stale = self.decide(mutants, {(Path("probe.cs"), 12): declaration(11)})

        # Assert
        self.assertEqual(stale, [11])

    def test_Given_ADeclarationOverALineTheCapLeftUnrun_When_ItIsDecided_Then_ItIsNotStale(self):
        # Arrange — the cap fails the run on its own, and a line nothing ran says nothing about a
        # declaration over it.
        deferred = {(Path("probe.cs"), 12)}

        # Act
        _, stale = self.decide([], {(Path("probe.cs"), 12): declaration(11)}, deferred)

        # Assert
        self.assertEqual(stale, [])

    def test_Given_ADeclarationTheBranchDidNotWriteOverAKilledLine_When_ItIsDecided_Then_ItIsNotStale(self):
        # Arrange — it answers for the base's change, and this branch is not being asked to keep it
        # current.
        declared = {(Path("probe.cs"), 12): declaration(11, written_here=False)}

        # Act
        _, stale = self.decide([survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED)], declared)

        # Assert
        self.assertEqual(stale, [])


class DeclarationFormatTests(unittest.TestCase):
    """What the guide shows and what the tree holds, against what the script will actually accept.

    A declaration whose category the script refuses is refused by it and reads to everyone else as an
    approved exemption, so the two have to agree.
    """

    def test_Given_TheDeclarationTheGuideShows_When_TheScriptsOwnPatternReadsIt_Then_ItIsAccepted(self):
        # Arrange
        shown = [mutation_check.Declaration(match.group(1), match.group(2).strip(), 0)
                 for match in mutation_check.DECLARATION.finditer(GUIDE.read_text())]

        # Act
        complaints = [held.complaint for held in shown if held.complaint]

        # Assert — the count rides along because a guide showing no example passes on the rest.
        self.assertEqual((len(shown) >= 1, complaints), (True, []))

    def test_Given_EveryDeclarationInThePackage_When_ItIsRead_Then_TheScriptAcceptsIt(self):
        # Arrange
        sources = list((REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs"))

        # Act
        complaints = ["{}:{} {}".format(path.name, held.line, held.complaint)
                      for path in sources
                      for _, held in mutation_check.declarations_in(path.read_text())
                      if held.complaint]

        # Assert — the source count rides along because a scan over nothing satisfies "no complaint",
        # and the tree carries no declaration yet, so that is the state this would pass in.
        self.assertEqual((len(sources) > 200, complaints), (True, []))

    def test_Given_EveryDeclarationInThePackage_When_ItsLineIsMutated_Then_ThereIsAMutantToAnswerFor(self):
        # Arrange — the campaign calls a declaration stale when the line under it produced no
        # survivor, and one over a line that generates nothing at all can never be anything else.
        # It reads as an approved exemption until a campaign happens to run over that file.
        sources = list((REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs"))

        # Act
        answering = []
        for path in sources:
            text = path.read_text()
            for subject, held in mutation_check.declarations_in(text):
                if not mutation_check.mutations_for(path, text, {subject}):
                    answering.append("{}:{}".format(path.name, held.line))

        # Assert — the source count rides along for the reason the case above carries.
        self.assertEqual((len(sources) > 200, answering), (True, []))


class StubbedCampaign:
    """A project of one mutable source, with the editor replaced by a file write.

    The editor is what a campaign spends its time in and is not what any case here asks about. What
    they ask about is the working tree at a moment the campaign is inside `wait_for_quiet`, which no
    reading taken after it returns can answer.
    """

    SOURCE = "Packages/com.velvet.core/Runtime/Probe.cs"

    def __init__(self, body=None):
        self.holder = tempfile.mkdtemp(prefix="mutation-check-")
        self.project = Path(self.holder)
        self.source = self.project / self.SOURCE
        self.source.parent.mkdir(parents=True, exist_ok=True)
        self.source.write_text(body if body is not None else textwrap.dedent("""\
            namespace Velvet
            {
                internal static class Probe
                {
                    internal static bool Ready(int a, int b) => a <= b;
                }
            }
            """))
        (self.project / "Library" / "ScriptAssemblies").mkdir(parents=True, exist_ok=True)
        self.seen = []
        self.busy = []

    def run_suite(self, _unity, _project, _platform, _scope, results, log, _timeout, _holder=None):
        Path(results).write_text(GREEN_RESULTS)
        Path(log).write_text("")
        return 0.0, False

    def unity_busy(self):
        self.seen.append(self.source.read_text())
        return self.busy.pop(0) if self.busy else 0

    def run(self, *arguments):
        """Drives `main` over this project, returning what it exited with."""
        saved = (sys.argv, mutation_check.run_suite, mutation_check.unity_busy)
        sys.argv = ["mutation_check.py", "--project", str(self.project),
                    "--files", str(self.source), "--output", str(self.project / "out"),
                    *arguments]
        mutation_check.run_suite = self.run_suite
        mutation_check.unity_busy = self.unity_busy
        try:
            return mutation_check.main()
        except SystemExit as stop:
            return stop.code
        finally:
            sys.argv, mutation_check.run_suite, mutation_check.unity_busy = saved


class QueuedCampaignTests(unittest.TestCase):
    def test_Given_ACampaignWaitingForTheEditor_When_TheTreeIsRead_Then_ItHoldsNoMutation(self):
        # Arrange — the machine is free for the baseline and busy for the first mutant, which is the
        # state both mutations found in a tree after a killed campaign were left in.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        campaign.busy = [0, 1]

        # Act — the wait fails, so what matters is only what was on disk while it was waiting.
        campaign.run("--max", "1", "--busy-timeout", "0")

        # Assert
        self.assertEqual(campaign.seen[1], original)

    def test_Given_ACampaignWaitingForTheEditor_When_TheWaitRunsOut_Then_ItStopsRatherThanSharing(self):
        # Arrange — a second editor is a second explanation for every failure the mutant run reports.
        campaign = StubbedCampaign()
        campaign.busy = [0, 1]

        # Act
        code = campaign.run("--max", "1", "--busy-timeout", "0")

        # Assert
        self.assertIn("still in flight", str(code))


class OutstandingMutationTests(unittest.TestCase):
    def test_Given_ASentinelFromAKilledCampaign_When_ANewOneStarts_Then_ItRefusesToMeasure(self):
        # Arrange — the baseline would be taken over somebody else's mutation, and the restore at the
        # end would write it back as though it were the author's own code.
        campaign = StubbedCampaign()
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, "the original text", "Probe.cs:5 <= -> < (boundary)")

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertIn("holding a mutation in this tree", str(code))

    def test_Given_ASentinelFromAKilledCampaign_When_RestoreRuns_Then_TheSourceIsPutBack(self):
        # Arrange
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        holder = mutation_check.Holder(campaign.project / mutation_check.SENTINEL)
        holder.hold(campaign.source, original, "Probe.cs:5 <= -> < (boundary)")
        campaign.source.write_text(original.replace("a <= b", "a < b"))

        # Act
        campaign.run("--restore")

        # Assert
        self.assertEqual(campaign.source.read_text(), original)

    def test_Given_ASentinelFromAKilledCampaign_When_RestoreRuns_Then_TheSentinelGoesWithIt(self):
        # Arrange
        campaign = StubbedCampaign()
        sentinel = campaign.project / mutation_check.SENTINEL
        mutation_check.Holder(sentinel).hold(campaign.source, campaign.source.read_text(), "x")

        # Act
        campaign.run("--restore")

        # Assert
        self.assertEqual(sentinel.exists(), False)

    def test_Given_ASentinelNothingCanRead_When_RestoreRuns_Then_ItSaysSoRatherThanNothingIsHeld(self):
        # Arrange — "no mutation is outstanding" over a record naming one is the reading this whole
        # mechanism exists to stop anybody taking.
        campaign = StubbedCampaign()
        (campaign.project / mutation_check.SENTINEL).write_text("{ truncated")

        # Act
        code = campaign.run("--restore")

        # Assert
        self.assertIn("could not put back", str(code))

    def test_Given_AHeldMutation_When_ACommitWouldRecordThatFile_Then_ItIsRefused(self):
        # Arrange — `git add -u` stages the mutation with everything else, and a reader cannot tell
        # it from an unfinished edit. One reached a commit this way with the record sitting beside it.
        campaign = StubbedCampaign()
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, campaign.source.read_text(), "Probe.cs:5 <= -> < (boundary)")

        # Act
        code = campaign.run("--carried", StubbedCampaign.SOURCE, "CONTRIBUTING.md")

        # Assert
        self.assertIn("campaign is holding", str(code))

    def test_Given_AHeldMutation_When_ACommitRecordsOtherFiles_Then_ItIsAllowed(self):
        # Arrange — the counterpart: a campaign in flight must not stop every commit in the tree.
        campaign = StubbedCampaign()
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, campaign.source.read_text(), "Probe.cs:5 <= -> < (boundary)")

        # Act
        code = campaign.run("--carried", "CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_NoCampaign_When_ACommitIsChecked_Then_ItIsAllowed(self):
        # Arrange — the ordinary state, so the two above are not passing for a check that refuses
        # whatever it is handed.
        campaign = StubbedCampaign()

        # Act
        code = campaign.run("--carried", StubbedCampaign.SOURCE)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_AHeldMutation_When_TheProcessIsSignalled_Then_TheSourceIsPutBack(self):
        # Arrange — SIGTERM runs no `finally` under the default handler, and it is what `TaskStop`, a
        # timeout and a killed session all send.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        script = campaign.project / "hold.py"
        script.write_text(textwrap.dedent("""\
            import importlib.util, sys, time
            spec = importlib.util.spec_from_file_location("mutation_check", sys.argv[1])
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            holder = module.Holder(sys.argv[2])
            holder.guard()
            source, original = sys.argv[3], sys.argv[4]
            holder.hold(source, original, "held")
            open(source, "w").write("mutated\\n")
            print("held", flush=True)
            time.sleep(120)
            """))

        # Act
        held = subprocess.Popen(
            [sys.executable, str(script), str(Path(mutation_check.__file__)),
             str(campaign.project / mutation_check.SENTINEL), str(campaign.source), original],
            stdout=subprocess.PIPE, text=True)
        held.stdout.readline()
        os.kill(held.pid, signal.SIGTERM)
        held.wait(timeout=30)

        # Assert
        self.assertEqual(campaign.source.read_text(), original)


class ReachTests(unittest.TestCase):
    """What the operators do not touch, which is most of a diff and was folded into a clean verdict.

    Measured over the twenty commits before this one: 497 changed code lines, 133 of them reached by
    a mutant. A method written as a run of assignments generates nothing, and the run said only that
    nothing survived.
    """

    ASSIGNMENTS = textwrap.dedent("""\
        void Settle(int next)
        {
            _phase = Phase.Settled;
            _value = next;
            _error = null;
        }
        """)

    def test_Given_AMethodOfAssignments_When_MutantsAreGenerated_Then_EveryLineIsUnreached(self):
        # Arrange
        numbers = set(range(1, len(self.ASSIGNMENTS.splitlines()) + 1))

        # Act
        reached = {mutant.line for mutant
                   in mutation_check.mutations_for(Path("probe.cs"), self.ASSIGNMENTS, numbers)}
        unreached = [number for number in mutation_check.code_line_numbers(self.ASSIGNMENTS, numbers)
                     if number not in reached]

        # Assert — the signature and the three assignments; the braces are not code lines.
        self.assertEqual(unreached, [1, 3, 4, 5])

    def test_Given_ALineHoldingOnlyABrace_When_TheCodeLinesAreRead_Then_ItIsNotOneOfThem(self):
        # Arrange — block punctuation is not something an operator failed to reach, and counting it
        # would put a number in the denominator that no widening could ever move.
        text = "{\n}\nif (a <= b) { }\n"

        # Act
        found = mutation_check.code_line_numbers(text, {1, 2, 3})

        # Assert
        self.assertEqual(found, [3])

    def test_Given_ADocumentationComment_When_TheCodeLinesAreRead_Then_ItIsNotOneOfThem(self):
        # Arrange — the ordinary way a production diff generates nothing, and it is not a hole.
        text = "/// <summary>Does the thing.</summary>\n"

        # Act
        found = mutation_check.code_line_numbers(text, {1})

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnyMutants_When_TheReachIsReported_Then_ItNamesTheLinesNoneOfThemCovers(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 3)]

        # Act
        report = mutation_check.reach(mutants, {Path("probe.cs"): [7, 8]}, Path("/nowhere"))

        # Assert
        self.assertEqual(report.splitlines()[0],
                         "1 mutant(s) over 3 changed code line(s); 2 line(s) no operator reaches")


class UnreachableChangeTests(unittest.TestCase):
    def test_Given_ADiffOfCodeNoOperatorReaches_When_TheCampaignStarts_Then_ItRefusesToVerdict(self):
        # Arrange — a verdict here would be about no line at all, and the run returned 0 for it.
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Probe
                {
                    private int _value;
                    internal void Settle(int next) { _value = next; }
                }
            }
            """))

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertIn("no operator reaches any of the", str(code))

    # GREEN_ON_BASE(characterization): the quiet pass the refusal above must not widen to swallow.
    def test_Given_ADiffOfOnlyDocumentationComments_When_TheCampaignStarts_Then_ItPassesQuietly(self):
        # Arrange — the counterpart, so the case above is not passing for a refusal of every change
        # that generates nothing.
        campaign = StubbedCampaign("/// <summary>Nothing to ask about here.</summary>\n")

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertEqual(code, 0)


class MaskRefusalTests(unittest.TestCase):
    def test_Given_ATargetFileTheMaskBlanks_When_TheCampaignStarts_Then_ItRefusesToRun(self):
        # Arrange — a blanked offset generates no mutant, so the campaign would otherwise report the
        # lines under it as having nothing to ask.
        campaign = StubbedCampaign('class Probe { string s = "unterminated;\n int a = 1 <= 2 ? 1 : 2; }\n')

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertIn("swallows code", str(code))


if __name__ == "__main__":
    unittest.main(verbosity=2)
