#!/usr/bin/env python3
"""Compile the assemblies this project's asmdefs declare with the editor's own Roslyn, no editor running.

    python3 scripts/unity/compile_check.py                          # every asmdef assembly, in reference order
    python3 scripts/unity/compile_check.py Velvet Velvet.Editor     # these two, with what they reference
    python3 scripts/unity/compile_check.py --project <checkout>     # sources here, the editor's files there

A compile error that first shows in CI costs the round that found it, and nothing shorter read the
sources before the push. This runs the compiler the editor runs, with the response file the editor
last wrote for each assembly, over the sources its asmdef owns now. It proves that those sources
compile and nothing more: the IL post-processor is not run, no test is, and a source no asmdef owns
(`Assets/Editor` holds this project's) is compiled by the editor into `Assembly-CSharp-Editor`, which
this does not build.

The editor leaves the response file of its last compile of each assembly under
`<project>/Library/Bee/artifacts/*/<name>.rsp`. Every option line in it — defines, engine, package and
precompiled references, analyzers, language version, warnings, whichever of `-` and `/` spells it —
is reused as it stands, except what the sources have moved since: the source list is the asmdef's
folder now, `-unsafe` is the asmdef's `allowUnsafeCode` now, the output is this run's, and a
reference to one of this project's own assemblies is the copy this run compiled. A reference or an
analyzer the editor took from inside the project is taken from the same path under the sources, where
the sources hold it, so a branch's committed generator is the one that runs. The `csc.rsp` beside the asmdef is passed after
the editor's lines: an option it adds is in force, a scalar one it changes takes the later value, and
a list-valued one — `-define:`, `-warnaserror:` — accumulates, so a value it has dropped or renamed
since the editor's compile is still in force, as the editor inlined it, until the editor compiles
again. A package reference the asmdef has dropped since is still passed the same way, and a
`precompiledReferences` entry it has gained since is not passed until then. Defines the editor
derived from `versionDefines` are reused as they were, and an asmdef reference in `GUID:` form is
not resolved.

The sources are every `.cs` under the asmdef's folder that no asmdef nested below it owns, leaving
out a folder or file whose name ends in `~` or begins with `.` — the editor's own source lists here
hold nothing under such a name. A selection is compiled with every assembly it references that this
project declares, in reference order, because the copies under `Library/ScriptAssemblies` are as old
as the editor's last compile: one there lacked a member the sources had since gained, and a test
assembly compiled against it was red for nothing. For the same reason an assembly whose reference
failed in this run is not compiled against that stale copy; it is reported skipped. An assembly a
package declares is referenced as the editor referenced it.

`--project` names the checkout the editor has compiled, for a worktree whose `Library` holds no
response files; the sources are still read from the tree the asmdefs are in. Response files and
assemblies go to a directory made for the run under the sources' `Logs/`, which git ignores.
`dotnet` is taken from PATH, as `Generators~` already requires.

Exits 0 when every selected assembly compiled, 1 when one did not or could not be set up.
"""

import argparse
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

PROJECT_MARKER = Path("ProjectSettings") / "ProjectVersion.txt"
LIBRARY_ASSEMBLIES = Path("Library") / "ScriptAssemblies"
EDITOR_RESPONSES = Path("Library") / "Bee" / "artifacts"
RESPONSE_FILE = "csc.rsp"

# Left out of the asmdef listing. The packages' asmdefs under `Library` are the editor's to compile,
# and what an asmdef here references among them is already on its editor line; the other three hold
# what tooling writes, copies of this tree among it, which would make a second asmdef of every name.
SKIPPED_DIRECTORIES = {"Library", "Logs", "Temp", "obj"}

EDITORS = Path("/Applications/Unity/Hub/Editor")
CSC_IN_EDITOR = Path("Unity.app/Contents/Resources/Scripting/DotNetSdkRoslyn/csc.dll")

# Option names as `option_of` lowercases them, without their `-` or `/`.
REFERENCE_OPTIONS = {"r", "reference"}
PATH_OPTIONS = {"analyzer", "additionalfile"}
# The run's own output replaces the first two; the asmdef's `allowUnsafeCode` replaces the rest.
REPLACED_OPTIONS = {"out", "refout", "unsafe", "unsafe+", "unsafe-"}

ERROR_LINE = re.compile(r"\berror [A-Z]+\d+\b")


def hidden(name):
    return name.endswith("~") or name.startswith(".")


def project_root(start):
    """The Unity project holding `start`, or None where no directory above it carries the marker."""
    for candidate in [start, *start.parents]:
        if (candidate / PROJECT_MARKER).is_file():
            return candidate
    return None


def asmdefs_under(tree):
    found = []
    for directory, subdirectories, files in os.walk(tree):
        subdirectories[:] = sorted(
            name for name in subdirectories if not hidden(name) and name not in SKIPPED_DIRECTORIES)
        found.extend(Path(directory) / name for name in files if name.endswith(".asmdef"))
    return sorted(found)


