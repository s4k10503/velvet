#!/usr/bin/env python3
"""Finds the benchmarks whose medians stepped up or drifted up on main, and files them as GitHub issues.

Reads the history `.github/workflows/benchmarks.yml` appends to the `benchmark-data` branch
(`dev/bench/data.js`). CONTRIBUTING.md's benchmarks section owns the rules this applies.

    benchmark_steps.py HISTORY             the steps and drifts the history holds now
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
from datetime import datetime, timedelta

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
DRIFT_MARKER = "<!-- benchmark-drift: {series} | {decided} -->"
DRIFT_MARKER_PATTERN = re.compile(r"^<!-- benchmark-drift: (?P<series>.+?) \| (?P<decided>[0-9a-f]{40}) -->$", re.M)
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
class DriftRule:
    threshold: float = 1.3
    min_processors: int = 2
    runs: int = 5
    days: int = 30
    min_days: int = 14
    held: float = 1.1


DRIFT = DriftRule()


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


@dataclass(frozen=True)
class Drift:
    benchmark: str
    unit: str
    earliest: Run
    decided: Run
    ratio: float
    # (processor, its first run compared, earlier median, latest median, ratio), one per processor judged.
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


def held(points, before, rule):
    """Whether a step still holds on one processor: no five of its runs since the step, `points`, have in a row
    a median under `rule.held` times the median of the five `before` it. With fewer than five on either side,
    it holds."""
    if len(before) < rule.runs or len(points) < rule.runs:
        return True
    bar = rule.held * statistics.median(before[-rule.runs:])
    return all(statistics.median(points[index:index + rule.runs]) >= bar
               for index in range(len(points) - rule.runs + 1))


def series_drift(runs, name, unit, start, cuts, rule, jump):
    """The drift of one time benchmark over `runs[start:]`, landed in order, measured on each processor within
    the stretches between the `cuts`, positions of steps' first slow runs, that still hold there, or None. A
    processor whose latest five runs measure `jump` times the five before them is left out of the decision: a
    rise that recent is left to the step rule."""
    order = {run.commit: index for index, run in enumerate(runs)}
    by_processor = {}
    for run in runs:
        if run.processor and run.values.get(name, 0) > 0:
            by_processor.setdefault(run.processor, []).append(run)
    cuts = sorted(cuts)
    judged, rising = [], set()
    for processor, every in sorted(by_processor.items()):
        points = [run for run in every if order[run.commit] >= start]
        if (len(points) < 2 * rule.runs
                or points[-1].timestamp - points[0].timestamp < timedelta(days=rule.min_days)):
            continue
        values = [run.values[name] for run in points]

        def first(after):
            return next((index for index, run in enumerate(points) if after(order[run.commit])), len(points))

        stretches, low_index = [], 0
        for cut in cuts:
            at = first(lambda position: position >= cut)
            before = [run.values[name] for run in every if order[run.commit] < cut]
            if low_index < at < len(points) and held(values[at:], before, rule):
                stretches.append((low_index, at))
                low_index = at
        stretches.append((low_index, len(points)))
        ratio = 1
        for low, high in stretches:
            if high - low >= 2 * rule.runs:
                ratio *= (statistics.median(values[high - rule.runs:high])
                          / statistics.median(values[low:low + rule.runs]))
        latest = statistics.median(values[-rule.runs:])
        if latest >= jump * statistics.median(values[-2 * rule.runs:-rule.runs]):
            rising.add(processor)
        judged.append((processor, points[0], statistics.median(values[:rule.runs]), latest, ratio))
    drifting = [row for row in judged if row[4] >= rule.threshold and row[0] not in rising]
    if len(drifting) < rule.min_processors:
        return None
    return Drift(name, unit, min((row[1] for row in judged), key=lambda run: run.timestamp), runs[-1],
                 min(row[4] for row in drifting), tuple(judged))


def drift_cuts(runs, name, unit, steps, order):
    """The positions in `order` of a time benchmark's steps' first slow runs."""
    return [order[step.first_slow.commit] for step in series_steps(runs, name, unit, steps)]


def find_drifts(runs, units, rule=DRIFT, steps=RULE, filed=None):
    """Every time benchmark that drifted up over the latest `rule.days` days, after the commit `filed` maps it
    to where that is later. Its steps, as `steps` finds them, split what is compared while their rise holds,
    so that a drift leaves out the jump of each step that holds and does not wait for one that was undone to
    leave the window."""
    runs = landed(runs)
    if not runs:
        return []
    order = {run.commit: index for index, run in enumerate(runs)}
    window = next(index for index, run in enumerate(runs)
                  if run.timestamp >= runs[-1].timestamp - timedelta(days=rule.days))
    drifts = []
    for name in sorted({name for run in runs for name in run.values if units.get(name) in TIME_UNITS}):
        start = max(window, order[filed[name]] + 1) if name in (filed or {}) else window
        drift = series_drift(runs, name, units[name], start, drift_cuts(runs, name, units[name], steps, order),
                             rule, steps.threshold)
        if drift is not None:
            drifts.append(drift)
    return drifts


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
    return title, fitted(lines)


def fitted(lines):
    body = "\n".join(lines) + "\n"
    if len(body) > BODY_LIMIT:
        body = body[:body.rindex("\n", 0, BODY_LIMIT - len(CUT_NOTE))] + CUT_NOTE
    return body


def drift_marker(drift):
    return DRIFT_MARKER.format(series=drift.benchmark, decided=drift.decided.commit)


def render_drifts(drifts, repository, rule=DRIFT):
    """The title and body of the issue for some drifts, all found at the same latest run."""
    drifts = sorted(drifts, key=lambda drift: (-drift.ratio, drift.benchmark))
    decided = drifts[0].decided
    earliest = min((drift.earliest for drift in drifts), key=lambda run: run.timestamp)
    days = (decided.timestamp - earliest.timestamp).days
    title = (f"Benchmarks drifted slower over {days} days to {decided.commit[:8]}: "
             f"{short_name(drifts[0].benchmark)} {ratio_text(drifts[0].ratio)}")
    if len(drifts) > 1:
        title += f" and {len(drifts) - 1} more"
    lines = sorted({drift_marker(drift) for drift in drifts})
    lines += [
        f"From `{earliest.commit[:8]}` ({quoted(earliest.subject)}) to `{decided.commit[:8]}` "
        f"({quoted(decided.subject)}), {len(drifts)} benchmark(s) measured at least {rule.threshold:g} times "
        f"slower on {rule.min_processors} or more processor models: "
        f"https://github.com/{repository}/compare/{earliest.commit}...{decided.commit}",
        "",
        "| Benchmark | Processor | First run compared | Earlier | Latest | Ratio |",
        "|---|---|---|---:|---:|---:|",
    ]
    for drift in drifts:
        for processor, first, earlier, latest, ratio in drift.per_processor:
            lines.append(f"| {short_name(drift.benchmark)} | {processor} | `{first.commit[:8]}` "
                         f"| {amount(earlier, drift.unit)} | {amount(latest, drift.unit)} | {ratio_text(ratio)} |")
    lines += ["", f"Earlier and Latest are medians over a processor's first and last {rule.runs} runs compared; "
              "the ratio leaves out the steps between them whose rise still holds. "
              "`.github/workflows/benchmarks.yml` opened this and does not rewrite it; "
              "CONTRIBUTING.md's benchmarks section says what counts as a drift."]
    return title, fitted(lines)


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
        open_issue(*render(members, repository), repository, run)
        actions.append(f"opened an issue for {', '.join(sorted({s.first_slow.commit[:8] for s in members}))}")
    return actions


def open_issue(title, body, repository, run):
    create = ["issue", "create", "--repo", repository, "--title", title, "--body-file", "-", "--assignee", ASSIGNEE]
    for label in LABELS:
        create += ["--label", label]
    run(create, body)


def filed_drifts(issues, runs):
    """Each benchmark's latest commit, in the order `runs` landed, that a drift marker in `issues` names."""
    order = {entry.commit: index for index, entry in enumerate(landed(runs))}
    filed = {}
    for issue in issues:
        for match in DRIFT_MARKER_PATTERN.finditer(issue.get("body") or ""):
            series, commit = match["series"], match["decided"]
            if commit in order and (series not in filed or order[commit] > order[filed[series]]):
                filed[series] = commit
    return filed


