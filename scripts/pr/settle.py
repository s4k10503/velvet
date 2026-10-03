#!/usr/bin/env python3
"""Watch open pull requests, and merge one only when every precondition holds.

Both halves existed as instructions rather than as code: `stop/unsettled_pr.py` printed a watcher for
the reader to reimplement, and the merge was typed by hand. An instruction is re-derived each time,
and that hook's own text owns what a re-derived watcher gets wrong.

**Nine of the preconditions below have a refuse hook behind them**, so a `gh pr merge` typed by hand
is held to those as well; `refuse/merge_unproven_head.py` records which hook holds which. Those hooks
match on `gh pr merge` and do not see the `gh api -X PUT .../merge` this script sends, so teaching
them that shape is its own change. What this adds is reporting them together: one run names
everything wrong rather than costing a round of CI per reason.

**One has no hook: a draft head**, or one whose merge state is `dirty`, or one no longer open.

Ten preconditions:

- **Checks are bound to the head SHA they were read at.** The checks API answers about whatever it
  last recorded, which after a force-push is the previous commit's run. So the head is read, then the
  checks, then the head again, and a change between the two readings voids the answer. The merge
  request carries that SHA as well, so a push landing after the last reading is refused by GitHub
  rather than by whoever reads the history next.
- **The base's required workflows must not have last failed on push.** The branch need not contain
  the base; CONTRIBUTING.md's continuous-integration section owns why, and `red_base.py` owns which
  run is read and what exempts a head. Which base comes off the pull request, and `--base` overrides
  it.
- **The branch must contain the base's newest release commit.** CONTRIBUTING.md's
  continuous-integration section owns why; `published_check.release_commit` finds the commit.
- **No worktree may hold the branch.** The branch is deleted locally after the merge, and a worktree
  holding it makes that delete fail once the merge has already happened — so the branch outlives the
  pull request and has to be swept by hand later, when nothing in the checkout can still tell a
  merged branch from an abandoned one.
- **An empty check list is not "still running".** It means no workflow was ever triggered for that SHA.
  Nor is a list complete while the head still has a workflow run to finish, carries a workflow
  whose newest run failed, or lacks a context its base requires; `expected_checks.py` owns what each
  of those is and why it is read.
- **The base must not hold an unpublished release.** `scripts/release/published_check.py` owns that
  decision, and CONTRIBUTING.md's release section owns what goes wrong without it.
- **A draft is not merged**, and neither is one whose merge state is `dirty` nor one that is closed
  or already merged.
- **A head on another repository is not merged from here.** Its branch is a ref this checkout has
  not got, so neither containment above is asked of it, and the branch deleted after a merge is
  addressed on origin by that name.
- **A long-lived head is not merged from here**, since this squashes and deletes it. `long_lived.py`
  owns which heads those are.
- **A mutation campaign that ran on the head must have passed, and a head carrying the `automerge`
  label must have one.** `campaign.py` owns which run is the campaign and what it concluded.

`watch` records a pull request as ready by asking `blocking_reasons` — the same question `merge`
decides from, not a second one beside it. Asked twice, the two disagreed: a draft with conflicts was
recorded ready, and `refuse/edit_while_a_ready_pr_sits.py` reads that record out of $HOME — so it
refused Edit and Write in sessions with nothing to do with that pull request, naming `settle.py merge`
as the way out when nothing could make that command take it. What a guard offers as the way out is
worth only what the readiness that raised it means, so the two are one function.

Run: python3 scripts/pr/settle.py watch
     python3 scripts/pr/settle.py merge <number>
"""

import argparse
import collections
import fcntl
import importlib.util
import json
import os
import re
import shutil
import subprocess
import tempfile
import sys
import time
from pathlib import Path


