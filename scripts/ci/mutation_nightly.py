#!/usr/bin/env python3
"""Choose what a nightly mutation campaign measures on the default branch, and report what it found.

`.github/workflows/mutation.yml` runs this on its scheduled runs alone. CONTRIBUTING.md ▸ Checking
that the tests can fail owns what a night measures and what its report means; this holds the two
readings that decide them.

    python3 scripts/ci/mutation_nightly.py window --output window.json [--reuse-from DIR]
    python3 scripts/ci/mutation_nightly.py tree --source SHA
    python3 scripts/ci/mutation_nightly.py report --window window.json [--verdict verdict.txt]

`window` reads where the last reported night reached and the first-parent commits since, and writes
the oldest slice of them whose mutants fit `NIGHTLY_MAX_SHARDS` shards: `base` in the file it writes
is what `change_base.py` hands every job of the run that reads a base, and it prints `reached=`, the
slice's newest commit, for $GITHUB_OUTPUT. The commits after it are left for later nights, which
start where this one reached, so a backlog is measured in order.

`tree` puts the slice's tree in place in a job's checkout (`restore_tree`), before anything reads it.

`report` reads that file, the verdict job's output and every job's result, and comments on the
newest issue labelled `ISSUE_LABEL`, opening one where there is none and reopening a closed one,
where the night left anything to answer: a survivor nothing answers for, a stale declaration, a
mutant nothing measured, a commit skipped as over the ceiling on its own, or a job that did not
pass. A night with nothing to answer, whose window was read, writes no comment. The report lists the
commits left for later nights either way. It prints `reached=` where the window was read, which the
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

# The artifact a night that reported uploads, naming the commit its slice reached.
REPORTED_ARTIFACT = "Mutation nightly reported {}"
REPORTED = re.compile(r"Mutation nightly reported ([0-9a-f]{40})")

# How many earlier scheduled runs are asked for the head the last report ended at.
CANDIDATES = 20

# Where no reported night is found, the window opens at the newest first-parent commit at least this old.
FALLBACK_HOURS = 24

# How many shards a night may split its slice across; a dispatch takes `mutation_check.MAX_SHARDS`.
# CONTRIBUTING.md ▸ Checking that the tests can fail says how it is sized against mutation.yml's
# `max-parallel` and the shard job's limit, which `NightlyBudgetTests` holds it to.
NIGHTLY_MAX_SHARDS = 40

# The directories a slice's tree is taken from: what the editor compiles or imports. Everything else
# stays the head's, the campaign's own scripts among them.
TREE_ROOTS = ("Packages", "Assets", "ProjectSettings")

# What one list in a report shows before it says how many more there are, and what a whole comment is
# cut to: GitHub refuses a comment over 65536 characters.
LIST_LIMIT = 200
COMMENT_LIMIT = 60000

PASSING = frozenset({"success", "skipped"})


def git(*arguments, project=REPO_ROOT, skip_lfs=False):
    environ = dict(os.environ, GIT_LFS_SKIP_SMUDGE="1") if skip_lfs else None
    done = subprocess.run(["git", "-C", str(project), *arguments], capture_output=True, text=True,
                          env=environ)
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
    """The commit a run's artifacts listing says its slice reached, or None.

    None where the listing is not a whole one, or holds no live marker, or markers of two commits.
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

    A reached commit the checkout does not contain, as after a force-push, is passed over.
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


def restore_tree(project, source, skip_lfs=False):
    """Puts `TREE_ROOTS` in `project`'s working tree and index back to `source`'s, leaving the rest
    at the checkout's: the campaign's tooling stays the head's, and the tree it measures is the slice's.

    Files under a root that `source` does not hold are removed, so the change read against a base is
    that base's diff to `source` in those roots.
    """
    # A root neither side holds matches no path, which git refuses as a pathspec.
    held = set(git("ls-tree", "--name-only", source, project=project).split())
    roots = [root for root in TREE_ROOTS if root in held or git("ls-files", "--", root, project=project)]
    if roots:
        git("restore", "--source=" + source, "--staged", "--worktree", "--", *roots, project=project,
            skip_lfs=skip_lfs)


def plan(project, base, reuse_from, most):
    """(whether the plan fits `most` shards, the mutant records it counted) for `base` against the tree.

    Raises for a plan that refused for any reason but the ceiling: the run cannot measure that
    window, and a window chosen past the refusal would hide it.
    """
    with tempfile.TemporaryDirectory() as scratch:
        listed = Path(scratch) / "mutants.json"
        command = [sys.executable, str(REPO_ROOT / "scripts/test_quality/mutation_check.py"),
                   "--project", str(project), "--base", base, "--plan", "--emit-mutants", str(listed),
                   "--max-shards", str(most)]
        if reuse_from:
            command += ["--reuse-from", str(reuse_from)]
        done = subprocess.run(command, capture_output=True, text=True, cwd=str(project))
        if done.returncode not in (0, mutation_check.CEILING_REFUSAL):
            raise RuntimeError("the plan against {} refused: {}".format(
                base[:12], (done.stdout + done.stderr).strip()[-2000:]))
        mutants = json.loads(listed.read_text()) if listed.exists() else []
    return done.returncode == 0, mutants


