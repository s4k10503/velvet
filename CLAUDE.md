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
"$UNITY" -runTests -batchmode -projectPath "$PWD" -testPlatform EditMode \
  -testResults /tmp/results.xml -logFile /tmp/run.log
```

- **Run a subset / single fixture:** add `-testFilter "Velvet.Tests.SomeFixture"` (semicolon-separates multiple; matches fully-qualified class or method names).
- **PlayMode:** `-testPlatform PlayMode`.
- **Do NOT pass `-nographics`.** Everything that needs a real panel (an `EditorWindow.rootVisualElement`, or anything reading `resolvedStyle` / firing pointer/focus events) goes through `TestGraphics.IgnoreIfHeadless`, so the flag does not fail those tests — it **skips** them, and the run reports green having exercised none of the panel behavior. Graphics-free tests all pass with graphics on, so the flag buys nothing and costs the half of the suite that is hardest to get right.
- Results land in the JUnit-style XML (`grep -o 'passed="[0-9]*"\|failed="[0-9]*"'`); compile errors appear only in the `-logFile` (`grep "error CS"`).
- Interactively, the same suites run from **Window ▸ General ▸ Test Runner**.

### Source generators (separate .NET solution)

The Roslyn analyzers/generators under `Packages/com.velvet.core/Generators~/` are a standalone dotnet solution (not part of the Unity build), pinned by `Generators~/global.json`. Their compiled DLLs in `Runtime/Plugins/` are **committed**.

```bash
cd Packages/com.velvet.core/Generators~
dotnet test Velvet.SourceGenerators.sln -c Release   # generator unit tests (no Unity license needed)
```

A generator change is complete only once the redeployed DLLs are committed with it; `Generators~/README.md` owns the build and deploy steps.

CI is split by what a change can affect: `.github/workflows/generators.yml` runs `source-generators` (no license) for `Generators~/**` changes and for `Runtime/**` changes (its drift guards re-derive the hook surface and the generator test stub's signatures from the runtime sources, so a rename there must run the generator suite), and `.github/workflows/test.yml` runs `unity-tests` (EditMode/PlayMode, **skipped unless a `UNITY_LICENSE` secret is set** — see CONTRIBUTING.md) only for package/project changes; docs and markdown trigger neither. Docs (`docs/`) are DocFX-generated from XML comments via `docs/build.sh`.

## Architecture (the parts that span many files)

The render pipeline, in dependency order under `Runtime/`:

1. **`Component/`** — the `V.*` factories build an immutable **`VNode`** tree (`VNodeTypes.cs`); `V.Mount` attaches it. `V.Component(Foo.Render)` wraps the `[Component] static VNode Render()` declared on a static class `Foo`. `V.List` / `V.When` / `V.Fragment` / `V.Provider` are the JSX-construct equivalents. `VNodePool.cs` pools the poolable primitives.
2. **`Reconciler/`** — diffs the new VNode tree against the live fiber tree and patches the underlying `VisualElement`s. Key seams: `FiberRenderer` (mount/flush/dispose), `ChildReconciler` (keyed/positional diff + `FlattenAndFilter`, which drops `null` children — so `cond ? node : null` is the idiomatic "render nothing"), `FiberBatchScheduler` (lane-based, coalesced frame-boundary drains), `FiberNodeFactory`/`FiberNodePatcher` (create/patch + attach styling manipulators), `FiberElementCleaner` + `FiberPrimitiveElementPool`/`FiberElementPoolReset` (resource cleanup + reset-before-pool — a recurring bug class is state ghosting across pool reuse, so a reset helper must scrub **every** field a node may have set).
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
- `DrainImmediateForTest()` (on `FiberBatchScheduler` via `mounted.Root.Reconciler.Context.BatchScheduler`) and `FlushEffectsForTest()` / `FlushStateForTest()` — the EditMode scheduler/PlayerLoop does not tick, so flush manually.
- EditMode batchmode drives neither layout, the panel scheduler, nor animations. `EditorPanelTestHelpers` reaches each of them by reflection — a styles/layout pass so `resolvedStyle` populates, one scheduler tick, one animation tick, and a substitute panel clock — and `PanelTestBase` packages the host window, the headless guard and that layout pass for a fixture that needs a real panel. Read `TestUtilities/` before hand-rolling any of it.

**Test convention for this repo:** Given/When/Then naming (`Given_..._When_..._Then_...`) for method names, with `// Arrange`/`// Act`/`// Assert` sections in the body, **exactly one assert per test**, and `Assume.That` for preconditions. Verify a regression test is RED without the fix and GREEN with it. Test fixtures are `internal sealed class` (the Unity Test Framework discovers internal fixtures; bases are `internal`/`public abstract`). Comments must not carry issue/PR numbers — state the reason in terms of behavior so it is self-contained. Templates: `ButtonChildPoolReuseTests.cs`, `ClickDrivenHookLifecycleTests.cs`.

## Conventions

- Commits use Conventional Commits with the `velvet` scope (e.g. `fix(velvet): …`, `feat(velvet): …`, `refactor(velvet): …`).
- Everything in this repo is written in English: code, comments, commit messages, and PR titles/bodies. PR descriptions state what changed and why — never the local workflow that produced the change (audit/review process, agent tooling, session details).
- A PR that adds, changes, or removes a feature updates the corresponding `Documentation~` guide (and the `Documentation~/README.md` index table) in the same PR. If the change has no doc impact, say so explicitly in the PR body. `DocumentationDriftTests` (EditMode) and the Generators~ diagnostic-table tests catch API references and diagnostic IDs that no longer exist, but they cannot verify that a behavior description is still accurate — that stays the PR author's responsibility.
- Documentation is single-source-of-truth: a given fact (the hooks table, the factory list, the diagnostic-ID table, a feature's behavior description) lives in exactly one owning document. Other documents link to it and add at most a one-line summary instead of restating it. Duplicated statements are how docs drift — when the fact changes, only one copy gets updated. This holds for comments too: name a sibling's mechanism ("same ownership rule as `StyleGapManipulator`"), never re-explain it.

## Comment length

A comment states why, never what — and states it once. Length is not capped, but every sentence must survive the **deletion test**: delete it; if a competent reader of the surrounding code plus the remaining sentences would still get it right, the sentence was carrying nothing. Delete it for real.

Four things reliably fail the test and should not be written:

- a restatement of the declaration below the comment — its name, signature, or next lines;
- a consequence that follows from a constraint already stated above it;
- an argument that a non-problem is not a problem;
- a sibling file's mechanism re-explained instead of named.

Four things pass, and stay however long they need to be:

- engine behavior that had to be measured, decompiled, or read out of Unity's source;
- an ordering constraint between passes, writes, or events;
- an invariant a future edit could silently break;
- a rejected alternative and the one reason it was rejected.

Two checks that need no judgement:

- A comment's first sentence may not open by restating the identifier it sits above. `// Resolves the direction:` over `ResolveDirection()` is dead text — open with the constraint.
- A block over 12 lines is a review trigger, not an error: the PR states what its load-bearing facts are. Long is fine; unexamined-long is not.

Prose in `Documentation~` is held to the same test. A guide for a polyfill tends to justify the polyfill at length; state what the behavior is and where it deviates, not why the deviation is defensible.