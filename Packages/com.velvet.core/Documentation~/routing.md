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
publishes it, with the location, the loader data and the loader errors that `Hooks.UseLocation`,
`Hooks.UseParams`, `Hooks.UseSearchParams`, `Hooks.UseMatch`, `Hooks.UseLoaderData` and
`Hooks.UseRouteError` read. It renders the matched route through a `V.Outlet` of its own, so it takes
no children — what appears beneath it is the route table's own elements.

Those six hooks read what it publishes, and so does every `V.Outlet`: the location picks the route to
render and the errors pick the boundary that replaces it. The hooks that **act** on a router rather
than read from it — `Hooks.UseNavigate` (and so every `V.Link`, `V.NavLink` and `V.Navigate`),
`Hooks.UseNavigation`, `Hooks.UseBlocker`, and the setter `Hooks.UseSearchParams` hands back — act on
the router the nearest `V.RouterProvider` publishes, so two routers can each drive a tree of their own.

Mount it above everything that navigates, and once: a `V.RouterProvider` beneath another throws an
`InvalidOperationException`, as React Router refuses a `<Router>` inside another. Either order works against the first `NavigateAsync`: mounted
first, the opening route arrives through the subscription that carries every later one; mounted after a
navigation has already committed, it reads `Router.CurrentLocation` at its first render.

Nothing else in the package publishes those contexts. Beneath no `V.RouterProvider`, `V.Outlet` renders
nothing, `Hooks.UseParams` returns an empty dictionary and `Hooks.UseOutletContext` returns `default`, as
React Router's `<Outlet>`, `useParams` and `useOutletContext` answer outside a router. Every other hook
named above throws an `InvalidOperationException` whose message names the hook, and so do the three
components through the hooks they call, as React Router's counterparts refuse to run outside one. `RouterContext` exposes the contexts themselves, `RouterContext.Router`
among them, which a test can publish directly; an application uses the component.

A value for `Hooks.UseOutletContext` comes from a layout route's own `V.Outlet(context: …)`, which is
where React Router's `<Outlet context>` lives too. The root Outlet takes none.

## Loaders

A route's `loader` runs when the route matches, and `RouteDefinition.LoaderMode` decides how the
navigation is sequenced against it.

| Mode | What it does | React Router equivalent |
|------|--------------|-------------------------|
| `LoaderMode.Await` (default) | The navigation waits. The route already on screen stays there, `Hooks.UseNavigation().State` reports `Loading`, and the location commits with the data. | a plain `loader` |
| `LoaderMode.Suspend` | The navigation commits at once and the loader runs on. `Hooks.UseLoaderData` returns `default` until it resolves, then the route re-renders. | none: React Router defers a value inside a loader's data instead, as the next section does |

`Await` is the one that awaits real I/O, and an `Await` loader that never completes is a navigation
that never commits. Loaders of one navigation all start before any of them is awaited, so the matched
chain's loaders — a parent layout's and its child's — run concurrently rather than one after the next.

Stepping `GoBack` / `GoForward` runs the destination route's loaders as a push to it does: React Router
keeps no loader data per history entry either.

A `Suspend` loader keeps running while an `Await` loader holds the next commit, because the route it
belongs to is still the one on screen: it keeps its cancellation token and its result still reaches
`Hooks.UseLoaderData`. The commit that leaves the route is what cancels it.

A loader still running when its round is cancelled can go on reading its token: the router never
disposes the source behind a token it hands out, so a cancelled token reads as cancelled for as long as
anything holds it.

A cancellation callback a loader registers on its token runs when that cancellation happens. One that
throws is reported through `Debug.LogException` rather than raised at the navigation or the disposal
that caused it.

A loader that throws — or whose task fails — does not abort the navigation. The location commits and
the error is recorded against that route: the nearest route at or above the failing one that carries
an `errorElement` renders it in place of its own `element`, and `Hooks.UseRouteError` returns the
exception there. With no `errorElement` anywhere in the matched chain the root route renders a default
one in place of its `element`, the layout routes below it included, as React Router's default error
element does: a heading, the exception's message and its stack trace. In the editor and in a
development build it also logs the exception.

