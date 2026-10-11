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
publishes it, with the location, the loader data, the loader errors and the action data that
`Hooks.UseLocation`, `Hooks.UseParams`, `Hooks.UseSearchParams`, `Hooks.UseMatch`, `Hooks.UseLoaderData`,
`Hooks.UseRouteError` and `Hooks.UseActionData` read. It renders the matched route through a `V.Outlet` of its own, so it takes
no children — what appears beneath it is the route table's own elements.

Those seven hooks read what it publishes, and so does every `V.Outlet`: the location picks the route to
render and the errors pick the boundary that replaces it. The hooks that **act** on a router rather
than read from it — `Hooks.UseNavigate` (and so every `V.Link`, `V.NavLink` and `V.Navigate`),
`Hooks.UseNavigation`, `Hooks.UseBlocker`, `Hooks.UseSubmit`, and the setter `Hooks.UseSearchParams`
hands back — act on
the router the nearest `V.RouterProvider` publishes, so two routers can each drive a tree of their own.

Mount it above everything that navigates, and once: a `V.RouterProvider` beneath another throws an
`InvalidOperationException`, as React Router refuses a `<Router>` inside another. Either order works against the first `NavigateAsync`: mounted
first, the opening route arrives through the subscription that carries every later one; mounted after a
navigation has already committed, it reads `Router.CurrentLocation` at its first render.

It does not dispose the router it is handed, as React Router's `RouterProvider` never calls
`router.dispose()`: the code that constructed the router disposes it, which cancels what it has in flight.

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

A navigation runs only the loaders React Router's default `shouldRevalidate` would. A route at the same
place in the committed chain, over the same pathname — the layouts above a changed child — keeps its data
and its loader's token, the pathname compared as the URL spells it. The loader runs again when the search
changes, when the URL is the one already committed, and where the route holds no settled data yet. Once a
route action has started, no route keeps its data on the next navigation to commit — the submission's own,
or one that took over from it — as React Router's `isRevalidationRequired` has it. Stepping `GoBack` /
`GoForward` decides the same way as a push: React Router keeps no loader data per history entry either.

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

An error thrown below a route's `element` — while rendering, from an effect or its cleanup, or from an
element's ref or creation callback — goes to the same place, as React Router's RenderErrorBoundary sends
it: the nearest route at or above it that carries an `errorElement` renders it in place of its `element`,
and the root renders the default one where none does. `Hooks.UseRouteError` returns that error there, and
a `V.Outlet` inside that `errorElement` renders nothing. The error stays until the router publishes a new
location: a navigation, which renders the route navigated to, or the errored route again on the way back;
or a `Suspend` loader of the current location settling, which renders the route again in place.

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
for whatever deferred value that `V.Await` is handed next, as React Router's AwaitErrorBoundary keeps it,
until the `V.Await` remounts — give it a new `key`. Any number of `V.Await` may read one `Deferred<T>`.

## Route actions

A route's `action` is React Router's route `action`: it runs for a submission, and its result is what
`Hooks.UseActionData` returns. `Hooks.UseSubmit()` is `useSubmit()`, handing back a `SubmitFunction` that
takes the form data and a `SubmitOptions` (`Method`, `Action`, `Replace`); `Router.SubmitAsync` is the
same submission for a host object with no component to hook from. With no `Action` named, or an empty
one, a submission goes to the route the submitting component renders in, with the current query string;
with none or with `.`, an index route — a route with an empty path and no children — puts a bare `index`
in front of the query string, and any other route takes a bare one out, as React Router's `normalizeTo`
does. A pathless layout, whose path is empty too, is not an index route.

- The method defaults to `get`, which runs no action: it navigates to the action's path with the form
  data as the query string, which only a `get` encodes. The body is read as React Router reads it with
  the URLSearchParams constructor: null, an `ISearchParams`, a string (one leading `?` dropped,
  `&`-separated, `+` and percent escapes decoded), a dictionary, a sequence of key/value pairs, tuples
  or two-element lists, a primitive, an enum, a `decimal` or a `BigInteger`, whose string (an enum's
  name) parses as a string body does, and any other object, whose readable public properties, indexers
  aside, and then public fields it declares itself stand for a plain object's own enumerable ones, each
  in declaration order. A value is stringified as JavaScript's `String` does: `null` is `"null"`, a
  number has no culture, and a list is its elements joined by `,`. Strings, dictionaries and pair
  sequences keep the order they were written in; an `ISearchParams` groups the values of a
  repeated key, as its interface does. The serialiser is the one every Velvet query uses, so a space
  is `%20`. A body that throws while it is read, or a sequence with an entry that is no pair, is one React
  Router cannot encode either: the navigation commits "Unable to encode submission body" as the error of
  the leaf route and runs no action, to the action's path with its query.
