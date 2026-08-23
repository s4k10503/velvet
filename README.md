# Velvet

[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![CI](https://github.com/s4k10503/velvet/actions/workflows/test.yml/badge.svg)](https://github.com/s4k10503/velvet/actions)
[![docs](https://img.shields.io/badge/docs-API%20reference-1f6feb.svg)](https://s4k10503.github.io/velvet/)
[![Releases](https://img.shields.io/github/release/s4k10503/velvet.svg)](https://github.com/s4k10503/velvet/releases)

**A React-style declarative UI framework for Unity UI Toolkit.**

Velvet brings React's authoring model to Unity UI Toolkit. You describe UI as the pure-function
output of state; a Virtual DOM and reconciler diff that description and apply only the changes to
the underlying `VisualElement` tree. Hooks, a Zustand-style store, utility-first styling, and
compile-time memoization round out the experience — all from C#, with no UXML or USS
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
  - [Compile-time memoization](#compile-time-memoization)
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

> Distribution is a dedicated `upm` branch (package-at-root), published automatically by CI on
> every merge to `main`. Tagged releases (`vX.Y.Z`) mark stable snapshots on that branch — pin
> one for reproducible installs, or track the branch head for the latest.

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

The UniTask git URL above tracks its latest release. Pin either by appending a tag:
`...UniTask#<tag>`, and `...velvet.git#vX.Y.Z` for a Velvet
[release](https://github.com/s4k10503/velvet/releases). Velvet's remaining dependencies
(`com.unity.addressables`, `com.unity.nuget.mono-cecil`) are on the Unity registry and resolve
automatically.

One more step before anything renders styled: attach the bundled utility stylesheet to the panel you
mount onto. Most utility classes are USS rules, and a panel without the sheet resolves every class the
sheet declares to nothing — while arbitrary values and the many families Velvet resolves itself rather
than declaring keep working, which reads as a styling bug rather than a missing sheet. See
[setup.md](Packages/com.velvet.core/Documentation~/setup.md) for the one-line call and the
scene-reference alternative.

To skip the wiring entirely, import the **Starter App** sample from the package's Samples section in
Package Manager and press Play on the scene it brings: a `UIDocument` host with that call, a router, a
store and hooks already assembled.

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

// Attach the utilities the classes above are declared in, then mount onto any VisualElement
// (for example a UIDocument root).
VelvetStyleUtilities.AttachTo(rootElement);
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

React's primary hooks, exposed in C# PascalCase. A representative sample — see
[react-migration.md §1](Packages/com.velvet.core/Documentation~/react-migration.md#1-hooks-mapping)
for the complete hook-by-hook mapping (all of `UseReducer` / `UseLayoutEffect` /
`UseInsertionEffect` / `UseMemo` / `UseTransition` / `UseDeferredValue` / `UseOptimistic` /
`UseId` / `UseImperativeHandle`, plus semantic-difference notes):

| React | Velvet |
|-------|--------|
| `useState` | `UseState` |
| `useEffect` (post-paint async) | `UseEffect` |
| `useContext` | `UseContext` |
| `useRef` | `Hooks.UseRef<T>()` (in component) / `new Ref<T>()` (outside) |

Velvet also ships hooks with no direct React-core equivalent, covering Unity / DI / routing /
async-mutation cases the React ecosystem usually hands to third-party libraries —
`UseService<T>()` (DI resolution), `UseStore` (Zustand-style store subscription, see
[Store](#store-zustand-style) below), `UseBlocker` (React-Router-style navigation blocking), and
`UseMutation` (react-query-style async-mutation lifecycle).

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

Mount / unmount and gesture animations, modeled on Framer Motion: **`V.Motion`** for an animated
element (`StyleTransition` presets, Framer-style `variants`, `whileHoverClass` / `whileTapClass`
gestures) and **`V.AnimatePresence`** for keyed enter/exit (DOM-less, with a `PopLayout` exit
mode). See [motion.md](Packages/com.velvet.core/Documentation~/motion.md) for the full guide —
variants, orchestration, springs, and the one-config-every-update transition semantics.

### Compile-time memoization

Velvet pushes memoization to compile time rather than leaving it a runtime mechanism the way React
does:

- `[MemoizeMethod]` — partial-method-level memoization, generated by a Roslyn source generator.
- `[Component(Memoize = true)]` — whole-component caching equivalent to `React.memo`; the flag is
  woven in by the same ILPP pass that drives `[Component(Compiler = true)]`'s auto-memoization, and
  checked by the reconciler at the props-bail boundary.

See [memoization.md](Packages/com.velvet.core/Documentation~/memoization.md).

## Developer tooling

Velvet reproduces the React-ecosystem editor tooling as Unity editor windows that drive off the
live framework: a **Preview window** (**Window ▸ Velvet ▸ Preview**, the Storybook equivalent,
with Controls / Viewport / Theme / Backgrounds / Zoom / Outline / Measure addons) and **DevTools**
(**Window ▸ Velvet ▸ DevTools Inspector**, the React DevTools equivalent, with state-history time
travel). See [preview-tooling.md](Packages/com.velvet.core/Documentation~/preview-tooling.md) for
the full guide, including the headless screenshot-capture pattern for visual regression.

## JSX → V.\*

A representative sample — see
[react-migration.md §2](Packages/com.velvet.core/Documentation~/react-migration.md#2-dsl-mapping--jsx--v)
for the complete DSL mapping (`Fragment`, `Provider`, `Suspense`, `ErrorBoundary`, and more):

| React (JSX) | Velvet |
|-------------|--------|
| `<div>` / `<button>` / `<input>` | `V.Div(...)` / `V.Button(...)` / `V.TextField(...)` |
| `{cond && <X/>}` | `V.When(cond, () => V.X())` |
| `items.map(x => <X key={x.id}/>)` | `V.List(items, x => x.id, x => V.X(...))` |

## Design philosophy

Velvet's first principle is to **reproduce React's semantics as faithfully as possible**,
deviating only where a C# language constraint, a Unity environment constraint, or a clear
type-safety / GC-allocation improvement justifies it — "names match but behaviour does not" is
treated as a trap to avoid, and any drift discovered is resolved toward React.
[Compile-time memoization](#compile-time-memoization) above is where this goes furthest: React's
runtime `React.memo` / Compiler mechanism becomes ILPP weaving plus a Source Generator.

See [Packages/com.velvet.core/README.md](Packages/com.velvet.core/README.md#design-philosophy) for
the full rationale — the three pillars, and what Velvet intentionally does not do (no new UXML/USS
authoring, no runtime-object control).

**A known trade-off, stated honestly.** Reproducing React faithfully *without* JSX means the
`new VNode[] { ... }` scaffolding can take up roughly 15–30% of a file as structural noise — the
necessary friction of "React-faithful × C# constraints." The practical mitigation is the same as
in React: split by component — extract each section into its own `[Component]` (or a private
`static VNode` helper) so the entry render lists sections instead of nesting them.

## Documentation

Framework documentation ships with the package under
[`Packages/com.velvet.core/Documentation~/`](Packages/com.velvet.core/Documentation~/):

- [Documentation index](Packages/com.velvet.core/Documentation~/README.md)
- [React migration guide](Packages/com.velvet.core/Documentation~/react-migration.md) — in-depth guide for developers coming from React
- [Styling variants & container queries](Packages/com.velvet.core/Documentation~/styling-variants.md) — the variant set (state / `dark:` / responsive / `group-`·`peer-` / stacked) and `@container`
- [Preview tooling](Packages/com.velvet.core/Documentation~/preview-tooling.md) — the Storybook-equivalent preview window, its addons, and screenshot capture
- [Memoization](Packages/com.velvet.core/Documentation~/memoization.md) — `[MemoizeMethod]` and component-level caching

## Repository layout

This repository is the **development project** for Velvet — a minimal Unity project that embeds the
package so it can be developed and tested in isolation.

```
.
├── Assets/                         # Unity project shell (URP setup, the starter sample + its tests)
├── Packages/
│   └── com.velvet.core/            # ← the Velvet package (source of truth)
│       ├── Runtime/                # framework runtime (+ colocated tests)
│       ├── Editor/                 # editor-only code
│       ├── CodeGen/                # IL post-processor (ILPP)
│       ├── TestUtilities/          # shared test helpers (dev-only; not shipped — see below)
│       ├── Generators~/            # Roslyn source-generator source (built to Runtime/Plugins)
│       ├── Samples~/               # importable samples (mirrors of the copies under Assets/)
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
