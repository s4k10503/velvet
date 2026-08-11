# Contributing to Velvet

## Project status & contributions

Velvet is built and maintained by a single developer as a personal project. Issues,
discussion, and pull requests are welcome — but reviews and responses are **best-effort**:
they may be slow or sparse, and not every change can be merged.

If you need Velvet to move on your own timeline, **forking is encouraged**. The MIT license
lets you build on it and maintain your own line freely — no need to wait on upstream.

### What a pull request says it came from

A pull request opens by naming its origin. If it closes an issue, the first line closes it on merge:

```
Closes #123.
```

If it closes nothing — a tooling change, a release, something noticed while reading — say so with a
reason, on a line of its own:

```
No issue: found while reading the pool reset helpers.
```

Either answer is fine; the silence is not, and it is the one that happened: a change that came
straight out of an issue was merged without linking it, so the issue stayed open with its work
already shipped. `refuse/pr_body_of_another_branch.py` declines a `gh pr create` whose body carries
neither. An answer is a closing or referring keyword — Closes, Fixes, Resolves, Refs — against the
number right after it, or the issue's own URL. A number on its own is not one: a colour is six digits
behind a `#`, and a number mentioned in passing closes nothing on merge.

The guard reads the description that will be posted, so the body has to exist before the command
runs. What it cannot read it declines rather than skips, and that is the part you are most likely to
meet:

- the file is not there — write the body in a step of its own and open the pull request in the next,
  because a heredoc in the same command has not run yet when the guard looks;
- the path is still unexpanded, or the body comes from stdin;
- the command changes directory and the body path is relative, so `gh` would open a different file
  than this one reads — give the body an absolute path.

A command carrying no body operand at all — `--fill` and its relatives, `--template`, `--editor`, the
interactive form — holds no text here, so the question goes unasked, and `--dry-run` or `--help`
opens nothing to ask about. Only `gh pr create` is asked: `gh pr edit --body-file` is how an answer
gets added after the fact.

It does not judge what the body is about, and no longer tries to. An earlier version dated the body
file against the branch's first commit. Posed the leftover it existed for — a body stamped when one
pull request here was opened, against the branch of the next, whose first commit lands sixteen
seconds later — it allowed it, the window being fifteen minutes wide. Widening or narrowing that
window does not help: a `PreToolUse` hook runs first, so the file it would date is whatever was
already at the path rather than the description that will be posted.

A gh that the parser does not recognise — behind sudo or bash -c, or with gh's own options before the
subcommand — is not seen at all rather than refused. Four attempts to reach it each refused ordinary
commands or broke a sibling guard, so the guard stops where it can answer.

## Local development

1. Install **Unity 6000.3.11f1** (see `ProjectSettings/ProjectVersion.txt`).
2. Open this repository as a Unity project. Velvet is loaded as an embedded package
   from `Packages/com.velvet.core/`; edit it in place.
3. Run the Unity test suites from **Window ▸ General ▸ Test Runner** (EditMode and PlayMode).

### Looking at what Velvet renders

Every `[VelvetPreview]` story can be rendered to a PNG, so a change to layout, styling or paint can
be inspected rather than only measured:

```bash
/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity -runTests -batchmode -projectPath "$PWD" -testPlatform PlayMode -testFilter "Velvet.Tests.StoryCaptureTests" -testResults /tmp/capture.xml -logFile /tmp/capture.log
```

The images land in `Logs/story-captures/` (git-ignored), grouped into a directory per story group, or
under the directory named by `VELVET_STORY_CAPTURE_DIR`. Each run deletes the captures the previous
run recorded in its manifest, so a renamed or deleted story leaves nothing stale behind. A file the
harness never wrote is never removed — but a capture it *did* write is, so point the variable at a
directory you are content for it to own rather than at one holding anything else. The run fails if a
story does not mount, if it renders a uniform frame, or — once for the whole run, not per story — if
the bundled stylesheet resolves none of its plain classes.