- `post`, `put`, `patch` and `delete` call the action of the route the path matches: the deepest route
  with a path, or the index route when the query string holds a bare `index`. The action receives a
  `RouteActionContext` carrying the route's `Params`, the upper-case `Method` and the `FormData` as it
  was handed to the submit function.
- When the action returns, its result is keyed to its route and published at once, so a route already on
  screen reads it while every matched loader runs; the location then commits with it. The next navigation
  that commits clears it.
- An action that throws, and a route with no action, commit the exception as that route's error, rendered
  by the nearest `errorElement` as a loader's is; only the loaders above the route rendering it run, that
  route keeps the data it held, and no action data commits.
- Any other method runs no action: the navigation commits React Router's 405 ("Invalid request method") as
  the error of the leaf route, as a failed action's is.
- A mutation submitted to the location already committed replaces that history entry, unless its action
  fails or `Replace` says otherwise, as React Router's does.

## Router state

A `Router` keeps the location it has committed apart from the navigation in flight, as React Router's
`router.state` does:

- **The committed location** — `Router.CurrentLocation`, `Router.CurrentLoaderData`,
  `Router.CurrentLoaderErrors` — is React Router's `location`, `loaderData` and `errors`. An attempt
  that does not commit — blocked, matching no route, cancelled or superseded, or thrown out of — leaves
  it as it was. Within a committed location, a `Suspend` loader settling is what changes it.
  `Hooks.UseLocation`, `Hooks.UseLoaderData` and `Hooks.UseRouteError` read it. `Router.CurrentActionData`
  is the exception the route actions section gives: an action's result is published before the
  loaders run.
- **The navigation in flight** — `Router.Navigation`, raised through `Router.OnNavigationChanged` — is
  React Router's `navigation`. It describes one attempt, from the moment its path has matched a route
  until it commits or gives up, and is `Idle` the rest of the time.

How an attempt ended is not router state: `Router.NavigateAsync` and `Router.SubmitAsync` hand it to
their caller, as a `NavigationResult` or as the exception the attempt threw.

## Pending UI

`Hooks.UseNavigation()` is `useNavigation()`, returning the `NavigationState` `Router.Navigation` holds:

- `State` is `NavigationLifecycle.Submitting` from the moment a `post`, `put`, `patch` or `delete`
  submission has matched a route until its action returns, `NavigationLifecycle.Loading` from the moment
  a navigation has matched a route until it commits or gives up, and `NavigationLifecycle.Idle`
  otherwise. A `get` submission, and one whose method no form takes, report `Loading` throughout. A
  path that matches none never reports either.
- `Location` is the location being navigated **to** while `State` is not `Idle` — resolved, so it
  carries the destination's `Params` and `Matches`, not just its path — and null while `State` is
  `Idle`, as `navigation.location` is `undefined` then.
- `FormMethod` (lower-case, as React Router 6.28 reports it), `FormAction` and `FormData` describe the
  submission in flight, a `get` one included, and are null while `State` is `Idle` or the navigation in
  flight is not a submission.

A host object with no component to hook from reads `Router.Navigation` directly.

## Where this deviates from React Router

**Guards.** A route's `guard` and `redirectTo` are declarative properties of the route with no React
Router counterpart, where the same job there is a `redirect` thrown from a loader or from middleware. A
guard runs after the blocker and before an action, and its redirect is not put to the blocker, as React
Router does not put a loader's redirect to its blocker; [routing-blockers.md](routing-blockers.md) owns
where the blocker sits. A submission a guard redirects reaches the target running no action, and
`Hooks.UseNavigation` goes on reporting the submission while the target loads.

**No URL.** There is no browser to own the address bar, so `Router` is `createMemoryRouter`'s
counterpart and holds its own history stack, which grows without a bound as a memory history's does.
`Router.NavigateAsync` states how long a redirect chain may be.

## Also see

[react-migration.md](react-migration.md) holds the React → Velvet naming and API tables, including the
hook list this guide's names come from.
