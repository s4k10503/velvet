#!/usr/bin/env python3
"""Unit tests for the pull-request reading two Stop guards share, and for what it says on failing.

A guard that could not read had one way of asking and one thing to say, and what it said was a claim
about the pull requests rather than about itself. Both halves are held here: that a second way is
asked before blindness is declared, and that the report names the guard as what failed and asks the
deferral about the work.

Run: python3 scripts/hooks/test_hook_repository.py
"""

import contextlib
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[2]
SETTINGS = REPO_ROOT / ".claude/settings.json"
STOP_GUARDS = ".claude/hooks/stop/"

FAILS = 'echo "HTTP 403: API rate limit exceeded" >&2; exit 1'
ANSWERS = "echo 612; echo 377"
ANSWERS_NOTHING = "exit 0"


def load_module():
    """Imports .claude/hooks/lib/repository.py by path, since .claude holds no packages."""
    path = REPO_ROOT / ".claude/hooks/lib/repository.py"
    spec = importlib.util.spec_from_file_location("hook_repository", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


repository = load_module()


@contextlib.contextmanager
def gh_answering(pull_request_list, api):
    """A `gh` on PATH whose two subcommands answer as told, yielding a log of what was asked."""
    with tempfile.TemporaryDirectory(prefix="hook-repository-") as directory:
        root = Path(directory)
        asked = root / "asked"
        asked.write_text("")
        stub = root / "gh"
        stub.write_text("#!/bin/sh\n"
                        'printf "%s\\n" "$*" >> "$VELVET_GH_ASKED"\n'
                        'case "$1" in\n'
                        f"  pr) {pull_request_list} ;;\n"
                        f"  api) {api} ;;\n"
                        "esac\n")
        stub.chmod(0o755)
        with mock.patch.dict(os.environ, {"PATH": f"{root}{os.pathsep}{os.environ['PATH']}",
                                          "VELVET_GH_ASKED": str(asked)}):
            yield asked


class OpenPullRequestTests(unittest.TestCase):
    def test_Given_TheFirstWayFailing_When_TheSecondAnswers_Then_TheNumbersAreReturned(self):
        # Arrange — a second way of asking is worth having only if it answers when the first cannot.
        with gh_answering(FAILS, ANSWERS):
            # Act / Assert
            self.assertEqual(repository.open_pull_requests().numbers, ["612", "377"])

    def test_Given_EveryWayFailing_When_TheReadingIsTaken_Then_ThereIsNoAnswerAtAll(self):
        # Arrange — None rather than an empty list, which is what an answered read can also be.
        with gh_answering(FAILS, FAILS):
            # Act / Assert
            self.assertIsNone(repository.open_pull_requests().numbers)

    def test_Given_EveryWayFailing_When_TheReadingIsTaken_Then_EachAttemptIsReported(self):
        # Arrange — a guard that declares blindness has to be able to show what it asked.
        with gh_answering(FAILS, FAILS):
            # Act
            attempts = repository.open_pull_requests().attempts

            # Assert
            self.assertEqual(len(attempts), len(repository.OPEN_PULL_REQUEST_READS))

    def test_Given_EveryWayFailing_When_TheReadingIsTaken_Then_MoreThanOneWasTried(self):
        # Arrange — asked against a number rather than against the table above, which would shrink
        # with it and agree with itself all the way down to one way of asking.
        with gh_answering(FAILS, FAILS):
            # Act
            attempts = repository.open_pull_requests().attempts

            # Assert
            self.assertGreater(len(attempts), 1)

    def test_Given_TheFirstWayAnswering_When_TheReadingIsTaken_Then_TheSecondIsNotAsked(self):
        # Arrange
        with gh_answering(ANSWERS, FAILS) as asked:
            # Act
            repository.open_pull_requests()

            # Assert
            self.assertEqual(asked.read_text().splitlines(),
                             [" ".join(repository.OPEN_PULL_REQUEST_READS[0])])

    def test_Given_AnAnswerNamingNoPullRequest_When_TheReadingIsTaken_Then_ItIsAnAnswer(self):
        # Arrange — no open pull request is the state open_backlog.py exists to act on, so an empty
        # answer must not read as the failure above.
        with gh_answering(ANSWERS_NOTHING, FAILS):
            # Act / Assert
            self.assertEqual(repository.open_pull_requests().numbers, [])

    def test_Given_TheWaysOfAsking_When_TheirCommandsAreCompared_Then_TheyAreNotOneQuotaTwice(self):
        # Arrange — `gh pr list` and `gh api` is the whole of the second way; two entries under one
        # subcommand would draw on one quota and read as two ways of asking.
        subcommands = {read[0] for read in repository.OPEN_PULL_REQUEST_READS}

        # Act / Assert
        self.assertGreater(len(subcommands), 1)


def stop_guard_budget():
    """The tightest timeout the settings register for a Stop guard reading the pull requests.

    Raises rather than defaulting when nothing matches: a budget nobody found would make the case
    below pass against a number this repository does not hold.
    """
    settings = json.loads(SETTINGS.read_text(encoding="utf-8"))
    return min(hook["timeout"]
               for entry in settings.get("hooks", {}).get("Stop", [])
               for hook in entry.get("hooks", [])
               if STOP_GUARDS in hook.get("command", ""))


class ReadingBudgetTests(unittest.TestCase):
    def test_Given_EveryWayOfAsking_When_TheirTimeoutsAreAdded_Then_TheyFitInTheGuardsOwn(self):
        # Arrange — the rule `repository.gh`'s own bound states: the calling guard's registered
        # timeout divided by the calls one invocation makes. This is what fails when a way of asking
        # is added and the division stops holding, and the registered number is scaled the way that
        # bound already reads it.
        spent = (len(repository.OPEN_PULL_REQUEST_READS)
                 * repository.OPEN_PULL_REQUEST_TIMEOUT * 1000)

        # Act / Assert
        self.assertLess(spent, stop_guard_budget())


class UnreadableReportTests(unittest.TestCase):
    ATTEMPTS = [("gh pr list", 1, "HTTP 403"), ("gh api repos/x/y/pulls", 1, "HTTP 403")]

    def test_Given_AReport_When_ItIsRead_Then_ItSaysTheGuardIsWhatCouldNotRead(self):
        # Act
        report = repository.unreadable_report("the open pull requests", self.ATTEMPTS, "pr-list")

        # Assert
        self.assertIn(repository.SELF_REPORT, report)

    def test_Given_AReport_When_ItIsRead_Then_EveryAttemptIsShown(self):
        # Arrange — a reader asked to establish it another way needs to know which ways were tried.
        report = repository.unreadable_report("the open pull requests", self.ATTEMPTS, "pr-list")

        # Act / Assert
        self.assertTrue(all(call in report for call, _, _ in self.ATTEMPTS), report)

    def test_Given_AReport_When_ItsDeferralLineIsRead_Then_ItAsksWhatTheWorkWaitsOn(self):
        # Arrange — a deferral naming the failed reading expires and is rewritten identically, so the
        # reason on the record ends up being one nothing was waiting on.
        report = repository.unreadable_report("the open pull requests", self.ATTEMPTS, "pr-list")

        # Act / Assert
        self.assertIn('echo "pr-list <what the work is waiting on>', report)


if __name__ == "__main__":
    unittest.main(verbosity=2)
