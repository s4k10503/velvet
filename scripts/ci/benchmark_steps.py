#!/usr/bin/env python3
"""Finds the benchmarks whose medians stepped up on main, and files the steps as GitHub issues.

Reads the history `.github/workflows/benchmarks.yml` appends to the `benchmark-data` branch
(`dev/bench/data.js`). CONTRIBUTING.md's benchmarks section owns the rule this applies and what it was
measured against.

    benchmark_steps.py HISTORY             the steps the history holds now
    benchmark_steps.py HISTORY --replay    each prefix of the history, read as it would have been then
    benchmark_steps.py HISTORY --issues    open or rewrite the issues, through `gh`
"""

import argparse
import json
import statistics
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime
from itertools import islice

SUITE = "Velvet benchmarks"
TIME_UNITS = {"ns", "μs", "ms", "s"}
COUNT_UNITS = {""}

# What benchmark_results.py writes into every value's `extra`.
PROCESSOR = "processor: "

LABELS = ("performance", "bug")
ASSIGNEE = "s4k10503"
MARKER = "<!-- benchmark-step: {} -->"
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
    last_fast: Run | None
    first_slow: Run
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


def times(after, before):
    return after / before if before else float("inf")


def slower(value, bar, rule):
    return value >= rule.threshold * bar and value >= bar + rule.floor


def judge(runs, points, start, baseline, settled, name, unit, rule):
    """The step whose first slow run is `points[start]`, or None where the runs from it are not one."""
    slowest = {key: max(values) for key, values in baseline.items()}
    after = list(islice((points[i] for i in range(start, len(points)) if points[i][1] in slowest), rule.window))
    if (len(after) < rule.min_slow_runs
            or len({key for _, key, _ in after}) < rule.min_processors
            or not all(slower(value, slowest[key], rule) for _, key, value in after)):
        return None
    medians = {key: statistics.median(values) for key, values in baseline.items()}
    last_fast = next((runs[points[i][0]] for i in reversed(range(start)) if settled[i]), None)
    per_processor = []
    for key in sorted({key for _, key, _ in after}):
        slow = [value for _, k, value in after if k == key]
        per_processor.append((key, medians[key], len(baseline[key]), statistics.median(slow), len(slow)))
    ratio = statistics.median(times(value, medians[key]) for _, key, value in after)
    return Step(name, unit, last_fast, runs[points[start][0]], ratio, tuple(per_processor))


def first_step(runs, name, unit, rule):
    points = [(index, "" if rule.pooled else run.processor, run.values[name]) for index, run in enumerate(runs)
              if name in run.values and (rule.pooled or (run.processor and run.values[name] > 0))]
    seen, settled = {}, []
    for start, (_, key, value) in enumerate(points):
        baseline = {k: v[-rule.baseline_runs:] for k, v in seen.items() if len(v) >= rule.min_baseline}
        if key in baseline:
            step = judge(runs, points, start, baseline, settled, name, unit, rule)
            if step is not None:
                return step
        # A step's range starts after the latest run that measured no higher than the slowest before it
        # on its processor. One above that and short of the threshold is left inside the range.
        settled.append(key in baseline and value <= max(baseline[key]))
        seen.setdefault(key, []).append(value)
    return None


def find_steps(runs, units, rule=RULE, counts=COUNTS):
    """Each benchmark's earliest step, over the runs in the order their commits landed."""
    landed = sorted(runs, key=lambda run: run.timestamp)
    steps = (first_step(landed, name, units.get(name, ""), rule if units.get(name) in TIME_UNITS else counts)
             for name in sorted({name for run in landed for name in run.values}))
    return [step for step in steps if step is not None]


def group(steps):
    """The steps keyed by the commit of their first slow run."""
    groups = {}
    for step in steps:
        groups.setdefault(step.first_slow.commit, []).append(step)
    return groups


def clusters(groups, runs):
    """The groups whose ranges share a commit, merged: one issue each.

    A group's range runs from just after the earliest last fast run among its steps to its first slow run.
    One commit can slow two benchmarks whose first slow runs differ, a run on another processor having
    measured one of them between the old level and the threshold, and their ranges then overlap."""
    order = {run.commit: index for index, run in enumerate(sorted(runs, key=lambda run: run.timestamp))}

    def span(steps):
        lows = [order[step.last_fast.commit] if step.last_fast else -1 for step in steps]
        return min(lows), order[steps[0].first_slow.commit]

    merged = []
    for commit, steps in sorted(groups.items(), key=lambda item: order[item[0]]):
        low, high = span(steps)
        if merged and low < merged[-1][1]:
            merged[-1] = (min(merged[-1][0], low), high, {**merged[-1][2], commit: steps})
        else:
            merged.append((low, high, {commit: steps}))
    return [members for _, _, members in merged]


def recent(runs, rule=RULE):
    """The commits of the latest `rule.fresh` runs appended: an issue opens only for a step starting at one."""
    return {run.commit for run in runs[-rule.fresh:]}


def short_name(name):
    return name.removeprefix("Velvet.Tests.Performance.")


def amount(value, unit):
    return f"{value:.4g} {unit}" if unit else f"{value:.4g}"


def ratio_text(after, before):
    return f"{after / before:.2f}x" if before else "from 0"


