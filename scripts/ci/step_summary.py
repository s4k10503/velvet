#!/usr/bin/env python3
"""Write a job-summary section whose size is bounded in code.

GitHub rejects a step's summary upload past 1 MiB and drops the whole of it, which happens to a
campaign exactly when it has the most to say. Every summary section the mutation and kill-set
workflows write goes through here, so the bound is enforced on the bytes written rather than on how
long the input happens to be.

    step_summary.py verdict VERDICT_FILE      counts, then unanswered survivors grouped by file
    step_summary.py block TITLE FILE [NOTE]   a titled code block over the head of FILE
    step_summary.py plain FILE                markdown already written, cut to the bound

The full text stays in the artifact the workflow uploads, which the closing line points to.
"""

import argparse
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import mutation_report  # noqa: E402

# Well under the 1 MiB the upload accepts, so that whatever else lands in the same step's summary fits.
LIMIT_BYTES = 512 * 1024

# What one file lists before it says how many more it has.
PER_FILE = 10

FENCE = "```"
POINTER = ("The full verdict is in the `Mutation campaign verdict` artifact of this run, and every "
           "unanswered survivor is an alert in Security > Code scanning (category `{}`).")
GATE = re.compile(r"^gate: .*$", re.MULTILINE)
SURVIVOR_VERDICT = re.compile(r"\)\s+\[(?P<verdict>[^\]]+)\]")
HEADING = re.compile(r"^--- (?P<title>.+?) ---$", re.MULTILINE)


def clamp(text, limit=LIMIT_BYTES, note="\n... cut to stay under the summary size limit"):
    """`text` unchanged where it fits `limit` bytes, otherwise its leading whole lines plus `note`."""
    if len(text.encode()) <= limit:
        return text
    room = max(limit - len(note.encode()), 0)
    head = text.encode()[:room].decode(errors="ignore")
    if "\n" in head:
        head = head[:head.rindex("\n")]
    return head + note


def block(title, text, limit=LIMIT_BYTES, pointer=""):
    """A heading and a fenced block over the head of `text`, `limit` bytes at most as a whole."""
    safe = text.replace(FENCE, "'''")
    frame = "### {}\n{}\n".format(title, FENCE)
    close = "\n{}\n{}".format(FENCE, pointer)
    room = limit - len((frame + close).encode()) - 200
    body = clamp(safe.rstrip("\n"), room, "\n... cut here, the rest is in the uploaded artifact")
    return clamp(frame + body + close, limit)


def survivor_counts(text):
    counts = {}
    for found in SURVIVOR_VERDICT.finditer(text):
        counts[found["verdict"]] = counts.get(found["verdict"], 0) + 1
    return counts


def verdict_summary(text, limit=LIMIT_BYTES, per_file=PER_FILE):
    """Counts by verdict, then the unanswered survivors by file, within `limit` bytes."""
    unanswered, stale, _, unread = mutation_report.sections(text)
    body = ["### Mutation campaign", ""]
    gate = GATE.search(text)
    if gate:
        body += [gate.group(0), ""]
    counts = survivor_counts(text)
    if counts:
        body += ["Mutants no test killed, by verdict:"]
        body += ["- {}: {}".format(name, number) for name, number in sorted(counts.items())]
        body += [""]
    headings = [found["title"] for found in HEADING.finditer(text)
                if found["title"] != "mutants no test killed" and "cases each mutant" not in found["title"]]
    if headings:
        body += ["Other sections of the verdict:"] + ["- {}".format(name.replace("`", "'")) for name in headings]
        body += [""]
    body += ["{} unanswered survivor(s), {} stale declaration(s), {} line(s) that could not be read."
             .format(len(unanswered), len(stale), len(unread))]
    by_file = {}
    for finding in unanswered:
        by_file.setdefault(finding["path"], []).append(finding)
    for path in sorted(by_file):
        found = sorted(by_file[path], key=lambda item: item["line"])
        body += ["", "**{}** ({})".format(path.replace("`", "'"), len(found))]
        for item in found[:per_file]:
            body += ["- line {}: `{}` -> `{}` ({})".format(
                item["line"], item["before"].replace("`", "'"), item["after"].replace("`", "'") or "(removed)",
                item["operator"])]
        if len(found) > per_file:
            body += ["- and {} more in this file".format(len(found) - per_file)]
    body += ["", POINTER.format(mutation_report.CATEGORY)]
    pointer = "\n".join(body[-1:])
    kept = clamp("\n".join(body[:-1]), limit - len(pointer.encode()) - 4)
    return kept + "\n\n" + pointer + "\n"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="mode", required=True)
    sub.add_parser("verdict").add_argument("file")
    sub.add_parser("plain").add_argument("file")
    one = sub.add_parser("block")
    one.add_argument("title")
    one.add_argument("file")
    one.add_argument("note", nargs="?", default="")
    args = parser.parse_args(argv)
    text = Path(args.file).read_text(errors="replace")
    if args.mode == "verdict":
        sys.stdout.write(verdict_summary(text))
    elif args.mode == "plain":
        print(clamp(text))
    else:
        print(block(args.title, text, pointer=args.note))
    return 0


if __name__ == "__main__":
    sys.exit(main())
