#!/usr/bin/env python3
"""Run the PlayMode benchmarks on a Linux standalone player, under licensed_editor.py's licence.

`.github/workflows/player-benchmarks.yml` runs this inside the `unityci/editor` image:

    python3 scripts/ci/player_benchmarks.py --filter Velvet.Tests.Performance.JsFrameworkKeyedBenchmarks \\
        --output Logs/player-benchmarks

`-testPlatform StandaloneLinux64` makes the editor build a player with the test assemblies and run it.
The editor reads `VELVET_PLAYER_BENCHMARKS` from its environment, which `BenchmarkPlayerBuild` turns into
the IL2CPP backend and the full iteration counts for that build alone. The results XML and the editor's
log land under OUTPUT, and the exit status is the editor's.

Run: python3 scripts/ci/test_player_benchmarks.py
"""

import argparse
import importlib.util
import os
import sys
from pathlib import Path

ENVIRONMENT_VARIABLE = "VELVET_PLAYER_BENCHMARKS"


def load_licensed_editor():
    """Imports licensed_editor by path, since scripts/ci is not a package."""
    spec = importlib.util.spec_from_file_location(
        "licensed_editor", Path(__file__).with_name("licensed_editor.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


licensed_editor = load_licensed_editor()


def editor_command(project, output, test_filter, unity=licensed_editor.UNITY):
    # No -quit: the editor exits when the run does. -debugCodeOptimization is AGENTS.md's headless recipe's.
    return [unity, "-runTests", "-batchmode", "-debugCodeOptimization", "-projectPath", str(project),
            "-testPlatform", "StandaloneLinux64", "-testFilter", test_filter,
            "-testResults", str(output / "results.xml"), "-logFile", str(output / "run.log")]


def main(argv, run=licensed_editor.run_licensed, environ=os.environ):
    parser = argparse.ArgumentParser(description="Run the PlayMode benchmarks on an IL2CPP Linux player.")
    parser.add_argument("--filter", required=True, help="the -testFilter value")
    parser.add_argument("--output", required=True)
    parser.add_argument("--project", default=".")
    args = parser.parse_args(argv)
    if not args.filter.strip(";"):
        print("::error::an empty --filter runs the whole PlayMode suite", flush=True)
        return 2
    if environ.get(ENVIRONMENT_VARIABLE) != "1":
        print("::error::{} is not 1, so the player would build on Mono with one iteration".format(
            ENVIRONMENT_VARIABLE), flush=True)
        return 2
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    return run(editor_command(Path(args.project).resolve(), output, args.filter), "the player benchmarks")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
