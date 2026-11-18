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
"""

import argparse
import collections
import json
import os
import re
import subprocess
import sys
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

SECTION = "CONTRIBUTING.md's merging section"
RELEASE_SECTION = "CONTRIBUTING.md's releasing section"

# gh fills the braces from the checkout's remote, so a command printed here runs as it stands.
CREATE = "gh api repos/{{owner}}/{{repo}}/milestones -f title={}"
REOPEN = "gh api -X PATCH repos/{{owner}}/{{repo}}/milestones/{} -f state=open"
CLOSE = "gh api -X PATCH repos/{{owner}}/{{repo}}/milestones/{} -f state=closed"

# A milestone, by the number its API path takes and its title.
Milestone = collections.namedtuple("Milestone", "number title")

# What the repository answers: its open milestones, the versions its tags publish, the major main
# builds, and its closed milestones.
Repository = collections.namedtuple("Repository", "open published series closed", defaults=((),))

# The pull request's own fields the decision reads: `landing` is the branch its merge lands on, its
# stack's base for a stack member.
PullRequest = collections.namedtuple("PullRequest",
                                     "title state base landing head sha fork")

# What a head's tree changes against where it branched: whether it touches the package, the versions
# it closes, and `next_version.derive`'s reading of the entries and members it adds, None where its
# tree was not read.
Change = collections.namedtuple("Change", "package closes derivation")

NOTHING = Change(False, (), None)


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


def opening(title, repository):
    """The command that opens milestone `title`: reopening a closed one that holds the title, and
    creating it otherwise."""
    closed = {milestone.title: milestone.number for milestone in repository.closed}
    return f"`{REOPEN.format(closed[title])}`" if title in closed else f"`{CREATE.format(title)}`"


def newest_published(repository, base):
    """The newest release `base` published, or for a branch that has published none yet, the newest
    release there is."""
    found = [version for version in repository.published
             if publishes(base, version, repository.series)] or repository.published
    return max(found, key=next_version.parse_version, default=None)


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


def candidates(previous, base, repository):
    """The commands opening each version that may follow `previous` on `base`."""
    following = [f"v{version}" for version in next_version.successors(previous)
                 if publishes(base, version, repository.series)]
    return " or ".join(f"{title} with {opening(title, repository)}" for title in following)


def series_reasons(base, repository):
    """What stops `base`'s lowest open version milestone being its next version."""
    found = owned(repository, base)
    if not found:
        previous = newest_published(repository, base)
        ways = (candidates(previous, base, repository) if previous
                else f"it with {opening('v<version>', repository)}")
        return [f"no open milestone names the next version {base} publishes, the one "
                f"`python3 scripts/release/next_version.py --rev origin/{base}` prints: open "
                f"{ways} ({SECTION})"]
    lowest = found[0]
    version = version_of(lowest.title)
    if version in repository.published:
        return [f"{lowest.title}, the lowest open version milestone {base} publishes, is published "
                f"already: close it with `{CLOSE.format(lowest.number)}` ({SECTION})"]
    previous = next_version.skipped_from(repository.published, version)
    if previous:
        return [f"{lowest.title}, the lowest open version milestone {base} publishes, does not "
                f"follow v{previous}, the newest release below it, so publishing it skips a version: "
                f"open {candidates(previous, base, repository)}, whichever comes next, or retitle "
                f"{lowest.title} ({SECTION})"]
    return []


def bump_of(version, published):
    """Which of `next_version.BUMPS` `version` takes over the newest release below it, or None
    where nothing below it was published or it follows none."""
    below = [named for named in published
             if next_version.parse_version(named) < next_version.parse_version(version)]
    if not below:
        return None
    previous = max(below, key=next_version.parse_version)
    return dict(zip(next_version.successors(previous), next_version.BUMPS)).get(version)


def bump_reasons(number, title, derivation, repository):
    """Why milestone `title` is a smaller bump than the one `next_version.derive` reads off what the
    head adds, which is the bump the release it ships in will be derived as."""
    version = version_of(title)
    bump = bump_of(version, repository.published) if version and derivation else None
    if bump is None or (next_version.BUMPS.index(bump)
                        >= next_version.BUMPS.index(derivation.bump)):
        return []
    previous = max((named for named in repository.published
                    if next_version.parse_version(named) < next_version.parse_version(version)),
                   key=next_version.parse_version)
    wanted = f"v{next_version.bumped(previous, derivation.bump)}"
    opened = "" if wanted in {milestone.title for milestone in repository.open} else (
        f", opening it with {opening(wanted, repository)}")
    return [f"what it adds ships in a {derivation.bump} — {derivation.reason} — and it carries "
            f"{title}, a {bump}: carry {wanted} with `gh pr edit {number} --milestone {wanted}`"
            f"{opened}, which merges once that is the next version its base publishes "
            f"({RELEASE_SECTION})"]


