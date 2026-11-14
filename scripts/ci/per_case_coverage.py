#!/usr/bin/env python3
"""Run one Unity suite in the CI editor image with each case's coverage written to a file of its own.

`.github/workflows/per-case-coverage.yml` runs this inside the `unityci/editor` image, under
`licensed_editor.py`'s licence, the way `kill-set.yml` runs `ci_mutation_shard.py`:

    python3 scripts/ci/per_case_coverage.py --platform EditMode --output Logs/per-case-coverage

The editor is given `generateTestReferences` in `-coverageOptions`, under which Code Coverage writes an
OpenCover file as each case finishes. The EditMode suite is thousands of those, so each file the package
has finished writing is gzipped into `OUTPUT/per-case` while the suite runs rather than after it, and an
empty file is left under its name in `OUTPUT/coverage`: the package numbers its next file by how many
`.xml` files that folder already holds, and writes nothing where the name it arrives at exists, so a
deleted file would make it skip a later case. That reading is of the writer in Code Coverage
`PINNED_PACKAGE`, and a project resolving another version is refused before any editor starts.

`scripts/test_quality/unique_coverage.py` reads what `OUTPUT/per-case` holds. The editor's results and
log go to `OUTPUT/results`, where the workflow's provenance checks read them. An `OUTPUT` that already
holds anything is refused: the package clears its own folder's `.xml` files when a run starts and
numbers from zero again, so a second run's files would take names whose copies the first run left, and
be passed over.

Exits with the editor's status; 1 where the editor passed and compressing failed; 2, before any editor
starts, where the project resolves a Code Coverage version other than `PINNED_PACKAGE`, declares no
package assembly to measure, or `OUTPUT` already holds something.

Run: python3 scripts/ci/test_per_case_coverage.py
"""

import argparse
import gzip
import json
import re
import shutil
import sys
import threading
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import licensed_editor  # noqa: E402

PACKAGE = "com.unity.testtools.codecoverage"
PINNED_PACKAGE = "1.3.0"
LOCK = Path("Packages/packages-lock.json")
PACKAGE_ROOT = Path("Packages/com.velvet.core")

# Every assembly of the package's own code is asked for, read off its asmdefs. test.yml's figure leaves
# Unity.Velvet.CodeGen out; a record cannot, since the weaver fixtures run it in the editor domain and a
# line only they reach has to be in their record.
NOT_MEASURED = frozenset({"Tests", "TestUtilities"})

NUMBERED = re.compile(r"_(\d+)\.xml$")

POLL = 5
PROGRESS = 1000


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


def editor_command(platform, project, coverage, results, assemblies, unity=licensed_editor.UNITY):
    options = "generateTestReferences;assemblyFilters:" + ",".join("+" + name for name in assemblies)
    # -debugCodeOptimization: AGENTS.md's headless recipe says why a run passes it.
    return [unity, "-runTests", "-batchmode", "-debugCodeOptimization", "-projectPath", str(project),
            "-testPlatform", platform,
            "-testResults", str(results / "results.xml"), "-logFile", str(results / "run.log"),
            "-enableCodeCoverage", "-coverageResultsPath", str(coverage), "-coverageOptions", options]


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


def watch(source, target, stop, poll=POLL):
    """Compress finished files every `poll` seconds until `stop` is set.

    A pass that fails is reported once and the next one tries again, since nothing it raises stops the
    editor writing; whether every file was compressed is the closing pass's to say.
    """
    done, failing = 0, False
    while not stop.wait(poll):
        try:
            compressed = compress_finished(source, target)
        except Exception as error:
            if not failing:
                print("::warning::compressing the per-case coverage failed, and is retried: {}".format(error),
                      flush=True)
            failing = True
            continue
        failing = False
        if (done + compressed) // PROGRESS > done // PROGRESS:
            licensed_editor.say("{} per-case files compressed".format(done + compressed))
        done += compressed


def main(argv, run=licensed_editor.run_licensed, poll=POLL):
    parser = argparse.ArgumentParser(description="Run a suite with each case's coverage on its own.")
    parser.add_argument("--platform", required=True, choices=("EditMode", "PlayMode"))
    parser.add_argument("--output", required=True)
    parser.add_argument("--project", default=".")
    args = parser.parse_args(argv)

    project = Path(args.project).resolve()
    output = Path(args.output).resolve()
    assemblies = package_assemblies(project)
    refusal = pinned_refusal(project / LOCK)
    if not refusal and not assemblies:
        refusal = "no asmdef under {} declares an assembly to measure".format(project / PACKAGE_ROOT)
    if not refusal and output.exists() and any(output.iterdir()):
        refusal = "{} already holds a run; name an output that does not exist yet".format(output)
    if refusal:
        print("::error::{}".format(refusal), flush=True)
        return 2

    coverage, per_case, results = output / "coverage", output / "per-case", output / "results"
    results.mkdir(parents=True, exist_ok=True)

    stop = threading.Event()
    watcher = threading.Thread(target=watch, args=(coverage, per_case, stop, poll), daemon=True)
    watcher.start()
    try:
        status = run(editor_command(args.platform, project, coverage, results, assemblies),
                     "the {} suite".format(args.platform))
    finally:
        stop.set()
        watcher.join()

    try:
        compress_finished(coverage, per_case, final=True)
    except Exception as error:
        print("::error::compressing the per-case coverage failed: {}".format(error), flush=True)
        return status or 1
    licensed_editor.say("{} per-case files in {}".format(
        sum(1 for _ in per_case.rglob("*.xml.gz")) if per_case.is_dir() else 0, per_case))
    return status


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
