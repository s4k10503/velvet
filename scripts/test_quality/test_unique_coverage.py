#!/usr/bin/env python3
"""Unit tests for unique_coverage.py.

The cases write OpenCover files in the shape Code Coverage gives a case's record under
`generateTestReferences` -- a module declaring its files and the one test it tracks, each sequence
point carrying that test's visit count -- and hand their directory to `main`, the way the workflow's
report job does.

Run: python3 scripts/test_quality/test_unique_coverage.py
"""

import contextlib
import gzip
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path


def load_module():
    """Imports unique_coverage by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "unique_coverage", Path(__file__).with_name("unique_coverage.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


unique_coverage = load_module()

A = "/github/workspace/Packages/com.velvet.core/Runtime/A.cs"


def test_name(fixture, method):
    return "System.Void Velvet.Tests.{}::{}()".format(fixture, method)


def record(test, points):
    """One case's OpenCover file. `points` is [(source, line, visits)]; `test` None writes a file that
    tracks no test, the shape a run without generateTestReferences writes."""
    sources = sorted({source for source, _, _ in points})
    uids = {source: uid for uid, source in enumerate(sources, start=1)}
    files = "".join('        <File uid="{}" fullPath="{}" />\n'.format(uids[source], source)
                    for source in sources)
    methods = ""
    for source in sources:
        sequence = ""
        for offset, (_, line, visits) in enumerate(item for item in points if item[0] == source):
            references = ("\n                  <TrackedMethodRefs>\n"
                          '                    <TrackedMethodRef uid="0" vc="{}" />\n'
                          "                  </TrackedMethodRefs>\n                ".format(visits)
                          if test else "")
            sequence += ('                <SequencePoint vc="{v}" uspid="{o}" ordinal="0" offset="{o}" '
                         'sl="{l}" sc="9" el="{l}" ec="9" bec="0" bev="0" fileid="{f}"{close}'
                         .format(v=visits, o=offset, l=line, f=uids[source],
                                 close=">" + references + "</SequencePoint>\n" if test else " />\n"))
        methods += ("        <Class>\n          <FullName>Velvet.A</FullName>\n          <Methods>\n"
                    '            <Method visited="true" isConstructor="false" isStatic="false">\n'
                    "              <MetadataToken>100663300</MetadataToken>\n"
                    "              <Name>System.Void Velvet.A::Run()</Name>\n"
                    '              <FileRef uid="{}" />\n'
                    "              <SequencePoints>\n{}              </SequencePoints>\n"
                    "              <BranchPoints />\n            </Method>\n"
                    "          </Methods>\n        </Class>\n").format(uids[source], sequence)
    tracked = ("      <TrackedMethods>\n"
               '        <TrackedMethod uid="0" token="100663301" name="{}" strategy="Test" />\n'
               "      </TrackedMethods>\n".format(test) if test else "")
    return ('<?xml version="1.0" encoding="utf-8"?>\n'
            '<CoverageSession xmlns:xsd="http://www.w3.org/2001/XMLSchema" '
            'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" Version="0.0.0.0">\n'
            "  <Modules>\n    <Module>\n      <ModuleName>Velvet</ModuleName>\n"
            "      <Files>\n{}      </Files>\n      <Classes>\n{}      </Classes>\n{}"
            "    </Module>\n  </Modules>\n</CoverageSession>\n").format(files, methods, tracked)


def run(records, *options, packed=False, extra=None):
    """(exit status, printed text) of `main` over a directory holding `records`, one file each."""
    printed = io.StringIO()
    with tempfile.TemporaryDirectory() as root, contextlib.redirect_stdout(printed), \
            contextlib.redirect_stderr(io.StringIO()):
        directory = Path(root) / "per-case"
        directory.mkdir()
        for number, text in enumerate(records):
            name = "TestCoverageResults_{:04d}.xml".format(number)
            if packed:
                (directory / (name + ".gz")).write_bytes(gzip.compress(text.encode()))
            else:
                (directory / name).write_text(text)
        if extra is not None:
            (directory / "TestCoverageResults_{:04d}.xml".format(len(records))).write_text(extra)
        status = unique_coverage.main([str(directory), *options])
    return status, printed.getvalue()


class UniqueCoverageTests(unittest.TestCase):

    def test_Given_LinesOneFixturesCaseAloneReaches_When_Reported_Then_TheyAreListedUnderThatFixture(self):
        # Arrange — two lines of one file, so a count of files would read as a count of lines.
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 1), (A, 30, 1)]),
                   record(test_name("BTests", "Given_Y"), [(A, 20, 1)])]

        # Act
        _, printed = run(records, "--root", "/elsewhere")

        # Assert
        self.assertIn("Velvet.Tests.ATests: 2 line(s) only it reaches\n  {}: 10, 30\n".format(A), printed)

    def test_Given_ALineTwoFixturesReach_When_Reported_Then_NeitherListsIt(self):
        # Arrange
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 1)]),
                   record(test_name("BTests", "Given_Y"), [(A, 10, 1)])]

        # Act
        _, printed = run(records)

        # Assert
        self.assertIn("2 group(s) reach no line alone:", printed)

    def test_Given_ALineAReferenceVisitedNoTimes_When_Reported_Then_ItIsNotThatCasesLine(self):
        # Arrange — the case entered the method and did not run this line of it.
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 0)]),
                   record(test_name("BTests", "Given_Y"), [(A, 10, 1)])]

        # Act
        _, printed = run(records)

        # Assert
        self.assertIn("Velvet.Tests.BTests: 1 line(s) only it reaches", printed)

    def test_Given_TwoCasesOfOneFixtureReachingALine_When_Reported_Then_ItIsStillThatFixturesAlone(self):
        # Arrange
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 1)]),
                   record(test_name("ATests", "Given_Y"), [(A, 10, 1)])]

        # Act
        _, printed = run(records)

        # Assert
        self.assertIn("Velvet.Tests.ATests: 1 line(s) only it reaches", printed)

    def test_Given_AGroupOfCasesFromOneFixture_When_Reported_Then_TheyAreReportedApartFromTheRestOfIt(self):
        # Arrange — what a pruning asks: which lines only the cases it removes reach.
        records = [record(test_name("ATests", "Given_Pruned"), [(A, 10, 1)]),
                   record(test_name("ATests", "Given_Kept"), [(A, 20, 1)])]

        # Act
        _, printed = run(records, "--group", "pruned=::Given_Pruned")

        # Assert
        self.assertIn("pruned: 1 line(s) only it reaches", printed)

    def test_Given_AGroupWhosePatternFindsNoTest_When_Reported_Then_TheReadingIsRefused(self):
        # Arrange — a misspelt pattern would otherwise leave the group out of the report without a word.
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 1)])]

        # Act
        status, _ = run(records, "--group", "pruned=::Given_Misspelt")

        # Assert
        self.assertEqual(status, unique_coverage.CANNOT_READ)

    def test_Given_FilesThatTrackNoTest_When_Reported_Then_TheReadingIsRefused(self):
        # Arrange — a run without generateTestReferences, which would otherwise read as no fixture
        # reaching anything alone.
        records = [record(None, [(A, 10, 1)])]

        # Act
        status, _ = run(records)

        # Assert
        self.assertEqual(status, unique_coverage.CANNOT_READ)

    def test_Given_AGzippedRecord_When_Reported_Then_ItIsReadAsThePlainOneIs(self):
        # Arrange
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 1)])]

        # Act
        _, printed = run(records, packed=True)

        # Assert
        self.assertIn("Velvet.Tests.ATests: 1 line(s) only it reaches", printed)

    def test_Given_AnEmptyFileBesideTheRecords_When_Reported_Then_TheReadingIsRefused(self):
        # Arrange — the name per_case_coverage.py leaves behind once it has compressed a file.
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 1)])]

        # Act
        status, _ = run(records, extra="")

        # Assert
        self.assertEqual(status, unique_coverage.CANNOT_READ)

    def test_Given_TheDefaultRoot_When_Reported_Then_PathsArePrintedRelativeToTheCheckout(self):
        # Arrange
        records = [record(test_name("ATests", "Given_X"), [(A, 10, 1)])]

        # Act
        _, printed = run(records)

        # Assert
        self.assertIn("\n  Packages/com.velvet.core/Runtime/A.cs: 10\n", printed)

    def test_Given_ConsecutiveLinesAlone_When_Reported_Then_TheyArePrintedAsARange(self):
        # Arrange
        records = [record(test_name("ATests", "Given_X"), [(A, 12, 1), (A, 10, 1), (A, 11, 1), (A, 20, 1)])]

        # Act
        _, printed = run(records)

        # Assert
        self.assertIn("A.cs: 10-12, 20\n", printed)

    def test_Given_AStaticTestsReference_When_ItsClassIsRead_Then_TheReturnTypeIsNotPartOfIt(self):
        # Arrange
        name = "static System.Void Velvet.Tests.ATests::Given_X()"

        # Act
        fixture = unique_coverage.test_of(name)[0]

        # Assert
        self.assertEqual(fixture, "Velvet.Tests.ATests")

    def test_Given_AParameterisedTestsReference_When_ItsMethodIsRead_Then_TheParametersAreNotPartOfIt(self):
        # Arrange
        name = "System.Void Velvet.Tests.ATests::Given_X(System.Int32, System.String)"

        # Act
        method = unique_coverage.test_of(name)[1]

        # Assert
        self.assertEqual(method, "Given_X")

    def test_Given_ANestedFixturesReference_When_ItsClassIsRead_Then_ItIsNamedAsTheRunnerNamesIt(self):
        # Arrange
        name = "System.Void Velvet.Tests.Outer/InnerTests::Given_X()"

        # Act
        nested = unique_coverage.test_of(name)[0].split(".")[-1]

        # Assert
        self.assertEqual(nested, "Outer+InnerTests")


if __name__ == "__main__":
    unittest.main(verbosity=2)
