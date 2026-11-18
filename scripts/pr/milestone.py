#!/usr/bin/env python3
"""Which milestone a pull request merges under, and which version a release publishes.

CONTRIBUTING.md's merging section owns the rule a merge is held to, and its releasing section the
rule a release is held to; this is their reading. `settle.py`, `refuse/merge_off_milestone.py`, the
`Source generators ▸ milestone` job and `upm.yml` ask it.

Main publishes every major from the one `maintenance_line.main_state` says it builds, and a line its
own major.

Run: python3 scripts/pr/milestone.py check --number <n> [--base <rev>] [--head <rev>]
     python3 scripts/pr/milestone.py release --version X.Y.Z
     python3 scripts/pr/milestone.py close --version X.Y.Z
     python3 scripts/pr/milestone.py rerun (--number <n> | --open)
"""

import argparse
import collections
import json
import re
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "release"))
import maintenance_line  # noqa: E402
import next_version  # noqa: E402
import published_check  # noqa: E402
import release_notes  # noqa: E402

INFRASTRUCTURE = "Infrastructure"
PACKAGE = "Packages/com.velvet.core/"
VERSION = re.compile(r"v([0-9]+\.[0-9]+\.[0-9]+)")
FEATURE_TITLE = re.compile(r"feat(?:\([^)]*\))?!?:", re.IGNORECASE)

SECTION = "CONTRIBUTING.md's merging section"
RELEASE_SECTION = "CONTRIBUTING.md's releasing section"

# gh fills the braces from the checkout's remote, so a command printed here runs as it stands.
CREATE = "gh api repos/{{owner}}/{{repo}}/milestones -f title={}"
CLOSE = "gh api -X PATCH repos/{{owner}}/{{repo}}/milestones/{} -f state=closed"

# An open milestone, by the number its API path takes and its title.
Milestone = collections.namedtuple("Milestone", "number title")

# What the repository answers: its open milestones, the versions its tags publish, and the major main
# builds.
Repository = collections.namedtuple("Repository", "open published series")

# What a pull request is judged on: its milestone's title and state, each None where it carries none,
# its base, the branch the merge lands on, its head, how many files GitHub says it changes, and its
# title.
PullRequest = collections.namedtuple("PullRequest",
                                     "title state base landing head sha changed fork name")

# What a head's tree adds against its base: the versions it closes, and the first breaking entry and
# the first `### Added` entry it adds to the open sections, each None where it adds none.
Change = collections.namedtuple("Change", "closes breaking added")

NOTHING = Change((), None, None)


class Unread(RuntimeError):
    """A reading that did not answer, which is never a reading that found nothing."""


def version_of(title):
    match = VERSION.fullmatch(title or "")
    return match.group(1) if match else None


def judged(base):
    """Whether a release is published from `base`, which is what holds its merges to a version."""
    return base == maintenance_line.MAIN or maintenance_line.is_line(base)


def publishes(base, version, series):
    major = next_version.parse_version(version)[0]
    if base == maintenance_line.MAIN:
        return major >= series
    return maintenance_line.is_line(base) and major == maintenance_line.major_of(base)


def publisher(version, series):
    major = next_version.parse_version(version)[0]
    return maintenance_line.MAIN if major >= series else maintenance_line.line_of(major)


def owned(repository, base):
    """The open version milestones `base` publishes, lowest first."""
    return sorted((milestone for milestone in repository.open
                   if version_of(milestone.title)
                   and publishes(base, version_of(milestone.title), repository.series)),
                  key=lambda milestone: next_version.parse_version(version_of(milestone.title)))


def changes_package(paths):
    return any(path.startswith(PACKAGE) for path in paths)


def suggested(package, base, repository):
    """The milestone a way out names: the next version where `base` publishes one, and
    Infrastructure for a change outside the package."""
    if not package:
        return INFRASTRUCTURE
    found = owned(repository, base) if judged(base) else []
    return found[0].title if found else "v<the version it ships in>"


