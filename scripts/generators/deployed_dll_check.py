#!/usr/bin/env python3
"""Fail when a committed generator DLL is not what its sources build.

The assemblies under Packages/com.velvet.core/Runtime/Plugins are committed binaries, and a consumer's
compile loads those rather than anything built from the sources beside them. Nothing else compares the
two, so a change to Generators~/src that skips the redeploy leaves Unity running the old analyzer with
a green suite.

This rebuilds each deployed project and compares the result byte for byte. Every way of not reaching
that comparison — an absent binary, a build that fails, an SDK other than the pinned one — exits
non-zero, because a guard that cannot read its artefact and says nothing is indistinguishable from one
that read it and was satisfied.

Run: python3 scripts/generators/deployed_dll_check.py
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import subprocess
import sys
from pathlib import Path
from typing import NamedTuple

GENERATORS_REL = Path("Packages/com.velvet.core/Generators~")

# The committed pair is a Release build, and build.py takes the configuration from the environment.
# Comparing a Debug build against it would report a mismatch that says nothing about the sources.
CONFIGURATION = "Release"

MISMATCH_EXIT = 1
REFUSED_EXIT = 2


class Refusal(Exception):
    """The comparison could not be made, which is never reported as a pass."""


class Deployment(NamedTuple):
    name: str
    project: Path
    built: Path
    committed: Path


def pinned_sdk_version(generators_root: Path) -> str:
    """Read from global.json, so this file does not become a second place the pin has to be bumped."""
    path = generators_root / "global.json"
    try:
        pinned = json.loads(path.read_text(encoding="utf-8"))["sdk"]["version"]
    except (OSError, ValueError, KeyError, TypeError) as error:
        raise Refusal(f"could not read the pinned SDK version from {path}: {error}") from error
    if not isinstance(pinned, str) or not pinned:
        raise Refusal(f"{path} names no SDK version, so the build cannot be held to one")
    return pinned


def installed_sdk_version(generators_root: Path) -> str:
    try:
        result = subprocess.run(
            ["dotnet", "--version"], cwd=str(generators_root),
            capture_output=True, text=True, check=False)
    except OSError as error:
        raise Refusal(f"could not run dotnet: {error}") from error
    if result.returncode != 0:
        raise Refusal(f"dotnet --version failed ({result.returncode}): {result.stderr.strip()}")
    return result.stdout.strip()


def sdk_problem(pinned: str, installed: str) -> str | None:
    """Non-None when the two disagree: the rebuild would then be a different compiler's output."""
    if pinned == installed:
        return None
    return (f"the installed .NET SDK is {installed}, and global.json pins {pinned} — "
            "a rebuild from another SDK cannot say whether the committed DLLs match their sources")


def build_script(generators_root: Path):
    """build.py as a module: it owns which assembly is deployed where, and how each one is built."""
    script = generators_root / "build.py"
    try:
        spec = importlib.util.spec_from_file_location("velvet_generators_build", script)
        if spec is None or spec.loader is None:
            raise Refusal(f"{script} could not be loaded as a module")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    except Refusal:
        raise
    except Exception as error:
        raise Refusal(f"could not load {script}: {error}") from error
    if module.CONFIGURATION != CONFIGURATION:
        raise Refusal(
            f"build.py would build {module.CONFIGURATION} and the committed pair is {CONFIGURATION} "
            f"— unset CONFIGURATION in the environment")
    return module


def deployments(module, generators_root: Path) -> list[Deployment]:
    try:
        declared = list(module.DEPLOYMENTS)
    except Exception as error:
        raise Refusal(f"could not read DEPLOYMENTS from build.py: {error}") from error

    if not declared:
        raise Refusal("build.py declares no deployments, so this would compare nothing")
    return [Deployment(name, generators_root / project, generators_root / built,
                       generators_root / deploy_dir / f"{name}.dll")
            for name, project, built, deploy_dir in declared]


def build(module, generators_root: Path, planned: list[Deployment]) -> None:
    """Builds through build.py's own command but stops short of its deployment, which would copy the
    rebuild over the committed DLL and leave the comparison to be made against itself."""
    for deployment in planned:
        print(f"[deployed-dll-check] dotnet build {deployment.name} -c {CONFIGURATION}", flush=True)
        try:
            result = subprocess.run(
                module.build_command(str(deployment.project)),
                cwd=str(generators_root), check=False)
        except OSError as error:
            raise Refusal(f"could not run dotnet build for {deployment.project}: {error}") from error
        if result.returncode != 0:
            raise Refusal(f"dotnet build failed for {deployment.project}, leaving nothing to compare")


def compare(planned: list[Deployment]) -> list[str]:
    """One message per deployment that is unreadable or differs; an empty list means every pair matched."""
    problems = []
    for deployment in planned:
        if not deployment.built.is_file():
            problems.append(f"{deployment.name}: the build produced no {deployment.built}")
            continue
        if not deployment.committed.is_file():
            problems.append(f"{deployment.name}: nothing is committed at {deployment.committed}")
            continue
        try:
            built_bytes = deployment.built.read_bytes()
            committed_bytes = deployment.committed.read_bytes()
        except OSError as error:
            problems.append(f"{deployment.name}: could not read both assemblies: {error}")
            continue
        if built_bytes != committed_bytes:
            problems.append(
                f"{deployment.name}: the committed DLL is not what the sources build "
                f"(committed {len(committed_bytes)} bytes, rebuilt {len(built_bytes)} bytes, "
                f"{describe_difference(built_bytes, committed_bytes)})")
    return problems


def describe_difference(built: bytes, committed: bytes) -> str:
    """Where the two part company, so a failure that is not a missed redeploy can be told apart."""
    shared = min(len(built), len(committed))
    differing = [offset for offset in range(shared) if built[offset] != committed[offset]]
    if not differing:
        return f"identical over the first {shared} bytes, then one runs longer"
    return f"first differing offset {differing[0]}, {len(differing) + abs(len(built) - len(committed))} in all"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=str(Path(__file__).resolve().parents[2]))
    args = parser.parse_args()

    generators_root = Path(args.repo_root) / GENERATORS_REL
    try:
        if not generators_root.is_dir():
            raise Refusal(f"{generators_root} does not exist")
        problem = sdk_problem(pinned_sdk_version(generators_root),
                              installed_sdk_version(generators_root))
        if problem:
            raise Refusal(problem)
        module = build_script(generators_root)
        planned = deployments(module, generators_root)
        build(module, generators_root, planned)
    except Refusal as refusal:
        print(f"error: the committed generator DLLs could not be checked: {refusal}", file=sys.stderr)
        return REFUSED_EXIT

    problems = compare(planned)
    if not problems:
        print(f"[deployed-dll-check] {len(planned)} committed DLL(s) match their sources.")
        return 0

    for message in problems:
        print(f"  {message}", file=sys.stderr)
    print("\nRedeploy them and commit what changes:", file=sys.stderr)
    print(f"  cd {GENERATORS_REL} && ./build.py", file=sys.stderr)
    return MISMATCH_EXIT


if __name__ == "__main__":
    sys.exit(main())
