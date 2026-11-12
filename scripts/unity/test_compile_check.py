#!/usr/bin/env python3
"""Holds `compile_check.py` to what it has to tell apart without a compiler: a source an asmdef owns
from one an asmdef nested below it owns, a name the editor compiles from one it skips, an assembly
from the ones it references, and, in the editor's response file, a line reused as it stands from one
the sources have moved past.

Run: python3 scripts/unity/test_compile_check.py
"""

import json
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import compile_check  # noqa: E402


class CompileCheckTests(unittest.TestCase):
    def setUp(self):
        self.tree = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.tree, True)

    def asmdef(self, folder, name, references=(), **fields):
        path = self.tree / folder / f"{name}.asmdef"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"name": name, "references": list(references), **fields}), encoding="utf-8")
        return path

    def file(self, relative, text="class A {}"):
        path = self.tree / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def lines_for(self, asmdef, editor_lines, own_names=("Velvet",), sources=(), out=None, sources_root=None):
        out = out or self.tree / "out"
        out.mkdir(exist_ok=True)
        return compile_check.response_lines(compile_check.name_of(asmdef), asmdef, editor_lines, set(own_names),
                                            list(sources), out, self.tree, sources_root or self.tree)

    # --- which sources an asmdef owns ---

    def test_Given_AnAsmdefNestedBelow_When_SourcesAreRead_Then_TheNestedFolderIsLeftOut(self):
        # Arrange
        outer = self.asmdef("Runtime", "Velvet")
        inner = self.asmdef("Runtime/Hooks/Tests/Editor", "Velvet.Tests.Hooks.Editor")
        nested = self.file("Runtime/Hooks/Tests/Editor/HookTests.cs")
        # Act
        sources = compile_check.sources_of(outer, [outer, inner])
        # Assert
        self.assertNotIn(nested, sources)

    def test_Given_AnAsmdefNestedBelow_When_SourcesAreRead_Then_ASiblingOfTheNestedFolderStays(self):
        # Arrange
        outer = self.asmdef("Runtime", "Velvet")
        inner = self.asmdef("Runtime/Hooks/Tests/Editor", "Velvet.Tests.Hooks.Editor")
        own = self.file("Runtime/Hooks/Hooks.cs")
        # Act
        sources = compile_check.sources_of(outer, [outer, inner])
        # Assert
        self.assertIn(own, sources)

    def test_Given_AnAsmdefAboveThisOne_When_SourcesAreRead_Then_ItsOwnSourcesAreNotTakenForTheParents(self):
        # Arrange
        outer = self.asmdef("Runtime", "Velvet")
        inner = self.asmdef("Runtime/Hooks/Tests/Editor", "Velvet.Tests.Hooks.Editor")
        own = self.file("Runtime/Hooks/Tests/Editor/HookTests.cs")
        # Act
        sources = compile_check.sources_of(inner, [outer, inner])
        # Assert
        self.assertEqual([own], sources)

    def test_Given_AFolderNamedWithATrailingTilde_When_SourcesAreRead_Then_ItsSourceIsLeftOut(self):
        # Arrange
        asmdef = self.asmdef("Package", "Velvet")
        skipped = self.file("Package/Samples~/App/App.cs")
        # Act
        sources = compile_check.sources_of(asmdef, [asmdef])
        # Assert
        self.assertNotIn(skipped, sources)

    def test_Given_AFileNamedWithALeadingDot_When_SourcesAreRead_Then_ItIsLeftOut(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        skipped = self.file("Runtime/.Hidden.cs")
        # Act
        sources = compile_check.sources_of(asmdef, [asmdef])
        # Assert
        self.assertNotIn(skipped, sources)

    def test_Given_AnAsmdefUnderAFolderNamedWithATrailingTilde_When_AsmdefsAreListed_Then_ItIsLeftOut(self):
        # Arrange
        self.asmdef("Package/Samples~/App", "Velvet.Samples.App")
        kept = self.asmdef("Package/Runtime", "Velvet")
        # Act
        found = compile_check.asmdefs_under(self.tree)
        # Assert
        self.assertEqual([kept], found)

    def test_Given_AnAsmdefUnderTheProjectsLibrary_When_AsmdefsAreListed_Then_ItIsLeftOut(self):
        # Arrange
        self.asmdef("Library/PackageCache/com.unity.ugui/Runtime", "UnityEngine.UI")
        kept = self.asmdef("Packages/Runtime", "Velvet")
        # Act
        found = compile_check.asmdefs_under(self.tree)
        # Assert
        self.assertEqual([kept], found)

    # --- order and selection ---

    def test_Given_AReferrerThatSortsBeforeItsReference_When_Ordered_Then_TheReferencedOneStillComesFirst(self):
        # Arrange
        tests = self.asmdef("Tests", "Alpha.Tests", references=["Velvet"])
        runtime = self.asmdef("Runtime", "Velvet")
        # Act
        order = compile_check.compile_order([tests, runtime])
        # Assert
        self.assertLess(order.index("Velvet"), order.index("Alpha.Tests"))

    def test_Given_AReferenceToAnAssemblyOutsideTheSet_When_Ordered_Then_TheReferrerIsStillOrdered(self):
        # Arrange
        tests = self.asmdef("Tests", "Velvet.Tests", references=["UnityEngine.TestRunner"])
        # Act
        order = compile_check.compile_order([tests])
        # Assert
        self.assertEqual(["Velvet.Tests"], order)

    def test_Given_ASelectedAssemblyReferencingAnother_When_Closed_Then_TheReferencedOneIsCompiledToo(self):
        # Arrange
        by_name = {"Velvet": self.asmdef("Runtime", "Velvet"),
                   "Velvet.Tests": self.asmdef("Tests", "Velvet.Tests", references=["Velvet"])}
        # Act
        closed = compile_check.with_references({"Velvet.Tests"}, by_name)
        # Assert
        self.assertIn("Velvet", closed)

    def test_Given_AReferenceToAnAssemblyAPackageDeclares_When_Closed_Then_ItIsNotAddedToTheRun(self):
        # Arrange
        by_name = {"Velvet.Tests": self.asmdef("Tests", "Velvet.Tests", references=["UnityEngine.TestRunner"])}
        # Act
        closed = compile_check.with_references({"Velvet.Tests"}, by_name)
        # Assert
        self.assertEqual({"Velvet.Tests"}, closed)

    def test_Given_AnAsmdefPathAndAName_When_SelectionIsRead_Then_BothResolveToNames(self):
        # Arrange
        runtime = self.asmdef("Runtime", "Velvet")
        editor = self.asmdef("Editor", "Velvet.Editor")
        by_name = {"Velvet": runtime, "Velvet.Editor": editor}
        # Act
        selected = compile_check.selected_names([str(runtime), "Velvet.Editor"], by_name)
        # Assert
        self.assertEqual({"Velvet", "Velvet.Editor"}, selected)

    def test_Given_ARelativePathToTheTreesAsmdef_When_SelectionIsRead_Then_ItResolvesToTheName(self):
        # Arrange
        runtime = self.asmdef("Runtime", "Velvet")
        relative = os.path.relpath(runtime, Path.cwd())
        # Act
        selected = compile_check.selected_names([relative], {"Velvet": runtime})
        # Assert
        self.assertEqual({"Velvet"}, selected)

    def test_Given_ANameNoAsmdefDeclares_When_SelectionIsRead_Then_ItIsRefusedByName(self):
        # Arrange
        by_name = {"Velvet": self.asmdef("Runtime", "Velvet")}
        # Act
        with self.assertRaises(KeyError) as refused:
            compile_check.selected_names(["Velvet.Nowhere"], by_name)
        # Assert
        self.assertEqual("Velvet.Nowhere", refused.exception.args[0])

    def test_Given_AnAsmdefPathOutsideTheTree_When_SelectionIsRead_Then_ItIsRefusedByPath(self):
        # Arrange
        by_name = {"Velvet": self.asmdef("Runtime", "Velvet")}
        elsewhere = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, elsewhere, True)
        stray = elsewhere / "Velvet.asmdef"
        stray.write_text(json.dumps({"name": "Velvet", "references": []}), encoding="utf-8")
        # Act
        with self.assertRaises(KeyError) as refused:
            compile_check.selected_names([str(stray)], by_name)
        # Assert
        self.assertEqual(str(stray), refused.exception.args[0])

    def test_Given_AReferenceThatFailedInThisRun_When_BlockingIsAsked_Then_ItIsNamed(self):
        # Arrange
        tests = self.asmdef("Tests", "Velvet.Tests", references=["Velvet", "UnityEngine.TestRunner"])
        # Act
        blocked = compile_check.blocked_by(tests, {"Velvet", "Velvet.Editor"})
        # Assert
        self.assertEqual(["Velvet"], blocked)

    # --- the editor's response file ---

    def test_Given_TwoResponseFilesForOneAssembly_When_TheEditorsIsLooked_Then_TheNewerIsTaken(self):
        # Arrange
        older = self.file("Library/Bee/artifacts/aaa.dag/Velvet.rsp", "-target:library")
        newer = self.file("Library/Bee/artifacts/bbb.dag/Velvet.rsp", "-target:library")
        os.utime(older, (1_000_000, 1_000_000))
        os.utime(newer, (2_000_000, 2_000_000))
        # Act
        found = compile_check.editor_response(self.tree, "Velvet")
        # Assert
        self.assertEqual(newer, found)

    def test_Given_NoResponseFileForAnAssembly_When_TheEditorsIsLooked_Then_NoneIsAnswered(self):
        # Arrange
        self.file("Library/Bee/artifacts/aaa.dag/Velvet.rsp", "-target:library")
        # Act
        found = compile_check.editor_response(self.tree, "Velvet.Editor")
        # Assert
        self.assertIsNone(found)

    def test_Given_TheEditorsReferenceToOneOfThisProjectsAssemblies_When_LinesAreBuilt_Then_ItIsNotReused(self):
        # Arrange
        tests = self.asmdef("Tests", "Velvet.Tests", references=["Velvet"])
        editor = ['-r:"Library/Bee/artifacts/aaa.dag/Velvet.ref.dll"']
        # Act
        lines = self.lines_for(tests, editor, own_names=("Velvet", "Velvet.Tests"))
        # Assert
        self.assertNotIn(f'-reference:"{self.tree / "Library/Bee/artifacts/aaa.dag/Velvet.ref.dll"}"', lines)

    def test_Given_AReferenceThisRunCompiled_When_LinesAreBuilt_Then_TheFreshCopyIsNamed(self):
        # Arrange
        tests = self.asmdef("Tests", "Velvet.Tests", references=["Velvet"])
        out = self.tree / "out"
        out.mkdir()
        (out / "Velvet.dll").write_bytes(b"")
        # Act
        lines = self.lines_for(tests, [], own_names=("Velvet", "Velvet.Tests"), out=out)
        # Assert
        self.assertIn(f'-reference:"{out / "Velvet.dll"}"', lines)

    def test_Given_AReferenceThisRunDidNotCompile_When_LinesAreBuilt_Then_TheLibraryCopyIsNamed(self):
        # Arrange
        tests = self.asmdef("Tests", "Velvet.Tests", references=["UnityEngine.TestRunner"])
        # Act
        lines = self.lines_for(tests, [])
        # Assert
        self.assertIn(f'-reference:"{self.tree / "Library/ScriptAssemblies/UnityEngine.TestRunner.dll"}"', lines)

    def test_Given_TheEditorsReferenceToAPackageAssembly_When_LinesAreBuilt_Then_ItIsReusedAsTheEditorNamedIt(self):
        # Arrange
        tests = self.asmdef("Tests", "Velvet.Tests")
        editor = ['-r:"Library/Bee/artifacts/aaa.dag/UnityEngine.UI.ref.dll"']
        # Act
        lines = self.lines_for(tests, editor)
        # Assert
        self.assertIn(f'-reference:"{self.tree / "Library/Bee/artifacts/aaa.dag/UnityEngine.UI.ref.dll"}"', lines)

    def test_Given_APackageAssemblyBothTheEditorAndTheAsmdefName_When_LinesAreBuilt_Then_ItIsReferencedOnce(self):
        # Arrange
        tests = self.asmdef("Tests", "Velvet.Tests", references=["UnityEngine.TestRunner"])
        editor = ['-r:"Library/Bee/artifacts/aaa.dag/UnityEngine.TestRunner.ref.dll"']
        # Act
        lines = self.lines_for(tests, editor)
        # Assert
        self.assertEqual(1, sum(1 for line in lines if "UnityEngine.TestRunner" in line))

    def test_Given_AReferencePathWithASpace_When_LinesAreBuilt_Then_ItIsQuoted(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        editor = ['-r:"/Applications/Unity Hub/netstandard.dll"']
        # Act
        lines = self.lines_for(asmdef, editor)
        # Assert
        self.assertIn('-reference:"/Applications/Unity Hub/netstandard.dll"', lines)

    def test_Given_TheEditorsOutputLines_When_LinesAreBuilt_Then_OnlyTheRunsOwnOutputStands(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        out = self.tree / "out"
        editor = ['-out:"Library/Bee/artifacts/aaa.dag/Velvet.dll"', '-refout:"Library/Bee/artifacts/aaa.dag/Velvet.ref.dll"']
        # Act
        lines = self.lines_for(asmdef, editor, out=out)
        # Assert
        self.assertEqual([f'-out:"{out / "Velvet.dll"}"'], [line for line in lines if line.startswith(("-out:", "-refout:"))])

    def test_Given_ASourceLineInTheEditorsFile_When_LinesAreBuilt_Then_ItIsNotCarried(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        editor = ['"Packages/com.velvet.core/Runtime/Deleted.cs"']
        # Act
        lines = self.lines_for(asmdef, editor)
        # Assert
        self.assertNotIn('"Packages/com.velvet.core/Runtime/Deleted.cs"', lines)

    def test_Given_ASourceTheAsmdefOwns_When_LinesAreBuilt_Then_ItEndsTheFileQuoted(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        source = self.file("Runtime/A.cs")
        # Act
        lines = self.lines_for(asmdef, ["-target:library"], sources=[source])
        # Assert
        self.assertEqual(f'"{source}"', lines[-1])

    def test_Given_TheEditorsSlashSpelledOption_When_LinesAreBuilt_Then_ItIsReusedAsItStands(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        # Act
        lines = self.lines_for(asmdef, ["/nowarn:0649"])
        # Assert
        self.assertIn("/nowarn:0649", lines)

    def test_Given_TheEditorsAdditionalFile_When_LinesAreBuilt_Then_ItIsRootedUnderTheProjectAndQuoted(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        editor = ['/additionalfile:"Library/Bee/artifacts/aaa.dag/Velvet.UnityAdditionalFile.txt"']
        # Act
        lines = self.lines_for(asmdef, editor)
        # Assert
        self.assertIn(f'/additionalfile:"{self.tree / "Library/Bee/artifacts/aaa.dag/Velvet.UnityAdditionalFile.txt"}"', lines)

    def test_Given_TheEditorsUnsafeFlag_When_TheAsmdefAllowsUnsafeCode_Then_TheFlagComesFromTheAsmdef(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet", allowUnsafeCode=True)
        # Act
        lines = self.lines_for(asmdef, ["/unsafe+"])
        # Assert
        self.assertEqual(["-unsafe"], [line for line in lines if line.lower().startswith(("-unsafe", "/unsafe"))])

    def test_Given_TheEditorsUnsafeFlag_When_TheAsmdefNoLongerAllowsUnsafeCode_Then_NoFlagIsPassed(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet", allowUnsafeCode=False)
        # Act
        lines = self.lines_for(asmdef, ["/unsafe+"])
        # Assert
        self.assertEqual([], [line for line in lines if line.lower().startswith(("-unsafe", "/unsafe"))])

    def test_Given_AnAnalyzerTheEditorTookFromInsideTheProject_When_TheSourcesHoldIt_Then_TheSourcesCopyIsNamed(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        sources_root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, sources_root, True)
        moved = sources_root / "Packages/Plugins/Velvet.SourceGenerators.dll"
        moved.parent.mkdir(parents=True)
        moved.write_bytes(b"")
        editor = ['-analyzer:"Packages/Plugins/Velvet.SourceGenerators.dll"']
        # Act
        lines = self.lines_for(asmdef, editor, sources_root=sources_root)
        # Assert
        self.assertIn(f'-analyzer:"{moved}"', lines)

    def test_Given_AnAnalyzerFromTheEditorInstall_When_LinesAreBuilt_Then_ItIsReusedWhereItIs(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        editor = ['-analyzer:"/Applications/Unity/Unity.SourceGenerators.dll"']
        # Act
        lines = self.lines_for(asmdef, editor)
        # Assert
        self.assertIn('-analyzer:"/Applications/Unity/Unity.SourceGenerators.dll"', lines)

    def test_Given_AResponseFileBesideTheAsmdef_When_LinesAreBuilt_Then_ItComesAfterTheEditorsLanguageVersion(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        beside = self.file("Runtime/csc.rsp", "-langversion:preview\n")
        # Act
        lines = self.lines_for(asmdef, ["-langversion:9.0"])
        # Assert
        self.assertLess(lines.index("-langversion:9.0"), lines.index(f'@"{beside}"'))

    def test_Given_NoResponseFileBesideTheAsmdef_When_LinesAreBuilt_Then_NoneIsNamed(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        # Act
        lines = self.lines_for(asmdef, ["-langversion:9.0"])
        # Assert
        self.assertEqual([], [line for line in lines if line.startswith("@")])

    def test_Given_TheEditorsDefine_When_LinesAreBuilt_Then_ItIsReusedAsItStands(self):
        # Arrange
        asmdef = self.asmdef("Runtime", "Velvet")
        # Act
        lines = self.lines_for(asmdef, ["-define:UNITY_INCLUDE_TESTS"])
        # Assert
        self.assertEqual(["-define:UNITY_INCLUDE_TESTS"], [line for line in lines if line.startswith("-define:")])


if __name__ == "__main__":
    unittest.main()