def pull_request_reasons(number, title, state, suggestion):
    """What is wrong with the milestone pull request `number` carries, whatever its head changes."""
    edit = f"`gh pr edit {number} --milestone {suggestion}`"
    if title is None:
        return [f"it carries no milestone, and every pull request merges under one: set it with "
                f"{edit} ({SECTION})"]
    if state != "open":
        return [f"its milestone {title} is closed: set an open one with {edit} ({SECTION})"]
    if version_of(title) is None and title != INFRASTRUCTURE:
        return [f"its milestone {title} is neither a version (vMAJOR.MINOR.PATCH) nor "
                f"{INFRASTRUCTURE}: set one with {edit} ({SECTION})"]
    return []


def series_reasons(base, repository):
    """What stops `base`'s lowest open version milestone being its next version."""
    found = owned(repository, base)
    if not found:
        return [f"no open milestone names the next version {base} publishes: open it with "
                f"`{CREATE.format('v<version>')}`, the version "
                f"`python3 scripts/release/next_version.py --rev origin/{base}` prints ({SECTION})"]
    lowest = found[0]
    version = version_of(lowest.title)
    if version in repository.published:
        return [f"{lowest.title}, the lowest open version milestone {base} publishes, is published "
                f"already: close it with `{CLOSE.format(lowest.number)}` ({SECTION})"]
    previous = next_version.skipped_from(repository.published, version)
    if previous:
        return [f"{lowest.title}, the lowest open version milestone {base} publishes, does not "
                f"follow v{previous}, the newest release below it, so publishing it skips a version: "
                f"open the milestone of whichever of {spelled(next_version.successors(previous))} "
                f"comes next with `{CREATE.format('v<version>')}`, or retitle {lowest.title} "
                f"({SECTION})"]
    return []


def spelled(versions):
    return ", ".join("v" + version for version in versions)


def bump_of(version, published):
    """Which of `next_version.BUMPS` `version` takes over the newest release below it, or None
    where nothing below it was published or it follows none."""
    below = [named for named in published
             if next_version.parse_version(named) < next_version.parse_version(version)]
    if not below:
        return None
    previous = max(below, key=next_version.parse_version)
    return dict(zip(next_version.successors(previous), next_version.BUMPS)).get(version)


def needed_bump(change, name):
    """The least bump what the head adds ships in, with the entry or title that asks it, or None."""
    if change.breaking:
        return "major", f"adds the breaking entry `{change.breaking}`"
    if change.added:
        return "minor", f"adds the `### Added` entry `{change.added}`"
    if FEATURE_TITLE.match((name or "").strip()):
        return "minor", f"is titled as a feature, `{name}`"
    return None


def bump_reasons(number, title, change, name, published):
    version = version_of(title)
    asked = needed_bump(change, name)
    bump = bump_of(version, published) if version else None
    if not asked or bump is None:
        return []
    need, why = asked
    if next_version.BUMPS.index(bump) >= next_version.BUMPS.index(need):
        return []
    previous = max((named for named in published
                    if next_version.parse_version(named) < next_version.parse_version(version)),
                   key=next_version.parse_version)
    wanted = next_version.bumped(previous, need)
    return [f"it {why}, which ships in a {need} at least, and carries {title}, a {bump}: carry "
            f"v{wanted} with `gh pr edit {number} --milestone v{wanted}`, which merges once that is "
            f"the next version its base publishes, or take the entry out where the API is "
            f"unchanged ({RELEASE_SECTION})"]