**Look at the images.** Those three checks are the floor, not the ceiling: the uniform-frame one in
particular is satisfied by a single differing pixel, so a story can render almost nothing and still
pass. The defects found in this harness so far — a missing backdrop that made every semantic-token
story capture near-blank, and a panel whose height was its content's rather than the story's — were
green under every check and visible immediately in the PNG. Interactively the same stories are live
in **Window ▸ Velvet ▸ Preview**.

### A run that stays in the process list

A process can enter macOS state `UE` — uninterruptible wait while exiting — where no signal
reaches it, `kill -9` included, and only a reboot clears it. Two different situations end there,
and they want opposite responses, so read the log before deciding which one is in front of you:

```bash
ps -eo pid,stat,etime,time,comm | awk '$2 ~ /^UE/'
tail -3 <logfile>
```

**The log ends after `Exiting batchmode successfully now!`** — the run did its work and then could
not exit. Its results stand while the process is still listed. Ten of these were resident while
twenty-five editor invocations ran on the same machine, eight player builds and fifteen EditMode
suites, and all twenty-five completed. Nothing needs doing.

**The log stops mid-compile and stays that size** — the run stalled, and the `netcorerun` it
launched wedges while the editor is still alive, before anything is killed. `SIGTERM` on the editor
reaps the editor; the wedged child does not follow it out, and no signal recovers it.

Not every unreapable process here comes from Unity, and none of them blocks a later run. They
accumulate, so `.claude/hooks/report/wedged_processes.py` reports the set at session start once it
holds enough memory for a reboot to be worth the interruption, and says nothing below that.

### Checking that the tests can fail

A green suite says nothing about whether the tests would have noticed the change. `scripts/test_quality/mutation_check.py`
answers that for the lines a branch touched, by breaking each of them and rerunning the suite:

```bash
python3 scripts/test_quality/mutation_check.py --base main
```

