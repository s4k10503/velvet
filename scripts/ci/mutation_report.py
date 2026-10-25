#!/usr/bin/env python3
"""Report what a record run of the mutation campaign left to answer, as a SARIF file for code scanning.

`.github/workflows/mutation.yml` runs this after every run that writes the record (`mutation_record.py`):
a push to the default branch, the nightly schedule, a catch-up run and a dispatch. CONTRIBUTING.md ▸
Checking that the tests can fail owns what those runs measure; this reads the verdict job's output and
every job's result. Each unanswered survivor and each stale declaration becomes one SARIF result at its
file and line, which the workflow uploads under the category `CATEGORY`. Code scanning keeps an alert
per `partialFingerprints` value across uploads and closes the alerts an upload no longer carries, so a
run with nothing to answer writes a file whose `results` is empty.

Without a verdict job's output nothing is known about the survivors, and an empty upload would close
every alert: no file is written then. Mutants nothing measured, mutants past the run's cap and jobs that
did not pass have no alert to hold them and go to the text printed on stdout, which the workflow appends
to the job summary. Mutants past the cap owe nothing: a later run measures them, and the record says
which.

    python3 scripts/ci/mutation_report.py [--verdict verdict.txt] [--sarif mutation.sarif]
"""

import argparse
import hashlib
import json
import os
import re
import sys
from pathlib import Path

CATEGORY = "mutation-campaign"
SARIF_SCHEMA = "https://json.schemastore.org/sarif-2.1.0.json"
SARIF_VERSION = "2.1.0"
FINGERPRINT = "mutationSurvivor/v1"

# What one list in the summary shows before it says how many more there are.
LIST_LIMIT = 200

PASSING = frozenset({"success", "skipped"})

STALE_RULE = "stale-declaration"

# One rule for each operator `mutation_check.OPERATORS`, `WORD_OPERATORS` and the removal operators
# name, with the reading a person needs to find what to write. `OperatorRuleTests` holds this set to
# those names.
OPERATOR_RULES = {
    "boundary": "A comparison's boundary was flipped (`<=` for `<`, and so on) and no test failed.",
    "equality": "An equality was inverted (`==` for `!=`) and no test failed.",
    "logic": "A logical operator was swapped (`&&` for `||`) and no test failed.",
    "arithmetic": "An arithmetic operator was inverted (`+` for `-`) and no test failed.",
    "literal": "A boolean literal was inverted and no test failed.",
    "clause removed": "A clause of a condition was removed and no test failed.",
    "guard removed": "A guard statement was removed and no test failed.",
    "line removed": "A statement was removed and no test failed.",
}

# `Mutant.describe`: `path:line before -> after (operator)`, with the operators' own padding spaces.
MUTANT = re.compile(r"^(?P<path>.+?):(?P<line>\d+) (?P<before>.*?) -> (?P<after>.*?) \((?P<operator>[^()]+)\)$")
DECLARATION = re.compile(r"^(?P<path>.+?):(?P<line>\d+) declares a survivor, and line (?P<subject>\d+) has none$")


def rule_id(operator):
    return operator.replace(" ", "-")


def sections(text):
    """(unanswered, stale, unmeasured, unread) out of a verdict job's output.

    The first two are lists of dicts holding `path`, `line` and the rest of what their line carries.
    A finding whose line does not read as one goes to `unread` whole, so the summary shows it rather
    than the upload silently losing it.
    """
    lines = text.splitlines()
    unanswered, stale, unmeasured, unread = [], [], [], []
    heading = None
    for index, line in enumerate(lines):
        if line.startswith("UNANSWERED  "):
            described = line[len("UNANSWERED  "):].strip()
            following = lines[index + 1].strip() if index + 1 < len(lines) else ""
            found = MUTANT.match(described)
            if found and found["operator"] in OPERATOR_RULES:
                unanswered.append({"path": found["path"], "line": int(found["line"]),
                                   "before": found["before"].strip(), "after": found["after"].strip(),
                                   "operator": found["operator"], "reason": following})
            else:
                unread.append("UNANSWERED {} — {}".format(described, following))
        elif line.startswith("STALE "):
            described = line[len("STALE "):].strip()
            found = DECLARATION.match(described)
            if found:
                stale.append({"path": found["path"], "line": int(found["line"]),
                              "subject": int(found["subject"])})
            else:
                unread.append("STALE {}".format(described))
        elif line.startswith("--- mutants nothing was asked of the suite about ---") or (
                line.startswith("--- not measured:")):
            heading = line
            unmeasured.append(line.strip("- "))
        elif heading is not None and line.strip():
            unmeasured.append(line.strip())
        elif heading is not None:
            heading = None
        if "further mutant(s) were never run" in line:
            unmeasured.append(line.strip())
    return unanswered, stale, unmeasured, unread


# `mutation_check.TIMED_OUT`'s heading in the verdict. These are kills, so they own no alert; the summary
# names them so that a kill no case stands behind is not read as one.
TIMED_OUT_HEADING = "--- mutants killed by timing out the suite ---"


def timed_out(text):
    """The lines the verdict lists under `TIMED_OUT_HEADING`."""
    found = []
    listing = False
    for line in text.splitlines():
        if line.startswith(TIMED_OUT_HEADING):
            listing = True
        elif listing and line.strip():
            found.append(line.strip())
        else:
            listing = False
    return found


def fingerprint(*parts):
    return hashlib.sha256("\n".join(parts).encode()).hexdigest()


