#!/usr/bin/env python3
"""Fix CS86xx warnings from a Unity compile log (safe, targeted edits)."""

from __future__ import annotations

import re
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "Packages/com.velvet.core"
LINE_RE = re.compile(
    r"Packages/com\.velvet\.core/(.+\.cs)\((\d+),(\d+)\): warning (CS\d+): (.+)"
)
VALUE_TYPES = frozenset(
    {"int", "float", "double", "bool", "long", "short", "byte", "char", "void", "uint", "ulong"}
)
PARAM_IN_MSG = re.compile(r"parameter '(\w+)' in '(.+)'")
MEMBER_IN_MSG = re.compile(r"(?:field|property|event) '(\w+)'")


def is_reference_type(type_str: str) -> bool:
    t = type_str.strip()
    if not t or t.endswith("?"):
        return False
    if t.endswith("[]"):
        return True
    base = t.split("<", 1)[0]
    return base not in VALUE_TYPES


def fix_cs8625_param(line: str) -> str:
    if " = null" not in line or ("new " in line and "{" in line):
        return line

    def repl(m: re.Match[str]) -> str:
        t, name = m.group(1), m.group(2)
        if not is_reference_type(t):
            return m.group(0)
        return f"{t}? {name} = null"

    return re.sub(r"([\w<>,\[\].]+?) (\w+) = null", repl, line)


def fix_cs8625_assign(lines: list[str], idx: int) -> bool:
    """Fix `name = null;` by nullable-ifying an out/ref/param declaration in the enclosing method."""
    line = lines[idx]
    m = re.match(r"\s*(\w+)\s*=\s*null\s*;", line)
    if not m:
        return False
    name = m.group(1)

    depth = 0
    started = False
    sig_start = sig_end = None
    for i in range(idx, -1, -1):
        if "{" in lines[i]:
            depth += lines[i].count("{")
            started = True
        if started:
            depth -= lines[i].count("}")
            if depth <= 0 and "(" in lines[i]:
                sig_end = i
                break
    if sig_end is None:
        return False

    paren = 0
    for i in range(sig_end, -1, -1):
        paren += lines[i].count("(") - lines[i].count(")")
        if paren > 0 and (
            re.search(r"\b(static|public|private|internal|protected)\b", lines[i])
            or re.match(r"^\s*[\w<>,\[\].?]+\s+\w+\s*\(", lines[i].strip())
        ):
            sig_start = i
            break
    if sig_start is None:
        sig_start = sig_end

    changed = False
    for i in range(sig_start, sig_end + 1):
        old = lines[i]
        new = fix_param_type(old, name)
        if new != old:
            lines[i] = new
            changed = True
    return changed


def fix_member_declaration(lines: list[str], name: str, nullable: bool = True) -> bool:
    """Add ? or = null! to a field/property named `name` in lines."""
    changed = False
    field_pat = re.compile(
        rf"^(\s*(?:\[.*?\]\s*)*(?:public|internal|protected|private)\s+(?:static\s+)?(?:readonly\s+)?(?:event\s+)?)"
        rf"([\w<>,\[\].]+?)(\??)(\s+{re.escape(name)}\s*;)\s*$"
    )
    prop_pat = re.compile(
        rf"^(\s*(?:\[.*?\]\s*)*(?:public|internal|protected|private)\s+(?:static\s+)?(?:readonly\s+)?)"
        rf"([\w<>,\[\].]+?)(\??)(\s+{re.escape(name)}\s*\{{\s*(?:get|init|set))"
    )
    prop_auto_pat = re.compile(
        rf"^(\s*(?:\[.*?\]\s*)*(?:public|internal|protected|private)\s+(?:static\s+)?(?:readonly\s+)?)"
        rf"([\w<>,\[\].]+?)(\??)(\s+{re.escape(name)}\s*\{{\s*get;\s*(?:init;\s*)?set;\s*\}})"
    )

    for i, line in enumerate(lines):
        stripped = line.rstrip("\r\n")
        suffix = line[len(stripped) :]
        mm = field_pat.match(stripped)
        if mm and is_reference_type(mm.group(2).strip()) and mm.group(3) != "?":
            if nullable:
                lines[i] = field_pat.sub(rf"\1\2?\4", stripped) + suffix
            else:
                lines[i] = field_pat.sub(rf"\1\2\4", stripped).replace(f" {name};", f" {name} = null!;") + suffix
            changed = True
            continue
        for pat, repl in (
            (prop_pat, rf"\1\2?\4" if nullable else rf"\1\2\4"),
            (prop_auto_pat, rf"\1\2?\4" if nullable else rf"\1\2\4"),
        ):
            mm = pat.search(line)
            if mm and is_reference_type(mm.group(2).strip()) and mm.group(3) != "?":
                lines[i] = pat.sub(repl, line, count=1)
                changed = True
                break
    return changed