def load_by_path(path, name):
    """Imports a script by path, since scripts/ holds no packages."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_published_check():
    """Imports the release guard by path.

    Its own directory goes on the path first: the module reads the CHANGELOG heading grammar out of
    release_notes.py rather than restating it, and a by-path import gives a module no siblings.
    """
    release = Path(__file__).resolve().parent.parent / "release"
    sys.path.insert(0, str(release))
    return load_by_path(release / "published_check.py", "published_check")


published_check = load_published_check()

red_base = load_by_path(Path(__file__).resolve().with_name("red_base.py"), "red_base")

long_lived = load_by_path(Path(__file__).resolve().with_name("long_lived.py"), "long_lived")

expected_checks = load_by_path(Path(__file__).resolve().with_name("expected_checks.py"),
                               "expected_checks")

campaign = load_by_path(Path(__file__).resolve().with_name("campaign.py"), "campaign")

# The three files this writes and two hooks read; watcher_state.py owns their format.
watcher_state = load_by_path(Path(__file__).resolve().with_name("watcher_state.py"),
                             "watcher_state")

# What the checks API calls a state that will not change again. "skipping" is terminal and passing:
# the Unity jobs are skipped wholesale on a fork with no licence, which is what lets one merge at all.
TERMINAL_PASS = frozenset({"pass", "skipping"})
TERMINAL_FAIL = frozenset({"fail", "cancel"})


# Every call below can hang rather than fail — a dead TCP connection, a credential helper waiting on
# a terminal that is gone. `watch` holds the watcher lock while it polls, so an unbounded call there
# stops the only watcher there can be, and `hold_the_watch` then refuses the replacement.
GH_TIMEOUT = 60
GIT_TIMEOUT = 300


def run(command, timeout):
    """subprocess.run with a bound, reporting a timeout as the same RuntimeError a failed call
    raises — so a hang reaches a caller as something it already handles rather than as a new kind."""
    try:
        return subprocess.run(command, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise RuntimeError("{} did not answer within {}s".format(" ".join(command), timeout))


def run_quietly(command, timeout):
    """The same bound for a call whose failure is reported rather than raised."""
    try:
        return subprocess.run(command, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        return subprocess.CompletedProcess(command, 1, "", f"did not answer within {timeout}s")


def gh(*args):
    result = run(["gh", *args], GH_TIMEOUT)
    if result.returncode != 0:
        raise RuntimeError("gh {} failed: {}".format(" ".join(args), result.stderr.strip()))
    return result.stdout


# REST rather than `gh pr view` / `gh pr list` / `gh pr checks` / `gh pr merge`, which go through
# GraphQL. The two quotas are separate, and a session long enough to need this script is a session
# long enough to exhaust the GraphQL one — which left the merge path unusable at exactly the moment
# the most pull requests were waiting on it. `gh pr checks` was the worst of the four: out of quota
# it returns nothing at all rather than failing, which this script read as "no workflow was ever
# triggered for this head" and refused with a reason that was not true. Nothing here needs a field
# REST does not carry.
def repository(project):
    """owner/name, read off the origin remote of the checkout being settled.

    Not `gh repo view`: that goes through GraphQL, which is the quota this whole change exists to
    stop depending on. Not a hardcoded constant either — --project points this at a checkout, and
    every git reading here is taken from that one, so the API paths have to address the same
    repository the git readings do.
    """
    if project not in _REPOSITORY:
        result = run(["git", "-C", str(project), "config", "--get", "remote.origin.url"],
                     GIT_TIMEOUT)
        url = result.stdout.strip()
        if result.returncode != 0 or not url:
            raise RuntimeError(f"{project} has no origin remote, so there is no repository to ask about")
        _REPOSITORY[project] = repository_slug(url)
    return _REPOSITORY[project]


_REPOSITORY = {}

# Everything before the first slash of a remote that carries no scheme: `git@github.com:`, and also
# the bare `alias:` an ssh config Host entry produces, which is what a clone made through one has in
# remote.origin.url.
_SCHEMELESS_HOST = re.compile(r"^[^/]+:")


def repository_slug(url):
    """owner/name out of the remote forms a GitHub clone is made with."""
    text = url.strip().rstrip("/")
    text = (text.split("://", 1)[1].partition("/")[2] if "://" in text
            else _SCHEMELESS_HOST.sub("", text, count=1))
    if text.endswith(".git"):
        text = text[:-len(".git")]
    parts = [part for part in text.split("/") if part]
    if len(parts) < 2:
        raise RuntimeError(f"origin {url} names no owner and repository")
    return "/".join(parts[-2:])


def rest(path, jq):
    return gh("api", path, "--jq", jq).strip()


def rest_json(path):
    return json.loads(gh("api", path))


def open_pull_requests(project):
    """The open pull request numbers.

    Numbers alone: `mergeable_state` is not on this payload, so everything else the decision reads is
    taken per pull request by `pull_request` anyway.
    """
    listing = rest("repos/{}/pulls?state=open&per_page=100".format(repository(project)),
                   ".[].number")
    return [int(line) for line in listing.split()]


def head_sha(project, number):
    return rest("repos/{}/pulls/{}".format(repository(project), number), ".head.sha")


# Everything the decision reads off the pull request itself. `mergeable_state` is on this payload and
# not on the listing one, so a caller that wants it has to ask per pull request anyway.
PullRequest = collections.namedtuple("PullRequest",
                                     "sha branch base draft merge_state fork labels state")


def pull_request(project, number):
    """The pull request's own fields, in one request.

    `fork` is what stops `branch` being handed to git: a cross-repository head names a branch on the
    fork, and `origin/<it>` is either not a ref here or a different branch of the same name. A head whose
    repository is gone reads as a fork too, which is the same answer — this checkout cannot see it.

    Both names come off this payload rather than one of them off `remote.origin.url`. GitHub answers
    the same pull request for any casing of the path, so a clone made with different capitals, or a
    repository since renamed, would make every pull request read as a fork — and then nothing is
    recorded ready and the guard that reads that state stops firing.
    """
    payload = rest_json("repos/{}/pulls/{}".format(repository(project), number))
    head = payload.get("head") or {}
    base = payload.get("base") or {}
    home = ((head.get("repo") or {}).get("full_name") or "")
    base_repository = ((base.get("repo") or {}).get("full_name") or "")
    labels = frozenset(label.get("name") for label in payload.get("labels") or []
                       if isinstance(label, dict))
    return PullRequest(head["sha"], head["ref"], base["ref"], bool(payload.get("draft")),
                       payload.get("mergeable_state") or "", home != base_repository, labels,
                       "merged" if payload.get("merged") else payload.get("state") or "unnamed")


# The bucket names this script decides from. A conclusion absent from the table falls to `fail` at
# the lookup, so a conclusion GitHub adds later blocks until somebody classifies it rather than
# merging unclassified.
_BUCKET = {"success": "pass", "neutral": "skipping", "skipped": "skipping",
           "failure": "fail", "timed_out": "fail", "action_required": "fail",
           "cancelled": "cancel", "stale": "cancel"}

# The legacy commit-status vocabulary, which is a different set of words for the same decision.
_STATUS_BUCKET = {"success": "pass", "pending": "pending", "failure": "fail", "error": "fail"}


def checks(project, sha, runs=(), ignore_campaign_display=False):
    """Check results for one head, or an empty list when no workflow ever ran for it.

    `runs` is `campaign_runs`' reading of the same head, whose superseded campaigns' checks are left
    out.
    """
    slug = repository(project)
    return check_results(rest_json("repos/{}/commits/{}/check-runs?per_page=100".format(slug, sha)),
                         rest_json("repos/{}/commits/{}/status?per_page=100".format(slug, sha)),
                         campaign.superseded_suites(runs), ignore_campaign_display)


def campaign_runs(project, sha):
    """Every workflow run whose head is `sha`, which `campaign.py` reads the campaign out of and
    `expected_checks.py` reads every other workflow's runs out of."""
    payload = rest_json(expected_checks.runs_path(repository(project), sha))
    listed = payload.get("workflow_runs", [])
    whole_page(payload, listed, "workflow runs")
    return listed


