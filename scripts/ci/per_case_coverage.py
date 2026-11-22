#!/usr/bin/env python3
"""Run one Unity suite in the CI editor image with each case's coverage written to a file of its own.

`.github/workflows/per-case-coverage.yml` runs this inside the `unityci/editor` image, under
`licensed_editor.py`'s licence, the way `kill-set.yml` runs `ci_mutation_shard.py`:

    python3 scripts/ci/per_case_coverage.py --platform EditMode --output Logs/per-case-coverage \\
        --shard 2/6

The editor is given `generateTestReferences` in `-coverageOptions`, under which Code Coverage writes an
OpenCover file as each case finishes. One editor taking the whole suite, in a container limited to 12g
of memory with swap at the same figure, was killed with SIGKILL after 223 of the EditMode suite's 17033
cases and after 232 of the PlayMode suite's 500: at nearly the same count although the files were 0.3 to
0.5 MB uncompressed in one and 3.4 to 5.7 MB in the other. Each launch below reports the most memory one of the watcher's samples read
while it ran, which is the reading that says whether a launch's size fits. This runs the suite in launches of at
most `--cases-per-launch` cases each:

1. One plain run of the suite, whose results file names every case: `OUTPUT/results`, where the
   workflow's provenance checks read it, and where a run that fails stops here.
2. `--shard I/N` takes the I-th of N contiguous blocks of those names, and each launch takes a slice of
   the block through a `-testFilter` of anchored names, with a coverage folder, results file and log of
   its own under `OUTPUT/coverage` and `OUTPUT/launches`. A launch killed with SIGKILL runs again as two
   halves, since the files it finished stay, and its newest file, which the package may have been
   writing, is not compressed; a launch of one case that is killed fails the run.
3. `OUTPUT/per-case/shard.json` names the shard, the length of the roster it was cut from and the
   launches that failed, which `per_case_manifest.py` checks the shards against one another with.

Both are one licence's work: the licence is activated once around this script's own `--measure` run,
and every editor above is launched from that.

Each file the package has finished writing is gzipped into `OUTPUT/per-case` while its launch runs, and
an empty file is left under its name in `OUTPUT/coverage`: the package numbers its next file by how many
`.xml` files that folder already holds, and writes nothing where the name it arrives at exists, so a
deleted file would make it skip a later case. That reading is of the writer in Code Coverage
`PINNED_PACKAGE`, and a project resolving another version is refused before any editor starts.

`scripts/test_quality/unique_coverage.py` reads what `OUTPUT/per-case` holds, the assemblies the run asked
for among it under that script's `ASKED`, so that the reader can refuse an asked assembly no record
reaches. Several shards' folders are read together: a case a halved launch ran twice is one test to it. An
`OUTPUT` that already holds anything is refused: the package clears its own folder's `.xml` files when a
run starts and numbers from zero again, so a second run's files would take names whose copies the first
run left, and be passed over.

Exits with the first failing editor's status; 1 where every editor passed and compressing failed, where
the plain run named no case, or where a launch selected fewer cases than it was given; 2, before any
editor starts, where the project resolves a Code Coverage version other than `PINNED_PACKAGE`, declares
no package assembly to measure, `--shard` is not I/N, or `OUTPUT` already holds something.

Run: python3 scripts/ci/test_per_case_coverage.py
"""

import argparse
import collections
import gzip
import json
import re
import shutil
import subprocess
import sys
import threading
import xml.etree.ElementTree as ET
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "test_quality"))
import licensed_editor  # noqa: E402
import unique_coverage  # noqa: E402

PACKAGE = "com.unity.testtools.codecoverage"
PINNED_PACKAGE = "1.3.0"
LOCK = Path("Packages/packages-lock.json")
PACKAGE_ROOT = Path("Packages/com.velvet.core")

# Every assembly of the package's own code is asked for, read off its asmdefs. test.yml's figure leaves
# Unity.Velvet.CodeGen out; a record cannot, since the weaver fixtures run it in the editor domain and a
# line only they reach has to be in their record.
NOT_MEASURED = frozenset({"Tests", "TestUtilities"})

NUMBERED = re.compile(r"_(\d+)\.xml$")
SHARD = re.compile(r"([1-9][0-9]*)/([1-9][0-9]*)$")

POLL = 5

