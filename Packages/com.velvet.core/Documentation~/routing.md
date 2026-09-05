# Routing: the router root, loaders, and pending UI

Velvet's router is React Router's data router (v6.4+): a route table with nested routes and loaders, a
`Router` that runs them, and one component that publishes what it produces to the tree.

```csharp compile
public static class App
{
    private static RouteDefinition[] Routes() => V.Routes(
        V.Route(path: "/", element: V.Component(Chrome), children: new[]
        {
            V.Route(path: "users/:id", element: V.Component(User),
                loader: (ctx, ct) => LoadUser(ctx.Params["id"], ct),
                errorElement: V.Component(Failed)),
        }));

    public static MountedTree Mount(VisualElement root, out Router router)
    {
        router = new Router(Routes());
        var tree = V.Mount(root, V.RouterProvider(router));
        router.NavigateAsync("/users/7").Forget();
        return tree;
    }

    [Component] private static VNode Chrome() => V.Div(children: new VNode[] { V.Outlet() });
    [Component] private static VNode User() => V.Label(text: Hooks.UseLoaderData<string>());
    [Component] private static VNode Failed() => V.Label(text: Hooks.UseRouteError()?.Message);

    private static async VelvetTask<object> LoadUser(string id, System.Threading.CancellationToken ct)
    {
        await VelvetTask.Yield();
        ct.ThrowIfCancellationRequested();
        return id;
    }
}
```

## The root

`V.RouterProvider(router)` is `<RouterProvider router={router}/>`: it subscribes to the router and
publishes the location, the loader data and the loader errors that `Hooks.UseLocation`,
`Hooks.UseParams`, `Hooks.UseSearchParams`, `Hooks.UseMatch`, `Hooks.UseLoaderData` and
`Hooks.UseRouteError` read. It renders the matched route through a `V.Outlet` of its own, so it takes
no children — what appears beneath it is the route table's own elements.

Those six hooks read what it publishes, and so does every `V.Outlet`: the location picks the route to
render and the errors pick the boundary that replaces it. The hooks that **act** on a router rather
than read from it — `Hooks.UseNavigate` (and so every `V.Link` and `V.NavLink`), `Hooks.UseNavigation`,
`Hooks.UseBlocker`, and the setter `Hooks.UseSearchParams` hands back — go to `Router.Current` instead
of to the provider's router. With one router live those are the same object. With two, a subtree under
`V.RouterProvider(a)` reads `a` and navigates `b`, so keep one router live at a time — constructing a
second logs a warning telling you to dispose the first.

Mount it above everything that navigates. Either order works against the first `NavigateAsync`: mounted
first, the opening route arrives through the subscription that carries every later one; mounted after a
navigation has already committed, it reads `Router.CurrentLocation` at its first render.

Nothing else in the package publishes those three contexts. A tree that renders `V.Outlet` with no
location published above it renders nothing, and one that publishes only `RouterContext.Location` by
hand renders routes while leaving `Hooks.UseLoaderData` empty and every `errorElement` unreachable —
both silently. `RouterContext` exposes the five contexts themselves, which a test can publish
directly; an application uses the component.

A value for `Hooks.UseOutletContext` comes from a layout route's own `V.Outlet(context: …)`, which is
where React Router's `<Outlet context>` lives too. The root Outlet takes none.

## Loaders

A route's `loader` runs when the route matches, and `RouteDefinition.LoaderMode` decides how the
navigation is sequenced against it.

| Mode | What it does | React Router equivalent |
|------|--------------|-------------------------|
| `LoaderMode.Await` (default) | The navigation waits. The route already on screen stays there, `Hooks.UseNavigation().State` reports `Loading`, and the location commits with the data. | a plain `loader` |
| `LoaderMode.Suspend` | The navigation commits at once and the loader runs on. `Hooks.UseLoaderData` returns `default` until it resolves, then the route re-renders. | an unawaited promise in the loader data, read through `<Await>` — but `<Await>` renders inside a `<Suspense>` fallback, and a Velvet route declares no pending element to pair with |

`Await` is the one that awaits real I/O, and an `Await` loader that never completes is a navigation
that never commits. Loaders of one navigation all start before any of them is awaited, so the matched
chain's loaders — a parent layout's and its child's — run concurrently rather than one after the next.

A `Suspend` loader keeps running while an `Await` loader holds the next commit, because the route it
belongs to is still the one on screen: it keeps its cancellation token and its result still reaches
`Hooks.UseLoaderData`. The commit that leaves the route is what cancels it.

A loader that throws — or whose task fails — does not abort the navigation. The location commits and
the error is recorded against that route: the nearest route at or above the failing one that carries
an `errorElement` renders it in place of its own `element`, and `Hooks.UseRouteError` returns the
exception there. With no `errorElement` anywhere in the matched chain nothing below the root renders,
the layout routes included.

Stepping `GoBack` / `GoForward` onto an entry whose loaders had finished serves that entry's data and
errors from the history cache instead of re-running the loaders.

## Pending UI

`Hooks.UseNavigation()` is `useNavigation()`, returning `NavigationState`:

- `State` is `NavigationLifecycle.Loading` from the moment a navigation has matched a route until it
  commits or gives up, and `NavigationLifecycle.Idle` otherwise. A path that matches none never
  reports `Loading`.
- `Location` is the location being navigated **to** while `State` is `Loading` — resolved, so it
  carries the destination's `Params` and `Matches`, not just its path.

`Router.PendingLocation` is the same destination read imperatively, for a host object that has no
component to hook from.

## Where this deviates from React Router

**`navigation.location` when idle.** React Router's is `undefined`, so `Boolean(navigation.location)`
is its canonical "is something pending" test. Velvet's `NavigationState.Location` is the committed
location when idle — the same value `Hooks.UseLocation()` returns. Branch on
`State == NavigationLifecycle.Loading` instead. The React spelling would move the value under every
existing reader with nothing failing to compile, so it is a breaking change and waits for a major
version.

**No route actions.** Velvet has no form-submission model: a route declares no action, nothing reads
an action's result, and `NavigationLifecycle` therefore has no `submitting` beside its two values.

**`UseBlocker` takes an async predicate.** Like `useBlocker` it takes the predicate and hands back a
state object with `Proceed()` / `Reset()`, but the predicate may be asynchronous — the router awaits
it — and the attempt is exposed as `RouteBlockerState.Attempt` (`CurrentPath`, `NextPath`,
`NavigationMode`) rather than as a location. [routing-blockers.md](routing-blockers.md) owns the
blocker states and what each of those two methods does.

**Guards, evaluated before blockers.** A route's `guard` and `redirectTo` are declarative properties
of the route with no React Router counterpart, where the same job there is a `redirect` thrown from a
loader or from middleware. They run before the blocker phase, so an auth redirect is not subject to an
unsaved-changes prompt.

**No URL.** There is no browser to own the address bar, so the router holds its own history stack,
capped at 50 entries. `Router.NavigateAsync` states how long a redirect chain may be.

## Also see

[react-migration.md](react-migration.md) holds the React → Velvet naming and API tables, including the
hook list this guide's names come from.
