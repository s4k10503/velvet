#!/usr/bin/env python3
"""Unit tests for benchmark_results.py: what a benchmark run's results become for the trend store.

Run: python3 scripts/ci/test_benchmark_results.py
"""

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("benchmark_results", Path(__file__).with_name("benchmark_results.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


benchmark_results = load_module()


def result(name, groups):
    return benchmark_results.RESULT_PREFIX + json.dumps({"Name": name, "SampleGroups": groups})


def results_xml(*lines):
    cases = "".join(
        f"<test-case fullname='c{i}'><output><![CDATA[noise\n{line}\n]]></output></test-case>"
        for i, line in enumerate(lines))
    return f"<test-run><test-suite>{cases}</test-suite></test-run>"


TIME = {"Name": "Time", "Unit": 2, "IncreaseIsBetter": False, "Median": 1.5}
BLOCKS = {"Name": "GC.Alloc", "Unit": 4, "IncreaseIsBetter": False, "Median": 0}
THROUGHPUT = {"Name": "Frames", "Unit": 3, "IncreaseIsBetter": True, "Median": 60}


class BenchmarkResultsTests(unittest.TestCase):

    # GREEN_ON_BASE(construction): benchmark_results.py is this change's own file, so no base run can
    # build the case. What shows it can fail is changing RESULT_PREFIX's last character: measured, no
    # line is read and the list is empty.
    def test_Given_TwoCasesWithOutput_When_TheDocumentIsRead_Then_EachResultLineIsOneResult(self):
        # Arrange
        xml = results_xml(result("A.One", [TIME]), result("A.Two", [TIME]))

        # Act
        names = [r["Name"] for r in benchmark_results.read_results(xml)]

        # Assert
        self.assertEqual(names, ["A.One", "A.Two"])

    # GREEN_ON_BASE(construction): same reason as above. Deleting the IncreaseIsBetter test in
    # entries() puts the Frames group in the list.
    def test_Given_AGroupWhereMoreIsBetter_When_EntriesAreTaken_Then_OnlySmallerIsBetterGroupsRemain(self):
        # Arrange
        results = [{"Name": "A.One", "SampleGroups": [TIME, THROUGHPUT, BLOCKS]}]

        # Act
        rows = benchmark_results.entries(results)

        # Assert
        self.assertEqual(rows, [("A.One / Time", "ms", 1.5), ("A.One / GC.Alloc", "B", 0)])

    # GREEN_ON_BASE(construction): same reason as above. Renaming the "value" key fails it.
    def test_Given_Rows_When_ConvertedToBenchmarkJson_Then_EachCarriesNameUnitAndValue(self):
        # Arrange
        rows = [("A.One / Time", "ms", 1.5)]

        # Act
        converted = benchmark_results.to_benchmark_json(rows)

        # Assert
        self.assertEqual(converted, [{"name": "A.One / Time", "unit": "ms", "value": 1.5}])

    # GREEN_ON_BASE(construction): same reason as above. Dropping the unit column fails it.
    def test_Given_Rows_When_RenderedAsMarkdown_Then_ATableRowFollowsTheHeader(self):
        # Arrange
        rows = [("A.One / Time", "ms", 1.5)]

        # Act
        lines = benchmark_results.to_markdown(rows).splitlines()

        # Assert
        self.assertEqual(lines[2], "| A.One / Time | 1.5 | ms |")

    # GREEN_ON_BASE(construction): same reason as above. Deleting the failure return makes main write
    # an empty trend entry instead of refusing.
    def test_Given_ResultsWithNoPerformanceLine_When_MainRuns_Then_ItFails(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "r.xml").write_text(results_xml("nothing here"), encoding="utf-8")

            # Act
            code = benchmark_results.main(
                [str(root / "r.xml"), "--json", str(root / "o.json"), "--summary", str(root / "o.md")])

        # Assert
        self.assertEqual(code, 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
