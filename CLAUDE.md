# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

This is the **development project** for **Velvet**, a React-style declarative UI framework for Unity UI Toolkit. The actual product is the embedded package at `Packages/com.velvet.core/` (the source of truth); the surrounding Unity project exists only to build and test it. The package is published to a separate `upm` branch (package-at-root) by CI — never edit there; edit in place under `Packages/com.velvet.core/`.

Guiding principle (from the README): **reproduce React's semantics as faithfully as possible**, deviating only where a C#/Unity constraint makes the deviation a clear improvement. When unsure whether a behavior is "correct," the answer is "what React does."

- **Unity 6000.3.11f1** (Unity 6.3 LTS) is the validated/floor version (`ProjectSettings/ProjectVersion.txt`). Bundled USS uses 6.3-only properties.
- C# root namespace is `Velvet` for the runtime. Namespaces are declared per-file and do NOT track folders — moving a file does not change its namespace.

## Running tests (headless / CLI)

Unity test runs require the editor to be **closed** (it holds the project lock). On macOS the editor binary is `/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity`.

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity
mkdir -p Logs   # gitignored, and one per worktree: /tmp/results.xml is one file for all of them
"$UNITY" -runTests -batchmode -projectPath "$PWD" -testPlatform EditMode \
  -testResults "$PWD/Logs/results.xml" -logFile "$PWD/Logs/run.log"
