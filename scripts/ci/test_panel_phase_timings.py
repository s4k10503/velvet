#!/usr/bin/env python3
"""Unit tests for panel_phase_timings.py: what the panel fixtures' timing CSV is reported as.

Run: python3 scripts/ci/test_panel_phase_timings.py
"""

import importlib.util
import tempfile
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location(
        "panel_phase_timings", Path(__file__).with_name("panel_phase_timings.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


panel_phase_timings = load_module()

CSV = (
    "fixture,case,phase,milliseconds\n"
    "N.A,One,create_show,10.000\n"
    "N.A,One,layout,2.000\n"
    "N.A,One,teardown,4.000\n"
    "N.A,One,case,30.000\n"
    "N.B,\"Two(\"\"x,y\"\")\",create_show,6.000\n"
    "N.B,\"Two(\"\"x,y\"\")\",teardown,2.000\n"
    "N.B,\"Two(\"\"x,y\"\")\",case,20.000\n"
)


def results_xml(*cases):
    body = "".join(f"<test-case classname='{c}' name='{n}' duration='{d}'/>" for c, n, d in cases)
    return f"<test-run><test-suite>{body}</test-suite></test-run>"


class PanelPhaseTimingsTests(unittest.TestCase):

    def test_Given_AQuotedCaseName_When_TheCsvIsRead_Then_TheCommaStaysInsideTheField(self):
        # Arrange
        text = CSV

        # Act
        rows = panel_phase_timings.read_rows(text)

        # Assert
        self.assertEqual(rows[4], ("N.B", 'Two("x,y")', "create_show", 6.0))

    def test_Given_AHeaderRepeatedMidFile_When_TheCsvIsRead_Then_OnlyDataRowsRemain(self):
        # Arrange
        text = CSV + "fixture,case,phase,milliseconds\nN.C,Three,case,1.000\n"

        # Act
        rows = panel_phase_timings.read_rows(text)

        # Assert
        self.assertEqual(rows[-1], ("N.C", "Three", "case", 1.0))

    def test_Given_Rows_When_TotalledByPhase_Then_EachPhaseHoldsItsCountAndSum(self):
        # Arrange
        rows = panel_phase_timings.read_rows(CSV)

        # Act
        totals = panel_phase_timings.totals_by_phase(rows)

        # Assert
        self.assertEqual({phase: tuple(value) for phase, value in totals.items()},
                         {"create_show": (2, 16.0), "layout": (1, 2.0), "teardown": (2, 6.0), "case": (2, 50.0)})

    def test_Given_ResultsXml_When_NonPanelTimeIsTaken_Then_PanelCasesAreLeftOutAndSecondsBecomeMilliseconds(self):
        # Arrange
        panel = {("N.A", "One")}
        xml = results_xml(("N.A", "One", "0.5"), ("N.C", "Three", "0.25"), ("N.C", "Four", "0.125"))

        # Act
        milliseconds = panel_phase_timings.non_panel_milliseconds(xml, panel)

        # Assert
        self.assertEqual(milliseconds, 375.0)

    def test_Given_PanelAndOtherTime_When_Reported_Then_TheSuiteShareIsPanelOverTheWhole(self):
        # Arrange
        rows = panel_phase_timings.read_rows(CSV)

        # Act
        text = panel_phase_timings.report(rows, non_panel_ms=150.0)

        # Assert
        self.assertIn("panel cases: 50.0 ms of 200.0 ms suite (25.0%)", text)

    def test_Given_Rows_When_Reported_Then_WindowCreationAndTeardownAreSummedAcrossFixtures(self):
        # Arrange
        rows = panel_phase_timings.read_rows(CSV)

        # Act
        text = panel_phase_timings.report(rows)

        # Assert
        self.assertIn("window creation + teardown: 22.0 ms", text)

    def test_Given_Rows_When_FixturesAreRanked_Then_TheLargestWindowCostComesFirst(self):
        # Arrange
        rows = panel_phase_timings.read_rows(CSV)

        # Act
        ranked = panel_phase_timings.overhead_by_fixture(rows)

        # Assert
        self.assertEqual(ranked, [("N.A", 14.0), ("N.B", 8.0)])

    def test_Given_ADirectoryWithNoCsv_When_MainRuns_Then_ItFails(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:

            # Act
            code = panel_phase_timings.main([directory])

        # Assert
        self.assertEqual(code, 1)

    def test_Given_ADirectoryHoldingTheCsv_When_MainRuns_Then_ItSucceeds(self):
        # Arrange
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, panel_phase_timings.FILE_NAME).write_text(CSV, encoding="utf-8")

            # Act
            code = panel_phase_timings.main([directory])

        # Assert
        self.assertEqual(code, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