def head_reasons(number, title, package, change, base, repository, name=""):
    """What is wrong with milestone `title` for what pull request `number`'s head changes onto
    `base`."""
    version = version_of(title)
    found = []
    if package and version is None:
        found.append(f"it changes {PACKAGE} and carries {INFRASTRUCTURE}, which is for a change "
                     f"outside it: set the version it ships in with `gh pr edit {number} --milestone "
                     f"{suggested(package, base, repository)}` ({SECTION})")
    elif package and judged(base):
        problems = series_reasons(base, repository)
        found.extend(problems)
        following = owned(repository, base)[0].title if not problems else None
        if following and following != title:
            found.append(f"it changes {PACKAGE} under {title}, and the next version {base} publishes "
                         f"is {following}: wait for {following} to be published and its milestone "
                         f"closed, or ship it in {following} with `gh pr edit {number} --milestone "
                         f"{following}` ({SECTION})")
    if version is not None:
        found.extend(bump_reasons(number, title, change, name, repository.published))
        found.extend(f"it closes {closed} in the CHANGELOG under the milestone {title}, and a "
                     f"release closes the version its milestone names: close {version} instead, or "
                     f"carry v{closed} with `gh pr edit {number} --milestone v{closed}` where that "
                     f"is the next version ({RELEASE_SECTION})"
                     for closed in change.closes if closed != version)
    return found


def merge_reasons(number, pull_request, package, change, repository):
    """Why pull request `number` does not merge under its milestone: what is wrong with the
    milestone itself, and where nothing is, what is wrong with it for what the head changes."""
    suggestion = suggested(package, pull_request.landing, repository)
    found = pull_request_reasons(number, pull_request.title, pull_request.state, suggestion)
    return found or head_reasons(number, pull_request.title, package, change, pull_request.landing,
                                 repository, pull_request.name)


def release_reasons(version, repository):
    """The reasons a release may not publish `version`, each naming the way out."""
    found = []
    previous = next_version.skipped_from(repository.published, version)
    if previous:
        found.append(f"v{version} does not follow v{previous}, the newest release below it, so "
                     f"publishing it skips a version: release whichever of "
                     f"{spelled(next_version.successors(previous))} comes next ({RELEASE_SECTION})")
    base = publisher(version, repository.series)
    title = f"v{version}"
    if title not in {milestone.title for milestone in repository.open}:
        found.append(f"no open milestone is titled {title}, and a release publishes the version "
                     f"its milestone names: open it with `{CREATE.format(title)}` where {title} is "
                     f"the next version {base} publishes ({RELEASE_SECTION})")
        return found
    below = [milestone for milestone in owned(repository, base)
             if next_version.parse_version(version_of(milestone.title))
             < next_version.parse_version(version)]
    if below and version_of(below[0].title) in repository.published:
        found.append(f"{below[0].title} is published and its milestone is still open: close it with "
                     f"`{CLOSE.format(below[0].number)}` ({RELEASE_SECTION})")
    elif below:
        found.append(f"{below[0].title} is open below {title} among the versions {base} publishes: "
                     f"publish {below[0].title} first, or close its milestone with "
                     f"`{CLOSE.format(below[0].number)}` ({RELEASE_SECTION})")
    return found


# The API is asked a page at a time: a listing GitHub cuts at its page size reads as complete
# otherwise.
PAGE = 100
PAGES = 30


def listed(api, path):
    """Every entry of a REST listing, or Unread past `PAGES` pages."""
    joiner = "&" if "?" in path else "?"
    found = []
    for page in range(1, PAGES + 1):
        answer = api(f"{path}{joiner}per_page={PAGE}&page={page}")
        if not isinstance(answer, list):
            raise Unread(f"{path} did not answer with a list")
        found.extend(answer)
        if len(answer) < PAGE:
            return found
    raise Unread(f"{path} lists more than {PAGE * PAGES}")


def open_milestones(api):
    return [Milestone(entry["number"], entry["title"])
            for entry in listed(api, "milestones?state=open")]


def changed_paths(api, number, expected):
    """Every path pull request `number` changes, a rename's old path included, or Unread where
    fewer files come back than the `expected` count the pull request reports."""
    entries = listed(api, f"pulls/{number}/files")
    if len(entries) < expected:
        raise Unread(f"pull request #{number} reports {expected} changed files and its listing "
                     f"holds {len(entries)}")
    return [path for entry in entries
            for path in (entry["filename"], entry.get("previous_filename")) if path]


