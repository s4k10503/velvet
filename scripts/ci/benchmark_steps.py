#!/usr/bin/env python3
"""Finds the benchmarks whose medians stepped up on main, and files the steps as GitHub issues.

Reads the history `.github/workflows/benchmarks.yml` appends to the `benchmark-data` branch
(`dev/bench/data.js`). CONTRIBUTING.md's benchmarks section owns the rule this applies.

    benchmark_steps.py HISTORY             the steps the history holds now
    benchmark_steps.py HISTORY --replay    every prefix of the history synced in turn, against issues kept in memory
    benchmark_steps.py HISTORY --issues    open or rewrite the issues, through `gh`
"""

import argparse
import json
import re
import statistics
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime

SUITE = "Velvet benchmarks"
TIME_UNITS = {"ns", "μs", "ms", "s"}
COUNT_UNITS = {""}

# What benchmark_results.py writes into every value's `extra`.
PROCESSOR = "processor: "

LABELS = ("performance", "bug")
ASSIGNEE = "s4k10503"
# Who opens the issues the job writes, as the REST listing names it.
AUTHOR = "github-actions[bot]"
MARKER = "<!-- benchmark-step: {series} | {first} -->"
MARKER_PATTERN = re.compile(r"^<!-- benchmark-step: (?P<series>.+?) \| (?P<first>[0-9a-f]{40}) -->$", re.M)
BODY_LIMIT = 60000
CUT_NOTE = "\n\n... cut to fit an issue body\n"


@dataclass(frozen=True)
class Rule:
    threshold: float
    min_processors: int
    pooled: bool = False
    floor: float = 0
    baseline_runs: int = 5
    min_baseline: int = 3
    min_slow_runs: int = 4
    window: int = 8
    fresh: int = 12


RULE = Rule(threshold=1.2, min_processors=2)
COUNTS = Rule(threshold=1.2, min_processors=1, pooled=True, floor=2)


@dataclass(frozen=True)
class Run:
    commit: str
    timestamp: datetime
    subject: str
    processor: str
    values: dict


@dataclass(frozen=True)
class Step:
    benchmark: str
    unit: str
    since: Run
    last_fast: Run | None
    first_slow: Run
    decided: Run
    ratio: float
    # (processor, before median, runs before, after median, runs after), one per processor after it.
    per_processor: tuple


def processor_of(bench):
    for line in (bench.get("extra") or "").splitlines():
        if line.startswith(PROCESSOR):
            return line[len(PROCESSOR):].strip()
    return ""


def read_history(text):
    """The suite's runs in the order they were appended, times and allocation counts, and each series' unit."""
    data = json.loads(text[text.index("{"):].rstrip().rstrip(";"))
    runs, units = [], {}
    for entry in data.get("entries", {}).get(SUITE, []):
        values, processors = {}, set()
        for bench in entry.get("benches", []):
            if bench.get("unit") not in TIME_UNITS | COUNT_UNITS:
                continue
            values[bench["name"]] = bench["value"]
            units.setdefault(bench["name"], bench["unit"])
            processors.add(processor_of(bench))
        commit = entry["commit"]
        runs.append(Run(
            commit=commit["id"],
            timestamp=datetime.fromisoformat(commit["timestamp"].replace("Z", "+00:00")),
            subject=(commit.get("message") or "").partition("\n")[0],
            processor=processors.pop() if len(processors) == 1 else "",
            values=values))
    return runs, units


def landed(runs):
    return sorted(runs, key=lambda run: run.timestamp)


def times(after, before):
    return after / before if before else float("inf")


def slower(value, bar, rule):
    return value >= rule.threshold * bar and value >= bar + rule.floor


