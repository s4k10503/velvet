#!/usr/bin/env python3
"""Rebuild the source generators and redeploy their committed DLLs.

Set CONFIGURATION to build something other than Release.
"""

import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent

CONFIGURATION = os.environ.get("CONFIGURATION", "Release")

STYLE_TABLE_PROJECT = "src/Velvet.StyleTable/Velvet.StyleTable.csproj"
STYLE_SHEET_DIR = "../Runtime/Styles"
STYLE_TABLE_OUTPUT = "../Runtime/Styling/StyleUtilityProperties.g.cs"

DEPLOYMENTS = (
    (
        "Velvet.SourceGenerators",
        "src/Velvet.SourceGenerators/Velvet.SourceGenerators.csproj",
        f"src/Velvet.SourceGenerators/bin/{CONFIGURATION}/netstandard2.0/Velvet.SourceGenerators.dll",
        "../Runtime/Plugins/Generators",
    ),
    (
        "Velvet.SourceGenerators.CodeFixes",
        "src/Velvet.SourceGenerators.CodeFixes/Velvet.SourceGenerators.CodeFixes.csproj",
        f"src/Velvet.SourceGenerators.CodeFixes/bin/{CONFIGURATION}/netstandard2.0/"
        "Velvet.SourceGenerators.CodeFixes.dll",
        "../Runtime/Plugins/Analyzers",
    ),
)


def run(command, failure):
    result = subprocess.run(command, cwd=HERE)
    if result.returncode != 0:
        raise SystemExit(f"error: {failure}")


def main():
    # The utility property table is a function of the bundled stylesheets alone, so it is derived
    # here into committed source rather than recomputed inside every consumer's compile. It runs
    # first: a stylesheet the derivation cannot model must stop the build before any assembly is
    # deployed.
    print(f"[Velvet.StyleTable] dotnet run -c {CONFIGURATION}", flush=True)
    run(
        [
            "dotnet", "run", "--project", STYLE_TABLE_PROJECT, "-c", CONFIGURATION,
            "--verbosity", "quiet", "--",
            "--styles", STYLE_SHEET_DIR, "--output", STYLE_TABLE_OUTPUT,
        ],
        "the utility property table could not be derived",
    )

    # Both assemblies build before either is deployed: a failure in the second build would
    # otherwise leave one refreshed DLL beside one stale DLL, which is the mismatch the deployed
    # pair exists to avoid.
    for name, project, _, _ in DEPLOYMENTS:
        print(f"[{name}] dotnet build -c {CONFIGURATION}", flush=True)
        run(
            ["dotnet", "build", project, "-c", CONFIGURATION, "--nologo"],
            f"dotnet build failed: {project}",
        )

    for name, _, built_dll, deploy_dir in DEPLOYMENTS:
        destination = HERE / deploy_dir / f"{name}.dll"
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(HERE / built_dll, destination)
        print(f"[{name}] Deployed to {deploy_dir}/{name}.dll", flush=True)

    return 0


if __name__ == "__main__":
    sys.exit(main())