def pull_request(api, number):
    payload = api(f"pulls/{number}")
    if not isinstance(payload, dict) or not payload.get("head") or not payload.get("base"):
        raise Unread(f"pull request #{number} came back without its head and base")
    milestone = payload.get("milestone") or {}
    stack = payload.get("stack")
    head, base = payload["head"], payload["base"]
    return PullRequest(milestone.get("title"), milestone.get("state"), base["ref"],
                       stack["base"]["ref"] if stack else base["ref"], head["ref"], head["sha"],
                       payload.get("changed_files") or 0,
                       ((head.get("repo") or {}).get("full_name") or "")
                       != ((base.get("repo") or {}).get("full_name") or ""),
                       payload.get("title") or "")


def repository(project, api, timeout, main=f"origin/{maintenance_line.MAIN}"):
    """`Repository` read off the API, origin's tags and the fetched `main`."""
    try:
        series = maintenance_line.main_state(project, main).series
        versions = published_check.published_versions(
            published_check.remote_tags(project, timeout=timeout))
    except maintenance_line.UNREAD as failure:
        raise Unread(f"which series {main} builds, or which versions origin publishes, could not "
                     f"be read: {failure}") from failure
    return Repository(open_milestones(api), versions, series)


def first_new(entries, before):
    new = published_check.entries_missing_from(entries, before)
    return next_version.first_line(new[0]).removeprefix("- ") if new else None


def change(project, base, head, versions):
    """`Change` for the tree at `head` against the tree at `base`."""
    try:
        before = published_check.changelog_at(project, base)
        after = published_check.changelog_at(project, head)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError,
            release_notes.ReleaseNotesError) as failure:
        raise Unread(f"what {head} changes in the CHANGELOG against {base} could not be read: "
                     f"{failure}") from failure
    breaking = release_notes.BREAKING_SECTION
    opened = release_notes.OPEN_SECTION
    return Change(
        tuple(published_check.closing_versions(before, after,
                                               {f"v{version}" for version in versions})),
        first_new(published_check.section_entries(after, breaking),
                  published_check.section_entries(before, breaking)),
        first_new(next_version.added_entries(after, opened),
                  next_version.added_entries(before, opened)))


GH_TIMEOUT = 30


def gh_api(project, timeout=GH_TIMEOUT):
    """A REST caller for the repository `project`'s origin names, raising Unread on any failure."""
    def ask(path, method="GET", fields=()):
        sent = [argument for field in fields for argument in ("-f", field)]
        try:
            done = subprocess.run(["gh", "api", f"repos/{{owner}}/{{repo}}/{path}", "-X", method,
                                   *sent], cwd=project, capture_output=True, text=True,
                                  timeout=timeout)
        except (subprocess.TimeoutExpired, OSError) as failure:
            raise Unread(f"gh api {path} did not answer: {failure}") from failure
        if done.returncode != 0:
            raise Unread(f"gh api {path} failed: {done.stderr.strip()}")
        try:
            return json.loads(done.stdout) if done.stdout.strip() else None
        except ValueError as failure:
            raise Unread(f"gh api {path} answered something other than JSON") from failure
    return ask


def check(project, api, number, base, head, timeout):
    """`merge_reasons` for pull request `number`, read off the API and the checkout."""
    asked = pull_request(api, number)
    package = changes_package(changed_paths(api, number, asked.changed))
    state = repository(project, api, timeout)
    added = change(project, base or f"origin/{asked.base}", head or asked.sha, state.published)
    return merge_reasons(number, asked, package, added, state)


def close(api, version):
    """Closes the open milestone titled after `version`, answering what it did."""
    title = f"v{version}"
    found = [milestone for milestone in open_milestones(api) if milestone.title == title]
    for milestone in found:
        api(f"milestones/{milestone.number}", "PATCH", ("state=closed",))
    return f"closed {title}" if found else f"no open milestone is titled {title}; nothing to close"