def sarif_result(rule, path, line, text, key):
    return {
        "ruleId": rule,
        "level": "warning",
        "message": {"text": text},
        "locations": [{"physicalLocation": {"artifactLocation": {"uri": path, "uriBaseId": "%SRCROOT%"},
                                           "region": {"startLine": line}}}],
        "partialFingerprints": {FINGERPRINT: key},
    }


def results_of(unanswered, stale):
    """The SARIF results, in file and line order.

    A fingerprint hashes the file, the rule and the mutation's own text, never a line number, so the
    alert survives edits above it. Two findings of one file with the same text (two `<=` flips) differ
    only by their order, which `occurrence` counts.
    """
    results = []
    seen = {}
    for finding in sorted(unanswered, key=lambda item: (item["path"], item["line"])):
        identity = (finding["path"], finding["operator"], finding["before"], finding["after"])
        seen[identity] = seen.get(identity, 0) + 1
        key = fingerprint(*identity, str(seen[identity]))
        shown = "`{}` -> `{}`".format(finding["before"], finding["after"] or "(removed)")
        results.append(sarif_result(
            rule_id(finding["operator"]), finding["path"], finding["line"],
            "Mutation {} ({}) survives and nothing answers for it: {}".format(
                shown, finding["operator"], finding["reason"]), key))
    for finding in sorted(stale, key=lambda item: (item["path"], item["line"])):
        identity = (finding["path"], STALE_RULE)
        seen[identity] = seen.get(identity, 0) + 1
        key = fingerprint(*identity, str(seen[identity]))
        results.append(sarif_result(
            STALE_RULE, finding["path"], finding["line"],
            "This MUTANT_SURVIVES declaration answers for line {}, which has no surviving "
            "mutant. Remove it.".format(finding["subject"]), key))
    return results


def build_sarif(unanswered, stale):
    rules = [{"id": rule_id(operator), "name": "Unanswered {} mutation".format(operator),
              "shortDescription": {"text": "Unanswered {} mutation".format(operator)},
              "fullDescription": {"text": text},
              "helpUri": "https://github.com/s4k10503/velvet/blob/main/CONTRIBUTING.md"
                         "#checking-that-the-tests-can-fail"}
             for operator, text in OPERATOR_RULES.items()]
    rules.append({"id": STALE_RULE, "name": "Stale survivor declaration",
                  "shortDescription": {"text": "Stale survivor declaration"},
                  "fullDescription": {"text": "A MUTANT_SURVIVES declaration sits over a line with no "
                                              "surviving mutant."},
                  "helpUri": "https://github.com/s4k10503/velvet/blob/main/CONTRIBUTING.md"
                             "#checking-that-the-tests-can-fail"})
    return {"$schema": SARIF_SCHEMA, "version": SARIF_VERSION,
            "runs": [{"tool": {"driver": {"name": "Velvet mutation campaign", "rules": rules}},
                      "results": results_of(unanswered, stale)}]}


def listed(lines):
    shown = ["- `{}`".format(line.replace("`", "'")) for line in lines[:LIST_LIMIT]]
    if len(lines) > LIST_LIMIT:
        shown.append("- and {} more".format(len(lines) - LIST_LIMIT))
    return shown


def summary(verdict, results, pending, run_url, uploaded):
    """The job summary a run owes, or None where nothing is left outside code scanning.

    `uploaded` is (unanswered, stale) counts where a SARIF file is written and None where it is not.
    """
    failed = ["{} concluded {}".format(job, result) for job, result in results if result not in PASSING]
    _, _, unmeasured, unread = sections(verdict) if verdict is not None else ([], [], [], [])
    owing = [("Not measured", unmeasured), ("Findings this report could not read", unread),
             ("Jobs that did not pass", failed)]
    body = ["## Mutation campaign", ""]
    if uploaded is None:
        body += ["No verdict was produced, so nothing was uploaded to code scanning and its alerts "
                 "are unchanged."]
    else:
        body += ["{} unanswered survivor(s) and {} stale declaration(s) are in the SARIF upload "
                 "(category `{}`).".format(uploaded[0], uploaded[1], CATEGORY)]
    if pending:
        body += ["", "{} mutant(s) past this run's cap are left to the next run.".format(pending)]
    for heading, lines in owing:
        if lines:
            body += ["", "### {}".format(heading)] + listed(lines)
    stopped = timed_out(verdict) if verdict is not None else []
    if stopped:
        body += ["", "### Killed by timeout (counted as detected, no case named)"] + listed(stopped)
    body += ["", "Run: {}".format(run_url)]
    return "\n".join(body)


def results_from(text):
    """[(job, result)] from `job=result` words."""
    return [tuple(word.split("=", 1)) for word in text.split() if "=" in word]


def main(argv=None, environ=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--verdict")
    parser.add_argument("--sarif", default="mutation.sarif")
    args = parser.parse_args(argv)
    environ = os.environ if environ is None else environ
    verdict_path = Path(args.verdict) if args.verdict else None
    verdict = verdict_path.read_text() if verdict_path and verdict_path.is_file() else None
    run_url = "{}/{}/actions/runs/{}".format(environ.get("GITHUB_SERVER_URL", "https://github.com"),
                                             environ.get("GITHUB_REPOSITORY", ""),
                                             environ.get("GITHUB_RUN_ID", ""))
    uploaded = None
    if verdict is not None:
        unanswered, stale = sections(verdict)[:2]
        Path(args.sarif).write_text(json.dumps(build_sarif(unanswered, stale), indent=2) + "\n")
        uploaded = (len(unanswered), len(stale))
    print(summary(verdict, results_from(environ.get("RESULTS", "")), int(environ.get("PENDING") or 0),
                  run_url, uploaded))
    return 0


if __name__ == "__main__":
    sys.exit(main())