def judge(runs, points, start, baseline, settled, since, name, unit, rule):
    """The step whose first slow run is `points[start]`, or None where the runs from it are not one, or not yet.

    The decision is taken on the fewest runs from the start that the rule asks for, so a run landing after
    them cannot withdraw a step an issue has already been opened for. Where they are fewer than the window,
    the runs after them up to it only add to the medians shown."""
    slowest = {key: max(values) for key, values in baseline.items()}
    after, keys, decided = [], set(), None
    for i in range(start, len(points)):
        _, key, value = points[i]
        if key not in slowest:
            continue
        if decided is None and not slower(value, slowest[key], rule):
            return None
        after.append(points[i])
        keys.add(key)
        if decided is None and len(after) >= rule.min_slow_runs and len(keys) >= rule.min_processors:
            decided = len(after)
        if decided is not None and len(after) >= rule.window:
            break
    if decided is None:
        return None
    medians = {key: statistics.median(values) for key, values in baseline.items()}
    last_fast = next((runs[points[i][0]] for i in reversed(range(start)) if settled[i]), None)
    per_processor = []
    for key in sorted({key for _, key, _ in after}):
        slow = [value for _, k, value in after if k == key]
        per_processor.append((key, medians[key], len(baseline[key]), statistics.median(slow), len(slow)))
    ratio = statistics.median(times(value, medians[key]) for _, key, value in after)
    return Step(name, unit, since, last_fast, runs[points[start][0]], runs[after[decided - 1][0]], ratio,
                tuple(per_processor))


def series_steps(runs, name, unit, rule):
    points = [(index, "" if rule.pooled else run.processor, run.values[name]) for index, run in enumerate(runs)
              if name in run.values and (rule.pooled or (run.processor and run.values[name] > 0))]
    if not points:
        return []
    since = runs[points[0][0]]
    seen, latest, settled, steps, era = {}, {}, [], [], None
    for start, (_, key, value) in enumerate(points):
        # After a step, a processor is judged again only once it has run since: with only runs from
        # before the step to compare with, it would read the step itself as a further one.
        baseline = {k: v[-rule.baseline_runs:] for k, v in seen.items()
                    if len(v) >= rule.min_baseline and (era is None or latest[k] >= era)}
        if key in baseline:
            step = judge(runs, points, start, baseline, settled, since, name, unit, rule)
            if step is not None:
                steps.append(step)
                era = start
        # A step's range starts after the latest run that was judged here and measured no higher than the
        # slowest before it on its processor. One above that and short of the threshold, or one its
        # processor was not judged at, is left inside the range.
        settled.append(key in baseline and value <= max(baseline[key]))
        seen.setdefault(key, []).append(value)
        latest[key] = start
    return steps


def find_steps(runs, units, rule=RULE, counts=COUNTS):
    """Every step in each benchmark's history, over the runs in the order their commits landed."""
    runs = landed(runs)
    return [step for name in sorted({name for run in runs for name in run.values})
            for step in series_steps(runs, name, units.get(name, ""),
                                     rule if units.get(name) in TIME_UNITS else counts)]


def span(step, order):
    """The positions (low, high] a step's range holds: after its last fast run, or from its benchmark's first
    run where none came before it, up to and including its first slow run."""
    low = order[step.last_fast.commit] if step.last_fast else order[step.since.commit] - 1
    return low, order[step.first_slow.commit]


def clusters(steps, runs):
    """The steps whose ranges overlap, merged."""
    order = {run.commit: index for index, run in enumerate(landed(runs))}
    merged = []
    for step in sorted(steps, key=lambda step: span(step, order)):
        low, high = span(step, order)
        if merged and low < merged[-1][1]:
            merged[-1][1] = max(merged[-1][1], high)
            merged[-1][2].append(step)
        else:
            merged.append([low, high, [step]])
    return [members for _, _, members in merged]


def recent(runs, rule=RULE):
    """The commits of the latest `rule.fresh` runs appended: an issue opens only for a step decided at one."""
    return {run.commit for run in runs[-rule.fresh:]}


def key(step):
    return step.benchmark, step.first_slow.commit