def fix_cs8618(lines: list[str], warn_idx: int, msg: str) -> bool:
    mm = MEMBER_IN_MSG.search(msg)
    if not mm:
        return False
    name = mm.group(1)
    if fix_member_declaration(lines, name, nullable=False):
        return True
    return fix_member_declaration(lines, name, nullable=True)


def _line_has_method_sig_start(line: str) -> bool:
    s = line.strip()
    if not s or s.startswith("//"):
        return False
    if re.match(r"^(if|for|foreach|while|switch|catch|using|lock|return)\b", s):
        return False
    return bool(re.search(r"\b(public|private|internal|protected|static)\b", s)) or bool(
        re.match(r"^\s*(?:\[.*?\]\s*)*(?:static\s+)?(?:[\w<>,\[\].?]+\??)\s+\w+\s*\(", s)
    )


def find_method_signature_span(lines: list[str], warn_idx: int) -> tuple[int, int] | None:
    end = warn_idx
    while end >= 0 and "{" not in lines[end]:
        end -= 1
    if end < 0:
        end = warn_idx

    start = end
    depth = 0
    found_open = False
    while start >= 0:
        chunk = lines[start]
        depth += chunk.count(")") - chunk.count("(")
        if "(" in chunk:
            found_open = True
        if found_open and depth <= 0 and _line_has_method_sig_start(lines[start]):
            return start, end
        if found_open and depth <= 0 and start < end:
            probe = start
            while probe >= 0 and probe >= start - 6:
                if _line_has_method_sig_start(lines[probe]):
                    return probe, end
                probe -= 1
            return start, end
        start -= 1
    return None


def fix_cs8603(lines: list[str], warn_idx: int) -> bool:
    span = find_method_signature_span(lines, warn_idx)
    if span is None:
        return False
    start, _end = span
    line = lines[start]
    patterns = [
        r"^(\s*(?:\[.*?\]\s*)*)((?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?)([\w<>,\[\].]+?)(\??)\s+(\w+)\s*\(",
        r"^(\s*(?:\[.*?\]\s*)*)((?:static\s+)(?:async\s+)?)([\w<>,\[\].]+?)(\??)\s+(\w+)\s*\(",
        r"^(\s*(?:\[.*?\]\s*)*)(internal\s+)([\w<>,\[\].]+?)(\??)\s+(\w+)\s*\(",
    ]
    for pat in patterns:
        m = re.match(pat, line)
        if not m or m.group(3) in VALUE_TYPES or m.group(4) == "?":
            continue
        if not is_reference_type(m.group(3)):
            continue
        lines[start] = line[: m.start(3)] + m.group(3) + "?" + line[m.end(3) :]
        return True
    return False


def parse_callee(msg: str) -> tuple[str, str] | None:
    pm = PARAM_IN_MSG.search(msg)
    if not pm:
        return None
    param_name, sig = pm.group(1), pm.group(2)
    mm = re.search(r"\.(\w+)\(", sig) or re.search(r"\b(\w+)\(", sig)
    if not mm:
        return None
    return param_name, mm.group(1)


def _is_method_declaration_line(line: str, method_name: str) -> bool:
    s = line.strip()
    if re.search(rf"\bnew\s+{re.escape(method_name)}\s*\(", s):
        return False
    if re.search(rf"\b{re.escape(method_name)}\s*\(", s):
        if re.search(r"\b(public|private|internal|protected|static)\b", s):
            return True
        if re.match(rf"^\s*(?:\[.*?\]\s*)*[\w<>,\[\].?]+\??\s+{re.escape(method_name)}\s*\(", s):
            return True
    return False


