"""The files the watcher and its readers leave for each other, and what each may conclude.

Three programs touch these. The watcher writes them; `scripts/pr/diagnostics/unsettled_pr.py` forgives a
pending check while one is alive; `.claude/hooks/refuse/edit_while_a_ready_pr_sits.py` refuses a
write while a ready pull request has sat. The format lives here because it was written in three
places and read back in two, under two different comments for the same 180 seconds.

One file runs the other way: a reader stamps `ASKED`, and the watcher reads it to find out whether
anything is still reading what it writes.

A heartbeat names the process that wrote it. Nothing stopped several watchers running at once, each
on its own poll cycle against one shared API quota and all writing this one file, so a fresh stamp
meant that one of them was alive and said nothing about which. `settle.py`'s `hold_the_watch` is what
stops the several; the pid here is what lets a reader ask whether the process that wrote the stamp
still exists. That is all it establishes — `running` answers about a process id, not about a watcher.
"""

import hashlib
import os
import shlex
import time
from pathlib import Path

HEARTBEAT = Path.home() / ".velvet-pr-watch.heartbeat"

# When each pull request first read as ready, so a guard can ask how long one has sat rather than
# whether one exists. Several are usually in flight here and one of them is usually green, so the
# existence of a ready pull request is the ordinary state and only its age is a defect.
READY_STATE = Path.home() / ".velvet-pr-ready"

LOCK = Path.home() / ".velvet-pr-watch.lock"

POLL_SECONDS = 60

# Three polls, so one slow `gh` call does not read as a dead watcher.
STALE_AFTER = 3 * POLL_SECONDS

# Written where a reader asks whether a watcher is alive, so the watcher can ask whether anything
# still reads what it writes. Not whether it is still owned: a watcher is one per machine and any
# session's guards may read it, so an orphan is unowned rather than unread.
ASKED = Path.home() / ".velvet-pr-watch.asked"

# A reader's cadence rather than the watcher's own poll, which is what makes this a different
# quantity from STALE_AFTER. An editing tool stamps; the turn boundary stamps only while some open
# pull request has a check still pending, since that is the branch of `stop/unsettled_pr.py` that
# asks whether a watcher is alive. So a session running commands with every pull request green
# stamps nothing, and that stretch is what this is sized against. Two hours, chosen past two
# measurements rather than by judgement: the 1800s wait for a quiet machine all three suite
# harnesses take, and the longest such stretch a week of sessions on this machine produced when this
# was chosen. Neither bounds one, and what a session gets back past one is the command a guard
# names.
RETIRE_AFTER = 120 * POLL_SECONDS

# Written by a `watch --until-actionable` run as it exits to report, naming its own pid. Its caller
# is woken by the exit, so between that exit and the re-arm nothing is polling, and `alive` would
# otherwise read the delivery itself as a dead watcher and refuse the edits that act on it.
HANDED_OFF = Path.home() / ".velvet-pr-watch.handed-off"

# A directory of records, one per caller, of the actionable lines a `watch --until-actionable` run
# has already exited to report, so a re-arm waits for something new rather than exiting at once over
# a pull request its caller already heard about. `settle.py` owns how long a record is carried.
REPORTED = Path.home() / ".velvet-pr-watch.reported"

# The actionable lines of the watcher's last poll, written by every watcher, so a
# `watch --until-actionable` run that finds the lock held follows the holder instead of refusing.
ACTIONABLE = Path.home() / ".velvet-pr-watch.actionable"


def reported_record(caller):
    """The record under `REPORTED` for `caller`, named by a digest so any caller string makes one
    file name."""
    return REPORTED / hashlib.sha256(caller.encode("utf-8")).hexdigest()[:16]


def rearm_command(root, caller=None):
    """The watcher in the form an agent runs it: backgrounded, it exits — waking its caller — once
    an open pull request is ready, is held only by worktrees, or has a failing check or failed run
    that `caller` has not already been told of. Without a caller, the run reports everything
    standing. It says where it runs, since `refuse/background_relative_path.py` refuses a
    backgrounded relative path."""
    command = f"cd {shlex.quote(str(root))} && python3 scripts/pr/settle.py watch --until-actionable"
    return command if caller is None else f"{command} --caller {shlex.quote(caller)}"


def beat(pid, now=None):
    """One heartbeat: when it was written, and by which process."""
    return f"{int(time.time() if now is None else now)} {pid}\n"


def written(text):
    """(stamp, pid) out of a heartbeat, or None when it carries neither."""
    fields = text.split()
    if len(fields) != 2 or not all(field.isdigit() for field in fields):
        return None
    return int(fields[0]), int(fields[1])


