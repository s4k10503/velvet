"""Which branches are maintenance lines, and what a pull request onto one may not bring.

CONTRIBUTING.md's maintenance-line section owns the rules, and this their reading: a script or hook
judging a line asks here rather than spelling a line's name, and `test_maintenance_line.py` reads
their strings for one that does.
"""

import collections
import re
import subprocess

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


def _counted(found, noun):
    return f"{len(found)} {noun}{'' if len(found) == 1 else 's'}"


def pull_request_reasons(base, branch, title):
    """The reasons a pull request from `branch` onto `base` does not land, read off its names and
    title, each naming its way out."""
    found = [f"its {role} {name} is named like a maintenance line and is not one: a line is "
             f"`<major>.x`, and any other branch takes a name that is not a version, as "
             f"`release/2.1.1` did ({SECTION})"
             for role, name in (("base", base), ("head", branch)) if lookalike(name)]
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
