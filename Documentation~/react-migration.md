# Velvet Migration Guide for React Developers

This guide maps the Velvet framework's API to React, since Velvet adopts much of React's design philosophy.  
It also explicitly documents the intentional differences imposed by C# language constraints and the Unity environment, so read it through to the end to avoid the trap of "same name, different behavior."

---

## Table of Contents

1. [Hooks Mapping](#1-hooks-mapping)
2. [DSL Mapping — JSX → V.*](#2-dsl-mapping--jsx--v)
3. [Lifecycle Mapping](#3-lifecycle-mapping)
4. [Styling Mapping](#4-styling-mapping)
4b. [Tooling Mapping](#4b-tooling-mapping)
5. [Common Rewrite Examples](#5-common-rewrite-examples)
6. [Known Differences](#6-known-differences)

---

## 1. Hooks Mapping

### 1-1. Naming Differences (C# Language Constraints)

In C#, methods conventionally use PascalCase, so the names always differ from React's camelCase hook names. The semantics are, in principle, a 1:1 match with React.

| React | Velvet | Semantic Difference |
|-------|--------|----------------|
| `useEffect(fn, deps)` | `Hooks.UseEffect(fn, deps)` | Nearly equivalent. Runs asynchronously after paint, at the next frame boundary. 2-pass cleanup → effect ordering |
| `useLayoutEffect(fn, deps)` | `Hooks.UseLayoutEffect(fn, deps)` | Nearly equivalent. Runs synchronously immediately after reconcile. For DOM measurement and layout adjustment |
| `useCallback(fn, deps)` | `Hooks.UseCallback<T>(fn, deps)` | Nearly equivalent. Type inference can fail, so specify the generic type argument explicitly |
| `useContext(Context)` | `Hooks.UseContext(context)` | React parity. Propagates Provider value changes live (a masked consumer — shadowed by an inner Provider — is still re-rendered, but it live-reads the same value, so the reconciler diffs it to a no-op) |
| `useTransition()` | `Hooks.UseTransition()` | React parity. Returns `(isPending, startTransition)` in the same order as React's `[isPending, startTransition]` |
| `useRef()` | Inside a component: `Hooks.UseRef<T>()` / outside a component (e.g. orchestrator): `new Ref<T>()` | For parent→child ref forwarding, pass the orchestrator-side `new Ref<T>()` to the `V.Component<TRef>(body, componentRef, key)` overload |
| Service Locator hooks such as R3F `useThree()` / react-redux `useStore()` | `Hooks.UseService<T>()` | Obtains a cross-cutting service from the DI container. See 1-2 below for details |

> **Note — Choosing between `UseEffect` and `UseLayoutEffect`**  
> Same as React:
> - `UseLayoutEffect` runs **synchronously before paint**. Use it only for DOM size measurement and layout adjustment
> - `UseEffect` runs **asynchronously after paint** (at the next frame boundary). Network requests, subscriptions, and heavy work belong here
> Note that writing heavy work in `UseLayoutEffect` blocks the frame

> **Note — `UseContext` live propagation (React parity)**  
> Like React, Velvet's `UseContext` automatically re-renders consumers when the Provider's value changes.  
> In a sub-tree under a masking inner Provider, the masked consumer is still re-scheduled and re-rendered, but it live-reads the same value from the context cursor, so its output is identical and the reconciler collapses it to a no-op. Velvet does not detect masking (that would be a pure optimization); the observable behavior matches React, which likewise re-renders consumers across `React.memo`.  
> For shared state that needs many subscribers, `Store<T>` + `UseStore(store, selector)` remains more efficient thanks to selective re-render.

### 1-2. Obtaining a Service via DI — `Hooks.UseService<T>()`

The Velvet core is independent of any DI framework, and receives an **`IHookServiceResolver`** abstraction (a minimal interface with a single method) via a Provider to bridge to the host DI container (e.g. VContainer). The canonical hook for obtaining short-lived services (UseCase / Factory / Logger, etc.) directly from a functional component is `Hooks.UseService<T>()`.

```csharp
// For example, services your host container registers:
[Component]
public static VNode UserCard()
{
    var profiles = Hooks.UseService<IProfileUseCase>();
    var logger   = Hooks.UseService<ILogger>();
    // ... use profiles / logger directly
    return V.Div(/* ... */);
}
```

**Mapping to React community canonicals**:

| Use case | React canonical | Velvet |
|------|----------------|--------|
| Direct access to renderer / scene | R3F `useThree()` | `Hooks.UseService<IFoo>()` |
| Obtaining a store via a store provider | react-redux `useStore()` | share the `Store` through a `V.Provider` context and read it with `UseContext` |
| Arbitrary DI container service | (no standard in the React community) | `Hooks.UseService<IFoo>()` |

**Rationale and rules**:

- The host bridge is one small class on the app side — an `IHookServiceResolver` implementation
  wrapping your container (VContainer, Zenject, a hand-rolled locator, …); the Velvet core never
  references any DI framework type
- At the root `V.Mount`, you must wire `V.Provider(HookServiceContext.Ref, value: serviceResolver, children: ...)`
- For **page-scoped view state** (a store, theme, navigation context, …), prefer a typed context
  published with `V.Provider` and read with `UseContext` over resolving services ad hoc
  (separation of responsibilities)
- `UseService<T>()` cannot be called **from within a Store's async lifecycle methods** (hook discipline). A Store obtains its UseCase via constructor injection

### 1-3. State Management (React + Zustand)

Velvet state is organized into two layers — **"component-local state" and "shared Store"** — each modeled on React and Zustand respectively.

#### Local State (React hooks)

| React | Velvet |
|-------|--------|
| `const [v, setV] = useState(initial)` | `var (v, setV) = Hooks.UseState(initial)` (2-tuple: value + `StateUpdater`; call `setV.Invoke(next)` or `setV.Invoke(prev => next)`) |
| `const [s, dispatch] = useReducer(reducer, initial)` | `var (s, dispatch) = Hooks.UseReducer(reducer, initial)` |

- `Hooks.UseState` / `Hooks.UseReducer` use a positional slot scheme. They must be called in **the same order every time** within the same component function (same as React's Rules of Hooks)
- Order violations throw an `InvalidOperationException` at runtime for fail-fast detection
- `setValue` / `dispatch` are stable references tied to a slot and remain the same reference across re-renders (no additional memoization equivalent to `useCallback` is needed)

#### Shared State (Zustand-inspired Store)

Velvet's `Store<TState>` is an Atomic Store based on R3 `BehaviorSubject`, corresponding to Zustand's `create()`.

| Zustand | Velvet |
|---------|--------|
| `create<T>((set, get) => ({ ... }))` | `sealed class MyStore : Store<TState> { /* logic */ }` |
| `useStore(myStore, s => s.field)` | `Hooks.UseStore(store, s => s.field)` (inside a component function; obtain `store` via `UseContext`) |
| `useStore(myStore, s => s.field, shallow)` | `Hooks.UseStore(store, s => s.field, customComparer)` |
| `set(newState)` | `SetState(s => s with { ... })` (protected, inside the Store) |
| `get()` | `store.Current` / `store.StateChanges` (R3 Observable) |

The Store is registered in DI via VContainer and distributed with `V.Provider` at the page root / orchestrator. Components receive it via `Hooks.UseContext` and subscribe to a selector via `Hooks.UseStore` (equivalent to React's `useContext` + `useStore`):

```csharp
public static readonly ComponentContext<CounterStore> CounterStoreContext = ComponentContext<CounterStore>.Create();

[Component]
private static VNode CounterComponentRender()
{
    var store = Hooks.UseContext(CounterStoreContext);
    var count = Hooks.UseStore(store, s => s.Count);
    return V.Label(text: count.ToString());
}

// Provider site (e.g., page root)
V.Provider(CounterStoreContext, _counterStore,
    children: new[] { V.Component(CounterComponentRender, key: "counter") })
```

If the selector's return value equals the previous one, no re-render occurs (the default is `EqualityComparer<T>.Default`; override it via the third argument).

---

## 2. DSL Mapping — JSX → V.*

Since C# has no JSX syntax, Velvet builds the VNode tree through `V.*` method calls.

### 2-1. Basic Elements

| React (JSX) | Velvet | Notes |
|-------------|--------|------|
| `<div className="x">` | `V.Div(className: "x")` | Unity has no HTML elements. Produces a `VisualElement` |
| `<span>` | `V.Div()` | No span-equivalent element. Substitute a generic `VisualElement` |
| `<button onClick={fn}>` | `V.Button(onClick: fn)` | Produces a UI Toolkit `Button` type |
| `<input type="text">` | `V.TextField()` | |
| `<input type="checkbox">` | `V.Toggle()` | |
| `<input type="range">` | `V.Slider()` | |
| `<p>` / `<h1>` | `V.Label()` | UI Toolkit `Label` type |
| `<>{children}</>` | `V.Fragment(children)` | No shorthand `<>` syntax |

### 2-2. Conditionals and Lists

| React | Velvet | Notes |
|-------|--------|------|
| `{cond && <X/>}` | `V.When(cond, () => V.X())` | There is no JS truthy evaluation, so use an explicit factory function |
| `items.map(x => <X key={k}/>)` | `V.List(items, keySelector, renderer)` | A dedicated API that enforces `key` |

### 2-3. Components

| React | Velvet | Notes |
|-------|--------|------|
| `<MyComponent/>` | `V.Component(MyRender, key: "...")` | `MyRender` is a static method annotated with `[Component]`. Stores are distributed via `V.Provider` + `UseContext` |
| `React.memo(Component)` | `[Component(Memoize = true)]` | An opt-in attribute that shallow-compares props at the reconcile boundary with `Object.is`, and bails out of parent re-render if they are equal |
| React Compiler (automatic memoization) | no annotation (all `[Component]`) | The ILPP `CompilerWeaver` weaves inner automatic memoization with default-on. Opt out with `[Component(Compiler = false)]` |
| `useMemo(value, deps)` | `Hooks.UseMemo(() => value, deps)` | Value-memoization hook; recomputes only when a dep changes (use inside render) |
| `useMemo(() => <X/>, deps)` | `Hooks.UseMemo(() => V.X(), deps)` or `V.Memoized(() => V.X(), deps)` | The hook returns a memoized VNode; `V.Memoized` is a node-level escape hatch usable outside render (e.g. expanded by `[Memoize]`), diff-skipping the subtree |
| `useCallback(fn, deps)` | `Hooks.UseCallback(fn, deps)` | Returns a stable delegate while deps are unchanged |

> **Note — Two memoization axes**  
> `[Component(Memoize = true)]` is equivalent to **React.memo**, bailing out of parent-driven re-render when props are shallow-equal to the previous ones (opt-in).  
> **Inner automatic memoization** (equivalent to React Compiler) is **default-on** for all `[Component]`; the ILPP caches VNode construction keyed on hook-derived inputs. No annotation needed. To exclude a specific Component, use `[Component(Compiler = false)]` (equivalent to React's `"use no memo"`).  
> `Hooks.UseMemo(factory, deps)` is the value-memoization hook (React's `useMemo`). `V.Memoized(factory, deps)` is a node-level escape hatch that explicitly memoizes a **VNode subtree** (callable outside a render, e.g. what `[Memoize]` expands to); the reconciler reuses the cached subtree while the deps are unchanged.

### 2-4. Context

| React | Velvet | Notes |
|-------|--------|------|
| `<ThemeContext.Provider value={v}>` | `V.Provider(ThemeContext, v, children)` | A functional form that takes the Context as its first argument |
| `useContext(ThemeContext)` | `Hooks.UseContext(ThemeContext)` | Callable only inside a component function. Propagates Provider value changes live (React parity) |

### 2-5. Suspense / Error Boundary

| React | Velvet | Notes |
|-------|--------|------|
| `<Suspense fallback={<Spinner/>}>` | `V.Suspense(fallback, children)` | Equivalent |
| Class Component + `getDerivedStateFromError` | The `V.ErrorBoundary(fallback, children)` helper, or `[Component(IsErrorBoundary = true)]` + `Hooks.UseFallback(fn)` | Explicit opt-in. The helper suits a use directly under Mount; the functional pattern suits cases where you want fallback/children values to update dynamically on parent re-render |
| Class Component + `componentDidCatch` | `Hooks.UseEffect` + try-catch, or logging via an error-notification Store | When you want to log side effects from a functional component, do it inside an effect |

> **Note — Error Boundary mapping**  
> Velvet uses the same explicit opt-in model as React. The `V.ErrorBoundary(fallback, children)` helper is ideal for a root boundary directly under mount. For cases where the fallback / children values change dynamically, use a static method annotated with `[Component(IsErrorBoundary = true)]` combined with `Hooks.UseFallback(ex => ...)`.<br/>
> Velvet's `UseFallback` is equivalent to React's `getDerivedStateFromError` (a pure function that simply returns a fallback VNode). Handle side effects separately inside a `UseEffect`.

---

## 3. Lifecycle Mapping

Like React's functional components, Velvet adopts a hook-based lifecycle model (there are no class-component lifecycle methods).

| React | Velvet | Notes |
|-------|--------|------|
| `componentDidMount` | `Hooks.UseEffect(fn, Array.Empty<object>())` | Empty deps means it runs once on mount |
| `componentWillUnmount` | The return value of `Hooks.UseEffect` (a cleanup delegate) | Return the cleanup from the same hook. Equivalent to React |
| `componentDidCatch(error, info)` | Catch descendant render errors via the `fallback` callback of `V.ErrorBoundary`, or via `Hooks.UseFallback` inside a boundary component (`[Component(IsErrorBoundary = true)]`) | The intended semantics is catching descendant render exceptions. Log side effects in the boundary's own `Hooks.UseEffect` |
| `getDerivedStateFromError(error)` | `V.ErrorBoundary(fallback, children)` or `[Component(IsErrorBoundary = true)]` + `Hooks.UseFallback` | For a root boundary directly under mount, the helper is more concise |

---

## 4. Styling Mapping

Velvet inherits the styling philosophy of **React + Tailwind CSS**.  
It excludes hand-written UXML/USS from its design and expresses all styles with utility-first classes.

### 4-1. Applying Classes

| React + Tailwind | Velvet | Notes |
|-----------------|--------|------|
| `className="p-4 bg-blue-500"` | `className: "p-4 bg-primary"` | Same utility-first approach. Design values are managed via USS token variables |
| `clsx(...)` / `classnames(...)` | `StyleClassNames.Class(...)` | Conditional class composition |
| `class-variance-authority` (cva) | `StyleRecipe` | variants + compoundVariants |
| cva's slot support | `StyleSlotRecipe` | Variant management for multiple slots |
| `theme.extend` in `tailwind.config.ts` | `:root` variables in `_tokens.uss` | Design token extension |
| Tailwind JIT's `w-[120px]` | Same syntax + `StyleArbitraryValueResolver` | Arbitrary values can be used as-is |
| `hover:` / `focus:` / `active:` / `checked:` state variants | Same prefixes | Driven by the element's own pointer / focus state (the payload is an ordinary utility) |
| `dark:` theme variant | Same prefix | Driven by `VelvetTheme.IsDark` |
| `sm:` / `md:` / `lg:` / `xl:` / `2xl:` responsive variants | Same prefixes | Min-width breakpoints; evaluated against the panel root by default (or a `@container` scope) |
| `group-*` / `peer-*` relational variants | Same prefixes (incl. named `group/<name>`) | React itself has no equivalent; this is Tailwind parity |
| Stacked variants (`dark:hover:`) | Same syntax, order-independent | Applies only when every gate holds |
| CSS container queries (`@container` / `container-type: inline-size`) | `@container` (apply via `VelvetResponsive.ContainerClass`) | Re-points descendants' `sm:`/`md:`/… at the marked element's width. See [styling-variants.md](styling-variants.md) |

### 4-2. Styling Conventions (Important)

A core principle of Velvet styling is to **not create new `.uss` files**.  
When you need to add USS beyond the existing `StyleUtilities.uss` / `_tokens.uss`, consider the following options in order of priority:

1. **Arbitrary value** — check whether you can express it with an arbitrary value such as `w-[120px]`
2. **A PR adding a utility to the Velvet package** — if it is general-purpose, propose adding a token/utility
3. **inline style (last resort)** — handle it with `refCallback` + `element.style.xxx = ...`

| React | Velvet | Notes |
|-------|--------|------|
| Don't create `.css` files beyond `globals.css` | **No new `.uss`** | Project convention |
| CSS Modules / styled-components | **Not adopted** | Intentionally excluded as it conflicts with the Tailwind philosophy |
| `style={{ padding: 16 }}` (inline) | `refCallback` + `element.style.xxx` | An escape hatch. Use as a last resort |

---

## 4b. Tooling Mapping

Velvet reproduces the React-ecosystem editor tooling as Unity editor windows that drive off the
live framework — see [preview-tooling.md](preview-tooling.md) for the full guide.

| React ecosystem | Velvet | Notes |
|-----------------|--------|------|
| Storybook (a "story") | Velvet Preview window — a `[VelvetPreview]` static method returning `VNode` | **Window ▸ Velvet ▸ Preview.** Live-renders without Play Mode |
| Storybook global decorators / `preview.js` | `[VelvetPreviewSetup]` | Runs once per assembly before any story mounts (fonts / store / resolver); returns `IDisposable` / `Action` / `void` |
| Storybook Controls / Args | The Controls addon + a story's single "args" object | Reflects the args type into live editor knobs and re-renders on edit |
| Storybook Viewport | The Viewport addon | Simulates responsive widths by sizing the canvas and making it a `@container` scope |
| React DevTools | Velvet DevTools | **Window ▸ Velvet ▸ DevTools Inspector.** VNode-tree inspector + state-history time travel |
| Visual-regression snapshots (e.g. Chromatic / Storybook test-runner) | Registry-driven screenshot capture | Reuses the same `[VelvetPreview]` registry to render each story off-screen to a PNG |

---

## 5. Common Rewrite Examples

### 5-1. Counter (local state)

```csharp
// Velvet — equivalent to React's useState
[Component]
private static VNode CounterRender()
{
    var (count, setCount) = Hooks.UseState(0);
    return V.Div(
        className: "flex-col gap-2",
        children: new[]
        {
            V.Label(text: $"Count: {count}"),
            V.Button(
                text: "+1",
                onClick: () => setCount.Invoke(count + 1)),
        });
}

// Parent side
V.Component(CounterRender, key: "counter")
```

```tsx
// React equivalent
function Counter() {
  const [count, setCount] = useState(0);
  return (
    <div className="flex-col gap-2">
      <p>Count: {count}</p>
      <button onClick={() => setCount(c => c + 1)}>+1</button>
    </div>
  );
}
```

### 5-2. Shared state — Store + UseStore (Zustand style)

Move state shared across pages / components into a Store. Components subscribe to a selector via `UseStore`.

```csharp
// Store definition (Application layer)
public sealed record SettingsState(float Volume, bool MuteOnBackground);

public sealed class SettingsStore : Store<SettingsState>
{
    [Inject]
    public SettingsStore() : base(new SettingsState(1.0f, false)) { }

    public void SetVolume(float v)
        => SetState(s => s with { Volume = v });
}

// Component (Presentation layer)
public static readonly ComponentContext<SettingsStore> SettingsStoreContext = ComponentContext<SettingsStore>.Create();

[Component]
private static VNode VolumeSliderRender()
{
    var store = Hooks.UseContext(SettingsStoreContext);
    // Re-render only when Volume changes
    var volume = Hooks.UseStore(store, s => s.Volume);
    return V.Slider(value: volume, onChange: store.SetVolume);
}

// Provider site (once at the page root, etc.)
V.Provider(SettingsStoreContext, _settingsStore,
    children: new[] { V.Component(VolumeSliderRender, key: "slider") })
```

```tsx
// Zustand equivalent
const useSettings = create<SettingsState>(set => ({
  volume: 1.0, muteOnBackground: false,
  setVolume: (v) => set({ volume: v })
}));

function VolumeSlider() {
  const volume = useSettings(s => s.volume);
  const setVolume = useSettings(s => s.setVolume);
  return <input type="range" value={volume} onChange={e => setVolume(+e.target.value)} />;
}
```

### 5-3. List rendering with key

```csharp
// Velvet
V.List(items, item => item.Id, item =>
    V.Label(text: item.Name, key: item.Id))
```

```tsx
// React equivalent
items.map(item => <span key={item.id}>{item.name}</span>)
```

### 5-4. Context Provider + Consumer

```csharp
// Provider side
V.Provider(ThemeContext, currentTheme,
    children: new[] { V.Component(ChildRender, key: "child") })

// Consumer side (inside a static method annotated with [Component])
var theme = Hooks.UseContext(ThemeContext); // propagates Provider value changes live (React parity)
```

```tsx
// React equivalent
<ThemeContext.Provider value={theme}>
  <ChildComponent />
</ThemeContext.Provider>

// Consumer
const theme = useContext(ThemeContext); // live subscribe
```

> **React parity**: Velvet's `UseContext` also propagates Provider value changes live. A masked consumer in a masking subtree is still re-rendered, but it live-reads the same value, so the reconciler collapses it to a no-op — matching React (which re-renders consumers across `React.memo`). Velvet does not optimize masking away.

### 5-5. Suspense + loading state

```csharp
// Velvet
V.Suspense(
    children: new[] { V.Component(DataViewRender, key: "data") },
    fallback: V.Label(text: "Loading..."))
```

```tsx
// React equivalent
<Suspense fallback={<p>Loading...</p>}>
  <DataView />
</Suspense>
```

### 5-6. Error Boundary

```csharp
// Velvet — V.ErrorBoundary helper (for a root boundary directly under mount)
V.ErrorBoundary(
    fallback: ex => V.Label(text: $"An error occurred: {ex.Message}"),
    children: new[] { V.Component(SafeViewRender, key: "safe") },
    key: "boundary")

[Component]
private static VNode SafeViewRender()
{
    // ... business logic ...
    return V.Label(text: "ok");
}

// For cases where you want to vary fallback / children dynamically, or log side effects (equivalent to componentDidCatch), use the functional boundary pattern
[Component(IsErrorBoundary = true)]
private static VNode SafeViewBoundaryRender()
{
    Hooks.UseFallback(ex => V.Label(text: $"Error: {ex.Message}"));
    // Log descendant exceptions in the boundary's own effect (e.g. observe the ex received in UseFallback via a Store)
    return V.Component(SafeViewRender, key: "child");
}
```

```tsx
// React equivalent (Class Component)
class SafeView extends React.Component {
  static getDerivedStateFromError(error) { return { hasError: true }; }
  componentDidCatch(error, info) { console.error(error); }
  render() {
    if (this.state.hasError) return <p>An error occurred</p>;
    return this.props.children;
  }
}
```

### 5-7. UseEffect + cleanup

```csharp
// Velvet — call as a positional hook inside a static method annotated with [Component]
[Component]
private static VNode SubscribingRender()
{
    var (value, setValue) = Hooks.UseState(0);

    Hooks.UseEffect(() =>
    {
        var sub = someObservable.Subscribe(v => setValue.Invoke(v));
        return () => sub.Dispose();
    }, Array.Empty<object>()); // empty deps = once on mount

    return V.Label(text: value.ToString());
}
```

```tsx
// React equivalent
function Component() {
  const [value, setValue] = useState(0);
  useEffect(() => {
    const sub = someObservable.subscribe(v => setValue(v));
    return () => sub.unsubscribe();
  }, []);
  return <p>{value}</p>;
}
```

### 5-8. UseCallback

```csharp
// Velvet — call inside a static method annotated with [Component]
[Component]
private static VNode ClickableRender()
{
    var (clicked, setClicked) = Hooks.UseState(false);
    var handleClick = Hooks.UseCallback<Action>(() => setClicked.Invoke(true), clicked);
    return V.Button(onClick: handleClick);
}
```

```tsx
// React equivalent
const handleClick = useCallback(() => setState(s => ({ ...s, clicked: true })), [someValue]);
```

### 5-9. className + StyleRecipe variants

```csharp
// Velvet — define variants with StyleRecipe
private static readonly StyleRecipe ButtonRecipe = new StyleRecipe(
    baseClass: "btn rounded-full font-bold",
    variants: new Dictionary<string, Dictionary<string, string>>
    {
        ["intent"] = new() { ["primary"] = "bg-primary text-white", ["ghost"] = "bg-transparent" },
        ["size"]   = new() { ["sm"] = "h-8 text-sm", ["lg"] = "h-12 text-lg" },
    });

// Inside Render
V.Button(
    className: ButtonCva.Apply(("intent", "primary"), ("size", "lg")),
    text: "Save")
```

```tsx
// React + cva equivalent
const button = cva("btn rounded-full font-bold", {
  variants: {
    intent: { primary: "bg-primary text-white", ghost: "bg-transparent" },
    size:   { sm: "h-8 text-sm",               lg:  "h-12 text-lg"    },
  },
});

<button className={button({ intent: "primary", size: "lg" })}>Save</button>
```

---

## 6. Known Differences

| Kind of difference | Summary |
|-----------|------|
| Styling convention (no new USS) | Strictly utility-first; creating new `.uss` files is prohibited |
| Component declaration style | Functional only (a static method annotated with `[Component]`). Equivalent to React's functional components; class components are not adopted |

### APIs Out of Scope for This Migration Guide (to be documented separately)

The following domains are out of scope for this migration guide and are intended to be covered in separate documents:

- Layout features such as virtual scroll / Portal (equivalent to React Window / Portal)
- Animation features (equivalent to Framer Motion)
- Routing features (equivalent to React Router)

Since concrete API names stay in sync more easily with the implementation, refer to the XmlDoc / IntelliSense inside the Velvet package.

---

## Appendix: Policy on Aligning with React Behavior

Velvet's first principle is to **reproduce React's semantics as faithfully as possible**. Hook behavior, Context propagation, Suspense, and Error Boundary aim to match React down to their semantics, and discovered differences are resolved over time.

Differences that are intentionally retained come only from those that are **clearly improvable** or from **environment constraints**, such as:

- Component declaration style: functional only (consistent with the React 16.8+ Hooks direction; class components are not introduced)
- C# language constraints: PascalCase naming / no JSX / `params` limitations, etc.
- Improvement differences aimed at type safety / reduced GC allocation: requiring `keySelector` for `V.List`, the lazy thunk in `V.When`, etc.

"Same name, different behavior" is a source of accidents, and Velvet treats it as **a trap to be avoided**.
