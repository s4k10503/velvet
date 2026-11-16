#!/usr/bin/env python3
"""Fail when Velvet's own render and commit code calls an entry's door rather than its body.

FiberExecutionContext.cs owns the model: a door is the method something outside Velvet's render and
commit calls, and a door reached while a root renders or commits does not run its work where it is
called. Velvet's render and commit code calls the body (the ...Work method) instead, which is what makes
the call sub-work of the entry on the stack. A door called from inside the render or commit moves that
work, and the assertions on the render and commit steps cannot see it, since it still runs inside an
entry.

Each reference to a door is read for the member it sits in, and only the members ALLOWED names may hold
one: the callbacks UI Toolkit, the panel scheduler and the main-thread pump run, the release and the held
work it runs, and the public and preview entries. The one other reference let through is one from the
door's own Type.Member, overloads included, to that same door, which is how a door re-enters itself where
it holds. Exemption is by member rather than by path, since doors and bodies share files and directories.

What it reads is the C# under the package's Runtime and Editor directories, outside Tests directories,
with comments and string literals blanked. A reference is a door's name as an identifier, a method group
included. A name other members share is read only where it is qualified as DOORS gives, or unqualified
in the file that declares the door; two doors share FlushPendingPassiveEffects and two ContinueReconcile,
and each pair is told apart by the qualifier, the type's own name for the static door and any other
receiver for the instance one. The member a reference sits in is the nearest declaration above it with
less indentation, a declaration being a line that opens with a modifier and names a method; that leans
on the indentation this repository's formatting keeps.

Run: python3 scripts/test_quality/entry_sites_check.py
"""

import argparse
import re
import sys
from pathlib import Path

PACKAGE = Path("Packages/com.velvet.core")
SOURCE_DIRS = ("Runtime", "Editor")

RENDERER = "Runtime/Reconciler/FiberRenderer.cs"
RECONCILER = "Runtime/Reconciler/Reconciler.cs"
MOUNT = "Runtime/Component/V.Mount.cs"
SCHEDULER = "Runtime/Reconciler/FiberBatchScheduler.cs"
EFFECTS = "Runtime/Reconciler/FiberEffects.cs"
WORK_LOOP = "Runtime/Reconciler/FiberWorkLoop.cs"

# door -> (pattern for a reference anywhere, file whose unqualified references are read, or None).
# A door whose name nothing else declares is read anywhere it appears.
DOORS = {
    "FiberBatchScheduler.DrainImmediate": (r"\bDrainImmediate\b(?!\w)", None),
    "FiberBatchScheduler.DrainDelayedTier": (r"\bDrainDelayedTier\b(?!\w)", None),
    "FiberBatchScheduler.DrainDelayed": (r"\bDrainDelayed\b(?!\w)", None),
    "FiberBatchScheduler.FlushImmediate": (r"\bFlushImmediate\b(?!\w)", None),
    "FiberBatchScheduler.FlushPendingPassiveEffects": (
        r"(?<!\bFiberEffects)\.FlushPendingPassiveEffects\b(?!\w)", SCHEDULER),
    "FiberEffects.FlushPendingPassiveEffects": (r"\bFiberEffects\.FlushPendingPassiveEffects\b(?!\w)", EFFECTS),
    "FiberBatchScheduler.FlushOwedExternalStoreRenders": (r"\bFlushOwedExternalStoreRenders\b(?!\w)", None),
    "FiberBatchScheduler.FlushDiscreteEvent": (r"\bFlushDiscreteEvent\b(?!\w)", None),
    "FiberWorkLoop.FlushState": (r"\bFlushState\b(?!\w)", None),
    "FiberWorkLoop.ContinueReconcile": (r"\bFiberWorkLoop\.ContinueReconcile\b(?!\w)", WORK_LOOP),
    "Reconciler.ContinueReconcile": (r"(?<!\bFiberWorkLoop)\.ContinueReconcile\b(?!\w)", RECONCILER),
    "FiberRenderer.RenderMounted": (r"\bRenderMounted\b(?!\w)", None),
    "FiberRenderer.NotifyAsyncResourceCompleted": (r"\bNotifyAsyncResourceCompleted\b(?!\w)", None),
    "FiberEffects.DrainScheduledPassiveEffects": (r"\bDrainScheduledPassiveEffects\b(?!\w)", None),
    "FiberEffects.RunScheduledEffects": (r"\bRunScheduledEffects\b(?!\w)", None),
    "FiberVirtualListController.UpdateVisibleRange": (r"\bUpdateVisibleRange\b(?!\w)", None),
    "FiberRenderer.Mount": (r"\bFiberRenderer\.Mount\b(?!\w)", RENDERER),
    "FiberRenderer.Unmount": (r"\bFiberRenderer\.Unmount\b(?!\w)", RENDERER),
    "FiberRenderer.Dispose": (r"\bFiberRenderer\.Dispose\b(?!\w)", RENDERER),
    "Reconciler.Reconcile": (r"(?<![\w])_?[Rr]econciler[!?]?\.Reconcile\b(?!\w)", RECONCILER),
    "V.Mount": (r"\bV\.Mount\b(?!\w)", MOUNT),
}