def render(members, repository):
    """The title and body of the issue for one cluster of steps."""
    steps = sorted((step for group_steps in members.values() for step in group_steps), key=lambda step: -step.ratio)
    first = max((step.first_slow for step in steps), key=lambda run: run.timestamp)
    lasts = [step.last_fast for step in steps if step.last_fast]
    last = min(lasts, key=lambda run: run.timestamp) if len(lasts) == len(steps) else None
    worst = steps[0]
    span = f"{last.commit[:8]}..{first.commit[:8]}" if last else f"the history up to {first.commit[:8]}"
    title = f"Benchmarks slowed down in {span}: {short_name(worst.benchmark)} {worst.ratio:.2f}x"
    if len(steps) > 1:
        title += f" and {len(steps) - 1} more"
    lines = [MARKER.format(commit) for commit in sorted(members)]
    lines += [
        (f"After `{last.commit[:8]}` ({last.subject}) and by `{first.commit[:8]}` ({first.subject}), "
         if last else f"By `{first.commit[:8]}` ({first.subject}), ")
        + f"{len(steps)} benchmark(s) measured slower than the runs before them, and stayed so in the runs "
        "after, up to eight: https://github.com/" + repository
        + (f"/compare/{last.commit}...{first.commit}" if last else f"/commit/{first.commit}"),
        "",
        "| Benchmark | First slow run | Processor | Before | After | Ratio |",
        "|---|---|---|---:|---:|---:|",
    ]
    for step in steps:
        for processor, before, before_runs, after, after_runs in step.per_processor:
            lines.append(f"| {short_name(step.benchmark)} | `{step.first_slow.commit[:8]}` | {processor or 'any'} "
                         f"| {amount(before, step.unit)} ({before_runs} runs) "
                         f"| {amount(after, step.unit)} ({after_runs} runs) | {ratio_text(after, before)} |")
    lines += ["", "Before and After are medians over the runs counted. `.github/workflows/benchmarks.yml` "
              "opened this and rewrites it while the step is in the history; CONTRIBUTING.md's benchmarks "
              "section says what counts as a step."]
    body = "\n".join(lines) + "\n"
    if len(body) > BODY_LIMIT:
        body = body[:body.rindex("\n", 0, BODY_LIMIT - len(CUT_NOTE))] + CUT_NOTE
    return title, body


def gh(args, stdin=None):
    return subprocess.run(["gh", *args], input=stdin, capture_output=True, text=True, check=True).stdout


def sync_issues(groups, runs, repository, run=gh, rule=RULE):
    """Rewrites the open issue carrying a cluster's marker, and opens one for a fresh cluster no issue carries.

    A cluster whose issue was closed is left alone: closing it is how a step is accepted or answered."""
    listed = json.loads(run(["issue", "list", "--repo", repository, "--label", LABELS[0], "--state", "all",
                             "--limit", "1000", "--json", "number,state,body"]))
    openable = recent(runs, rule)
    actions = []
    for members in clusters(groups, runs):
        markers = [MARKER.format(commit) for commit in members]
        matches = [issue for issue in listed if any(marker in (issue.get("body") or "") for marker in markers)]
        issue = next((issue for issue in matches if issue.get("state") == "OPEN"), None)
        title, body = render(members, repository)
        if not matches and openable & set(members):
            create = ["issue", "create", "--repo", repository, "--title", title, "--body-file", "-",
                      "--assignee", ASSIGNEE]
            for label in LABELS:
                create += ["--label", label]
            run(create, body)
            actions.append(f"opened an issue for {', '.join(commit[:8] for commit in members)}")
        elif issue is not None and issue.get("body") != body:
            run(["issue", "edit", str(issue["number"]), "--repo", repository, "--title", title,
                 "--body-file", "-"], body)
            actions.append(f"rewrote #{issue['number']}")
    return actions


def describe(members):
    lines = []
    for commit, steps in members.items():
        last = steps[0].last_fast
        lines.append(f"step {last.commit[:8] if last else '-'}..{commit[:8]}: {len(steps)} benchmark(s)")
        lines += [f"  {step.ratio:6.2f}x {short_name(step.benchmark)}" for step in sorted(steps, key=lambda s: -s.ratio)]
    return "\n".join(lines)


def replay(runs, units, rule=RULE, counts=COUNTS):
    """Each issue the history would have opened, keyed by its earliest first slow commit: the number of runs
    appended when it opened, and the steps it held then."""
    opened, known = {}, set()
    for end in range(1, len(runs) + 1):
        prefix = runs[:end]
        for members in clusters(group(find_steps(prefix, units, rule, counts)), prefix):
            if known & set(members):
                known |= set(members)
            elif recent(prefix, rule) & set(members):
                opened[next(iter(members))] = (end, members)
                known |= set(members)
    return opened


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
        for end, members in sorted(replay(runs, units).values(), key=lambda item: item[0]):
            print(f"after {end} runs ({runs[end - 1].commit[:8]}) it would have opened:\n{describe(members)}")
        return 0
    groups = group(find_steps(runs, units))
    print("\n".join(describe(members) for members in clusters(groups, runs)) or "no step")
    if args.issues:
        for action in sync_issues(groups, runs, args.repository):
            print(action)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