def run_jobs(project, runs, now):
    """The jobs of each run in `expected_checks.open_runs`, by run id."""
    slug = repository(project)
    found = {}
    for run in expected_checks.open_runs(runs, now):
        payload = rest_json(expected_checks.jobs_path(slug, run))
        listed = payload.get("jobs", [])
        whole_page(payload, listed, "jobs")
        found[run.get("id")] = listed
    return found


def campaign_state(runs, jobs, now):
    """`campaign.state`, with what a run concluded read the way `expected_checks` reads it."""
    return campaign.state(runs, lambda run: expected_checks.conclusion(run, jobs, now))


def campaign_reason(labels, runs, jobs, now, head):
    """`campaign.reason` for a head, naming how to end its newest campaign where it is stuck."""
    newest = campaign.newest(runs)
    return campaign.reason(labels, campaign_state(runs, jobs, now), head,
                           expected_checks.stuck_exit(newest) if newest else "")


def required_contexts(project, base):
    """The status-check contexts the rulesets over `base` require."""
    contexts = expected_checks.required(rest_json(expected_checks.rules_path(repository(project),
                                                                             base)))
    if contexts is None:
        raise RuntimeError(f"the rules over {base} filled a whole page, so one may not have been read")
    return contexts


def whole_page(payload, listed, kind):
    """Raises when a payload says more entries exist for this head than its page carried.

    An entry that fell off the page produces no reason at all, rather than a wrong one: the buckets
    handed to `reasons_from` are the only thing it decides from, and the entries that did arrive can
    all be passing. The merge then lands green with nothing said about the one nobody read. Both
    payloads go through here, because a merge decided from a partial read of either is a merge over a
    check nobody saw.
    """
    total = payload.get("total_count", len(listed))
    if total > len(listed):
        raise RuntimeError(f"{total} {kind} exist for this head but only {len(listed)} were read")


def check_results(runs, statuses, superseded=frozenset(), ignore_campaign_display=False):
    """One bucket per check, from the check-runs payload and the legacy commit-status one.

    One commit carries two check surfaces, and the base can require a context from either, so
    leaving one out would decide a merge against a check nobody read. Only the status payload's
    individual entries become buckets; its rollup state is not read at all.

    A page that did not carry everything raises rather than deciding; `whole_page` owns why. A check
    run of a suite in `superseded` is left out.
    """
    listed = runs.get("check_runs", [])
    reported = statuses.get("statuses", [])
    whole_page(runs, listed, "check runs")
    whole_page(statuses, reported, "commit statuses")
    listed = [run for run in listed
              if (run.get("check_suite") or {}).get("id") not in superseded]
    results = [{"name": run.get("name", ""),
                "bucket": "pending" if run.get("status") != "completed"
                else _BUCKET.get(run.get("conclusion") or "", "fail")}
               for run in listed]
    results.extend({"name": status.get("context", ""),
                    "bucket": _STATUS_BUCKET.get(status.get("state") or "", "fail")}
                   for status in reported
                   if not ignore_campaign_display or status.get("context") != campaign.DISPLAY_CONTEXT)
    return results


def worktree_branches(project):
    """Branch names currently checked out in a worktree, which cannot be deleted while they are."""
    held = set()
    for line in gh_git(project, "worktree", "list", "--porcelain").splitlines():
        if line.startswith("branch "):
            held.add(line.split(" ", 1)[1].strip().removeprefix("refs/heads/"))
    return held