def definition_of(asmdef):
    return json.loads(asmdef.read_text(encoding="utf-8"))


def name_of(asmdef):
    return definition_of(asmdef)["name"]


def references_of(asmdef):
    return list(definition_of(asmdef).get("references", []))


def allows_unsafe(asmdef):
    return bool(definition_of(asmdef).get("allowUnsafeCode", False))


def sources_of(asmdef, asmdefs):
    """Every `.cs` the asmdef owns: under its folder, outside any asmdef folder nested below it."""
    base = asmdef.parent
    nested = [other.parent for other in asmdefs if other != asmdef and base in other.parent.parents]
    sources = []
    for path in sorted(base.rglob("*.cs")):
        if any(hidden(part) for part in path.relative_to(base).parts):
            continue
        if any(folder in path.parents for folder in nested):
            continue
        sources.append(path)
    return sources


def compile_order(asmdefs):
    """Assembly names, each after every assembly it references that is among `asmdefs`."""
    by_name = {name_of(asmdef): asmdef for asmdef in asmdefs}
    order, visited = [], set()

    def visit(name):
        if name in visited or name not in by_name:
            return
        visited.add(name)
        for referenced in references_of(by_name[name]):
            visit(referenced)
        order.append(name)

    for name in sorted(by_name):
        visit(name)
    return order


def with_references(selected, by_name):
    """`selected` and, transitively, every assembly it references that `by_name` declares."""
    closed, pending = set(), list(selected)
    while pending:
        name = pending.pop()
        if name in closed or name not in by_name:
            continue
        closed.add(name)
        pending.extend(references_of(by_name[name]))
    return closed


def selected_names(requested, by_name):
    """The assembly names `requested` spells, as names or as the tree's own asmdef paths; a KeyError
    names a miss, a path to an asmdef outside the tree among them."""
    names = set()
    for item in requested:
        path = Path(item)
        if path.suffix == ".asmdef" and path.is_file():
            name = name_of(path)
            if name not in by_name or by_name[name].resolve() != path.resolve():
                raise KeyError(item)
        elif item in by_name:
            name = item
        else:
            raise KeyError(item)
        names.add(name)
    return names


def blocked_by(asmdef, failed):
    """The assemblies among `failed` this asmdef references."""
    return sorted(set(references_of(asmdef)) & set(failed))


def editor_response(project, name):
    """The response file the editor last wrote for `name`, or None where it has not compiled it."""
    candidates = list((project / EDITOR_RESPONSES).glob(f"*/{name}.rsp"))
    return max(candidates, key=lambda path: path.stat().st_mtime) if candidates else None


def option_of(line):
    """(name, value) of an option line, the name lowercased without its `-` or `/`; None for a source."""
    if not line or line[0] not in "-/":
        return None
    name, _, value = line[1:].partition(":")
    return name.lower(), value


def assembly_name(path):
    """`Velvet` for `.../Velvet.dll` and for the editor's `.../Velvet.ref.dll` alike."""
    name = Path(path).name
    for suffix in (".ref.dll", ".dll"):
        if name.endswith(suffix):
            return name[:-len(suffix)]
    return name


def placed(value, project, sources_root):
    """A path the editor's response file names, absolute, and under the sources where it was
    inside the project and the sources hold the same file."""
    path = Path(value.strip('"'))
    if not path.is_absolute():
        path = project / path
    try:
        relative = path.relative_to(project)
    except ValueError:
        return path
    moved = sources_root / relative
    return moved if moved.is_file() else path


def response_lines(name, asmdef, editor_lines, own_names, sources, out, project, sources_root):
    """What csc is handed: the editor's lines with the replacements the docstring names, then the
    asmdef's own references, then the `csc.rsp` beside it, then the sources. Every path is quoted."""
    lines = ["-nostdlib+", f'-out:"{out / name}.dll"']
    referenced = set()
    for raw in editor_lines:
        line = raw.strip()
        option = option_of(line)
        if option is None:
            continue
        kind, value = option
        if kind in REPLACED_OPTIONS:
            continue
        if kind in REFERENCE_OPTIONS:
            path = placed(value, project, sources_root)
            if assembly_name(path) in own_names:
                continue
            referenced.add(assembly_name(path))
            lines.append(f'-reference:"{path}"')
        elif kind in PATH_OPTIONS:
            lines.append(f'{line.split(":", 1)[0]}:"{placed(value, project, sources_root)}"')
        else:
            lines.append(line)
    if allows_unsafe(asmdef):
        lines.append("-unsafe")
    for referenced_name in references_of(asmdef):
        if referenced_name in referenced:
            continue
        referenced.add(referenced_name)
        built = out / f"{referenced_name}.dll"
        lines.append(f'-reference:"{built if built.is_file() else project / LIBRARY_ASSEMBLIES / (referenced_name + ".dll")}"')
    beside = asmdef.parent / RESPONSE_FILE
    if beside.is_file():
        lines.append(f'@"{beside}"')
    lines.extend(f'"{source}"' for source in sources)
    return lines


