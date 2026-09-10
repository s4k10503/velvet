#!/usr/bin/env python3
"""Translate client events to the shared guards' Claude-compatible protocol."""

import json
import os
from pathlib import Path
import shlex
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]


def patch_files(command, cwd, after=False):
    lines = command.splitlines()
    if not lines or lines[0] != "*** Begin Patch" or lines[-1] != "*** End Patch":
        raise ValueError("Unrecognized apply_patch envelope")
    changes = []
    index = 1
    while index < len(lines) - 1:
        header = lines[index]
        kind = next((k for k in ("Add", "Update", "Delete")
                     if header.startswith(f"*** {k} File: ")), None)
        if kind is None:
            raise ValueError("Unrecognized apply_patch file header")
        path = Path(cwd) / header.split(": ", 1)[1]
        target = path
        index += 1
        if index < len(lines) and lines[index].startswith("*** Move to: "):
            target = Path(cwd) / lines[index].split(": ", 1)[1]
            index += 1
        block = []
        while index < len(lines) - 1 and not lines[index].startswith(("*** Add File:", "*** Update File:", "*** Delete File:")):
            block.append(lines[index])
            index += 1
        if after:
            changes.append((str(target), ""))
            continue
        if kind == "Delete":
            if block or target != path:
                raise ValueError("Unexpected content in deleted-file patch")
            changes.append((str(path), ""))
            continue
        if kind == "Add":
            if any(not line.startswith("+") for line in block):
                raise ValueError("Unrecognized added-file content")
            content = "\n".join(line[1:] for line in block) + "\n"
        else:
            original = path.read_text()
            content_lines = original.splitlines()
            cursor = 0
            pos = 0
            while pos < len(block):
                heading = block[pos]
                if heading.startswith("@@"):
                    anchor = heading[3:] if heading.startswith("@@ ") else ""
                    if anchor:
                        matches = [i for i in range(cursor, len(content_lines)) if content_lines[i] == anchor]
                        if not matches:
                            raise ValueError("Patch section anchor not found")
                        cursor = matches[0] + 1
                    pos += 1
                old, new = [], []
                eof = False
                while pos < len(block) and not block[pos].startswith("@@"):
                    line = block[pos]
                    pos += 1
                    if line == "*** End of File":
                        eof = True
                    elif line.startswith((" ", "+", "-")):
                        if line[0] != "+":
                            old.append(line[1:])
                        if line[0] != "-":
                            new.append(line[1:])
                    else:
                        raise ValueError("Unrecognized patch hunk")
                matches = [i for i in range(cursor, len(content_lines) - len(old) + 1)
                           if content_lines[i:i + len(old)] == old
                           and (not eof or i + len(old) == len(content_lines))]
                if len(matches) != 1:
                    raise ValueError("Patch context is missing or ambiguous; use more exact context")
                start = matches[0]
                content_lines[start:start + len(old)] = new
                cursor = start + len(new)
            content = "\n".join(content_lines) + ("\n" if content_lines else "")
        if target != path:
            changes.append((str(path), ""))
        changes.append((str(target), content))
    if not changes:
        raise ValueError("Patch has no file changes")
    return changes


def normalize(client, event, payload):
    record = dict(payload)
    data = dict(payload.get("tool_input") or {})
    cwd = data.get("working_directory") or data.get("workdir") or payload.get("cwd") or str(ROOT)
    record["cwd"] = cwd
    record["hook_event_name"] = event
    tool = payload.get("tool_name")
    if tool == "apply_patch":
        return [{**record, "tool_name": "Write", "tool_input": {"file_path": path, "content": text}}
                for path, text in patch_files(data.get("command", ""), cwd, event == "PostToolUse")]
    record["tool_name"] = "Bash" if tool == "Shell" else tool
    if "path" in data and "file_path" not in data:
        data["file_path"] = data["path"]
    if data.get("file_path"):
        data["file_path"] = str(Path(cwd) / data["file_path"])
    record["tool_input"] = data
    return [record]