def gh_git(project, *args):
    result = run(["git", "-C", str(project), *args], GIT_TIMEOUT)
    if result.returncode != 0:
        raise RuntimeError("git {} failed: {}".format(" ".join(args), result.stderr.strip()))
    return result.stdout


def contains_base(project, branch, base):
    """Whether the branch already holds every commit on the base.

    Asked of the remote refs rather than of local ones, because a local base can be behind what the
    merge will actually happen against and would report a stale answer as a clean one. `update`
    fetches both first.
    """
    merge_base = gh_git(project, "merge-base", f"origin/{base}", f"origin/{branch}").strip()
    base_head = gh_git(project, "rev-parse", f"origin/{base}").strip()
    return merge_base == base_head


def contains_commit(project, branch, sha):
    """Whether the branch on origin holds `sha`, a commit of its base that `project_state` fetched.

    An exit other than git's yes (0) and no (1) is raised rather than read as either.
    """
    answer = run(["git", "-C", str(project), "merge-base", "--is-ancestor", sha, f"origin/{branch}"],
                 GIT_TIMEOUT)
    if answer.returncode not in (0, 1):
        raise RuntimeError(f"whether origin/{branch} holds {sha[:7] or 'an unnamed commit'} could "
                           f"not be read: {answer.stderr.strip()}")
    return answer.returncode == 0


def reasons_from(before, after, results, branch, base, held_by_worktree,
                 unpublished_release, draft, merge_state, fork, failing_runs, behind_release,
                 long_lived_head, runs_unfinished, runs_failed, required, owed_campaign):
    """Every reason not to merge, decided from plain data so the decision is testable without a network.

    `unpublished_release` takes no default on purpose: a caller that stops supplying it would otherwise
    read as a clean base, and the only production caller is held by no test. `failing_runs` takes
    none either: a caller that stopped supplying it would read as a green base. It holds the base's
    failing push runs this head is not exempt from. `behind_release` is (sha, version) of the base's
    newest release commit where the head lacks it, and None otherwise. `long_lived_head` takes none
    for the reason `unpublished_release` gives, and neither do `runs_unfinished` and `runs_failed`,
    `expected_checks`' reasons about the head's workflow runs, `required`, the contexts the base
    requires, or `owed_campaign`, the reason `campaign.reason` gives or None.

    A moved head returns with the reasons that are not about a commit and nothing else: with the
    readings straddling a force-push, nothing else read here is known to be about the same commit, so
    reporting the rest would be reporting about two SHAs at once. The publication reason is about the
    base, which the force-push did not touch; draft is a state of the pull request rather than of
    its head, and a long-lived head is one by its branch name.
    """
    reasons = [unpublished_release] if unpublished_release else []
    if draft:
        reasons.append("it is a draft: mark it ready for review first")
    if long_lived_head:
        reasons.append(long_lived.reason(branch))
    if before != after:
        return reasons + [f"head moved from {before[:7]} to {after[:7]} while its checks were being read"]

    # `unknown` is left out: it is the absence of a reading rather than a reading, and a state that
    # comes and goes would drop and re-add the entry, resetting the age
    # `refuse/edit_while_a_ready_pr_sits.py` measures a pull request by.
    if merge_state == "dirty":
        reasons.append(f"it conflicts with {base}: resolve the conflict in the branch, which "
                       f"`settle.py update` declines to do")

    absent = expected_checks.absent(required, (entry["name"] for entry in results))
    if not results and not runs_unfinished:
        reasons.append(f"no check has run for {after[:7]}: a workflow was never triggered for this head")
    elif absent:
        reasons.append("required by {} and not reported at {}: {}".format(
            base, after[:7], ", ".join(absent)))
    if runs_unfinished:
        reasons.append("workflow runs not finished at {}: {}".format(after[:7],
                                                                   ", ".join(runs_unfinished)))
    if runs_failed:
        reasons.append("workflow runs failed at {}: {}".format(after[:7], ", ".join(runs_failed)))

    unfinished = [entry["name"] for entry in results
                  if entry["bucket"] not in TERMINAL_PASS and entry["bucket"] not in TERMINAL_FAIL]
    failed = [f"{entry['name']}={entry['bucket']}" for entry in results
              if entry["bucket"] in TERMINAL_FAIL]
    if unfinished:
        reasons.append("still pending at {}: {}".format(after[:7], ", ".join(sorted(unfinished))))
    if failed:
        reasons.append("failing at {}: {}".format(after[:7], ", ".join(sorted(failed))))
    if owed_campaign:
        reasons.append(owed_campaign)

    for failing in failing_runs:
        reasons.append(f"origin/{base}'s last {failing.workflow} push run failed at "
                       f"{failing.sha[:7]}: fix or revert it on {base} first. A head is exempt only "
                       f"where it contains that commit and its own Unity tests ran and passed")

    if behind_release:
        reasons.append(f"does not contain {behind_release[0][:7]}, which dated {behind_release[1]} on "
                       f"{base}: take {base} in with `settle.py update`, so its checks run again "
                       f"over the section that commit closed")

    if fork:
        reasons.append("its head is on another repository: this settles branches on origin")

    if held_by_worktree:
        reasons.append(f"a worktree holds {branch}: remove it first, or the local branch outlives "
                       f"the merge and has to be swept by hand later")

    return reasons


