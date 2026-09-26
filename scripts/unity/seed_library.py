#!/usr/bin/env python3
"""Seed a worktree's Unity `Library` from another checkout's by cloning it, never by copying bytes.

    python3 scripts/unity/seed_library.py <other>/Library [destination]

The destination defaults to `Library` at the Unity project root holding the working directory. Every
top-level entry of the source is cloned except `ScriptAssemblies`, which holds what the other
checkout compiled (AGENTS.md owns why it stays behind).

Where the two paths cannot share blocks, this copies nothing: it prints the byte copy to run instead
and exits 1. A byte copy of a Library is the thing `library_seed_without_room.py` judges, so it is
left to be spelled where that guard reads it rather than made here out of its sight.
"""

import ctypes
import os
import re
import shlex
import shutil
import stat
import subprocess
import sys
from pathlib import Path

LEFT_BEHIND = "ScriptAssemblies"

# What marks the Unity project root, so the default destination is the project's own Library however
# deep in the tree this was run from.
PROJECT_MARKER = Path("ProjectSettings") / "ProjectVersion.txt"


class _StatFs(ctypes.Structure):
    # `__DARWIN_STRUCT_STATFS64` in the SDK's `sys/mount.h`. Its symbol is `statfs$INODE64` where
    # `sys/cdefs.h` also offers the older layout and plain `statfs` where it does not, so
    # `filesystem_type` asks for the suffixed one first.
    _fields_ = [("f_bsize", ctypes.c_uint32), ("f_iosize", ctypes.c_int32),
                ("f_blocks", ctypes.c_uint64), ("f_bfree", ctypes.c_uint64),
                ("f_bavail", ctypes.c_uint64), ("f_files", ctypes.c_uint64),
                ("f_ffree", ctypes.c_uint64), ("f_fsid", ctypes.c_int32 * 2),
                ("f_owner", ctypes.c_uint32), ("f_type", ctypes.c_uint32),
                ("f_flags", ctypes.c_uint32), ("f_fssubtype", ctypes.c_uint32),
                ("f_fstypename", ctypes.c_char * 16), ("f_mntonname", ctypes.c_char * 1024),
                ("f_mntfromname", ctypes.c_char * 1024), ("f_flags_ext", ctypes.c_uint32),
                ("f_reserved", ctypes.c_uint32 * 7)]


def filesystem_type(path):
    """The filesystem type name macOS reports for `path`, or None off macOS or where it cannot say."""
    if sys.platform != "darwin":
        return None
    try:
        libc = ctypes.CDLL(None, use_errno=True)
        call = getattr(libc, "statfs$INODE64", None) or libc.statfs
        found = _StatFs()
        if call(os.fsencode(str(path)), ctypes.byref(found)) != 0:
            return None
        return found.f_fstypename.decode("ascii", "replace")
    except (OSError, AttributeError, ValueError):
        return None


def existing_ancestor(path):
    """`path` itself where it exists, else the nearest directory above it that does."""
    path = Path(os.path.abspath(str(path)))
    while not path.exists() and path != path.parent:
        path = path.parent
    return path


def device_of(path):
    return os.stat(str(path)).st_dev


def clones(source, destination):
    """Whether `cp -c` from `source` to `destination` shares blocks rather than copying bytes.

    `man cp` documents `-c` falling back to a byte copy where the two sides are on different
    filesystems or the filesystem cannot clone, and succeeding either way, so the flag alone is not an
    answer. `destination` need not exist yet; the directory it would be made in is read.
    """
    try:
        near = existing_ancestor(destination)
        if device_of(source) != device_of(near):
            return False
    except OSError:
        return False
    return filesystem_type(near) == "apfs"


def clone_command(source, destination):
    """The argv that clones one entry.

    GNU coreutils documents `--reflink=always` as failing where it cannot clone. `cp -c` copies bytes
    there instead, which is why `seed` asks `clones` before running it on macOS.
    """
    if sys.platform == "darwin":
        return ["cp", "-c", "-a", str(source), str(destination)]
    return ["cp", "-a", "--reflink=always", str(source), str(destination)]


def byte_copy(source, destination):
    """The byte copy to run where no clone is possible, as one shell line."""
    return (f"rsync -a --exclude {LEFT_BEHIND} "
            f"{shlex.quote(str(source).rstrip('/') + '/')} "
            f"{shlex.quote(str(destination).rstrip('/') + '/')}")


def project_library(start):
    """`Library` at the Unity project root holding `start`, or None outside any project."""
    here = Path(os.path.abspath(str(start)))
    for candidate in [here, *here.parents]:
        if (candidate / PROJECT_MARKER).is_file():
            return candidate / "Library"
    return None