# caller -> the doors it may reference.
ALLOWED = {
    # What UI Toolkit, the panel scheduler and the main-thread pump run.
    "FiberBatchScheduler.RunImmediateCallback": {"FiberBatchScheduler.DrainImmediate"},
    "FiberBatchScheduler.RunDelayedCallback": {"FiberBatchScheduler.DrainDelayedTier"},
    "FiberBatchScheduler.OweExternalStoreFlush": {"FiberBatchScheduler.FlushOwedExternalStoreRenders"},
    "FiberWorkLoop.ScheduleStandaloneFlush": {"FiberWorkLoop.FlushState"},
    "FiberWorkLoop.ScheduleNextSlice": {"FiberWorkLoop.ContinueReconcile"},
    "FiberCommitWork.ScheduleParkedPassResume": {"FiberWorkLoop.ContinueReconcile"},
    "FiberEffects.PassiveEffectDrainCallback": {"FiberEffects.DrainScheduledPassiveEffects"},
    "FiberEffects.ScheduleStandaloneEffectRun": {"FiberEffects.RunScheduledEffects"},
    "FiberVirtualListController.OnGeometryChanged": {"FiberVirtualListController.UpdateVisibleRange"},
    "FiberVirtualListController.OnScrollValueChanged": {"FiberVirtualListController.UpdateVisibleRange"},
    "FiberDiscreteEventScope.Run": {"FiberBatchScheduler.FlushDiscreteEvent"},
    "Hooks.UseCore": {"FiberRenderer.NotifyAsyncResourceCompleted"},
    # Held work runs through the door it was asked for at, with nothing set (FiberExecutionContext).
    "FiberExecutionContext.Release": {
        "FiberBatchScheduler.FlushPendingPassiveEffects", "FiberBatchScheduler.FlushImmediate"},
    "FiberBatchScheduler.FiberBatchScheduler": {
        "FiberBatchScheduler.FlushDiscreteEvent", "FiberBatchScheduler.FlushPendingPassiveEffects"},
    "FiberRenderer.HoldFirstRender": {"FiberRenderer.RenderMounted"},
    "FiberVirtualListController.RenderHeldRange": {"FiberVirtualListController.UpdateVisibleRange"},
    # Public and preview entries, which something outside Velvet calls.
    "FiberRenderer.Mount": {"FiberRenderer.RenderMounted"},
    "FiberRenderer.NotifyAsyncResourceCompletedWork": {"FiberRenderer.NotifyAsyncResourceCompleted"},
    "FiberBatchScheduler.DrainDelayed": {"FiberBatchScheduler.DrainDelayedTier"},
    "FiberEffects.FlushPendingPassiveEffects": {"FiberEffects.RunScheduledEffects"},
    "VelvetPreviewHost.Mount": {"V.Mount"},
    "VelvetPreviewHost.Settle": {
        "FiberBatchScheduler.FlushPendingPassiveEffects", "FiberBatchScheduler.FlushImmediate",
        "FiberBatchScheduler.DrainDelayed"},
}

MODIFIERS = ("public", "private", "internal", "protected", "static", "override", "virtual", "sealed", "async",
             "unsafe", "extern", "new", "partial", "readonly", "abstract")
DECLARATION = re.compile(
    r"^\s*(?:(?:" + "|".join(MODIFIERS) + r")\s+)+[^=(;]*?\b(?P<name>\w+)\s*(?:<[^>()]*>)?\s*\(")
TYPE = re.compile(r"\b(?:class|struct|record|interface)\s+(?P<name>\w+)")


