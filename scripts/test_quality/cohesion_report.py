#!/usr/bin/env python3
"""Print cohesion and coupling rankings over the package's non-test sources."""

import argparse
import subprocess
import sys
from pathlib import Path

GENERATORS_REL = Path("Packages/com.velvet.core/Generators~")
PROJECT_REL = Path("src/Velvet.CohesionReport/Velvet.CohesionReport.csproj")


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="Unity project root (default: cwd)")
    parser.add_argument("--configuration", default="Release", help="dotnet configuration (default: Release)")
    args = parser.parse_args()

    project_root = Path(args.project).resolve()
    generators = project_root / GENERATORS_REL
    project = generators / PROJECT_REL
    if not project.is_file():
        print(f"error: cohesion report project not found at {project}", file=sys.stderr)
        return 1

    result = subprocess.run(
        [
            "dotnet", "run", "--project", str(project), "-c", args.configuration,
            "--nologo", "--verbosity", "quiet",
        ],
        cwd=generators,
    )
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