An error a route's `element`, or a route below it, throws while rendering goes to the same place, as React
Router's `RenderErrorBoundary` sends it: the nearest route at or above it that carries an `errorElement`
renders it in place of its `element`, and the root renders the default one where none does.
`Hooks.UseRouteError` returns that error there. The error stays until the location changes: navigating
away renders the route navigated to, and navigating back renders the errored route again.

### Deferred data

A loader that has part of its data at once and part later returns the late part as a `Deferred<T>` inside
its data — React Router's unawaited promise in loader data. The navigation commits with the rest, and
the route reads the late part through `V.Await` — `<Await>` — beneath a `V.Suspense`, whose fallback shows
until the value arrives:

```csharp compile
public static class Product
{
    public sealed class Data
    {
        public Data(string name, Deferred<string[]> reviews)
        {
            Name = name;
            Reviews = reviews;
        }

        public string Name { get; }
        public Deferred<string[]> Reviews { get; }
    }

    public static VelvetTask<object> Load(RouteLoaderContext ctx, System.Threading.CancellationToken ct)
        => VelvetTask.FromResult<object>(new Data("Lamp", new Deferred<string[]>(FetchReviews(ct))));

    [Component]
    public static VNode Render()
    {
        var data = Hooks.UseLoaderData<Data>();
        return V.Div(children: new VNode[]
        {
            V.Label(text: data?.Name),
            V.Suspense(
                fallback: V.Label(text: "Loading reviews…"),
                children: new VNode[]
                {
                    V.Await(data!.Reviews, reviews => V.Label(text: string.Join(", ", reviews)),
                        errorElement: V.Label(text: "Reviews are unavailable.")),
                }),
        });
    }

    private static async VelvetTask<string[]> FetchReviews(System.Threading.CancellationToken ct)
    {
        await VelvetTask.Yield();
        ct.ThrowIfCancellationRequested();
        return new[] { "Bright", "Sturdy" };
    }
}
```

`V.Await` takes a function of the value or, as element children, components that read it through
`Hooks.UseAsyncValue`. Its `errorElement` renders when the deferred value's task fails or rendering the
value throws, and `Hooks.UseAsyncError` returns the exception beneath it; without one the exception
propagates to the nearest error boundary. After a throw while rendering the value, the `errorElement` stays
for whatever deferred value that `V.Await` is handed next, as React Router's `AwaitErrorBoundary` keeps it,
until the `V.Await` remounts — give it a new `key`. Any number of `V.Await` may read one `Deferred<T>`.

## Pending UI

`Hooks.UseNavigation()` is `useNavigation()`, returning `NavigationState`:

- `State` is `NavigationLifecycle.Loading` from the moment a navigation has matched a route until it
  commits or gives up, and `NavigationLifecycle.Idle` otherwise. A path that matches none never
  reports `Loading`.
- `Location` is the location being navigated **to** while `State` is `Loading` — resolved, so it
  carries the destination's `Params` and `Matches`, not just its path — and null while `State` is
  `Idle`, as `navigation.location` is `undefined` then.

`Router.PendingLocation` is the same destination read imperatively, for a host object that has no
component to hook from.

## Where this deviates from React Router

**No route actions.** Velvet has no form-submission model: a route declares no action, nothing reads
an action's result, and `NavigationLifecycle` therefore has no `submitting` beside its two values.

**Guards.** A route's `guard` and `redirectTo` are declarative properties of the route with no React
Router counterpart, where the same job there is a `redirect` thrown from a loader or from middleware. A
guard runs after the blocker, and its redirect is not put to the blocker, as React Router does not put a
loader's redirect to its blocker; [routing-blockers.md](routing-blockers.md) owns where the blocker sits.

**No URL.** There is no browser to own the address bar, so `Router` is `createMemoryRouter`'s
counterpart and holds its own history stack, which grows without a bound as a memory history's does.
`Router.NavigateAsync` states how long a redirect chain may be.

## Also see

[react-migration.md](react-migration.md) holds the React → Velvet naming and API tables, including the
hook list this guide's names come from.