def sync_drift_issues(runs, units, repository, run=gh, rule=DRIFT, steps=RULE):
    """Opens one issue for the drifts found after the commits drift issues name, and writes nothing else."""
    new = find_drifts(runs, units, rule, steps, filed_drifts(list_issues(repository, run), runs))
    if not new:
        return []
    open_issue(*render_drifts(new, repository, rule), repository, run)
    return [f"opened an issue for the drift of {len(new)} benchmark(s) to {new[0].decided.commit[:8]}"]


def file_regressions(runs, units, repository, run=gh, rule=RULE, counts=COUNTS, drift=DRIFT):
    """The steps and drifts the history holds, synced: what the job runs."""
    return (sync_issues(find_steps(runs, units, rule, counts), runs, repository, run, rule)
            + sync_drift_issues(runs, units, repository, run, drift, rule))


class MemoryIssues:
    """The issues the syncs read and write, held in memory: what `--replay` syncs against."""

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


def replay(runs, units, rule=RULE, counts=COUNTS, drift=DRIFT):
    """Every prefix of the history synced in turn, as the job would have synced it, against issues in memory."""
    issues = MemoryIssues()
    for end in range(1, len(runs) + 1):
        issues.now = end
        file_regressions(runs[:end], units, "replay/replay", issues, rule, counts, drift)
    return issues


def describe(steps):
    return "\n".join(f"  {ratio_text(step.ratio):>8} {short_name(step.benchmark)} at {step.first_slow.commit[:8]}"
                     for step in sorted(steps, key=lambda step: -step.ratio))


def describe_drifts(drifts):
    return "\n".join(f"  {ratio_text(drift.ratio):>8} {short_name(drift.benchmark)} from {drift.earliest.commit[:8]}"
                     for drift in sorted(drifts, key=lambda drift: -drift.ratio))


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
    print(describe_drifts(find_drifts(runs, units)) or "no drift")
    if args.issues:
        for action in file_regressions(runs, units, args.repository):
            print(action)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
