"""Which branches are maintenance lines, and what a pull request onto one may not bring.

CONTRIBUTING.md's maintenance-line section owns the rules, and this their reading: a script or hook
judging a line asks here rather than spelling a line's name, and `test_maintenance_line.py` reads
their strings for one that does.
"""

import collections
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "release"))
import published_check  # noqa: E402
import release_notes  # noqa: E402

MAIN = "main"

LINE = re.compile(r"[0-9]+\.x")

# A version or a series, matched whole, that a reader could take for a line.
LOOKALIKE = re.compile(r"[vV]?[0-9]+(?:\.[0-9]+)*\.[xX*]|[vV]?[0-9]+(?:\.[0-9]+)+|[vV][0-9]+")

# A Conventional Commits subject for a feature, or for a type marked breaking with `!`.
REFUSED_SUBJECT = re.compile(r"(?:feat(?:\([^)]*\))?!?|[a-z]+(?:\([^)]*\))?!):", re.IGNORECASE)
BREAKING_FOOTER = re.compile(r"^BREAKING[ -]CHANGE:", re.MULTILINE)
BREAKING_FRAGMENTS = "Packages/com.velvet.core/Changelog~/breaking"

SECTION = "CONTRIBUTING.md's maintenance-line section"

# What a head brings onto a line: its commits the line lacks, as (sha, subject, body); those of them
# `main` holds too, as (sha, subject); and the breaking fragments it adds, by path.
Reading = collections.namedtuple("Reading", "commits from_main fragments")


def is_line(branch):
    return bool(LINE.fullmatch(branch or ""))


def lookalike(branch):
    """Whether `branch` is named like a line without being one."""
    return not is_line(branch) and bool(LOOKALIKE.fullmatch(branch or ""))


# What `maintained` raises where the tree at its revision cannot be read for an answer.
UNREAD = (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError, ValueError,
          release_notes.ReleaseNotesError)


def maintained(cwd, rev=f"origin/{MAIN}"):
    """The one line the tree at `rev` maintains: the series before main's, which is its package's
    major, and the next major while its CHANGELOG holds a breaking entry. Raises one of `UNREAD`."""
    version = published_check.declared_version(
        published_check.read_at(cwd, rev, published_check.PACKAGE_JSON_PATH))
    if not isinstance(version, str) or not version.split(".")[0].isdigit():
        raise ValueError(f"the package at {rev} declares no version: {version!r}")
    building = int(version.split(".")[0]) + bool(published_check.section_entries(
        published_check.changelog_at(cwd, rev), release_notes.BREAKING_SECTION))
    return f"{building - 1}.x"


def unmaintained(line, kept):
    """Why `line` is not the line `kept`, `maintained`'s answer, or None where it is."""
    if not is_line(line) or line == kept:
        return None
    if int(line.split(".")[0]) < int(kept.split(".")[0]):
        return (f"{line} is no longer maintained: the one line is {kept}, the series before main's. "
                f"The line moves when main starts building its next major, and SECURITY.md's "
                f"supported-versions table is updated in the same change that ends {line} ({SECTION})")
    return (f"{line} is not the line main maintains, which is {kept}: a line is the series before "
            f"main's, and {line} becomes it once main holds a breaking entry for the major after "
            f"{line.split('.')[0]} ({SECTION})")


def refused_subject(subject, body=""):
    return (bool(REFUSED_SUBJECT.match((subject or "").strip()))
            or bool(BREAKING_FOOTER.search(body or "")))


def _git(cwd, args, timeout):
    done = subprocess.run(["git", "-C", str(cwd), *args], capture_output=True, text=True,
                          timeout=timeout)
    if done.returncode != 0:
        raise subprocess.CalledProcessError(done.returncode, ["git", *args], done.stdout, done.stderr)
    return done.stdout


def read(cwd, line, head, timeout, remote="origin"):
    """What `head` brings onto `<remote>/<line>`, read off refs the caller has fetched.

    Raises CalledProcessError, TimeoutExpired or OSError where git did not answer, which is never a
    reading that found nothing.
    """
    line_ref, main_ref = f"{remote}/{line}", f"{remote}/{MAIN}"
    logged = _git(cwd, ["log", "--format=%H%x00%s%x00%b%x1e", f"{line_ref}..{head}"], timeout)
    commits = [tuple(entry.strip("\n").split("\x00", 2)) for entry in logged.split("\x1e")
               if entry.strip()]
    bases = _git(cwd, ["merge-base", "--all", head, main_ref], timeout).split()
    listed = _git(cwd, ["log", "--format=%H%x00%s", *bases, "--not", line_ref], timeout)
    from_main = [tuple(entry.split("\x00", 1)) for entry in listed.splitlines() if entry]
    added = _git(cwd, ["diff", "--name-only", "--no-renames", "--diff-filter=A",
                       f"{line_ref}...{head}", "--", BREAKING_FRAGMENTS], timeout)
    fragments = [path for path in added.splitlines()
                 if path.endswith(".md") and not path.rsplit("/", 1)[-1].startswith(".")]
    return Reading(commits, from_main, fragments)


