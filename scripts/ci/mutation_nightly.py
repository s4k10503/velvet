#!/usr/bin/env python3
"""Choose what a nightly mutation campaign measures on the default branch, and report what it found.

`.github/workflows/mutation.yml` runs this on its scheduled runs alone. CONTRIBUTING.md ▸ Checking
that the tests can fail owns what a night measures and what its report means; this holds the two
readings that decide them.

    python3 scripts/ci/mutation_nightly.py window --output window.json [--reuse-from DIR]
    python3 scripts/ci/mutation_nightly.py report --window window.json [--verdict verdict.txt]

`window` reads where the last reported night ended and the first-parent commits since, and writes the
newest slice of them whose mutants fit the campaign's ceiling: `base` in the file it writes is what
`change_base.py` hands every job of the run that reads a base. The mutants of the commits before
that slice are written beside it as unmeasured, so the report names them rather than the night
passing over them.

`report` reads that file, the verdict job's output and every job's result, and comments on the
newest issue labelled `ISSUE_LABEL`, opening one where there is none and reopening a closed one,
where the night left anything to answer: a survivor nothing answers for, a stale declaration, a
mutant nothing measured, or a job that did not pass. A night with nothing to answer, whose window was
read, writes nothing. It prints `head=` for $GITHUB_OUTPUT where the window was read, which the
workflow uploads `REPORTED_ARTIFACT` under once the report is written; the next night's window starts
there.
"""

import argparse
import datetime
import json
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "scripts" / "test_quality"))
import mutation_check  # noqa: E402

WORKFLOW = "mutation.yml"

ISSUE_LABEL = "mutation-nightly"
ISSUE_TITLE = "Nightly mutation campaign: unanswered survivors on main"

# The artifact a night that reported uploads, naming the head it measured up to.
REPORTED_ARTIFACT = "Mutation nightly reported {}"
REPORTED = re.compile(r"Mutation nightly reported ([0-9a-f]{40})")

# How many earlier scheduled runs are asked for the head the last report ended at.
CANDIDATES = 20

# Where no reported night is found, the window opens at the newest first-parent commit at least this old.
FALLBACK_HOURS = 24

# What one list in a report shows before it says how many more there are, and what a whole comment is
# cut to: GitHub refuses a comment over 65536 characters.
LIST_LIMIT = 200
COMMENT_LIMIT = 60000

PASSING = frozenset({"success", "skipped"})