def running(pid):
    """Whether a process with that id exists. One this user may not signal counts as existing."""
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def stamp_age(text, now):
    """Seconds since the stamp in this text's first field, or None when it carries none.

    Bounded below: a stamp in the future — a millisecond epoch, a typo, a backward clock step —
    reads as the most recent one there could be, and so vouches for its writer permanently.
    """
    fields = text.split()
    if not fields or not fields[0].isdigit():
        return None
    age = now - int(fields[0])
    return age if age >= 0 else None


def stamped(text, now):
    """Seconds since a heartbeat was written, or None when it is not inside the poll window."""
    age = stamp_age(text, now)
    return None if age is None or age >= STALE_AFTER else age


def note_asked(now=None):
    """Record that something has just asked whether a watcher is alive.

    A failed write is swallowed. A home that cannot be written leaves the watcher retiring on its
    own clock, which it says out loud on the way, and a reader that failed to stamp still has its own
    answer to give.
    """
    try:
        ASKED.write_text(f"{int(time.time() if now is None else now)}\n", encoding="utf-8")
    except OSError:
        pass


def asked_ago(now=None):
    """Seconds since something last asked, or None when no stamp is readable.

    A file that is not text is one of the unreadable kinds rather than a reason to raise: this is
    read at the top of every poll, so raising here ends a watcher at its first, and the command a
    guard names as the way out of a refusal is the one that would keep failing.
    """
    try:
        text = ASKED.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None
    return stamp_age(text, time.time() if now is None else now)


def beat_text():
    """The heartbeat's text, or None when there is nothing readable to answer from.

    A file whose bytes are not UTF-8 raises `UnicodeDecodeError`, which is a `ValueError` and so goes
    straight past an `OSError` catch. Every reader below runs inside a `PreToolUse` or `Stop` hook,
    where a raise exits non-zero for a reason having nothing to do with what the guard judges — and
    the same shape had already killed `settle.py watch` at startup from a corrupt ready file.

    One reader rather than the catch repeated three times, because that is what left the widening to
    be done three times and done once.
    """
    try:
        return HEARTBEAT.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return None


def hand_off(pid, now=None):
    """Record that the watcher `pid` is exiting to report. A failed write is swallowed, as
    `note_asked` swallows one: the report still reaches the caller through the exit."""
    try:
        HANDED_OFF.write_text(beat(pid, now), encoding="utf-8")
    except OSError:
        pass


def handed_off(pid, now):
    """Whether `pid` recorded exiting to report inside the poll window."""
    try:
        text = HANDED_OFF.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return False
    read = written(text)
    return read is not None and read[1] == pid and stamped(text, now) is not None


def alive(now=None):
    """Whether the process that wrote the heartbeat is running and wrote it recently, or exited
    to report after writing it — the heartbeat's window bounds both.

    The asking is recorded here rather than in each reader, so a reader written later is counted
    without being told to.
    """
    note_asked(now)
    return beating(now)


def beating(now=None):
    """`alive` without recording the asking, for a follower: a stamp from it would hold off the
    retirement of the watcher it follows, and its own retirement reads the same stamp."""
    text = beat_text()
    if text is None:
        return False
    now = time.time() if now is None else now
    read = written(text)
    return (read is not None
            and stamped(text, now) is not None
            and (running(read[1]) or handed_off(read[1], now)))


def unreadable_beat(now=None):
    """Whether something inside the window is writing a heartbeat this cannot read.

    A watcher older than the pid field writes one field and nothing else, so a reader looking only at
    `alive` sees exactly what an absent file gives it and would call that "nothing is watching".
    Something is watching; what failed is the reading. Separating the two is what lets a guard name
    the recovery, which is to end that watcher rather than to start another.
    """
    text = beat_text()
    if text is None:
        return False
    return stamped(text, time.time() if now is None else now) is not None and written(text) is None


def beating_elsewhere(mine, now=None):
    """Whether a watcher this process is not is still writing the heartbeat.

    The lock cannot see a watcher launched from a checkout older than the lock itself: it takes no
    lock, and what it does leave is a heartbeat that names no process. So a nameless one inside the
    window is read as somebody still watching. A named one belongs to a watcher the lock already
    answers for, and is believed only while that process exists — otherwise a watcher restarted
    inside the window would refuse itself.
    """
    text = beat_text()
    if text is None:
        return False
    if stamped(text, time.time() if now is None else now) is None:
        return False
    read = written(text)
    return read is None or (read[1] != mine and running(read[1]))
