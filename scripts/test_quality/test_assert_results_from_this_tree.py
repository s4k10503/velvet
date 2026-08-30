#!/usr/bin/env python3
"""Unit tests for assert_results_from_this_tree.py, plus two of its readings held against real sources.

The cases over its verdict are built from a results file and a log rather than from an editor run,
because what the guard has to survive is the state an editor run leaves behind when it does not
finish -- a results file at a path a later run never reached. Two readings are held against material
nothing here invents instead: the type reader against this repository's own fixtures, and the log
pattern against every diagnostic identifier the analyzer sources declare.

Run: python3 scripts/test_quality/test_assert_results_from_this_tree.py
"""

import contextlib
import importlib.util
import io
import json
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# A diagnostic identifier as the analyzer sources declare one, in the two spellings they use:
# a bare string literal in a descriptor and a const beside the fixer that consumes it.
ANALYZER_ID = re.compile(r'"((?:VEL|USS)\d{3})"')


def load_module(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        name, Path(__file__).resolve().with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


check = load_module("assert_results_from_this_tree")
base_red_check = load_module("base_red_check")

FIXTURE_SOURCE = """using NUnit.Framework;

namespace Velvet.Tests
{
    internal sealed class ProbeTests
    {
        [Test]
        public void Given_A_When_B_Then_C() => Assert.Pass();

        internal sealed class Inner
        {
            [Test]
            public void Given_D_When_E_Then_F() => Assert.Pass();
        }
    }

    internal abstract class ProbeTestsBase<TElement>
    {
        [Test]
        public void Given_G_When_H_Then_I() => Assert.Pass();
    }
}
"""


def results_xml(assemblies):
    """An NUnit results file shaped the way Unity writes one: fixtures under an assembly suite."""
    body = []
    for assembly, fixtures in assemblies:
        cases = "".join(
            '<test-suite type="TestFixture" name="{fixture}" fullname="{fixture}">'
            '<test-case name="Case" fullname="{fixture}.Case" classname="{fixture}" '
            'result="Passed" /></test-suite>'.format(fixture=fixture)
            for fixture in fixtures)
        body.append('<test-suite type="Assembly" name="{}.dll" fullname="/x/{}.dll">{}</test-suite>'
                    .format(assembly, assembly, cases))
    return ('<?xml version="1.0" encoding="utf-8"?><test-run id="2" total="1" passed="1" failed="0" '
            'inconclusive="0" skipped="0">{}</test-run>'.format("".join(body)))


class Workspace:
    """A project the guard can be pointed at: a git worktree, an asmdef, a source, a package cache."""

    def __init__(self, sources=None, assemblies=("Velvet.Tests.Probe.Editor",),
                 packages=("Unity.Package.Tests",), library=True, csharp=True):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-provenance-"))
        self.project = self.root / "project"
        (self.project / "Runtime").mkdir(parents=True)
        (self.project / ".gitignore").write_text("/Library/\n", encoding="utf-8")
        subprocess.run(["git", "init", "-q", str(self.project)], check=True, capture_output=True)

        for name in assemblies:
            (self.project / "Runtime" / (name + ".asmdef")).write_text(
                json.dumps({"name": name}), encoding="utf-8")
        if csharp:
            (self.project / "Runtime" / "ProbeTests.cs").write_text(
                FIXTURE_SOURCE if sources is None else sources, encoding="utf-8")

        if library:
            for name in packages:
                cached = self.project / "Library" / "PackageCache" / name / "Tests"
                cached.mkdir(parents=True)
                (cached / (name + ".asmdef")).write_text(json.dumps({"name": name}),
                                                         encoding="utf-8")

        self.run_directory = self.root / "run"
        self.run_directory.mkdir()
        self.results = self.run_directory / "results.xml"
        self.log = self.run_directory / "run.log"

    def wrote(self, assemblies=(("Velvet.Tests.Probe.Editor", ["Velvet.Tests.ProbeTests"]),),
              log=None, results=None):
        self.results.write_text(results if results is not None else results_xml(assemblies),
                                encoding="utf-8")
        self.log.write_text(
            "Successfully changed project path to: {}\nSaving results to: {}\n".format(
                self.project, self.results) if log is None else log,
            encoding="utf-8")
        return self

    def verdict(self, *arguments):
        """(exit code, everything it said) for one invocation over this workspace."""
        captured = io.StringIO()
        argv = list(arguments) or [str(self.results), "--log", str(self.log),
                                   "--project", str(self.project)]
        with contextlib.redirect_stderr(captured), contextlib.redirect_stdout(captured):
            code = check.main(argv)
        return code, captured.getvalue()

    def close(self):
        shutil.rmtree(self.root, ignore_errors=True)


@contextlib.contextmanager
def workspace(**arguments):
    made = Workspace(**arguments)
    try:
        yield made
    finally:
        made.close()


class ProvenanceTests(unittest.TestCase):
    def test_Given_AFixtureNoSourceHereDeclares_When_TheResultsAreRead_Then_TheyAreRefused(self):
        # Arrange -- reported under an assembly this project declares, and declared by no source
        # here, which is the reading that stands when the log says nothing is wrong.
        with workspace() as tree:
            tree.wrote([("Velvet.Tests.Probe.Editor", ["Velvet.Tests.ZzScratchDiagnosticsTests"])])

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "ZzScratchDiagnosticsTests" in said), (1, True))

    def test_Given_EveryFixtureDeclaredHere_When_TheResultsAreRead_Then_TheyAreNotRefused(self):
        # Arrange
        with workspace() as tree:
            tree.wrote()

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, said.strip().startswith("checked 1 test run")), (0, True))

    def test_Given_AFixtureOfAResolvedPackagesOwnAssembly_When_TheResultsAreRead_Then_ItIsNotRefused(self):
        # Arrange -- Unity.Addressables.DocExampleCode.Editor.Tests reports a case here, out of a
        # source that is the package's rather than this project's.
        with workspace() as tree:
            tree.wrote([("Velvet.Tests.Probe.Editor", ["Velvet.Tests.ProbeTests"]),
                        ("Unity.Package.Tests", ["Some.Package.OwnTests"])])

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_AnAssemblyBothThisTreeAndAPackageDeclare_When_ItReportsAStranger_Then_ItIsRefused(self):
        # Arrange -- of the two readings available for a name both declare, only checking it can
        # refuse a stranger.
        with workspace(assemblies=("Unity.Package.Tests",)) as tree:
            tree.wrote([("Unity.Package.Tests", ["Velvet.Tests.ZzScratchDiagnosticsTests"])])

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 1)

    def test_Given_AnAssemblyNeitherThisTreeNorAPackageNames_When_ItReportsAStrangerBesideOurs_Then_ItIsRefused(self):
        # Arrange -- the shape a seeded Library reports: this tree's own assemblies compile and run,
        # and the Library carries a test assembly no asmdef here names, holding another checkout's
        # fixture. One of ours has to report alongside it, or the floor refuses for want of anything
        # of this worktree's having run and the fixture reading is never reached.
        with workspace() as tree:
            tree.wrote([("Velvet.Tests.Probe.Editor", ["Velvet.Tests.ProbeTests"]),
                        ("Velvet.Tests.Scratch.Editor", ["Velvet.Tests.ZzScratchDiagnosticsTests"])])

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "ZzScratchDiagnosticsTests" in said), (1, True))

    def test_Given_ANestedFixtureDeclaredHere_When_ItIsReportedWithAPlus_Then_ItIsNotRefused(self):
        # Arrange
        with workspace() as tree:
            tree.wrote([("Velvet.Tests.Probe.Editor", ["Velvet.Tests.ProbeTests+Inner"])])

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_AGenericFixtureDeclaredHere_When_ItIsReportedWithItsArity_Then_ItIsNotRefused(self):
        # Arrange -- the cases a generic base declares report under the base, which NUnit names by
        # its runtime type.
        with workspace() as tree:
            tree.wrote([("Velvet.Tests.Probe.Editor", ["Velvet.Tests.ProbeTestsBase`1"])])

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)