python3 scripts/test_quality/assert_results_from_this_tree.py Logs/results.xml --log Logs/run.log
python3 scripts/test_quality/assert_no_inconclusive.py Logs/results.xml
```

- **Run a subset / single fixture:** add `-testFilter "Velvet.Tests.SomeFixture"` (semicolon-separates multiple; matches fully-qualified class or method names).
- **PlayMode:** `-testPlatform PlayMode`.
- **Seeding `Library` from another checkout** is what makes a `git worktree` run practical, and it is what puts another checkout's compiled test assemblies under `Library/ScriptAssemblies`. Leave that directory behind — `rsync -a --exclude ScriptAssemblies <other>/Library/ Library/` — so nothing another checkout compiled is sitting there to be reported.
- **Do NOT pass `-nographics`.** Everything that needs a real panel (an `EditorWindow.rootVisualElement`, or anything reading `resolvedStyle` / firing pointer/focus events) goes through `TestGraphics.IgnoreIfHeadless`, so the flag does not fail those tests — it **skips** them, and the run reports green having exercised none of the panel behavior. Graphics-free tests all pass with graphics on, so the flag buys nothing and costs the half of the suite that is hardest to get right.
- Results land in the JUnit-style XML (`grep -o 'passed="[0-9]*"\|failed="[0-9]*"'`); compile errors appear only in the `-logFile`, and `grep ": error "` rather than `error CS` — an analyzer under `Generators~` raises its own at error severity, which fails the compile with no `CS` code in the log to find it by. A run that will not compile writes no XML at all, so what is left at that path is whatever last wrote there — measured, a filter nobody posed reporting a fixture the tree does not hold. And neither the exit code nor any reporter treats an inconclusive case as a failure. `assert_results_from_this_tree.py` refuses the first, `assert_no_inconclusive.py` the second, and each Unity job in CI runs both.
- Interactively, the same suites run from **Window ▸ General ▸ Test Runner**.

### Source generators (separate .NET solution)

The Roslyn analyzers/generators under `Packages/com.velvet.core/Generators~/` are a standalone dotnet solution (not part of the Unity build), pinned by `Generators~/global.json`. Their compiled DLLs in `Runtime/Plugins/` are **committed**.

```bash
cd Packages/com.velvet.core/Generators~
dotnet test Velvet.SourceGenerators.sln -c Release   # generator unit tests (no Unity license needed)
```

A generator change is complete only once the redeployed DLLs are committed with it; `Generators~/README.md` owns the build and deploy steps.

**Every pull request runs both workflows**, because each carries a `required-checks` aggregate that branch protection requires, and a required check whose workflow a path filter stopped from starting stays `Pending` with nothing able to clear it. Pushes to `main` are still split by what a change can affect: `.github/workflows/generators.yml` runs `source-generators` (no license) for `Generators~/**` changes and for `Runtime/**` changes (its drift guards re-derive the hook surface and the generator test stub's signatures from the runtime sources, so a rename there must run the generator suite), and `.github/workflows/test.yml` runs `unity-tests` (EditMode/PlayMode, **skipped unless a `UNITY_LICENSE` secret is set** — see CONTRIBUTING.md) for package/project changes, `scripts/`, and every markdown file in the repository — `assert_results_from_this_tree.py` and `assert_no_inconclusive.py` are what each Unity job runs after its suite. Its licence-free `release-notes` job runs `scripts/release/test_release_notes.py`, which fails when a CHANGELOG version cannot produce a complete release note (`scripts/release/release_notes.py` builds one at release time, from the Highlights block every version carries plus its long-form entries) — the failure lands on the pull request rather than at the dispatch, where no pull request is left to fail. Its licence-free `publication` job runs `scripts/release/published_check.py`, which refuses to let anything merge onto a version the CHANGELOG closed and the `upm` dispatch never published — `scripts/pr/settle.py` and `.claude/hooks/refuse/merge_onto_unpublished_release.py` ask the same module, and CONTRIBUTING.md's release section owns what the window costs. `WorkflowTriggerCoverageTests` fails when a file `DocumentationDriftTests` scans starts no push run, when either required workflow stops subscribing to `pull_request` or `merge_group`, and when either of those triggers gains a path filter. Docs (`docs/`) are DocFX-generated from XML comments via `docs/build.py`.

## Architecture (the parts that span many files)

The render pipeline, in dependency order under `Runtime/`:

1. **`Component/`** — the `V.*` factories build an immutable **`VNode`** tree (`VNodeTypes.cs`); `V.Mount` attaches it. `V.Component(Foo.Render)` wraps the `[Component] static VNode Render()` declared on a static class `Foo`. `V.List` / `V.When` / `V.Fragment` / `V.Provider` are the JSX-construct equivalents. `VNodePool.cs` pools the poolable primitives.
2. **`Reconciler/`** — diffs the new VNode tree against the live fiber tree and patches the underlying `VisualElement`s. Key seams: `FiberRenderer` (mount/flush/dispose), `ChildReconciler` (keyed/positional diff), `GeneralPathReconciler` (inline expansion, where a `null` child is dropped and a `FragmentNode`'s children take its place — so `cond ? node : null` is the idiomatic "render nothing"), `FiberBatchScheduler` (lane-based, coalesced frame-boundary drains), `FiberNodeFactory`/`FiberNodePatcher` (create/patch + attach styling manipulators), `FiberElementCleaner` + `FiberPrimitiveElementPool.cs`/`FiberElementPoolReset` (resource cleanup + reset-before-pool — a recurring bug class is state ghosting across pool reuse, so a reset helper must scrub **every** field a node may have set).
3. **`Hooks/`** — `Hooks.cs` is the public surface (`UseState`/`UseEffect`/`UseStore`/`UseRef`/…). Hook state lives on the `ComponentFiber`; `StateUpdater<T>` (the setter) is reference-stable across renders and supports the functional form `setX.Invoke(prev => next)`.
4. **`Store/`** — `Store<T>` (Zustand-style immutable state + subscribers); `UseStore(store, selector)` subscribes synchronously at render and unsubscribes on unmount.
5. **`Styling/`** — utility-first className resolution (no per-component USS). `StyleRecipe`/`StyleSlotRecipe` are the cva/tailwind-variants equivalent; `StyleArbitraryValueResolver` handles `w-[120px]`-style JIT values; variant **manipulators** (`StyleVariantManipulator` = `hover:`/`focus:`/`active:`, `StyleConditionalVariantManipulator` = `dark:`/`sm:`…, `StyleRelationalVariantManipulator` = `group-`/`peer-`, `StyleGapManipulator`) attach as UI Toolkit `Manipulator`s and are tracked in `ReconcilerContext` so cleanup can remove them.
6. **`Routing/`** — React-Router-style routing: the router, the route matcher and the navigation hooks. The `V.Outlet` factory itself sits with every other factory in `Component/V.cs`.

**Two memoization axes (independent), both `[Component]` knobs in `Component/ComponentAttribute.cs`:**
- `Compiler` (default `true`) = the **React Compiler equivalent**: the ILPP under `CodeGen/` (`CompilerWeaver`, driven by `VelvetCompilerILPostProcessor`) weaves auto-memoization of a component's VNode construction keyed on its hook inputs + props. It processes the `Velvet` assembly and **every assembly that references it**, skipping only the `*.CodeGen.Tests` assemblies and anything handed to it without PE or PDB data, and bails gracefully (no diagnostic) on memo-unsafe hooks. Opt a component out with `[Component(Compiler = false)]`.
- `Memoize` (default `false`) = the **`React.memo` equivalent**: a props-bail at the reconcile boundary (skip a parent-driven re-render when props are shallow-equal). The component's own store/state updates still re-render it. Note auto-memo is keyed on props too, so an unstable callback prop (fresh delegate each render) defeats both axes — stabilize with `UseCallback`.

Do not confuse either axis with the standalone method-level `[MemoizeMethod]` attribute (`Component/MemoizeMethodAttribute.cs`): that is an unrelated source-generator marker that wraps a partial method's body in `V.Memoized(factory, deps)` (see `Documentation~/memoization.md`). Shared name root, independent mechanism.

`Generators~/` (Roslyn) handles the analyzer side (exhaustive-deps, rules-of-hooks); `CodeGen/` (Cecil ILPP) handles the weaving. Both run at compile time.

## Tests

Tests are **colocated** with the code: `Runtime/<Area>/Tests/Editor/` and `.../Tests/PlayMode/`, each its own asmdef (`Velvet.Tests.<Area>.{Editor,PlayMode}`). Editor test asmdefs are Editor-platform and may use `UnityEditor` (e.g. an `EditorWindow` for a real panel). Shared helpers are in `Packages/com.velvet.core/TestUtilities/` (asmdef `Velvet.TestUtilities`, referenced by the test asmdefs):

- `SimulateClick()` / `SimulateChange()` / `SimulateEvent<TEvent>()` — fire events through an element's callback registry without a live panel (the only way to exercise the discrete-event commit path, e.g. `button.SimulateClick()` which runs the handler + a synchronous `FlushImmediate`).
- `DrainImmediateForTest()` / `DrainDelayedForTest()` (`FiberBatchSchedulerTestExtensions`, called as `mounted.GetSchedulerForTest().DrainImmediateForTest()`) and `FlushEffectsForTest()` / `FlushStateForTest()` — the EditMode scheduler/PlayerLoop does not tick, so flush manually.
- EditMode batchmode drives neither layout, the panel scheduler, nor animations. `EditorPanelTestHelpers` reaches each of them by reflection — a styles/layout pass so `resolvedStyle` populates, one scheduler tick, one animation tick, and a substitute panel clock — and `PanelTestBase` packages the host window, the headless guard and that layout pass for a fixture that needs a real panel. Read `TestUtilities/` before hand-rolling any of it.

**Test convention for this repo:** Given/When/Then naming (`Given_..._When_..._Then_...`) for method names, with `// Arrange`/`// Act`/`// Assert` sections in the body, **exactly one assert per test**, and `Assume.That` for preconditions. Verify a regression test is RED without the fix and GREEN with it. Test fixtures are `internal sealed class` (the Unity Test Framework discovers internal fixtures; bases are `internal`/`public abstract`). Comments must not carry issue/PR numbers — state the reason in terms of behavior so it is self-contained. Production types under `Runtime/` carry no test-only members: a test that needs private state reflects for it from the test assembly, or from a shared helper in `TestUtilities/` once a second fixture needs the same reach. `TestOnlyMemberConventionTests` fails when such a member reappears. Templates: `ButtonChildPoolReuseTests.cs`, `ClickDrivenHookLifecycleTests.cs`.

Four ways a green test has lied here:

- An `Assume` that gates the behavior under test is folded into the assertion — one comparison over a tuple of the gated state and the state under test — rather than deleted; deletion is correct only when the assertion alone would still fail on the broken behavior. `scripts/test_quality/assume_gate_check.py` finds two sub-shapes of this that the Arrange/Act/Assert sections make decidable, against `scripts/test_quality/assume_gate_baseline.txt`; CONTRIBUTING.md owns what it reads and what it declines to judge.
- A threshold compared against a measured value is platform-dependent, since font metrics and layout land differently on a CI runner than on a developer machine: assert declared values, and where a measured one must take part, let the floating-point margin separate the right outcome from the wrong one rather than a hand-picked pixel budget.
- `GC.GetAllocatedBytesForCurrentThread()` and `GC.GetTotalMemory()` both report 0 under Unity's Mono while allocation is happening, so "no allocations" holds only after a canary that allocates a known amount moves the instrument. What measures it is Unity's GC.Alloc recorder, reached through `Unity.PerformanceTesting`'s `.GC()` or through `TestUtilities/GCAllocationProbe`. Unity's own no-allocation constraint reads that same counter after widening it back to every thread, so it cannot fail for a reason the delegate owns — do not use it.
- A benchmark is evidence only for the code its fixture drives — a paint change measured free on the manipulator-reconcile fixture, whose class strings never take the paint path it changed — so confirm the code under test runs in a fixture before citing its numbers.

## Conventions

- Commits use Conventional Commits with the `velvet` scope (e.g. `fix(velvet): …`, `feat(velvet): …`, `refactor(velvet): …`).
- Everything in this repo is written in English: code, comments, commit messages, and PR titles/bodies. PR descriptions state what changed and why — never the local workflow that produced the change (audit/review process, agent tooling, session details).
- A PR that adds, changes, or removes a feature updates the corresponding `Documentation~` guide (and the `Documentation~/README.md` index table) in the same PR. If the change has no doc impact, say so explicitly in the PR body. `DocumentationDriftTests` (EditMode) and the Generators~ diagnostic-table tests catch API references, file paths and diagnostic IDs that no longer exist — across the repository's markdown, including the type, member and file names this file itself carries — but only as far as the token no longer occurring in any source: a deleted name a rename left behind in a sibling's declaration still resolves, and comments are stripped from every scanned format that has them, and C# and Python lose their strings besides, C# its `#region` labels too — a string in USS, YAML, JSON or an asmdef is the content rather than a label for it and stays. And they cannot verify that a behavior description is still accurate — that stays the PR author's responsibility.
- A fact the bundled stylesheets own — the longhands a utility writes, where it sits in the cascade — is derived from them rather than restated in C#: `Generators~/src/Velvet.StyleTable` reads `Runtime/Styles/*.uss` and emits `Runtime/Styling/StyleUtilityProperties.g.cs`. Where a mirror is genuinely unavoidable it is pinned by a test that fails when the stylesheet moves, because an unpinned one drifts silently — a guard that unioned property sets where the cascade takes only the last declaration suspended the transitions it existed to protect.
- Documentation is single-source-of-truth: a given fact (the hooks table, the factory list, the diagnostic-ID table, a feature's behavior description) lives in exactly one owning document. Other documents link to it and add at most a one-line summary instead of restating it. Duplicated statements are how docs drift — when the fact changes, only one copy gets updated. This holds for comments too: name a sibling's mechanism ("same ownership rule as `StyleGapManipulator`"), never re-explain it.

## Comments

### Not at all, first

The cheapest comment is the one the code makes unnecessary, and that question comes before every rule below it: can this be carried by the code instead — a name, a type split, a method that serves one caller rather than three? A comment that is hard to write is evidence about the code, not about the comment.

The most expensive comments here have been the ones covering for a shape. A method reached from three paths with different meanings needed a sentence saying which one it was written for, and that sentence was false at two of them — and at one, the behaviour was wrong too, which the comment's confidence hid. A three-valued reading whose members are reachable under different conditions per call site took three corrections, each moving the falsity rather than removing it. In both the sentence sat where the code had stopped explaining itself, and rewriting it was never going to work.

So the first move is to try the code. The three kinds under *Then short* are what survives that attempt, not a licence to skip it.

### True first

A comment states why — never what, and once. Before any question of economy, it has to be **true**. The commonest failure is a comment naming a *mechanism* the author never verified, in the shape "X because Y" with X observed and Y assumed; a second one is rarer and worse, calling a true statement false because it named a term that was not the operative one. Five of the six that shipped in one session are below. The sixth is not, and its absence is the entry: a single sentence about which transform a reading carries was corrected four times, each correction wrong in a new way, and the fifth attempt is this paragraph declining to make it. **When a sentence has been corrected twice and is still wrong, delete it — and ask what about the code made it hard to state.** Twice is where the evidence stops being about the sentence: a third attempt has never worked here, and the two that took four rounds each were describing a method or a reading that meant different things to different callers. Nothing in the code needed the sentence; four rounds of review went into prose that was load-bearing for nobody.

- "the uniform-frame check catches an unstyled capture" — it does not; a backdrop and a font leave the frame varied
- "without the stylesheet this class is inert, so the control holds" — that class has no USS rule at all and is written from C#
- "the halo lies wholly outside the box, so the clip removes the paint" — the interior survives untouched
- "without the sheet a panel resolves none of the utility classes" — the families Velvet realises from C# resolve, and every attempt to enumerate them has come up short: first four were named, then at least eleven more were found. The list belongs in a guard derived from the sheets, not in a sentence
- "the tolerance propagates into a tuple comparison" — it does not, and this one sat in a skill

A reader who trusts a wrong reason is worse off than one who finds no reason and goes to look. So **an unverified mechanism is not written down** — not hedged, not softened, not marked "probably". Write what was measured, or write nothing and let the code speak. If the reason is worth having, it is worth the measurement that earns it; a reason nobody could be bothered to check is a reason nobody should be asked to trust.

The same holds for `Documentation~` prose, a PR description, and a commit message. `DocumentationDriftTests` pins names and paths, never claims.

### Then short

Length is not capped, but every sentence must survive the **deletion test**: delete it; if a competent reader of the surrounding code plus the remaining sentences would still get it right, the sentence was carrying nothing. Delete it for real.

Four things reliably fail the test and should not be written:

- a restatement of the declaration below the comment — its name, signature, or next lines;
- a consequence that follows from a constraint already stated above it;
- an argument that a non-problem is not a problem;
- a sibling file's mechanism re-explained instead of named.

Three things pass, and stay however long they need to be:

- an ordering constraint between passes, writes, or events;
- an invariant a future edit could silently break;
- a rejected alternative and the one reason it was rejected.

Each is a statement about a decision made here. That is what makes it safe to write: the author owns it, and nothing outside this repository can turn it false.

**A fact about the engine is not one of them, and that is where every false comment came from.** Of the six that shipped in one session, four described UI Toolkit rather than a decision — which transform a reading carries, whether a tolerance reaches a tuple's members, what a check catches, what a build embeds. A measured engine fact is a claim about somebody else's code, held by nothing here, and it goes stale when they change it or when the measurement was subtly of something else. So it does not go in a comment on its own. It goes where the stylesheet mirrors go — into a **test that fails when it stops being true** — and the comment names that test instead of restating the fact. This is the same rule as `Generators~/src/Velvet.StyleTable`, one level out: an unpinned mirror drifts silently, and a sentence is a mirror.

Where no test can hold it, it does not get written. A dated observation is the same hedge as "probably" with a timestamp on it: nothing updates it, nothing fails when it rots, and the reader is still left deciding whether it holds. Keep the decision and drop the mechanism — "do not fold a scalar comparison into a tuple" is the part a reader acts on, and how NUnit reaches that outcome is what goes stale.

Two checks that need no judgement:

- A comment's first sentence may not open by restating the identifier it sits above. `// Resolves the direction:` over `ResolveDirection()` is dead text — open with the constraint.
- A block over 12 lines is a review trigger, not an error: the PR states what its load-bearing facts are. Long is fine; unexamined-long is not.

Prose in `Documentation~` is held to the same test. A guide for a polyfill tends to justify the polyfill at length; state what the behavior is and where it deviates, not why the deviation is defensible.