# Cases one editor launch takes. A launch over a whole suite was killed after 223 cases of one suite and
# 232 of the other, so a hundred is under half of the fewer of those, and a launch killed anyway runs
# again as halves.
CASES_PER_LAUNCH = 100

# What a launch killed with SIGKILL returns: `subprocess.call` reports a death by signal 9 as -9, and a
# shell reports it as 128 + 9.
KILLED = (-9, 137)

# Where a container reads its own memory in use, the unified hierarchy first.
CGROUP_MEMORY = (Path("/sys/fs/cgroup/memory.current"), Path("/sys/fs/cgroup/memory/memory.usage_in_bytes"))


def pinned_refusal(lock):
    """Why this project cannot be measured here, or None where it resolves `PINNED_PACKAGE`."""
    try:
        resolved = json.loads(Path(lock).read_text())["dependencies"][PACKAGE]["version"]
    except (OSError, ValueError, KeyError, TypeError):
        return "{} does not say which {} the project resolves".format(lock, PACKAGE)
    if resolved != PINNED_PACKAGE:
        return ("the project resolves {} {}, and how this compresses the files the package writes "
                "was read off {}: read that version's CoverageResultWriterBase and "
                "OpenCoverResultWriter, then move PINNED_PACKAGE").format(PACKAGE, resolved, PINNED_PACKAGE)
    return None


def package_assemblies(project):
    """The assemblies the package's asmdefs declare, outside its test folders and its `~` folders."""
    names = []
    for path in sorted((project / PACKAGE_ROOT).rglob("*.asmdef")):
        parts = path.relative_to(project / PACKAGE_ROOT).parts
        if NOT_MEASURED & set(parts) or any(part.endswith("~") for part in parts):
            continue
        names.append(json.loads(path.read_text())["name"])
    return names


def suite_command(platform, project, results, unity=licensed_editor.UNITY):
    """The plain run of the suite, whose results file is the roster the launches take their cases from."""
    # -debugCodeOptimization: AGENTS.md's headless recipe says why a run passes it.
    return [unity, "-runTests", "-batchmode", "-debugCodeOptimization", "-projectPath", str(project),
            "-testPlatform", platform,
            "-testResults", str(results / "results.xml"), "-logFile", str(results / "run.log")]


def editor_command(platform, project, coverage, results, assemblies, names=None,
                   unity=licensed_editor.UNITY):
    """A launch with coverage on, over the cases `names` give by their full names, or over the whole
    suite where there are none."""
    options = "generateTestReferences;assemblyFilters:" + ",".join("+" + name for name in assemblies)
    command = suite_command(platform, project, results, unity)
    if names:
        command += ["-testFilter", filter_for(names)]
    return command + ["-enableCodeCoverage", "-coverageResultsPath", str(coverage),
                      "-coverageOptions", options]


def filter_for(names):
    """A `-testFilter` value selecting the cases with these full names.

    The value is a regex per `;`-separated term, so a name is escaped and anchored, and the split does
    not trim. A name carrying `;` would split a term, so that character is matched as any character
    and the term can select a neighbour too, whose second record names the same test to the reader.
    """
    return ";".join("^" + re.escape(name).replace(";", ".") + "$" for name in names)


def roster_of(results_xml):
    """The full name of each case a results file holds, each once, in sorted order, or [] where there is
    no readable file."""
    names = set()
    try:
        for _, element in ET.iterparse(str(results_xml), events=("end",)):
            if element.tag == "test-case" and element.get("fullname"):
                names.add(element.get("fullname"))
            element.clear()
    except (OSError, ET.ParseError):
        return []
    return sorted(names)


def selected_by(results_xml):
    """How many cases a results file ran or skipped, or None where it names no total."""
    try:
        for _, element in ET.iterparse(str(results_xml), events=("start",)):
            if element.tag == "test-run":
                return int(element.get("total"))
    except (OSError, ET.ParseError, TypeError, ValueError):
        pass
    return None


def shard_of(text):
    """(I, N) of an `I/N` argument whose I is at most N, or None."""
    found = SHARD.match(text)
    return (int(found.group(1)), int(found.group(2))) if found and int(found.group(1)) <= int(found.group(2)) else None


def measuring_command(platform, output, project, shard, per_launch):
    """What the licence is held around: this script's own measuring run."""
    return [sys.executable, "-u", str(Path(__file__).resolve()), "--measure",
            "--platform", platform, "--output", str(output), "--project", str(project),
            "--shard", "{}/{}".format(*shard), "--cases-per-launch", str(per_launch)]