def translate(client, event, code, stdout, stderr):
    try:
        value = json.loads(stdout) if stdout.strip() else {}
    except ValueError:
        value = {}
    specific = value.get("hookSpecificOutput", {})
    denied = code == 2 or specific.get("permissionDecision") == "deny"
    reason = specific.get("permissionDecisionReason") or stderr or stdout
    context = specific.get("additionalContext") or value.get("systemMessage") or stdout or stderr
    if client == "codex":
        if event == "SubagentStop":
            return 0, json.dumps({"systemMessage": context}) if context else "{}", stderr
        return code, stdout, stderr
    if event == "PreToolUse":
        return 0, json.dumps({"permission": "deny" if denied else "allow",
                              "user_message": reason, "agent_message": reason}), ""
    if event == "Stop":
        return 0, json.dumps({"followup_message": reason} if denied else {}), ""
    if event == "SubagentStop":
        # Cursor has no observational context output for this event.
        return 0, "{}", context
    return 0, json.dumps({"additional_context": context}), ""


def main():
    client, event, script = sys.argv[1:]
    manifest = json.loads((ROOT / ".harness/hooks.json").read_text())
    entry = next(item for item in manifest if item["event"] == event and item["script"] == script)
    try:
        payload = json.load(sys.stdin)
        if client == "cursor" and event == "Stop" and payload.get("status") in ("aborted", "error"):
            print("{}")
            return 0
        records = normalize(client, event, payload)
        environment = dict(os.environ, CLAUDE_PROJECT_DIR=str(ROOT))
        environment.pop("CLAUDE_CODE_SESSION_ID", None)
        session = payload.get("session_id") or payload.get("conversation_id")
        if session:
            environment["CLAUDE_CODE_SESSION_ID"] = session
        reports = []
        for record in records:
            if entry["tools"] and record.get("tool_name") not in entry["tools"]:
                continue
            result = subprocess.run([sys.executable, str(ROOT / ".harness/hooks" / script)],
                                    input=json.dumps(record), text=True, capture_output=True,
                                    cwd=record["cwd"], env=environment, timeout=entry["timeout"])
            out, err = result.stdout, result.stderr
            returncode = result.returncode
            if returncode not in (0, 2) and event in ("PreToolUse", "Stop"):
                err = f"Harness guard {script} exited {returncode}:\n{err}"
                returncode = 2
            if client != "claude":
                replacement = shlex.quote(session) if session else "'<session-id-unavailable>'"
                out = out.replace("$CLAUDE_CODE_SESSION_ID", replacement)
                err = err.replace("$CLAUDE_CODE_SESSION_ID", replacement)
            code, out, err = translate(client, event, returncode, out, err)
            if err:
                print(err, file=sys.stderr, end="\n")
            if out:
                reports.append(out)
            if code or (event == "PreToolUse" and '"deny"' in out):
                print(out)
                return code
        if len(reports) == 1:
            print(reports[0])
        elif reports:
            contexts = []
            for report in reports:
                value = json.loads(report)
                contexts.append(value.get("additional_context") or
                                value.get("hookSpecificOutput", {}).get("additionalContext", ""))
            context = "\n".join(filter(None, contexts))
            if client == "cursor":
                print(json.dumps({"additional_context": context} if event != "PreToolUse" else {"permission": "allow"}))
            else:
                print(json.dumps({"hookSpecificOutput": {"hookEventName": event, "additionalContext": context}}))
        else:
            print("{}")
        return 0
    except (ValueError, OSError, subprocess.SubprocessError) as error:
        message = f"Harness could not inspect this {event}: {error}"
        code, out, err = translate(client, event, 2, "", message)
        print(out)
        if err:
            print(err, file=sys.stderr)
        return code


if __name__ == "__main__":
    raise SystemExit(main())
