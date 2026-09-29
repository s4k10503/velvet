#!/usr/bin/env python3
"""Prototype: measure a campaign's mutants from one compile and one editor session.

    python3 scripts/test_quality/mutant_schemata.py --files <source> --shard 9/11 --max 200 --output <dir>

Takes the same mutants `mutation_check.py` takes for the same arguments, rewrites every one of them into a
guarded form with `schemata/MutantSchemata.cs`, compiles the tree once, and runs each mutant in one editor
session through `Assets/MutantSchemata/Editor/SchemataRunner.cs`. The verdicts are the campaign's own.
"""

import argparse
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import mutation_check as mc  # noqa: E402
import schemata_tool  # noqa: E402

SCHEMATA_SENTINEL = "SCHEMATA_IN_PROGRESS.json"
ENV = "VELVET_MUTANT"
LAUNCH_FLAG = "-runTestsSchemata"
OPERATORS = {operator for _, _, operator in mc.OPERATORS}
CLOSING = -1
# The session editor, so a signal ending the driver reaps it before the tree is put back.
CURRENT = {"child": None}


def cs_id(number):
    """An id as the runner's `ToString("000")` spells it in a file name."""
    return format(number, "03d") if number >= 0 else "-" + format(-number, "03d")


def utf16(text):
    return len(text.encode("utf-16-le")) // 2


def edit_of(text, mutant):
    """(utf-16 column, utf-16 length, replacement) of the edit `apply_mutation` makes on the mutant's line."""
    start, end = mc.line_spans(text)[mutant.line - 1]
    line = text[start:end]
    if mutant.operator in OPERATORS:
        column, length, replacement = mutant.column, len(mutant.before) + 2, " {} ".format(mutant.after)
    else:
        column, length, replacement = mutant.column, len(mutant.before), mutant.after
    applied = text[:start + column] + replacement + text[start + column + length:]
    if applied != mc.apply_mutation(text, mutant):
        raise SystemExit("the edit read for {} is not the one apply_mutation makes".format(mutant.describe(Path("."))))
    return utf16(line[:column]), utf16(line[column:column + length]), replacement


def response_file(project, assembly):
    found = sorted(Path(project, "Library", "Bee", "artifacts").glob("*/{}.rsp".format(assembly)),
                   key=lambda path: path.stat().st_mtime)
    return str(found[-1].relative_to(project)) if found else None


def area_assemblies(area, platform):
    kind = "Editor" if platform == "EditMode" else "PlayMode"
    return sorted(json.loads(asmdef.read_text())["name"] for asmdef in area.rglob("*.asmdef")
                  if "/Tests/{}/".format(kind) in asmdef.as_posix())


class Held:
    """Every rewritten file, recorded before it is written and put back by `release`."""

    def __init__(self, sentinel):
        self.sentinel = Path(sentinel)

    def hold(self, originals):
        if self.sentinel.exists() or (self.sentinel.parent / mc.SENTINEL).exists():
            raise SystemExit("a campaign already holds something in this tree")
        self.sentinel.write_text(json.dumps({str(path): text for path, text in originals.items()}))

    def release(self):
        if not self.sentinel.exists():
            return
        for path, text in json.loads(self.sentinel.read_text()).items():
            Path(path).write_text(text)
        self.sentinel.unlink()


def reap(child):
    """Kills the editor's process group and waits for it; `mutation_check.reap` once the lock change lands."""
    try:
        os.killpg(child.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    child.wait()


def launch(unity, project, plan_path, log, bound, progress):
    """One editor session over the plan; returns (exit code or None when killed, the progress it died at)."""
    command = [unity, LAUNCH_FLAG, "-batchmode", "-debugCodeOptimization", "-projectPath", str(project),
               "-logFile", str(log)]
    if progress.exists():
        progress.unlink()
    launched = time.time()
    child = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True,
                             env=dict(os.environ, VELVET_SCHEMATA_PLAN=str(plan_path)))
    CURRENT["child"] = child
    try:
        while True:
            try:
                return child.wait(timeout=2), None
            except subprocess.TimeoutExpired:
                pass
            if not progress.exists() and time.time() - launched > bound:
                reap(child)
                raise SystemExit("the session wrote no progress within {}s of its launch".format(bound))
            if progress.exists():
                try:
                    current = json.loads(progress.read_text())
                except ValueError:
                    continue
                if time.time() - current["since"] > bound:
                    reap(child)
                    return None, current
    finally:
        if child.poll() is None:
            reap(child)


