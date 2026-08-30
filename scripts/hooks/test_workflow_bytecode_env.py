#!/usr/bin/env python3
"""Every workflow that runs Python sets PYTHONDONTWRITEBYTECODE, at job level.

Python validates a cached bytecode file against the source's mtime in whole seconds and its size, so
a length-preserving edit made and undone inside one second leaves the cache valid and the next run
executes the previous state's bytecode against the current source. `base_red_check.py` withdraws a
carried file and asks the tree again, which is that window.

`-B` on an invocation is not the remedy and was measured not to be: with a stale cache in place, a
plain run and a `-B` run both answer from the cache, because the flag stops the write and not the
read. So a cache one invocation writes is read by the next, and the setting is worth nothing unless
every invocation carries it. At job level every one does, which is why this reads the workflow rather
than the call sites.

What it cannot reach is a developer's own shell, where the cache also sits wherever
`sys.pycache_prefix` points -- outside the worktree on macOS, and surviving `git clean`.

Run: python3 scripts/hooks/test_workflow_bytecode_env.py
"""

import unittest
from pathlib import Path

import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = REPO_ROOT / ".github" / "workflows"

SETTING = "PYTHONDONTWRITEBYTECODE"


def runs_python(text):
    return "python3 " in text


class WorkflowBytecodeEnvTests(unittest.TestCase):
    def workflows_running_python(self):
        return sorted(path for path in WORKFLOWS.glob("*.yml")
                      if runs_python(path.read_text(encoding="utf-8")))

    def test_Given_TheWorkflowsHere_When_TheyAreRead_Then_SomeRunPython(self):
        # Arrange -- the floor: a case over an empty set would pass having asked nothing.
        # Act / Assert
        self.assertGreater(len(self.workflows_running_python()), 0)

    def test_Given_AWorkflowThatRunsPython_When_ItIsRead_Then_ItSetsTheSettingAtJobLevel(self):
        # Arrange -- job level rather than step level, because a step that forgets it writes the
        # cache every later step reads.
        without = [path.name for path in self.workflows_running_python()
                   if (yaml.safe_load(path.read_text(encoding="utf-8")).get("env") or {})
                   .get(SETTING) != "1"]

        # Act / Assert
        self.assertEqual(without, [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