def head_reasons(number, title, change, base, repository):
    """What is wrong with milestone `title` for what pull request `number`'s head changes onto
    `base`."""
    version = version_of(title)
    found = []
    if change.package and version is None:
        found.append(f"it changes {PACKAGE} and carries {INFRASTRUCTURE}, which is for a change "
                     f"outside it: set the version it ships in with `gh pr edit {number} --milestone "
                     f"{suggested(True, base, repository)}` ({SECTION})")
    elif change.package and judged(base):
        problems = series_reasons(base, repository)
        found.extend(problems)
        following = owned(repository, base)[0].title if not problems else None
        if following and following != title:
            found.append(f"it changes {PACKAGE} under {title}, and the next version {base} publishes "
                         f"is {following}: wait for {following} to be published and its milestone "
                         f"closed, or ship it in {following} with `gh pr edit {number} --milestone "
                         f"{following}` ({SECTION})")
    if version is not None:
        found.extend(bump_reasons(number, title, change.derivation, repository))
        found.extend(f"it closes {closed} in the CHANGELOG under the milestone {title}, and a "
                     f"release closes the version its milestone names: close {version} instead, or "
                     f"carry v{closed} with `gh pr edit {number} --milestone v{closed}` where that "
                     f"is the next version ({RELEASE_SECTION})"
                     for closed in change.closes if closed != version)
    return found


def merge_reasons(number, pull_request, change, repository):
    """Why pull request `number` does not merge under its milestone: what is wrong with the
    milestone itself, and where nothing is, what is wrong with it for what the head changes."""
    suggestion = suggested(change.package, pull_request.landing, repository)
    found = pull_request_reasons(number, pull_request.title, pull_request.state, suggestion)
    return found or head_reasons(number, pull_request.title, change, pull_request.landing,
                                 repository)


def release_reasons(version, repository):
    """The reasons a release may not publish `version`, each naming the way out."""
    found = []
    previous = next_version.skipped_from(repository.published, version)
    if previous:
        found.append(f"v{version} does not follow v{previous}, the newest release below it, so "
                     f"publishing it skips a version: release whichever of "
                     f"{', '.join('v' + named for named in next_version.successors(previous))} "
                     f"comes next ({RELEASE_SECTION})")
    base = publisher(version, repository.series)
    title = f"v{version}"
    if title not in {milestone.title for milestone in repository.open}:
        found.append(f"no open milestone is titled {title}, and a release publishes the version "
                     f"its milestone names: open it with {opening(title, repository)} where "
                     f"{title} is the next version {base} publishes ({RELEASE_SECTION})")
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


def listed(api, path, pages=PAGES):
    """Every entry of a REST listing, or Unread past `pages` pages."""
    joiner = "&" if "?" in path else "?"
    found = []
    for page in range(1, pages + 1):
        answer = api(f"{path}{joiner}per_page={PAGE}&page={page}")
        if not isinstance(answer, list):
            raise Unread(f"{path} did not answer with a list")
        found.extend(answer)
        if len(answer) < PAGE:
            return found
    raise Unread(f"{path} lists more than {PAGE * pages}")


def milestones(api, pages=PAGES):
    """(open, closed) milestones, from one listing of both."""
    entries = listed(api, "milestones?state=all", pages)
    return tuple([Milestone(entry["number"], entry["title"]) for entry in entries
                  if entry.get("state") == state] for state in ("open", "closed"))


def pull_request(api, number):
    payload = api(f"pulls/{number}")
    if not isinstance(payload, dict) or not payload.get("head") or not payload.get("base"):
        raise Unread(f"pull request #{number} came back without its head and base")
    milestone = payload.get("milestone") or {}
    stack = payload.get("stack")
    head, base = payload["head"], payload["base"]
    return PullRequest(milestone.get("title"), milestone.get("state"), base["ref"],
                       stack["base"]["ref"] if stack else base["ref"], head["ref"], head["sha"],
                       ((head.get("repo") or {}).get("full_name") or "")
                       != ((base.get("repo") or {}).get("full_name") or ""))


