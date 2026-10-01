#!/usr/bin/env python3
"""What a push since an earlier campaign on the same branch can have changed about the kills it recorded.

`mutation_check.py --carry-to` asks this before it carries a kill forward. A kill is a test failing on the
mutated program under a green baseline; it still holds at the later head when neither that program nor
that test can have changed. So this answers from the paths the push changed, in two tiers:

- A change to anything but a test assembly's C# stops every kill from carrying. That is an allowlist,
  so a path nobody thought of lands on the side that measures everything.
- A changed test source stops the kills resting on the fixtures it declares. Where something outside
  the file can reach what it declares -- another source names one of its types, it extends a type, it
  opens a namespace, it declares something NUnit or Unity applies beyond the file -- the kills resting
  on its whole assembly stop instead, and on every assembly referencing that one.

What a test does to shared state while it runs is not read: a kill taken by an area's own assemblies
already stands in `mutation_check.py` without the rest of the suite running beside it.
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
NAMESPACE = re.compile(r"\bnamespace\s+([A-Za-z_][\w.]*)")
# What reaches past the file it is written in without any other source naming it: an attribute on the
# assembly, a using every file of it sees, an extension method, a fixture NUnit applies to a namespace,
# and the hooks Unity and its test runner call on their own.
BEYOND_THE_FILE = re.compile(
    r"\[\s*(?:assembly|module)\s*:|\bglobal\s+using\b|\(\s*(?:\[[^\]]*\]\s*)*this\s+[A-Za-z_@]"
    r"|\b(?:SetUpFixture|InitializeOnLoad\w*|RuntimeInitializeOnLoadMethod|InitializeOnEnterPlayMode"
    r"|DidReloadScripts|ModuleInitializer|AssetPostprocessor|PrebuildSetup|PostBuildCleanup)\b")
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


def show(project, revision, relative):
    found = git(project, "show", "{}:{}".format(revision, relative))
    return found.stdout if found.returncode == 0 else None


def current(project, relative):
    try:
        return (project / relative).read_text(encoding="utf-8")
    except OSError:
        return None


def named_elsewhere(project, word, own, uncommented):
    """Whether any file but `own` under Packages/ or Assets/ holds `word` where it can bind: in code
    rather than in a comment of a C# source, and anywhere at all in any other text file but prose."""
    found = git(project, "grep", "-l", "-w", "-I", "--untracked", "-F", "-e", word, "--",
                "Packages", "Assets")
    for relative in found.stdout.splitlines():
        if relative in own or relative.endswith((".md", ".meta")):
            continue
        if any(part.endswith("~") for part in PurePosixPath(relative).parts):
            continue
        if not relative.endswith(".cs"):
            return True
        text = current(project, relative)
        if text is None or re.search(r"\b{}\b".format(re.escape(word)), uncommented(text)):
            return True
    return False


def guid_held_elsewhere(project, relative, previous):
    """Whether a file other than the source's own .meta names the GUID the source is imported under, as
    a prefab, scene or asset holding one of its components does."""
    meta = relative + ".meta"
    for text in (current(project, meta), show(project, previous, meta)):
        found = GUID.search(text or "")
        if not found:
            continue
        hits = git(project, "grep", "-l", "-I", "--untracked", "-F", "-e", found.group(1))
        if any(hit != meta for hit in hits.stdout.splitlines()):
            return True
    return False


def self_contained(project, relative, previous, texts, names, uncommented):
    """Whether nothing outside the source can reach what either side of the change declares."""
    if any(BEYOND_THE_FILE.search(text) for text in texts):
        return False
    spaces = {space for text in texts for space in NAMESPACE.findall(text)}
    if any(len(NAMESPACE.findall(text)) > 1 for text in texts):
        return False
    own = {relative}
    for space in spaces:
        # A namespace no other source opens is a name an existing reference can start binding to.
        opened = git(project, "grep", "-l", "-E", r"namespace[[:space:]]+{}([^.[:alnum:]_]|$)".format(
            re.escape(space)), "--", "*.cs")
        if not [hit for hit in opened.stdout.splitlines() if hit not in own]:
            return False
    if any(named_elsewhere(project, name, own, uncommented) for name in names):
        return False
    return not guid_held_elsewhere(project, relative, previous)


class Touched:
    """What a push reached among the test sources: the fixtures by name, and the assemblies whole."""

    def __init__(self, classes=(), assemblies=()):
        self.classes = set(classes)
        self.assemblies = set(assemblies)


def touched(project, previous, paths, uncommented=lambda text: text):
    """(the reason nothing can carry, None) or (None, `Touched`) for the paths `changed_paths` listed."""
    for relative in paths:
        reason = blocker(project, relative)
        if reason is not None:
            return "{} changed, and {}".format(relative, reason), None
    reach = Touched()
    for relative in paths:
        name = PurePosixPath(relative).name
        owner = assembly(project, relative)
        if name.endswith(".meta") and not name.endswith(".cs.meta"):
            reach.assemblies.add(owner)
            continue
        source = relative[:-len(".meta")] if name.endswith(".meta") else relative
        texts = [text for text in (show(project, previous, source), current(project, source))
                 if text is not None]
        names = set().union(*(declared(text) for text in texts)) if texts else set()
        if self_contained(project, source, previous, texts, names, uncommented):
            reach.classes |= names
        else:
            reach.assemblies.add(owner)
    reach.assemblies = referencing(project, reach.assemblies)
    return None, reach


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


class FixtureIndex:
    """Class name -> the test assemblies whose sources declare a type of that name."""

    def __init__(self, project):
        self.project = project
        self.owners = {}
        for relative in tracked(project, "*.cs"):
            if not test_source(relative):
                continue
            text = current(project, relative)
            if text is None:
                continue
            owner = assembly(project, relative)
            for name in declared(text):
                self.owners.setdefault(name, set()).add(owner)

    def stands(self, case, reach):
        """Whether a case's kill can be taken at this head: its fixture is declared in one test assembly,
        which the push did not reach, and is no type a changed source declares."""
        fixture = fixture_of(case)
        owners = self.owners.get(fixture, set())
        if len(owners) != 1 or None in owners:
            return False
        return fixture not in reach.classes and not owners & reach.assemblies