# What merge needs from the readings besides the verdict: the SHA the checks were read at, which the
# merge request carries, the branch it deletes afterwards, and the check results, which `watch` prints.
Blocking = collections.namedtuple("Blocking", "reasons head branch results base")

# The readings that answer for something wider than one pull request: the publication state of a
# base, its newest release commit, its required workflows' last push verdicts, the contexts its
# rulesets require, and the branches this
# checkout's worktrees hold, which is the repository's. Taken once per base and handed down, so a watcher poll over N pull
# requests costs one fetch, one `git ls-remote --tags`, one rules listing and one runs listing per
# workflow per base rather than N of each.
ProjectState = collections.namedtuple("ProjectState", "held unpublished_release red release required")


def project_state(project, base):
    """The per-repository readings, with the fetch that the git ones depend on."""
    gh_git(project, "fetch", "origin", "--quiet")
    slug = repository(project)
    red = [failing for failing in (
               red_base.failing_run(workflow,
                                    rest_json(f"repos/{slug}/{red_base.runs_path(workflow, base)}"))
               for workflow in red_base.REQUIRED_WORKFLOWS)
           if failing]
    return ProjectState(worktree_branches(project),
                        published_check.unpublished_reason(project, f"origin/{base}", fetch=False),
                        red, release_commit(project, base), required_contexts(project, base))


def release_commit(project, base):
    """`published_check.release_commit` for origin/<base>, raising a failure as the RuntimeError
    `watch` catches per pull request rather than as one that stops the watcher."""
    try:
        return published_check.release_commit(project, f"origin/{base}", timeout=GIT_TIMEOUT)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired) as failure:
        raise RuntimeError(f"the newest release commit on origin/{base} could not be read: {failure}")


def blocking_reasons(project, number, base=None, states=None):
    """reasons_from, with every reading taken from the repository and the API.

    `base` names the branch to judge against, and None means the pull request's own.

    `states` carries the per-base readings across one poll. They are per base rather than per
    repository — a fetch and a tag listing each answer about one — and a poll normally sees a single
    base, so the cache is what keeps a poll over N pull requests at one fetch rather than N.
    """
    before = pull_request(project, number)
    target = base or before.base
    # Ahead of every other reading, and with no check results, so `watch` has nothing to print or
    # record for a pull request that closed after the listing named it.
    if before.state != "open":
        return Blocking([f"it is {before.state}, not open: there is nothing to merge"],
                        before.sha, before.branch, [], target)
    states = {} if states is None else states
    if target not in states:
        states[target] = project_state(project, target)
    state = states[target]
    # Re-asked with this pull request's head, because the per-base reading cannot see that this one
    # is the repair: a base holding an unpublished release refuses the change that reopens it.
    unpublished = state.unpublished_release
    if unpublished:
        unpublished = published_check.unpublished_reason(
            project, f"origin/{target}", fetch=False, result=before.sha)
    runs = campaign_runs(project, before.sha)
    now = time.time()
    others = campaign.others(runs)
    jobs = run_jobs(project, runs, now)
    owed_campaign = None if before.fork else campaign_reason(before.labels, runs, jobs, now, before.sha)
    results = checks(project, before.sha, runs, ignore_campaign_display=not before.fork)
    after = head_sha(project, number)
    ran = red_base.unity_ran([(entry["name"], "success" if entry["bucket"] == "pass"
                               else entry["bucket"]) for entry in results])
    uncovered = [failing for failing in state.red
                 if before.fork or not ran
                 or not contains_commit(project, before.branch, failing.sha)]
    behind_release = (state.release if state.release and not before.fork
                      and not contains_commit(project, before.branch, state.release[0]) else None)
    long_lived_head = not before.fork and long_lived.is_long_lived(before.branch)
    # A head on another repository is dispatched no campaign, and the fork reason refuses it already.
    return Blocking(reasons_from(before.sha, after, results, before.branch, target,
                                 held_by_worktree=before.branch in state.held,
                                 unpublished_release=unpublished,
                                 draft=before.draft,
                                 merge_state=before.merge_state,
                                 fork=before.fork,
                                 failing_runs=uncovered,
                                 behind_release=behind_release,
                                 long_lived_head=long_lived_head,
                                 runs_unfinished=expected_checks.unfinished(others, jobs, now),
                                 runs_failed=expected_checks.failed(others, jobs, now),
                                 required=state.required,
                                 owed_campaign=owed_campaign),
                    after, before.branch, results, target)


