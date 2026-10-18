#!/usr/bin/env python3
"""Report what a record run of the mutation campaign left to answer, on one tracking issue.

`.github/workflows/mutation.yml` runs this after every run that writes the record (`mutation_record.py`):
a push to the default branch, the nightly schedule, a catch-up run and a dispatch. CONTRIBUTING.md ▸
Checking that the tests can fail owns what those runs measure; this reads the verdict job's output and
every job's result, and comments on the newest issue labelled `ISSUE_LABEL`, opening one where there is
none and reopening a closed one, where the run left anything to answer: a survivor nothing answers for,
a stale declaration, a mutant nothing measured, or a job that did not pass. Mutants past the run's cap
owe nothing: a later run measures them, and the record says which.

    python3 scripts/ci/mutation_report.py [--verdict verdict.txt]

The issue is where people read the findings, and no decision reads it: the release gate reads the
record and git alone.
"""

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

ISSUE_LABEL = "mutation-nightly"
ISSUE_TITLE = "Mutation campaign: unanswered survivors on main"

# What one list in a report shows before it says how many more there are, and what a whole comment is
# cut to: GitHub refuses a comment over 65536 characters.
LIST_LIMIT = 200
COMMENT_LIMIT = 60000

PASSING = frozenset({"success", "skipped"})


def gh_json(path, method="GET", fields=None):
    command = ["gh", "api", "--method", method, path]
    for name, value in (fields or {}).items():
        command += ["-f", "{}={}".format(name, value)]
    done = subprocess.run(command, capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError("gh api {} {} failed: {}".format(method, path, done.stderr.strip()))
    return json.loads(done.stdout) if done.stdout.strip() else None


def sections(text):
    """(unanswered, stale, unmeasured) lines out of a verdict job's output."""
    lines = text.splitlines()
    unanswered, stale, unmeasured = [], [], []
    heading = None
    for index, line in enumerate(lines):
        if line.startswith("UNANSWERED  "):
            following = lines[index + 1].strip() if index + 1 < len(lines) else ""
            unanswered.append("{} — {}".format(line[len("UNANSWERED  "):].strip(), following))
        elif line.startswith("STALE "):
            stale.append(line[len("STALE "):].strip())
        elif line.startswith("--- mutants nothing was asked of the suite about ---") or (
                line.startswith("--- not measured:")):
            heading = line
            unmeasured.append(line.strip("- "))
        elif heading is not None and line.strip():
            unmeasured.append(line.strip())
        elif heading is not None:
            heading = None
        if "further mutant(s) were never run" in line:
            unmeasured.append(line.strip())
    return unanswered, stale, unmeasured


def listed(lines):
    shown = ["- `{}`".format(line.replace("`", "'")) for line in lines[:LIST_LIMIT]]
    if len(lines) > LIST_LIMIT:
        shown.append("- and {} more".format(len(lines) - LIST_LIMIT))
    return shown


def compose(verdict, results, pending, run_url):
    """The comment a run owes the issue, or None where it left nothing to answer."""
    failed = ["{} concluded {}".format(job, result) for job, result in results if result not in PASSING]
    unanswered, stale, unmeasured = sections(verdict) if verdict is not None else ([], [], [])
    owing = [("Unanswered survivors", unanswered), ("Stale declarations", stale),
             ("Not measured", unmeasured), ("Jobs that did not pass", failed)]
    if not any(lines for _, lines in owing):
        return None
    body = ["## Mutation campaign", ""]
    if pending:
        body += ["{} mutant(s) past this run's cap are left to the next run.".format(pending)]
    for heading, lines in owing:
        if lines:
            body += ["", "### {}".format(heading)] + listed(lines)
    body += ["", "Run: {}".format(run_url)]
    text = "\n".join(body)
    if len(text) > COMMENT_LIMIT:
        text = text[:COMMENT_LIMIT] + "\n\n(cut; the run holds the rest)"
    return text


def tracking_issue(slug, read):
    """The newest issue carrying `ISSUE_LABEL`, open or closed, or None."""
    found = read("repos/{}/issues?labels={}&state=all&sort=created&direction=desc&per_page=10".format(
        slug, ISSUE_LABEL)) or []
    issues = [issue for issue in found if isinstance(issue, dict) and "pull_request" not in issue]
    return issues[0] if issues else None


def post(slug, text, read=gh_json, write=gh_json):
    """Comments `text` on the tracking issue, opening or reopening it first. Returns its number."""
    issue = tracking_issue(slug, read)
    if issue is None:
        try:
            write("repos/{}/labels".format(slug), "POST", {"name": ISSUE_LABEL, "color": "b60205"})
        except RuntimeError:
            pass  # Refused where the label exists already, which is every run after the first.
        issue = write("repos/{}/issues".format(slug), "POST", {
            "title": ISSUE_TITLE, "labels[]": ISSUE_LABEL,
            "body": "Each mutation campaign run that writes the record comments here with what it "
                    "left to answer. CONTRIBUTING.md ▸ Checking that the tests can fail says how a "
                    "survivor is answered. Close this once every comment is answered; the next run "
                    "with anything to answer reopens it."})
    elif issue.get("state") != "open":
        write("repos/{}/issues/{}".format(slug, issue["number"]), "PATCH", {"state": "open"})
    write("repos/{}/issues/{}/comments".format(slug, issue["number"]), "POST", {"body": text})
    return issue["number"]


def results_from(text):
    """[(job, result)] from `job=result` words."""
    return [tuple(word.split("=", 1)) for word in text.split() if "=" in word]


def main(argv=None, environ=None, read=gh_json, write=gh_json):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--verdict")
    args = parser.parse_args(argv)
    environ = os.environ if environ is None else environ
    verdict_path = Path(args.verdict) if args.verdict else None
    verdict = verdict_path.read_text() if verdict_path and verdict_path.is_file() else None
    slug = environ.get("GITHUB_REPOSITORY", "")
    run_url = "{}/{}/actions/runs/{}".format(environ.get("GITHUB_SERVER_URL", "https://github.com"),
                                             slug, environ.get("GITHUB_RUN_ID", ""))
    text = compose(verdict, results_from(environ.get("RESULTS", "")), int(environ.get("PENDING") or 0),
                   run_url)
    if text is None:
        print("nothing to answer: no comment is written")
    else:
        print(text)
        print("commented on issue #{}".format(post(slug, text, read, write)), file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
