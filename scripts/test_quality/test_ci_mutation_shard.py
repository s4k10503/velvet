#!/usr/bin/env python3
"""Unit tests for ci_mutation_shard.py and for how the workflows launch the editor image.

The licence around the campaign is licensed_editor.py's, and scripts/ci/test_licensed_editor.py holds
it. The editor is replaced by a recorder here, since what the case asks is which command the campaign
is handed.

Run: python3 scripts/test_quality/test_ci_mutation_shard.py
"""

import base64
import importlib.util
import re
import tempfile
import unittest
from pathlib import Path


def load_module():
    """Imports ci_mutation_shard by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "ci_mutation_shard", Path(__file__).with_name("ci_mutation_shard.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ci_mutation_shard = load_module()

REPO_ROOT = Path(__file__).resolve().parents[2]

SERIAL = "F4-ABCD-EFGH-IJKL-MNOP-QRST"


def licence_file(serial):
    """A `.ulf` carrying `serial` where the real one does: base64 behind four characters of padding."""
    payload = base64.b64encode(("\x01\x02\x03\x04" + serial).encode("latin-1")).decode()
    return '<root><License><DeveloperData Value="{}"/></License></root>'.format(payload)


class Recorder:
    """Stands in for `subprocess.call`, answering every command with 0 and keeping each."""

    def __init__(self):
        self.commands = []

    def __call__(self, command, **_options):
        self.commands.append(command)
        return 0


def run_shard(recorder):
    with tempfile.TemporaryDirectory() as holder:
        version = Path(holder) / "ProjectVersion.txt"
        version.write_text("m_EditorVersion: 6000.3.23f1\n")
        return ci_mutation_shard.main(
            ["--", "--base", "abc", "--shard", "0/2"], version_file=version,
            environ={"UNITY_LICENSE": licence_file(SERIAL), "UNITY_EMAIL": "e", "UNITY_PASSWORD": "p"},
            run=recorder, sleep=lambda _seconds: None, machine_id=lambda: None,
            display=lambda: ({}, lambda: None))


class CampaignTests(unittest.TestCase):
    def test_Given_TheCampaignsArguments_When_TheShardRuns_Then_TheyReachItWithTheImagesEditor(self):
        # Arrange
        recorder = Recorder()

        # Act
        run_shard(recorder)

        # Assert
        campaign = next(command for command in recorder.commands
                        if any(str(part).endswith("mutation_check.py") for part in command))
        self.assertEqual(campaign[3:], ["--base", "abc", "--shard", "0/2", "--unity",
                                        ci_mutation_shard.licensed_editor.UNITY])


# The one job whose editor is not the project's: it exists to compile on a later one.
NEWEST_EDITOR_JOB = "newest-editor-compile"
UNITY_VERSION = re.compile(r"unityVersion: (\S+)|unityci/editor:ubuntu-([0-9][^-\s]*)-")
# A job key, or a key back at column zero, which ends the jobs block.
BLOCK_START = re.compile(r"^(?:  [A-Za-z_][A-Za-z0-9_-]*:\s*(?:#.*)?|[A-Za-z].*)$")


def split_job(text, job):
    """A workflow's text as (the lines outside `job`'s block, the lines of it)."""
    outside, inside, within = [], [], False
    for line in text.splitlines(keepends=True):
        if BLOCK_START.match(line.rstrip("\n")):
            within = line.startswith("  {}:".format(job))
        (inside if within else outside).append(line)
    return "".join(outside), "".join(inside)


def named_versions(text):
    return {version for match in UNITY_VERSION.findall(text) for version in match if version}


def project_version():
    found = re.search(r"^m_EditorVersion: (\S+)$",
                      (REPO_ROOT / "ProjectSettings/ProjectVersion.txt").read_text(), re.MULTILINE)
    return found.group(1) if found else None


def minor(version):
    """(major, minor) of an editor version such as 6000.3.23f1, or None for anything else."""
    found = re.match(r"^([0-9]+)\.([0-9]+)\.", version or "")
    return (int(found.group(1)), int(found.group(2))) if found else None


class EditorVersionTests(unittest.TestCase):
    """The editor a shard runs against the one the project and the unity-tests jobs name.

    The shard's image is `docker run` in mutation.yml rather than game-ci's action, so nothing of the
    action's own version reading reaches it.
    """

    # GREEN_ON_BASE(construction): the base's workflows name the project's version alone, so it passes.
    # What shows the exemption is needed is reading each workflow with `path.read_text()` in place of
    # `split_job(path.read_text(), NEWEST_EDITOR_JOB)[0]`: measured, this case alone then fails.
    def test_Given_TheWorkflows_When_TheirEditorVersionsAreRead_Then_EachIsTheProjects(self):
        # Arrange
        project = project_version()
        sources = sorted((REPO_ROOT / ".github").rglob("*.yml"))
        texts = [split_job(path.read_text(), NEWEST_EDITOR_JOB)[0] for path in sources]

        # Act
        named = set().union(*(named_versions(text) for text in texts))
        derived = any('ubuntu-$version-linux-il2cpp-3' in text
                      and "m_EditorVersion" in text for text in texts)

        # Assert — the campaign reads its version from the project, so it names none to compare.
        self.assertEqual((named, derived), ({project} if project else None, True))

    def test_Given_TheNewestEditorJob_When_ItsVersionIsRead_Then_ItIsALaterMinorThanTheProjects(self):
        # Arrange — a version at the project's minor or below says nothing about a later editor, and
        # the job would pass all the same.
        _, block = split_job((REPO_ROOT / ".github/workflows/test.yml").read_text(), NEWEST_EDITOR_JOB)
        project = minor(project_version())

        # Act — the Library cache key carries the version too, so a bump that misses it restores a
        # Library written by the previous editor.
        versions = named_versions(block) | set(re.findall(r"Library-newest-([0-9][^-\s]*)-", block))
        later = {version: project is not None and (minor(version) or (0, 0)) > project
                 for version in versions}

        # Assert — exactly one version, so a job that names none, or spellings that disagree, fails here too.
        self.assertEqual((len(later), all(later.values())), (1, True))


class NewestEditorLaunchTests(unittest.TestCase):
    """How the newest-editor job starts the image, which no editor run here can check."""

    def test_Given_TheNewestEditorJob_When_ItsLaunchIsRead_Then_ItRunsTheCompileUnderALicenceWithTheShmItNeeds(self):
        # Arrange — the 6.6 editor aborts before importing anything without the larger /dev/shm, and
        # a method name nothing declares fails the run only once an editor is up in CI.
        _, block = split_job((REPO_ROOT / ".github/workflows/test.yml").read_text(), NEWEST_EDITOR_JOB)
        source = (REPO_ROOT / "Packages/com.velvet.core/Editor/PlayerBuild/PlayerScriptCompilation.cs").read_text()

        # Act
        launch = re.search(r"docker run (.*?)\n\n|docker run (.*)\Z", block, re.DOTALL)
        text = launch.group(0) if launch else ""
        method = re.search(r"-executeMethod (\S+)\.(\w+)\.(\w+)", text)
        declared = method is not None and all(
            re.search(pattern, source) for pattern in (
                r"\bnamespace {}\s*[{{;\n]".format(re.escape(method.group(1))),
                r"\bclass {}\b".format(method.group(2)),
                r"\bstatic void {}\(\)".format(method.group(3))))

        # Assert
        self.assertEqual(("--shm-size=1025m" in text, "python3 -u scripts/ci/licensed_editor.py --" in text,
                          declared),
                         (True, True, True))


class SplitJobTests(unittest.TestCase):
    """What EditorVersionTests exempts: one job's block and nothing past it."""

    # GREEN_ON_BASE(construction): `split_job` is this file's own, so a base run takes its copy.
    # What shows the case can fail is `within = within or line.startswith(...)`: measured, the block
    # then runs past the next job key, and this case alone fails.
    def test_Given_AJobBetweenTwoOthers_When_Split_Then_ItsBlockEndsAtTheNextJobKey(self):
        # Arrange
        workflow = ("jobs:\n"
                    "  before:\n    with:\n      unityVersion: 1.0.0f1\n"
                    "  newest-editor-compile:  # the job\n    with:\n      unityVersion: 2.0.0f1\n"
                    "  after:\n    with:\n      unityVersion: 3.0.0f1\n")

        # Act
        outside, inside = split_job(workflow, NEWEST_EDITOR_JOB)

        # Assert
        self.assertEqual((named_versions(outside), named_versions(inside)),
                         ({"1.0.0f1", "3.0.0f1"}, {"2.0.0f1"}))


class ShardPlatformTests(unittest.TestCase):
    """Which suites the workflow's shards run a campaign against."""

    def test_Given_TheWorkflow_When_ItsShardLaunchesAreRead_Then_EachPlatformHasOne(self):
        # Arrange — a mutant only a PlayMode fixture kills survives wherever no shard runs that suite.
        workflow = (REPO_ROOT / ".github/workflows/mutation.yml").read_text()
        launches = [launch.partition("\n\n")[0]
                    for launch in workflow.split("ci_mutation_shard.py --")[1:]]

        # Act
        platforms = sorted((re.findall(r"--platform (\w+)", launch) or ["EditMode"])[0]
                           for launch in launches)

        # Assert
        self.assertEqual(platforms, ["EditMode", "PlayMode"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