def block_of(names, shard, shards):
    """The `shard`-th (from 1) of `shards` contiguous blocks of `names`."""
    return names[len(names) * (shard - 1) // shards: len(names) * shard // shards]


def slices_of(names, size):
    return [names[start:start + size] for start in range(0, len(names), size)]


def memory_in_use(paths=CGROUP_MEMORY):
    """MiB the container holds right now, or None where no cgroup file says."""
    for path in paths:
        try:
            return int(path.read_text()) // (1024 * 1024)
        except (OSError, ValueError):
            continue
    return None


def numbered_files(directory):
    """The files in `directory` the package numbered, in the order of their numbers."""
    numbered = []
    for path in directory.iterdir():
        found = NUMBERED.search(path.name)
        if found and path.is_file():
            numbered.append((int(found.group(1)), path))
    return [path for _, path in sorted(numbered)]


def compress_finished(source, target, final=False):
    """Gzip each file under `source` the package has finished into the same place under `target`, and
    empty it where it stands. Returns how many were compressed.

    The package writes one file at a time, so in each folder every numbered file but the newest is
    finished, and the newest is too once the editor has exited, which is what `final` says. A file
    whose copy is already in `target` is passed over: a copy takes its name only once it is whole, and
    compressing the empty file left under the original's name would overwrite it.
    """
    if not source.is_dir():
        return 0
    compressed = 0
    for directory in sorted({path.parent for path in source.rglob("*.xml")}):
        files = numbered_files(directory)
        for path in files if final else files[:-1]:
            copy = target / path.parent.relative_to(source) / (path.name + ".gz")
            if copy.exists():
                continue
            copy.parent.mkdir(parents=True, exist_ok=True)
            partial = copy.with_name(copy.name + ".partial")
            with path.open("rb") as raw, gzip.open(str(partial), "wb") as packed:
                shutil.copyfileobj(raw, packed)
            partial.replace(copy)
            path.open("wb").close()
            compressed += 1
    return compressed


def watch(source, target, stop, poll=POLL, peak=None, memory=memory_in_use):
    """Compress finished files every `poll` seconds until `stop` is set, and keep in `peak[0]` the most
    memory the container held at any pass.

    A pass that fails is reported once and the next one tries again, since nothing it raises stops the
    editor writing; whether every file was compressed is the closing pass's to say.
    """
    failing = False
    while not stop.wait(poll):
        if peak is not None:
            held = memory()
            if held is not None:
                peak[0] = max(peak[0], held)
        try:
            compress_finished(source, target)
        except Exception as error:
            if not failing:
                print("::warning::compressing the per-case coverage failed, and is retried: {}".format(error),
                      flush=True)
            failing = True
            continue
        failing = False


def watched(launch, command, source, target, poll):
    """(the launch's status, whether every file compressed, the most MiB sampled during it)."""
    stop, peak = threading.Event(), [0]
    watcher = threading.Thread(target=watch, args=(source, target, stop, poll, peak), daemon=True)
    watcher.start()
    try:
        status = launch(command)
    finally:
        stop.set()
        watcher.join()
    # A launch killed mid-write leaves its newest file cut short, and the halves it runs again as
    # reach those cases anyway, so that file is left uncompressed.
    try:
        compress_finished(source, target, final=status not in KILLED)
    except Exception as error:
        print("::error::compressing the per-case coverage failed: {}".format(error), flush=True)
        return status, False, peak[0]
    return status, True, peak[0]


MANIFEST = "shard.json"


def write_manifest(per_case, platform, shard, roster, mine, problems, status):
    """What the report checks the shards against each other with: which block this was, how long the
    roster it was cut from, and which launches failed."""
    (per_case / MANIFEST).write_text(json.dumps(
        {"platform": platform, "shard": shard[0], "shards": shard[1], "roster": roster, "cases": mine,
         "problems": problems, "status": status}, indent=1) + "\n")


def measure(platform, project, output, shard, per_launch, assemblies, launch=subprocess.call, poll=POLL):
    """Run the plain suite, then this shard's cases in launches of at most `per_launch`, and return the
    status the run ends with."""
    coverage, per_case = output / "coverage", output / "per-case"
    results, launches = output / "results", output / "launches"
    for directory in (results, per_case, launches):
        directory.mkdir(parents=True, exist_ok=True)
    (per_case / unique_coverage.ASKED).write_text("".join(name + "\n" for name in assemblies))

    licensed_editor.say("running the {} suite".format(platform))
    status = launch(suite_command(platform, project, results))
    names = roster_of(results / "results.xml")
    if status or not names:
        print("::error::the plain {} run {}".format(
            platform, "ended with status {}".format(status) if status else "named no case"), flush=True)
        write_manifest(per_case, platform, shard, len(names), 0,
                       ["the plain run ended with status {}".format(status or 1)], status or 1)
        return status or 1
    mine = block_of(names, *shard)
    licensed_editor.say("{} case(s), {} of them this shard's, in launches of at most {}".format(
        len(names), len(mine), per_launch))

    waiting, number, failed, problems = collections.deque(slices_of(mine, per_launch)), 0, 0, []
    while waiting:
        chunk = waiting.popleft()
        number += 1
        folder = "{:04d}".format(number)
        directory = launches / folder
        directory.mkdir()
        command = editor_command(platform, project, coverage / folder, directory, assemblies, chunk)
        status, compressed, peak = watched(launch, command, coverage / folder, per_case / folder, poll)
        files = sum(1 for _ in (coverage / folder).rglob("*.xml")) if (coverage / folder).is_dir() else 0
        licensed_editor.say("launch {}: {} case(s), {} file(s), peak {} MiB, status {}, {} slice(s) waiting".format(
            number, len(chunk), files, peak if peak else "unread", status, len(waiting)))
        if not compressed:
            failed = failed or 1
            problems.append("launch {}: its files could not be compressed".format(folder))
        if status in KILLED and len(chunk) > 1:
            half = len(chunk) // 2
            waiting.extendleft([chunk[half:], chunk[:half]])
            print("::warning::launch {} was killed with the container holding {} MiB at its last reading and runs again as two halves".format(
                number, peak or "no"), flush=True)
            continue
        if status:
            failed = failed or status
            problems.append("launch {}: status {}".format(folder, status))
            continue
        ran = selected_by(directory / "results.xml")
        if ran is None or ran < len(chunk):
            print("::error::launch {} was given {} case(s) and ran {}".format(
                number, len(chunk), "none that it reported" if ran is None else ran), flush=True)
            failed = failed or 1
            problems.append("launch {}: {} of {} case(s) ran".format(folder, ran, len(chunk)))
    write_manifest(per_case, platform, shard, len(names), len(mine), problems, failed)
    licensed_editor.say("{} per-case files in {}".format(
        sum(1 for _ in per_case.rglob("*.xml.gz")), per_case))
    return failed


def main(argv, run=licensed_editor.run_licensed, poll=POLL):
    parser = argparse.ArgumentParser(description="Run a suite with each case's coverage on its own.")
    parser.add_argument("--platform", required=True, choices=("EditMode", "PlayMode"))
    parser.add_argument("--output", required=True)
    parser.add_argument("--project", default=".")
    parser.add_argument("--shard", default="1/1", help="I/N: the I-th of N blocks of the suite's cases")
    parser.add_argument("--cases-per-launch", type=int, default=CASES_PER_LAUNCH)
    parser.add_argument("--measure", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args(argv)

    project = Path(args.project).resolve()
    output = Path(args.output).resolve()
    assemblies = package_assemblies(project)
    shard = shard_of(args.shard)
    refusal = pinned_refusal(project / LOCK)
    if not refusal and not assemblies:
        refusal = "no asmdef under {} declares an assembly to measure".format(project / PACKAGE_ROOT)
    if not refusal and not shard:
        refusal = "--shard {} is not I/N with I at most N".format(args.shard)
    if not refusal and args.cases_per_launch < 1:
        refusal = "--cases-per-launch {} is not a case count".format(args.cases_per_launch)
    if not refusal and not args.measure and output.exists() and any(output.iterdir()):
        refusal = "{} already holds a run; name an output that does not exist yet".format(output)
    if refusal:
        print("::error::{}".format(refusal), flush=True)
        return 2

    if args.measure:
        return measure(args.platform, project, output, shard, args.cases_per_launch, assemblies,
                       poll=poll)
    # The licence is held around this script's own measuring run, so that one activation serves every
    # launch.
    return run(measuring_command(args.platform, output, project, shard, args.cases_per_launch),
               "the {} suite".format(args.platform))


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