[Generators~/README.md ▸ Mutation testing](Packages/com.velvet.core/Generators~/README.md#mutation-testing)
covers this and the generator solution's own run, and owns what the verdicts mean and how to read a survivor.

A mutation asks whether any test depends on one line. `scripts/test_quality/neuter_check.py` asks the other question a
fixture's name makes — with the mechanism it is named for disabled, does it still pass?

```bash
python3 scripts/test_quality/neuter_check.py --validate   # every anchor still matches exactly once, no editor needed
python3 scripts/test_quality/neuter_check.py
```

Which layer the cut is made at decides the answer. Some vacuous tests are green at the applier cut and red
at the parser cut, because a gate read one layer up survives a neuter one layer down; others are green at
every cut of their mechanism, having no term any of them can move. So a single cut undercounts, and a
fixture is asked only the cuts it reaches, declared in `scripts/test_quality/neuter_cuts.json`. Asking a
parser-only fixture an applier cut reports its whole body as holes, which has happened on two separate
rebuilds of this instrument. `NeuterCutAnchorTests` fails in CI when an anchor stops matching exactly once,
so a rename is caught by the pull request that makes it rather than by the next sweep.

Most holes are legitimate — a negative assertion no cut can falsify, a case whose subject is another cut's
layer — so what is worth catching is not the count but a change to the set, which a hole appearing while
another disappears leaves identical. `scripts/test_quality/neuter_holes.txt` carries the approved set;
`--report` regenerates it, so a sweep is read as a diff against it. Nothing runs the sweep automatically:
wiring it into CI needs a licence activation this repository does not have.

### Checking that a new test could have failed

Both instruments above ask what happens when the production code is broken on purpose.
`scripts/test_quality/base_red_check.py` asks the cheaper question first, and the one a pull request is
actually claiming: was this case red before the change it says it pins? It checks out the merge base,
copies the branch's changed test files onto it, and runs the cases the branch wrote there.

```bash
python3 scripts/test_quality/base_red_check.py --base origin/main --plan   # what it would ask, no run
python3 scripts/test_quality/base_red_check.py --base origin/main --warm-library Library
```

**It passes only where every changed case was measured on a base tree that demonstrably answers.** Most
of what goes wrong with a run like this ends in a reading nobody took, and a reading nobody took is never
a pass. So a case that passes on the base fails the check, and a case that could not compile there does
not — it names something the branch adds, which is a pin doing its job — but that second reading is only
believed on a tree the run proved can build and answer at all. Two things prove it. Fixtures of the
base's own that the branch did not carry run alongside, and at least one has to pass; a run that wrote no
results file at all fails outright rather than reading as a base that built none of the branch's tests.
Alongside that, the cases the branch left alone in a file it changed nothing shared in are the base's own
text, so one of those going red means the tree is answering about itself and that fixture's verdicts are
withdrawn. Change a `[SetUp]`, a field, a private helper or anything under `TestUtilities/`, and those
cases stop being the base's text and stop being read as the instrument — the run says which and why.

Two kinds of case belong on the base, and say so above themselves with a reason a reviewer can weigh:

```csharp
// GREEN_ON_BASE(characterization): the keyed-reorder order this refactor must not change.
```

```python
# GREEN_ON_BASE(refactor): the settle-path names this rename preserves.
```

`characterization` pins behaviour the base already has; `refactor` rides with a change meant to preserve
it. A declaration answers for the change written under it, and it is read three ways so it cannot outlive
what it describes: one over a case that turns out red on the base fails the check, one whose category or
reason the script refuses fails it, and one the branch did not itself write is a declaration for a change
the base already carries and does not cover this one — restate it. The base tree is a checkout the
machine has never imported, and that import is most of what a base run costs; `--warm-library` copies an
existing `Library` into it, sharing blocks where the filesystem will.

`Test ▸ base-red-python` runs the Python lane on every pull request and needs no licence.
`Test ▸ base-red` runs the C# lane where one is configured, but only one round of it: a base that
cannot build one carried file writes no results for anything, and separating that file from the ones
standing next to it takes withdrawing it and asking again, which is what the local run does and a
workflow does not. Run it locally on a branch whose tests are the point.
`scripts/test_quality/test_base_red_check.py` holds the reader against every test file in this
repository and runs in `Test ▸ test-quality`.

### Repository scripts

`scripts/` holds the harnesses, grouped by what they are for — `test_quality/` (mutation, neuter,
inconclusive-result guard), `release/` (the release-note builder), `unity/` (sample sync). Two rules keep
the tree readable:

- **Python, named in `snake_case`.** Every harness is importable, so a test can exercise it directly rather
  than only through a shell invocation — which is what `release/test_release_notes.py` does. Python needs no
  runtime or configuration file this repository does not already have; `python3` is on the CI runner and on
  every developer machine that can run the Unity suites.
- **A script's name reaches C#.** `NeuterCutAnchorTests`, `WorkflowTriggerCoverageTests`,
  `StarterSampleShippingTests`, `BaseRedDeclarationTests` and `DocumentationDriftTests` each name one, so
  a rename that misses a reference fails a pull request instead of failing the next person to run it.

The same holds for `.claude/hooks/` and for the two build scripts, so nothing in this repository is
written in shell. That is not a preference about shell: the guards under `.claude/hooks/` parse a
tool call's JSON, compare it against git state and format a refusal, and each of those was already
reaching for `python3` from inside a shell script by the end. What the rule buys is that a guard can
be tested by importing it, and that `Generators~/build` is one file instead of a bash and a
PowerShell copy that nothing compared.

`.claude/hooks/` is grouped by what a script is able to stop:

- `refuse/` — `PreToolUse`. Stops the tool call, by exiting 2 with the reason on stderr or by
  answering with a `permissionDecision` of `deny`.
- `stop/` — `Stop`. Exits 2 to refuse the end of a turn.
- `report/` — `SessionStart`, `SubagentStop`, `PostToolUse`. Stops nothing: `PostToolUse` fires
  after the tool has already run, so it writes into the transcript and exits 0 whatever it finds.
- `lib/` — imported by the rest, wired to no event of its own.

A guard over state that is shared rather than owned by one session — a branch, the stash — belongs
in `.claude/settings.json`, where it runs for every session. An agent's frontmatter can only narrow
that: a guard named there and nowhere else is absent from the main session and from every other
agent type. Such a guard says so with a `HOOK_SCOPE = "session"` line.

A `PreToolUse` guard is reached only for the tools its `"matcher"` names, and acts only on the tools
its own gate admits. It declares the second as a `HOOK_TOOLS = {...}` set and gates on that set
rather than on a literal, which is what leaves the two halves comparable. Register it under the tool
names themselves: a matcher naming no set a check can read is refused — `"*"`, an empty string, and a
`PreToolUse` entry carrying no `"matcher"` key at all, which is the shape the `SessionStart`, `Stop`
and `SubagentStop` entries take. A guard covering several tools spells them out in the matcher.

Every failure mode here is silence, so the wiring is asserted rather than trusted.
`HookWiringCoverageTests` pairs each script against the settings and agent frontmatter that run it,
in both directions, fails on a script name a hook builds a path from that no file answers to, fails
on a `HOOK_SCOPE = "session"` guard that the settings do not register or that an agent registers a
second time, and fails when a `PreToolUse` guard's `HOOK_TOOLS` and its matcher name different tools,
when the guard declares a set its gate never reads, and when its gate answers none of the payloads it
is posed under a tool it is routed — which is the shape an inverted gate takes, since such a gate
returns before its readings for exactly the tools it exists to read. A gate reading no tool name at
all answers under both, and fails the other way round. That first one also fails for a guard none of
the fixture's `GatePayloads` happens to pose anything about — a guard added since that table was last
extended wants a row added to it rather than a change to itself — and for a guard that exits neither
0 nor 2 under any of them, which is one raising rather than deciding.

A guard's readings are the other way it goes quiet. Exiting 0 lets the tool through, so a `gh` that
could not answer and a repository with nothing to report reach the caller as the same event — which
is how an exhausted GitHub GraphQL quota emptied `gh pr view --json headRefName` while the separate
REST quota stayed healthy, leaving `gh` working everywhere else and two merge guards alone silent.
Each guard therefore declares an `UNREADABLE_POLICY` of `"refuse"`, `"allow"` or `"none"`, and an
`UNREADABLE_PROBE` holding a tool input to pose it. `scripts/hooks/test_unreadable_state_check.py`
runs every guard with `git` or `gh` unable to answer and compares the verdict against the
declaration, rather than reading the source for the shape: which direction a `return 0` means is a
property of the call site, and `shared_git_state.py`'s refusal is the answer it gives when git cannot
be asked. `"none"` claims the probe reaches neither program, and fails if either is invoked.
`"allow"` needs a comment above it saying what holds instead, and fails unless another guard in the
directory refuses the same probe — otherwise the tool call is guarded by nothing at all. It runs in
the licence-free `source-generators` job rather than beside the fixtures above, which are skipped
entirely on a checkout with no `UNITY_LICENSE` secret.

### Source generators

The Roslyn source generators live under `Packages/com.velvet.core/Generators~/` and target a
.NET SDK pinned by `Generators~/global.json`.

```bash
cd Packages/com.velvet.core/Generators~
dotnet test Velvet.SourceGenerators.sln -c Release   # run generator unit tests
```

The compiled DLLs under `Packages/com.velvet.core/Runtime/Plugins/` are committed, so a change to
generator source is only complete once they are rebuilt and committed too. Build and DLL-shipping
steps live in [Generators~/README.md](Packages/com.velvet.core/Generators~/README.md) — `./build.py`
on every platform.

## Continuous integration

| Workflow | Trigger | Unity license | Required to merge |
|----------|---------|---------------|-------------------|
| `Source generators ▸ source-generators` | push (filtered) / every PR / merge group | not required | no |
| `Source generators ▸ repository-settings` | push (filtered) / every PR / merge group; skipped when the workflow runs in a fork | not required | no |
| `Source generators ▸ Required checks (generators)` | every PR / merge group | not required | **yes** |
| `Test ▸ license-check` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ unity-tests` (EditMode / PlayMode) | push (filtered) / every PR / merge group | **required** (skipped if absent) | no |
| `Test ▸ release-notes` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ publication` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ test-quality` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ base-red-python` | push (filtered) / every PR / merge group | not required | no |
| `Test ▸ base-red` (EditMode / PlayMode) | every PR | **required** (skipped if absent) | no |
| `Test ▸ Required checks (Unity)` | every PR / merge group | not required | **yes** |
| `UPM ▸ split` | push to `main` / manual (`workflow_dispatch`, which also tags and publishes the release) | not required | no |
| `Docs` (DocFX → GitHub Pages) | push to `main` / release / manual | **required** (skipped if absent) | no |

The two required checks are aggregates, and the real jobs are not required themselves. A required check
that does not run stays `Pending` and blocks the pull request with nothing able to clear it, which is what
a path filter, a matrix change or a rename would each cause. The aggregates carry no path filter, `needs:`
the real jobs, and pass when every dependency is `success` **or** `skipped` — the second is what lets a
fork with no `UNITY_LICENSE` merge, since `unity-tests` is skipped in exactly that case.

Path filtering therefore applies to `push` only. Every pull request runs both workflows, and so does every
merge-group entry once a queue is turned on — the `merge_group:` keys are there for that, and
`WorkflowTriggerCoverageTests` fails if either of the two gated triggers goes missing from either workflow,
or gains a path filter. Skipping work per queue entry is a job-level condition, not a trigger filter: a
required check that does not start has nothing able to clear it.

`main` does not require heads to be up to date before merging. That setting serialises the queue — each
merge invalidates every other branch's run, and the Unity matrix is 21–25 minutes — without testing the
combination it exists to protect any better than a merge does. What does test that combination is a merge
queue, which is a separate change.

The source-generator tests and the `upm`-branch split run with no Unity license, so the pipeline
works out of the box on a free account. The Unity EditMode/PlayMode job is skipped automatically
unless a license secret is configured.

### Enabling Unity tests (free Personal license)

A free Unity **Personal** license works in CI. Add these **Actions secrets** (Settings ▸ Secrets and
variables ▸ Actions):

- `UNITY_LICENSE` — the contents of your `.ulf` license file
- `UNITY_EMAIL` — your Unity account email
- `UNITY_PASSWORD` — your Unity account password

Get the `.ulf` by activating a free Personal license in Unity Hub and copying the generated file
(`Unity_lic.ulf` — macOS `/Library/Application Support/Unity/`, Windows `C:\ProgramData\Unity\`,
Linux `~/.local/share/unity3d/Unity/`). CI runs Unity through game-ci, which randomizes the machine
ID on activation, so a locally-activated `.ulf` works. If activation ever fails with
"Machine bindings don't match", generate a CI-clean `.ulf` instead: create an activation file
(`.alf`) and upload it at <https://license.unity3d.com/manual>.

The same secret enables both the Unity EditMode/PlayMode tests (`test.yml`) and the API-docs build
(`docs.yml`). A Personal license has a single seat, so avoid running CI while the local editor holds
the license on the same account.

## Releasing

1. Close the version in `Packages/com.velvet.core/CHANGELOG.md` — rename the working section to
   `## [X.Y.Z] - YYYY-MM-DD` — and bump `version` in `package.json` to match.
2. Merge to `main` (the `upm` branch is updated automatically).
3. Run the **UPM** workflow via *Actions ▸ UPM ▸ Run workflow*, entering the same version.
   This tags `vX.Y.Z` on the `upm` (package-at-root) commit and publishes a GitHub release.

Between step 2 and step 3, `main` names a version that does not exist. The dispatch builds the note
from the CHANGELOG section, which was written before anything merged in that window and describes none
of it — so those commits ship inside the release, undescribed. v2.0.1 spent a day there and took twelve
merges, and publishing it meant tagging the release commit by hand and dispatching from the tag, since
dispatching from the branch would have shipped all twelve.

So the window is guarded: `settle.py merge` and `gh pr merge` refuse while it is open, and
`Test ▸ publication` fails for a pull request whose checks run in it.
`scripts/release/published_check.py` decides it and states the repair in its own message.

**A pull request that went green *before* the release landed keeps that result.** This repository sets
`strict_required_status_checks_policy: false` so a 21-minute Unity matrix is not re-run for every base
move, so the merge button on github.com stays enabled for it. What refuses there is `merge_onto_unpublished_release.py`, `stale_merge.py`
and `settle.py`'s contains-base precondition, none of which github.com consults; turning the strict
policy on is what would close it server-side, at the cost that buys.

**A green pull request left sitting starts refusing every edit.**
`.claude/hooks/refuse/edit_while_a_ready_pr_sits.py` refuses every editing tool once one has been ready
for fifteen minutes, and the instruction it prints is `settle.py merge`, which the publication guard now
declines. Both are behaving correctly and the combination is a stall: record the deferral the hook's own
message describes, or publish.

**If the dispatch itself is what is broken, the guard has no in-band escape.** `upm.yml` runs from the
workflow file at whatever ref is dispatched, so tagging the release commit and dispatching from the tag
re-runs the same broken workflow — that manoeuvre solves a different problem, a branch tip that has
moved on, which is what v2.0.1 needed. What works is a branch carrying the fix on top of the release
commit, pushed and dispatched with `--ref`, accepting that the fix ships inside that release. What does
not is an administrator merge: `protect-main` lists no bypass actor and reports
`current_user_can_bypass: never`, so `gh pr merge --admin` is refused like any other. Short of the
branch, the remaining lever is the ruleset itself — a bypass actor, or `enforcement: disabled` — which
is a repository-settings change and not a merge.

Note what a dispatch from a tag costs either way: `upm.yml` force-pushes the split to `upm`, so a
consumer tracking `#upm` unpinned drops back to that commit until the next push to `main`.

**Afterwards, a red left over from the window does not clear itself.** The tag list is read live, so any
fresh run of the check passes once the release exists — re-run the failed jobs, or push.

The release notes are built from that CHANGELOG section by `scripts/release/release_notes.py`, so a release
is never written twice. Each version therefore needs a `### Highlights` block above its
`### Added` / `### Changed` / `### Fixed` headings: a handful of one-paragraph bullets, which the
note leads with and the long-form entries follow, collapsed. A version missing one fails the release
— and fails the `Test ▸ release-notes` check on the pull request that introduced it, which is where
you want to hear about it. Preview a note before merging:

```bash
python3 scripts/release/release_notes.py --version X.Y.Z --repo s4k10503/velvet
```

Consumers then install a pinned version with:

```jsonc
"com.velvet.core": "https://github.com/s4k10503/velvet.git#v1.0.0"
```

## API documentation

The API reference under `docs/` is generated by [DocFX](https://dotnet.github.io/docfx/) from the
runtime XML doc comments. `docs/Velvet.Docs.csproj` compiles the `Velvet` sources only so DocFX can
bind the public API — it is not part of the Unity build and ships nothing.

Generate the site locally:

```bash
# 1. Compile the project once (so Library/ScriptAssemblies has the referenced DLLs):
/path/to/Unity -batchmode -nographics -quit -projectPath . -logFile -
# 2. Install DocFX and build:
dotnet tool install -g docfx     # once; ensure ~/.dotnet/tools is on PATH
docs/build.py                     # → docs/_site/index.html
```

For a non-default editor install, set `UnityEditorContents` (see `docs/build.py`). The
**Docs** workflow publishes the site to GitHub Pages on release (it needs the Unity license secret;
verify its editor-image tag and managed-assembly path on the first run).