def blank(source):
    """The source with comments and string and character literals replaced by spaces, lines kept."""
    out = []
    i, n = 0, len(source)
    while i < n:
        c = source[i]
        nxt = source[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            end = source.find("\n", i)
            end = n if end < 0 else end
            out.append(" " * (end - i))
            i = end
        elif c == "/" and nxt == "*":
            end = source.find("*/", i + 2)
            end = n if end < 0 else end + 2
            out.append("".join(ch if ch == "\n" else " " for ch in source[i:end]))
            i = end
        elif c == "@" and nxt == '"' or c == "$" and nxt == "@" or c == "@" and nxt == "$":
            start = source.index('"', i)
            j = start + 1
            while j < n and not (source[j] == '"' and (j + 1 >= n or source[j + 1] != '"')):
                j += 2 if source[j] == '"' else 1
            out.append(source[i:start] + " " * (j + 1 - start))
            i = j + 1
        elif c == '"' or c == "'":
            j = i + 1
            while j < n and source[j] != c and source[j] != "\n":
                j += 2 if source[j] == "\\" else 1
            out.append(" " * (j + 1 - i))
            i = j + 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def indentation(line):
    return len(line) - len(line.lstrip(" "))


def opens_nothing(line):
    """A line the walk up passes over: blank, a brace on its own, which sits at its header's indentation, or a
    preprocessor directive, which sits at the margin."""
    stripped = line.strip()
    return stripped in ("", "{", "}") or stripped.startswith("#")


def declared(line):
    match = DECLARATION.match(line)
    return match.group("name") if match else None


def enclosing(lines, index):
    """'Type.Member' for the member the reference on lines[index] sits in, or None outside every one."""
    own = declared(lines[index])
    if own is not None:
        member, at = own, index
    else:
        member, at = None, index
        limit = indentation(lines[index])
        for j in range(index - 1, -1, -1):
            if opens_nothing(lines[j]) or indentation(lines[j]) >= limit:
                continue
            name = declared(lines[j])
            if name is not None:
                member, at = name, j
                break
            limit = indentation(lines[j])
        if member is None:
            return None
    limit = indentation(lines[at])
    for j in range(at - 1, -1, -1):
        if not opens_nothing(lines[j]) and indentation(lines[j]) < limit:
            found = TYPE.search(lines[j])
            if found:
                return f"{found.group('name')}.{member}"
            limit = indentation(lines[j])
    return member


def bare(door):
    return door.rsplit(".", 1)[-1]


def references(relative, source):
    """(line number, door, caller) for every door a file references outside the door's own declaration."""
    lines = blank(source).split("\n")
    found = set()
    for door, (pattern, home) in DOORS.items():
        name = bare(door)
        patterns = [re.compile(pattern)]
        if home == relative:
            patterns.append(re.compile(r"(?<![\w.])" + re.escape(name) + r"\b(?!\w)"))
        for index, line in enumerate(lines):
            declaration = DECLARATION.match(line)
            for compiled in patterns:
                for match in compiled.finditer(line):
                    if declaration and declaration.group("name") == name \
                            and match.start() <= declaration.start("name") < match.end():
                        continue
                    found.add((index + 1, door, enclosing(lines, index)))
    return sorted(found, key=lambda entry: (entry[0], entry[1]))


def holds_itself(caller, door):
    """Whether caller is the door, which re-enters itself where it holds."""
    return caller is not None and caller == door


def violations(relative, source, used=None):
    """The references the member they sit in may not hold; the allowed ones are added to used."""
    out = []
    for line, door, caller in references(relative, source):
        if holds_itself(caller, door):
            continue
        if door in ALLOWED.get(caller, ()):
            if used is not None:
                used.add((caller, door))
            continue
        out.append(f"{relative}:{line}: {caller or '(no member)'} references the door {door}")
    return out


def stale(used):
    """Allowed references the tree no longer makes, which would otherwise let a later one through unread."""
    return [f"ALLOWED: {caller} no longer references the door {door}"
            for caller, doors in sorted(ALLOWED.items()) for door in sorted(doors) if (caller, door) not in used]


def scan(root):
    package = root / PACKAGE
    out = []
    used = set()
    for directory in SOURCE_DIRS:
        for path in sorted((package / directory).rglob("*.cs")):
            relative = path.relative_to(package).as_posix()
            if "/Tests/" in f"/{relative}":
                continue
            out.extend(violations(relative, path.read_text(encoding="utf-8"), used))
    return out + stale(used)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2],
                        help="the repository to read (default: the one holding this script)")
    args = parser.parse_args(argv)
    found = scan(args.root)
    for line in found:
        print(line)
    if found:
        print(f"{len(found)} finding(s). Call the body (the ...Work method) from Velvet's render and commit "
              "code, add the member to ALLOWED if something outside Velvet calls it, or take out an entry "
              "nothing references any more.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