def marker(step):
    return MARKER.format(series=step.benchmark, first=step.first_slow.commit)


def short_name(name):
    return name.removeprefix("Velvet.Tests.Performance.")


def amount(value, unit):
    return f"{value:.4g} {unit}" if unit else f"{value:.4g}"


def ratio_text(ratio):
    return f"{ratio:.2f}x" if ratio != float("inf") else "from 0"


def quoted(subject):
    return "`" + subject.replace("`", "'") + "`"


def render(steps, repository):
    """The title and body of the issue for some steps."""
    steps = sorted(steps, key=lambda step: (-step.ratio, step.benchmark, step.first_slow.timestamp))
    first = max((step.first_slow for step in steps), key=lambda run: run.timestamp)
    lasts = [step.last_fast for step in steps]
    last = min(lasts, key=lambda run: run.timestamp) if all(lasts) else None
    worst = steps[0]
    span_text = f"{last.commit[:8]}..{first.commit[:8]}" if last else f"the history up to {first.commit[:8]}"
    title = f"Benchmarks slowed down in {span_text}: {short_name(worst.benchmark)} {ratio_text(worst.ratio)}"
    if len(steps) > 1:
        title += f" and {len(steps) - 1} more"
    lines = sorted({marker(step) for step in steps})
    lines += [
        (f"After `{last.commit[:8]}` ({quoted(last.subject)}) and by `{first.commit[:8]}` ({quoted(first.subject)}), "
         if last else f"By `{first.commit[:8]}` ({quoted(first.subject)}), ")
        + f"{len(steps)} step(s) measured slower than the runs before them: https://github.com/" + repository
        + (f"/compare/{last.commit}...{first.commit}" if last else f"/commit/{first.commit}"),
        "",
        "| Benchmark | First slow run | Processor | Before | After | Ratio |",
        "|---|---|---|---:|---:|---:|",
    ]
    for step in steps:
        for processor, before, before_runs, after, after_runs in step.per_processor:
            lines.append(f"| {short_name(step.benchmark)} | `{step.first_slow.commit[:8]}` | {processor or 'any'} "
                         f"| {amount(before, step.unit)} ({before_runs} runs) "
                         f"| {amount(after, step.unit)} ({after_runs} runs) | {ratio_text(times(after, before))} |")
    lines += ["", "Before and After are medians over the runs counted. `.github/workflows/benchmarks.yml` "
              "opened this and rewrites it while it is open, nobody has reopened it and the history holds "
              "these steps; "
              "CONTRIBUTING.md's benchmarks section says what counts as a step."]
    body = "\n".join(lines) + "\n"
    if len(body) > BODY_LIMIT:
        body = body[:body.rindex("\n", 0, BODY_LIMIT - len(CUT_NOTE))] + CUT_NOTE
    return title, body


def gh(args, stdin=None):
    return subprocess.run(["gh", *args], input=stdin, capture_output=True, text=True, check=True).stdout


def list_issues(repository, run):
    """Every issue carrying the first label, open or closed, read through the REST issues endpoint rather than
    `gh issue list`, whose label filter goes through search."""
    out = run(["api", "--paginate", f"repos/{repository}/issues?labels={LABELS[0]}&state=all&per_page=100",
               "--jq", ".[] | select(.pull_request == null) | {number, state, state_reason, body, user: .user.login}"])
    return [json.loads(line) for line in out.splitlines() if line.strip()]


def is_open(issue):
    return str(issue.get("state", "")).lower() == "open"


def is_reopened(issue):
    return is_open(issue) and issue.get("state_reason") == "reopened"