def choose(start, commits, ask):
    """(base, reached, the slice's mutants, [(commit, its mutants)] skipped) over `commits`.

    `commits` are the first-parent commits after `start`, oldest first, and `ask(base, commit)` is
    `plan` over `base`..`commit`. The slice grows a commit at a time from `start` and stops before the
    first commit that would take it over the ceiling, so what a night leaves behind is the newest
    commits, which the next night starts from. A commit over the ceiling on its own is skipped,
    with its mutants, and the slice starts after it: left in place it would stop every later night.
    """
    base, reached, measured, skipped = start, None, [], []
    for commit in commits:
        fits, mutants = ask(base, commit)
        if fits:
            reached, measured = commit, mutants
        elif reached is None:
            skipped.append((commit, mutants))
            base = commit
        else:
            break
    return base, reached or base, measured, skipped


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
    commits = git("rev-list", "--first-parent", "--reverse", "{}..HEAD".format(start),
                  project=project).split()
    # The plans are read in a worktree of their own, whose large files are left as pointers: a plan
    # reads the C# sources alone, and the checkout the later steps measure is left as it was.
    scratch = Path(tempfile.mkdtemp(prefix="mutation-window-"))
    git("worktree", "add", "--detach", str(scratch / "tree"), "HEAD", project=project, skip_lfs=True)
    try:
        def ask(base, commit):
            restore_tree(scratch / "tree", commit, skip_lfs=True)
            return plan(scratch / "tree", base, args.reuse_from, NIGHTLY_MAX_SHARDS)
        base, reached, measured, skipped = choose(start, commits, ask)
    finally:
        git("worktree", "remove", "--force", str(scratch / "tree"), project=project)
    behind = commits[commits.index(reached) + 1:] if reached in commits else commits

    def subject(sha):
        return git("log", "-1", "--format=%s", sha, project=project)

    record = {
        "head": head, "start": start, "source": source, "base": base, "reached": reached,
        "skipped": [{"sha": sha, "subject": subject(sha), "mutants": mutants} for sha, mutants in skipped],
        "behind": [{"sha": sha, "subject": subject(sha)} for sha in behind],
        "measured_mutants": measured,
    }
    Path(args.output).write_text(json.dumps(record, indent=1))
    print("window {}..{} ({}): measuring {}..{}, {} mutant(s); {} commit(s) skipped over the ceiling, "
          "{} left for later nights".format(start[:12], head[:12], source, base[:12], reached[:12],
                                            len(measured), len(skipped), len(behind)), file=sys.stderr)
    print("reached={}".format(reached))
    print("max-shards={}".format(NIGHTLY_MAX_SHARDS))
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
    """(the night's report, whether it owes the issue a comment).

    The commits left for later nights are listed and owe nothing: a later night measures them.
    """
    failed = ["{} concluded {}".format(job, result) for job, result in results if result not in PASSING]
    unanswered, stale, unmeasured = sections(verdict) if verdict is not None else ([], [], [])
    body = []
    if window is None:
        body += ["The window could not be read, so nothing was measured; the next night measures "
                 "this one's commits."]
    else:
        body += ["Window from `{}` ({}): measured `{}`..`{}`, {} mutant(s); {} commit(s) up to `{}` are "
                 "left for later nights.".format(
                     window["start"][:12], window["source"], window["base"][:12],
                     window["reached"][:12], len(window["measured_mutants"]), len(window["behind"]),
                     window["head"][:12])]
        if verdict is None and not reused and window["measured_mutants"]:
            unmeasured = ["no verdict was reached, so none of the {} mutant(s) of the measured slice "
                          "has one".format(len(window["measured_mutants"]))] + [
                described(mutant) for mutant in window["measured_mutants"]]
    skipped = (window or {}).get("skipped") or []
    owing = [("Unanswered survivors", unanswered), ("Stale declarations", stale),
             ("Not measured", unmeasured), ("Jobs that did not pass", failed),
             ("Commits over a night's ceiling on their own, skipped",
              ["{} {}".format(commit["sha"][:12], commit["subject"]) for commit in skipped]),
             ("Their mutants, unmeasured",
              [described(mutant) for commit in skipped for mutant in commit["mutants"]])]
    owed = window is None or any(lines for _, lines in owing)
    behind = [("Left for later nights",
               ["{} {}".format(commit["sha"][:12], commit["subject"])
                for commit in (window or {}).get("behind") or []])]
    for heading, lines in owing + behind:
        if lines:
            body += ["", "### {}".format(heading)] + listed(lines)
    body += ["", "Run: {}".format(run_url)]
    text = "\n".join(["## Nightly mutation campaign", ""] + body)
    if len(text) > COMMENT_LIMIT:
        text = text[:COMMENT_LIMIT] + "\n\n(cut; the run holds the rest)"
    return text, owed


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
    text, owed = compose(window, verdict, results_from(environ.get("RESULTS", "")),
                         environ.get("REUSED", ""), run_url)
    print(text)
    if owed:
        print("commented on issue #{}".format(post(slug, text, read, write)), file=sys.stderr)
    else:
        print("nothing to answer: no comment is written", file=sys.stderr)
    if window is not None:
        print("reached={}".format(window["reached"]))
    return 0


def main(argv=None, environ=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=str(REPO_ROOT))
    commands = parser.add_subparsers(dest="command", required=True)
    window = commands.add_parser("window")
    window.add_argument("--output", required=True)
    window.add_argument("--reuse-from")
    tree = commands.add_parser("tree")
    tree.add_argument("--source", required=True)
    report = commands.add_parser("report")
    report.add_argument("--window", required=True)
    report.add_argument("--verdict")
    args = parser.parse_args(argv)
    environ = os.environ if environ is None else environ
    if args.command == "window":
        return window_main(args, environ)
    if args.command == "tree":
        restore_tree(Path(args.project).resolve(), args.source)
        return 0
    return report_main(args, environ)


if __name__ == "__main__":
    sys.exit(main())
