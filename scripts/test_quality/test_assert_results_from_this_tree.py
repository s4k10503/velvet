#!/usr/bin/env python3
"""Unit tests for assert_results_from_this_tree.py, plus its type reader over this repository.

Every case here is built from a results file and a log rather than from an editor run, because what
the guard has to survive is the state an editor run leaves behind when it does not finish -- a
results file at a path a later run never reached. The one reading that needs real material is the
type reader, and it gets this repository's own fixtures.

Run: python3 scripts/test_quality/test_assert_results_from_this_tree.py
"""

import contextlib
import importlib.util
import io
import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


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
                 packages=("Unity.Package.Tests",), library=True):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-provenance-"))
        self.project = self.root / "project"
        (self.project / "Runtime").mkdir(parents=True)
        (self.project / ".gitignore").write_text("/Library/\n", encoding="utf-8")
        subprocess.run(["git", "init", "-q", str(self.project)], check=True, capture_output=True)

        for name in assemblies:
            (self.project / "Runtime" / (name + ".asmdef")).write_text(
                json.dumps({"name": name}), encoding="utf-8")
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
            "Saving results to: {}\n".format(self.results) if log is None else log,
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

    def test_Given_ALogSayingErrorCSWithNoDiagnosticCode_When_TheRunIsRead_Then_ItIsNotRefused(self):
        # Arrange -- a test's own expected-log message reaches the editor log, and refusing on the
        # words alone would fail a run for what one of its cases printed on purpose.
        with workspace() as tree:
            tree.wrote(log="Saving results to: {}\nexpected message: no error CS here\n"
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


class UnreadableTests(unittest.TestCase):
    """Every reading the guard cannot take is a refusal, since exiting 0 unread looks like a pass."""

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


class RepositoryTests(unittest.TestCase):
    """The type reader against this repository's own fixtures, rather than against invented ones."""

    def test_Given_EveryFixtureThisRepositoryDeclares_When_TheTypesAreRead_Then_EachIsNamed(self):
        # Arrange -- a fixture a run reports and this reader cannot name is a refusal of a good run.
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