class LogTests(unittest.TestCase):
    def test_Given_ALogCarryingACompilerDiagnostic_When_TheRunIsRead_Then_ItIsRefused(self):
        # Arrange -- every fixture here is this project's own, so the log is the only reading left
        # that can refuse.
        with workspace() as tree:
            tree.wrote(log="Saving results to: {}\nStore.cs(637,1): error CS1002: ; expected\n"
                           .format(tree.results))

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "error CS1002" in said), (1, True))

    def test_Given_ALogNamingNoResultsFile_When_TheRunIsRead_Then_TheResultsAreRefused(self):
        # Arrange -- what an aborted run leaves: the file at the path is the previous run's. Aborted
        # over something other than a compile error, so this reading is the only one that can refuse.
        with workspace() as tree:
            tree.wrote(log="Aborting batchmode due to failure:\nSomething went wrong.\n")

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "some earlier run" in said), (1, True))

    def test_Given_ALogNamingTheResultsUnderTheContainersPath_When_TheRunIsRead_Then_ItIsNotRefused(self):
        # Arrange -- game-ci runs the editor in a container, so the path in the log is not the path
        # the check reads the file from afterwards.
        with workspace() as tree:
            tree.wrote(log="Saving results to: /github/workspace/EditMode-results/results.xml\n")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_ALogCarryingAnAnalyzerDiagnosticAtErrorSeverity_When_TheRunIsRead_Then_ItIsRefused(self):
        # Arrange -- an analyzer under Generators~ raising its own at error severity fails the
        # compile with no CS code in the log to find it by.
        with workspace() as tree:
            tree.wrote(log="Saving results to: {}\nV.cs(9,5): error VEL501: Member 'R' makes 21 "
                           "branching decisions; the limit is 20\n".format(tree.results))

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "VEL501" in said), (1, True))

    def test_Given_ALogCarryingUnitysOwnCapitalisedErrorLine_When_TheRunIsRead_Then_ItIsNotRefused(self):
        # Arrange -- chatter a log that compiled carries anyway. What this case pins is the capital:
        # it dies under a reading that reaches `Error:`, whether by IGNORECASE or by an alternation,
        # and a widening to the bare lowercase word leaves it green -- that one is what the case
        # over a run's own output catches.
        with workspace() as tree:
            tree.wrote(log="Saving results to: {}\n[Licensing::Module] Error: Access token is "
                           "unavailable; failed to update\n".format(tree.results))

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_ALogWhoseOwnOutputSaysErrorWithoutTheSeparator_When_TheRunIsRead_Then_ItIsNotRefused(self):
        # Arrange -- a case's expected-log message reaches the editor log, so a pattern reading the
        # bare word would fail a run for what one of its own cases printed on purpose.
        with workspace() as tree:
            tree.wrote(log="Saving results to: {}\n[Velvet] mount error: the target was null\n"
                           .format(tree.results))

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 0)

    def test_Given_ADirectoryHoldingBothFiles_When_ItIsTheOnlyArgument_Then_TheLogIsFoundInIt(self):
        # Arrange -- how CI names them: one artifacts directory, no --log.
        with workspace() as tree:
            tree.wrote(log="Saving results to: {}\nStore.cs(1,1): error CS1002: ; expected\n"
                           .format(tree.results))

            # Act
            code, said = tree.verdict(str(tree.run_directory), "--project", str(tree.project))

            # Assert
            self.assertEqual((code, "error CS1002" in said), (1, True))

    def test_Given_AnotherRunsOutputInASubdirectory_When_TheDirectoryIsNamed_Then_ItIsNotSweptIn(self):
        # Arrange -- base_red_check.py writes its own runs under the same Logs, and those are
        # readings of a base tree whose fixtures are not this one's to answer for.
        with workspace() as tree:
            tree.wrote()
            nested = tree.run_directory / "base_red_check"
            nested.mkdir()
            (nested / "EditMode-1.xml").write_text(
                results_xml([("Velvet.Tests.Probe.Editor", ["Velvet.Tests.SomeoneElsesTests"])]),
                encoding="utf-8")

            # Act
            code, _ = tree.verdict(str(tree.run_directory), "--project", str(tree.project))

            # Assert
            self.assertEqual(code, 0)


    def test_Given_NoResultsFileAndADiagnosticInTheLog_When_TheRunIsRead_Then_TheDiagnosticIsWhatItSays(self):
        # Arrange -- what an aborted CI job leaves: an artifacts directory holding the log alone.
        with workspace() as tree:
            tree.wrote(log="V.cs(9,5): error VEL500: Member 'R' nests 5 levels deep\n")
            tree.results.unlink()

            # Act
            code, said = tree.verdict(str(tree.run_directory), "--project", str(tree.project))

            # Assert
            self.assertEqual((code, "VEL500" in said), (1, True))


