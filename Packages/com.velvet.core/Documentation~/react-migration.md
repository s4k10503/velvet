# Velvet Migration Guide for React Developers

This guide maps the Velvet framework's API to React, since Velvet adopts much of React's design philosophy.  
It also explicitly documents the intentional differences imposed by C# language constraints and the Unity environment, so read it through to the end to avoid the trap of "same name, different behavior."

---

## Table of Contents

1. [Hooks Mapping](#1-hooks-mapping) — including [what a dependency list means](#1-4-what-a-dependency-list-means)
2. [DSL Mapping — JSX → V.*](#2-dsl-mapping--jsx--v)
3. [Lifecycle Mapping](#3-lifecycle-mapping)
4. [Styling Mapping](#4-styling-mapping)
4b. [Tooling Mapping](#4b-tooling-mapping)
5. [Common Rewrite Examples](#5-common-rewrite-examples)
6. [Known Differences](#6-known-differences)

---

## 1. Hooks Mapping

§1-1 through §1-3 below cover every hook with a direct React naming/semantic relationship except
two documented alongside the concept they belong to: `Hooks.UseMemo` sits with the memoization
axes in [§2-3 Components](#2-3-components), and `Hooks.Use` / `Hooks.UseFallback` sit with
[§2-5 Suspense / Error Boundary](#2-5-suspense--error-boundary).

### 1-1. Naming Differences (C# Language Constraints)

In C#, methods conventionally use PascalCase, so the names always differ from React's camelCase hook names. The semantics are, in principle, a 1:1 match with React.

| React | Velvet | Semantic Difference |
|-------|--------|----------------|
| `useEffect(fn, deps)` | `Hooks.UseEffect(fn, deps)` | Nearly equivalent. Runs asynchronously after paint, at the next frame boundary. 2-pass cleanup → effect ordering |
| `useLayoutEffect(fn, deps)` | `Hooks.UseLayoutEffect(fn, deps)` | Nearly equivalent. Runs synchronously immediately after reconcile. For DOM measurement and layout adjustment |
| `useInsertionEffect(fn, deps)` | `Hooks.UseInsertionEffect(fn, deps)` | Nearly equivalent. Runs synchronously after render, before every `UseLayoutEffect` of the same commit. For style injection; must not read layout or refs |
| `useOptimistic(state, updateFn)` | `Hooks.UseOptimistic(state, applyOptimistic)` | Returns `(optimisticState, addOptimistic)`. Every render folds the pass-through state through the outstanding entries in the order `addOptimistic` received them, so an entry still outstanding lands on the authoritative state as it now is rather than on the state it was added over. An entry added while a `startTransition` callback runs — an `async` action's up to the point it first suspends — belongs to the innermost transition open there whose `isPending` is lit, and is discarded when that transition settles, or when no `async` action is left in flight (below): whether or not the pass-through state changed, and whether the action succeeded or faulted. An entry added where no such transition is open while an `async` action is in flight — that action's own code past a suspension included — belongs to every action in flight together. As in React, the actions in flight are entangled: a transition that settles while any is in flight hands its entries to them, so they are discarded once none is left, the last completing. An action counts from its start until its task completes, whether or not the component that started it is still mounted, as React's entangled scope waits on the promise: one awaiting a task that never completes holds every later transition's entries, its component's unmount included, until the next Play Mode entry. Before this an unmount gave the action up. The render `addOptimistic` requests is an ordinary one at the priority a setter called there would take outside a transition, so the value shows before the transition commits; an entry whose transition settles before any render showed it — a synchronous callback that queued nothing — is shown by that render and discarded by the component's next Transition-lane render. An entry nothing owns is discarded by the component's next Transition-lane render too; added outside every `startTransition` callback it logs a warning in the editor, and added in a callback whose starter's component has unmounted it does not. A Transition-lane drain that lands the last work a transition queued renders the component it drains, and any component its pass renders, without that transition's entries, so that commit already shows them gone as React's revert lane does. **Velvet deviation:** the work a transition queued commits one component at a time, so where it ends on a component that neither holds the entries nor renders the one that does, the entries are taken down by the render the settle requests, after that commit. The same holds where the work ended on a component and on a descendant its pass subsumes: the components rendered ahead of the descendant still show the entries in that commit, and the descendant leaves them out |
| `useCallback(fn, deps)` | `Hooks.UseCallback(fn, deps)` | Nearly equivalent. In a C# 9 assembly the type argument is inferred for a parameterless `Action` lambda or method group, and for a lambda with typed parameters that is a `Func` of one to three parameters or an `Action` of two or three. Every other lambda or method group takes it explicitly there, as `Hooks.UseCallback<T>(fn, deps)`: a parameterless `Func`, a one-parameter `Action`, a lambda with untyped parameters, a method group with parameters, and a lambda of any other delegate type. A value that already has a delegate type — a delegate variable, or a cast lambda — takes no type argument either: it binds the overload named for its shape where there is one, and `UseCallback<T>` otherwise. No overload takes one type argument, since beside `UseCallback<T>` it would be a candidate for every such explicit call and take some of them over |
| `useContext(Context)` | `Hooks.UseContext(context)` | React parity. Propagates Provider value changes live (a masked consumer — shadowed by an inner Provider — is still re-rendered, but it live-reads the same value, so the reconciler diffs it to a no-op) |
| `useTransition()` | `Hooks.UseTransition()` | React parity on the returned tuple: `(isPending, startTransition)` in the same order as React's `[isPending, startTransition]`. The callback marks the updates it schedules synchronously, whichever component owns the state they write — a setter received as a prop included, and a starter still held after the component that declared it unmounted — matching React's ambient transition flag. That last case marks the writes without lighting an `isPending` nobody is left to render. As in React, the marking ends where the callback hands control back — for an `async` action, the point it first suspends: it keeps `isPending` true until the task completes, but an update it makes after an `await` that suspended it falls outside the scope its callback opened, so that update takes the Normal lane unless it lands in some other scope — a discrete handler's, or a further `startTransition` call, which is what React's reference tells callers to write for them. An `await` of a `VelvetTask` suspends even where the task had already completed, as JavaScript's `await` resumes in a microtask once the code that called `startTransition` has returned: inside a discrete handler the continuation runs after the handler returns and ahead of the handler's flush, and elsewhere on the main thread's next tick, so the update after it falls outside the scope. An `await` of a completed `Task`, a ValueTask or `Awaitable` does not suspend, because C# continues inline wherever an awaiter it is handed reports completion and those awaiters are not Velvet's: the callback runs on past such an await still inside the scope, and that update is a transition, with `isPending` staying lit until it commits. Wrapping post-`await` updates in the starter makes the two paths agree, since a joined call is a transition on both. An error the callback throws, or an `async` action faults with, is not seen by the caller: it is thrown from the declaring component's next Transition-lane render to the error boundary above it, as React's `startTransition` dispatches it as the `isPending` update where the callback returns. The outcome rendered is that of the call whose callback returned last, an `async` action's included: a later call takes down an error not yet rendered, and an action that settles after a later call returned is not rendered. Once the declaring component has unmounted, the error is dropped, as React's dispatch to an unmounted component does nothing. An update from elsewhere that lands while the action awaits keeps its own priority, discrete input included, and a second `UseTransition()` slot started there is an independent transition with its own `isPending`. `isPending` follows the updates the slot's own callback scheduled, not the Transition lane: a synchronous callback that scheduled no update settles as soon as it returns, and a `UseDeferredValue` in the same component holding that lane does not keep the flag lit. Where those updates landed on other components, the flag stays lit until each of them has discharged that work — committed through its terminal reconcile slice, unmounted with no commit left to make, or had the scheduler drop it at the update-depth cap; two such components wait on each other, so what settles them is the last of those commits rather than the first. For an `async` action the flag stays lit until the task completes as well, and the declaring component re-renders once whichever of the two lands last. **Velvet deviation:** nothing renders purely because `isPending` turned true, where React re-renders the component on that alone. Whichever render some other cause already produces while the transition is open is what observes the flag here — the urgent update a click also makes, an ancestor's pass, a flush the callback itself reaches, the commit of one of two components it enrolled — and where nothing produces one, the first render the component gets is the one committing the transition with the flag still lit. The terminal commit then asks for the render that takes it down. An `async` action that suspends shows it from the commit of the work its callback queued until its task completes; one that never suspends is the synchronous case. A same-starter async call joined before an outer action completes keeps that lifecycle open until the joined call completes too |
| `useDeferredValue(value)` / `useDeferredValue(value, initialValue)` | `Hooks.UseDeferredValue<T>(value)` / `Hooks.UseDeferredValue<T>(value, initialValue)` | React parity. Defers the commit of `value` changes through the Transition lane, returning the previous committed value during an urgent re-render, while a render on the Transition lane commits and returns the value it is handed — a value built afresh on every render included; the `initialValue` overload returns it on the first render only, then immediately schedules a transition toward `value`. Change detection is `Object.is`, the same default comparer as `Hooks.UseStore`, with no comparer argument of its own |
| `useSyncExternalStore(subscribe, getSnapshot)` | `Hooks.UseSyncExternalStore<T>(subscribe, getSnapshot)` | Reads a store Velvet does not own: `subscribe` takes the change callback and returns the unsubscribe action, and `getSnapshot` must return a cached snapshot. Velvet's `Store<T>` keeps `Hooks.UseStore`. See [External stores](#external-stores) for subscription identity, the main-thread rule and lanes |
| `useRef()` | Inside a component: `Hooks.UseRef<T>()` / outside a component (e.g. orchestrator): `new Ref<T>()` | For parent→child ref forwarding, pass the orchestrator-side `new Ref<T>()` to the `V.Component<TRef>(body, componentRef, key)` overload |
| `useImperativeHandle(ref, createHandle, deps?)` | `Hooks.UseImperativeHandle<THandle>(handleRef, factory)` / `Hooks.UseImperativeHandle<THandle>(handleRef, factory, deps)` | React parity. Builds a handle via `factory` and writes it into the `Ref<THandle>` the parent forwarded through `componentRef:` (read back inside the child with `ForwardedRef<T>()`); omitting `deps` re-invokes `factory` every render, same as passing no deps array in React |
| `useId()` | `Hooks.UseId(prefix?)` | React parity. Stable ID tied to the component instance and hook-slot position — same value across re-renders, distinct across instances/slots — for label/field association or `aria-*` attributes. Format is `:r{hex}:` (or `{prefix}:r{hex}:`), the same colon-wrapped shape React emits; a `prefix` is honored only on the first render |
| Service Locator hooks such as R3F `useThree()` / react-redux `useStore()` | `Hooks.UseService<T>()` | Obtains a cross-cutting service from the DI container. See 1-2 below for details |

> **Note — Choosing between `UseEffect` and `UseLayoutEffect`**  
> Same as React:
> - `UseLayoutEffect` runs **synchronously before paint**. Use it only for DOM size measurement and layout adjustment
> - `UseEffect` runs **asynchronously after paint** (at the next frame boundary). Network requests, subscriptions, and heavy work belong here
> Note that writing heavy work in `UseLayoutEffect` blocks the frame

> **Note — When Transition-lane work renders**  
> An update `startTransition` marks and a `UseDeferredValue` derivation render with no fixed delay, as in React, and in a later panel scheduler pass than the urgent render that asked for them. What Velvet holds to is a later pass, not a later frame. A request made inside one of these callbacks, run by the scheduler of the panel the requesting tree is mounted on, renders on the next pass: Velvet's own render drains (so an urgent render one of them commits, reaching `UseDeferredValue`, and a Transition-lane render asking for another), the scheduled `UseEffect` drain, a time-sliced render's resume, the notification of a resource that resolved while its reader was rendering, and a `UseFrame` callback. A request made anywhere else renders on the pass after that: a discrete event handler, including the urgent render its synchronous flush commits (a search box's `UseDeferredValue`) and the `UseEffect`s it flushes before it starts; another panel's callbacks; Velvet's other scheduled work; and code outside every panel. Normal- and Urgent-lane work still queued when a transition drains commits first, in that same pass, and a component whose own deferred value is queued has it held back from an urgent pass that renders that component. An urgent update to a component whose deferred value is queued for the coming pass moves the deferred value to a later one; after `TransitionStarvationThreshold` (30) such moves it stays where it is and can commit in the same pass as an urgent update, so urgent work every frame cannot hold it back indefinitely.

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
- `UseService<T>()` is a hook, so React's rules of hooks apply: it runs only during a component's render, and
  a call from anywhere else — a Store's async lifecycle method included — throws, as a hook called outside
  a React render does. A Store obtains its UseCase via constructor injection

### 1-2a. Async mutations — `Hooks.UseMutation`

TanStack Query's `useMutation` equivalent. Returns a handle with `Mutate` (fire-and-forget) and `MutateAsync` (awaitable), plus `Status` / `Data` / `Error` / `Variables` snapshots and `Reset`.

| React Query | Velvet |
|-------------|--------|
| `useMutation({ mutationFn, onSuccess, onError, onSettled })` | `Hooks.UseMutation(new MutationOptions<TVariables, TData>(MutationFn: ..., OnSuccess: ..., OnError: ...) { OnSettled = ... })` |
| `useMutation({ mutationFn, onMutate, onSuccess, onError, onSettled })` | `Hooks.UseMutation(new MutationOptions<TVariables, TData, TContext>(MutationFn: ..., OnMutate: ..., OnSuccess: ..., OnError: ..., OnSettled: ...))` |
| `mutate(variables)` | `mutation.Mutate(variables)` |
| `mutateAsync(variables)` | `await mutation.MutateAsync(variables)` |
| `mutate(variables, { onSuccess, onError, onSettled })` | `mutation.Mutate(variables, new MutateOptions<TVariables, TData> { OnSuccess = ..., OnError = ..., OnSettled = ... })`; `MutateAsync` takes the same second argument |

**Lifecycle callbacks.** `OnMutate` runs when the call starts — after the handle has turned `Pending`
with the call's `Variables`, before `MutationFn` — and what it returns is the context that call's
`OnSuccess`, `OnError` and `OnSettled` receive. `OnSettled` runs on both paths, after `OnSuccess` or
`OnError`: with the data and a null exception on success, with default data and the exception on
failure. That is v5's order, and with it v5's optimistic-update recipe: snapshot and write the
optimistic value in `OnMutate`, roll it back from the context in `OnError`, finish in `OnSettled`.
Each call's context is its own, so overlapping calls never see each other's. The context-free option
records carry `OnSettled` too, as an init-only property set in an object initializer; `OnMutate` and the
context are only on `MutationOptions<TVariables, TData, TContext>`, which has no void form: a void
mutation takes `Unit` as `TData` there and returns `Unit.Default`.

```csharp
// Loadout is a class, so a context OnMutate never returned arrives as null.
var equip = Hooks.UseMutation(new MutationOptions<ItemId, Unit, Loadout>(
    MutationFn: async (item, ct) =>
    {
        await inventoryApi.EquipAsync(item, ct);    // a VelvetTask
        return Unit.Default;
    },
    OnMutate: item =>
    {
        var previous = inventory.Current.Loadout;   // snapshot
        inventory.Equip(item);                      // optimistic write through a Store action
        return previous;
    },
    OnError: (error, _, previous) =>
    {
        // An unmount cancelled the request: the server may have applied it, so reload rather than guess.
        if (error is OperationCanceledException) { inventory.Reload(); return; }
        if (previous != null) inventory.RestoreLoadout(previous);   // null when OnMutate threw
    },
    OnSettled: (_, _, _, _) => setBusy.Invoke(false)));
```

**Per-call callbacks.** `Mutate` and `MutateAsync` take a `MutateOptions<TVariables, TData>` whose
`OnSuccess`, `OnError` and `OnSettled` run after the hook options' own, once the call's outcome is on the
handle, so they read `Status` / `Data` / `Error` as the call left them. As in v5, only the call the handle
follows delivers them: a newer call, `Reset` and the component unmounting each drop them, while the hook
options' callbacks still run. A throwing per-call callback is logged and costs neither the next one nor the
outcome. Each receives the call's `OnMutate` result as an `object?`, null for the context-free option
records, since the handle's type does not carry `TContext`.

**Unmounting.** The callbacks of a call in flight still run after its component unmounts, as v5's
option callbacks outlive the observer; only the handle is no longer written and the component no longer
re-rendered. Unlike v5, which never cancels, Velvet cancels the call's `CancellationToken` on unmount. A
`MutationFn` that honours it ends in the `OperationCanceledException`, which `OnError` and then
`OnSettled` receive with the call's context, and `MutateAsync` rejects with that exception. One that
ignores it completes as usual and runs `OnSuccess` or `OnError`, then `OnSettled`. A `MutateAsync`
called after the unmount never starts and rejects with an `OperationCanceledException` too.
A cancelled request may already have reached the server and been applied, so a cancellation does not say
the write failed: rolling the optimistic value back on it can undo a write the server kept. Branch on
`error is OperationCanceledException` in `OnError`, as the sample does, and reload the authoritative state
there instead of restoring the snapshot.

`OnMutate` returns the context itself where v5 awaits a returned promise, as every callback here is
synchronous.

**Concurrent calls.** Calling `Mutate` twice starts two runs, neither cancels the other, each
delivers its own callbacks with its own context, and `Status` / `Data` / `Error` / `Variables` are one
snapshot following the newest — so a double-tapped button does not lose the first call's follow-up
write. Starting a call resets all four to that call's own — `Data` included, so a pending call never
shows the previous one's result. Not cancelling on re-entry, following the newest, and clearing
`Data` when a call starts are all v5's behaviour.

The `CancellationToken` handed to `MutationFn` has **no v5 counterpart** — a v5 `mutationFn` receives
its variables and a context of the query client, the mutation's `meta` and its `mutationKey`, with no
cancellation signal. It is cancelled when the component unmounts (see **Unmounting** above), which is what
a Unity web request wants, and it is never cancelled by a later call.

**`Reset`.** Puts the handle back to `Idle` and abandons whatever is in flight, as v5's `reset()`
detaches the observer from the mutation. The abandoned call is not cancelled: it runs to completion
and still delivers its own callbacks, but neither its result nor its failure reaches the
handle, so resetting a save while it is in flight leaves the handle idle when the save lands.

**When the outcome is committed.** After the handlers, which is when v5 dispatches it. A handler
therefore never reads its own call's outcome: `Status` / `Data` / `Error` still show whatever the
handle showed before it ran — this call pending on the ordinary path, a newer call's outcome where
that call has already settled, `Idle` for a call `Reset` abandoned. `Variables` is not an outcome —
it is written when a call starts, and neither outcome touches it — but a handler reads it under the
same rule: `Reset` clears it and a later call overwrites it, so a superseded call's handler reads the
newer call's variables and a reset one's reads none. Each outcome is written whole: `Data`
is what one call produced and `Error` is how one call failed, so `Data` stands only under
`Status == Success` and `Error` only under `Status == Error` — a pending or reset handle has neither.

**Callback error semantics** (TanStack Query v5 parity):

- A throwing **`onSuccess`** handler makes the mutation an **error**: `Status` becomes `Error`, `Error` holds the handler's exception, `onError` runs with that exception, and `MutateAsync` rethrows it to the caller. This matches React Query — the success state is not committed when the handler throws, so `Data` is left empty as well.
- A throwing **`onError`** handler does **not** change the mutation outcome (`Status` / `Error` still become the mutation's own, after the handler has returned). The handler exception is handed to `.Forget()` as a fault, which logs it with `Debug.LogException`, an `OperationCanceledException` included. `MutateAsync` still rethrows the **mutation** exception, not the handler's, and `onSettled` still runs.
- A throwing **`onMutate`** fails the call with its exception before `MutationFn` runs; `onError` and `onSettled` receive it with a default context.
- A throwing **`onSettled`** follows the path it ran on. On success it fails the call as a throwing `onSuccess` does, so `onError` runs with its exception and `onSettled` runs a second time, with it — v5's order, since both handlers sit in the block its failure path catches. On failure it is logged as a throwing `onError` is, and the outcome stays the mutation's own.

### 1-2b. Cached queries — `Hooks.UseQuery`

TanStack Query's `useQuery` over a `QueryClient`: a keyed cache shared by every component that reaches
the client, which `Hooks.Use` deliberately is not — `Hooks.Use` stays React's cache-less `use()`
([§2-5](#2-5-suspense--error-boundary)).

| TanStack Query | Velvet |
|----------------|--------|
| `new QueryClient({ defaultOptions: { queries: { staleTime, gcTime } } })` | `new QueryClient(new QueryClientOptions { StaleTime = ..., GcTime = ... })` |
| `<QueryClientProvider client={client}>` | `V.Provider(QueryClientContext.Ref, client, children: ...)` |
| `useQueryClient()` | `Hooks.UseContext(QueryClientContext.Ref)` |
| `useQuery({ queryKey: ['todos', id], queryFn })` | `Hooks.UseQuery(new QueryOptions<T>(new QueryKey("todos", id), ct => ...))` |
| `useQuery(options, queryClient)` | `Hooks.UseQuery(options, client)` |
| `staleTime` / `gcTime` on one query | `new QueryOptions<T>(...) { StaleTime = ..., GcTime = ... }` |
| `retry` / `retryDelay` as a number and a function | `new QueryOptions<T>(...) { Retry = ..., RetryDelay = ... }`, or `QueryClientOptions.Retry` / `RetryDelay` for every query |
| `structuralSharing` as a function | `new QueryOptions<T>(...) { StructuralSharing = (held, arrived) => ... }` |
| `notifyOnChangeProps: ['data']` / `'all'` | `new QueryOptions<T>(...) { NotifyOnChangeProps = QueryProperties.Data }` / `QueryProperties.All` |
| `status` / `isPending` / `isSuccess` / `isError` / `data` / `error` / `isFetching` / `isStale` / `failureCount` / `failureReason` | `Status` / `IsPending` / `IsSuccess` / `IsError` / `Data` / `Error` / `IsFetching` / `IsStale` / `FailureCount` / `FailureReason` |
| `refetch()` | `result.Refetch()` |
| `queryClient.invalidateQueries({ queryKey })` | `client.InvalidateQueries(new QueryKey(...))` |
| `queryClient.clear()` | `client.Clear()` |

**One entry per key.** Two keys are the same entry when they hold as many parts and each pair is equal
under `object.Equals`, so a key rebuilt every render names the same entry, and a string, a boxed number,
or a record or tuple of such values compares by content. A part that is a collection whose type keeps
`object`'s own `Equals` compares by what it holds: an array or a list element by element, in order, as
v5's structural hash compares an array; a dictionary by key and value whatever order its entries were
added in, as v5 sorts an object's keys before hashing it; a set by element, in any order. A collection
type that defines its own equality keeps it. A record or a tuple compares by its members' own `Equals`, so
a collection held inside one compares by reference and names a new entry every time it is rebuilt — a
fetch on every render. Give the collection a key part of its own. A collection part is enumerated on
every comparison, so pass a materialised collection. The Editor warns when a query's key changes on two
commits running while printing the same.
Components reading one key share one entry and one request: the first to mount starts it and
the rest join it. A component unmounting takes nothing from the others — the request belongs to the
entry and runs on for them. The entry keeps its result after its last reader unmounts, so navigating
back renders that result on the first render instead of the pending state.

**Freshness.** `StaleTime` defaults to zero, as in v5: a component mounting over a cached result
renders it and fetches again in the background, with `IsFetching` true and `Status` still `Success`. A
result is stale once its age reaches `StaleTime`, and `TimeSpan.MaxValue` keeps it fresh until it is
invalidated. `Data` is the last successful result and stays through a later failure and through a
refetch; a failed request marks it stale, so the next component to mount over it fetches again. Data
that turns stale by age re-renders a component that reads `IsStale`, when its age reaches `StaleTime`:
the wait is polled once a frame on the client's `Clock`, as a retry's is, and ends when the component
unmounts, the data is replaced or the client is cleared. `QueryClientOptions.Clock` replaces the client's stopwatch, for game time that pauses or a
test that advances time by hand.

**Retries.** A request that fails runs again up to `Retry` times before the failure becomes the entry's
error: three by default, as in v5, set for every query on `QueryClientOptions` or for one on its
`QueryOptions`, where zero or less retries nothing. The wait before each retry is `RetryDelay`, which is
handed the number of failures before this one and the exception; by default one second, doubled at each
failure and capped at thirty seconds, as in v5. The wait is measured on the client's `Clock`, is checked
once a frame, and lasts at least one frame. While a retry waits, the request is still in flight:
`IsFetching` stays true, `Status` and `Error` stay what they were, which is `Pending` and none for an entry
with no data, and `FailureCount` and `FailureReason` report the failures so far; a request that lands
resets both. A query function that throws before it returns a task, or a `RetryDelay` that throws,
fails the request as one returning a faulted task does. With its last reader gone a request in flight finishes but is not retried, and a
reader that mounts and joins it lets its retries go on, as in v5.

**Re-rendering.** A component re-renders when a property of the result that it has read changes, as
`useQuery` tracks them: one that reads only `Data` does not re-render when a request goes into flight or
lands the value it already held. Until the component has read any property, every change to the result
re-renders it. `NotifyOnChangeProps` replaces what was read with a list — `QueryProperties.All` re-renders
at every change to the result, `QueryProperties.None` never. A property changes when its value does; `Data` is compared by
value where it is a struct or a string and by instance where it is any other class. Clearing the client re-renders every
component reading it, whatever it has read.

**Sharing data between results.** When a request lands, `Data` keeps the instance the entry already held
if the new result is deeply equal to it, and keeps the equal parts of it if it is not, which is
`replaceEqualDeep`, v5's default `structuralSharing`. An array, a `List<T>` and a `Dictionary<TKey, TValue>`
are compared element by element and entry by entry, and a dictionary keeps its comparer; a string, a
number and a struct are equal when `Equals` says so; any other class, a record included, is kept only as
the very instance held, as v5 keeps anything but a plain array or object, so a refetch landing an equal
record as a new instance makes that instance the data. `StructuralSharing` replaces all
of that with a function of the data held, default when there is none, and the data that arrived; one that
throws fails the request.

**Changing the key** moves the component to that key's entry: it renders that entry's data, or none,
and a result the old key's request delivers afterwards is never shown as the new key's. There is no
`placeholderData` yet, so a key with nothing cached renders `Pending`.

**Invalidating after a mutation.** `InvalidateQueries` takes a filter, as v5's non-exact
`partialMatchKey` does: `new QueryKey("todos")` matches `["todos", 1]` and `["todos"]` alike. A filter part
that is an array or a list matches a part that starts with it, one that is a dictionary matches a part
holding at least the entries it names, each value matched the same way, and any other part, a set
included, must equal its counterpart whole. A matching entry a
mounted component reads is fetched again; a request already in flight for it is cancelled and started
over when the entry holds data, so a result fetched before the write does not land as current. An entry
nothing reads is only marked stale, and is fetched by the next component that mounts over it.

```csharp
var client = Hooks.UseContext(QueryClientContext.Ref)!;
var save = Hooks.UseMutation(new MutationOptions<Todo, Todo>(
    MutationFn: (todo, ct) => api.SaveAsync(todo, ct),
    OnSuccess: (_, _) => client.InvalidateQueries(new QueryKey("todos"))));
```

**Deviations from v5:**

- Garbage collection runs no timer. An entry unread for its `GcTime` (five minutes by default) reads as
  absent from then on, and is removed the next time a query subscribes to the client or
  `InvalidateQueries` runs; removing it cancels the request it still has in flight.
- A query function that returns a task that has already completed, or throws before returning one,
  settles the entry a frame later, after every subscription of the commit, so readers mounting together
  share one request; v5's result arrives a microtask later.
- Structural sharing does not enter the members of a class instance, a record included, where v5 shares
  the equal properties of a changed plain object: such an instance is kept or replaced whole, because
  Velvet does not rebuild one.
- The `CancellationToken` a query function receives is cancelled when a refetch starts that request
  over, when the entry is removed, and by `Clear`. v5 also aborts a request whose function read its
  signal once the last observer unsubscribes; here it runs on, so a component mounting again finds its
  result. The same holds for the extra cleanup and setup StrictMode runs on a mounting component's
  effects: the second subscription joins the first one's request, where v5 cancels and refetches a request
  whose function read its signal.
- `UseQuery` never suspends: with no data yet it returns `Pending`. There is no `useSuspenseQuery`.
- A key holds one data type. A query reading it as another throws `InvalidOperationException`.
- Not yet available: `retry` as a function or `true`, retries that pause while the application is
  unfocused or offline (`networkMode`), `enabled`, `select`, `placeholderData`, `refetchInterval`,
  `refetchOnWindowFocus` and `refetchOnReconnect`, and `getQueryData` / `setQueryData`.

### 1-3. State Management (React + Zustand)

Velvet state is organized into two layers — **"component-local state" and "shared Store"** — each modeled on React and Zustand respectively.

#### Local State (React hooks)

| React | Velvet |
|-------|--------|
| `const [v, setV] = useState(initial)` | `var (v, setV) = Hooks.UseState(initial)` (2-tuple: value + `StateUpdater`; call `setV.Invoke(next)` or `setV.Invoke(prev => next)`) |
| `const [s, dispatch] = useReducer(reducer, initial)` | `var (s, dispatch) = Hooks.UseReducer(reducer, initial)` |

- `Hooks.UseState` / `Hooks.UseReducer` use a positional slot scheme. They must be called in **the same order every time** within the same component function (same as React's Rules of Hooks)
- A render that calls a slot-keeping hook (`Hooks.UseState`, `Hooks.UseEffect`, `Hooks.UseMemo`, …) more or fewer times than the previous render throws an `InvalidOperationException` that names the component and continues in React's words: `Rendered more hooks than during the previous render` from the extra call, or `Rendered fewer hooks than expected` once the body returns. It is a render error, so an enclosing error boundary catches it. A hook inside a plain helper method belongs to the component calling the helper, so calling the helper on some renders only is the same violation
- The analyzers report these at edit time, as React's `rules-of-hooks` lint does. VEL102 reports a hook called in a method that is neither a component nor a custom hook named `Use` followed by an uppercase letter, and a hook in a field or property initializer. VEL101 reports any other hook that sits in a condition, a loop, a `try` or `catch` block, after an early return, or in a lambda other than a component's render body that sits inside a component or a custom hook. A hook in an `if` condition, a conditional expression's condition, or the left operand of `&&`, `||` or `??` runs before the branch is chosen and is not reported. As in React's lint, a call on a member is a hook call only where the receiver is a single name starting with an uppercase letter, as with `Hooks.UseState`, or, in C#, a qualified name that binds to a namespace or a type, as with `Velvet.Hooks.UseState`; a lambda held by a variable of a hook's name is a custom hook. A component is a `[Component]` method, or a method or lambda handed to `V.Component` or `V.Memo` as its render body in the same compilation. Renaming a helper to the custom-hook shape makes each call to it a hook call VEL101 checks. VEL103 reports a component whose declaration calls a hook being called directly as a plain method, since its hooks then run as part of the caller; mount it with `V.Component`. VEL103 reads a call that names the component by a bare name from within its declaring type, or qualified by that type's simple name; a call through `using static`, an alias, a base class or an instance is not reported, and nor is a call that is the whole render body of a lambda handed to `V.Component`. React's lint has no counterpart; React's rules say a component is used only in JSX and never called as a regular function. A test harness calling hooks from a plain method opts out with a `#pragma warning disable` naming VEL102
- `setValue` / `dispatch` are stable references tied to a slot and remain the same reference across re-renders (no additional memoization equivalent to `useCallback` is needed)

#### Shared State (Zustand-inspired Store)

Velvet's `Store<TState>` is an Atomic Store with a hand-rolled synchronous listener set (no Rx-style
operators or buffering — see `StoreStateNotifier<T>`), corresponding to Zustand's `create()`.

| Zustand | Velvet |
|---------|--------|
| `create<T>((set, get) => ({ ... }))` | `sealed class MyStore : Store<TState> { /* logic */ }` |
| `useStore(myStore, s => s.field)` | `Hooks.UseStore(store, s => s.field)` (inside a component function; obtain `store` via `UseContext`) |
| `useStore(myStore, s => s.field, shallow)` | `Hooks.UseStore(store, s => s.field, customComparer)` |
| `set(newState)` | `SetState(s => s with { ... })` (protected, inside the Store) |
| `get()` | `store.Current` (snapshot) / `store.Subscribe(...)` / `store.Select(...)` (change notifications) |

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

If the selector's return value equals the previous one, no re-render occurs. The default comparer is
`Object.is` rather than value equality, so a selector returning a fresh `record class` instance of
equal content re-renders. Passing `EqualityComparer<TSel>.Default` as the third argument is what skips
that; for a string selector, or a value-type selector other than `float`/`double` — a `record struct`
included — it changes nothing: `Object.is` already gives the same answer as that comparer for both. The
`comparer` parameter points at `StateUpdater<T>`'s remarks, which state each branch.

#### External stores

State that lives outside Velvet — a model object of the game, a service raising its own change
notifications — is read with `Hooks.UseSyncExternalStore`, without mirroring it into a `Store<T>` first:

```csharp
public sealed class Inventory
{
    private readonly List<Action> _listeners = new();
    public IReadOnlyList<Item> Items { get; private set; } = Array.Empty<Item>();

    public Action Subscribe(Action onStoreChange)
    {
        _listeners.Add(onStoreChange);
        return () => _listeners.Remove(onStoreChange);
    }

    public IReadOnlyList<Item> GetSnapshot() => Items;

    public void Add(Item item)
    {
        Items = Items.Append(item).ToArray(); // a new snapshot only when the contents change
        foreach (var listener in _listeners.ToArray()) listener();
    }
}

[Component]
private static VNode InventoryCountRender()
{
    var inventory = Hooks.UseContext(InventoryContext);
    var items = Hooks.UseSyncExternalStore(inventory.Subscribe, inventory.GetSnapshot);
    return V.Label(text: items.Count.ToString());
}
```

It follows React's `useSyncExternalStore`:

- **Snapshots are compared with `Object.is` and must be cached.** A notification re-renders the
  component when `getSnapshot` then returns a value that is not `Object.is`-equal to the one it last
  rendered, or throws (the render repeats the call, so the exception reaches the error boundary). So
  `getSnapshot` returns the same instance until the store changes, and in the Editor the hook logs an
  error the first time two consecutive reads in one render differ. A `getSnapshot` that projects a field
  (`() => model.Current.Count`) re-renders when that field changes, not when another one does. A render
  that returns another snapshot than the previous one, or that subscribes, checks the snapshot again
  once it commits, so a `getSnapshot` that builds a new snapshot on every read
  re-renders the component after every render until the scheduler's update-depth limit drops the update
  and logs an error.
- **`subscribe` is called on mount and whenever its identity changes.** The hook compares it with the
  previous render's using delegate equality, so a method group on the same instance
  (`inventory.Subscribe`) keeps one subscription across renders, while a lambda capturing a local is a
  new closure every render and re-subscribes every render, as an inline subscribe function does in
  React; the StrictMode diagnostic render does not subscribe. Switching to another store's `subscribe`
  removes the previous subscription first. Unmounting removes it; an unsubscribe that throws there is
  handled as a throwing effect cleanup is, and the component's other subscriptions are still removed. A change the store raises while
  `subscribe` runs is rendered by the render that subscribed.
- **The change callback must be invoked on the Unity main thread.** Invoked from another thread it
  throws `InvalidOperationException` back to the invoker and schedules nothing; marshal the
  notification to the main thread first. The re-render it schedules takes the Urgent lane and never the
  Transition lane, including when the store is mutated inside `startTransition`. The scheduler flushes
  it from the main thread's posted work rather than waiting for its next frame-boundary callback.
- **Readers in one drain pass see one snapshot.** Within a pass, readers passing equal `getSnapshot`
  delegates — the same method group on the same instance — read the snapshot the pass's first read
  pinned, even when the store changes partway through it, the same guarantee `Hooks.UseStore` gives
  readers of one `Store<T>`; the change re-renders them in a later pass, which pins afresh, the delayed
  tier's included. A reader that first subscribes after the store moved
  past the pin renders the pin, then asks for that re-render itself once the render commits. Readers
  holding different closures are not pinned to each other. A render outside a drain — the initial
  mount, a synchronous flush, or a time-sliced render resumed after its drain returned — reads the
  live snapshot, and so does a render whose `subscribe` raises a change while it runs. The scheduler's
  resume of a parked time-sliced pass flushes such a re-render first, so readers the pass committed in an
  earlier slice show the changed snapshot before the next slice renders it.

For a Velvet `Store<T>`, `Hooks.UseStore` stays the shorter form, with a selector and a comparer.

### 1-4. What a dependency list means

The Velvet APIs that take a dependency list — the effect hooks, `UseCallback`, `UseMemo`,
`UseImperativeHandle`, `UseBlocker`, `UseAnimationSequence`, and the node-level `V.Memoized` /
`V.MemoizedWithKey` — read it the same way:

| Spelling | Meaning | React equivalent |
|----------|---------|------------------|
| argument omitted | no dependency list: re-run / recompute / rebuild on every render | `useEffect(fn)` |
| explicit `null` | identical to omitting it | *(a type error in React)* |
| `Array.Empty<object>()` | an empty dependency set: run once and never again | `useEffect(fn, [])` |
| one or more values | re-run when any of them changes, compared with `Object.is` semantics | `useEffect(fn, [a, b])` |

`MemoNode.Dependencies`' remarks state which branch each element type takes.

Three consequences worth knowing before writing one:

- `null` never freezes a value. React has no equivalent spelling, so nothing forces the choice — Velvet
  reads it as the absence of a list, which is the harmless direction: a frozen `UseCallback` captures stale
  state silently, whereas an unmemoized one only costs an allocation.
- Where `deps` is a `params` array, an omitted argument would otherwise arrive as an empty array — the
  opposite meaning — so every such API carries a companion overload declaring no deps parameter at all.
  Where `deps` is an ordinary optional parameter instead, its default is already `null`.
- `V.Memoized` / `V.MemoizedWithKey` build a fresh node per call, so a call site in a render body gets a
  rebuild every render. A node instance hoisted out of the render body and handed back unchanged is the
  same node, and keeps the subtree it already built.

---

## 2. DSL Mapping — JSX → V.*

Since C# has no JSX syntax, Velvet builds the VNode tree through `V.*` method calls.

### 2-1. Basic Elements

| React (JSX) | Velvet | Notes |
|-------------|--------|------|
| `<div className="x">` | `V.Div(className: "x")` | Unity has no HTML elements. Produces a `VisualElement` |
| `<span>text</span>` | `V.Text("text")` | A run of text, materialized as a `Label`. A `<span>` styling part of a sentence is a rich-text tag inside the one `V.Text`, as in `V.Text("a <b>bold</b> word")`. A `<span>` grouping other elements in a line is a `V.Div(className: "flex-row flex-wrap")` |
| `<button onClick={fn}>` | `V.Button(onClick: fn)` | Produces a UI Toolkit `Button` type |
| `<input type="text">` | `V.TextField()` | `placeholder` / `maxlength` / `readonly` are the `placeholder:` / `maxLength:` / `isReadOnly:` parameters. `isDelayed:` has no HTML counterpart: it holds the value back instead of updating per keystroke — see below for what releases it. `inputmode` / `autocorrect` are `keyboardType:` (a `TouchScreenKeyboardType`) / `autoCorrection:`, written to the field's own `keyboardType` / `autoCorrection` |
| `<textarea>` | `V.TextField(multiline: true)` | A render toggling `multiline:` patches the same element, where React swapping `<input>` for `<textarea>` remounts it. Declaring `isPasswordField:` beside it leaves both flags on, a multi-line password field that HTML has no control for |
| `<input type="checkbox">` | `V.Toggle()` | |
| `<input type="range">` | `V.Slider()` | `min` / `max` / `step` are the `lowValue:` / `highValue:` / `step:` parameters. `direction:` (`SliderDirection.Vertical`) and `inverted:` set UI Toolkit's `Slider.direction` and `Slider.inverted` — see below for what null means |
| `<input type="range" step="1">` | `V.SliderInt()` | A UI Toolkit `SliderInt`: `V.Slider`'s parameters over `int`, with an `Action<int>` change handler. The two paragraphs below on `V.Slider`'s parameters and its input hold for it as well |
| `<p>` / `<h1>` | `V.Label()` | UI Toolkit `Label` type |
| `<>{a}{b}</>` | `V.Fragment(a, b)` | `V.Fragment(children, key: "k")` is `<Fragment key="k">` |

`V.TextField`'s `placeholder:`, `maxLength:`, `isReadOnly:`, `isDelayed:`, `multiline:`, `keyboardType:`
and `autoCorrection:` are **undeclared** when null, not reset: null is not `placeholder=""`, not
`maxLength: -1`, not `isReadOnly: false` and not `multiline: false`. A member no render has declared
is left wherever a `refCallback:` put it, and one a render declared and a later render dropped goes
back to the value the field carried before any render declared it. The three focus props follow the
same rule — [focus.md](focus.md) states it for those — and so do
`V.Slider`'s `direction:` and `inverted:`: a dropped `direction:` puts back the direction the slider
had before any render declared one, which is horizontal unless something wrote another by then (a
`refCallback:` or `onCreated:`). `lowValue:` and `highValue:` do not follow it: a render changing
either writes both, a null `lowValue:` as 0 and a null `highValue:` as 10.

A `V.Slider` with `direction: SliderDirection.Vertical` has its high end at the top, as Radix's vertical
`Slider` does, and `inverted: true` puts the low end there. Its input is Radix's `Slider`, whatever the
direction and the flag: Home sets the low value and End the high value; PageUp and PageDown move ten
steps; an arrow moves one step, ten with Shift, with no Enter pressed first. The
arrows are Radix's: Up and Right raise the value and Down and Left lower it, except that an inverted
horizontal slider swaps Left and Right and an inverted vertical one swaps Up and Down. `step:` is the
distance one step covers and defaults to 1, as in Radix, and a value a key or a drag gives the slider
lands on the grid of steps counted from `lowValue:`, which is why a slider over 0 to 1 declares
`step: 0.01`; the controlled `value:` is shown as declared. A key aimed at the numeric input field a slider shows is the field's.

A field holding `isDelayed:` releases the typed text into its value on Enter, on losing focus, and on
a render taking the flag off — that third one whether the render declares `isDelayed: false` or drops
the parameter. The render-driven release reports through `onValueChanged:`, so a component that turns
the flag off mid-edit receives the pending text rather than stranding it on screen. A render turning
`multiline:` on leaves that pending text on screen too, still unreleased.

UI Toolkit puts a field's `keyboardType` back to `Default` and its `autoCorrection` back to false when
the field hands focus from its input back to itself (Enter, Shift+Enter in multiline, Escape). A
declared `keyboardType:` or `autoCorrection:` is written again each time focus comes back into the
field; one written from `refCallback:` is not.

A field's `className` background, border, radius, padding, shadow and ring utilities paint the box the value
is shown in, as on an `<input>`; [which factories and utilities that covers](styling-variants.md#payloads-velvet-realises-itself)
is listed with the `[&>*]:` composite notes.

A render changing `maxLength:` while an edit is pending keeps the edit on screen, cut to the new limit,
and leaves it uncommitted and unreported. When the same render also takes `isDelayed:` off, the cut edit
is released as above.

### 2-2. Conditionals and Lists

| React | Velvet | Notes |
|-------|--------|------|
| `{cond && <X/>}` | `cond ? V.X() : null` or `V.When(cond, () => V.X())` | `null` is the "render nothing" child, and it holds its slot — see [what a position is](#what-a-position-is). `V.When` takes a factory that runs only while `cond` holds |
| `items.map(x => <X key={k}/>)` | `V.List(items, keySelector, renderer)` | Gives each node the selector's key |
| `items.map(x => <X/>)` | `items.Select(x => V.X()).ToArray()` | Unkeyed nodes, matched by index |

### 2-3. Components

| React | Velvet | Notes |
|-------|--------|------|
| `<MyComponent/>` | `V.Component(MyRender, key: "...")` | `MyRender` is a static method annotated with `[Component]`. Stores are distributed via `V.Provider` + `UseContext` |
| `React.memo(Component)` | `[Component(Memoize = true)]` | An opt-in attribute that compares props one member at a time at the reconcile boundary and bails out of parent re-render if they are equal. The attribute's own remarks state the per-member rule, and which props values skip the member walk |
| React Compiler (automatic memoization) | no annotation (all `[Component]`) | The ILPP `CompilerWeaver` weaves inner automatic memoization with default-on, as one cache per component rather than one per subtree (deviation in the note below). Opt out with `[Component(Compiler = false)]` |
| `useMemo(value, deps)` | `Hooks.UseMemo(() => value, deps)` | Value-memoization hook; recomputes only when a dep changes (use inside render) |
| `useMemo(() => <X/>, deps)` | `Hooks.UseMemo(() => V.X(), deps)` or `V.Memoized(() => V.X(), deps)` | The hook returns a memoized VNode; `V.Memoized` is a node-level escape hatch usable outside render (e.g. expanded by `[MemoizeMethod]`), diff-skipping the subtree |
| `useCallback(fn, deps)` | `Hooks.UseCallback(fn, deps)` | Returns a stable delegate while deps are unchanged |

> **Note — Two memoization axes**  
> `[Component(Memoize = true)]` is equivalent to **React.memo**, bailing out of parent-driven re-render when props are shallow-equal to the previous ones (opt-in).  
> **Inner automatic memoization** (equivalent to React Compiler) is **default-on** for all `[Component]`; the ILPP caches VNode construction keyed on the component's props and hook-derived inputs, compared per [§1-4](#1-4-what-a-dependency-list-means). No annotation needed. `ComponentAttribute.Compiler` states what a render whose inputs compare equal shows, and which components are left unwoven. To exclude a specific Component, use `[Component(Compiler = false)]` (equivalent to React's `"use no memo"`).
>
> **Velvet deviation:** the cache is all-or-nothing per component. The weaver emits a single gate keyed on every prop and every hook-derived input together, and the runtime grants each render of a component one memo slot (`Hooks.TryGetMemoizedVNode`), so a change to any one input runs the component's whole VNode construction again, including subtrees that read none of the changed inputs. React Compiler groups a body into reactive scopes, each invalidated by its own dependencies, so a change usually leaves the scopes that do not depend on it cached. To keep a subtree cached across such a change, split it out by hand: `V.Memoized(factory, deps)`, `Hooks.UseMemo`, or a child component declared `[Component(Memoize = true)]` (or mounted through `V.Memo`). Without that, its own cache compares a reference-type props object by instance, and the parent's re-run builds a fresh one.
>
> A props change accepted by the component comparison invalidates its inner VNode cache. A float member changing from `0f` to `-0f` therefore rebuilds the output even when the enclosing record struct considers those props equal.
>
> `Hooks.UseMemo(factory, deps)` is the value-memoization hook (React's `useMemo`). `V.Memoized(factory, deps)` is a node-level escape hatch that explicitly memoizes a **VNode subtree** (callable outside a render, e.g. what `[MemoizeMethod]` expands to); the reconciler reuses the cached subtree while the deps are unchanged. Both read `deps` per [§1-4](#1-4-what-a-dependency-list-means).

<a id="what-a-position-is"></a>
> **Note — what a position is**  
> A component instance and its hook state belong to the position it is rendered at, as in React. The element a `V.Component` is written into is part of that position, so two containers hold two instances even where nothing else about the two call sites differs:
>
> ```csharp
> V.Div(name: "left",  children: new VNode?[] { V.Component(Counter) })
> V.Div(name: "right", children: new VNode?[] { V.Component(Counter) })
> ```
>
> Those are two counters with two counts. Writing a component into a different container than the previous render did is therefore a fresh mount there and an unmount of the one it left — its state, refs and effects do not travel, and `key:` does not carry them, because a key separates siblings of one container rather than one container from another. A key is still what separates two occurrences **in** one container, and what keeps a reordered sibling matched with itself.
>
> Within one container the position is the slot a child is written at, and a child that renders nothing occupies its own: `cond ? V.Component(Row) : null` unmounts that one instance and leaves the components after it on the slots they already held. A slot whose component changes is a remount rather than a re-bind, so two unkeyed siblings swapping places both start over. The elements that component's output emitted go with it: `cond ? V.Component(Row) : V.Component(Counter)` builds the elements `Counter` renders instead of patching `Row`'s into them, so nothing `Row` rendered there is left behind. The `V.Fragment`s and `V.Provider`s a component is written under inside that container count toward its slot, keyed or not, so one keyed component under two sibling wrappers is two instances, and moving it from one wrapper into another is a remount, as in React.
>
> A component keeps the elements it rendered wherever its siblings move it, as in React: a keyed component that a reorder or an inserted sibling moves takes its elements to its new slot, whether it is written into an element, under a `V.Fragment` or at the root, and what it rendered into them or into its `V.Portal` keeps its state. An unkeyed element is matched by the index it is written at in the output of the component that renders it — among the children of the `V.Fragment`, `V.Provider` or other wrapper it is written under there, if any — as in React. What that component's siblings render does not move it; nor does a sibling written before it in that output turning to `null`, or a `V.Fragment` or component written there rendering more or fewer elements. A keyed `V.Fragment` a component returns as its whole output keeps its key, so changing that key remounts what the Fragment holds, as in React; an unkeyed one is the component's output itself. Likewise an unkeyed `V.Fragment` that is an element's, a `V.Motion`'s, a `V.Portal`'s or a `V.WorldSpace`'s only child stands for that parent's children, as React unwraps an unkeyed top-level Fragment under any parent; one written among siblings takes one slot, so moving an unkeyed element into or out of it is a remount.
>
> An element's `key:` is compared with the other elements its own component renders into the container, not with a sibling component's: two components that each render an element keyed `"title"` into one container keep both elements across a re-render, as in React.
>
> A `V.Memoized`'s dependency-cache entry belongs to the same kind of position: the component whose output holds the memo, the `V.Portal` it is written under if there is one, the element the memo's output lands in, and its slot there — counted through the `V.Fragment`s, `V.Provider`s and other wrappers that enclose it inside that element and that output, so wrapping a memo in a `V.Fragment` gives it a new slot. Two containers each holding a memo at their first child cache separately, as do two components that each memoize their own first child into one container. An explicit `key:` stands in for the memo's own index and nothing above it: it keeps the entry across a reorder of the siblings it is written beside, and two `V.ListFragment`s keep apart the memos they each key alike. What the memo renders is placed the same way: under a keyed memo it keeps its state across that reorder, and under an unkeyed one it is placed by the memo's slot, so after a reorder a keyed component there remounts and an unkeyed one takes over the state of whatever held that slot. The entry goes when Velvet tears down that component, that Portal or that element — a Portal moving its children to another element included — so a memo written into a replaced container, or held by a component that remounts, computes again. When recomputation reuses a node from the prior result, the current cached tree keeps that node’s properties and children intact.
>
> Sibling **elements** are matched by position the same way, a `null` among them holding its slot: in `cond ? V.Div(V.Component(Row)) : null` beside a second such `V.Div`, the surviving wrapper keeps its own element and the `Row` inside it keeps its own state, keyed or not.

A container of direct plain elements warns once for each repeated sibling key, on mount and every update; both siblings render. An update with unique sibling keys produces no duplicate-key warning, even if the previous render repeated a key.

### 2-4. Context

| React | Velvet | Notes |
|-------|--------|------|
| `<ThemeContext.Provider value={v}>` | `V.Provider(ThemeContext, v, children)` | A functional form that takes the Context as its first argument |
| `useContext(ThemeContext)` | `Hooks.UseContext(ThemeContext)` | Callable only inside a component function. Propagates Provider value changes live (React parity) |

### 2-5. Suspense / Error Boundary

| React | Velvet | Notes |
|-------|--------|------|
| `<Suspense fallback={<Spinner/>}>` | `V.Suspense(fallback, children)` | Equivalent |
| `use(promise)` | `Hooks.Use(() => someVelvetTask, resourceKey)` | Reads an async resource declaratively; while pending it throws to the nearest `V.Suspense` boundary, just like React's `use()` with a Promise. A loader cancelled through a token the caller owns (a logout CTS, a superseded request) surfaces the `OperationCanceledException` to the nearest error boundary, as React does for an aborted promise. Velvet's own cancellation of the token it hands the loader — on supersede or unmount, as the `Use` API doc describes — records nothing instead. Without a `resourceKey` the loader delegate is the key, so a delegate built afresh each render — a lambda that captures that render's values, or a method group on an instance — is a new resource on every render: the resource restarts each time and a loader that has to wait never delivers, as React's `use()` starts over on a promise created during render once the render that suspended on it has unwound, and the Editor logs a warning naming `resourceKey`. The StrictMode re-run of a render keeps the resource that render read, as React's second invocation keeps the thenable the first one tracked. Like `use()`, it caches nothing beyond its own component: a cache shared across components is `Hooks.UseQuery` ([§1-2b](#1-2b-cached-queries--hooksusequery)) |
| Class Component + `getDerivedStateFromError` | The `V.ErrorBoundary(fallback, children)` helper, or `[Component(IsErrorBoundary = true)]` + `Hooks.UseFallback(fn)` | Explicit opt-in. Once a boundary catches, it keeps showing its fallback until it remounts, as React's does: a re-render — its parent's, or its own — renders the fallback again with the error it caught and does not bring its children back. Give the boundary a new `key` to render its children again. The helper suits a use directly under Mount; the functional pattern suits a fallback that reads the boundary's own props or state |
| Class Component + `componentDidCatch` | `Hooks.UseEffect` + try-catch, or logging via an error-notification Store | When you want to log side effects from a functional component, do it inside an effect. What every caught error goes to is the root's `OnCaughtError`, in the next row |
| `createRoot(container, { onCaughtError })` | `V.Mount(target, tree, new MountOptions(OnCaughtError: (ex, info) => ...))` | Called when a boundary in the tree catches an error, in the commit that shows its fallback: after the fallback's layout effects and before those of the boundary's ancestors, where React calls it. A boundary that an ancestor boundary replaces before that commit reports nothing, as in React. A boundary that a time-sliced transition mounted or re-rendered, catching before the transition completes, reports in the commit that completes it, after its own layout effects. `info` carries the `ComponentStack` and `ErrorBoundary`, the name of the boundary that caught it. Without a handler, the error is logged with `Debug.LogException`: the entry reads as the caught exception itself, and its stack trace goes on to name the boundary and the component stack. An exception the handler throws is logged |

Suspense boundaries in separate host elements or Portals keep independent pending state, including
Portals sharing one target. Updating a suspended primary keeps its fallback visible until its resource
resolves. A component whose render suspended keeps its state meanwhile where no host element sits between it
and the Suspense — one inside such an element is disposed with it — and the layout effects and imperative
handles of one the boundary had shown are taken down in the commit that shows the fallback until the
boundary reveals it again, while one first mounted under the fallback runs none of its effects, passive ones
included, and creates no imperative handle until then, as React
disconnects and mounts them. Removing the boundary releases that pending state when its displayed children are removed.

Where an update's render suspends with no Suspense expansion inside it to catch the signal — the render
of the component that updated, or of one below it that the render reaches, in any slice of a time-sliced
render — the nearest boundary above the updated component renders again, and the updated component renders
again inside it. With no boundary above it, the Editor logs a warning, and each render that suspended is
retried when a resource of the component whose read suspended it resolves; a resource no suspended render
waits on renders nothing when it resolves, and the render retried later reads its value. A render that
suspends with no boundary above commits none of the props it passed to the component that suspended, so a
later render passes them again and, while the resource is pending, suspends again, as React keeps the
previous UI. The boundary is the nearest one above the component whose read suspended, so a component that
renders its own `V.Suspense` reveals through the boundary above it.

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
| A callback ref, `ref={el => …}` | `refCallback:` on an element factory | Attaches at the end of the reconcile pass rather than where the pass creates or patches the element, so every cleanup a pass owes runs before any setup it owes — including when the pass is spread over several frames. A read taken *during* the pass — another component's render body, an `onCreated:`, a `wrapElement:` — therefore sees what the previous pass left, as React's `ref.current` does during render. A second `refCallback:` setup is not such a read: the pass runs its setups as one uninterrupted sequence, in the order it reached their elements, so each sees what the ones before it just wrote. Layout effects and effects run after the pass, so they find it attached — a frame-budgeted flush defers both to the slice that completes the pass, which is also the slice the setups run in. The returned `Action` is the cleanup, run when the element leaves the tree, when the tree is disposed, or when the callback's identity changes — that last one at the end of the pass with the setups, ahead of all of them, so a pass's own patches still see what the old setup published; the same delegate instance across renders leaves the installed ref untouched, which is what `Ref<T>.SetElement` is for |
| `componentDidCatch(error, info)` | Catch descendant render errors via the `fallback` callback of `V.ErrorBoundary`, or via `Hooks.UseFallback` inside a boundary component (`[Component(IsErrorBoundary = true)]`) | The intended semantics is catching descendant render exceptions. Log side effects in the boundary's own `Hooks.UseEffect`; a handler for every caught error is the root's `OnCaughtError` ([§2-5](#2-5-suspense--error-boundary)) |
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
| Fractions (`w-1/2`, `left-1/2`, `-translate-x-1/2`) | Same syntax | Resolved as an inline percent. Sizing (`w-`, `h-`, `size-`, `min-w-`, `min-h-`, `max-w-`, `max-h-`, `basis-`), position (`top-`, `right-`, `bottom-`, `left-`, `inset-`, `inset-x-`, `inset-y-`) and per-axis translate (`translate-x-`, `translate-y-`) take any `a/b` of whole numbers with a non-zero denominator, as Tailwind does (`left-3/2` is 150%). Position and translate also take the negated form (`-left-1/2`); position also takes `full` / `-full` (`left-full` is 100%, `-left-full` is -100%) and `auto` (`top-auto`, written inline, so it overrides a `top-4` the stylesheet supplies and leaves the element where one with no offset sits; it is not a `V.Motion` length channel). `auto` resets the offset to the engine's unset value, UI Toolkit's initial value for these properties; where the engine places an element whose offsets are all unset is its own layout's rule, and is not claimed to match CSS's static position. Not supported: the logical `inset-s-`, `inset-e-`, `inset-bs-`, `inset-be-` and `1/0` |
| `hover:` / `focus:` / `active:` / `checked:` state variants | Same prefixes | Driven by the element's own pointer / focus state (the payload is an ordinary utility) |
| `dark:` theme variant | Same prefix | Driven by `VelvetTheme.IsDark` |
| `sm:` / `md:` / `lg:` / `xl:` / `2xl:` responsive variants | Same prefixes | Min-width breakpoints; evaluated against the panel root by default (or a `@container` scope) |
| `group-*` / `peer-*` relational variants | Same prefixes (incl. named `group/<name>`) | React itself has no equivalent; this is Tailwind parity |
| Stacked variants (`dark:hover:`) | Same syntax, order-independent | Applies only when every gate holds |
| CSS container queries (`@container` / `container-type: inline-size`) | `@container` (apply via `VelvetResponsive.ContainerClass`) | Re-points descendants' `sm:`/`md:`/… at the marked element's width. See [styling-variants.md](styling-variants.md) |

### 4-1a. Logical-direction utilities

Tailwind's logical utilities resolve against the element's inline and block axes. UI Toolkit lays out one
writing mode, left to right and top to bottom, and has no `direction` or `writing-mode`, so Velvet reads
inline-start as left, inline-end as right, block-start as top and block-end as bottom: each utility is the
physical one it equals there, and nothing flips under a right-to-left language.

| Tailwind | Resolves as |
|----------|-------------|
| `ms-*` `me-*` `mbs-*` `mbe-*` | `ml-*` `mr-*` `mt-*` `mb-*` |
| `ps-*` `pe-*` `pbs-*` `pbe-*` | `pl-*` `pr-*` `pt-*` `pb-*` |
| `start-*` or `inset-s-*`, `end-*` or `inset-e-*`, `inset-bs-*`, `inset-be-*` | `left-*` `right-*` `top-*` `bottom-*` |
| `border-s` `border-e` `border-bs` `border-be`, each with `-0` `-2` `-4` `-8` | `border-l` `border-r` `border-t` `border-b` |
| `rounded-s-*` `rounded-e-*` | `rounded-l-*` `rounded-r-*` |
| `rounded-ss-*` `rounded-se-*` `rounded-es-*` `rounded-ee-*` | `rounded-tl-*` `rounded-tr-*` `rounded-bl-*` `rounded-br-*` |

Each takes the values its physical utility takes in Velvet: the spacing scale (`ms-4`, `ms-px`), a bracket value
(`ps-[12px]`), `auto` on a margin or an inset, `full` and the fractions on an inset (`start-1/2`), the radius scale
including the bare `rounded-e`, and a minus sign on a margin or an inset (`-ms-4`, `-start-1/2`). A step the physical
utility lacks, such as `ms-13` or `border-s-3`, is left unresolved as it is there; `border-s-0` is the exception,
resolving to a zero width though the stylesheet declares no `border-l-0`. Variants and the `!` modifier apply
as they do to the physical utility. A logical utility resolves as inline style, so its scale comes from C#
mirrors of `--space-*`, `--radius-*` and the `border-l` widths rather than from the stylesheet: a custom
stylesheet that redefines `--space-4` moves `ml-4` and leaves `ms-4` where it was.

- **Ranking.** Two rules of one variant rank order by the property Tailwind sorts them by: a shorthand
  (`m-4`, `px-4`, `inset-0`, `rounded-lg`, `border-2`) first, then the logical property, then the edge, so
  `hover:m-4 hover:ms-2` leaves `ms-2` on the start edge and `hover:ml-8 hover:ms-4` leaves `ml-8` there. Two base
  utilities on one edge still tie as [styling-variants.md](styling-variants.md) describes, and an inline-resolved
  one outranks a stylesheet class: `ml-8 ms-4` gives the edge to `ms-4`, where Tailwind gives it to `ml-8`.
- **Not resolved.** `scroll-ms-*` and the other `scroll-m*` / `scroll-p*` utilities, because UI Toolkit has no
  scroll-margin or scroll-padding and the physical `scroll-ml-*` is not resolved either; and
  `border-s-` followed by a color, as the physical per-side border colors are not.

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
| Storybook global decorators / `preview.js` | `[VelvetPreviewSetup]` | Runs for each full story mount and is retained during args updates; returns `IDisposable` / `Action` / `void` |
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

    // Store<T> declares this abstract: what Reset() puts the state back to.
    protected override void ResetCore()
        => SetState(_ => new SettingsState(1.0f, false));
}

// Component (Presentation layer)
public static readonly ComponentContext<SettingsStore> SettingsStoreContext = ComponentContext<SettingsStore>.Create();

[Component]
private static VNode VolumeSliderRender()
{
    var store = Hooks.UseContext(SettingsStoreContext);
    // Re-render only when Volume changes
    var volume = Hooks.UseStore(store, s => s.Volume);
    return V.Slider(value: volume, onValueChanged: store.SetVolume);
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
    var handleClick = Hooks.UseCallback(() => setClicked.Invoke(true), clicked);
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
    @base: "btn rounded-full font-bold",
    variants: new Dictionary<string, Dictionary<string, string>>
    {
        ["intent"] = new() { ["primary"] = "bg-primary text-white", ["ghost"] = "bg-transparent" },
        ["size"]   = new() { ["sm"] = "h-8 text-sm", ["lg"] = "h-12 text-lg" },
    });

// Inside Render
V.Button(
    className: ButtonRecipe.Apply(("intent", "primary"), ("size", "lg")),
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

### APIs Out of Scope for This Migration Guide (documented separately)

The following domains are out of scope for this migration guide and are covered in separate documents:

- Layout features such as virtual scroll (`V.VirtualList`, equivalent to react-window — see [virtual-list.md](virtual-list.md)) and Portal (see [portals.md](portals.md))
- Animation features (equivalent to Framer Motion — see [motion.md](motion.md))
- Routing features (equivalent to React Router — see [routing.md](routing.md), and [routing-blockers.md](routing-blockers.md) for navigation blocking)

Since concrete API names stay in sync more easily with the implementation, refer to the XmlDoc / IntelliSense inside the Velvet package.

---

## Appendix: Policy on Aligning with React Behavior

Velvet's first principle is to **reproduce React's semantics as faithfully as possible**; "same name, different behavior" is treated as **a trap to be avoided**, and any difference discovered is resolved toward React over time. See the [package README's design philosophy](https://github.com/s4k10503/velvet/blob/main/Packages/com.velvet.core/README.md#design-philosophy) for the full policy and the three pillars it rests on.

The differences this guide keeps from React are documented inline above. Where the tables list a Velvet-only addition — the `keySelector` `V.List` takes, the factory `V.When` takes — it sits beside the plain C# counterpart rather than replacing it.
