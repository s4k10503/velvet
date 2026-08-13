#!/usr/bin/env python3
"""Watch open pull requests, and merge one only when every precondition holds.

Both halves existed as instructions rather than as code: `stop/unsettled_pr.py` printed a watcher for
the reader to reimplement, and the merge was typed by hand. An instruction is re-derived each time,
and that hook's own text owns what a re-derived watcher gets wrong.

**Every precondition below has a refuse hook behind it**, so a `gh pr merge` typed by hand is held
to them as well; `refuse/merge_unproven_head.py` records which hook holds which. Those hooks match on
`gh pr merge` and do not see the `gh api -X PUT .../merge` this script sends, so teaching them that
shape is its own change. What this adds is reporting them together: one run names everything wrong
rather than costing a round of CI per reason. If a precondition is worth having here it belongs in a
hook, because a script nobody is obliged to run guards nothing.

Seven preconditions:

- **Checks are bound to the head SHA they were read at.** The checks API answers about whatever it
  last recorded, which after a force-push is the previous commit's run. So the head is read, then the
  checks, then the head again, and a change between the two readings voids the answer. The merge
  request carries that SHA as well, so a push landing after the last reading is refused by GitHub
  rather than by whoever reads the history next.
- **The branch must contain the current base.** `mergeStateStatus` reports CLEAN for a branch whose
  tests never saw a commit that is now on main: GitHub reports BEHIND only where the base requires
  up-to-date heads, which this repository deliberately does not. So the merge-base is compared
  directly.
- **No worktree may hold the branch.** The branch is deleted locally after the merge, and a worktree
  holding it makes that delete fail once the merge has already happened — so the branch outlives the
  pull request and has to be swept by hand later, when nothing in the checkout can still tell a
  merged branch from an abandoned one.
- **An empty check list is not "still running".** It means no workflow was ever triggered for that SHA.
- **The base must not hold an unpublished release.** `scripts/release/published_check.py` owns that
  decision, and CONTRIBUTING.md's release section owns what goes wrong without it.
- **A draft is not merged**, and neither is one whose merge state is `dirty`.
- **A head on another repository is not merged from here.** Its branch is a ref this checkout has
  not got, so the containment reading above cannot be taken at all — and a reading nobody took is
  not a precondition anybody met.

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
    """subprocess.run with a bound, reporting a timeout as the failure every caller here catches."""
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
PullRequest = collections.namedtuple("PullRequest", "sha branch draft merge_state fork")


def pull_request(project, number):
    """The pull request's own fields, in one request.

    `fork` is what stops `branch` being handed to git: a cross-repository head names a branch on the
    fork, `origin/<it>` is not a ref here, and `contains_base` exits 128 on the lookup. A head whose
    repository is gone reads as a fork too, which is the same answer — this checkout cannot see it.

    Both names come off this payload rather than one of them off `remote.origin.url`. GitHub answers
    the same pull request for any casing of the path, so a clone made with different capitals, or a
    repository since renamed, would make every pull request read as a fork — and then nothing is
    recorded ready and the guard that reads that state stops firing.
    """
    payload = rest_json("repos/{}/pulls/{}".format(repository(project), number))
    head = payload.get("head") or {}
    home = ((head.get("repo") or {}).get("full_name") or "")
    base_repository = ((payload.get("base") or {}).get("repo") or {}).get("full_name") or ""
    return PullRequest(head["sha"], head["ref"], bool(payload.get("draft")),
                       payload.get("mergeable_state") or "", home != base_repository)


# The bucket names this script decides from. A conclusion absent from the table falls to `fail` at
# the lookup, so a conclusion GitHub adds later blocks until somebody classifies it rather than
# merging unclassified.
_BUCKET = {"success": "pass", "neutral": "skipping", "skipped": "skipping",
           "failure": "fail", "timed_out": "fail", "action_required": "fail",
           "cancelled": "cancel", "stale": "cancel"}

# The legacy commit-status vocabulary, which is a different set of words for the same decision.
_STATUS_BUCKET = {"success": "pass", "pending": "pending", "failure": "fail", "error": "fail"}


def checks(project, sha):
    """Check results for one head, or an empty list when no workflow ever ran for it."""
    slug = repository(project)
    return check_results(rest_json("repos/{}/commits/{}/check-runs?per_page=100".format(slug, sha)),
                         rest_json("repos/{}/commits/{}/status?per_page=100".format(slug, sha)))


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


def check_results(runs, statuses):
    """One bucket per check, from the check-runs payload and the legacy commit-status one.

    One commit carries two check surfaces, and the base can require a context from either, so
    leaving one out would decide a merge against a check nobody read. Only the status payload's
    individual entries become buckets; its rollup state is not read at all.

    A page that did not carry everything raises rather than deciding; `whole_page` owns why.
    """
    listed = runs.get("check_runs", [])
    reported = statuses.get("statuses", [])
    whole_page(runs, listed, "check runs")
    whole_page(statuses, reported, "commit statuses")
    results = [{"name": run.get("name", ""),
                "bucket": "pending" if run.get("status") != "completed"
                else _BUCKET.get(run.get("conclusion") or "", "fail")}
               for run in listed]
    results.extend({"name": status.get("context", ""),
                    "bucket": _STATUS_BUCKET.get(status.get("state") or "", "fail")}
                   for status in reported)
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
    merge will actually happen against and would report a stale answer as a clean one. Both refs have
    to have been fetched first: `project_state` does it for the readings `blocking_reasons` takes,
    and `update` fetches for itself.
    """
    merge_base = gh_git(project, "merge-base", f"origin/{base}", f"origin/{branch}").strip()
    base_head = gh_git(project, "rev-parse", f"origin/{base}").strip()
    return merge_base == base_head