VANISHED = re.compile(r"^cp: (?P<path>/.+): No such file or directory$")


def vanished(stderr, source):
    """The source paths `stderr` reports missing that are gone now, or None where it reports more."""
    lines = [line for line in (stderr or "").splitlines() if line.strip()]
    gone = []
    for line in lines:
        found = VANISHED.match(line)
        if (not found or not found.group("path").startswith(str(source) + os.sep)
                or os.path.lexists(found.group("path"))):
            return None
        gone.append(found.group("path"))
    return gone or None


def _writable(func, path, _):
    """`shutil.rmtree`'s retry after a refusal: open the directories involved to the owner, try again."""
    for target in (os.path.dirname(path), path):
        if os.path.isdir(target) and not os.path.islink(target):
            try:
                os.chmod(target, stat.S_IRWXU)
            except OSError:
                pass
    func(path)


def remove(placed):
    """Removes each of `placed`; returns the ones still there afterwards."""
    for path in reversed(placed):
        try:
            if path.is_dir() and not path.is_symlink():
                shutil.rmtree(str(path), onerror=_writable)
            elif os.path.lexists(str(path)):
                path.unlink()
        except OSError:
            pass
    return [path for path in placed if os.path.lexists(str(path))]


def seed(source, destination, run=subprocess.run, out=sys.stdout, err=sys.stderr):
    """Clones `source` into `destination` minus `LEFT_BEHIND`: 0 where it did, 130 where it was
    interrupted, 1 where it did not."""
    source = Path(source)
    destination = Path(destination)
    if not source.is_dir():
        err.write(f"seed_library: {source} is not a directory.\n")
        return 1
    if os.path.realpath(str(source)) == os.path.realpath(str(destination)):
        err.write(f"seed_library: {source} is the destination itself.\n")
        return 1
    if destination.exists() and (not destination.is_dir() or any(destination.iterdir())):
        err.write(f"seed_library: {destination} already holds something, and a seed laid over it "
                  "would mix two checkouts' state.\nA Library is regenerable: remove it first, "
                  f"then run this again.\n\n  rm -rf {shlex.quote(str(destination))}\n")
        return 1
    if sys.platform == "darwin" and not clones(source, destination):
        err.write(f"seed_library: {source} and {destination} cannot share blocks here, so this "
                  "copied nothing.\nThe byte copy is:\n\n"
                  f"  {byte_copy(source, destination)}\n")
        return 1

    entries = sorted(entry for entry in os.listdir(str(source)) if entry != LEFT_BEHIND)
    made = not destination.exists()
    destination.mkdir(parents=True, exist_ok=True)
    placed = []
    gone = []
    for entry in entries:
        placed.append(destination / entry)
        try:
            done = run(clone_command(source / entry, destination / entry),
                       capture_output=True, text=True)
        except KeyboardInterrupt:
            done = subprocess.CompletedProcess([], 130, "", "interrupted")
        if done.returncode == 0:
            continue
        missing = vanished(done.stderr, source)
        if missing is not None:
            gone.extend(missing)
            continue
        left = remove(placed)
        if made:
            try:
                destination.rmdir()
            except OSError:
                left.append(destination)
        said = (" so this removed what it had placed" if not left else
                " and this could not remove:\n" + "".join(f"  {path}\n" for path in left))
        err.write(f"seed_library: cloning {entry} failed,{said}\n{(done.stderr or '').strip()}\n")
        if done.returncode == 130:
            return 130
        if sys.platform == "darwin":
            err.write("\n`clones` answered that these paths share blocks, so this is not the "
                      "filesystem declining a clone.\n")
        else:
            err.write("\nWhere this filesystem cannot reflink, the byte copy is:\n\n"
                      f"  {byte_copy(source, destination)}\n")
        return 1
    skipped = " (left behind: {})".format(LEFT_BEHIND) if (source / LEFT_BEHIND).exists() else ""
    out.write(f"cloned {len(entries)} entries of {source} into {destination}{skipped}\n")
    if gone:
        out.write(f"{len(gone)} file(s) left the source while it was cloned, so it is in use.\n")
    return 0


def main(argv=None, cwd=None):
    argv = sys.argv[1:] if argv is None else argv
    if not argv or len(argv) > 2 or argv[0].startswith("-"):
        print("usage: seed_library.py <other>/Library [destination]", file=sys.stderr)
        return 1
    destination = Path(argv[1]) if len(argv) == 2 else project_library(cwd or os.getcwd())
    if destination is None:
        print(f"seed_library: no Unity project holds {cwd or os.getcwd()}, so no destination is "
              "implied; name one.", file=sys.stderr)
        return 1
    return seed(Path(argv[0]), destination)


if __name__ == "__main__":
    sys.exit(main())