def per_mutant_launches(args, project, output, items):
    """The comparison arm: the same compiled tree, one ordinary `-runTests` launch per stage, the id in
    the environment. Records each item the way the session runner does, so one classifier reads both."""
    launches = []
    for item in items:
        record = {"id": item["id"], "stages": []}
        for stage in item["stages"]:
            results = output / "mutant-{}-{}.xml".format(cs_id(item["id"]), stage["name"])
            command = [args.unity, "-runTests", "-batchmode", "-debugCodeOptimization", "-projectPath", str(project),
                       "-testPlatform", args.platform, "-testResults", str(results),
                       "-logFile", str(output / "launch-{}-{}.log".format(cs_id(item["id"]), stage["name"]))]
            if stage["assemblyNames"]:
                command += ["-assemblyNames", ";".join(stage["assemblyNames"])]
            if stage["groupNames"]:
                command += ["-testFilter", ";".join(stage["groupNames"])]
            armed = output / "armed-{}-{}.txt".format(cs_id(item["id"]), stage["name"])
            if armed.exists():
                armed.unlink()
            clock = time.time()
            subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT,
                           env=dict(os.environ, **{ENV: str(item["id"]), "VELVET_SCHEMATA_ARMED": str(armed)}))
            wall = time.time() - clock
            counts = mc.read_counts(results) or {"passed": 0, "failed": 0, "inconclusive": 0}
            failures = mc.failing_names(results) if results.exists() else []
            record["stages"].append({"name": stage["name"], "passed": counts["passed"], "failed": counts["failed"],
                                     "inconclusive": counts["inconclusive"], "finished": results.exists(),
                                     "failures": failures, "seconds": wall, "reloadSeconds": 0,
                                     "armed": armed.read_text().split(",") if armed.exists() else [],
                                     "xml": str(results)})
            launches.append({"seconds": wall, "code": 0, "died": None})
            if stage["stopAtFirstFailure"] and failures:
                break
        (output / "item-{}.json".format(cs_id(item["id"]))).write_text(json.dumps(record))
    return launches


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", default=".")
    parser.add_argument("--files", nargs="+", required=True)
    parser.add_argument("--shard", type=mc.parse_shard)
    parser.add_argument("--max", type=int, default=40)
    parser.add_argument("--platform", default="EditMode")
    parser.add_argument("--timeout", type=int, default=900)
    parser.add_argument("--output", required=True)
    parser.add_argument("--unity", default=mc.DEFAULT_UNITY)
    parser.add_argument("--rewrite-only", action="store_true")
    parser.add_argument("--hang", type=int, help="an id whose run the runner spins forever in")
    parser.add_argument("--launch-per-mutant", action="store_true",
                        help="the comparison arm: one ordinary -runTests launch per stage over the compiled tree")
    parser.add_argument("--scrub", action="store_true",
                        help="close leftover editor windows and clear the console before every stage")
    parser.add_argument("--only", help="comma-separated mutant indices to keep of those selected")
    parser.add_argument("--stages", default="narrowed,whole", help="which of the two stages each mutant runs")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    started = time.time()

    targets = {}
    for name in args.files:
        path = Path(name).resolve()
        if mc.mutable(path, project):
            targets[path] = set(range(1, len(path.read_text().splitlines()) + 1))
    mutants = []
    for path, lines in sorted(targets.items()):
        mutants.extend(mc.mutations_for(path, path.read_text(), lines))
    mutants = mutants[:args.max]
    selected = mc.sharded(list(range(1, len(mutants) + 1)), args.shard)
    if args.only:
        selected = [index for index in selected if index in {int(n) for n in args.only.split(",")}]
    originals = {path: path.read_text() for path in targets}

    files = {}
    assemblies = {}
    for index in selected:
        mutant = mutants[index - 1]
        assembly = mc.assembly_of(mutant.path)
        rsp = os.environ.get("SCHEMATA_RSP_" + assembly.replace(".", "_")) or response_file(project, assembly)
        if rsp:
            assemblies[assembly] = rsp
        start, length, replacement = edit_of(originals[mutant.path], mutant)
        entry = files.setdefault(mutant.path, {"path": str(mutant.path.relative_to(project)), "assembly": assembly,
                                               "text": originals[mutant.path], "mutants": []})
        entry["mutants"].append({"id": index, "line": mutant.line, "start": start,
                                 "length": length, "replacement": replacement, "operator": mutant.operator,
                                 "before": mutant.before})
    request = {"project": str(project), "env": ENV, "shapeRulesOff": os.environ.get("SCHEMATA_SHAPE_ON") is None, "assemblies": assemblies, "files": list(files.values())}
    clock = time.time()
    answer = schemata_tool.rewrite(args.unity, project, request, output)
    rewrite_seconds = time.time() - clock
    placed = answer["placed"]
    declined = {int(key): value for key, value in answer["declined"].items()}
    print("rewriter: {} placed, {} declined in {:.1f}s (tool's own {:.1f}s), rounds {}".format(
        len(placed), len(declined), rewrite_seconds, answer["seconds"], answer["rounds"]))
    for index, reason in sorted(declined.items()):
        print("  declined [{}] {}: {}".format(index, mutants[index - 1].describe(project), reason))
    if answer["fatal"]:
        raise SystemExit("rewriter: " + "; ".join(answer["fatal"]))
    (output / "schemata-files").mkdir(exist_ok=True)
    for relative, text in answer["files"].items():
        (output / "schemata-files" / Path(relative).name).write_text(text)
    if args.rewrite_only:
        return 0

    text_readers = mc.text_reading_fixtures(project)
    exclusions = ["!\\.{}$".format(name) for name in sorted(text_readers)]
    areas = {index: mc.area_of(mutants[index - 1].path, project) for index in placed}
    area_names = sorted({name for area in areas.values() if area for name in area_assemblies(area, args.platform)})
    if len({area for area in areas.values()}) != 1:
        raise SystemExit("prototype: one area per run")

    def stages(stop):
        found = []
        wanted = args.stages.split(",")
        if area_names and "narrowed" in wanted:
            found.append({"name": "narrowed", "assemblyNames": area_names, "groupNames": exclusions,
                          "testNames": [], "stopAtFirstFailure": stop})
        if "whole" in wanted:
            found.append({"name": "whole", "assemblyNames": [], "groupNames": exclusions, "testNames": [],
                          "stopAtFirstFailure": stop})
        return found

    # Opened and closed by the unmutated program under the switch: the first is the session's baseline,
    # and the second says whether anything a mutant left behind outlived its reload.
    # A PlayMode job cancelled mid-run leaves its test scene modified, and the next job's scene-saving task
    # then waits on a dialog batchmode cannot answer (measured), so PlayMode stages run to their end.
    early = args.platform == "EditMode"
    items = ([{"id": 0, "stages": stages(False)}] + [{"id": index, "stages": stages(early)} for index in placed]
             + [{"id": CLOSING, "stages": stages(False)}])
    plan = {"output": str(output), "platform": args.platform, "env": ENV, "items": items, "start": 0}
    if args.hang is not None:
        plan["hang"] = args.hang
    plan["scrub"] = args.scrub
    plan_path = output / "plan.json"
    state = output / "runner-state.json"
    progress = output / "runner-progress.json"
    for stale in list(output.glob("item-*.json")) + list(output.glob("mutant-*.xml")) + [state, progress,
                                                                                          output / "runner-done"]:
        if stale.exists():
            stale.unlink()

    if not mc.wait_for_quiet(1800):
        raise SystemExit("another editor stayed up")
    held = Held(project / SCHEMATA_SENTINEL)
    held.hold(originals)

    def restore(number, _frame):
        if CURRENT["child"] is not None and CURRENT["child"].poll() is None:
            reap(CURRENT["child"])
        held.release()
        signal.signal(number, signal.SIG_DFL)
        os.kill(os.getpid(), number)

    for number in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(number, restore)
    launches = []
    try:
        for relative, text in answer["files"].items():
            (project / relative).write_text(text)
        position = 0
        while not args.launch_per_mutant:
            plan["start"] = position
            plan_path.write_text(json.dumps(plan, indent=1))
            clock = time.time()
            code, died = launch(args.unity, project, plan_path, output / "session-{}.log".format(len(launches)),
                                args.timeout, progress)
            launches.append({"seconds": time.time() - clock, "code": code, "died": died})
            print("session {}: exit {} after {:.0f}s{}".format(len(launches), code, time.time() - clock,
                                                               "; killed at id {}".format(died["id"]) if died else ""))
            if died is None:
                break
            (output / "item-{}.json".format(cs_id(died["id"]))).write_text(json.dumps(
                {"id": died["id"], "stages": [], "hung": True}))
            position = died["position"] + 1
            state.write_text(json.dumps({"position": position, "stage": 0, "phase": "arm", "guid": "",
                                         "requested": 0}))
            if position >= len(items):
                break
        if args.launch_per_mutant:
            launches = per_mutant_launches(args, project, output, items)
    finally:
        held.release()

    report = {"launches": launches, "rewrite_seconds": rewrite_seconds, "declined": declined,
              "wall": time.time() - started, "verdicts": {}}
    for name, item in (("opening", 0), ("closing", CLOSING)):
        path = output / "item-{}.json".format(cs_id(item))
        found = json.loads(path.read_text()) if path.exists() else None
        if found is None or any(stage["failed"] or stage["inconclusive"] or not stage.get("finished")
                                for stage in found["stages"]):
            print("{} baseline under the switch is not green: {}".format(name, found))
        else:
            print("{} baseline under the switch: ".format(name) + ", ".join("{} {} passed in {:.0f}s (reload {:.1f}s)".format(
                stage["name"], stage["passed"], stage["seconds"], stage["reloadSeconds"]) for stage in found["stages"]))
        report[name] = found
    baseline = report["opening"]
    whole_seconds = baseline["stages"][-1]["seconds"] if baseline else 0
    for index in placed:
        path = output / "item-{}.json".format(cs_id(index))
        mutant = mutants[index - 1]
        result = json.loads(path.read_text()) if path.exists() else {"stages": []}
        stages_run = result.get("stages", [])
        spent = sum(stage.get("seconds", 0) + stage.get("reloadSeconds", 0) for stage in stages_run)
        armed = all(any(entry.endswith(":{}".format(index)) for entry in stage.get("armed", []))
                    for stage in stages_run)
        failures = [name for stage in stages_run for name in stage.get("failures", [])]
        if result.get("hung"):
            verdict = mc.HUNG if whole_seconds * mc.HANG_MARGIN <= args.timeout else mc.TIMED_OUT
            detail = "the session was killed at {}s".format(args.timeout)
        elif not stages_run or not armed:
            verdict, detail = mc.NOT_BUILT, "armed {}".format([stage.get("armed") for stage in stages_run])
        elif failures:
            verdict, detail = mc.KILLED, "{} in {}: {}".format(len(failures), stages_run[-1]["name"],
                                                            failures[0].split(".")[-1])
        elif any(stage["inconclusive"] for stage in stages_run):
            verdict, detail = mc.INCONCLUSIVE, ""
        else:
            verdict, detail = mc.SURVIVED, ""
        report["verdicts"][index] = {"mutant": mutant.describe(project), "verdict": verdict, "detail": detail,
                                     "seconds": spent, "stages": [(s["name"], round(s.get("reloadSeconds", 0), 1),
                                                                   round(s.get("seconds", 0), 1),
                                                                   round(s.get("setupSeconds", 0), 1), s.get("windows"),
                                                                   s.get("logEntries"), s.get("objects"))
                                                                  for s in stages_run]}
        print("[{}] {} -> {} ({}) in {:.0f}s {}".format(index, mutant.describe(project), verdict, detail, spent,
                                                       report["verdicts"][index]["stages"]))
    (output / "report.json").write_text(json.dumps(report, indent=1))
    print("wall {:.0f}s".format(report["wall"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
