# Contributing to Velvet

## Project status & contributions

Velvet is built and maintained by a single developer as a personal project. Issues,
discussion, and pull requests are welcome — but reviews and responses are **best-effort**:
they may be slow or sparse, and not every change can be merged.

If you need Velvet to move on your own timeline, **forking is encouraged**. The MIT license
lets you build on it and maintain your own line freely — no need to wait on upstream.

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

### Checking that the tests can fail

A green suite says nothing about whether the tests would have noticed the change. `scripts/mutation-check.py`
answers that for the lines a branch touched, by breaking each of them and rerunning the suite:

```bash
python3 scripts/mutation-check.py --base main
```

[Generators~/README.md ▸ Mutation testing](Packages/com.velvet.core/Generators~/README.md#mutation-testing)
covers this and the generator solution's own run, and owns what the verdicts mean and how to read a survivor.

A mutation asks whether any test depends on one line. `scripts/neuter-check.py` asks the other question a
fixture's name makes — with the mechanism it is named for disabled, does it still pass?

```bash
python3 scripts/neuter-check.py --validate   # every anchor still matches exactly once, no editor needed
python3 scripts/neuter-check.py
```

Which layer the cut is made at decides the answer. Some vacuous tests are green at the applier cut and red
at the parser cut, because a gate read one layer up survives a neuter one layer down; others are green at
every cut of their mechanism, having no term any of them can move. So a single cut undercounts, and a
fixture is asked only the cuts it reaches, declared in `scripts/neuter-cuts.json`. Asking a
parser-only fixture an applier cut reports its whole body as holes, which has happened on two separate
rebuilds of this instrument. `NeuterCutAnchorTests` fails in CI when an anchor stops matching exactly once,
so a rename is caught by the pull request that makes it rather than by the next sweep.

### Source generators

The Roslyn source generators live under `Packages/com.velvet.core/Generators~/` and target a
.NET SDK pinned by `Generators~/global.json`.

```bash
cd Packages/com.velvet.core/Generators~
dotnet test Velvet.SourceGenerators.sln -c Release   # run generator unit tests
```

The compiled DLLs under `Packages/com.velvet.core/Runtime/Plugins/` are committed, so a change to
generator source is only complete once they are rebuilt and committed too. Build and DLL-shipping
steps live in [Generators~/README.md](Packages/com.velvet.core/Generators~/README.md) — `./build.sh`
on macOS / Linux, `./build.ps1` on Windows.

## Continuous integration

| Workflow | Trigger | Unity license | Required to merge |
|----------|---------|---------------|-------------------|
| `Source generators ▸ source-generators` | push (filtered) / every PR / merge group | not required | no |
| `Source generators ▸ Required checks (generators)` | every PR / merge group | not required | **yes** |
| `Test ▸ unity-tests` (EditMode / PlayMode) | push (filtered) / every PR / merge group | **required** (skipped if absent) | no |
| `Test ▸ Required checks (Unity)` | every PR / merge group | not required | **yes** |
| `UPM ▸ split` | push to `main` | not required | no |
| `UPM ▸ release` | manual (`workflow_dispatch`) | not required | no |
| `Docs` (DocFX → GitHub Pages) | push to `main` / release / manual | **required** (skipped if absent) | no |

The two required checks are aggregates, and the real jobs are not required themselves. A required check
that does not run stays `Pending` and blocks the pull request with nothing able to clear it, which is what
a path filter, a matrix change or a rename would each cause. The aggregates carry no path filter, `needs:`
the real jobs, and pass when every dependency is `success` **or** `skipped` — the second is what lets a
fork with no `UNITY_LICENSE` merge, since `unity-tests` is skipped in exactly that case.

Path filtering therefore applies to `push` only. Every pull request runs both workflows, and so does every
merge-group entry once a queue is turned on — the `merge_group:` keys are there for that, and
`WorkflowTriggerCoverageTests` fails if either goes missing or gains a filter. GitHub does not accept a path
filter under `merge_group` at all, so one there is a workflow that runs for nothing rather than one that
runs selectively.

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
`Machine bindings don't match`, generate a CI-clean `.ulf` instead: create an activation file
(`.alf`) and upload it at <https://license.unity3d.com/manual>.

The same secret enables both the Unity EditMode/PlayMode tests (`test.yml`) and the API-docs build
(`docs.yml`). A Personal license has a single seat, so avoid running CI while the local editor holds
the license on the same account.

## Releasing

1. Bump `version` in `Packages/com.velvet.core/package.json` and update `CHANGELOG.md`.
2. Merge to `main` (the `upm` branch is updated automatically).
3. Run the **UPM** workflow via *Actions ▸ UPM ▸ Run workflow*, entering the same version.
   This tags `vX.Y.Z` on the `upm` (package-at-root) commit and publishes a GitHub release.

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
docs/build.sh                     # → docs/_site/index.html
```

For a non-default editor install, set `UnityEditorContents` (see `docs/build.sh`). The
**Docs** workflow publishes the site to GitHub Pages on release (it needs the Unity license secret;
verify its editor-image tag and managed-assembly path on the first run).