def git(*arguments, project=REPO_ROOT):
    done = subprocess.run(["git", "-C", str(project), *arguments], capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError("git {} failed: {}".format(" ".join(arguments), done.stderr.strip()))
    return done.stdout.strip()


def gh_json(path, method="GET", fields=None):
    command = ["gh", "api", "--method", method, path]
    for name, value in (fields or {}).items():
        command += ["-f", "{}={}".format(name, value)]
    done = subprocess.run(command, capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError("gh api {} {} failed: {}".format(method, path, done.stderr.strip()))
    return json.loads(done.stdout) if done.stdout.strip() else None


def reported_head(payload):
    """The head a run's artifacts listing says it reported up to, or None.

    None where the listing is not a whole one, or holds no live marker, or markers of two heads.
    """
    listed = payload.get("artifacts") if isinstance(payload, dict) else None
    if not isinstance(listed, list) or payload.get("total_count", len(listed)) > len(listed):
        return None
    heads = {match.group(1) for match in (REPORTED.fullmatch(item.get("name") or "") for item in listed
                                          if isinstance(item, dict) and not item.get("expired"))
             if match}
    return heads.pop() if len(heads) == 1 else None


def previous_head(slug, branch, current, read, is_ancestor):
    """(sha, run id) of the newest earlier scheduled run that reported, or None.

    A reported head the checkout does not contain, as after a force-push, is passed over.
    """
    listing = read("repos/{}/actions/workflows/{}/runs?event=schedule&branch={}&status=completed"
                   "&per_page={}".format(slug, WORKFLOW, branch, CANDIDATES))
    runs = [run for run in (listing or {}).get("workflow_runs") or []
            if str(run.get("id")) != str(current)]
    for run in sorted(runs, key=lambda run: run.get("run_number") or 0, reverse=True):
        head = reported_head(read("repos/{}/actions/runs/{}/artifacts?per_page=100".format(
            slug, run.get("id"))))
        if head and is_ancestor(head):
            return head, run.get("id")
    return None


def fallback_start(project, now):
    """The newest first-parent commit at least `FALLBACK_HOURS` old, or the first one there is."""
    before = (now - datetime.timedelta(hours=FALLBACK_HOURS)).strftime("%Y-%m-%dT%H:%M:%SZ")
    found = git("rev-list", "-1", "--first-parent", "--before=" + before, "HEAD", project=project)
    return found or git("rev-list", "--first-parent", "HEAD", project=project).splitlines()[-1]


def plan(project, base, reuse_from):
    """(whether the plan fits the ceiling, the mutant records it counted) for `base`..HEAD.

    Raises for a plan that refused for any reason but the ceiling: the run cannot measure that
    window, and a window chosen past the refusal would hide it.
    """
    with tempfile.TemporaryDirectory() as scratch:
        listed = Path(scratch) / "mutants.json"
        command = [sys.executable, str(REPO_ROOT / "scripts/test_quality/mutation_check.py"),
                   "--project", str(project), "--base", base, "--plan", "--emit-mutants", str(listed)]
        if reuse_from:
            command += ["--reuse-from", str(reuse_from)]
        done = subprocess.run(command, capture_output=True, text=True, cwd=str(project))
        if done.returncode not in (0, mutation_check.CEILING_REFUSAL):
            raise RuntimeError("the plan against {} refused: {}".format(
                base[:12], (done.stdout + done.stderr).strip()[-2000:]))
        mutants = json.loads(listed.read_text()) if listed.exists() else []
    return done.returncode == 0, mutants


def identity(record):
    return json.dumps({key: record.get(key) for key in ("path", "line", "column", "operator",
                                                        "before", "after")}, sort_keys=True)


def choose(start, commits, ask):
    """(the base measured, the commits left unmeasured, the window's mutants, the slice's).

    `commits` are the first-parent commits after `start`, oldest first, and `ask` is `plan`. The
    oldest base whose plan fits is taken; where none fits, the head itself, which measures nothing.
    """
    candidates = [start] + commits[:-1]
    window = None
    for index, candidate in enumerate(candidates):
        fits, mutants = ask(candidate)
        if window is None:
            window = mutants
        if fits:
            return candidate, commits[:index], window, mutants
    return (commits[-1] if commits else start), list(commits), window or [], []


def window_main(args, environ, read=gh_json, now=None):
    project = Path(args.project).resolve()
    head = git("rev-parse", "HEAD", project=project)
    slug, branch = environ.get("GITHUB_REPOSITORY", ""), environ.get("GITHUB_REF_NAME", "")

    def is_ancestor(sha):
        return subprocess.run(["git", "-C", str(project), "merge-base", "--is-ancestor", sha, "HEAD"],
                              capture_output=True).returncode == 0

    try:
        found = previous_head(slug, branch, environ.get("GITHUB_RUN_ID", ""), read, is_ancestor)
    except (RuntimeError, ValueError) as failure:
        print("no reported night was read: {}".format(failure), file=sys.stderr)
        found = None
    if found:
        start, source = found[0], "the night reported by run {}".format(found[1])
    else:
        start = fallback_start(project, now or datetime.datetime.now(datetime.timezone.utc))
        source = "no reported night was found, so the last {} hours".format(FALLBACK_HOURS)
    listed = git("rev-list", "--first-parent", "--reverse", "{}..HEAD".format(start), project=project)
    commits = listed.split()
    base, unmeasured, window, measured = choose(
        start, commits, lambda candidate: plan(project, candidate, args.reuse_from))
    kept = {identity(record) for record in measured}
    record = {
        "head": head, "start": start, "source": source, "base": base,
        "unmeasured_commits": [{"sha": sha, "subject": git("log", "-1", "--format=%s", sha, project=project)}
                               for sha in unmeasured],
        "unmeasured_mutants": [mutant for mutant in window if identity(mutant) not in kept],
        "measured_mutants": measured,
    }
    Path(args.output).write_text(json.dumps(record, indent=1))
    print("window {}..{} ({}): measuring {}..{}, {} mutant(s); {} commit(s) and {} mutant(s) "
          "left unmeasured".format(start[:12], head[:12], source, base[:12], head[:12], len(measured),
                                   len(unmeasured), len(record["unmeasured_mutants"])))
    return 0


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


def described(mutant):
    return "{}:{} {} -> {} ({})".format(mutant.get("path"), mutant.get("line"), mutant.get("before"),
                                       mutant.get("after"), mutant.get("operator"))


def compose(window, verdict, results, reused, run_url):
    """The comment a night owes its issue, or None where it left nothing to answer."""
    failed = ["{} concluded {}".format(job, result) for job, result in results if result not in PASSING]
    unanswered, stale, unmeasured = sections(verdict) if verdict is not None else ([], [], [])
    body = []
    if window is None:
        body += ["The window could not be read, so nothing was measured; the next night measures "
                 "this one's commits."]
    else:
        body += ["Window `{}`..`{}` ({}); measured `{}`..`{}`.".format(
            window["start"][:12], window["head"][:12], window["source"], window["base"][:12],
            window["head"][:12])]
        if verdict is None and not reused and window["measured_mutants"]:
            unmeasured = ["no verdict was reached, so none of the {} mutant(s) of the measured slice "
                          "has one".format(len(window["measured_mutants"]))] + [
                described(mutant) for mutant in window["measured_mutants"]]
    sectioned = [("Unanswered survivors", unanswered), ("Stale declarations", stale),
                 ("Not measured", unmeasured), ("Jobs that did not pass", failed)]
    if window is not None and window["unmeasured_commits"]:
        sectioned.append(("Commits outside the measured slice, over the ceiling",
                          ["{} {}".format(commit["sha"][:12], commit["subject"])
                           for commit in window["unmeasured_commits"]]))
        sectioned.append(("Their mutants, unmeasured",
                          [described(mutant) for mutant in window["unmeasured_mutants"]]))
    if not any(lines for _, lines in sectioned) and window is not None:
        return None
    for heading, lines in sectioned:
        if lines:
            body += ["", "### {}".format(heading)] + listed(lines)
    body += ["", "Run: {}".format(run_url)]
    text = "\n".join(["## Nightly mutation campaign", ""] + body)
    return text if len(text) <= COMMENT_LIMIT else text[:COMMENT_LIMIT] + "\n\n(cut; the run holds the rest)"


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
            pass  # Refused where the label exists already, which is every night after the first.
        issue = write("repos/{}/issues".format(slug), "POST", {
            "title": ISSUE_TITLE, "labels[]": ISSUE_LABEL,
            "body": "Each scheduled mutation campaign on the default branch comments here with what it "
                    "left to answer. CONTRIBUTING.md ▸ Checking that the tests can fail says how a "
                    "survivor is answered. Close this once every comment is answered; the next night "
                    "with anything to answer reopens it."})
    elif issue.get("state") != "open":
        write("repos/{}/issues/{}".format(slug, issue["number"]), "PATCH", {"state": "open"})
    write("repos/{}/issues/{}/comments".format(slug, issue["number"]), "POST", {"body": text})
    return issue["number"]


def results_from(text):
    """[(job, result)] from `job=result` words."""
    return [tuple(word.split("=", 1)) for word in text.split() if "=" in word]


def report_main(args, environ, read=gh_json, write=gh_json):
    window_path, verdict_path = Path(args.window), Path(args.verdict) if args.verdict else None
    window = json.loads(window_path.read_text()) if window_path.is_file() else None
    verdict = verdict_path.read_text() if verdict_path and verdict_path.is_file() else None
    slug = environ.get("GITHUB_REPOSITORY", "")
    run_url = "{}/{}/actions/runs/{}".format(environ.get("GITHUB_SERVER_URL", "https://github.com"),
                                             slug, environ.get("GITHUB_RUN_ID", ""))
    text = compose(window, verdict, results_from(environ.get("RESULTS", "")),
                   environ.get("REUSED", ""), run_url)
    if text is None:
        print("nothing to answer: no comment is written")
    else:
        print(text)
        print("commented on issue #{}".format(post(slug, text, read, write)), file=sys.stderr)
    if window is not None:
        print("head={}".format(window["head"]))
    return 0


def main(argv=None, environ=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=str(REPO_ROOT))
    commands = parser.add_subparsers(dest="command", required=True)
    window = commands.add_parser("window")
    window.add_argument("--output", required=True)
    window.add_argument("--reuse-from")
    report = commands.add_parser("report")
    report.add_argument("--window", required=True)
    report.add_argument("--verdict")
    args = parser.parse_args(argv)
    environ = os.environ if environ is None else environ
    if args.command == "window":
        return window_main(args, environ)
    return report_main(args, environ)


if __name__ == "__main__":
    sys.exit(main())
