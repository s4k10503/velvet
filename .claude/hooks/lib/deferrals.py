"""A merge or a pause held on purpose, and the expiry that keeps it from becoming silence.

A review in flight, a dependency on another change, a user waiting on an answer — none of these is
the failure the Stop guards exist for, but "I am waiting" is exactly what a nine-hour stall said too.
So a deferral is accepted and EXPIRES: it names what is held, states what clears it, and stops
counting after TTL. That cannot decay into permanent silence the way an unqualified exemption did,
and re-stating it is the moment the reason gets re-examined, which is the only part that ever
mattered.

    echo "236 waiting on round-4 review $(date +%s) $CLAUDE_CODE_SESSION_ID" >> ~/.velvet-pr-deferrals

Both Stop guards read this one file under one expiry, so a held item is re-read in one place and a
change to the format cannot land in one guard and not the other.

The trailing field is the session that wrote the line, and only that session's lines suppress
anything. A process with no view of what a key is about can append as readily as the one holding it:
measured, a subagent blocked by a guard naming six pull requests it neither owned nor could merge
deferred all six -- the only route forward it had, and it silenced the guard that holds the merge
queue. `disowned` reports the rest, so a suppression that did not happen is in front of the reader
rather than behind them.
"""

import os
import time
from pathlib import Path

DEFERRALS = Path.home() / ".velvet-pr-deferrals"
TTL = 2700


def epoch(field):
    """The field as an epoch second, or None. `str.isdigit` is true of characters `int` rejects, and
    the caller that reads a malformed entry runs before the one that would have raised — an exception
    out of a Stop guard exits 1, which the harness treats as non-blocking, so the guard turns off."""
    try:
        return int(field)
    except ValueError:
        return None


def writer():
    """The session a line written now would belong to, or None where nothing says.

    Measured: a Bash tool call carries CLAUDE_CODE_SESSION_ID and it is this session's id, while a
    PreToolUse payload carries cwd, tool_name and tool_input and nothing else. So the writer is
    identifiable from its environment rather than from what a hook is handed, and a reader that has
    no such variable cannot attribute anything -- which is a state this reports rather than guesses
    at.
    """
    return os.environ.get("CLAUDE_CODE_SESSION_ID") or None


def written_by(fields):
    """The session id a line records, or None for one written before this was recorded."""
    return fields[-1] if len(fields) >= 3 and "-" in fields[-1] and epoch(fields[-1]) is None \
        else None


def deferred(key, now=None):
    """Return (reason, minutes) when a live deferral covers the key, else None.

    The reason is free text nothing can check, so a stale one silences a guard exactly as well as a
    true one — which is what happened: a pull request was held as "fix agent working" after the
    agent had finished and its work sat unpushed in a worktree. Suppression is therefore never
    silent; a caller still prints what was claimed and how long ago, so a reason that has stopped
    being true is in front of the reader instead of behind them.
    """
    try:
        lines = DEFERRALS.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return None

    prefix = f"{key} "
    matching = [line for line in lines if line.startswith(prefix)]
    if not matching:
        return None

    fields = matching[-1].split()
    session = written_by(fields)
    stamp = epoch(fields[-2] if session else fields[-1])
    if stamp is None:
        return None

    # Bounded below as well as above. A stamp in the future — a millisecond epoch, a typo, a backward
    # clock step — was live indefinitely, which is the permanent silence the expiry exists to prevent.
    age = (time.time() if now is None else now) - stamp
    if age < 0 or age >= TTL:
        return None

    # Whose it is decides whether it suppresses. A process with no view of what the key is about can
    # append a line as readily as the one holding it -- measured, a subagent blocked by a guard
    # naming six pull requests it neither owned nor could merge deferred all six, which was the only
    # route forward it had and silenced the guard that holds the merge queue. A line another session
    # wrote is reported by `disowned` instead, and one written before this was recorded goes the same
    # way rather than being grandfathered: the whole point is that nothing says who stood behind it.
    mine = writer()
    if mine is not None and session != mine:
        return None

    return " ".join(fields[1:-2] if session else fields[1:-1]), int(age // 60)


def disowned(key, now=None):
    """(reason, whose) for every live deferral on the key that this session did not write.

    Read by the guards so a suppression that did not happen is in front of the reader rather than
    behind them, which is the same principle `deferred` states about a stale reason.
    """
    mine = writer()
    if mine is None:
        return []
    try:
        lines = DEFERRALS.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return []

    found = []
    for line in lines:
        if not line.startswith(f"{key} "):
            continue
        fields = line.split()
        session = written_by(fields)
        if session == mine:
            continue
        stamp = epoch(fields[-2] if session else fields[-1])
        if stamp is None:
            continue
        age = (time.time() if now is None else now) - stamp
        if age < 0 or age >= TTL:
            continue
        found.append((" ".join(fields[1:-2] if session else fields[1:-1]),
                      session or "a writer that did not say"))
    return found


def unusable(key, now=None):
    """Return why the newest entry for `key` was rejected outright, or None.

    None is not a state: an entry that is live, one that has expired, and no entry at all all take
    it, and `deferred` separates only the first from the other two. Nothing distinguishes an expired
    entry from an absent one, here or anywhere, for the reason below.

    A rejected deferral and an absent one both make `deferred` return None, so writing an unusable
    one reads as having written nothing — the guard fires again with the same text and the entry
    that was supposed to answer it is never mentioned. The stamp is the moment the deferral was
    WRITTEN, and `date +%s` in the guidance is easy to read as the moment it should expire; an
    entry stamped in the future is rejected by the lower bound and says nothing about why.

    Expiry is not reported, and cannot be: a stamp says when the deferral was written and nothing
    says when the line was added, so an entry written already stale and one written fresh that has
    since expired are the same data. Reporting the pair as "stale on arrival" claimed a distinction
    the file does not carry. Expiry is also the design working — it ends with the guard firing and
    the reason being re-stated.
    """
    try:
        lines = DEFERRALS.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return None

    matching = [line for line in lines if line.startswith(f"{key} ")]
    if not matching:
        return None

    fields = matching[-1].split()
    stamp = epoch(fields[-1])
    if stamp is None:
        return f"its last field is {fields[-1]!r}, not the epoch second it was written"
    if (time.time() if now is None else now) - stamp < 0:
        return ("it is stamped in the future — the stamp is when the deferral was WRITTEN "
                "(`$(date +%s)`), not when it should expire")
    return None
