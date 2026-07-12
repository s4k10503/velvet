# Velvet

**A React-style declarative UI framework for Unity UI Toolkit.**

Velvet ports a React-style, utility-first styling development experience onto Unity UI Toolkit. It ships with a Virtual DOM, Reconciler, Hooks, and utility-first styling so that the UI Toolkit primitives can be driven entirely from C#.

---

## Installation

Velvet requires Unity 6000.3 (Unity 6.3 LTS) or newer (validated on Unity 6000.3.11f1) and
[UniTask](https://github.com/Cysharp/UniTask) as a **required peer dependency you install yourself**.
UniTask is not on the Unity registry, so Velvet intentionally does not declare it as a package
dependency — an existing UniTask install (any method) is left untouched.

**If you already have UniTask, just add Velvet.** Otherwise add both to `Packages/manifest.json`:

```jsonc
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.velvet.core": "https://github.com/s4k10503/velvet.git#upm"
  }
}
```

The UniTask git URL tracks its latest release. `com.unity.addressables` and `com.unity.nuget.mono-cecil`
are on the Unity registry and resolve automatically. See the repository root README for full details.

---

## Design philosophy

### Match React's behaviour; deviate only where deviation is a clear improvement

Velvet's first principle is **"reproduce React's semantics as faithfully as possible."** Naming, hook behaviour, Context propagation, Suspense, Error Boundary, Lane priority, and so on are designed to align with React's behaviour by default.

Velvet intentionally diverges from React only when **the improvement is clearly justified**, for example:

- **C# language constraints**: PascalCase identifiers, JSX-less factory style, etc.
- **Unity environment constraints**: `refCallback` for direct `VisualElement` access, bridging the UI Toolkit event model
- **Type safety / GC-allocation reductions**: stricter constraints than React's spec when warranted, plus Source Generator-driven static expansion

"Names match but behaviour does not" is treated as **a trap to avoid**: any drift discovered is resolved in the direction of React's behaviour.

### The three pillars

#### 1. Declarative UI (React)

The UI is described as the pure-function output of state. The VNode tree may be regenerated every frame; the Reconciler diffs and applies only the changes to the VisualElement tree.

- **VNode construction**: type-safe VNode trees built via the `V.*` factories
- **Functional components**: `V.Component(() => ...)` mirrors React Function Components
- **Reconciler**: diff patching plus Lane-based priority scheduling
- **Hooks**: React's primary hooks exposed in C# PascalCase (see [Documentation~/](./Documentation~/) for details)
- **Animation**: `V.Motion` / `V.AnimatePresence` model Framer Motion — variants with `initial` / `animate` / `exit` labels, standalone mount enters, `PopLayout` exits, `staggerChildren` / `delayChildren` orchestration, per-property transition overrides, and opt-in spring physics (see [Documentation~/motion.md](./Documentation~/motion.md)); `AnimatePresence` is DOM-less (it emits no wrapper, mirroring React/Framer). Lists are `V.AnimatePresence(children: V.List(items, key, (x, i) => V.Motion(...)))` — author the animated cell directly, exactly like Framer's `motion.div`
- **Source Generator memoization**: `[Memoize]` for partial-method-level memoization, `[Component(Memoize = true)]` for whole-component `React.memo`-equivalent caching

#### 2. Utility-first styling

Styling is composed entirely from **utility classes**. Per-component CSS files are not used.

- **StyleUtilities**: utility classes shipped with the package
- **`StyleClassNames.Class`**: conditional class composition
- **`StyleRecipe` / `StyleSlotRecipe`**: variants + slot API
- **`StyleArbitraryValueResolver`**: arbitrary-value JIT syntax (`w-[120px]`, etc.)

#### 3. Unity UI Toolkit (UIToolkit)

The runtime substrate is UI Toolkit itself. Velvet does not replace UI Toolkit — it is a thin abstraction that swaps the authoring layer. `VisualElement` / `Button` / `Label` / `TextField` and friends are the targets of Velvet's factory functions.

---

## Things Velvet intentionally does NOT do

### No new UXML authoring

UI structure is described **exclusively in C# VNode trees**. The UI Builder + UXML editing workflow is not adopted.

**Why:**

- JSX-equivalent constructs (conditionals, loops, variable references) collapse when expressed in UXML
- Avoids data binding + event handlers being scattered across both C# and UXML (double-bookkeeping)
- Static checking and type safety from the Source Generator take precedence

### No new USS authoring

Styling is **expressed only through the utility classes passed to `className`**. Creating new `.uss` files in the project is forbidden by convention.

**Why:**

- Aligning with utility-first philosophy ("don't write CSS") eliminates the naming, scoping, and decay problems wholesale
- Styling that utility classes cannot express is consolidated as token extensions on the package side
- Component-local CSS clashes with the utility-first philosophy and is therefore not adopted

**Priority order:**

1. Compose existing utilities (`StyleClassNames.Class` / `StyleRecipe`)
2. Arbitrary values via `StyleArbitraryValueResolver`
3. Submit a PR adding utilities to the Velvet package
4. Inline styles via `refCallback` (last resort)

### No runtime-object control

Velvet is dedicated to the UI layer. Avatar control, camera, physics, and input processing belong to separate architectures and do not flow through Velvet's VNode / Reconciler.

---

## React API quick reference

For full details see [Documentation~/react-migration.md](./Documentation~/react-migration.md). The tables below list only the most common mappings.

### Hooks

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
| `useRef` | Inside a component: `Hooks.UseRef<T>()` / Outside a component (orchestrator etc.): `new Ref<T>()` |
| `useImperativeHandle` | `UseImperativeHandle` |

#### Velvet-only hooks

Hooks without a direct React core equivalent. These cover Unity / DI / async-mutation use cases that the React ecosystem typically handles via 3rd-party libraries (react-router, react-query).

| Hook | Purpose |
|------|--------|
| `UseService<T>()` | Resolves a service via `HookServiceContext` (DI framework neutral) |
| `UseBlocker(predicate, deps)` | Blocks navigation departures — sync and async overloads (mirrors React Router) |
| `UseMutation<TVariables, TData>(options)` | Tracks the Idle/Pending/Success/Error lifecycle of an async mutation. Use `Velvet.Unit` for void variables or void return (mirrors react-query's `useMutation`) |

### JSX → V.\*

| React (JSX) | Velvet |
|-------------|--------|
| `<div>` / `<button>` / `<input>` | `V.Div(...)` / `V.Button(...)` / `V.TextField(...)` |
| `{cond && <X/>}` | `V.When(cond, () => V.X())` |
| `items.map(x => <X key={x.id}/>)` | `V.List(items, x => x.id, x => V.X(...))` |
| `<>{children}</>` | `V.Fragment(children)` |
| `<Ctx.Provider value={v}>` | `V.Provider(ctx, v, ...children)` |
| `<Suspense fallback={<X/>}>` | `V.Suspense(fallback, ...children)` |
| `<ErrorBoundary fallback={...}>` | `V.ErrorBoundary(fallback, ...children)` |

---

## Quick start

### Counter (functional component + UseState)

```csharp
using Velvet;
using static Velvet.Hooks;

VNode CounterApp() =>
    V.Component(() =>
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
    });

V.Mount(rootElement, CounterApp());
```

### Store + UseStore (Zustand-style reactive binding)

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
        return V.Button(
            text: $"Count: {count}",
            onClick: store.Increment);
    });
```

---

## Dependencies

See [package.json](./package.json) for dependent packages. Velvet is a self-contained package and does not depend on PresentationFramework.

---

## Documentation

- [Documentation~/README.md](./Documentation~/README.md) — Velvet documentation index and quick reference
- [Documentation~/react-migration.md](./Documentation~/react-migration.md) — In-depth guide for developers coming from React
- [Documentation~/memoization.md](./Documentation~/memoization.md) — `[Memoize]` and component-level caching

---

## License

[MIT](LICENSE.md) © s4k10503