# The job the Milestone workflow runs again, by the name generators.yml gives it.
WORKFLOW = "generators.yml"
JOB = "Milestone"
POLL_SECONDS = 20
RERUN_SECONDS = 1200


def rerun(api, number, deadline, now=time.time, sleep=time.sleep):
    """Runs the milestone job of pull request `number`'s newest generators.yml run again once that
    run has finished, answering what it did. Raises Unread where the run is still going at
    `deadline`.

    A run still going is waited for, since its job may have read the milestone before the change
    that brought this here.
    """
    head = pull_request(api, number).sha
    while True:
        runs = (api(f"actions/workflows/{WORKFLOW}/runs?event=pull_request&head_sha={head}")
                or {}).get("workflow_runs") or []
        if not runs:
            return (f"#{number}: no {WORKFLOW} run at {head[:7]} yet, and the one to come reads the "
                    f"milestone itself")
        run = max(runs, key=lambda entry: entry["run_number"])
        if run.get("status") == "completed":
            break
        if now() >= deadline:
            raise Unread(f"#{number}: run {run['id']} at {head[:7]} is still going; once it "
                         f"finishes, run its {JOB} job again with `gh run rerun {run['id']}`")
        sleep(POLL_SECONDS)
    jobs = (api(f"actions/runs/{run['id']}/jobs?per_page=100") or {}).get("jobs") or []
    job = next((entry for entry in jobs if entry.get("name") == JOB), None)
    if job is None:
        return f"#{number}: run {run['id']} has no {JOB} job to run again"
    # Asked again last: a push landing meanwhile starts a run of its own, which reads the milestone
    # itself.
    if pull_request(api, number).sha != head:
        return f"#{number}: its head moved past {head[:7]}, whose run is no longer the one to ask"
    api(f"actions/jobs/{job['id']}/rerun", "POST")
    return f"#{number}: ran {JOB} again in run {run['id']} at {head[:7]}"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--timeout", type=int, default=GH_TIMEOUT,
                        help="seconds to allow each reading")
    commands = parser.add_subparsers(dest="command", required=True)
    checking = commands.add_parser("check", help="judge a pull request's milestone")
    checking.add_argument("--number", type=int, required=True)
    checking.add_argument("--base", help="revision the change is read against "
                                         "(default: origin/<its base>)")
    checking.add_argument("--head", help="revision the change produces (default: its head)")
    for command, summary in (("release", "judge a version a release would publish"),
                             ("close", "close a published version's milestone")):
        commands.add_parser(command, help=summary).add_argument("--version", required=True)
    running = commands.add_parser("rerun", help="run the milestone job again")
    which = running.add_mutually_exclusive_group(required=True)
    which.add_argument("--number", type=int)
    which.add_argument("--open", action="store_true", help="every open pull request")
    args = parser.parse_args(argv)

    project = Path(args.project).resolve()
    api = gh_api(project, args.timeout)
    try:
        if args.command == "close":
            print(close(api, args.version))
            return 0
        if args.command == "rerun":
            return rerun_all(api, [args.number] if args.number else
                             [entry["number"] for entry in listed(api, "pulls?state=open")])
        found = (check(project, api, args.number, args.base, args.head, args.timeout)
                 if args.command == "check"
                 else release_reasons(args.version, repository(project, api, args.timeout)))
    except Unread as failure:
        print(f"could not be read, so nothing was decided: {failure}", file=sys.stderr)
        return 1
    for reason in found:
        print(reason, file=sys.stderr)
    if not found:
        print("the milestone holds")
    return 1 if found else 0


def rerun_all(api, numbers):
    deadline, failed = time.time() + RERUN_SECONDS, 0
    for number in numbers:
        try:
            print(rerun(api, number, deadline))
        except Unread as failure:
            failed += 1
            print(failure, file=sys.stderr)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
