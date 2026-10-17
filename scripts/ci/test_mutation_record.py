#!/usr/bin/env python3
"""Unit tests for mutation_record.py: the record branch written through a stand-in for GitHub's Git
Data API and read back with git, and the commits a read trusts.

The stand-in keeps its objects in a bare repository, which is the origin every reader clones, and
answers each commit's verification as `signed` says: GitHub's own signing is not reproduced here.

Run: python3 scripts/ci/test_mutation_record.py
"""

import importlib.util
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("mutation_record",
                                                  Path(__file__).with_name("mutation_record.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


record = load_module()

SLUG = "owner/name"
# What the commits API reports for a commit the workflow's token created: the bot as author, web-flow as
# committer. `login` is the committer's.
BOT = {"verified": True, "reason": "valid", "login": "web-flow", "author": "github-actions[bot]"}


class FakeGitHub:
    """The Git Data API calls `publish` makes and the commit read `foreign_commits` makes, over a bare
    repository. `signed` maps a commit to {verified, reason, login, author}; one it does not name reads as
    `BOT`. `before_update`, where set, runs once before the next ref update, as another run would."""

    def __init__(self, bare):
        self.bare, self.signed, self.calls, self.before_update = bare, {}, [], None

    def git(self, *arguments, env=None, stdin=None):
        done = subprocess.run(["git", *arguments], cwd=self.bare, capture_output=True, text=True,
                              env=dict(os.environ, **(env or {})), input=stdin)
        return done

    def __call__(self, method, path, body=None):
        self.calls.append((method, path))
        rest = path.split("repos/{}/".format(SLUG), 1)[1]
        if method == "GET" and rest.startswith("git/ref/heads/"):
            done = self.git("rev-parse", "--verify", "--quiet", "refs/heads/" + rest[len("git/ref/heads/"):])
            if done.returncode != 0:
                raise record.ApiError(404, "Not Found")
            return {"object": {"sha": done.stdout.strip()}}
        if method == "GET" and rest.startswith("git/commits/"):
            return {"tree": {"sha": self.git("rev-parse", rest[len("git/commits/"):] + "^{tree}").stdout.strip()}}
        if method == "POST" and rest == "git/trees":
            index = Path(tempfile.mkdtemp()) / "index"
            env = {"GIT_INDEX_FILE": str(index)}
            self.git("read-tree", *([body["base_tree"]] if "base_tree" in body else ["--empty"]), env=env)
            for item in body["tree"]:
                blob = self.git("hash-object", "-w", "--stdin", stdin=item["content"]).stdout.strip()
                self.git("update-index", "--add", "--cacheinfo", "100644,{},{}".format(blob, item["path"]), env=env)
            return {"sha": self.git("write-tree", env=env).stdout.strip()}
        if method == "POST" and rest == "git/commits":
            if "author" in body or "committer" in body or "signature" in body:
                raise AssertionError("a commit carrying its own identity is not one GitHub signs as the bot")
            parents = sum((["-p", parent] for parent in body["parents"]), [])
            env = {"GIT_AUTHOR_NAME": "bot", "GIT_AUTHOR_EMAIL": "b@b", "GIT_COMMITTER_NAME": "bot",
                   "GIT_COMMITTER_EMAIL": "b@b"}
            return {"sha": self.git("commit-tree", body["tree"], *parents, "-m", body["message"], env=env).stdout.strip()}
        if method in ("PATCH", "POST") and "git/refs" in rest:
            if self.before_update:
                hook, self.before_update = self.before_update, None
                hook()
            name = "refs/heads/" + record.RECORD_BRANCH
            old = self.git("rev-parse", "--verify", "--quiet", name).stdout.strip()
            if method == "PATCH" and body.get("force") is not False:
                raise AssertionError("the record is only ever moved forward")
            if old and self.git("merge-base", "--is-ancestor", old, body["sha"]).returncode != 0:
                raise record.ApiError(422, "Update is not a fast forward")
            if method == "POST" and old:
                raise record.ApiError(422, "Reference already exists")
            self.git("update-ref", name, body["sha"])
            return {}
        if method == "GET" and rest.startswith("commits/"):
            held = self.signed.get(rest[len("commits/"):], BOT)
            return {"commit": {"verification": {"verified": held["verified"], "reason": held["reason"]}},
                    "author": {"login": held["author"]}, "committer": {"login": held["login"]}}
        raise AssertionError("unexpected call {} {}".format(method, path))


class Origin:
    """A bare origin with a stand-in API over it, and clones standing for the jobs that read it."""

    def __init__(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-record-"))
        self.bare = self.root / "origin.git"
        subprocess.run(["git", "init", "-q", "--bare", "-b", "main", str(self.bare)], check=True)
        seed = self.clone("seed")
        (seed / "README.md").write_text("probe\n")
        self.git(seed, "add", "README.md")
        self.git(seed, "commit", "-qm", "seed")
        self.git(seed, "push", "-q", "origin", "main")
        self.api = FakeGitHub(self.bare)

    @staticmethod
    def git(cwd, *arguments):
        return subprocess.run(["git", "-c", "user.email=p@e", "-c", "user.name=P", *arguments], cwd=cwd,
                              check=True, capture_output=True, text=True).stdout

    def clone(self, name):
        path = self.root / name
        subprocess.run(["git", "clone", "-q", str(self.bare), str(path)], check=True, capture_output=True)
        return path

    def entries(self, name, **contents):
        directory = self.root / name
        directory.mkdir()
        for key, text in contents.items():
            (directory / "{}.json".format(key)).write_text(text)
        return directory

    def publish(self, name, **contents):
        return record.publish(self.entries(name, **contents), name, SLUG, self.api)

    def head(self):
        return subprocess.run(["git", "rev-parse", "refs/heads/" + record.RECORD_BRANCH], cwd=self.bare,
                              capture_output=True, text=True).stdout.strip()

    def remove(self):
        shutil.rmtree(self.root, ignore_errors=True)


class RecordTests(unittest.TestCase):
    def setUp(self):
        self.origin = Origin()
        self.addCleanup(self.origin.remove)

    def fetched(self, name):
        directory = self.origin.root / ("fetched-" + name)
        record.fetch(self.origin.clone(name), directory, SLUG, self.origin.api)
        return {path.name: path.read_text() for path in sorted(directory.glob("*.json"))}

    def test_Given_NoRecordBranch_When_Fetched_Then_TheDirectoryIsEmpty(self):
        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(found, {})

    def test_Given_PublishedEntries_When_Fetched_Then_EachComesBackUnderItsKey(self):
        # Arrange
        self.origin.publish("one", a='{"v": 1}')

        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(found, {"a.json": '{"v": 1}'})

    def test_Given_TwoPublishes_When_Fetched_Then_BothRunsEntriesAreHeld(self):
        # Arrange
        self.origin.publish("one", a='{"v": 1}')
        self.origin.publish("two", b='{"v": 2}')

        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(sorted(found), ["a.json", "b.json"])

    def test_Given_AnotherRunMovingTheBranchFirst_When_Published_Then_ItRetriesOverItAndBothAreHeld(self):
        # Arrange — the other run's update lands between this one's read of the branch and its own.
        self.origin.publish("one", a='{"v": 1}')
        self.origin.api.before_update = lambda: self.origin.publish("two", b='{"v": 2}')
        self.origin.publish("three", c='{"v": 3}')

        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(sorted(found), ["a.json", "b.json", "c.json"])

    def test_Given_AnEntryPublishedAgain_When_Fetched_Then_TheNewerVerdictReplacesIt(self):
        # Arrange
        self.origin.publish("one", a='{"v": 1}')
        self.origin.publish("two", a='{"v": 2}')

        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(found, {"a.json": '{"v": 2}'})

    def test_Given_TheRecordBranch_When_Read_Then_ItSharesNoHistoryWithMain(self):
        # Arrange — the record is never merged anywhere, so it carries none of the code's history.
        self.origin.publish("one", a='{"v": 1}')

        # Act
        shared = subprocess.run(["git", "merge-base", "main", record.RECORD_BRANCH], cwd=self.origin.bare,
                                capture_output=True, text=True).stdout.strip()

        # Assert
        self.assertEqual(shared, "")


class TrustTests(unittest.TestCase):
    """Which commits on the branch a read trusts."""

    def setUp(self):
        self.origin = Origin()
        self.addCleanup(self.origin.remove)
        self.origin.publish("one", a='{"v": 1}')

    def read(self):
        """(the entries a fresh reader fetched, or the refusal it gave)."""
        self.reads = getattr(self, "reads", 0) + 1
        directory = self.origin.root / "fetched-{}".format(self.reads)
        try:
            record.fetch(self.origin.clone("reader-{}".format(self.reads)), directory, SLUG, self.origin.api)
        except SystemExit as refusal:
            return str(refusal)
        return sorted(path.name for path in directory.glob("*.json"))

    def test_Given_ACommitAuthoredByTheBotAndCommittedByWebFlow_When_Read_Then_ItsEntriesAreTrusted(self):
        # Act
        found = self.read()

        # Assert
        self.assertEqual(found, ["a.json"])

    def test_Given_AnUnverifiedCommit_When_Read_Then_TheRecordIsRefusedNamingIt(self):
        # Arrange — a push by anyone with write access, which GitHub does not sign.
        self.origin.publish("two", b='{"v": 2}')
        forged = self.origin.head()
        self.origin.api.signed[forged] = dict(BOT, verified=False, reason="unsigned")

        # Act
        found = self.read()

        # Assert
        self.assertIn("{}: GitHub does not report it signed".format(forged), found)

    def test_Given_AVerifiedFlagWithAReasonOtherThanValid_When_Read_Then_TheRecordIsRefusedNamingIt(self):
        # Arrange — both fields are read, so a payload in which they disagree is not taken as signed.
        self.origin.publish("two", b='{"v": 2}')
        odd = self.origin.head()
        self.origin.api.signed[odd] = dict(BOT, reason="unknown_key")

        # Act
        found = self.read()

        # Assert
        self.assertIn("{}: GitHub does not report it signed".format(odd), found)

    def test_Given_AVerifiedCommitByAPerson_When_Read_Then_TheRecordIsRefusedNamingIt(self):
        # Arrange — a person's own signed commit is verified too, and is still not the writer's.
        self.origin.publish("two", b='{"v": 2}')
        human = self.origin.head()
        self.origin.api.signed[human] = dict(BOT, login="someone")

        # Act
        found = self.read()

        # Assert
        self.assertIn("{}: committed by someone".format(human), found)

    def test_Given_AVerifiedCommitAuthoredByAPerson_When_Read_Then_TheRecordIsRefusedNamingIt(self):
        # Arrange — web-flow signs a person's commit made in the web interface just the same.
        self.origin.publish("two", b='{"v": 2}')
        human = self.origin.head()
        self.origin.api.signed[human] = dict(BOT, author="someone")

        # Act
        found = self.read()

        # Assert
        self.assertIn("{}: authored by someone".format(human), found)

    def test_Given_AVerifiedCommitCommittedByTheBot_When_Read_Then_TheRecordIsRefusedNamingIt(self):
        # Arrange — the bot as committer is what a commit pushed by the workflow's git identity carries.
        self.origin.publish("two", b='{"v": 2}')
        pushed = self.origin.head()
        self.origin.api.signed[pushed] = dict(BOT, login="github-actions[bot]")

        # Act
        found = self.read()

        # Assert
        self.assertIn("{}: committed by github-actions[bot]".format(pushed), found)

    def test_Given_AForeignCommitUnderABotOne_When_Read_Then_TheRecordIsRefused(self):
        # Arrange — a later commit by the writer does not launder what came before it.
        forged = self.origin.head()
        self.origin.api.signed[forged] = dict(BOT, verified=False, reason="unsigned")
        self.origin.publish("two", b='{"v": 2}')

        # Act
        found = self.read()

        # Assert
        self.assertIn(forged, found)

    def test_Given_TheSameBranch_When_ReadTwice_Then_TheAnswersAreEqual(self):
        # Arrange
        self.origin.api.signed[self.origin.head()] = dict(BOT, login="someone")

        def refusal():
            """The refusal a read gave, or a value equal to nothing where it gave none."""
            answer = self.read()
            return answer if isinstance(answer, str) else object()

        # Act
        first, second = refusal(), refusal()

        # Assert
        self.assertEqual(first, second)

    def test_Given_ABranchOfSeveralCommits_When_Read_Then_EachIsAskedAboutOnce(self):
        # Arrange
        self.origin.publish("two", b='{"v": 2}')
        self.origin.api.calls.clear()

        # Act
        self.read()

        # Assert
        self.assertEqual(sum(1 for method, path in self.origin.api.calls if "/commits/" in path
                             and "/git/" not in path), 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)
