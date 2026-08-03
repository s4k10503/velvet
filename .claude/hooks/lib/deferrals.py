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
    stamp = fields[-1]
    if not stamp.isdigit():
        return None

    # Bounded below as well as above. A stamp in the future — a millisecond epoch, a typo, a backward
    # clock step — was live indefinitely, which is the permanent silence the expiry exists to prevent.
    age = (time.time() if now is None else now) - int(stamp)
    if age < 0 or age >= TTL:
        return None

    return " ".join(fields[1:-1]), int(age // 60)
