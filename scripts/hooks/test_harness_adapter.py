#!/usr/bin/env python3
"""Protocol checks for the shared harness adapter."""

import importlib.util
import json
from pathlib import Path
import tempfile
import subprocess
import sys
import unittest


ADAPTER_PATH = Path(__file__).resolve().parents[1] / "harness" / "adapter.py"
SPEC = importlib.util.spec_from_file_location("harness_adapter", ADAPTER_PATH)
adapter = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(adapter)


class HarnessAdapterTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)

    def test_Given_CursorShell_When_Normalized_Then_WorkingDirectoryAndBashAreUsed(self):
        # Arrange
        payload = {"tool_name": "Shell", "cwd": "/other", "tool_input": {
            "command": "git status", "working_directory": str(self.root)}}
        # Act
        record, = adapter.normalize("cursor", "PreToolUse", payload)
        # Assert
        self.assertEqual((record["tool_name"], record["cwd"], record["tool_input"]["command"]),
                         ("Bash", str(self.root), "git status"))

    def test_Given_CursorRelativeWrite_When_Normalized_Then_PathUsesWorkingDirectory(self):
        # Arrange
        payload = {"tool_name": "Write", "tool_input": {
            "path": "nested/file.md", "working_directory": str(self.root), "content": "text"}}
        # Act
        record, = adapter.normalize("cursor", "PreToolUse", payload)
        # Assert
        self.assertEqual(record["tool_input"]["file_path"], str(self.root / "nested/file.md"))

    def test_Given_AddPatch_When_Normalized_Then_GuardReceivesWriteContent(self):
        # Arrange
        payload = {"tool_name": "apply_patch", "cwd": str(self.root), "tool_input": {
            "command": "*** Begin Patch\n*** Add File: new.md\n+one\n+two\n*** End Patch"}}
        # Act
        record, = adapter.normalize("codex", "PreToolUse", payload)
        # Assert
        self.assertEqual((record["tool_name"], record["tool_input"]),
                         ("Write", {"file_path": str(self.root / "new.md"), "content": "one\ntwo\n"}))

    def test_Given_UpdatePatch_When_Read_Then_UnchangedLinesRemain(self):
        # Arrange
        (self.root / "file.md").write_text("before\nold\nafter\n")
        patch = "*** Begin Patch\n*** Update File: file.md\n@@\n-old\n+new\n*** End Patch"
        # Act
        changes = adapter.patch_files(patch, self.root)
        # Assert
        self.assertEqual(changes, [(str(self.root / "file.md"), "before\nnew\nafter\n")])

    def test_Given_DeletePatch_When_Read_Then_GuardReceivesEmptyContent(self):
        # Arrange
        (self.root / "file.md").write_text("old\n")
        patch = "*** Begin Patch\n*** Delete File: file.md\n*** End Patch"
        # Act
        changes = adapter.patch_files(patch, self.root)
        # Assert
        self.assertEqual(changes, [(str(self.root / "file.md"), "")])

    def test_Given_MovePatch_When_Read_Then_SourceAndDestinationAreInspected(self):
        # Arrange
        (self.root / "old.md").write_text("old\n")
        patch = ("*** Begin Patch\n*** Update File: old.md\n*** Move to: new.md\n"
                 "@@\n-old\n+new\n*** End Patch")
        # Act
        changes = adapter.patch_files(patch, self.root)
        # Assert
        self.assertEqual(changes, [(str(self.root / "old.md"), ""), (str(self.root / "new.md"), "new\n")])

    def test_Given_MultipleHunks_When_Read_Then_EachReplacementUsesUpdatedOffsets(self):
        # Arrange
        (self.root / "file.md").write_text("first\ngap\nlast\n")
        patch = ("*** Begin Patch\n*** Update File: file.md\n@@\n-first\n+one\n+two\n"
                 "@@\n-last\n+end\n*** End Patch")
        # Act
        changes = adapter.patch_files(patch, self.root)
        # Assert
        self.assertEqual(changes, [(str(self.root / "file.md"), "one\ntwo\ngap\nend\n")])

    def test_Given_MalformedEnvelope_When_Read_Then_ItIsRefused(self):
        # Arrange
        patch = "*** Add File: file.md\n+content"
        # Act / Assert
        with self.assertRaisesRegex(ValueError, "Unrecognized apply_patch envelope"):
            adapter.patch_files(patch, self.root)

    def test_Given_MalformedHunk_When_Read_Then_ItIsRefused(self):
        # Arrange
        (self.root / "file.md").write_text("old\n")
        patch = "*** Begin Patch\n*** Update File: file.md\n@@\ninvalid\n*** End Patch"
        # Act / Assert
        with self.assertRaisesRegex(ValueError, "Unrecognized patch hunk"):
            adapter.patch_files(patch, self.root)

    def test_Given_DeleteWithUnexpectedBody_When_Read_Then_ItIsRefused(self):
        # Arrange
        (self.root / "file.md").write_text("old\n")
        patch = "*** Begin Patch\n*** Delete File: file.md\ninvalid\n*** End Patch"
        # Act / Assert
        with self.assertRaisesRegex(ValueError, "[Dd]elet|[Uu]nrecognized"):
            adapter.patch_files(patch, self.root)

    def test_Given_AmbiguousContext_When_Read_Then_ItIsRefused(self):
        # Arrange
        (self.root / "file.md").write_text("old\nold\n")
        patch = "*** Begin Patch\n*** Update File: file.md\n@@\n-old\n+new\n*** End Patch"
        # Act / Assert
        with self.assertRaisesRegex(ValueError, "Patch context is missing or ambiguous"):
            adapter.patch_files(patch, self.root)

    def test_Given_MissingContext_When_Read_Then_ItIsRefused(self):
        # Arrange
        (self.root / "file.md").write_text("different\n")
        patch = "*** Begin Patch\n*** Update File: file.md\n@@\n-old\n+new\n*** End Patch"
        # Act / Assert
        with self.assertRaisesRegex(ValueError, "Patch context is missing or ambiguous"):
            adapter.patch_files(patch, self.root)

    def test_Given_DenyJson_When_TranslatedForCursor_Then_PermissionIsDenied(self):
        # Arrange
        output = json.dumps({"hookSpecificOutput": {"permissionDecision": "deny",
                                                   "permissionDecisionReason": "blocked"}})
        # Act
        code, out, err = adapter.translate("cursor", "PreToolUse", 0, output, "")
        # Assert
        self.assertEqual((code, json.loads(out), err),
                         (0, {"permission": "deny", "user_message": "blocked", "agent_message": "blocked"}, ""))

    def test_Given_ExitTwo_When_TranslatedForCursor_Then_PermissionIsDenied(self):
        # Arrange
        reason = "blocked by guard"
        # Act
        code, out, err = adapter.translate("cursor", "PreToolUse", 2, "", reason)
        # Assert
        self.assertEqual((code, json.loads(out), err),
                         (0, {"permission": "deny", "user_message": reason, "agent_message": reason}, ""))

    def test_Given_DeniedStop_When_TranslatedForCursor_Then_FollowupIsRequested(self):
        # Arrange
        reason = "finish verification"
        # Act
        code, out, err = adapter.translate("cursor", "Stop", 2, "", reason)
        # Assert
        self.assertEqual((code, json.loads(out), err), (0, {"followup_message": reason}, ""))

    def test_Given_SubagentContext_When_TranslatedForCursor_Then_ItIsLoggedWithoutFollowup(self):
        # Arrange
        output = json.dumps({"hookSpecificOutput": {"additionalContext": "review complete"}})
        # Act
        code, out, err = adapter.translate("cursor", "SubagentStop", 0, output, "")
        # Assert
        self.assertEqual((code, json.loads(out), err), (0, {}, "review complete"))

    def test_Given_SubagentContext_When_TranslatedForCodex_Then_SystemMessageCarriesIt(self):
        # Arrange
        output = json.dumps({"hookSpecificOutput": {"additionalContext": "review complete"}})
        # Act
        code, out, err = adapter.translate("codex", "SubagentStop", 0, output, "")
        # Assert
        self.assertEqual((code, json.loads(out), err), (0, {"systemMessage": "review complete"}, ""))


class HarnessAdapterCliTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        subprocess.run(["git", "init", "-q", str(self.root)], check=True, capture_output=True)

    def pose(self, client, script, payload):
        return subprocess.run([sys.executable, str(ADAPTER_PATH), client, "PreToolUse", script],
                              input=json.dumps(payload), capture_output=True, text=True,
                              cwd=self.root, timeout=30)

    def test_Given_CursorShellSweep_When_SharedGuardRuns_Then_ItDeniesTheCommand(self):
        # Arrange
        payload = {"tool_name": "Shell", "tool_input": {
            "command": "git add -A", "working_directory": str(self.root)}}
        # Act
        result = self.pose("cursor", "refuse/blind_git_add.py", payload)
        # Assert
        self.assertEqual((result.returncode, json.loads(result.stdout)["permission"]), (0, "deny"))

    def test_Given_CodexBashSweep_When_SharedGuardRuns_Then_ItDeniesTheCommand(self):
        # Arrange
        payload = {"tool_name": "Bash", "cwd": str(self.root),
                   "tool_input": {"command": "git add -A"}}
        # Act
        result = self.pose("codex", "refuse/blind_git_add.py", payload)
        # Assert
        self.assertEqual((result.returncode,
                          json.loads(result.stdout)["hookSpecificOutput"]["permissionDecision"]),
                         (0, "deny"))

    def test_Given_CursorStatus_When_SharedGuardRuns_Then_ItAllowsTheCommand(self):
        # Arrange
        payload = {"tool_name": "Shell", "tool_input": {
            "command": "git status", "working_directory": str(self.root)}}
        # Act
        result = self.pose("cursor", "refuse/blind_git_add.py", payload)
        # Assert
        self.assertEqual((result.returncode, json.loads(result.stdout)["permission"]), (0, "allow"))

    def test_Given_CodexStatus_When_SharedGuardRuns_Then_ItAllowsTheCommand(self):
        # Arrange
        payload = {"tool_name": "Bash", "cwd": str(self.root),
                   "tool_input": {"command": "git status"}}
        # Act
        result = self.pose("codex", "refuse/blind_git_add.py", payload)
        # Assert
        self.assertEqual((result.returncode, json.loads(result.stdout)), (0, {}))

    def test_Given_SecondPatchedFileHasBrokenDeclaration_When_SharedGuardRuns_Then_ItIsDenied(self):
        # Arrange
        marker = "GREEN_" + "ON_BASE(characterization)"
        patch = ("*** Begin Patch\n*** Add File: Safe.cs\n+internal sealed class Safe {}\n"
                 "*** Add File: Broken.cs\n+namespace Velvet.Tests\n+{\n"
                 f"+    // {marker}: the base already separates these and\n"
                 "+    // The rest of the sentence sits under it.\n+}\n*** End Patch")
        payload = {"tool_name": "apply_patch", "cwd": str(self.root), "tool_input": {"command": patch}}
        # Act
        result = self.pose("codex", "refuse/declaration_first_line_fragment.py", payload)
        # Assert
        self.assertEqual((result.returncode, "declaration's first line breaks off" in result.stderr),
                         (2, True))


if __name__ == "__main__":
    unittest.main()
