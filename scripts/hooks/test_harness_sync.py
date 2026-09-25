#!/usr/bin/env python3
"""Exercise shared harness links and client configuration in temporary trees."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location(
    "harness_sync", Path(__file__).resolve().parents[1] / "harness/sync.py")
sync = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(sync)


class HarnessSyncTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        sources = {
            ".harness/hooks/refuse/check.py": "print('shared policy')\n",
            ".agents/skills/sample/SKILL.md": "---\nname: sample\n---\nShared skill\n",
            ".harness/agents/reviewer.md": "---\nname: reviewer\ndescription: Review\n---\nShared prompt\n",
            ".harness/hooks.json": json.dumps([{
                "event": "PreToolUse", "tools": ["Bash"],
                "script": "refuse/check.py", "timeout": 15}]),
            ".claude/settings.json": '{"permissions": {"deny": ["Read(secret)"]}}',
        }
        for relative, content in sources.items():
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content)

    def test_Given_RepositoryPolicies_When_ClientsAreGenerated_Then_RepositoryWorkCannotBlockStop(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        # Act
        generated = sync.outputs(root)
        commands = []
        for client, filename, event in (("claude", "settings.json", "Stop"),
                                        ("codex", "hooks.json", "Stop"),
                                        ("cursor", "hooks.json", "stop")):
            groups = json.loads(generated[f".{client}/{filename}"])["hooks"].get(event, [])
            for group in groups:
                commands.extend(hook["command"] for hook in group.get("hooks", [group]))
        # Assert
        self.assertEqual([command for command in commands
                          if any(name in command for name in
                                 ("stop/open_backlog.py", "stop/unsettled_pr.py"))], [])

    def test_Given_SharedSources_When_Edited_Then_ClientsReadChangesWithoutSynchronization(self):
        # Arrange
        sync.synchronize(self.root)
        sources = {
            ".harness/hooks/refuse/check.py": (".claude/hooks/refuse/check.py", ".codex/hooks/refuse/check.py"),
            ".agents/skills/sample/SKILL.md": (".claude/skills/sample/SKILL.md", ".agents/skills/sample/SKILL.md"),
            ".harness/agents/reviewer.md": (".claude/agents/reviewer.md",),
        }
        # Act
        for source in sources:
            (self.root / source).write_text("Updated source\n")
        # Assert
        self.assertEqual(
            {client: (self.root / client).read_text() for clients in sources.values() for client in clients},
            {client: "Updated source\n" for clients in sources.values() for client in clients})

    def test_Given_LinkedSkillDirectories_When_SourceIsAdded_Then_ClientsSeeItWithoutSynchronization(self):
        # Arrange
        sync.synchronize(self.root)
        source = self.root / ".agents/skills/new/SKILL.md"
        source.parent.mkdir()
        # Act
        source.write_text("New skill\n")
        # Assert
        self.assertEqual([(self.root / client / "skills/new/SKILL.md").read_text()
                          for client in (".claude", ".agents")], ["New skill\n"] * 2)

    def test_Given_LinkedSkillDirectories_When_SourceIsRemoved_Then_ClientsLoseItWithoutSynchronization(self):
        # Arrange
        sync.synchronize(self.root)
        targets = [self.root / client / "skills/sample/SKILL.md"
                   for client in (".claude", ".agents")]
        before = [path.exists() for path in targets]
        # Act
        (self.root / ".agents/skills/sample/SKILL.md").unlink()
        # Assert
        self.assertEqual((before, [path.exists() for path in targets]), ([True] * 2, [False] * 2))

    def test_Given_NativeSharedSkills_When_Synchronized_Then_CursorSpecificSkillDirectoryIsAbsent(self):
        # Arrange
        target = self.root / ".cursor/skills"
        # Act
        sync.synchronize(self.root)
        # Assert
        self.assertEqual(((self.root / ".cursor/hooks.json").is_file(), target.exists(), target.is_symlink()),
                         (True, False, False))

    def test_Given_SynchronizedClients_When_CheckedAgain_Then_NoFilesAreWritten(self):
        # Arrange
        sync.synchronize(self.root)
        before = {str(path.relative_to(self.root)): path.lstat().st_mtime_ns
                  for path in self.root.rglob("*")}
        # Act
        changed = sync.synchronize(self.root, check=True)
        after = {str(path.relative_to(self.root)): path.lstat().st_mtime_ns
                 for path in self.root.rglob("*")}
        # Assert
        self.assertEqual((changed, after), ([], before))

    def test_Given_RealClientDirectory_When_Synchronized_Then_LocalFilesArePreservedAndConflictIsRefused(self):
        # Arrange
        target = self.root / ".claude/skills/local/SKILL.md"
        target.parent.mkdir(parents=True)
        target.write_text("Local skill\n")
        # Act
        try:
            sync.synchronize(self.root)
            refused = False
        except ValueError:
            refused = True
        # Assert
        self.assertEqual((refused, target.read_text()), (True, "Local skill\n"))

    def test_Given_AgentPrompt_When_DescriptorsAreGenerated_Then_ClientsReferenceItsOwner(self):
        # Arrange
        # Act
        generated = sync.outputs(self.root)
        descriptors = [generated[".codex/agents/reviewer.toml"], generated[".cursor/agents/reviewer.md"]]
        # Assert
        self.assertEqual([(".harness/agents/reviewer.md" in text, "Shared prompt" in text)
                          for text in descriptors], [(True, False)] * 2)

    def test_Given_RemovedAgentSource_When_Synchronized_Then_GeneratedDescriptorsAreRemoved(self):
        # Arrange
        sync.synchronize(self.root)
        targets = [self.root / ".codex/agents/reviewer.toml", self.root / ".cursor/agents/reviewer.md"]
        before = [path.exists() for path in targets]
        (self.root / ".harness/agents/reviewer.md").unlink()
        # Act
        sync.synchronize(self.root)
        # Assert
        self.assertEqual((before, [path.exists() for path in targets]), ([True, True], [False, False]))

    def test_Given_UnrelatedHandAuthoredAgents_When_Synchronized_Then_DescriptorsArePreserved(self):
        # Arrange
        sync.synchronize(self.root)
        targets = [self.root / ".codex/agents/local.toml", self.root / ".cursor/agents/local.md"]
        for path in targets:
            path.write_text("Local role\n")
        # Act
        sync.synchronize(self.root)
        # Assert
        self.assertEqual([path.read_text() for path in targets], ["Local role\n", "Local role\n"])

    def test_Given_PersonalPermissions_When_Generated_Then_PermissionsArePreserved(self):
        # Arrange
        # Act
        config = json.loads(sync.outputs(self.root)[".claude/settings.json"])
        # Assert
        self.assertEqual(config["permissions"], {"deny": ["Read(secret)"]})

    def test_Given_WholeSecondFloatTimeout_When_GeneratedForCodex_Then_TimeoutIsAnInteger(self):
        # Arrange
        manifest = self.root / ".harness/hooks.json"
        entries = json.loads(manifest.read_text())
        entries[0]["timeout"] = 15.0
        manifest.write_text(json.dumps(entries))
        # Act
        config = json.loads(sync.outputs(self.root)[".codex/hooks.json"])
        timeout = config["hooks"]["PreToolUse"][0]["hooks"][0]["timeout"]
        # Assert
        self.assertEqual((type(timeout), timeout), (int, 15))

    def test_Given_FractionalTimeout_When_Generated_Then_OnlyCodexRoundsUpToWholeSeconds(self):
        # Arrange
        manifest = self.root / ".harness/hooks.json"
        entries = json.loads(manifest.read_text())
        entries[0]["timeout"] = 1.5
        manifest.write_text(json.dumps(entries))
        # Act
        generated = sync.outputs(self.root)
        codex = json.loads(generated[".codex/hooks.json"])
        claude = json.loads(generated[".claude/settings.json"])
        cursor = json.loads(generated[".cursor/hooks.json"])
        # Assert
        self.assertEqual((codex["hooks"]["PreToolUse"][0]["hooks"][0]["timeout"],
                          claude["hooks"]["PreToolUse"][0]["hooks"][0]["timeout"],
                          cursor["hooks"]["preToolUse"][0]["timeout"]), (2, 1.5, 1.5))

    def test_Given_ShellRegistration_When_GeneratedForCursor_Then_NativeMatcherIsUsed(self):
        # Arrange
        # Act
        config = json.loads(sync.outputs(self.root)[".cursor/hooks.json"])
        # Assert
        self.assertEqual((config["version"], config["hooks"]["preToolUse"][0]["matcher"],
                          config["hooks"]["preToolUse"][0]["timeout"]), (1, "Shell", 15))


if __name__ == "__main__":
    unittest.main()