def write_ready_state(ready, since):
    """Record each ready pull request beside the time it first read that way.

    Rewritten whole every poll, so a merged one leaves and a newly ready one arrives without the file
    accumulating. `since` carries entries across polls; without it every poll would reset the clock a
    guard reads and nothing would ever look stale.
    """
    for number in list(since):
        if number not in ready:
            del since[number]
    for number in ready:
        since.setdefault(number, int(time.time()))
    watcher_state.READY_STATE.write_text(
        "".join(f"{number} {since[number]}\n" for number in sorted(since)))


def read_ready_state():
    """When each recorded pull request first read as ready, out of the file a watcher left behind.

    Retirement makes a restart ordinary rather than a machine reboot, and a `since` that began
    empty would hand every green pull request a new clock — the one
    `refuse/edit_while_a_ready_pr_sits.py` reads to decide that a pull request has sat.

    Carried only while the record was itself written inside the staleness window, because a wider
    gap is one nothing polled through: the record's own rule is to drop an entry the moment its pull
    request stops being ready, so a pull request that left the ready set and returned unseen would
    be carried here as having sat throughout. Dated by the file rather than by the heartbeat beside
    it, which a watcher that beat and then died before its first record would leave fresh over
    somebody else's. The window runs from the last poll rather than from the retirement, which
    leaves a replacement about two of its three polls — one started later carries nothing. A stamp
    in the future is dropped, and the window is bounded below as well as above, for the reason
    `watcher_state.stamp_age` gives about the one in the heartbeat.
    """
    try:
        text = watcher_state.READY_STATE.read_text(encoding="utf-8")
        written = watcher_state.READY_STATE.stat().st_mtime
    except (OSError, UnicodeDecodeError):
        return {}
    now = int(time.time())
    if not 0 <= now - written < watcher_state.STALE_AFTER:
        return {}
    carried = {}
    for line in text.splitlines():
        fields = line.split()
        if len(fields) == 2 and all(field.isdigit() for field in fields) and int(fields[1]) <= now:
            carried[int(fields[0])] = int(fields[1])
    return carried


def hold_the_watch():
    """(the open lock, the pid holding it). No lock means another watcher already has it.

    flock rather than a pidfile: the kernel drops it when the holder exits, so the LOCK is never
    stale and the next watcher takes it without reading anything to decide that. The pid written
    here is read only to name a holder in a refusal. A pid judgement still happens, one file over —
    `watcher_state.beating_elsewhere` makes it about the heartbeat, which is a different question.
    The handle is returned rather than dropped: the lock lives with the open file description, so
    keeping it open is the whole of holding it.
    """
    handle = open(watcher_state.LOCK, "a+", encoding="utf-8")
    try:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        handle.seek(0)
        holder = handle.read().strip()
        handle.close()
        return None, holder
    handle.seek(0)
    handle.truncate()
    handle.write(f"{os.getpid()}\n")
    handle.flush()
    return handle, ""


def beat():
    """Record that a reading has just answered.

    After the readings and never before them, and never on the path that caught their failure. A
    watcher whose calls hang costs up to the bound on each of them, and one that stamped the file on
    the way in would keep it inside the staleness window for the whole of that — so the guards would
    read a live watcher while nothing was being read, and `write_ready_state` would meanwhile empty
    the ready file. A wedge has to go stale; that is what makes it visible.
    """
    watcher_state.HEARTBEAT.write_text(watcher_state.beat(os.getpid()))


def unasked_for(started, now=None):
    """Seconds since a reader last recorded asking, counted from this watcher's own start.

    A stamp left before this watcher started says nothing about whether anything reads this one, so
    the interval runs from the later of the two.
    """
    now = time.time() if now is None else now
    ago = watcher_state.asked_ago(now)
    return now - started if ago is None else min(ago, now - started)


def retire(lock, unasked, ever_asked=None):
    """The lock goes back here rather than at the exit: `watch` returns to a caller, and one that
    went on to other work would hold it while nothing was watching.

    `ever_asked` is whether a stamp is readable there at all. None is a third state the interval
    cannot express: a guard resolves `watcher_state` from its own checkout, so one at a commit
    predating `note_asked` reads this watcher on every turn and cannot record having done it.
    """
    lock.close()
    never = ("\nThat file carries no stamp at all. A guard resolves the stamping code from its own "
             "checkout,\nso one at a commit predating it reads this watcher every turn and cannot "
             "record having done it —\npull there before reading this as nobody watching."
             if ever_asked is False else "")
    print(f"Retiring {int(unasked)}s since anything last stamped {watcher_state.ASKED}, which is "
          f"how a guard records reading the watcher's state — floored by this watcher's own start, "
          f"so a first stamp that never came reads the same as one that stopped.{never}\n"
          f"\nFrom here a pending check blocks a Stop again, and an edit is refused until something "
          f"is watching. Both are what a live watcher was forgiving. Start another when that is what "
          f"you want:\n"
          f"\n  python3 scripts/pr/settle.py watch\n", flush=True)
    return 0


