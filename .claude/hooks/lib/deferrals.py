"""A merge or a pause held on purpose, and the expiry that keeps it from becoming silence.

A review in flight, a dependency on another change, a user waiting on an answer — none of these is
the failure the Stop guards exist for, but "I am waiting" is exactly what a nine-hour stall said too.
So a deferral is accepted and EXPIRES: it names what is held, states what clears it, and stops
counting after TTL. That cannot decay into permanent silence the way an unqualified exemption did,
and re-stating it is the moment the reason gets re-examined, which is the only part that ever
mattered.

    echo "236 waiting on round-4 review $(date +%s)" >> ~/.velvet-pr-deferrals

Both Stop guards read this one file under one expiry, so a held item is re-read in one place and a
change to the format cannot land in one guard and not the other.
"""

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
    stamp = epoch(fields[-1])
    if stamp is None:
        return None

    # Bounded below as well as above. A stamp in the future — a millisecond epoch, a typo, a backward
    # clock step — was live indefinitely, which is the permanent silence the expiry exists to prevent.
    age = (time.time() if now is None else now) - stamp
    if age < 0 or age >= TTL:
        return None

    return " ".join(fields[1:-1]), int(age // 60)


def unusable(key, now=None):
    """Return why the newest entry for `key` cannot be honoured, or None — which covers both a live
    entry and no entry at all. Ask `deferred` which of those it is; this one answers only "written,
    and rejected".

    A rejected deferral and an absent one both make `deferred` return None, so writing an unusable
    one reads as having written nothing — the guard fires again with the same text and the entry
    that was supposed to answer it is never mentioned. The stamp is the moment the deferral was
    WRITTEN, and `date +%s` in the guidance is easy to read as the moment it should expire; an
    entry stamped in the future is rejected by the lower bound and says nothing about why.

    Expiry is not reported: that one is the design working, and it already ends with the guard
    firing and the reason being re-stated.
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
    age = (time.time() if now is None else now) - stamp
    if age < 0:
        return ("it is stamped in the future — the stamp is when the deferral was WRITTEN "
                "(`$(date +%s)`), not when it should expire")
    if age >= TTL:
        return (f"it was already {int(age // 60)}m old when it arrived, past the {TTL // 60}m expiry — "
                "a stamp copied from an earlier message is stale by the time it is pasted")
    return None