def sync_issues(steps, runs, repository, run=gh, rule=RULE):
    """Rewrites the open issues this job opened, and opens one for the fresh steps no issue names.

    An issue names a step by its marker alone: the benchmark and the first slow commit, whoever opened it.
    One this job opened is rewritten only while it is open, nobody has reopened it, and the history holds
    every step it names; nothing here closes an issue. A step decided at one of the latest runs that no
    issue names, in any state, is new, and new steps whose ranges share a commit go into one new issue."""
    issues = list_issues(repository, run)
    by_key = {key(step): step for step in steps}
    named, actions = set(), []
    for issue in issues:
        keys = {(match["series"], match["first"]) for match in MARKER_PATTERN.finditer(issue.get("body") or "")}
        named |= keys
        if (not keys or issue.get("user") != AUTHOR or not is_open(issue) or is_reopened(issue)
                or not keys <= by_key.keys()):
            continue
        title, body = render([by_key[k] for k in keys], repository)
        if issue.get("body") != body:
            run(["issue", "edit", str(issue["number"]), "--repo", repository, "--title", title, "--body-file", "-"],
                body)
            actions.append(f"rewrote #{issue['number']}")
    openable = recent(runs, rule)
    new = [step for step in steps if key(step) not in named and step.decided.commit in openable]
    for members in clusters(new, runs):
        title, body = render(members, repository)
        create = ["issue", "create", "--repo", repository, "--title", title, "--body-file", "-",
                  "--assignee", ASSIGNEE]
        for label in LABELS:
            create += ["--label", label]
        run(create, body)
        actions.append(f"opened an issue for {', '.join(sorted({s.first_slow.commit[:8] for s in members}))}")
    return actions


class MemoryIssues:
    """The issues `sync_issues` reads and writes, held in memory: what `--replay` syncs against."""

    def __init__(self):
        self.issues, self.log, self.now = [], [], 0

    def __call__(self, args, stdin=None):
        if args[0] == "api":
            return "".join(json.dumps({key: issue.get(key) for key in ("number", "state", "state_reason", "body",
                                                                       "user")})
                           + "\n" for issue in self.issues)
        if args[:2] == ["issue", "create"]:
            self.issues.append({"number": len(self.issues) + 1, "state": "open", "body": stdin, "user": AUTHOR,
                                "title": args[args.index("--title") + 1], "opened": self.now})
            self.log.append((self.now, "opened", len(self.issues)))
        elif args[:2] == ["issue", "edit"]:
            issue = self.issues[int(args[2]) - 1]
            issue["body"] = stdin
            issue["title"] = args[args.index("--title") + 1]
            self.log.append((self.now, "rewrote", issue["number"]))
        return ""


def replay(runs, units, rule=RULE, counts=COUNTS):
    """Every prefix of the history synced in turn, as the job would have synced it, against issues in memory."""
    issues = MemoryIssues()
    for end in range(1, len(runs) + 1):
        issues.now = end
        prefix = runs[:end]
        sync_issues(find_steps(prefix, units, rule, counts), prefix, "replay/replay", issues, rule)
    return issues


def describe(steps):
    return "\n".join(f"  {ratio_text(step.ratio):>8} {short_name(step.benchmark)} at {step.first_slow.commit[:8]}"
                     for step in sorted(steps, key=lambda step: -step.ratio))


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("history")
    parser.add_argument("--replay", action="store_true")
    parser.add_argument("--issues", action="store_true")
    parser.add_argument("--repository", default="s4k10503/velvet")
    args = parser.parse_args(argv)

    with open(args.history, encoding="utf-8") as handle:
        runs, units = read_history(handle.read())
    if args.replay:
        issues = replay(runs, units)
        for issue in issues.issues:
            rewrites = sum(1 for _, action, number in issues.log if action == "rewrote" and number == issue["number"])
            print(f"#{issue['number']} opened after {issue['opened']} runs ({runs[issue['opened'] - 1].commit[:8]}), "
                  f"rewritten {rewrites} times: {issue['title']}")
        return 0
    steps = find_steps(runs, units)
    print("\n\n".join(describe(members) for members in clusters(steps, runs)) or "no step")
    if args.issues:
        for action in sync_issues(steps, runs, args.repository):
            print(action)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