def watch(project, base):
    """Emit each check that reaches a terminal state, once, and hold the heartbeat open until
    nothing reads it."""
    lock, holder = hold_the_watch()
    if lock is None:
        print(f"Refusing to watch: process {holder or 'unknown'} already holds "
              f"{watcher_state.LOCK}. A second watcher draws on the same quota on its own cycle, "
              f"which is the whole of what it costs now that the heartbeat names its writer.\n"
              f"\nIf that process is wedged rather than watching — every guard reading the heartbeat "
              f"says nothing is watching while this refuses to replace it — kill it and run this "
              f"again:\n\n  kill {holder or '<pid>'}\n", file=sys.stderr)
        return 1
    # After the lock and not before: "writing the heartbeat without holding the lock" is only a
    # reading anyone can take while this process is the one holding it.
    if watcher_state.beating_elsewhere(os.getpid()):
        lock.close()
        print(f"Refusing to watch: {watcher_state.HEARTBEAT} was written inside the last "
              f"{watcher_state.STALE_AFTER}s by something that is not holding {watcher_state.LOCK} "
              f"— a watcher from a checkout older than the lock, either still running or only just "
              f"stopped. Find it with `ps -Ao pid=,command= | grep 'settle[.]py watch'` and kill "
              f"it; if it is already gone, its last heartbeat ages out within "
              f"{watcher_state.STALE_AFTER}s.", file=sys.stderr)
        return 1

    seen = set()
    ready_since = read_ready_state()
    started = time.time()
    while True:
        # Before the readings, so the error path that sleeps and continues cannot skip it — a
        # watcher whose calls all fail goes unread like any other.
        unasked = unasked_for(started)
        if unasked >= watcher_state.RETIRE_AFTER:
            return retire(lock, unasked, watcher_state.asked_ago() is not None)
        try:
            pull_requests = open_pull_requests(project)
        except RuntimeError as error:
            print(f"! {error}", flush=True)
            time.sleep(watcher_state.POLL_SECONDS)
            continue
        beat()

        # Emptied per poll, not held across them: a base whose release is dispatched between two
        # polls would otherwise go on blocking every pull request that names it until the watcher
        # is restarted.
        states = {}

        ready = set()
        for number in pull_requests:
            try:
                blocking = blocking_reasons(project, number, base, states)
            except RuntimeError as error:
                print(f"! PR#{number}: {error}", flush=True)
                continue
            beat()

            for result in blocking.results:
                if result["bucket"] == "pending":
                    continue
                line = "PR#{} {} {} => {}".format(number, blocking.head[:7], result["name"],
                                                  result["bucket"])
                if line not in seen:
                    seen.add(line)
                    print(line, flush=True)

            if not blocking.reasons:
                # A pull request that finished green and sits unmerged is what this exists for as much
                # as a pending one: a watcher reporting only state CHANGES goes silent on it forever.
                ready.add(number)
                line = f"PR#{number} {blocking.head[:7]} READY: nothing blocks the merge"
                if line not in seen:
                    seen.add(line)
                    print(line, flush=True)

        write_ready_state(ready, ready_since)
        time.sleep(watcher_state.POLL_SECONDS)


def update_reasons(branch, base, holds_base, held_by_worktree):
    """Every reason not to bring the base into this branch, decided from plain data.

    A branch already holding the base is refused rather than no-opped: an update that merges nothing
    still pushes, and a push re-triggers every check for a head that already passed them.
    """
    reasons = []
    if holds_base:
        reasons.append(f"already contains origin/{base}: there is nothing to bring in")
    if held_by_worktree:
        reasons.append(f"a worktree holds {branch}: its own state would decide the merge, "
                       f"not the pushed head")
    return reasons


def update(project, number, base):
    """Merges the base into a pull request's branch and pushes it.

    Done in a throwaway worktree off the REMOTE head rather than in the checkout: the branch may be
    checked out somewhere with work in progress, and a local ref may be behind what the merge has to
    happen against. Refuses a conflict rather than leaving a half-merged worktree behind — a branch
    whose files really do conflict needs a person, and the conflicting one this exists for
    (a long-held branch that re-conflicts on every base move) is exactly the case not to automate.
    """
    payload = "repos/{}/pulls/{}".format(repository(project), number)
    branch = rest(payload, ".head.ref")
    base = base or rest(payload, ".base.ref")
    gh_git(project, "fetch", "origin", "--quiet")
    reasons = update_reasons(branch, base, contains_base(project, branch, base),
                             branch in worktree_branches(project))
    if reasons:
        print(f"Refusing to update PR#{number}:", file=sys.stderr)
        for reason in reasons:
            print(f"  - {reason}", file=sys.stderr)
        return 1

    scratch = Path(tempfile.mkdtemp(prefix="velvet-settle-"))
    work = scratch / "worktree"
    temp_branch = f"settle-update-{number}"
    try:
        gh_git(project, "worktree", "add", str(work), "-b", temp_branch,
               f"origin/{branch}", "--quiet")
        try:
            gh_git(work, "merge", f"origin/{base}", "-m",
                   f"Merge {base} into {branch}")
        except RuntimeError as exc:
            print(f"Refusing to update PR#{number}: the merge did not apply cleanly", file=sys.stderr)
            print(f"  {exc}", file=sys.stderr)
            return 1
        gh_git(work, "push", "origin", f"HEAD:{branch}")
    finally:
        run_quietly(["git", "-C", str(project), "worktree", "remove", str(work), "--force"],
                    GIT_TIMEOUT)
        run_quietly(["git", "-C", str(project), "branch", "-D", temp_branch], GIT_TIMEOUT)
        shutil.rmtree(scratch, ignore_errors=True)
    print(f"PR#{number}: {base} merged into {branch} and pushed; its checks run again")
    return 0


