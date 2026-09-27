import re

# Read by report/untracked_scratch.py and report/gitignored_write.py; one set, both sides.
BUILD_DIRECTORIES = re.compile(r"^(Library|Temp|obj|Logs)/")
