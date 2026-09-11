import os
import re

# Written by refuse/branch_from_unmerged.py and read by refuse/stale_merge.py; one name, both sides.
BRANCH_BASES = os.path.join(os.path.expanduser("~"), ".velvet-branch-bases")

# Where an ignored file is ignored on purpose: build output, and Logs, which is where a worktree's
# scratch goes. Read by report/untracked_scratch.py and report/gitignored_write.py; one set, both sides.
BUILD_DIRECTORIES = re.compile(r"^(Library|Temp|obj|Logs)/")