def merge(project, number, base, dry_run):
    blocking = blocking_reasons(project, number, base)
    if blocking.reasons:
        print(f"Refusing to merge PR#{number}:", file=sys.stderr)
        for reason in blocking.reasons:
            print(f"  - {reason}", file=sys.stderr)
        return 1

    if dry_run:
        print(f"PR#{number} would merge: no blocking reason")
        return 0

    # REST, for the same reason as every other read here — `gh pr merge` is GraphQL, so under an
    # exhausted quota the whole merge path stopped working while the reads still could have answered.
    #
    # `sha` binds the merge to the head those readings were about: the sandwich above reports a move,
    # this one refuses it.
    #
    # The squash body is passed rather than left to GitHub, which composes one from the branch's
    # commits — landing a copy of every Co-Authored-By trailer they carry. The pull request's
    # description is the summary somebody wrote for the whole change, and it lands instead.
    title, body = pull_request_text(project, number)
    gh("api", "-X", "PUT", "repos/{}/pulls/{}/merge".format(repository(project), number),
       "-f", "merge_method=squash", "-f", "sha={}".format(blocking.head),
       "-f", "commit_title={} (#{})".format(title, number),
       "-f", "commit_message={}".format(body))
    # `gh pr merge` printed its own confirmation and the API call prints nothing, so without this a
    # merge and a dry run that decided nothing look identical from the terminal.
    print(f"PR#{number} merged: {blocking.branch} squashed onto {blocking.base}")
    for failure in delete_merged_branch(project, blocking.branch):
        print(f"PR#{number} merged, but {failure}", file=sys.stderr)
    return 0


def reference_already_gone(stderr):
    """Whether a ref delete failed because the ref was not there, which is not a failure to report.

    The repository deletes the head on merge by itself, so this DELETE normally arrives second and
    finds nothing. It is still sent, because that setting is GitHub state nothing in this repository
    reads back, and a script that relies on it silently stops deleting when somebody turns it off.
    """
    return "Reference does not exist" in stderr


def delete_merged_branch(project, branch):
    """Deletes the merged branch locally and on the remote, and returns what survived.

    The local half is the one nothing else does, and the one that cost: ninety-six local branches
    accumulated before anyone counted them, and the sweep that followed is what
    `.claude/hooks/refuse/merge_without_branch_deletion.py` records. A branch that was only ever
    worked on from a detached worktree has no local ref at all, which is not a failure.

    Failures come back to be reported rather than raised, because the merge has already happened and
    an exit code saying otherwise sends the reader to merge it again.
    """
    failures = []
    present = run_quietly(["git", "-C", str(project), "rev-parse", "--verify", "--quiet",
                           f"refs/heads/{branch}"], GIT_TIMEOUT)
    if present.returncode == 0:
        deleted = run_quietly(["git", "-C", str(project), "branch", "-D", branch], GIT_TIMEOUT)
        if deleted.returncode != 0:
            failures.append(f"the local branch {branch} survived: {deleted.stderr.strip()}")

    remote = delete_remote_ref(project, branch)
    if remote:
        failures.append(f"the remote branch {branch} survived: {remote}")
    return failures


def delete_remote_ref(project, branch):
    """Deletes the branch on the remote, and answers with what stderr said when it did not."""
    removed = run_quietly(["gh", "api", "-X", "DELETE", "repos/{}/git/refs/heads/{}".format(
        repository(project), branch)], GH_TIMEOUT)
    if removed.returncode == 0 or reference_already_gone(removed.stderr):
        return ""
    return removed.stderr.strip()


def pull_request_text(project, number):
    """The pull request's own title and body, which is what the squash commit must carry."""
    payload = rest_json("repos/{}/pulls/{}".format(repository(project), number))
    return payload["title"], payload.get("body") or ""


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--base", help="branch merged into (default: each pull request's own)")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("watch", help="emit terminal check results for every open pull request")
    merge_parser = sub.add_parser("merge", help="merge one pull request if nothing blocks it")
    merge_parser.add_argument("number", type=int)
    merge_parser.add_argument("--dry-run", action="store_true",
                              help="report what blocks it and merge nothing")
    update_parser = sub.add_parser(
        "update", help="merge the base into one pull request's branch so its checks run against it")
    update_parser.add_argument("number", type=int)
    args = parser.parse_args()

    project = Path(args.project).resolve()
    if args.command == "watch":
        return watch(project, args.base)
    if args.command == "update":
        return update(project, args.number, args.base)
    return merge(project, args.number, args.base, args.dry_run)


if __name__ == "__main__":
    sys.exit(main())
