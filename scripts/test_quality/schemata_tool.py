#!/usr/bin/env python3
"""Build and run the schemata rewriter with the editor's own .NET runtime and Roslyn.

Prototype. `MutantSchemata.cs` is compiled once per source digest into Logs/, against the runtime and the
compiler the editor ships, so the parse and the compile check it makes are the ones Unity's build makes.
"""

import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "schemata" / "MutantSchemata.cs"


def scripting(unity):
    """The editor's Scripting directory, from the editor binary on either platform."""
    binary = Path(unity).resolve()
    for candidate in (binary.parent.parent / "Resources" / "Scripting",  # macOS: Contents/MacOS/Unity
                      binary.parent / "Data"):                            # Linux: Editor/Unity
        if (candidate / "DotNetSdkRoslyn").exists():
            return candidate
    raise SystemExit("no DotNetSdkRoslyn beside {}".format(unity))


def runtime(root):
    shared = root / "NetCoreRuntime" / "shared" / "Microsoft.NETCore.App"
    versions = sorted(shared.iterdir())
    return root / "NetCoreRuntime" / "dotnet", versions[-1]


def build(unity, project):
    root = scripting(unity)
    dotnet, framework = runtime(root)
    roslyn = root / "DotNetSdkRoslyn"
    digest = hashlib.sha256(SOURCE.read_bytes() + str(framework).encode()).hexdigest()[:16]
    out = Path(project) / "Logs" / "schemata-tool" / digest
    tool = out / "MutantSchemata.dll"
    if tool.exists():
        return dotnet, tool
    out.mkdir(parents=True, exist_ok=True)
    references = []
    for dll in sorted(framework.glob("*.dll")):
        name = dll.name
        if name.startswith(("System.Private.", "Microsoft.VisualBasic")) and name != "System.Private.CoreLib.dll":
            continue
        references.append("-r:" + str(dll))
    for name in ("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll"):
        references.append("-r:" + str(roslyn / name))
    command = [str(dotnet), str(roslyn / "csc.dll"), "/noconfig", "/nologo", "/preferreduilang:en-US", "/nowarn:CS1701,CS1702",
               "-langversion:10", "-nullable:enable", "-target:exe", "-out:" + str(tool)] + references + [str(SOURCE)]
    built = subprocess.run(command, capture_output=True, text=True, env=dict(os.environ, DOTNET_gcServer="0"))
    errors = [line for line in built.stdout.splitlines() if ": error " in line]
    if built.returncode != 0 or errors:
        raise SystemExit("the rewriter did not build:\n" + "\n".join(errors or [built.stdout, built.stderr]))
    config = json.loads((roslyn / "csc.runtimeconfig.json").read_text())
    config["runtimeOptions"].setdefault("configProperties", {})["System.GC.Server"] = False
    (out / "MutantSchemata.runtimeconfig.json").write_text(json.dumps(config, indent=2))
    for name in ("Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll"):
        target = out / name
        if not target.exists():
            target.symlink_to(roslyn / name)
    return dotnet, tool


def rewrite(unity, project, request, scratch):
    """Runs the rewriter over `request` (the JSON the C# side reads) and returns its answer."""
    dotnet, tool = build(unity, project)
    scratch = Path(scratch)
    scratch.mkdir(parents=True, exist_ok=True)
    asked = scratch / "schemata-request.json"
    answered = scratch / "schemata-answer.json"
    asked.write_text(json.dumps(request))
    run = subprocess.run([str(dotnet), str(tool), str(asked), str(answered)], capture_output=True, text=True,
                         cwd=str(project), env=dict(os.environ, DOTNET_gcServer="0"))
    if not answered.exists():
        raise SystemExit("the rewriter answered nothing (exit {}):\n{}\n{}".format(
            run.returncode, run.stdout[-4000:], run.stderr[-4000:]))
    return json.loads(answered.read_text())


if __name__ == "__main__":
    print(build(sys.argv[1], sys.argv[2]))
