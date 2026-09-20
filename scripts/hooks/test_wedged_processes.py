#!/usr/bin/env python3
"""Unit tests for report/wedged_processes.py's reading of the process table.

Run: python3 scripts/hooks/test_wedged_processes.py
"""

import contextlib
import importlib.util
import io
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HOOK = Path(__file__).resolve().parents[2] / ".harness" / "hooks" / "report" / "wedged_processes.py"


def load_module():
    spec = importlib.util.spec_from_file_location("wedged_processes", HOOK)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


wedged_processes = load_module()


@contextlib.contextmanager
def listed_processes(listing):
    """A `ps` first on PATH that prints `listing` whatever it is asked."""
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "listing").write_bytes(listing)
        ps = root / "ps"
        ps.write_text('#!/bin/sh\nexec cat "{0}/listing"\n'.format(root))
        ps.chmod(0o755)
        with mock.patch.dict(os.environ, {"PATH": directory + os.pathsep + os.environ["PATH"],
                                          "VELVET_WEDGE_REPORT_MB": "1"}):
            yield


class ProcessTableDecodingTests(unittest.TestCase):
    def test_Given_ANeighbourWhoseNameIsNotUtf8_When_TheTableIsRead_Then_TheWedgedProcessIsReported(self):
        # Arrange
        listing = b"UE 1024000 /usr/libexec/stuck\nS 10 /usr/bin/probe\xff\n"
        printed = io.StringIO()

        # Act
        with listed_processes(listing), mock.patch.object(wedged_processes.platform, "system",
                                                          return_value="Darwin"):
            with contextlib.redirect_stdout(printed):
                try:
                    code = wedged_processes.main()
                except UnicodeDecodeError as error:
                    code = repr(error)

        # Assert
        self.assertEqual((code, "stuck" in printed.getvalue()), (0, True))


if __name__ == "__main__":
    unittest.main()