class UnreadableTests(unittest.TestCase):
    """Every reading the guard cannot take is a refusal, since exiting 0 unread looks like a pass."""

    def test_Given_ARunOfAnotherWorktree_When_TheProjectIsThisOne_Then_TheReadingIsRefused(self):
        # Arrange -- an absolute path to another worktree's output with --project left where the
        # call started. Where the two trees declare the same fixtures this was exit 0; where they
        # do not it names a fixture as a stranger, which is the sentence for a stale Library.
        with workspace() as here, workspace() as ran:
            ran.wrote()

            # Act
            code, _ = here.verdict(str(ran.results), "--log", str(ran.log),
                                   "--project", str(here.project))

            # Assert
            self.assertEqual(code, 2)

    def test_Given_ARunOfAnotherWorktree_When_TheProjectIsThisOne_Then_TheRefusalNamesTheTree(self):
        # Arrange
        with workspace() as here, workspace() as ran:
            ran.wrote()

            # Act
            _, said = here.verdict(str(ran.results), "--log", str(ran.log),
                                   "--project", str(here.project))

            # Assert
            self.assertIn(str(ran.project), said)

    # GREEN_ON_BASE(construction): the base has no precondition to fire, so it passes for want of
    # one. What this holds is the shape the first attempt at one refused, and it is the case named
    # for it: replacing `measured_elsewhere` with a reading of whether each named file sits under
    # `project` fails it, along with 23 others that keep their output beside the project too.
    def test_Given_ARunOfThisProjectWritingItsResultsElsewhere_When_ItIsRead_Then_ItIsNotRefused(self):
        # Arrange -- CONTRIBUTING's story-capture recipe: -projectPath the checkout, -testResults
        # /tmp. Where the files sit says nothing; what the run opened is the question.
        with workspace() as tree:
            outside = tree.root / "elsewhere"
            outside.mkdir()
            tree.results, tree.log = outside / "capture.xml", outside / "capture.log"
            tree.wrote()

            # Act
            code, _ = tree.verdict(str(tree.results), "--log", str(tree.log),
                                   "--project", str(tree.project))

            # Assert
            self.assertEqual(code, 0)

    # GREEN_ON_BASE(construction): the base passes for want of a precondition. Dropping the
    # `named.is_dir()` term from `measured_elsewhere` fails this case and no other, and it is the
    # one CI takes on every pull request — the container path a run names is on no runner.
    def test_Given_ALogNamingAProjectThisMachineDoesNotHave_When_ItIsRead_Then_ItIsNotRefused(self):
        # Arrange -- a run inside a container names the container's path, and the tree it opened is
        # not one this machine has. This is the shape CI reads on every pull request.
        with workspace() as tree:
            # The trailing "/." is what game-ci's own logs carry, and Path normalises it away.
            tree.wrote(log="Successfully changed project path to: /github/workspace/.\n"
                           "Saving results to: {}\n".format(tree.results))

            # Act
            code, _ = tree.verdict(str(tree.results), "--log", str(tree.log),
                                   "--project", str(tree.project))

            # Assert
            self.assertEqual(code, 0)

    def test_Given_NoEditorLogAtAll_When_TheRunIsRead_Then_TheReadingIsRefused(self):
        # Arrange
        with workspace() as tree:
            tree.wrote()

            # Act
            code, _ = tree.verdict(str(tree.results), "--project", str(tree.project))

            # Assert
            self.assertEqual(code, 2)

    def test_Given_NoLibraryToReadTheResolvedPackagesFrom_When_TheRunIsRead_Then_TheReadingIsRefused(self):
        # Arrange -- without it, a package's own fixtures and a stranger's are the same reading.
        with workspace(library=False) as tree:
            tree.wrote()

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 2)

    def test_Given_AnAssemblyNeitherThisTreeNorAPackageNames_When_ItIsAllThatRan_Then_TheReadingIsRefused(self):
        # Arrange -- a test asmdef renamed here, reported out of a Library seeded before the rename.
        # Its fixture is one this tree does declare, so only the floor can catch it.
        with workspace() as tree:
            tree.wrote([("Velvet.Tests.Renamed.Editor", ["Velvet.Tests.ProbeTests"])])

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 2)

    def test_Given_NoResultsFileAndNothingInTheLogToSayWhy_When_TheRunIsRead_Then_TheRefusalSaysBoth(self):
        # Arrange -- a run that wrote nothing and explained nothing. The floor below refuses this
        # reading too, for want of a test run, so only the wording separates a missing file from a
        # file that turned out to hold no run.
        with workspace() as tree:
            tree.wrote(log="Refreshing native plugins compatible for Editor\n")
            tree.results.unlink()

            # Act
            code, said = tree.verdict(str(tree.run_directory), "--project", str(tree.project))

            # Assert
            self.assertEqual((code, "no results file" in said), (2, True))

    def test_Given_NoAssemblyDefinitionHere_When_TheRunIsRead_Then_TheRefusalNamesIt(self):
        # Arrange -- with none, no assembly is this worktree's, and the floor below refuses the same
        # reading for a different reason, so only the wording separates a project holding no asmdef
        # from a run that loaded none of them.
        with workspace(assemblies=()) as tree:
            tree.wrote()

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "no assembly definition here" in said), (2, True))

    def test_Given_NoCSharpSourceHere_When_TheRunIsRead_Then_TheReadingIsRefused(self):
        # Arrange -- an empty set of declared types makes every reported fixture foreign, which would
        # refuse for the wrong reason and name every fixture in the run doing it.
        with workspace(csharp=False) as tree:
            tree.wrote()

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 2)

    def test_Given_AnXmlThatIsNoTestRun_When_ItIsAllThatWasNamed_Then_TheRefusalNamesIt(self):
        # Arrange -- a coverage report has no test-case in it. The floor below refuses this reading
        # too, for want of an assembly, so only the wording says the file was never a run at all.
        with workspace() as tree:
            tree.wrote(results='<?xml version="1.0"?><coverage />')

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "no test run among" in said), (2, True))

    def test_Given_ResultsNamingNoAssemblyOfThisTree_When_TheRunIsRead_Then_TheReadingIsRefused(self):
        # Arrange -- a run where this project's assemblies never loaded measured nothing of it, and
        # every fixture check below passes for want of anything to check.
        with workspace() as tree:
            tree.wrote([("Unity.Package.Tests", ["Some.Package.OwnTests"])])

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 2)

    def test_Given_AResultsPathThatIsNotThere_When_ARealOneIsNamedBesideIt_Then_TheReadingIsRefused(self):
        # Arrange -- beside a real one, since a lone absent path leaves nothing to read either way
        # and the refusal would be the empty argument list's rather than this file's.
        with workspace() as tree:
            tree.wrote()

            # Act
            code, _ = tree.verdict(str(tree.results), str(tree.run_directory / "absent.xml"),
                                   "--log", str(tree.log), "--project", str(tree.project))

            # Assert
            self.assertEqual(code, 2)

    def test_Given_ACaseUnderNoAssemblySuite_When_OneUnderAnAssemblyIsReadBesideIt_Then_TheReadingIsRefused(self):
        # Arrange -- the loose case names a fixture this tree does declare, so the alternative of
        # sorting the unattributed bucket alongside the rest would have exited 0 on a file half of
        # which could not be attributed at all.
        with workspace() as tree:
            loose = ('<test-case name="Case" fullname="Velvet.Tests.ProbeTests.Case" '
                     'classname="Velvet.Tests.ProbeTests" result="Passed" />')
            tree.wrote(results=results_xml([("Velvet.Tests.Probe.Editor",
                                             ["Velvet.Tests.ProbeTests"])])
                       .replace("</test-run>", loose + "</test-run>"))

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "no assembly suite" in said), (2, True))

    def test_Given_AResultsFileNoParserCanRead_When_TheRunIsRead_Then_TheReadingIsRefused(self):
        # Arrange
        with workspace() as tree:
            tree.wrote(results="<test-run>truncated")

            # Act
            code, _ = tree.verdict()

            # Assert
            self.assertEqual(code, 2)

    def test_Given_ADirectoryGitCannotBeAskedAbout_When_TheRunIsRead_Then_TheRefusalNamesGit(self):
        # Arrange -- git answering nothing and a project holding nothing refuse alike, so the
        # refusal has to say which, or the reader goes looking for an asmdef that is right there.
        with workspace() as tree:
            tree.wrote()
            shutil.rmtree(tree.project / ".git")

            # Act
            code, said = tree.verdict()

            # Assert
            self.assertEqual((code, "git could not list" in said), (2, True))


