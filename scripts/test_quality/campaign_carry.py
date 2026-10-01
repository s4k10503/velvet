#!/usr/bin/env python3
"""What a push since an earlier campaign on the same branch can have changed about the kills it recorded.

`mutation_check.py --carry-to` asks this before it carries a kill forward. A kill is a test failing on the
mutated program under a green baseline; it still holds at the later head when neither that program nor
that test can have changed. So this answers from the paths the push changed, in two tiers:

- A change to anything but a test assembly's C#, or its `.meta`, stops every kill from carrying. That
  is an allowlist, so a path nobody thought of lands on the side that measures everything.
- A changed test source reaches its whole test assembly, and every assembly referencing that one. A
  source is an input to its assembly without anything naming it: the IL post-processor weaves or leaves
  the assembly by what any of its files declares, and a case can reflect over the assembly it runs in.
  A kill carries only on a case whose fixture lives in an assembly the push did not reach.

A case whose fixture's source reads across the loaded assemblies, directly or through a type declared
outside the test sources and `CodeGen/` that does, can read a test assembly the push did reach, so a
kill resting on one does not carry either. What a test
does to shared state while it runs is not read: a kill taken by an area's own assemblies already stands
in `mutation_check.py` without the rest of the suite running beside it.
"""

import json
import re
import subprocess
from pathlib import Path, PurePosixPath

# A type a file declares, past any comment between the keyword and the name.
DECLARED = re.compile(
    r"\b(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)(?:\s|/\*.*?\*/|//[^\n]*\n)+@?"
    r"([A-Za-z_]\w*)", re.S)
DELEGATE = re.compile(r"\bdelegate\s+[\w<>\[\],.?\s]*?\s@?([A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\(")
# What reads the loaded assemblies rather than the one a case runs in.
ACROSS_ASSEMBLIES = re.compile(
    r"\bGetAssemblies\b|\bTypeCache\.|\bAssembly\.Load\w*|\bAppDomain\.CurrentDomain\b|\bCompilationPipeline\b")
# Words DECLARED can capture that are not a type's name: `where T : class where U : struct`.
KEYWORDS = {"where", "class", "struct", "new", "unmanaged", "notnull", "default", "enum", "interface"}
GUID = re.compile(r"^guid:\s*([0-9a-f]{32})\s*$", re.M)


def git(project, *arguments):
    return subprocess.run(["git", "-C", str(project), *arguments], capture_output=True, text=True)


def ancestor(project, previous):
    """Whether `previous` is HEAD or one of its ancestors."""
    return git(project, "merge-base", "--is-ancestor", previous, "HEAD").returncode == 0


def changed_paths(project, previous):
    """Every repository-relative path the working tree holds differently from `previous`, untracked
    ones included, or None where git cannot diff the two."""
    diff = git(project, "diff", "--name-only", "--no-renames", "-z", previous)
    untracked = git(project, "ls-files", "--others", "--exclude-standard", "-z")
    if diff.returncode or untracked.returncode:
        return None
    return sorted({name for name in diff.stdout.split("\0") + untracked.stdout.split("\0") if name})


def test_source(relative):
    """Whether a path sits under a test directory that Unity compiles: a `Tests` directory, or one named
    `<something>.Tests`, with no `~` directory above it."""
    parts = PurePosixPath(relative).parts
    if any(part.endswith("~") for part in parts):
        return False
    return any(part == "Tests" or part.endswith(".Tests") for part in parts[:-1])


def nearest_asmdef(project, relative):
    """(the nearest .asmdef or .asmref at or above a path, relative) or None. A deleted path is answered
    from the directories still standing above it."""
    for parent in PurePosixPath(relative).parents:
        directory = project / parent
        if not directory.is_dir():
            continue
        found = sorted(directory.glob("*.asmdef")) + sorted(directory.glob("*.asmref"))
        if found:
            return found[0].relative_to(project).as_posix()
        if parent == PurePosixPath("."):
            break
    return None


def assembly(project, relative):
    """The test assembly a test source compiles into, or None where it is not one this can name."""
    asmdef = nearest_asmdef(project, relative)
    if asmdef is None or not asmdef.endswith(".asmdef") or not test_source(asmdef):
        return None
    try:
        return json.loads((project / asmdef).read_text(encoding="utf-8"))["name"]
    except (OSError, ValueError, KeyError):
        return None


def blocker(project, relative):
    """Why a change to `relative` stops every kill from carrying, or None for a test source whose reach
    `touched` reads."""
    path = PurePosixPath(relative)
    if any(part.endswith("~") for part in path.parts):
        return "Unity compiles nothing under a ~ directory, and what reads it there is not read here"
    folder_meta = path.name.endswith(".meta") and "." not in path.name[:-len(".meta")]
    if not (path.name.endswith(".cs") or path.name.endswith(".cs.meta") or folder_meta):
        return "it is not C#"
    if assembly(project, relative) is None:
        return "no test assembly's .asmdef is the nearest one above it"
    return None