# Fetches every branch named `*.x` on origin into the refs `carried` reads.
LINES_REFSPEC = "+refs/heads/*.x:refs/remotes/origin/*.x"


def carried(cwd, base, head, timeout, remote="origin"):
    """Each line on `<remote>` other than `base` whose commits `head` holds and `<remote>/<base>`
    does not, as (line, [(sha, subject)]), read off refs the caller has fetched. Raises as `read`
    does."""
    listed = _git(cwd, ["for-each-ref", "--format=%(refname:strip=3)", f"refs/remotes/{remote}"],
                  timeout)
    found = []
    for line in sorted(name for name in listed.split() if is_line(name) and name != base):
        bases = _git(cwd, ["merge-base", "--all", head, f"{remote}/{line}"], timeout).split()
        logged = _git(cwd, ["log", "--format=%H%x00%s", *bases, "--not", f"{remote}/{base}"],
                      timeout)
        commits = [tuple(entry.split("\x00", 1)) for entry in logged.splitlines() if entry]
        if commits:
            found.append((line, commits))
    return found


def squash_reasons(base, lines):
    """The reasons a head carrying `lines`, `carried`'s answer, is not squashed onto `base`, each
    naming its way out."""
    return [f"it carries {_counted(commits, 'commit')} of {line} that {base} does not hold, starting "
            f"with {commits[0][0][:7]} {commits[0][1]}: squashed, they reach {base} and {line} stays "
            f"out of its ancestry. Land it as a merge commit, from the web interface's \"Create a "
            f"merge commit\" or with `gh pr merge <number> --merge --delete-branch` ({SECTION})"
            for line, commits in lines]


def _counted(found, noun):
    return f"{len(found)} {noun}{'' if len(found) == 1 else 's'}"


def pull_request_reasons(base, branch, title, kept=None):
    """The reasons a pull request from `branch` onto `base` does not land, read off its names and
    title and, where `kept` is `maintained`'s answer, which line main maintains, each naming its way
    out."""
    found = [f"its {role} {name} is named like a maintenance line and is not one: a line is "
             f"`<major>.x`, and any other branch takes a name that is not a version, as "
             f"`release/2.1.1` did ({SECTION})"
             for role, name in (("base", base), ("head", branch)) if lookalike(name)]
    why = unmaintained(base, kept) if kept else None
    if why:
        found.append(f"{why}. Retarget it onto {kept} or {MAIN}")
    if is_line(base) and refused_subject(title):
        found.append(f"its title `{title}` is a feature or a breaking change, and {base} takes "
                     f"neither: a fix is titled as one, and the rest lands on {MAIN} ({SECTION})")
    return found


def head_reasons(base, reading):
    """The reasons `read`'s answer for a head gives it not to land on the line `base`, each naming
    its way out."""
    found = []
    if reading.from_main:
        sha, subject = reading.from_main[0]
        found.append(f"it carries {_counted(reading.from_main, 'commit')} of {MAIN} that {base} "
                     f"does not hold, starting with {sha[:7]} {subject}: {MAIN} is not merged into a "
                     f"line. Cut the branch again from origin/{base}, and take a fix across with "
                     f"`git cherry-pick -x` or write it fresh there ({SECTION})")
    picked = [(sha, subject) for sha, subject, body in reading.commits
              if refused_subject(subject, body)]
    if picked:
        sha, subject = picked[0]
        found.append(f"it carries {_counted(picked, 'commit')} titled or footed as a feature or a "
                     f"breaking change, starting with {sha[:7]} {subject}: reword one that is a fix, "
                     f"and write a fix that came with either fresh on {base} ({SECTION})")
    if reading.fragments:
        found.append(f"it adds {reading.fragments[0]}"
                     f"{f' and {len(reading.fragments) - 1} more' if len(reading.fragments) > 1 else ''}, "
                     f"a breaking change, which {base} does not take: a breaking change lands on "
                     f"{MAIN} ({SECTION})")
    return found