class TypeReadingTests(unittest.TestCase):
    def test_Given_TwoBlockNamespaces_When_TheTypeInsideIsRead_Then_BothQualifyIt(self):
        # Arrange -- the name NUnit reports for such a type carries both, and a reader holding only
        # the last one it saw names it one level short and refuses a fixture that is right here.
        text = "namespace A\n{\n    namespace B\n    {\n        class C { }\n    }\n}\n"

        # Act
        names = check.declared_types(text)

        # Assert
        self.assertEqual(sorted(names), ["A.B.C"])

    def test_Given_AFileScopedNamespace_When_ATypeBelowItIsRead_Then_ItStillQualifiesIt(self):
        # Arrange -- it opens no body, so a reader dropping a scope on depth alone loses it at the
        # first closing brace in the file.
        text = "namespace Velvet.Tests;\n\nclass D\n{\n    class E { }\n}\n\nclass F { }\n"

        # Act
        names = check.declared_types(text)

        # Assert
        self.assertEqual(sorted(names),
                         ["Velvet.Tests.D", "Velvet.Tests.D.E", "Velvet.Tests.F"])


class DiagnosticIdTests(unittest.TestCase):
    def test_Given_EveryDiagnosticIdTheAnalyzersHereDeclare_When_RaisedAsAnError_Then_TheLogReadingMatchesIt(self):
        # Arrange -- read off the analyzer sources rather than listed, since which of them is an
        # error is not fixed: a descriptor can be declared at error severity, and any of them can be
        # promoted to one by warnaserror or by a severity entry.
        #
        # What this holds is that the reading is keyed on the rendering rather than on an ID space:
        # it dies when the pattern is narrowed back to one, `: error CS` above all. It is not
        # coverage of a newly declared identifier and cannot be -- the pattern never reads the name,
        # which is exactly why adding one needs no change here.
        generators = REPO_ROOT / "Packages" / "com.velvet.core" / "Generators~" / "src"
        ids = sorted({found for path in generators.rglob("*.cs")
                      for found in ANALYZER_ID.findall(
                          path.read_text(encoding="utf-8", errors="replace"))})

        # Act
        unmatched = [name for name in ids
                     if not check.COMPILE_ERROR.search(
                         "Packages/x/Foo.cs(1,1): error {}: something".format(name))]

        # Assert -- the count rides along because an empty corpus leaves nothing unmatched.
        self.assertEqual((len(ids) > 20, unmatched), (True, []))


