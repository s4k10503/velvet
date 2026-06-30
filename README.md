# Velvet

[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![CI](https://github.com/s4k10503/velvet/actions/workflows/test.yml/badge.svg)](https://github.com/s4k10503/velvet/actions)
[![Releases](https://img.shields.io/github/release/s4k10503/velvet.svg)](https://github.com/s4k10503/velvet/releases)

**A React-style declarative UI framework for Unity UI Toolkit.**

Velvet brings React's authoring model to Unity UI Toolkit. You describe UI as the pure-function
output of state; a Virtual DOM and reconciler diff that description and apply only the changes to
the underlying `VisualElement` tree. Hooks, a Zustand-style store, utility-first styling, and
Source Generator-driven memoization round out the experience — all from C#, with no UXML or USS
authoring required.

Velvet's guiding principle is **"reproduce React's semantics as faithfully as possible,"**
deviating only where a C# / Unity constraint makes a deviation a clear improvement.

### Why Velvet — who it's for

If building Unity UI by imperatively wiring up `VisualElement`s feels like fighting state/UI
desync bugs, Velvet is for you:

- **Web / React developers**: write Unity UI with near-zero learning cost — your React mental model
  (components, hooks, props, context, a Zustand-style store) transfers directly.
- **Anyone tired of state/UI desync**: "UI is a pure function of state" structurally removes a whole
  *class* of bugs — you describe the target UI for a given state and the reconciler makes the tree
  match, instead of hand-patching elements on every change.
- **Anyone burned by CSS rot**: utility-first styling means no USS to author — no class-name,
  scoping, or specificity-cascade problems to manage.

## Table of contents

- [Requirements](#requirements)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Core concepts](#core-concepts)
  - [Declarative UI](#declarative-ui)
  - [Hooks](#hooks)
  - [Store (Zustand-style)](#store-zustand-style)
  - [Utility-first styling](#utility-first-styling)
  - [Animation (Framer Motion)](#animation-framer-motion)
  - [Source Generator memoization](#source-generator-memoization)
- [Developer tooling](#developer-tooling)
- [JSX → V.\*](#jsx--v)
- [Design philosophy](#design-philosophy)
- [Documentation](#documentation)
- [Repository layout](#repository-layout)
- [License](#license)

## Requirements

- **Unity 6000.3 (Unity 6.3 LTS) or newer.** Developed and validated on **Unity 6000.3.11f1**.
  Velvet's bundled USS uses properties added in Unity 6.3 (e.g. `aspect-ratio`), so 6.3 is the floor.
- [UniTask](https://github.com/Cysharp/UniTask) (`com.cysharp.unitask`) — a **required peer dependency you install yourself** (see [Installation](#installation)).
- `com.unity.addressables` and `com.unity.nuget.mono-cecil` — resolved automatically by the Unity
  Package Manager from the package's declared dependencies.

## Installation

> Published distribution from a dedicated `upm` branch is set up via CI. Until the first release is
> tagged, install directly from the repository.

Velvet uses [UniTask](https://github.com/Cysharp/UniTask) and references it by assembly name. UniTask is
not on the Unity registry, and Velvet intentionally does **not** declare it as a package dependency — so an
existing UniTask install (UPM git URL, OpenUPM, or `.unitypackage`) is never disturbed. Velvet only needs
some UniTask present in the project.

**If you already have UniTask, just add Velvet.** Otherwise add both to `Packages/manifest.json`:

```jsonc
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.velvet.core": "https://github.com/s4k10503/velvet.git#upm"
  }
}
```

The UniTask git URL above tracks its latest release; pin a version by appending a tag (e.g.
`...UniTask#2.5.0`), and pin Velvet with `...velvet.git#v1.0.0`. Velvet's remaining dependencies
(`com.unity.addressables`, `com.unity.nuget.mono-cecil`) are on the Unity registry and resolve
automatically.

## Getting started

A counter built from a functional component and the `UseState` hook:

```csharp
using Velvet;
using static Velvet.Hooks;

public static class CounterApp
{
    [Component]
    public static VNode Render()
    {
        var (count, setCount) = UseState(0);
        return V.Div(
            className: "flex flex-col items-center gap-4 p-4",
            children: new[]
            {
                V.Label(className: "text-2xl font-bold", text: $"Count: {count}"),
                V.Button(
                    className: StyleClassNames.Class(
                        "px-4 py-2 rounded bg-primary text-white",
                        count >= 10 ? "opacity-50" : null),
                    text: "Increment",
                    onClick: () => setCount.Invoke(count + 1)),
            });
    }
}

// Mount onto any VisualElement (for example a UIDocument root).
V.Mount(rootElement, V.Component(CounterApp.Render));
```

## Core concepts

### Declarative UI

UI is described as a tree of `VNode`s built with the type-safe `V.*` factories. The tree can be
regenerated every frame; the reconciler diffs it against the previous tree and patches only the
differences, scheduling work by lane-based priority.

- **VNode construction** — type-safe trees via the `V.*` factories.
- **Functional components** — `V.Component(() => ...)` mirrors React Function Components.
- **Reconciler** — diff patching plus lane-based priority scheduling.

### Hooks

React's primary hooks, exposed in C# PascalCase:

| React | Velvet |
|-------|--------|
| `useState` | `UseState` |
| `useReducer` | `UseReducer` |
| `useEffect` (post-paint async) | `UseEffect` |
| `useLayoutEffect` (pre-paint sync) | `UseLayoutEffect` |
| `useCallback` | `UseCallback` |
| `useMemo` | `UseMemo` |
| `useContext` | `UseContext` |
| `useTransition` | `UseTransition` |
| `useDeferredValue` | `UseDeferredValue` |
| `useId` | `UseId` |
| `useRef` | `Hooks.UseRef<T>()` (in component) / `new Ref<T>()` (outside) |
| `useImperativeHandle` | `UseImperativeHandle` |

Velvet also ships hooks that cover Unity / DI / async-mutation cases the React ecosystem usually
handles with third-party libraries:

| Hook | Purpose |
|------|---------|
| `UseService<T>()` | Resolves a service via `HookServiceContext` (DI-framework neutral) |
| `UseBlocker(predicate, deps)` | Blocks navigation departures (sync + async overloads) |
| `UseMutation<TVariables, TData>(options)` | Tracks the Idle/Pending/Success/Error lifecycle of an async mutation |
| `UseStore(store, selector)` | Subscribes a component to a slice of a `Store` |

### Store (Zustand-style)

A `Store<T>` holds immutable state and notifies subscribers on change. `UseStore` binds a component
to a selected slice, re-rendering only when that slice changes:

```csharp
public sealed record CounterState(int Count);

public sealed class CounterStore : Store<CounterState>
{
    public CounterStore() : base(new CounterState(Count: 0)) { }
    public void Increment() => SetState(s => s with { Count = s.Count + 1 });
}

VNode CounterApp(CounterStore store) =>
    V.Component(() =>
    {
        var count = UseStore(store, s => s.Count);
        return V.Button(text: $"Count: {count}", onClick: store.Increment);
    });
```

### Utility-first styling

Styling is composed entirely from utility classes — no per-component USS files.

- **StyleUtilities** — utility classes shipped with the package.
- **`StyleClassNames.Class`** — conditional class composition.
- **`StyleRecipe` / `StyleSlotRecipe`** — variants and the slot API.
- **`StyleArbitraryValueResolver`** — arbitrary-value JIT syntax (`w-[120px]`, `scale-[1.4]`, `rotate-[45deg]`, etc.).
- **Variants** — Tailwind-style prefixes: state (`hover:` / `focus:` / `active:` / `checked:`), theme (`dark:`), responsive (`sm:` / `md:` / `lg:` / `xl:` / `2xl:`), relational (`group-` / `peer-`), and stacked (`dark:hover:`, order-independent). See [styling-variants.md](Packages/com.velvet.core/Documentation~/styling-variants.md).
- **Container queries** — `@container` (apply via `VelvetResponsive.ContainerClass`) marks an element a responsive root, so its descendants' `sm:` / `md:` / … evaluate against **that** element's width instead of the panel root's (the CSS `container-type: inline-size` equivalent). Binding is resolved when a descendant attaches, so toggle the marker before a subtree mounts (or re-mount to re-point it).
- **Transforms & transitions** — `scale-*` / `translate-*` / `rotate-*`, `transition-*` / `duration-*` / `ease-*`. Note: UI Toolkit 6.x cannot transition the combined `transform`, so these map onto the independent `translate` / `scale` / `rotate` properties.

### Animation (Framer Motion)

Mount / unmount and gesture animations, modeled on Framer Motion:

- **`V.Motion`** — an animated element with `StyleTransition` presets (`Fade`, `SlideUp`, `ScaleIn`, `FadeSlideUp`, …) and `whileHoverClass` / `whileTapClass` gestures.
- **`V.AnimatePresence`** — keyed enter / exit, keeping a removed child mounted until its exit finishes. **DOM-less** (React/Framer parity): it emits no wrapper, so its children flow directly into the parent's layout.
- **`V.AnimatedList`** — sugar combining `AnimatePresence` + `List` + `Motion` for animated collections, with `staggerSec`.

### Source Generator memoization

A Roslyn source generator and an IL post-processor provide static, allocation-conscious
memoization:

- `[Memoize]` — partial-method-level memoization.
- `[Component(Memoize = true)]` — whole-component caching, equivalent to `React.memo`.

## Developer tooling

Velvet reproduces the React-ecosystem editor tooling as Unity editor windows that drive off the
live framework. See [preview-tooling.md](Packages/com.velvet.core/Documentation~/preview-tooling.md)
for the full guide.

- **Preview window (Storybook)** — open via **Window ▸ Velvet ▸ Preview**. Declare a *story* with
  `[VelvetPreview]` on a static method returning `VNode` (with optional `[VelvetPreviewSetup]` for
  shared fonts / store / localization), and it live-renders onto a real panel **without entering Play
  Mode**. Addons mirror Storybook's: **Controls / Args** (live prop knobs from a story's args object),
  **Viewport** (responsive widths driven by `@container`), **Theme** (`dark:`), **Backgrounds**,
  **Zoom**, **Outline**, and **Measure**.
- **DevTools (React DevTools)** — open via **Window ▸ Velvet ▸ DevTools Inspector**. A real-time
  VNode-tree inspector with state-history time travel. It **auto-attaches**: every `V.Mount` registers
  its root, so the running app's tree shows up with no setup (manual
  `VelvetDevToolsRegistry.Register(fiber, "Label")` stays available for labelling an interior sub-tree).
- **Screenshot capture** — a registry-driven visual-regression harness renders every `[VelvetPreview]`
  story off-screen to a PNG, reusing the exact same story source as the preview window.

## JSX → V.\*

| React (JSX) | Velvet |
|-------------|--------|
| `<div>` / `<button>` / `<input>` | `V.Div(...)` / `V.Button(...)` / `V.TextField(...)` |
| `{cond && <X/>}` | `V.When(cond, () => V.X())` |
| `items.map(x => <X key={x.id}/>)` | `V.List(items, x => x.id, x => V.X(...))` |
| `<>{children}</>` | `V.Fragment(children)` |
| `<Ctx.Provider value={v}>` | `V.Provider(ctx, v, ...children)` |
| `<Suspense fallback={<X/>}>` | `V.Suspense(fallback, ...children)` |
| `<ErrorBoundary fallback={...}>` | `V.ErrorBoundary(fallback, ...children)` |

## Design philosophy

Velvet's first principle is to **match React's behaviour** — naming, hook semantics, context
propagation, Suspense, Error Boundary, and lane priority all align with React by default. "Names
match but behaviour does not" is treated as a trap to avoid; any drift is resolved toward React.

Velvet diverges only when the improvement is clearly justified:

- **C# language constraints** — PascalCase identifiers, JSX-less factory style.
- **Unity environment constraints** — `refCallback` for direct `VisualElement` access; bridging the
  UI Toolkit event model.
- **Type safety / GC reduction** — stricter constraints than React's spec where warranted, plus
  Source Generator-driven static expansion.

That third point is also where Velvet aims to go *beyond* React, not merely deviate from it:
React's memoization is a runtime mechanism, whereas Velvet pushes it to **compile time** via an
ILPP pass (the React-Compiler equivalent, `[Component(Compiler = true)]`) and Source Generators
(`[Memoize]` / `[Component(Memoize = true)]`). It spends C#'s real strengths — static type
information and code generation — on the practical axis of performance. See
[memoization.md](Packages/com.velvet.core/Documentation~/memoization.md).

**A known trade-off, stated honestly.** Reproducing React faithfully *without* JSX means the
`new VNode[] { ... }` scaffolding can take up roughly 15–30% of a file as structural noise — the
necessary friction of "React-faithful × C# constraints." Rather than hide it, the
[deep-nest mitigation guide](Packages/com.velvet.core/Documentation~/deep-nest-mitigation.md)
documents the DX patterns that keep it in check.

Velvet also intentionally **does not** introduce new UXML or USS authoring, and does not control
runtime objects (avatars, camera, physics, input) — it is dedicated to the UI layer. See the
[documentation](#documentation) for the full rationale.

## Documentation

Framework documentation ships with the package under
[`Packages/com.velvet.core/Documentation~/`](Packages/com.velvet.core/Documentation~/):

- [Documentation index](Packages/com.velvet.core/Documentation~/README.md)
- [React migration guide](Packages/com.velvet.core/Documentation~/react-migration.md) — in-depth guide for developers coming from React
- [Styling variants & container queries](Packages/com.velvet.core/Documentation~/styling-variants.md) — the variant set (state / `dark:` / responsive / `group-`·`peer-` / stacked) and `@container`
- [Preview tooling](Packages/com.velvet.core/Documentation~/preview-tooling.md) — the Storybook-equivalent preview window, its addons, and screenshot capture
- [Memoization](Packages/com.velvet.core/Documentation~/memoization.md) — `[Memoize]` and component-level caching
- [Deep-nest mitigation](Packages/com.velvet.core/Documentation~/deep-nest-mitigation.md) — DX patterns for deeply nested `V.*` trees

## Repository layout

This repository is the **development project** for Velvet — a minimal Unity project that embeds the
package so it can be developed and tested in isolation.

```
.
├── Assets/                         # Unity project shell (URP render-pipeline setup)
├── Packages/
│   └── com.velvet.core/            # ← the Velvet package (source of truth)
│       ├── Runtime/                # framework runtime (+ colocated tests)
│       ├── Editor/                 # editor-only code
│       ├── CodeGen/                # IL post-processor (ILPP)
│       ├── TestUtilities/          # shared test helpers (dev-only; not shipped — see below)
│       ├── Generators~/            # Roslyn source-generator source (built to Runtime/Plugins)
│       └── Documentation~/         # framework documentation
└── ProjectSettings/                # Unity project settings (Unity 6000.3.11f1)
```

The package is distributed from a dedicated **`upm` branch** where its contents are placed at the
repository root (package-at-root), generated automatically by CI. Consumers install from that
branch; the `main` branch you are looking at is the full development project.

The published artifact contains only what a consumer compiles: CI strips the **developer-only**
sources during the split — every `Tests/` folder, the `TestUtilities/` assembly, and `Generators~/`
(the source generators ship as prebuilt DLLs under `Runtime/Plugins`). `TestUtilities` is therefore
**dev-only by design**: it carries Velvet's own reconciler-level test scaffolding (NUnit-bound,
backed by internal reconciler types) and is not part of the consumer API surface. A consumer tests
its own app through the public API (`V.Mount`, hooks) and standard Unity Test Framework helpers; the
framework's internal test harness is not a shipped deliverable.

To develop: install Unity 6000.3.11f1, open this repository as a Unity project, and edit the
embedded package in place under `Packages/com.velvet.core/`. Run the test suites from
**Window ▸ General ▸ Test Runner**.

## Status & contributing

Velvet is a personal, single-maintainer project. Contributions are welcome on a best-effort
basis — see [CONTRIBUTING.md](CONTRIBUTING.md). If you need it to move faster than one
maintainer can keep up with, forking is encouraged (it's MIT).

## License

[MIT](LICENSE) © s4k10503
