"""Spelling a path into a line a reader reads whole.

`repository.printable` answers the other half of that question — what the stream's encoding cannot
carry. The two are separate because they are separately needed: `report/untracked_scratch.py` takes
this one alone, `refuse/commit_failing_fast_checks.py` both.
"""

import json


def displayed(path):
    """`path` quoted and escaped where it would not stay on one line.

    Every line of a report is read whole — an entry, the headline, the sentence a listing hangs
    under — and a path carrying a line break of its own divides the line it lands in.
    """
    return path if path.splitlines() == [path] else json.dumps(path)