def default_csc(sources_root):
    """The installed editor's compiler, on macOS; None elsewhere, where `--csc` names it."""
    if platform.system() != "Darwin":
        return None
    text = (sources_root / PROJECT_MARKER).read_text(encoding="utf-8")
    version = re.search(r"m_EditorVersion:\s*(\S+)", text)
    return EDITORS / version.group(1) / CSC_IN_EDITOR if version else None


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("assemblies", nargs="*",
                        help="asmdef paths or assembly names; every asmdef under the sources by default")
    parser.add_argument("--sources", type=Path,
                        help="the Unity project whose sources are compiled (default: the one above the "
                             "working directory)")
    parser.add_argument("--project", type=Path,
                        help="the checkout the editor has compiled, holding Library (default: the sources)")
    parser.add_argument("--out", type=Path,
                        help="where response files and assemblies go (default: a new directory under "
                             "the sources' Logs)")
    parser.add_argument("--csc", type=Path, help="the editor's csc.dll (default: the installed editor's, on macOS)")
    args = parser.parse_args()

    sources_root = args.sources or project_root(Path.cwd().resolve())
    if sources_root is None or not (sources_root / PROJECT_MARKER).is_file():
        print(f"No Unity project above {Path.cwd()}; pass --sources.", file=sys.stderr)
        return 1
    sources_root = sources_root.resolve()
    project = (args.project or sources_root).resolve()
    csc = args.csc or default_csc(sources_root)
    if csc is None or not csc.is_file():
        print(f"No compiler at {csc or '(no default on this platform)'}; pass --csc <csc.dll>.", file=sys.stderr)
        return 1
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        print("No dotnet on PATH.", file=sys.stderr)
        return 1

    asmdefs = asmdefs_under(sources_root)
    by_name = {}
    for asmdef in asmdefs:
        name = name_of(asmdef)
        if name in by_name:
            print(f"Two asmdefs are named {name}: {by_name[name]} and {asmdef}.", file=sys.stderr)
            return 1
        by_name[name] = asmdef
    try:
        selected = selected_names(args.assemblies, by_name) if args.assemblies else set(by_name)
    except KeyError as missing:
        print(f"No asmdef under {sources_root} is {missing.args[0]}.", file=sys.stderr)
        return 1
    selected = with_references(selected, by_name)

    out = args.out
    if out is None:
        (sources_root / "Logs").mkdir(exist_ok=True)
        out = Path(tempfile.mkdtemp(prefix="compile_check.", dir=sources_root / "Logs"))
    out = out.resolve()
    out.mkdir(parents=True, exist_ok=True)

    rows, failed = [], set()
    for name in (n for n in compile_order(asmdefs) if n in selected):
        asmdef = by_name[name]
        blocked = blocked_by(asmdef, failed)
        if blocked:
            print(f"{name}: not compiled, since {', '.join(blocked)} failed.", file=sys.stderr)
            failed.add(name)
            rows.append((name, "skipped", 0, 0.0))
            continue
        editor = editor_response(project, name)
        if editor is None:
            print(f"{name}: no {project / EDITOR_RESPONSES}/*/{name}.rsp — the editor writes one when it compiles "
                  f"the project; open {project} in the editor once, or name a checkout it has compiled with "
                  "--project.", file=sys.stderr)
            failed.add(name)
            rows.append((name, "no rsp", 0, 0.0))
            continue
        sources = sources_of(asmdef, asmdefs)
        lines = response_lines(name, asmdef, editor.read_text(encoding="utf-8").splitlines(), set(by_name),
                               sources, out, project, sources_root)
        response = out / f"{name}.rsp"
        response.write_text("\n".join(lines) + "\n", encoding="utf-8")
        started = time.monotonic()
        run = subprocess.run([dotnet, str(csc), "-noconfig", f"@{response}"], capture_output=True, text=True)
        elapsed = time.monotonic() - started
        errors = [line for line in run.stdout.splitlines() if ERROR_LINE.search(line)]
        if run.returncode != 0:
            failed.add(name)
            for line in errors or run.stdout.splitlines()[-20:] or [run.stderr.strip()]:
                print(line)
        rows.append((name, "ok" if run.returncode == 0 else "FAILED", len(sources), elapsed))

    width = max([len("assembly")] + [len(row[0]) for row in rows])
    print(f"{'assembly':{width}}  result   sources  seconds")
    for name, result, count, elapsed in rows:
        print(f"{name:{width}}  {result:7}  {count:7}  {elapsed:7.1f}")
    print(f"Response files and assemblies: {out}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
