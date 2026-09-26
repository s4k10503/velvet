#!/usr/bin/env python3
"""Unit tests for change_base.py, and for the workflows reading the base only through it.

The resolver cases run the script as a workflow step does, in a scratch repository holding a test
merge: a head cut from one commit, merged onto a base branch that has moved on since, so the merge's
first parent and the commit the head was cut from differ.

Run: python3 scripts/ci/test_change_base.py
"""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO_ROOT = HERE.parents[1]
SCRIPT = HERE / "change_base.py"
RESOLVER_RUN = 'python3 scripts/ci/change_base.py >> "$GITHUB_OUTPUT"'


class Repository:
    """`cut` on main, `head` cut from it, `moved` merged to main after, and `merge` of head onto moved."""

    def __init__(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-change-base-"))
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "probe@example.com")
        self.git("config", "user.name", "Probe")
        self.cut = self.commit("cut.txt")
        self.git("checkout", "-q", "-b", "head")
        self.head = self.commit("head.txt")
        self.git("checkout", "-q", "-b", "other", self.cut)
        self.other = self.commit("other.txt")
        self.git("checkout", "-q", "main")
        self.moved = self.commit("moved.txt")
        self.git("checkout", "-q", "--detach", self.moved)
        self.git("merge", "-q", "--no-ff", "--no-edit", self.head)
        self.merge = self.git("rev-parse", "HEAD").strip()

    def git(self, *arguments):
        return subprocess.run(["git"] + list(arguments), cwd=self.root, check=True,
                              capture_output=True, text=True).stdout

    def commit(self, name):
        (self.root / name).write_text(name, encoding="utf-8")
        self.git("add", name)
        self.git("commit", "-q", "-m", name)
        return self.git("rev-parse", "HEAD").strip()

    def resolve(self, event_name, payload):
        event = self.root.parent / (self.root.name + "-event.json")
        event.write_text(json.dumps(payload), encoding="utf-8")
        environment = dict(os.environ, GITHUB_EVENT_NAME=event_name, GITHUB_EVENT_PATH=str(event))
        try:
            return subprocess.run([sys.executable, str(SCRIPT)], cwd=self.root, env=environment,
                                  capture_output=True, text=True)
        finally:
            event.unlink()

    def remove(self):
        shutil.rmtree(self.root, ignore_errors=True)


def pull_request(repository, head):
    return {"pull_request": {"base": {"sha": repository.cut}, "head": {"sha": head}}}


class ResolverTests(unittest.TestCase):

    def setUp(self):
        self.repository = Repository()
        self.addCleanup(self.repository.remove)

    def test_Given_ATestMergeOntoAMovedBase_When_Resolved_Then_ItNamesTheFirstParent(self):
        # Arrange
        repository = self.repository

        # Act
        done = repository.resolve("pull_request", pull_request(repository, repository.head))

        # Assert
        self.assertEqual(done.stdout, "sha={}\n".format(repository.moved))

    def test_Given_ACheckoutOfTheHeadItself_When_Resolved_Then_ItRefuses(self):
        # Arrange
        repository = self.repository
        repository.git("checkout", "-q", "--detach", repository.head)

        # Act
        done = repository.resolve("pull_request", pull_request(repository, repository.head))

        # Assert — the head named in the refusal, so a script that failed to run at all is not one.
        self.assertEqual((done.returncode != 0, repository.head in done.stderr), (True, True))

    def test_Given_ATestMergeOfAnotherHead_When_Resolved_Then_ItRefuses(self):
        # Arrange
        repository = self.repository

        # Act
        done = repository.resolve("pull_request", pull_request(repository, repository.other))

        # Assert
        self.assertEqual((done.returncode != 0, repository.other in done.stderr), (True, True))

    def test_Given_AMergeGroupEvent_When_Resolved_Then_ItNamesTheGroupsBase(self):
        # Arrange — a base_sha that is neither parent, so only the event can have supplied it.
        repository = self.repository
        payload = {"merge_group": {"base_sha": repository.cut, "head_sha": repository.merge}}

        # Act
        done = repository.resolve("merge_group", payload)

        # Assert
        self.assertEqual(done.stdout, "sha={}\n".format(repository.cut))

    def test_Given_APushEvent_When_Resolved_Then_ItNamesNothing(self):
        # Arrange
        repository = self.repository

        # Act
        done = repository.resolve("push", {"before": repository.moved, "after": repository.merge})

        # Assert
        self.assertEqual((done.returncode, done.stdout), (0, "sha=\n"))


def workflow_sources(root=REPO_ROOT):
    github = root / ".github"
    return sorted([path for pattern in ("*.yml", "*.yaml") for path in (github / "workflows").glob(pattern)]
                  + [path for name in ("action.yml", "action.yaml") for path in (github / "actions").rglob(name)])


def without_comments(text):
    return "\n".join(re.sub(r"(^|\s)#.*$", r"\1", line) for line in text.splitlines())


EVENT_BASE = re.compile(r"pull_request\W{1,8}base\W{1,8}sha\b", re.IGNORECASE)