class RepositoryTests(unittest.TestCase):
    """The type reader against this repository's own fixtures, rather than against invented ones."""

    def test_Given_EveryFixtureThisRepositoryDeclares_When_TheTypesAreRead_Then_EachIsNamed(self):
        # Arrange -- the two readers share base_red_check.py's masking and brace profile, so this
        # holds them to naming the same fixtures and cannot see a defect below that line. What reads
        # the names NUnit itself reports is the guard running over each suite's real results, which
        # both Unity jobs do wherever a licence is configured.
        names = subprocess.run(["git", "-C", str(REPO_ROOT), "ls-files"],
                               capture_output=True, text=True, check=True).stdout.splitlines()
        declared = set()
        fixtures = set()

        # Act
        for relative in names:
            path = REPO_ROOT / relative
            if not relative.endswith(".cs") or not path.exists():
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            declared |= check.declared_types(text)
            # Off the qualified case name rather than Case.fixture, which qualifies a file outside
            # the Tests/Editor convention by its path instead of by its type.
            fixtures |= {case.name.rsplit(".", 1)[0]
                         for case in base_red_check.csharp_cases(text, relative)}

        # Assert -- the count rides along because an empty corpus leaves nothing unnamed.
        self.assertEqual((len(fixtures) > 100, sorted(fixtures - declared)), (True, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)