def repository(project, api, timeout, main=f"origin/{maintenance_line.MAIN}", pages=PAGES):
    """`Repository` read off the API, origin's tags and the fetched `main`."""
    try:
        series = maintenance_line.main_state(project, main).series
        versions = published_check.published_versions(
            published_check.remote_tags(project, timeout=timeout))
    except maintenance_line.UNREAD as failure:
        raise Unread(f"which series {main} builds, or which versions origin publishes, could not "
                     f"be read: {failure}") from failure
    opened, closed = milestones(api, pages)
    return Repository(opened, versions, series, closed)


# The bound of each git reading here that published_check's own `git` does not take.
GIT_TIMEOUT = 5


def text_at(project, rev, path):
    """The file at `rev`, or "" where the tree holds none."""
    present = subprocess.run(["git", "-C", str(project), "cat-file", "-e", f"{rev}:{path}"],
                             capture_output=True, timeout=GIT_TIMEOUT)
    return published_check.read_at(project, rev, path) if present.returncode == 0 else ""


def change(project, base, head, versions):
    """`Change` for the tree at `head` against where it branched from `base`.

    Read from the merge base rather than from `base` itself: a head behind a release would otherwise
    read the released section's entries as ones it adds.
    """
    try:
        fork = published_check.git(project, "merge-base", base, head).strip()
        paths = published_check.git(project, "diff", "--name-only", "--no-renames", fork,
                                    head).split()
        before = published_check.changelog_at(project, fork)
        after = published_check.changelog_at(project, head)
        gained = next_version.api_gained(text_at(project, fork, next_version.PUBLIC_API_PATH),
                                         text_at(project, head, next_version.PUBLIC_API_PATH))
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError,
            release_notes.ReleaseNotesError) as failure:
        raise Unread(f"what {head} changes against {base} could not be read: {failure}") from failure
    breaking, opened = release_notes.BREAKING_SECTION, release_notes.OPEN_SECTION
    return Change(
        changes_package(paths),
        tuple(published_check.closing_versions(before, after,
                                               {f"v{version}" for version in versions})),
        next_version.derive(
            "0.0.0",
            published_check.entries_missing_from(published_check.section_entries(after, breaking),
                                                 published_check.section_entries(before, breaking)),
            gained,
            published_check.entries_missing_from(next_version.added_entries(after, opened),
                                                 next_version.added_entries(before, opened))))


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
    state = repository(project, api, timeout)
    read = change(project, base or f"origin/{asked.base}", head or asked.sha, state.published)
    return merge_reasons(number, asked, read, state)


def close(api, version):
    """Closes the open milestone titled after `version`, answering what it did."""
    title = f"v{version}"
    found = [milestone for milestone in milestones(api)[0] if milestone.title == title]
    for milestone in found:
        api(f"milestones/{milestone.number}", "PATCH", ("state=closed",))
    return f"closed {title}" if found else f"no open milestone is titled {title}; nothing to close"


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
    args = parser.parse_args(argv)

    project = Path(args.project).resolve()
    api = gh_api(project, args.timeout)
    try:
        if args.command == "close":
            print(close(api, args.version))
            return 0
        found = (check(project, api, args.number, args.base, args.head, args.timeout)
                 if args.command == "check"
                 else release_reasons(args.version, repository(project, api, args.timeout)))
    except Unread as failure:
        print(f"could not be read, so nothing was decided: {failure}. Ask again once the reading "
              f"works{rerun_hint(args.command)}", file=sys.stderr)
        return 1
    for reason in found:
        print(reason, file=sys.stderr)
    if found and rerun_hint(args.command) != ".":
        print(f"This check reads the milestone when its run reaches it, and nothing runs it again "
              f"when the milestone changes later{rerun_hint(args.command)}", file=sys.stderr)
    if not found:
        print("the milestone holds")
    return 1 if found else 0


def rerun_hint(command):
    """The way to ask the Milestone check again in a workflow run, where this is one."""
    run = os.environ.get("GITHUB_RUN_ID")
    return (f": run it again with `gh run rerun {run} --failed`, or push to the branch"
            if command == "check" and run else ".")


if __name__ == "__main__":
    sys.exit(main())
