#!/usr/bin/env python3
"""Publish campaign progress from the default branch; campaign.py remains the merge verdict."""

import argparse
import json
import os
import re
from pathlib import Path

import automerge
import settle

CONTEXT = settle.campaign.DISPLAY_CONTEXT


def identity(run):
    return run.get("id"), run.get("run_attempt") or 1


def reading(pull, runs, slug, event_run=None, released=False):
    head = pull.get("head") or {}
    sha = head.get("sha", "")
    if (pull.get("state") != "open" or not re.fullmatch(r"[0-9a-f]{40}", sha)
            or (head.get("repo") or {}).get("full_name") != slug
            or ((pull.get("base") or {}).get("repo") or {}).get("full_name") != slug):
        return None
    campaigns = [run for run in runs if run.get("path") == settle.campaign.PATH
                 and run.get("head_sha") == sha]
    if any(not isinstance(item.get(field), int) or item[field] <= 0
           for item in campaigns for field in ("id", "run_number", "run_attempt")):
        return None
    latest = max(campaigns, key=lambda run: (run.get("run_number") or 0, run.get("run_attempt") or 1),
                 default=None)
    if latest is not None and ((latest.get("head_repository") or {}).get("full_name") != slug
                               or latest.get("event") != "workflow_dispatch"):
        return None
    if event_run is not None and (latest is None or identity(event_run) != identity(latest)
                                  or event_run.get("path") != settle.campaign.PATH
                                  or event_run.get("head_sha") != sha
                                  or (event_run.get("head_repository") or {}).get("full_name") != slug):
        return None
    labels = {label.get("name") for label in pull.get("labels") or []}
    owed = settle.campaign.reason(labels, None, sha, "") is not None
    released = released and not owed
    if latest is None:
        if not owed and not released:
            return None
        return {"sha": sha, "state": "success" if released else "pending",
                "description": "Mutation campaign not requested" if released else "Awaiting mutation campaign",
                "target_url": f"https://github.com/{slug}/pull/{pull['number']}"}
    run_id = latest.get("id")
    if not isinstance(run_id, int) or run_id <= 0:
        return None
    found = settle.campaign.state([latest], lambda item: item.get("conclusion")
                                  if item.get("status") == "completed" else None)
    if found == settle.campaign.RUNNING:
        state, description = "pending", "Mutation campaign is running"
    elif settle.campaign.reason(labels, found, sha, "") is None:
        state = "success"
        description = "Mutation campaign passed" if found == settle.campaign.PASSED else "Cancelled campaign is not required"
    elif found == settle.campaign.FAILED:
        state, description = "failure", "Mutation campaign failed"
    else:
        state, description = "error", "Mutation campaign did not complete successfully"
    return {"sha": sha, "state": state, "description": description,
            "target_url": f"https://github.com/{slug}/actions/runs/{run_id}"}


def publish(project, number, event_run=None, read=settle.rest_json, post=None, released=False):
    slug = settle.repository(project)
    path = f"repos/{slug}/pulls/{number}"
    pull = read(path)
    sha = (pull.get("head") or {}).get("sha", "")
    payload = read(settle.expected_checks.runs_path(slug, sha))
    runs = settle.expected_checks.listed_runs(payload)
    if runs is None:
        raise RuntimeError("campaign run listing is incomplete")
    initial = reading(pull, runs, slug, event_run, released)
    if initial is None:
        return None
    current = read(path)
    payload = read(settle.expected_checks.runs_path(slug, sha))
    runs = settle.expected_checks.listed_runs(payload)
    if runs is None:
        raise RuntimeError("campaign run listing is incomplete")
    result = reading(current, runs, slug, event_run, released)
    if result is None or result["sha"] != initial["sha"]:
        return None
    if post is None:
        fields = [part for key, value in result.items() if key != "sha" for part in ("-f", f"{key}={value}")]
        settle.gh("api", "--method", "POST", f"repos/{slug}/statuses/{result['sha']}",
                  "-f", f"context={CONTEXT}", *fields)
    else:
        post(result)
    return result


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default=".")
    args = parser.parse_args(argv)
    project = Path(args.project).resolve()
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
    run = event.get("workflow_run")
    slug = settle.repository(project)
    if run is not None and run.get("path") != settle.campaign.PATH:
        return 0
    if "pull_request" in event:
        numbers = [event["pull_request"]["number"]]
    else:
        pulls = settle.rest_json(f"repos/{slug}/pulls?state=open&per_page=100")
        if not isinstance(pulls, list) or len(pulls) >= 100:
            raise RuntimeError("open pull request listing is incomplete")
        numbers = [pull["number"] for pull in pulls
                   if (pull.get("head") or {}).get("sha") == (run or {}).get("head_sha")]
    released = event.get("action") == "unlabeled" and (event.get("label") or {}).get("name") == settle.campaign.LABEL
    for number in numbers:
        result = publish(project, number, run, released=released)
        if result and result["state"] == "success" and not released:
            default = (event.get("repository") or {}).get("default_branch")
            if not default:
                raise RuntimeError("the event names no default branch for the merge hand-off")
            automerge.dispatch(project, automerge.MERGE_WORKFLOW, default, number=number,
                               after_run=os.environ["GITHUB_RUN_ID"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