def find_method_param_span(lines: list[str], method_name: str, param_name: str) -> tuple[int, int] | None:
    for i, line in enumerate(lines):
        if not _is_method_declaration_line(line, method_name):
            continue
        depth = line.count("(") - line.count(")")
        j = i
        while depth > 0 and j + 1 < len(lines):
            j += 1
            depth += lines[j].count("(") - lines[j].count(")")
        block = "".join(lines[i : j + 1])
        if not re.search(rf"([\w<>,\[\].]+?)\??\s+{re.escape(param_name)}\b", block):
            continue
        if re.search(rf":\s*{re.escape(param_name)}\b", block):
            continue
        for k in range(i, j + 1):
            ln = lines[k]
            if re.search(rf":\s*{re.escape(param_name)}\b", ln):
                continue
            if re.search(rf"([\w<>,\[\].]+?)\??\s+{re.escape(param_name)}\b", ln):
                return i, j
    return None


def fix_param_type(line: str, param_name: str) -> str:
    if re.search(rf":\s*{re.escape(param_name)}\b", line):
        return line
    pat = re.compile(
        rf"((?:^|[,(])\s*)([\w<>,\[\].]+?)(\??)(\s+{re.escape(param_name)})\b"
    )

    def repl(m: re.Match[str]) -> str:
        prefix, t, q, rest = m.group(1), m.group(2), m.group(3), m.group(4)
        if q == "?" or not is_reference_type(t):
            return m.group(0)
        return f"{prefix}{t}?{rest}"

    return pat.sub(repl, line, count=1)


def fix_cs8604_in_file(lines: list[str], param_name: str, method_name: str) -> bool:
    span = find_method_param_span(lines, method_name, param_name)
    if span is None:
        return False
    start, end = span
    changed = False
    for i in range(start, end + 1):
        old = lines[i]
        new = fix_param_type(old, param_name)
        if new != old:
            lines[i] = new
            changed = True
    return changed


def apply_fixes(warn_path: Path) -> int:
    entries: list[tuple[str, int, str, str]] = []

    for raw in warn_path.read_text(encoding="utf-8").splitlines():
        m = LINE_RE.search(raw)
        if not m:
            continue
        path, ln, _col, code, msg = m.groups()
        if "/Tests/" in path:
            continue
        if code in {"CS8625", "CS8618", "CS8603", "CS8604"}:
            entries.append((path, int(ln), code, msg))

    by_file: dict[str, list[tuple[int, str, str]]] = defaultdict(list)
    callee_jobs: dict[tuple[str, str], None] = {}

    for path, ln, code, msg in entries:
        by_file[path].append((ln, code, msg))
        if code == "CS8604":
            parsed = parse_callee(msg)
            if parsed:
                callee_jobs[parsed] = None

    files_changed = 0
    search_roots = [ROOT / "Runtime", ROOT / "CodeGen", ROOT / "TestUtilities"]
    cs_files: list[Path] = []
    for root in search_roots:
        if root.exists():
            cs_files.extend(root.rglob("*.cs"))

    for (param_name, method_name) in callee_jobs:
        for fp in cs_files:
            rel = str(fp.relative_to(ROOT))
            lines = fp.read_text(encoding="utf-8").splitlines(keepends=True)
            if fix_cs8604_in_file(lines, param_name, method_name):
                fp.write_text("".join(lines), encoding="utf-8")
                print(f"CS8604 callee {method_name}({param_name}) -> {rel}")
                files_changed += 1

    for rel, fixes in by_file.items():
        fp = ROOT / rel
        if not fp.exists():
            continue
        lines = fp.read_text(encoding="utf-8").splitlines(keepends=True)
        changed = False
        for ln, code, msg in fixes:
            idx = ln - 1
            if idx < 0 or idx >= len(lines):
                continue
            if code == "CS8625":
                old = lines[idx]
                new = fix_cs8625_param(old)
                if new != old:
                    lines[idx] = new if new.endswith(("\n", "\r")) else new + "\n"
                    changed = True
            elif code == "CS8618":
                if fix_cs8618(lines, idx, msg):
                    changed = True
            elif code == "CS8603":
                if fix_cs8603(lines, idx):
                    changed = True
        if changed:
            fp.write_text("".join(lines), encoding="utf-8")
            print(rel)
            files_changed += 1

    return files_changed


if __name__ == "__main__":
    warn = Path(sys.argv[1] if len(sys.argv) > 1 else "/tmp/warn-v5.txt")
    print(f"Changed {apply_fixes(warn)} files", file=sys.stderr)
