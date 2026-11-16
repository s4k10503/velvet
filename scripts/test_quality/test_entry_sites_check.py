#!/usr/bin/env python3
"""Unit tests for entry_sites_check.py's reading, plus a guard over this repository's own tree.

Run: python3 scripts/test_quality/test_entry_sites_check.py
"""

import importlib.util
import io
import shutil
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = Path(__file__).resolve().with_name("entry_sites_check.py")


def load_module():
    """Imports entry_sites_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location("entry_sites_check", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


entry_sites_check = load_module()

RELATIVE = "Runtime/Component/Probe.cs"


def member(body, name="Probe", modifiers="private void", parameters="ComponentFiber fiber"):
    """A file declaring one class with one member whose body is the lines given, indented as the repository keeps."""
    lines = "\n".join("            " + line for line in body.split("\n"))
    return ("namespace Velvet\n{\n    internal sealed class ProbeType\n    {\n"
            f"        {modifiers} {name}({parameters})\n        {{\n{lines}\n        }}\n    }}\n}}\n")


class ReadingTests(unittest.TestCase):
    def test_Given_AMemberCallingADoorItMayNotHold_When_ItIsRead_Then_TheCallIsReported(self):
        # Arrange
        source = member("FiberRenderer.Dispose(fiber);", name="DisposeFiber")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [f"{RELATIVE}:7: ProbeType.DisposeFiber references the door FiberRenderer.Dispose"])

    def test_Given_AMemberCallingTheBody_When_ItIsRead_Then_NothingIsReported(self):
        # Arrange
        source = member("FiberRenderer.DisposeWork(fiber);", name="DisposeFiber")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AMemberAllowedTheDoor_When_ItIsRead_Then_NothingIsReported(self):
        # Arrange
        source = member("DrainImmediate();", name="RunImmediateCallback", parameters="").replace(
            "ProbeType", "FiberBatchScheduler")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnAllowedMemberOfAnotherType_When_ItCallsTheDoor_Then_TheCallIsReported(self):
        # Arrange — the member's name alone is allowed in FiberBatchScheduler, not here
        source = member("DrainImmediate();", name="RunImmediateCallback", parameters="")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ADoorsOwnDeclaration_When_ItIsRead_Then_ItIsNotAReference(self):
        # Arrange
        source = member("return;", name="FlushImmediate", modifiers="internal void", parameters="")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADoorThatReentersItselfWhereItHolds_When_ItIsRead_Then_NothingIsReported(self):
        # Arrange
        source = member("Hold(fiber, static held => FlushState(held));", name="FlushState",
                        modifiers="internal static void").replace("ProbeType", "FiberWorkLoop")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADoorNamedInACommentAndAString_When_ItIsRead_Then_NeitherIsAReference(self):
        # Arrange
        source = member('// FiberRenderer.Dispose(fiber) is the door\nLog("FlushImmediate()");\n/* DrainImmediate() */')

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADoorPassedAsAMethodGroup_When_ItIsRead_Then_TheGroupIsReported(self):
        # Arrange
        source = member("Run(fiber, NotifyAsyncResourceCompleted);")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ASharedNameUnqualifiedOutsideItsDoorsFile_When_ItIsRead_Then_ItIsNotAReference(self):
        # Arrange — Dispose is the door only where it is FiberRenderer's
        source = member("Dispose(fiber);")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ASharedNameUnqualifiedInItsDoorsFile_When_ItIsRead_Then_ItIsReported(self):
        # Arrange
        source = member("Dispose(fiber);", name="DisposeChild")

        # Act
        found = entry_sites_check.violations(entry_sites_check.RENDERER, source)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AReconcilerInstanceQualifyingReconcile_When_ItIsRead_Then_ItIsReported(self):
        # Arrange
        source = member("fiber.Reconciler!.Reconcile(parent, old, next);")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AChildReconcilersReconcile_When_ItIsRead_Then_ItIsNotAReference(self):
        # Arrange
        source = member("_childReconciler.Reconcile(parent, old, next);\nChildPass.Reconcile(parent, old, next);")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ACallInsideABlockAndARegionBelowItsMember_When_ItIsRead_Then_TheMemberIsTheOneDeclaredAbove(self):
        # Arrange
        source = member("if (fiber != null)\n{\n#if UNITY_EDITOR\n    FlushImmediate();\n#endif\n}",
                        name="Outer").replace("        private void Outer", "#region\n        private void Outer")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [f"{RELATIVE}:11: ProbeType.Outer references the door FiberBatchScheduler.FlushImmediate"])

    def test_Given_ACallOnItsExpressionBodiedMembersOwnLine_When_ItIsRead_Then_ThatMemberHoldsIt(self):
        # Arrange
        source = ("namespace Velvet\n{\n    internal sealed class FiberBatchScheduler\n    {\n"
                  "        internal void DrainDelayed() => DrainDelayedTier(null);\n    }\n}\n")

        # Act
        found = entry_sites_check.violations(RELATIVE, source)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnAllowedReferenceTheTreeNoLongerMakes_When_StaleEntriesAreRead_Then_ItIsReported(self):
        # Arrange
        used = {(caller, door) for caller, doors in entry_sites_check.ALLOWED.items() for door in doors}
        used.discard(("FiberBatchScheduler.RunImmediateCallback", "FiberBatchScheduler.DrainImmediate"))

        # Act
        found = entry_sites_check.stale(used)

        # Assert
        self.assertEqual(found, ["ALLOWED: FiberBatchScheduler.RunImmediateCallback no longer references the door "
                                 "FiberBatchScheduler.DrainImmediate"])


class ScanTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="entry_sites_check_"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def write(self, relative, source):
        path = self.root / entry_sites_check.PACKAGE / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source, encoding="utf-8")

    def test_Given_ADoorCalledFromATestDirectory_When_TheTreeIsScanned_Then_ItIsNotReported(self):
        # Arrange
        self.write("Runtime/Reconciler/Tests/Editor/ProbeTests.cs", member("FiberRenderer.Dispose(fiber);"))

        # Act
        found = [line for line in entry_sites_check.scan(self.root) if not line.startswith("ALLOWED:")]

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADoorCalledFromTheEditorDirectory_When_TheTreeIsScanned_Then_ItIsReported(self):
        # Arrange
        self.write("Editor/Probe.cs", member("FiberRenderer.Dispose(fiber);"))

        # Act
        found = [line for line in entry_sites_check.scan(self.root) if not line.startswith("ALLOWED:")]

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ATreeWithAFinding_When_TheCheckRuns_Then_ItExitsOne(self):
        # Arrange
        self.write("Runtime/Probe.cs", member("FiberRenderer.Dispose(fiber);"))

        # Act
        with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
            code = entry_sites_check.main(["--root", str(self.root)])

        # Assert
        self.assertEqual(code, 1)


class RepositoryTests(unittest.TestCase):
    def test_Given_ThisRepository_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange
        root = REPO_ROOT

        # Act
        found = entry_sites_check.scan(root)

        # Assert
        self.assertEqual("\n".join(found), "")


if __name__ == "__main__":
    unittest.main()