def units(text, composite):
    """Each job of a workflow that has steps, or the whole of a composite action, as (name, steps)."""
    if composite:
        named = ["runs", text]
    else:
        found = re.search(r"^jobs:[ \t]*$", text, re.M)
        named = re.split(r"^  ([A-Za-z0-9_-]+):[ \t]*$", text[found.end():], flags=re.M)[1:] if found else []
    for name, body in zip(named[::2], named[1::2]):
        found = re.search(r"^( *)steps:[ \t]*$", body, re.M)
        if not found:
            continue
        opening = r"^{} {{2}}- ".format(found.group(1))
        after = body[found.end():]
        starts = [match.start() for match in re.finditer(opening, after, re.M)]
        yield name, [after[start:end] for start, end in zip(starts, starts[1:] + [len(after)])]


def wiring(sources):
    """(label, text, composite) -> the steps passing `--base` wrongly, and the jobs that pass one."""
    wrong, reading = [], set()
    for label, text, composite in sources:
        for name, steps in units(without_comments(text), composite):
            resolvers = set()
            for index, step in enumerate(steps):
                where = "{} {} step {}".format(label, name, index)
                identity = re.search(r"^\s*-?\s*id:\s*(\S+)\s*$", step, re.M)
                if RESOLVER_RUN in step and identity:
                    resolvers.add(identity.group(1))
                values = re.findall(r"--base[\s=]+(\S+)", step)
                if not values:
                    continue
                reading.add((label, name))
                if any(not re.match(r'"?\$\{?BASE\b', value) for value in values):
                    wrong.append(where + ": --base " + " ".join(values))
                env = re.search(r"^\s+BASE:\s*(.+?)\s*$", step, re.M)
                wired = env and re.fullmatch(r"\$\{\{\s*steps\.([\w-]+)\.outputs\.sha\s*\}\}", env.group(1))
                if not wired or wired.group(1) not in resolvers:
                    wrong.append(where + ": BASE is " + (env.group(1) if env else "unset"))
    return wrong, reading


def repository_sources():
    return [(str(path.relative_to(REPO_ROOT)), path.read_text(encoding="utf-8"), path.parent.name != "workflows")
            for path in workflow_sources()]


class WorkflowTests(unittest.TestCase):

    def test_Given_EveryWorkflowAndAction_When_ReadWithoutComments_Then_NoneReadsThePullRequestsBaseSha(self):
        # Arrange
        sources = repository_sources()

        # Act
        reading = [label for label, text, _ in sources if EVENT_BASE.search(without_comments(text))]

        # Assert
        self.assertEqual(reading, [])

    # GREEN_ON_BASE(construction): the scan is this file's own, so a base run takes the branch's copy of
    # it. What shows the case can fail is putting back the dot-only `pull_request\s*\.\s*base` pattern:
    # measured, it then misses the index spelling.
    def test_Given_EachSpellingOfTheEventsBase_When_Scanned_Then_EveryOneIsFound(self):
        # Arrange
        spellings = [
            "${{ github.event.pull_request.base.sha }}",
            "${{ github.event['pull_request']['base']['sha'] }}",
            "${{ github.event.pull_request['base'].sha }}",
            "${{ github.event.Pull_Request.BASE.SHA }}",
        ]

        # Act
        missed = [spelling for spelling in spellings if not EVENT_BASE.search(spelling)]

        # Assert
        self.assertEqual(missed, [])

    # GREEN_ON_BASE(construction): `workflow_sources` is this file's own, so a base run takes the
    # branch's copy of it. What shows the case can fail is narrowing it back to `action.yml`: measured,
    # the listing then leaves the action out.
    def test_Given_ActionsAndWorkflowsUnderEitherExtension_When_Listed_Then_EachIsRead(self):
        # Arrange
        root = Path(tempfile.mkdtemp(prefix="velvet-change-base-sources-"))
        self.addCleanup(shutil.rmtree, root, True)
        expected = [root / ".github" / "actions" / "probe" / "action.yaml",
                    root / ".github" / "workflows" / "probe.yaml"]
        for path in expected:
            path.parent.mkdir(parents=True)
            path.write_text("name: probe\n", encoding="utf-8")

        # Act
        listed = workflow_sources(root)

        # Assert
        self.assertEqual(listed, expected)

    def test_Given_EveryStepPassingABase_When_Read_Then_ItTakesTheResolverOutputFromAnEarlierStep(self):
        # Arrange
        sources = repository_sources()

        # Act
        wrong, reading = wiring(sources)

        # Assert — more than one job, so a reader that ran every job together into one reads as broken
        # rather than as clean.
        self.assertEqual((wrong, len(reading) > 1), ([], True))

    # GREEN_ON_BASE(construction): `units` is this file's own, so a base run takes the branch's copy of
    # it. What shows the case can fail is finding the jobs by `text.partition("\njobs:\n")` again:
    # measured, every job is then read as one and nothing is reported.
    def test_Given_AResolverInAnotherJobUnderACommentedJobsLine_When_Read_Then_TheReadingStepIsReported(self):
        # Arrange
        workflow = (
            "on: pull_request\n"
            "jobs: # the jobs\n"
            "  one:\n"
            "    steps:\n"
            "      - id: base\n"
            "        run: " + RESOLVER_RUN + "\n"
            "  two:\n"
            "    steps:\n"
            "      - env:\n"
            "          BASE: ${{ steps.base.outputs.sha }}\n"
            "        run: python3 probe.py --base \"$BASE\"\n")

        # Act
        wrong, _ = wiring([("probe.yml", workflow, False)])

        # Assert
        self.assertEqual([entry.partition(":")[0] for entry in wrong], ["probe.yml two step 0"])


if __name__ == "__main__":
    unittest.main(verbosity=2)