def declared(text):
    names = set(DECLARED.findall(text)) | set(DELEGATE.findall(text))
    return names - KEYWORDS


def references(project):
    """Assembly name -> the names its .asmdef references, every GUID reference resolved by .meta."""
    asmdefs = {}
    guids = {}
    for relative in tracked(project, "*.asmdef"):
        try:
            body = json.loads((project / relative).read_text(encoding="utf-8"))
        except (OSError, ValueError):
            continue
        asmdefs[body.get("name")] = body.get("references") or []
        try:
            meta = (project / (relative + ".meta")).read_text(encoding="utf-8")
        except OSError:
            continue
        found = GUID.search(meta)
        if found:
            guids[found.group(1)] = body.get("name")
    return {name: {guids.get(ref[len("GUID:"):], ref) if ref.startswith("GUID:") else ref
                   for ref in refs}
            for name, refs in asmdefs.items()}


def referencing(project, assemblies):
    """`assemblies` with every assembly that references one of them, directly or through another."""
    graph = references(project)
    found = set(assemblies)
    grew = True
    while grew:
        grew = False
        for name, refs in graph.items():
            if name not in found and refs & found:
                found.add(name)
                grew = True
    return found


def tracked(project, pattern):
    listed = git(project, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", pattern)
    return [name for name in listed.stdout.split("\0")
            if name and not any(part.endswith("~") for part in PurePosixPath(name).parts)]


def current(project, relative):
    try:
        return (project / relative).read_text(encoding="utf-8")
    except OSError:
        return None


def touched(project, paths):
    """(the reason nothing can carry, None) or (None, the test assemblies the push reached) for the paths
    `changed_paths` listed."""
    for relative in paths:
        reason = blocker(project, relative)
        if reason is not None:
            return "{} changed, and {}".format(relative, reason), None
    return None, referencing(project, {assembly(project, relative) for relative in paths})


def strip_arguments(name):
    """A case's full name with every parenthesised argument list taken out, quotes respected."""
    kept, depth, quote = [], 0, None
    for character in name:
        if quote:
            if character == quote:
                quote = None
            continue
        if depth and character in "\"'":
            quote = character
        elif character == "(":
            depth += 1
        elif character == ")":
            depth = max(depth - 1, 0)
        elif not depth:
            kept.append(character)
    return "".join(kept)


def fixture_of(case):
    """The class a case's full name runs under, the outermost where it is nested, or None."""
    parts = strip_arguments(case).split(".")
    if len(parts) < 2:
        return None
    outer = re.sub(r"<.*", "", parts[-2].split("+")[0])
    return outer or None


def sources(project):
    """(relative path, text) of every C# source Unity compiles under Packages/ and Assets/."""
    for relative in tracked(project, "*.cs"):
        if relative.startswith(("Packages/", "Assets/")):
            text = current(project, relative)
            if text is not None:
                yield relative, text


def reflecting(texts):
    """The paths among `texts` (path -> code) that can read a test assembly other than their own: a
    source spelling `ACROSS_ASSEMBLIES`, a runtime source naming a type such a runtime source declares,
    followed to every runtime source naming one in turn, and a test source naming any of those types.

    `CodeGen/` is no runtime source here: the post-processor's reads happen while an assembly compiles,
    over that assembly and its references, and a push reaching that assembly reaches it whole already.
    """
    runtime = {relative for relative in texts if not test_source(relative) and "/CodeGen/" not in relative}
    found = {relative for relative, text in texts.items() if ACROSS_ASSEMBLIES.search(text)}
    names, pattern = set(), None
    while True:
        grown = set().union(*(declared(texts[relative]) for relative in found & runtime)) - names
        if not grown:
            break
        names |= grown
        pattern = re.compile(r"\b(?:{})\b".format("|".join(sorted(map(re.escape, names)))))
        found |= {relative for relative in runtime if pattern.search(texts[relative])}
    if pattern is not None:
        found |= {relative for relative, text in texts.items() if test_source(relative) and pattern.search(text)}
    return found


class FixtureIndex:
    """Class name -> the test assemblies whose sources declare a type of that name, and the names declared
    in a test source `reflecting` holds."""

    def __init__(self, project, code=lambda text: text):
        texts = {relative: code(text) for relative, text in sources(project)}
        across = reflecting(texts)
        self.owners = {}
        self.reflecting = set()
        for relative, text in texts.items():
            if not test_source(relative):
                continue
            owner = assembly(project, relative)
            for name in declared(text):
                self.owners.setdefault(name, set()).add(owner)
                if relative in across:
                    self.reflecting.add(name)

    def stands(self, case, reached):
        """Whether a case's kill can be taken at this head: its fixture is declared in one test assembly,
        which the push did not reach, and in no source reflecting across the loaded assemblies."""
        fixture = fixture_of(case)
        owners = self.owners.get(fixture, set())
        if len(owners) != 1 or None in owners:
            return False
        return fixture not in self.reflecting and not owners & reached