def reasons_from(before, after, results, branch, base, holds_base, held_by_worktree,
                 unpublished_release, draft, merge_state, fork):
    """Every reason not to merge, decided from plain data so the decision is testable without a network.

    `unpublished_release` takes no default on purpose: a caller that stops supplying it would otherwise
    read as a clean base, and the only production caller is held by no test.

    A moved head returns with the reasons that are not about a commit and nothing else: with the
    readings straddling a force-push, nothing else read here is known to be about the same commit, so
    reporting the rest would be reporting about two SHAs at once. The publication reason is about the
    base, which the force-push did not touch, and draft is a state of the pull request rather than of
    its head.
    """
    reasons = [unpublished_release] if unpublished_release else []
    if draft:
        reasons.append("it is a draft: mark it ready for review first")
    if before != after:
        return reasons + [f"head moved from {before[:7]} to {after[:7]} while its checks were being read"]

    # Not merely a better message for what the containment reading already blocks. The two readings
    # are of different base tips: `project_state` fetches once a cycle and this is a fresh read per
    # pull request, so a branch can contain the tip that fetch saw while GitHub reports a conflict
    # against a newer one. There the containment reason is absent and this is the only thing
    # blocking, which is why it is read rather than left to the message.
    #
    # `unknown` is still left out: it is the absence of a reading rather than a reading, and a state
    # that comes and goes would drop and re-add the entry, resetting the age
    # `refuse/edit_while_a_ready_pr_sits.py` measures a pull request by.
    if merge_state == "dirty":
        reasons.append(f"it conflicts with {base}: resolve the conflict in the branch, which "
                       f"`settle.py update` declines to do")

    if not results:
        reasons.append(f"no check has run for {after[:7]}: a workflow was never triggered for this head")

    unfinished = [entry["name"] for entry in results
                  if entry["bucket"] not in TERMINAL_PASS and entry["bucket"] not in TERMINAL_FAIL]
    failed = [f"{entry['name']}={entry['bucket']}" for entry in results
              if entry["bucket"] in TERMINAL_FAIL]
    if unfinished:
        reasons.append("still pending at {}: {}".format(after[:7], ", ".join(sorted(unfinished))))
    if failed:
        reasons.append("failing at {}: {}".format(after[:7], ", ".join(sorted(failed))))

    if fork:
        # Ahead of the containment reason rather than beside it: `holds_base` was never computed for
        # a fork, so reporting it would be reporting a default as a reading.
        reasons.append(f"its head is on another repository: this settles branches on origin, so "
                       f"nothing here read whether it contains origin/{base}")
    elif not holds_base:
        reasons.append(f"does not contain origin/{base}: merge it in and let the checks run again")

    if held_by_worktree:
        reasons.append(f"a worktree holds {branch}: remove it first, or the local branch outlives "
                       f"the merge and has to be swept by hand later")

    return reasons


# What merge needs from the readings besides the verdict: the SHA the checks were read at, which the
# merge request carries, the branch it deletes afterwards, and the check results, which `watch` prints.
Blocking = collections.namedtuple("Blocking", "reasons head branch results")

# The readings that answer for the whole repository rather than for one pull request. Taken once and
# handed down, so a watcher poll over N pull requests costs one fetch and one `git ls-remote --tags`
# rather than N of each.
ProjectState = collections.namedtuple("ProjectState", "held unpublished_release")


def project_state(project, base):
    """The per-repository readings, with the fetch that both of the git ones below depend on."""
    gh_git(project, "fetch", "origin", "--quiet")
    return ProjectState(worktree_branches(project),
                        published_check.unpublished_reason(project, f"origin/{base}", fetch=False))


def blocking_reasons(project, number, base, state=None):
    """reasons_from, with every reading taken from the repository and the API."""
    state = project_state(project, base) if state is None else state
    before = pull_request(project, number)
    results = checks(project, before.sha)
    after = head_sha(project, number)
    return Blocking(reasons_from(before.sha, after, results, before.branch, base,
                                 holds_base=(not before.fork
                                             and contains_base(project, before.branch, base)),
                                 held_by_worktree=before.branch in state.held,
                                 unpublished_release=state.unpublished_release,
                                 draft=before.draft,
                                 merge_state=before.merge_state,
                                 fork=before.fork),
                    after, before.branch, results)


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


def watch(project, base):
    """Emit each check that reaches a terminal state, once, and hold the heartbeat open meanwhile."""
    lock, holder = hold_the_watch()
    if lock is None:
        print(f"Refusing to watch: process {holder or 'unknown'} already holds "
              f"{watcher_state.LOCK}. A second watcher polls the same API on its own cycle against "
              f"the same quota, and writes the same heartbeat, so neither says which one is alive.\n"
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
              f"stopped. Find it with `ps -Ao pid=,command= | grep '[s]ettle.py watch'` and kill "
              f"it; if it is already gone, its last heartbeat ages out within "
              f"{watcher_state.STALE_AFTER}s.", file=sys.stderr)
        return 1

    seen = set()
    ready_since = {}
    while True:
        try:
            pull_requests = open_pull_requests(project)
            state = project_state(project, base)
        except RuntimeError as error:
            print(f"! {error}", flush=True)
            time.sleep(watcher_state.POLL_SECONDS)
            continue
        beat()

        ready = set()
        for number in pull_requests:
            try:
                blocking = blocking_reasons(project, number, base, state)
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
    branch = rest("repos/{}/pulls/{}".format(repository(project), number), ".head.ref")
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
    print(f"PR#{number} merged: {blocking.branch} squashed onto {base}")
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
    parser.add_argument("--base", default="main", help="branch merged into (default: main)")
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
