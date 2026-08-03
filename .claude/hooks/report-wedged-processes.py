#!/usr/bin/env python3
"""Report unreapable processes once they hold enough memory for a reboot to be worth it.

Gated on resident memory rather than on the count, and silent below the gate. No signal recovers one
of these, so the only response is a reboot, and a report that cannot be acted on except by rebooting
has to stay quiet until rebooting is the right call. The count goes in the report because it is what
a reader sees in `ps`, but it is not what decides.

Darwin only: the state letters this reads are that platform's.

Exit 0 always.
"""

import os
import platform
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

from wedged import report, summarize  # noqa: E402

DEFAULT_THRESHOLD_MB = 500


def threshold():
    declared = os.environ.get("VELVET_WEDGE_REPORT_MB", "")
    return int(declared) if declared.isdigit() else DEFAULT_THRESHOLD_MB


def main():
    if platform.system() != "Darwin" or shutil.which("ps") is None:
        return 0

    try:
        table = subprocess.run(
            ["ps", "ax", "-o", "stat=,rss=,comm="],
            capture_output=True, text=True, timeout=10,
        ).stdout
    except (OSError, subprocess.SubprocessError):
        return 0

    count, kilobytes, held = summarize(table.splitlines())
    if count == 0 or kilobytes / 1024 < threshold():
        return 0

    print(report(count, kilobytes, held))
    return 0


if __name__ == "__main__":
    sys.exit(main())
