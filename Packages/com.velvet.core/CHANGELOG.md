# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased — breaking]

## [3.0.0] - 2026-11-14

### Highlights

- Components keep the state React keeps, and errors stop disappearing. An unkeyed child is matched by the position
  it is written at, so a sibling `cond ? node : null` that stops rendering no longer moves the state of the siblings
  after it; a keyed `V.Fragment` a component returns whole keeps its key; and the container a `V.Component` is
  written into is part of which instance it is, so a move across a `V.Portal`'s boundary remounts it. A boundary
  logs the error it catches, and mounts its fallback afresh when the error comes from an effect, a ref callback or
  the render of its own or a child's update; an error no boundary catches unmounts the tree `V.Mount` mounted, as a
  React root does.

- Suspense, transitions and effects run as React runs them. A `V.Suspense` that suspends again after showing its
  children hides them rather than tearing them down, a transition keeps shown children on screen instead of
  swapping in the fallback, and `UseState` and `UseReducer` queue each update on the lane it was written on.
  Passive effects run children first and before the next render, a `V.Portal`'s children commit where the portal
  sits, a `V.Suspense` runs the layout cleanups of what it hides before hiding it, a removed subtree is taken down
  from the top in one walk, and `UseSyncExternalStore` and `UseStore` subscribe in the commit's effects. Where no
  other portal or virtual-list row encloses them, a parked time-sliced render's resume, a portal's first render and
  an error boundary's fallback read the `V.Provider` above them, where they could read a context's default or
  another Provider's value.

- Stylesheets the UI Toolkit importer used to empty now load, and labels take the default font. A custom property
  holding a `rem`, an exponent, Tailwind's `--aspect-video: 16 / 9`, an `rgb()` with a percentage or a `var()`, or
  another value the importer refuses or misreads is kept as the text it was written with, where the importer failed
  the whole file or stored a different value. `VelvetFonts.DefaultFamily` reaches a label that names no font, as the
  font Tailwind's preflight gives `html` reaches a `<p>`, and gives way to one an author's stylesheet sets.

- The utility classes are Tailwind v4's. The palette, the radius, blur, tracking and easing scales and the line
  height paired with each `text-*` size come from v4; spacing takes a bare multiple of 0.25 such as `p-13` or
  `gap-4.5`, brackets read CSS math functions and custom properties, and a stylesheet's theme tokens generate
  classes. `@container` queries, logical-direction utilities, per-side border colours, `order-*`, `backdrop-*`,
  `disabled:` and `animate-ping` / `animate-bounce` are new. What draws follows CSS: `hue-rotate-*` and filters over
  translucent pixels, `drop-shadow-*`, `shadow-*` and `ring-*` layers and colours, a ring beside an outline, text
  line boxes, `z-*` in flex flow, and a class transition eased on Tailwind's curve from its first frame.

- Motion follows Framer Motion. A transition that names nothing gives each value the default Framer picks for it,
  an unset duration is 0.3 s rather than a jump, `EaseIn`, `EaseOut`, `EaseInOut` and the back eases run on
  Framer's curves, repeats apply to `V.Motion` plays and to `Animator.Animate`, and a per-value transition replaces
  the enclosing one for its property. `Hooks.UseAnimate`, `AnimatePresence(propagate:)`, per-variant transitions,
  sequence `Speed`, spring `Bounce` and `StyleTransitionConfig.Layout` for a `layoutId` move are new.

- Routing is React Router 7's data router. Routes rank as React Router ranks them, index and pathless routes are
  kinds of their own, a path no route matches commits with a 404 `ErrorResponse`, and route actions arrive with
  `UseSubmit` and `UseActionData`, reading a request body by its `encType`. Deferred loader data through `V.Await`,
  `UseMatches`, `UseRouteLoaderData`, `UseResolvedPath`, `UseHref`, and `state` and `relative` on a navigation are
  new.

- Data fetching is TanStack Query v5's. Each `QueryClient` holds a `QueryCache` and a `MutationCache` to find and
  filter entries in; `UseSuspenseQuery`, `UseQueries`, `UseInfiniteQuery`, `UseIsFetching`, `UseMutationState` and
  `UseIsMutating` join `UseQuery` and `UseMutation`; queries and mutations retry by a `RetryPolicy` and pause
  offline under their `NetworkMode`, and a stale query refetches when the application is shown again or the device
  reconnects. `UseQueries` gives a query that shares its entry with another an observer of its own, and hands
  `combine` the other queries' results as it last committed or was notified of them.

- Velvet no longer needs UniTask: it ships `VelvetTask`, an awaitable that allocates nothing on a path that
  completes synchronously. Drag and drop gains dnd-kit's hooks, sensors and ranked collisions, focus scopes follow
  React Aria, and `V.Particles` draws VFX Graph effects and each particle system's baked geometry. The compiler
  transform caches a VNode-building call on only the values it reads, and a component its parent hands back as the
  same element no longer renders again with it. Removing many rows at once, patching every row of an unkeyed list
  and time-slicing a long one no longer take time growing with the square of the row count.

- **Breaking:** the minimum Unity is 6000.3.23f1, the package depends on `com.unity.modules.accessibility`, and every
  public signature that named a UniTask type names `VelvetTask`. Removed: `Router.Status`, `Router.OnStatusChanged`
  and `RouterStatus`, `RouteActionContext.FormData`, `FiberUpdatePriority.Deferred`, `Velvet.PureAttribute`, the
  `V.Outlet` node kind, the painted skew layer, the `QueryOptions` constructors over a `CancellationToken`, the
  `NavigationAttempt` type and the asynchronous predicate overloads of `Hooks.UseBlocker` and
  `RouteBlockerManager.Register`, and `V.FocusScope`'s element arguments. Changed signatures: `V.Portal` takes the
  target element, so `V.Portal(null)` no longer compiles, nor do `V.Particles(null, …)` and a shorthand factory
  given a literal `null` before its children; the factories that gained `events:` take it as their last parameter;
  `MountOptions` takes its options as `init` properties; `V.Motion(variants:)` maps each label to a `MotionVariant`;
  `V.DndContext`, `V.DragOverlay` and `V.FocusScope` return a `ComponentNode`, `V.DndContext` takes its new
  arguments after `activation:`, `DndCollisionDetection` returns a ranked `DndCollision[]`, and `DragOverArgs` and
  `DragEndArgs` take `Collisions`; `Hooks.UseNavigate` returns a `NavigateFunction`, `Hooks.UseBlocker` takes a
  `bool` or a predicate over `BlockerFunctionArgs`, `RouteBlockerState.Location` replaces `Attempt` and its
  `Proceed` and `Reset` are delegates, `SearchParams.Size` replaces `Count` and its enumeration yields entries, and
  `NavigationState.FormData` is a `FormData`; `Hooks.UseAnimationSequence` takes a required `deps`; `QueryInfo`
  became `Query`, the shared options moved to `QueryOptionsBase`, `QueryOptions.QueryFn` is a `QueryFunction<T>`,
  query and mutation functions take a `QueryFunctionContext` and a `MutationFunctionContext`, `QueryState`'s
  failure fields gained a `Fetch` prefix, `Retry` takes a `RetryRule` and `StaleTime` a `QueryStaleTime`,
  `PlaceholderData`, `StructuralSharing` and `QueryFilters.Exact` changed type, and `InvalidateQueries` and the
  `MutationOptions` callbacks return a `VelvetTask`; `VelvetPreviewRegistry.RunSetupFor` returns a
  `VelvetPreviewEnvironment`; the box-shadow shader is named `Velvet/BoxShadow` and `drop-shadow` is a reserved
  `VelvetFilters` name; a skewed `V.Div`, `V.Button` or label is a subclass of its UI Toolkit type; and six more
  `V.*` enum arguments refuse a value naming no member. Behaviour that now follows React, CSS, Framer Motion, React
  Router, TanStack Query and the other libraries Velvet mirrors is described in this version's entries.

### Added

- `VelvetTask` / `VelvetTask<T>` under `Velvet`, an awaitable Velvet ships itself. A pooled source
  carrying a version token, a custom async method builder that stays lazy until first suspension, and
  completed tasks held inline so a synchronously-completed path allocates nothing. Frame-bound
  continuations are driven from the PlayerLoop at runtime and from `EditorApplication.update` in the
  editor, so an `async VelvetTask` method resumes in EditMode. A `UnityEngine.Awaitable` and a BCL
  `Task` are awaited inside one as they stand, with no adapter.

- `VelvetTask.SwitchToMainThread()` brings an `async VelvetTask` body back to Unity's main thread:
  awaited there it completes at once, and elsewhere it resumes the method on the main thread. A
  `VelvetTask` awaited on the main thread resumes there whichever thread completes it — the
  continuation is handed to Unity's main-thread synchronization context rather than run on the
  completing thread — so a route loader that returns after awaiting `ConfigureAwait(false)` hands its
  result back on the main thread. `VelvetTask.Yield()` called off the main thread resumes where
  `Task.Yield()` does. The async guide says what code resumed off the main thread may call.

- `VelvetTask.WhenAll` awaits several tasks as one. Over `VelvetTask` members it completes carrying
  nothing; over `VelvetTask<T>` members it completes with a `T[]` holding each result at its own
  argument position, whatever order the members arrived in; over an empty list it is complete already.
  A member that fails does not end the wait — the others are still waited for, and the combination
  then throws the first fault in argument order, or, where no member faulted, an
  `OperationCanceledException` carrying the token of the first cancelled member in argument order. A
  route loader fetching two resources no longer needs a hand-rolled counter over a completion source.

- `V.RouterProvider(router)` is the router root, as `<RouterProvider router={router}/>` is: it
  subscribes to the router and publishes the location, the loader data and the loader errors that
  `Hooks.UseLocation`, `Hooks.UseLoaderData` and `Hooks.UseRouteError` read, then renders the matched
  route through a `V.Outlet` of its own. It takes no children. Nothing in the package published those
  three contexts before, so an application had to write the bridge itself — a `Hooks.UseState` over
  `Router.CurrentLocation`, a `Hooks.UseEffect` subscribing to `Router.OnLocationChanged` and a
  re-read to cover the navigation that lands before the subscription attaches, under three nested
  `V.Provider`s. A bridge publishing the location alone leaves the other two contexts at their empty
  defaults, so `Hooks.UseLoaderData` answers with nothing whatever the Loader returned, and no
  `errorElement` is reachable — the boundary is chosen from the error context. That is the shape the
  starter sample shipped; it declared no Loader, so nothing there depended on the two it left out. The
  new routing guide states what the provider publishes and where an outlet context comes from.

- A routing guide: `Documentation~/routing.md`. Navigation blocking had one; the rest of routing
  had none.

- `animate-spin` — a full turn a second, linear, forever, with `animate-spin-[<time>]` overriding the
  loop like the other looping utilities. It owns the rotate slot while it runs, on the terms
  `animate-hue` owns the filter.

- `V.TextField` declares four more of the text-input surface: `placeholder:`, `maxLength:`,
  `isReadOnly:` and `isDelayed:` — HTML's `placeholder`, `maxlength` and `readonly`, plus a value that
  catches up with the typed text on Enter, on the field losing focus, and on a render taking
  `isDelayed:` off. That last one reports through `onValueChanged:`, so turning the flag off mid-edit
  hands the component the pending text instead of stranding it on screen.
  Reaching them previously meant writing the UI Toolkit properties
  by hand from `refCallback:`, which left them outside the diff and so unable to change with state.
  Each is undeclared when null: a member no render has declared is left alone, and one a render
  declared and a later one dropped restores the value the field carried before any render declared it.
  The same four arrive on `Velvet.Experimental.VTextField` as `Placeholder`, `MaxLength`, `IsReadOnly`
  and `IsDelayed`.

- `V.Mount(target, tree, options)` takes a `MountOptions`, the options object React's `createRoot`
  takes. Its `OnCaughtError` is React's `onCaughtError`: it is called when a boundary in the tree
  catches an error, with the caught exception and an `ErrorInfo` that now also carries
  `ErrorBoundary`, the name of the boundary that caught it. An exception the handler throws is logged.
  The migration guide's error-boundary table states what happens without one.

- `animate-ping` and `animate-bounce`, with Tailwind v4's keyframes, durations and timing functions. The ping
  scales its element to twice its scale while fading it to nothing by three quarters of the one-second loop,
  then holds. The bounce lifts it a quarter of its height and drops it back, easing in and out on Tailwind's two
  curves. They are documented with the other `animate-*` loops in the motion guide.

- `bg-linear-[…]`, `bg-radial-[…]` and `bg-conic-[…]` take a CSS gradient argument list, as Tailwind v4 does:
  an optional angle, `to_{side}`, `at_{position}` or `from_{n}deg` first argument, optionally with an
  `in_{space}` interpolation, then 2 to 16 colour stops, each with up to two percentage positions —
  `bg-linear-[90deg,#0f172a_0%,#0f172a_45%,#ffffff_50%,#0f172a_55%,#0f172a_100%]` draws a narrow highlight on
  a steady background. Missing positions are filled in by CSS's colour-stop fix-up, a list with no first
  argument runs to bottom, a malformed list leaves the class inert, and skewed elements and
  `animate-gradient` / `animate-shimmer` draw the list as they draw `from-` / `via-` / `to-`. The
  `from-` / `via-` / `to-` stops follow the list's, as Tailwind places them, and a `/` modifier after a
  bracketed list leaves the class inert, as it does there. A radial bracket that is no stop list keeps its
  earlier reading as a centre. A new gradients guide, styling-gradients.md, covers the gradient utilities
  and where they differ from CSS and Tailwind.
  `bg-radial-[…]` also takes a CSS shape and size (`circle`, `ellipse`, the four extent keywords, or radii), stop
  positions may lie outside 0%–100%, and `via-` alone fades in from and out to its own transparent colour.
  A stop may sit at a length in pixels, or on a conic at an angle, and a bare position between two stops is a
  colour hint that moves the half-way mix of the pair.
  The interpolation space may also be `srgb-linear`, `lab`, `lch` or `hsl`, with a hue method
  (`longer`, `shorter`, `increasing`, `decreasing`) for the polar ones.
  A hard edge or narrow band is baked at the element's length (up to 2048 texels along an axis, 512 by 512
  otherwise) instead of 128, so it stays sharp.
- A bracketed length reads the absolute CSS units `in`, `cm`, `mm`, `pt`, `pc` and `Q` at their fixed ratios to a
  pixel (`top-[1in]` is 96px), as it already read `px` and `rem`. This reaches every utility that takes a length
  in brackets, among them positions, translate, blur, gap, shadow offsets, clip-path insets and `leading-[…]`,
  as well as the gradient stop positions.

- The position utilities take Tailwind's fractions: `top-1/2`, `right-1/4`, `bottom-3/4`, `left-1/2`,
  `inset-1/2`, `inset-x-1/3` and `inset-y-2/3` resolve to a percent of the containing block, and so do their
  negated forms (`-left-1/2`). `absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2` centres an
  element in its parent. Each was previously added to the element's class list as a plain class, where it
  positioned nothing and logged no warning. They apply under variants (`hover:left-1/2`) and drive a
  `V.Motion` length channel the way the sizing fractions do.
- The position utilities take `full` and `-full` (`left-full`, `-top-full`, `inset-x-full`), which resolve to
  100% and -100%.
- Fractions take any `a/b` of whole numbers with a non-zero denominator, as in Tailwind: `left-3/2` is 150%,
  `w-1/7` is a seventh. `min-w-`, `min-h-`, `max-w-`, `max-h-` and `basis-` take them too; each was previously
  added to the class list as a plain class and did nothing. A denominator of zero stays unrecognised.
- `translate-x-` and `translate-y-` (and their negated forms) take any `a/b` of whole numbers with a non-zero
  denominator, where they previously took only halves, thirds and quarters.
- `top-auto`, `right-auto`, `bottom-auto`, `left-auto`, `inset-auto`, `inset-x-auto` and `inset-y-auto` write an
  inline `auto` offset, which overrides a value the stylesheet supplies and leaves the element where one with no
  offset sits. Each was previously added to the class
  list as a plain class and did nothing.

- `MountOptions.MotionClock` chooses the clock a mounted tree's motion advances on: every `V.Motion` play
  whatever its `TransitionType`, its delays and stagger slots included, `layoutId` moves, `filter-*`
  transitions, the `animate-*` loops, and the delta `Hooks.UseFrame` hands its callback, which
  `Hooks.UseAnimationSequence` walks its steps on. `MotionClock.GameTime` reads `Time.timeAsDouble`, so that
  motion moves only as far as game time does; a class deriving from `MotionClock` is a clock the application
  steps itself, such as for a frame-step capture. On any clock but `MotionClock.Realtime`, a `Tween` is
  played frame by frame on its own duration, delay, `PropertyOverrides` and easing instead of by UI
  Toolkit's transition, which runs on the panel's time, and reverses over the shortened timing UI Toolkit
  gives a reversed transition. `MotionClock.Realtime`, the default, keeps native transitions and the panel's
  time.

- `V.AnimatePresence` takes `propagate: true`, after Framer's `propagate`. Set on an inner presence, it exits all of
  the inner presence's children through its own exit path while the enclosing presence's keyed child that holds it
  is leaving, and the enclosing child stays mounted until those exits have completed. The inner presence's
  `onExitComplete` runs once, when its own exits finish. A child the inner presence was already exiting is waited
  for rather than exited again, a returning enclosing key brings the inner presence's present children back, and
  a presence between the two that does not propagate stops it. A key added to the inner presence while the
  enclosing child is leaving mounts already leaving, at its `initial` pose, and plays its `exit`.

- `VelvetKeyframes.Register` names a keyframe animation (opacity, pixel translate, scale and rotation stops, each with
  an optional curve), which `animate-<name>` and `animate-[name_1s_ease-in-out_infinite]` loop through the same driver
  as `animate-pulse`.

- Spring and Bezier Motion plays now animate the paint layers their poses name: `shadow-*` layer by layer, `skew-*`
  per axis and `bg-gradient-*` stop by stop (stepping at one half where the two gradients share no stop list), and
  `z-*` swaps at the end of the play. A skew, shadow or gradient exit no longer lands with the swap.

- `Hooks.UseAnimate()`, Framer Motion's `useAnimate`: a `scope` ref and an `Animator` that animates opacity, x, y, scale
  and rotation keyframes of elements inside the scope by reference or selector, alone or as a sequence whose segments
  overlap through `AnimateAt` (`Time`, `WithPrevious`, `Offset`), returning `AnimateControls` with `Play`, `Pause`,
  `Speed`, `TimeSec`, `Then`, `Finished`, `Stop` and `Cancel`.

- `V.FocusScope` takes `orientation:`, a `FocusScopeOrientation` that is also a `FocusScopeSettings` property. On
  a `singleTabStop` group, `Horizontal` ignores an up or down arrow, and `Vertical` a left or right arrow,
  before it moves focus; React Aria's `useToolbar` takes an `orientation` too. Of nested groups, the outermost
  one's value decides. The default, `Both`, is the behaviour before.

- `DndCollisions.ClosestCorners`, dnd-kit's `closestCorners`: every droppable ranked by the mean distance
  between its four corners and the active rect's corresponding four.

- `AnimationSequenceControls.Speed`, Framer Motion's `speed`: the rate a `Hooks.UseAnimationSequence` timeline
  and the `Spring`, `Bezier` and (on a non-panel `MotionClock`) `Tween` plays its steps started advance at, delays included. 0 holds both; setting it
  re-times running plays from the next frame. A negative, NaN or infinite speed throws.
- `AnimationSequenceControls.Cancel`, Framer Motion's `cancel()`: the cursor stops, and each `Spring`,
  `Bezier` or driven `Tween` play the steps started returns to the values it started from and stops there, without its
  completion running. `Play` then starts the sequence again from step 0. The motion guide's Timelines section
  owns both.

- VEL102 warns where a hook is called in a method that is neither a component nor a custom hook named `Use`
  followed by an uppercase letter or a digit, and where a hook sits in a field or property initializer, as React's
  `rules-of-hooks` lint reports a hook in a function that is neither. A helper calling `Hooks.UseState` that a
  component called on some renders only previously drew no diagnostic. A component is a `[Component]` method, or
  a method or lambda handed to `V.Component` or `V.Memo` as its render body. Mark the method `[Component]`, or
  rename it to the custom-hook shape so VEL101 checks each call to it. A `#pragma warning disable` naming VEL102
  opts a test harness out.
- VEL103 warns where a component whose declaration calls a hook is called directly as a plain method, whose hooks
  then run as part of the caller. Mount it with `V.Component` instead. It reads a call that names the component by
  a bare name from within its declaring type or qualified by that type's simple name; a call through
  `using static`, an alias, a base class or an instance is not reported, and nor is a call that is the whole
  render body of a lambda handed to `V.Component`, as in V.Component(() => Sheet()).

- Tailwind's logical-direction utilities resolve, read as the physical utility each equals with the inline axis
  running left to right and the block axis top to bottom: `ms-*`, `me-*`, `mbs-*`, `mbe-*`; `ps-*`, `pe-*`, `pbs-*`,
  `pbe-*`; `start-*`, `end-*`, `inset-s-*`, `inset-e-*`, `inset-bs-*`, `inset-be-*`; `border-s`, `border-e`,
  `border-bs`, `border-be`; and `rounded-s-*`, `rounded-e-*`, `rounded-ss-*`, `rounded-se-*`, `rounded-es-*`,
  `rounded-ee-*`. They take the spacing scale, bracket values, `auto` on a margin or an inset, `full` and fractions on
  an inset, and a minus sign on a margin or an inset, under variants and `!`. They were left in the class list and
  resolved nothing. `scroll-ms-*` and the other scroll margin and padding utilities still resolve nothing.

- Tailwind's per-side border colors resolve: `border-t-*`, `border-r-*`, `border-b-*`, `border-l-*`, `border-x-*`,
  `border-y-*` and the logical `border-s-*`, `border-e-*`, `border-bs-*`, `border-be-*`, each with a palette name, a
  bracketed color or either with an opacity modifier (`border-b-red-500`, `border-x-[#1e293b]`,
  `border-s-white/40`), under variants and `!`. A side's color wins on that side over a `border-{color}` class of the
  same variant rank, whichever is written first, and two inline colors on one side at the same variant rank order
  by Tailwind's property order. They were left in the class list and resolved nothing.

- A bracketed colour reads CSS Color 4's space syntax for `rgb()` / `rgba()` (`bg-[rgb(15_23_42_/_50%)]`),
  `hsl()` / `hsla()` in both syntaxes (`text-[hsl(210_40%_50%)]`), and `hwb()` (`border-[hwb(150_20%_10%)]`),
  with the `/` alpha, percentages, `none` and the `deg` / `grad` / `rad` / `turn` hue units.
  Each previously left the class inert. Every utility taking a colour in brackets reads them:
  `bg-` / `text-` / `border-` and their opacity modifier, `ring-` / `outline-`, `divide-`, `shadow-` /
  `drop-shadow-`, the gradient stops and a custom filter's colour argument. A new guide, styling-colors.md,
  lists the accepted forms.

- Bracketed lengths read `calc()`, `min()`, `max()` and `clamp()`. A function that comes to one pixel length
  or one percentage (`top-[calc(1rem+4px)]`, `gap-[min(2rem,24px)]`, `w-[calc(100%/3)]`) is read wherever
  that plain length or percentage is, except that a border width takes no percentage (below). These classes
  were inert before. See
  `Documentation~/styling-arbitrary-lengths.md`.

- The inline length utilities — sizing, position, padding, margin, `text-[…]` and `tracking-[…]` — read the
  bracketed lengths only an element can measure: `em`, `vw` / `vh` / `vmin` / `vmax` and their `s` / `l` /
  `d` forms, and a math function mixing a percentage with a length (`w-[calc(50%-1rem)]`). Each is measured
  against the element's font size, the panel and the parent's box, and measured again when those change.
  These classes were inert before. See `Documentation~/styling-arbitrary-lengths.md`.

- Every spacing utility takes a bare number the way Tailwind v4 does: any non-negative multiple of 0.25 written
  in canonical decimal form is that many 4px steps, so `p-13`, `gap-4.5` and `w-0.25` resolve where only the
  `--space-*` steps did, and a negated inset (`-top-4`) resolves where no step did. The utilities are padding,
  margin, `gap-*`, `space-x-*` / `space-y-*`, `w-*`, `h-*`, `size-*`, `min-*` / `max-*` width and height,
  `basis-*`, the insets and logical insets, and `translate-x-*` / `translate-y-*`, with a minus sign on the
  margin, inset, translate and space ones. A step the bundled stylesheets already declare stays a class. The box
  utilities resolve the others as inline style, as a bracket value does; `gap-*` and `space-*` are applied by the
  gap manipulator, as they are for the steps they already took.
- `leading-<n>` takes the same steps (`leading-13` is an absolute 52px line height), and a named leading is read
  first.
- `z-<n>` takes a whole number written as Tailwind v4 writes one (`z-15`, `-z-15`), not only 0, 10, 20, 30, 40
  and 50.

- A margin, `w-*`, `h-*`, `size-*`, `min-w-*`, `min-h-*`, `basis-*` or inset takes `[auto]` (`mx-[auto]`); padding
  and the maximum sizes take none.

- Tailwind v4's container query variants: `@container` (or `@container/name`) marks a query container, and
  `@3xs:` … `@7xl:`, `@min-*:`, `@max-*:` and `@min-[475px]:` / `@[475px]:` / `@max-[600px]:` apply by the width
  of the nearest one (its content width, padding and border excluded), `@md/name:` by the nearest one of that name. A query with no container above it
  matches nothing. They stack (`dark:@md:`, `@md:hover:`), rank after every viewport breakpoint
  and before `dark:`, and re-resolve when a render adds, removes or renames the marker on an ancestor.
  `StyleVariantKind.Container` is the kind the parser reports for them.
- `VelvetResponsive.ViewportClass`, the marker that makes `sm:`…`2xl:` measure an element's width in place of the
  panel root's, for tooling that simulates a viewport.

- `navigate(to, options)` takes `NavigateOptions`, as React Router's does: `Replace`, `State`, and `Relative`
  (`RelativeRoutingType.Route` or `.Path`, where `..` drops a segment of the calling route's pathname rather than a matched
  route). `PreventScrollReset` is accepted and read by nothing, as the router has no scroll restoration.
  `navigate(-1)` and `navigate(n)` move `n` entries along the history, and a delta past either end lands on that end
  entry; a delta that does not move runs the current entry's loaders again. `flushSync` and `viewTransition` have nothing to act on in UI Toolkit and are not offered.
  `Router.NavigateAsync(path, options)` and `Router.NavigateAsync(delta)` are the same calls without a component.

- `RouterLocation` gains `State`, `Key` and `Hash`, which `Hooks.UseLocation` returns. `Key` is `"default"` for the
  first entry and eight random base-36 characters, as React Router's `createKey` makes, for every later push or
  replace; a step along the history lands on the state and key its entry was created with.

- `V.Link`, `V.NavLink` and `V.Navigate` take `state` and `relative`. A target that is only a fragment, such as
  `#top`, keeps the current path and drops its query, and `V.NavLink` ignores the fragment when it decides whether
  it is active.

- `Hooks.UseSyncExternalStore<T>(subscribe, getSnapshot)`, React's `useSyncExternalStore`, reads a store
  Velvet does not own without mirroring it into a `Store<T>`. A notification re-renders when the
  snapshot is not `Object.is`-equal to the rendered one or `getSnapshot` throws, on the Urgent lane and
  never the Transition lane, flushed from the main thread's posted work; a `getSnapshot` that builds a
  new snapshot per read re-renders until the update-depth limit drops the update. A `subscribe` that is
  not equal to the previous render's re-subscribes, and unmounting unsubscribes. The change callback
  must be invoked on the main thread, and readers with equal `getSnapshot` delegates share one snapshot
  within one batch drain pass, as `Hooks.UseStore` readers of one store do. The scheduler's resume of a
  parked time-sliced pass flushes such a re-render first.

- `Hooks.UseQuery` over a `QueryClient`, TanStack Query's `useQuery` and `QueryClient`: components reading
  one `QueryKey` share one cache entry and one request in flight, a component unmounting leaves the request
  running for the others, and the entry keeps its result after its last reader unmounts, so a screen
  navigated back to renders it at once instead of fetching from scratch. `StaleTime` and `GcTime` set when a
  result is fetched again and when an unread entry is dropped, `QueryClient.InvalidateQueries` marks the
  entries a key filter matches stale and fetches again those a mounted query reads — the step a
  `UseMutation` `OnSuccess` takes — and `QueryResult.Refetch` fetches one again. The client is provided through `QueryClientContext.Ref` or passed to the hook.
  A failed request runs again three times by default, after one second, then two, then four
  (`QueryClientOptions.Retry` and `RetryDelay`, or `QueryOptions.Retry` and `RetryDelay` for one query), and
  `QueryResult.FailureCount` and `FailureReason` report the failures meanwhile, and data that turns stale by
  age re-renders a component reading `IsStale`. A result that lands keeps the
  instance of the data the entry held where the two are deeply equal, and the equal parts of it where they
  are not (`QueryOptions.StructuralSharing` replaces that), and a component that has read properties of the
  result re-renders only when one of them changes (`QueryOptions.NotifyOnChangeProps` lists them instead). A dictionary or set
  part of a `QueryKey` compares whatever order it was built in, and `InvalidateQueries` matches an array,
  list or dictionary part of its filter partially, as TanStack Query's `partialMatchKey` does.
  In the Editor, a key that is unequal on two commits running while printing the same — an array inside a
  record, which compares by reference — logs a warning, since each such commit fetches a new entry.
  `Hooks.Use` is unchanged and stays cache-less, as React's `use()` is.

- `QueryOptions.Enabled`, `Select` and `PlaceholderData`, TanStack Query v5's `enabled`, `select` and
  `placeholderData`. A disabled query fetches nothing on its own and reports itself not stale, and fetches
  stale data when it is turned on; `Refetch` still fetches. `Select` turns the entry's data into the
  result's, possibly of another type through `QueryOptions<TQueryFnData, TData>` and
  `Hooks.UseQuery<TQueryFnData, TData>`, runs again over the entry's data only when that data's instance or
  the select's changes, and keeps the result's instance where what it returns is deeply equal.
  `PlaceholderData` shows data while the entry is pending, as a success with
  `QueryResult.IsPlaceholderData` true, and never writes it to the entry; `QueryPlaceholder.KeepPreviousData`
  keeps the previous key's data on screen across a key change.
- `QueryClient.GetQueryData` and `SetQueryData`, v5's `getQueryData` and `setQueryData`: a write lands as a
  request's result would, fresh and shared structurally with the data held, leaves a request in flight
  running, and creates the entry when there is none.

- `QueryOptions.RefetchInterval` and `RefetchIntervalInBackground`, TanStack Query v5's `refetchInterval` and
  `refetchIntervalInBackground`: an enabled query fetches again each time the interval has passed since its
  entry last changed or the interval last came round, waiting while the application is not visible unless
  it runs in the background.
- `QueryOptions.RefetchOnWindowFocus` and `RefetchOnReconnect` (`QueryRefetchMode`, or for every query on
  `QueryClientOptions`): a mounted query fetches again when the application becomes visible or the device
  comes back online, by default when its data is stale. `NetworkSignals` holds both readings, with an
  override for an application that measures them itself.
- `QueryOptions.RefetchIntervalFn`, `RefetchOnWindowFocusFn` and `RefetchOnReconnectFn` (the last two also on
  `QueryClientOptions`), TanStack Query v5's function forms of those options, handed the entry's
  `Query`.
- `QueryOptions` constructors taking a query function with no arguments, whose request is not
  cancelled when its last reader leaves, as TanStack Query's is for a function that never reads its `signal`.

- `RetryPolicy`, TanStack Query's `retry` / `retryDelay` / `networkMode` as a value. `Retry` takes a count, a
  bool or `RetryRule.When` over the failure count and exception, as TanStack's `retry` takes a number, a
  boolean or a function (a function replaces the count and `true` retries without end). `RetryDelay` takes a
  constant or a function and defaults to one second doubled per retry and capped at thirty. A retry waits for
  focus and, per `NetworkMode`, for a connection, and the wait is replaceable. `MutationOptions.Retry` opts a
  `Hooks.UseMutation` call into it — the call stays pending across attempts, its `OnMutate` runs once
  before them and its `OnSuccess` / `OnError` / `OnSettled` once after, and `MutationResult` reports `FailureCount`, `FailureReason` and `IsPaused` — and
  `RetryPolicy.RunAsync` wraps a `Hooks.Use` loader so the resource stays pending until the last attempt
  settles. Mutations still do not retry unless a policy is given, as TanStack's mutations default to
  `retry: 0`.

- `Hooks.UseErrorBoundaryReset(resetKeys, onReset)`, react-error-boundary's `resetErrorBoundary`, `resetKeys` and
  `onReset` for a `[Component(IsErrorBoundary = true)]` component. The `ErrorBoundaryReset` it returns makes a
  boundary showing its fallback call `onReset` with the arguments it was invoked with and queue its reset as a
  state update, on the lane the call is made on, after which it renders its children again. Reset keys that
  differ from the last committed render's do the same from the commit, as the library does from
  `componentDidUpdate`. The children mount again, so a failed `Hooks.Use` load among them runs again: this is
  how such a load is retried.
- `Hooks.UseErrorBoundary()`, react-error-boundary's `useErrorBoundary()`. `ShowBoundary(error)`, from an event
  handler or an async continuation, makes the caller throw `error` on its next render, which reaches the
  boundaries above it as any render error does: the first that shows a fallback for it catches it, and one that
  declines passes it up. `ResetBoundary()` resets the nearest error boundary above the caller, passing over the
  ones Velvet renders for routing.
- `V.ErrorBoundary(fallbackRender: (error, reset) => …, children, resetKeys, onReset)`, react-error-boundary's
  `fallbackRender` with `resetErrorBoundary`.

- `Hooks.UseMutation` takes `MutationOptions<TVariables, TData, TContext>`, TanStack Query's `onMutate` /
  `onSuccess` / `onError` / `onSettled` quartet: `OnMutate` runs before `MutationFn`, once the handle shows
  the call pending, and what it returns is the context that call's `OnSuccess`, `OnError` and `OnSettled`
  receive, so an optimistic write made there can be rolled back from `OnError`. A throwing `OnMutate` fails
  the call with its exception before `MutationFn` runs, and `OnError` / `OnSettled` receive a default context.
  Overlapping calls each carry their own context.

- `MutationOptions`, `MutationOptions<TVariables>` and `MutationOptions<TVariables, TData>` take an
  `OnSettled`, an init-only property set in an object initializer, so each record's constructor and
  deconstruction are unchanged. It runs after `OnSuccess` or `OnError` on either path and before the outcome
  is committed. A throwing `OnSettled` on the success path fails the call, which then runs `OnError` and
  `OnSettled` with that exception, as v5 does; on the failure path it is logged and the outcome stays the
  mutation's own, as it does after a throwing `OnError`, which does not cost the call its `OnSettled`.

- `MutationResult.Mutate` and `MutationResult.MutateAsync` take a `MutateOptions<TVariables, TData>` with
  per-call `OnSuccess`, `OnError` and `OnSettled`, TanStack Query's `mutate(variables, { onSuccess, onError,
  onSettled })`. They run after the hook options' callbacks, once the outcome is on the handle, and are
  dropped when the component unmounts, a newer call starts or `Reset` is called, as v5 drops them when the
  observer detaches. Each receives the call's `OnMutate` result as an `object?`.

- Nine-slice background utilities over UI Toolkit's `-unity-slice-*` properties: `slice-[N]` (and
  `border-image-slice`'s two-, three- and four-value forms, `slice-[12_8_4_2]`, with an optional `fill`) and
  its `slice-x-` / `slice-y-` / `slice-t-` / `slice-r-` / `slice-b-` / `slice-l-` edges set the insets — in
  pixels or as a percentage of the background image's size on that axis (`slice-[25%]`, read as at most
  100%), re-resolved when the image changes — `slice-scale-[N]` the scale, and `slice-sliced` / `slice-tiled` the fill mode (UI Toolkit tiles only a
  Sprite whose Mesh Type is Full Rect). They take
  variants and the `!` modifier, and on a field control they go to the input box with the other background
  utilities. The new `styling-backgrounds.md` guide covers them, including that UI Toolkit paints the centre
  with or without `fill`.
- Tailwind's `bg-repeat`, `bg-no-repeat`, `bg-repeat-x`, `bg-repeat-y`, `bg-repeat-round` and
  `bg-repeat-space` utilities.
- `StyleOverrides.BackgroundRepeat`, `UnitySliceTop`, `UnitySliceRight`, `UnitySliceBottom`,
  `UnitySliceLeft`, `UnitySliceScale` and `UnitySliceType`, for the same properties computed at runtime. Each
  wins over a utility writing the same property and loses to an important one.

- `pointer-events-none` and `pointer-events-auto`. `pointer-events-none` makes an element and its whole
  subtree transparent to the pointer while it keeps painting and stays enabled, so a click reaches whatever is
  behind it — CSS's `pointer-events: none`, a uGUI CanvasGroup with `blocksRaycasts` off. `pointer-events-auto`
  on a descendant takes that descendant back. Elements mounted into the subtree later join it — a tree
  mounted or portalled into it and `V.VirtualList` rows included — every element gets back its own picking
  mode when the utility goes away, and both utilities work behind the `dark:`, responsive and `group-`
  variants. See `Documentation~/styling-pointer-events.md`.

- `caret-*`, `selection:bg-*` and `selection:text-*` colour a text field's caret and selection, as
  Tailwind's `caret-color` and `selection:` utilities do:
  - each takes a palette colour, `white`, `black`, `transparent`, a bracketed value or an opacity
    modifier, and `caret-current` follows the text colour, `caret-current/50` at half its alpha;
  - variants apply, a field takes the nearest ancestor's `caret-*`, and `-inherit` passes to the
    parent;
  - competing utilities resolve in the order Tailwind emits them, not the order the classes were
    added in, and a `selection:*` on the field competes with every ancestor's, as Tailwind's
    `& *::selection, &::selection` does;
  - a colour goes back to the theme's, or the field's own, when no utility asks for one;
  - while a selection utility applies and the field holds focus and a non-empty selection, the
    selected text is drawn again above UI Toolkit's selection highlight, so an opaque highlight reads
    as a background.

- `V.TextField` takes `onKeyDown:`, `onKeyUp:`, `onFocus:`, `onBlur:`, `onSubmit:` and `onCreated:`, so a
  commit-on-Enter field, a chat box clearing itself on send or a field reacting to leaving focus no
  longer needs a `V.Motion` wrapper or a `refCallback:` registering callbacks by hand. `onKeyDown:`
  runs before the field takes the key, and stopping the event's propagation keeps the key out of it.
  `onFocus:` and `onBlur:` report focus entering and leaving the field as a whole, not the step where
  the field hands focus from its input to itself on Enter. `onSubmit:` receives the value on the Enter
  in a single-line field, after a delayed field has released its text, as the browser's implicit form
  submission does: a read-only field submits, the input keeps focus and its caret across the
  submitting Enter, the Enter
  arriving while an IME composition is open does not, Enter with Ctrl or Command held and Alt not held
  does not, and a multi-line field never submits. The soft keyboard's Done submits too, read from the
  keyboard's status when it blurs the field; that reading has not yet been verified on a device.
  `Velvet.Experimental.VTextField` carries the five handlers as `OnKeyDown`, `OnKeyUp`, `OnFocus`,
  `OnBlur` and `OnSubmit`.

- `V.TextField` declares `multiline:`, `keyboardType:` and `autoCorrection:` — HTML's `<textarea>`,
  `inputmode` and `autocorrect`. Each is undeclared when null on the terms the field's other text-input
  parameters follow, so a multi-line notes box or a field asking for a numeric soft keyboard can change
  with state instead of being written by hand from `refCallback:`. A render taking `multiline:` and
  `isDelayed:` off together commits the pending text with its line breaks, and one turning `multiline:`
  on leaves a delayed field's pending text on screen. A declared `keyboardType:` or `autoCorrection:` is
  written again as focus comes back into the field, since UI Toolkit resets both when the field hands
  focus from its input back to itself (Enter, Shift+Enter in multiline, Escape). `TextFieldSettings`
  carries them as the init-only `Multiline`, `KeyboardType` and `AutoCorrection`, leaving its constructor and
  deconstruction as they were, and `Velvet.Experimental.VTextField` as properties of the same names.

- `V.Slider` declares `direction:`, `inverted:` and `step:`. The first two are UI Toolkit's `Slider.direction` and `Slider.inverted`,
  so a vertical or inverted slider can change with state instead of being set by hand from `refCallback:`.
  Those two are undeclared when null, on the rule `V.TextField`'s `placeholder:` follows: a member no render has
  declared is left where a `refCallback:` put it, and one a later render drops goes back to what the slider
  carried before any render declared it. A `direction:` naming no member of `SliderDirection` throws an
  `ArgumentOutOfRangeException` before the call rents anything, as does a `step:` that is not above zero and finite. A render that changes only these two leaves
  the range alone, so a range a `refCallback:` set survives it. The same three arrive on
  `Velvet.Experimental.VSlider` as `Direction`, `Inverted` and `Step`.

- `V.SliderInt` builds a UI Toolkit `SliderInt`, the whole-number counterpart of `V.Slider`: `value:`,
  `lowValue:`, `highValue:` and `step:` are `int`, `onValueChanged:` receives an `int`, and `direction:`,
  `inverted:`, the range rule and the keyboard are `V.Slider`'s. A `direction:` naming no member of
  `SliderDirection`, or a `step:` that is not above zero, throws an `ArgumentOutOfRangeException` before the
  call rents anything. The props bag carries it as `FiberElementProps.SliderInt` (`SliderIntSettings`), and
  `Velvet.Experimental.VSliderInt` is its builder.

- `AnimationSequenceControls`, the handle `Hooks.UseAnimationSequence` returns, carries a read-only
  `TimeSec` beside `Play`, `Pause` and `Restart`: the Web Animations API's `currentTime` over the
  sequence's own timeline, counting each hold at its authored length. Seek and reverse are not offered; the
  motion guide's Timelines section says why.

- `StyleTransitionConfig.Repeat`, `RepeatType` and `RepeatDelaySec` repeat a `V.Motion` play: Framer Motion's
  `repeat`, `repeatType` and `repeatDelay`, played by `TransitionType.Bezier` and `TransitionType.Spring` on
  enters, label changes and exits. `Repeat` counts the passes after the first and takes
  `float.PositiveInfinity` for a play that repeats until something replaces it. `TransitionRepeatType.Loop`
  starts each pass at the from-pose, `Reverse` plays every second pass backwards in time (CSS's
  `animation-direction: alternate`), and `Mirror` plays it from the to-pose back. A spring's channels each
  repeat on the time their own spring takes to rest, as Framer's values do. An odd count under `Reverse` or
  `Mirror` ends on the from-pose and holds it, as Framer's does. `When = BeforeChildren` waits for every pass
  and every wait between them, and never releases its children under an endless repeat. A
  `Hooks.UseAnimationSequence` `To` step deriving its hold waits for a repeat below 20; one of 20 or more is
  dropped from the transition the step hands out, with a warning, as Framer's sequence drops it.

- `Hooks.UseAnimationSequence` has an overload taking `iterations` in place of `loop`, which plays the steps a
  fixed number of passes: the Web Animations API's `iterations` and CSS's `animation-iteration-count`, so the
  count includes the first pass. The sequence completes once the last pass's last hold elapses, a restart
  plays every pass again, and `iterations: 0` plays none, committing no step. A count a re-render changes applies
  as the Web Animations API's `updateTiming` applies it: lowered to the passes already finished it completes the
  sequence at the next frame with the end state a normal completion holds, no skipped `Call` run, and raised it
  resumes a completed one. `repeatDelaySec` is Framer Motion's `repeatDelay`, a gap between passes that never
  delays completion. The overload taking `loop` is unchanged.
  `controls.TimeSec` counts a `repeatDelaySec` gap as it passes and a sequence that played its passes reads their
  total length plus the gaps between them, as Framer Motion's `time` does for a repeated animation.
  An alternate direction is not offered; the motion guide's Timelines section says why, and where the count
  differs from the Web Animations API's.

- `AnimationSequenceStep.Await(taskFactory)` holds a `Hooks.UseAnimationSequence` sequence until the
  `VelvetTask` its factory returns settles, the `await` a Framer Motion `useAnimate` caller writes between two
  animations, so a sequence can wait on a server response, a tap or a dialogue advance rather than only on a
  clock. The factory is handed a `CancellationToken` that a restart, a `deps` change or unmount cancels while
  the task is pending, and a task the sequence has left advances nothing. A task that settles while the sequence
  is paused is read once it resumes, and a fault reaches the nearest error boundary as a throwing `Call` step's
  does. The motion guide's Timelines section has the rest.

- Every `V.*` factory that returns an element node, other than the class-and-children shorthands, takes an
  `events:` array of `FiberEventBinding`s, as `V.Motion` already did, so a pointer, key, focus, wheel or
  geometry handler goes on a `V.Div`, a `V.SceneView` or a control directly, the way a React host element
  takes `onPointerDown`, instead of on a `V.Motion` wrapper that animates nothing. Those shorthands, of
  `V.Div`, `V.Custom<T>`, `V.ScrollView` and `V.Button`, each gain a sibling taking the array,
  `V.VirtualList` takes it for its `ScrollView`, and `V.Link` and `V.NavLink` for their button. A
  factory's own `onClick:` or `onValueChanged:` is bound ahead of the array, and one delegate passed both
  ways is bound once and left bound across renders. The array is never written, and once the pool is warm
  a factory handed one allocates nothing the same call without it does not. The `Velvet.Experimental`
  builders carry it as `Events`. The migration guide's event-props section states the rule.
- `Capture = true` on a pointer down, up or move, wheel, key, focus in or out, focus or blur binding is
  React's `on…Capture`: capture handlers run outermost first and ahead of every bubble handler, the target's
  own included. Across a portal, a logical ancestor of the call site captures before the portal's content
  and bubbles after it, once however many portals' content the target's physical path crosses, and a
  pointer-down or pointer-up the layer router hands to a layer portal's content reaches its logical
  ancestors in both phases.
- `ClickedEventBinding` hands its handler the click as a `ClickedEvent`, shared by every such binding on the
  button for that click. `ClickedEvent.PreventDefault()` is React's `event.preventDefault()`, and `V.Link` and
  `V.NavLink` skip their navigation for a click a handler in their `events:` prevented, as React Router's
  `Link` does.

- `leading-<n>` sets a line height of n spacing units, as Tailwind's bare-number `leading-*` does:
  `leading-6` gives 24px and `leading-3-5` 14px, on the same `--space-*` steps as `p-*`. Only the six
  named presets and the bracket form were recognised before, so `leading-6` changed nothing.

- `V.VirtualList` takes `horizontal: true` to lay its items out in a row it scrolls sideways, FlashList's
  `horizontal`; `itemHeight` is then each item's width.
- `VirtualListHandle.ScrollToItem` takes a `VirtualListScrollBehavior`, react-window's `scrollToRow`
  `behavior`: `Smooth` animates the list to the item over 300 ms, and any other scroll along the list's axis
  ends the animation where it stands. `Auto`, the default, and `Instant` jump there as before.

- `Hooks.UseSuspenseQuery`, TanStack Query's `useSuspenseQuery`: a pending result suspends to the nearest
  `V.Suspense`, its render starting the request, and an error the entry holds no data for is thrown to the nearest
  error boundary.
- `ThrowOnError` and `ThrowOnErrorFn` on `QueryOptions` and `QueryClientOptions`, v5's `throwOnError`, with `QueryErrorResetBoundary`,
  `QueryErrorResetBoundaryContext` and `Hooks.UseQueryErrorResetBoundary`, v5's `QueryErrorResetBoundary` and
  `useQueryErrorResetBoundary`.
- `QueryOptions.InitialData`, `InitialDataUpdatedAt` and `InitialDataUpdatedAtFn`, v5's `initialData` and
  `initialDataUpdatedAt`; `ResetQueries` returns an entry to the initial data it took.
- `QueryStaleTime.Static`, v5's `staleTime: 'static'`, and `StaleTimeFn` on `QueryOptions` and
  `QueryClientOptions`, v5's `staleTime` as a function.
- `RefetchOnMount` and `RefetchOnMountFn` on `QueryOptions` and `QueryClientOptions`, v5's `refetchOnMount`.
- `QueryOptions.EnabledFn`, v5's `enabled` as a function, and `QueryOptions.RetryOnMount` and `RetryOnMountFn`,
  v5's `retryOnMount`.

- `QueryClient` commands from TanStack Query v5: `RefetchQueries`, `CancelQueries`, `RemoveQueries`,
  `ResetQueries`, `FetchQuery`, `PrefetchQuery`, `EnsureQueryData`, `GetQueryState`, `GetQueriesData` and
  `SetQueriesData`, and an `InvalidateQueries` overload taking `QueryFilters` and `RefetchOptions`.
  `QueryFilters` selects entries by key prefix or exact key, active or inactive type, staleness, fetch status
  and predicate. `RefetchQueries`, `InvalidateQueries` and `ResetQueries` return a task that completes once
  their requests have settled, so a mutation callback that returns it waits for the fresh data, and
  `CancelQueries` returns a completed task for an `OnMutate` to await before it writes an optimistic value.

- `ErrorResponse`, React Router's error response: `Status`, `StatusText` and `Detail`, the exception a navigation to a
  path no route matches commits with status 404. `Hooks.UseRouteError` returns it to the `errorElement` that renders it.

- `Hooks.UseMatches`, React Router's `useMatches`: every route the current location matched, parent first, as a `UiMatch`
  with its `Id`, `Pathname`, `Params`, loader `Data` and `Handle`. `RouteDefinition.Handle` (and `V.Route(handle:)`)
  carries the data a route surfaces there.

- `RouteDefinition.Id` (and `V.Route(id:)`), React Router's route `id`: the key of the route's loader data, and what
  `Hooks.UseRouteLoaderData` takes.

- `Hooks.UseRouteLoaderData`, React Router's `useRouteLoaderData`: a matched route's loader data by id, from any component
  beneath the provider.

- `Hooks.UseResolvedPath` and `Hooks.UseHref`, React Router's `useResolvedPath` and `useHref`: a target resolved from the
  route the calling component renders in, as `Hooks.UseNavigate` resolves it, as a `ResolvedPath` (`Pathname`, `Search`,
  `Hash`) or as the joined string. They take `RelativeRoutingType`.

- `Hooks.UseNavigationType` and `Router.NavigationType`, React Router's `useNavigationType`: whether the committed
  location was reached by a `Push`, a `Replace` or a `Pop` (nothing committed yet, or a step along the history).

- `Hooks.UseInRouterContext`, React Router's `useInRouterContext`: true beneath a `V.RouterProvider`.

- `MemoizationReportAttribute`, written onto each `[Component]` method by the build-time transform: whether the
  body as a whole is keyed, how many parts are cached on their own, and one line per thing left uncached with the
  reason. An assembly carrying `AssemblyMetadata` with `MemoizationReportAttribute.MetadataKey` and
  `MemoizationReportAttribute.WarnValue` has the same lines reported as build warnings.

- CSS custom properties as classes: `[--name:value]` declares one on an element and its descendants inherit it,
  `var(--name)` and `var(--name, fallback)` read it in a utility's bracket or its modifier's
  (`w-[calc(var(--w)*2)]`, `from-[var(--brand)]`, `bg-red-500/[var(--alpha)]`), and Tailwind v4's
  `bg-(--brand)` / `bg-red-500/(--alpha)` shorthand and `[property:value]` arbitrary properties read it too,
  and `z-[var(--z)]` / `z-(--z)` restack an absolute element when the property changes.
  Custom properties a USS stylesheet declares are read as well. A `var()` that does not resolve computes the
  property to `unset`, as in a browser. These classes were inert before. See
  `Documentation~/styling-custom-properties.md`.
- A shorthand's bracket takes one value per side, as the CSS property does: `p-[1px_2px]`, `m-[0_auto]`,
  `inset-*`, `rounded-*`, `border-[length:…]`, `border-[color:…]`, and a row and a column gap in `gap-[…]`.
- `ring-current`, `outline-current`, `leading-[inherit]`, `font-[weight:inherit]`, `font-[number:…]`,
  `font-[family-name:…]` and `font-[generic-name:…]`, and a percentage in an opacity modifier's bracket
  (`bg-red-500/[50%]`), as Tailwind writes them.

- `skew-<n>`, `-skew-<n>` and `skew-[…]` skew both axes by one angle, as Tailwind writes them, and compose with
  `skew-x-*` and `skew-y-*` as Tailwind's `skewX() skewY()`, a `shadow-*` on the element included. Each axis takes
  the class Tailwind emits last: a `skew-x-*` or `skew-y-*` class takes its axis over `skew-<n>` whichever is
  written first and whatever their signs (`skew-x-12 skew-6` and `-skew-x-12 skew-6` keep their x angle), a class
  behind a variant takes it over the base ones (`skew-x-12 hover:skew-6` gives 6° on both axes while hovered), and
  an important class takes it over every plain one. A bare value is a whole number of degrees without a
  leading zero (`skew-1.5` and `skew-07` are declined), and a bracket an angle or `0`.
- `-scale-<n>`, `-scale-x-<n>` and `-scale-y-<n>` scale by the negative factor, mirroring the element, as Tailwind
  writes them; they drew nothing. `-scale-<n>` writes both axes the way `scale-<n>` does, so `-scale-50 scale-x-75`
  gives `(0.75, -0.5)`.
- `scale-<n>`, `scale-x-<n>` and `scale-y-<n>`, negated or not, take any whole number as a percent, as Tailwind does
  (`scale-33`, `scale-x-120`); a value outside the bundled presets drew nothing. A leading zero is declined
  (`scale-07`), as Tailwind generates no rule for it.

- `Hooks.UseCallback` overloads named for `Action`, `Action` of two or three parameters and `Func` of one to
  three, so a C# 9 assembly, where a lambda has no type of its own, calls it without a type argument for a
  parameterless `Action` lambda or method group and for a lambda of those shapes with typed parameters, as
  TypeScript infers `useCallback`'s. None of them takes one type argument, so every explicit
  `UseCallback<T>(...)` call binds as it did.

- `V.Fragment(a, b, …)` takes its children as `params` arguments, the counterpart of `<>{a}{b}</>`, so a
  Fragment needs no `new VNode?[] { … }` around them.
- VEL600 warns at a `V.*` factory call without a `key:` whose node reaches a list built by `Select`, by
  `yield return`, by storing into an array element or by a `List<VNode>` adder, through locals, fields,
  properties, helper methods and delegates, as React warns about a list child with no key.

- `V.VirtualList` takes its `itemHeight` as a function from an item's index to its height, react-window's
  `rowHeight` function, as well as a single height.
- `V.VirtualList`'s `listRef` holds a `VirtualListHandle` while the list is mounted. Its
  `ScrollToItem(index, align)` scrolls the list as react-window's `scrollToRow` does, `align` being
  `VirtualListAlign.Auto`, `Smart`, `Center`, `End` or `Start`, and its `Element` is the list's `ScrollView`.
- `V.List` logs a warning, once per component, when its `keySelector` returns `null`, as React warns about a
  list child with no key.

- `StyleTransitionConfig.Bounce`: with `DurationSec`, it describes a `Spring` that sets none of `Stiffness`,
  `Damping` and `Mass`, as Framer Motion's spring `bounce` does.

- `StyleTransitionConfig.StaggerDirection`: -1, or any value but 1, starts the last inheriting child first when
  `StaggerChildrenSec` staggers them, as Framer Motion's `staggerDirection: -1` does. The children are numbered back
  from the count the Motion's node declares, a component counting as one.
- A `skew-x-*`, `skew-y-*` or `skew-*` angle animates through a Motion's enter, label swap and exit on a Spring, a Bezier
  or a Tween, where an exit used to keep the angle it rested at. A Tween that moves a skew angle runs on the Bezier
  driver, since UI Toolkit's transition cannot draw one.
- A `PopLayout` ghost paints over the siblings that reflow into its rect: it moves into its parent's front layer while
  it exits, where it used to keep its original paint order, and its exits run on the driver while it does.

- A `disabled:` variant, as Tailwind's. Its payload holds while the element or any ancestor is disabled — the
  condition USS `:disabled` matches — whether a Velvet `enabled:` prop or a `SetEnabled` call made it so, and
  it stacks with the other variants in either order (`dark:disabled:`, `disabled:hover:`). On a tie it ranks
  above `active:`, where Tailwind emits it. `StyleVariantKind.Disabled` is the new member.

- `group-disabled:` and `peer-disabled:`, as Tailwind's. They read the marked `group` ancestor or `peer` sibling
  on the same terms `disabled:` reads the element, and rank above the other `group-*` and `peer-*` states
  respectively on a tie. `StyleVariantKind.GroupDisabled` and `StyleVariantKind.PeerDisabled` are the new members.

- `nth-N:` and `nth-last-N:`, Tailwind's functional forms of `[&:nth-child(N)]:` and `[&:nth-last-child(N)]:`.
  As in Tailwind, `N` is a whole number written without leading zeros, and `nth-0:` matches no child.

- `grow-<N>` / `shrink-<N>` take a whole-number factor, as Tailwind's bare values do: `grow-3` sets
  `flex-grow: 3`. A fraction still takes the bracket form, `grow-[2.5]`.

- Negative space utilities, `-space-x-*` / `-space-y-*`.

- `border-t-0`, `border-r-0`, `border-b-0`, `border-l-0`, `border-x-0` and `border-y-0`, which Tailwind ships and the
  bundled stylesheet did not. `border border-t-0` has no top border.
- Tailwind v4's `flex` shorthand utilities: `flex-<N>` (`flex: <N>`), `flex-<a>/<b>` (`flex: calc(<a>/<b> * 100%)`)
  and `flex-[…]`, which takes the CSS shorthand with `_` for each space, such as `flex-[2_1_120px]`, `flex-[1_1_0]`
  or `flex-[none]`. A `grow-*`, `shrink-*` or `basis-*` beside one takes its own part whichever is listed first, as
  Tailwind declares the shorthand ahead of them. They were left in the class list and resolved nothing.

- The `backdrop-*` utilities filter what lies behind an element, inside its rounded border box, and leave the
  element's own background, border and children unfiltered: `backdrop-blur-*`, `backdrop-brightness-*`,
  `backdrop-contrast-*`, `backdrop-grayscale-*`, `backdrop-hue-rotate-*`, `backdrop-invert-*`,
  `backdrop-opacity-*`, `backdrop-saturate-*` and `backdrop-sepia-*`, their bracket forms included, composed in
  Tailwind's order, and `backdrop-filter-none` and `backdrop-filter-[…]`, a list run in the order written. They
  work under variants, the important modifier, the custom-property forms, an ancestor's rounded clip and a
  `clip-path-*`, and transition under `transition-all` by CSS's filter-list interpolation. Each class was previously
  inert. styling-filters.md lists the values and where the result differs from a browser's.
- `brightness-*`, `contrast-*`, `saturate-*`, `grayscale-*`, `invert-*`, `sepia-*` and `hue-rotate-*` take any
  whole number, as Tailwind v4 does (`brightness-80`, `hue-rotate-45`); only the old preset values were read before.
- `blur-[…]` and the lengths of a `drop-shadow-[…]` read `em`, the viewport units and a math function over them,
  measured on the element and measured again when its font size or the panel's size moves, as the sizing and
  spacing utilities do. A math function coming below zero gives a blur or a deviation of zero.

- `antialiased` and `subpixel-antialiased`, which write `-unity-editor-text-rendering-mode` (`sdf` and
  `bitmap`).
- `leading-[…]` reads a viewport unit (`leading-[10vw]`) and a math function mixing an em or a percentage
  with another length (`leading-[calc(1em+4px)]`), measured on the element that declares it and measured
  again when its font size or the panel changes. These used to leave the line height unset.

- `tabular-nums`, `proportional-nums` and `normal-nums`: under `tabular-nums` each run of digits is wrapped in an
  `<mspace>` tag at the width of the widest digit of the leaf's font, so every digit takes the same room.
- A font-family list, `font-[Inter,jp,sans-serif]`: the first registered family is assigned and the
  Font Assets of the families after it are appended to its fallback table in list order, so a glyph it lacks
  is taken from the next. See `Documentation~/fonts.md`.

- `font-stretch-ultra-condensed` … `font-stretch-ultra-expanded`, `font-stretch-<percentage>` and
  `font-stretch-[<percentage>]`, matched against a new `stretch` on `VelvetFontWeightEntry` (a percentage of the
  normal width, 100 where not set). A family settles the width of the face before its style and its weight, as
  CSS's font matching does. `VelvetFonts.Resolve` has an overload taking the width, and `FontIntent`'s
  constructor two trailing parameters for it. These classes used to name a family called `stretch-…`.
- `leading-[normal]`, CSS's `line-height: normal`: the text keeps its font's own line advance and an ancestor's
  line height no longer reaches it.

- `FocusScopeSettings.Wrap` and `V.FocusScope`'s `wrap:`: an arrow or d-pad move past one end of a
  `singleTabStop` group lands on the other, as a React Aria radio group's arrows do.

- `PanelFocusOrder.Appended`: a host panel's elements follow the declaring tree's own panel in Tab order, and
  the hosts follow each other in the order each came to hold an appended portal.

- `Hooks.UseFocusManager`, React Aria's `useFocusManager`: `FocusNext`, `FocusPrevious`, `FocusFirst` and
  `FocusLast` move focus among the elements of the focus scope around the calling component, with
  `FocusManagerOptions`' `From`, `Tabbable`, `Wrap` and `Accept`.

- `Deferred<T>`, `V.Await`, `Hooks.UseAsyncValue` and `Hooks.UseAsyncError`: React Router's deferred loader
  data, `<Await>`, `useAsyncValue` and `useAsyncError`. A loader returns a `Deferred<T>` inside its data so
  the navigation commits without waiting for it, and `V.Await` renders the value once it arrives,
  suspending to the nearest `V.Suspense` until then.

- Route actions, React Router's route `action`, `useSubmit` and `useActionData`: `RouteDefinition.Action`
  (and `V.Route(action:)`), `Hooks.UseSubmit`, `Router.SubmitAsync`, `SubmitOptions` and
  `Hooks.UseActionData`. A `post`, `put`, `patch` or `delete` submission calls the action of the route it
  targets; its result reaches `UseActionData` as soon as the action returns, the loaders run again, and
  the navigation commits with it. An action that throws renders through the nearest `errorElement`, as
  does React Router's 405 for a method no form takes. A `get` submission navigates with its
  `ISearchParams` as the query string. `Hooks.UseNavigation` reports `NavigationLifecycle.Submitting`
  while a submission's guards and action run, and `NavigationState.FormMethod`, `FormAction` and
  `FormData` describe the submission in flight.

- `VelvetTask.Preserve()` and `VelvetTask<T>.Preserve()` consume a task once and return one that any
  number of awaiters may await and read, as .NET's ValueTask.Preserve() does; `AsTask()` returns a `Task` or
  `Task<T>` instead. Both settle on the thread that completes the original task. Any other task carrying a
  source, a `VelvetTaskCompletionSource`'s aside, still allows one consume, the rule a ValueTask carries.

- A `VelvetTask<T>` converts implicitly to a `VelvetTask` sharing its source, as a `Task<T>` is a
  `Task`, so members of different result types combine in one `VelvetTask.WhenAll` and are read from
  their preserved tasks afterwards.

- `await task.SuppressThrowing()` waits for a `VelvetTask` or `VelvetTask<T>` and throws neither its
  fault nor its cancellation, as ConfigureAwaitOptions.SuppressThrowing does for a `Task`, returning
  the task's `VelvetTaskStatus` in place of the result.

- `VelvetTask.WhenAll` keeps every member's fault in argument order, those of a faulted combination
  among its members included, as `Task.WhenAll` does. Awaiting it still throws the first;
  `AsTask()` hands all of them to the `Task`'s `Exception`.

- `V.Particles` takes a VFX Graph `VisualEffect`: the element clones it into a hidden host, renders the clone with a
  camera of its own into a texture the element's size on its tick, and draws that texture over its box. The clone
  stands on a layer the project has not named, where one is left, and its renderers are forced off for every other
  camera.

- `VelvetPreviewSmokeTest.Run` mounts every preview story with its default args and its setups onto a
  runtime panel, runs pending passive effects and the renders and transitions they schedule over 100 rounds,
  and reports each story whose build, mount or
  setup threw, or during which an exception no error boundary in the story caught was logged, as Storybook's
  test runner smoke-tests a story. **Window ▸ Velvet ▸ Run Preview Smoke Test** logs the failures, and
  `-executeMethod Velvet.Editor.Preview.VelvetPreviewSmokeTestCommand.RunAndExit` runs it in a batch-mode
  editor and exits 1 when a story fails or none is found.

- The preview window's toolbar takes the game's `PanelSettings`, and the window renders a story at the scale a
  runtime panel on those settings takes for the viewport's screen size, or for their target texture's size
  when they have one. The window used to render at the editor panel's own scale only.

- `VelvetPreviewPanel` builds a runtime panel that renders into a texture from a copy of a `PanelSettings`,
  with Velvet's utility stylesheet attached to its root, and `Resize` gives it a texture of another size. The
  preview window shows a story through one, so a capture harness that mounts a story without an explicit size
  on one built from the same settings at the window's screen size reads the window's pixels for it.
  `VelvetPreviewSmokeTest` mounts each story on one as well.

- VEL012 reports a field or property of `this` that the `_Impl` of an instance `[MemoizeMethod]` method reads
  while it runs and the memo does not key on: any but a `static`, `const` or `readonly` field and a `static`,
  init-only or get-only auto-property, on a class or an interface. A read inside a lambda or local function
  taken to run after `_Impl` returns, such as an event handler, is not reported. The cached node was served
  after such a member changed, with nothing reported. Pass the value to the method as a parameter.

- **Project Settings ▸ Velvet** lets a project leave the utility stylesheet's holder, or any of the four
  bundled shaders, out of its player builds. Both were carried into every build with no way to exclude
  them short of deleting them from the package. A player built without the holder throws from
  `VelvetStyleUtilities.Sheet`, naming the setting; a paint whose shader was left out and reaches the
  player by no other route draws nothing and logs the missing shader once. The choice is saved in
  the project's ProjectSettings folder and read from there on every build.

- `QueryOptions.NetworkMode` and `QueryClientOptions.NetworkMode` (`QueryClient.DefaultNetworkMode`), TanStack
  Query v5's `networkMode` for queries, with the paused state it produces: `QueryResult.FetchStatus` and
  `IsPaused`, `QueryFetchStatus.Paused`, `Query.FetchStatus`, `QueryState<T>.FetchStatus` and the
  `QueryProperties.FetchStatus` and `IsPaused` flags for `NotifyOnChangeProps`.
- `QueryOptions.QueryKeyHashFn` and `QueryClientOptions.QueryKeyHashFn` (`QueryClient.DefaultQueryKeyHashFn`),
  TanStack Query v5's `queryKeyHashFn`: keys the function maps to one string name one entry.

- The translate utilities that write both axes: `translate-<n>`, `-translate-<n>`, `translate-px`, `translate-full`,
  the fractions (`translate-1/2`) and `translate-[…]`. Each axis holds against `translate-x-*` / `translate-y-*`
  by importance, variant and Tailwind's order, an axis utility of the same rank holding over it.
  A `V.Motion` Spring or Bezier play drives both translate channels from the pixel forms, as it drives
  `translate-x-*` and `translate-y-*`; the percent forms land with the swap.

- `StyleTransitionConfig.Layout`: the transition a `layoutId` move takes in place of the Motion's own,
  Framer's `transition.layout`.

- `MountOptions.OnUncaughtError`, React's `onUncaughtError` root option: called with the error and an
  `ErrorInfo` carrying the component stack once the tree has unmounted for an error no boundary caught. Without
  it the error is logged with `Debug.LogException`, as before, and in the Editor and a development build a warning
  naming the component that threw follows it, as React's development build warns.

- `V.Portal(target, …)` takes the element itself, the way `createPortal` does — no registration, no
  shared name, and an element from a `refCallback` is a valid container. Passing a different target on a
  later render moves the children — an unmount and a remount, so their state does not survive. The
  portals guide states which containers a portal of either form may target.

- `MutateOptions.CancellationToken` is the token a call's `MutationFn` receives, for a caller that wants to
  cancel a request: only the caller cancels it. A function that honours it ends in the
  `OperationCanceledException`, which fails the call like any other.

- A `MutationCache` on each `QueryClient`, TanStack Query's: every call a `Hooks.UseMutation` handle makes is
  a `Mutation` in it, with a `MutationState`, found through `Find` / `FindAll` / `GetAll` and `MutationFilters`
  (`MutationKey`, `Exact`, `Status`, `Predicate`), and removed `GcTime` after no handle shows it, or later
  while it is still pending. The cache's `OnMutate`, `OnSuccess`, `OnError` and `OnSettled` run for every mutation before
  the options' own, and `Subscribe` hears mutations added, updated, observed and removed. Hand one in through
  `QueryClientOptions.MutationCache`; `QueryClient.GetMutationCache` and `QueryClient.IsMutating` read it.
- `MutationKey`, `Scope`, `Meta` and `GcTime` on every mutation option record, through their new base
  `MutationOptionsBase`. Mutations sharing a `MutationScope` id run one after another; `Meta` reaches the cache
  callbacks as `Mutation.Meta`; a `MutationKey` replaced by an unequal one between commits leaves the handle
  idle.
- `Hooks.UseMutationState` and `Hooks.UseIsMutating`, TanStack Query's `useMutationState` and `useIsMutating`.
- Mutation defaults: `QueryClient.SetMutationDefaults`, `GetMutationDefaults` and `DefaultMutationOptions`, and
  `QueryClientOptions.Mutations`, filling what a mutation's options leave null, a `MutationFn` included.
- `MutationOptionsBase.ThrowOnError` (`true` or `ThrowOnErrorRule.When(...)`) throws the failure a
  `Hooks.UseMutation` handle shows to the nearest error boundary.
- `QueryClient.ResumePausedMutations` and `MutationCache.ResumePausedMutations`, and `MutationCacheEvent.Observer`.

- A `QueryCache` on each `QueryClient`, TanStack Query's: every entry is a `Query` in it, found through `Find` /
  `FindAll` / `GetAll`, or by its `Query.QueryHash` through `Get`, and removed with `Remove` / `Clear`.
  `Subscribe` hears entries added, removed and updated (with the `QueryActionType` that changed them), and
  mounted queries added, removed, their results updated and their options updated. The cache's `OnSuccess`,
  `OnError` and `OnSettled` run as a request lands or fails. Hand one in through `QueryClientOptions.QueryCache`;
  `QueryClient.GetQueryCache` reads it.
- `QueryOptions.Meta`, v5's `meta`, carried by the entry as `Query.Meta`, and a `QueryOptions` constructor
  whose query function takes a `QueryFunctionContext` (client, key, meta, token), v5's `QueryFunctionContext`.
  A request is cancelled with its last reader only once its function has read
  `QueryFunctionContext.CancellationToken`.
- `Hooks.UseIsFetching` and `QueryClient.IsFetching`, TanStack Query's `useIsFetching` and `isFetching`.
- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries`, TanStack Query's `useQueries` and `useSuspenseQueries`,
  with and without a `combine`: two to four queries as their own arguments, each keeping its data types, or a
  list built at run time, each query keeping its observer while some query of the list reads its entry.
- `Hooks.UseInfiniteQuery` and `Hooks.UseSuspenseInfiniteQuery`, TanStack Query's `useInfiniteQuery` and
  `useSuspenseInfiniteQuery`, over `InfiniteQueryOptions` (`QueryFn`, `InitialPageParam`, `GetNextPageParam`,
  `GetPreviousPageParam`, `MaxPages`) and an entry holding `InfiniteData`. The `InfiniteQueryResult` adds
  `FetchNextPage`, `FetchPreviousPage`, `HasNextPage`, `HasPreviousPage`, `IsFetchingNextPage`,
  `IsFetchingPreviousPage`, `IsFetchNextPageError` and `IsFetchPreviousPageError`, each a `QueryProperties` flag
  for `NotifyOnChangeProps`. A refetch fetches the pages again from the first, as many as the entry holds.
  `QueryClient.FetchInfiniteQuery`, `PrefetchInfiniteQuery` and `EnsureInfiniteQueryData` fill the cache ahead
  of it.
- `Query.State`, a `QueryState` snapshot with v5's `QueryState` fields (`DataUpdateCount`, `ErrorUpdatedAt`,
  `ErrorUpdateCount`, `FetchFailureCount`, `FetchFailureReason`, `FetchDirection` among them), and `Query.Options`,
  `SetState`, `Fetch`, `Cancel`, `Reset` and `Invalidate`, v5's query methods.
- `QueryResult.IsRefetching` and `IsRefetchError`, v5's `isRefetching` and `isRefetchError`, with their
  `QueryProperties` flags.

- A custom property a stylesheet declares in a Tailwind v4 theme namespace (`--color-brand`, `--radius-pill`,
  `--text-*`, `--font-*`, `--font-weight-*`, `--tracking-*`, `--leading-*`, `--shadow-*`, `--inset-shadow-*`,
  `--drop-shadow-*`, `--blur-*`, `--aspect-*`, `--spacing`, `--spacing-*`, `--container-*`, `--breakpoint-*`,
  `--animate-*`) generates the utilities of that namespace: `bg-brand`, `border-b-brand`, `ring-brand/50`,
  `rounded-pill`, `3xl:`, `@wide:`. `--color-*: initial` and `--*: initial` remove the bundled utilities of a
  namespace. The bundled semantic colours take the families the palette names, so `border-b-primary`, `ring-primary`
  and `bg-primary/50` resolve, and `max-md:`, `min-3xl:`, `max-[600px]:` and `min-[600px]:` are variants ranked as
  Tailwind orders them. See `Documentation~/styling-colors.md`.

- `V.Motion(enterOnMount:)` and `MotionNode.EnterOnMount`.

- A leading `-` negates a bracket `z-*` value as it does a bare one (`-z-[5]` is -5), as Tailwind v4 emits it.

- `order-*` (`order-3`, `-order-2`, `order-[5]`, `order-first`, `order-last`, `order-none`, and a custom
  property through `order-[var(--o)]` or `order-(--o)`) lays a flex child out at its place in flex order and
  paints it there, Tab order staying declared order.
- `z-*` and `order-*` behind a variant (`hover:z-10`, `md:order-first`) and a position utility behind one
  (`md:absolute`) restack the element while the variant holds.

- `drop-shadow-<color>`, `drop-shadow-[<color>]`, `drop-shadow-current` and a `/N` modifier on a size or a
  colour, which combine as Tailwind's do. `drop-shadow-[…]` takes a comma-separated list of shadows.

- `text-pretty`, which sets the wrap mode the way `text-balance` does and avoids a last line holding a
  single short word, on Chromium's rule: when that word is narrower than a third of the line, a word
  from the line above moves down to join it, without adding a line.

- `Hooks.UseDraggable`, `Hooks.UseDroppable`, `Hooks.UseDndContext` and `Hooks.UseDndMonitor`, dnd-kit's
  hooks: `DraggableState`, `DroppableState` and `DndContextState` re-render their component as the drag
  changes, and a `DndMonitorListener` hears every drag event. `onDragMove` and `DragMoveArgs` carry every move.
- Sensors: `DndSensor`, `DndSensorActivation` and `DndSensorSession` for a custom sensor, the built-in
  `DndPointerSensor` and `DndKeyboardSensor` (with `DndKeyboardCodes` and a `DndKeyboardCoordinateGetter`),
  and `DndSensors.Default`.
- `DndAccessibility` and `DndAnnouncements`, to replace the announcements or send them elsewhere.
- Modifiers: `DndModifier`, `DndModifierArgs`, and `DndModifiers.RestrictToVerticalAxis`,
  `RestrictToHorizontalAxis`, `RestrictToParentElement`, `RestrictToWindowEdges`,
  `RestrictToFirstScrollableAncestor`, `SnapCenterToCursor` and `CreateSnapModifier`, on a scope and on a
  `V.DragOverlay`.
- `DndAutoScroll` and `DndDropAnimation`.
- The sortable preset: `V.SortableContext`, `V.Sortable`, `Hooks.UseSortable`, the strategies in
  `DndSortingStrategies`, `DndSortable.ArrayMove`, `ArraySwap` and `KeyboardCoordinates`, and
  `DraggableInfo.Sortable` / `DroppableInfo.Sortable`.
- `DndCollisionQuery.HasPointer`, false for a drag no pointer drives; `DndCollisions.PointerWithin` collides
  with nothing then.

- `FiberElementProps.NoDrag`: a press that starts on the element or inside it never arms a draggable
  enclosing it.

- An edit in the preview Controls addon hands the story a new args instance, with a copy of each nested
  object, struct, array or list on the path to the edited member, as Storybook replaces args rather than
  mutating them. The addon used to mutate one args instance in place.

- The preview Controls addon edits `long` and `double` members, an `int` or `float` carrying `[Range]` as a
  slider, a `[Flags]` enum as a flags field, a `DateTime` as text, a `UnityEngine.Object` member as an asset
  picker, and a one-dimensional array, a `List<T>` or a class or struct with public writable members as a
  foldout of controls for its elements or members, built when first expanded, with a Length field for an
  array or list, a **Set object** button for a null one, and a note where a value refers back to an object
  above it. An enum of up to five values is a radio group. An `int` or `float`
  carrying `[Range]` used to be a plain number field, every enum a dropdown, and each of the other types a
  read-only note.

- The `mauve`, `olive`, `mist` and `taupe` palette hues, in the eleven shades the others have.

- `RouteDefinition.Index` and `V.Route(index: true)` make an index route, as React Router's `index: true`
  does: it matches its parent's own path, scores above a route of the same path, and is the route a bare
  `?index` in a submission's query targets. A tree that gives an index route a child list, an empty one
  included, throws `ArgumentException`, and so does `V.Route` given both.
- `SubmitOptions.EncType` is React Router's `encType`. `application/json` passes the body to the action as
  JSON, parsing a string body; `text/plain` sends it as text; a `get` takes neither and commits "Unable to
  encode submission body". Any value other than those and the default sends the form as
  `multipart/form-data`.
- `RouteActionContext.Request` is React Router's `request`: a `RouteRequest` whose `FormData()`, `Json()` and
  `Text()` read the body once, in the encoding it was submitted with, beside its `Method`, `Url` and
  `ContentType`. `FormData` is the web's `FormData`, entries in the order they were appended.
- `NavigationState.FormEncType`, `NavigationState.Json` and `NavigationState.Text` report the submission in
  flight as React Router's `navigation` does.

- `RouteLoaderContext.Request` is React Router's loader `request`: a GET `RouteRequest` with no body whose
  `Url` is the location's path and query resolved against `http://localhost` and serialized as the URL
  constructor serializes it, as memory history's createURL makes it.
- `ErrorResponse.Internal`, React Router's `internal`, is true on the responses the router makes.
- `SearchParams.Set`, `Delete`, `Sort` and `ToString`, and `ISearchParams.Values`, as URLSearchParams has them.

- `RouterOptions`, taken by `new Router(routes, scopeFactory, options)`, holds React Router's
  `createMemoryRouter` `initialEntries` and `initialIndex`; `Router.Initialized` is its `state.initialized`;
  `RouteDefinition.HydrateFallbackElement` and `V.Route(hydrateFallbackElement:)` are its
  `hydrateFallbackElement`.

- `ease-[cubic-bezier(x1,y1,x2,y2)]`, `ease-<name>` for a `--ease-<name>` token a project declares, and the CSS easing
  keywords as an `ease-[…]` value run a transition along the curve, as Tailwind v4's `ease-*` utilities do. UI Toolkit's
  `transition-timing-function` names only an `EasingMode` keyword, so the element is left easing by the keyword nearest
  the curve and Velvet has the transition the engine runs ease along the curve once it is running. Duration, delay and
  the reversal of an interrupted transition stay the engine's, per property, and `transition-filter`'s tween follows the
  same curve.
- `steps()`, `step-start` and `step-end` run as steps in `ease-[…]` and in a `--ease-*` token.
- A custom property holding `cubic-bezier()` or `steps()` in a USS sheet is imported as the text it was written with,
  as one holding `rem` or a math function is.

- `VelvetPreviewPanel.Render()` draws one frame into the panel's `Texture` before it returns. It first runs
  `VelvetPreviewPanel.SettlePasses` update passes over the runtime panels drawing into the texture, each a simulated
  frame that moves the panel's clock on by `VelvetPreviewPanel.SettlePassMilliseconds`, and the clock stays moved
  afterwards. Work those panels time by their own clock advances as over that many frames, so the renders, effects
  and state updates Velvet schedules there are drawn, so is what Velvet measures from their layout, and a transition
  timed by that clock that ends within the passes is drawn finished. Motion that reads its mount's
  `MotionClock` rather than the panel's time moves only as far as that clock has, and work the story resumes on a
  later editor or player frame is drawn by a `Render()` after that frame. The panels are then repainted and
  rendered, and an exception the frame does not catch reaches the caller as it was thrown. The headless screenshot capture the preview guide describes calls it
  between mounting a story and reading the texture back; a mount alone does not draw its story into the texture in its
  own frame.

### Changed

- The loader runner allocates fewer rounds. It opened one at the start of every run that the run's own
  round replaced within the same call, and one more at disposal; it now opens one per run and none at
  disposal. A Back or Forward the history cache serves runs no loaders; the round it promotes instead is
  the one round that path allocated before.

- The switches that classify a `StyleVariantKind` are exhaustive by compilation rather than by review:
  `Runtime/csc.rsp` compiles CS8509 as an error, so a member added without an arm fails the build rather
  than warning into a log that nothing gates on. That response file ships with the package, so a project
  compiling `Velvet.asmdef` compiles CS8509 as an error too, applying to Velvet's own sources only.

- A deferred host mount that cannot resolve where it goes is now reported and skipped rather than
  escaping the render. `V.Portal`, `V.WorldSpace` and a `z-*` placement share one queue drained after
  the pass, and an escape used to take everything still behind it: a portal on a named layer queued
  behind the failing one never mounted and its later patches warned that the *named* layer's host was
  missing, and a `z-*` element queued behind it never entered the tree at all. An escape also left the
  committed tree of the fiber that began the pass short of what the pass had already put in the DOM,
  so each later render appended another copy of the difference. A `z-*` placement resolves no target
  and stays outside that containment, unchanged: it performs its own insert, so a throw from the
  panel-attach callbacks a `V.Custom<T>` subclass registered on the element being landed escapes the
  render pass rather than being reported by the drain, and the entries queued behind it are still
  lost.

- The switch behind the layer offset now names every layer, for the reason the `StyleVariantKind` entry
  above gives, and so do the ones behind `divide-*` colours, `clip-path` radius keywords, `animate-*`
  transition slots and the structural variants — each already answered every named member as it does now.

- `V.Portal(layer:)` refuses a `UILayer` value naming no member of the enum at construction, with an
  `ArgumentOutOfRangeException` that names the parameter and carries the value. 2.1.4 answered such a
  value with a host of its own at the `Overlay` sorting offset, without a word.

- `StyleVariantClass.BreakpointPx` and `StyleVariantClass.IsResponsive` throw for a `StyleVariantKind`
  value naming no member of the enum, where they returned `0f` and `false`. Both have done so since
  2.0.1.

- A key holding a NUL (U+0000) is refused wherever one reaches a VNode, where `V.Fragment` and the
  two `V.ListFragment` overloads were the only factories refusing one. NUL is the delimiter the
  reconciler's scope chains are composed from, so a key carrying it split into more segments than
  one and let two different tree positions compose to the same scope string — where they then
  reconciled as one identity. Measured on a `V.Label` keyed `"a\0b"` sitting beside a `V.Fragment`
  keyed `"a"` that holds a `V.Label` keyed `"b"`, both under one keyed Fragment: the two labels
  compose the same scope, and a re-render left the second label's element in the first label's
  slot, a fresh element in the second's, and the first label's own element nowhere in the tree.
  `V.Provider`, `V.Suspense`, `V.AnimatePresence`, `V.Component`, `V.MemoizedWithKey`,
  `V.DragOverlay`, every element factory's `key:`, a `V.List` selector's key and a `V.VirtualList`
  selector's key all reached that delimiter and none of them refused it. The refusal now sits on
  `VNode.Key`, which each of them but the `V.VirtualList` selector assigns through, so a call passing
  such a key throws `ArgumentException` naming `key` where it used to build a node. A factory that
  takes from a pool before building its node refuses ahead of that rent, so a refusal strands nothing
  that factory rented; `V.List`, whose selector key is not known until an item is mapped, gives the
  child array it rented back instead. A `V.VirtualList` `keySelector`'s key is compared only with the
  list's other keys and never becomes a `VNode.Key`, so an item whose key holds the delimiter renders like
  any other.

- A node a component keeps in `Hooks.UseState`, the element-in-state pattern, stays out of the VNode pool while a
  queued write carries it, the result of an updater already computed against it is queued, or the state a skipped
  write is replayed over is that node, as it does while the state itself holds it. A write that a render skips is
  replayed by a later render, which emits the node it carries.

- VEL101 reads a try and a catch as React's lint does. A hook in a try block is reported only where the try has a
  catch and something that may throw runs before the hook in it (a call, an object creation, a member or element
  access, an await or a throw); a hook in a catch is reported unless its try cannot complete normally. A hook in
  a foreach's collection or a for's initializer, which run once, is not reported, and a hook on the right of
  `??=` or after `?.` is.
- VEL101 is no longer reported for a hook VEL102 reports, matching React's lint, which asks the conditional
  checks only of a component or a custom hook.
- VEL101 no longer reports a hook in a lambda handed to `V.Component` or `V.Memo` as its render body, and still
  reports one inside a condition there.
- VEL101 reports a hook in any other lambda only where the lambda sits inside a component or a custom hook, as
  React's lint reports a hook in a callback. A hook in a lambda inside a plain helper or a field initializer is no
  longer reported.
- A call on a member counts as a hook call only where the receiver is a single name starting with an uppercase
  letter, as in React's lint, or a qualified name that binds to a namespace or a type, as in
  Velvet.Hooks.UseState(...). A method named like a hook called on a local, a parameter, a field, a property or
  `this`, such as svc.UseDefaults() or cfg.Logger.UseDefaults(), is an ordinary call. VEL101 previously reported
  such a call inside a condition.
- A hook in an if's condition, a conditional expression's condition, or the left operand of `&&`, `||` or `??`
  is no longer reported by VEL101: it runs before the branch is chosen, and React's lint reports none of them.
- A name of `Use` followed by a digit, such as Use2D, is a hook's, as React's lint reads `use` followed by a digit.
- A lambda held by a variable, or assigned to a name, of a hook's name is a custom hook: VEL101 no longer reports
  a hook in it as a nested lambda.

- The migration guide now states where an `absolute` element with no offset on an axis sits in a `flex` or
  `flex-col` container: at the static position css-flexbox gives a flex container's child, across the content
  edges and along the main axis as its sole flex item, so `justify-*` and `items-*` place it. A `grid`
  container is not claimed. Layout is unchanged.

- The error for a component rendering more or fewer store hooks than before names the kind
  `UseStore / UseSyncExternalStore`, since the two hooks share one slot list.

- A `KeyDownBinding` or `KeyUpBinding` on a text-input element now runs on the element's trickle-down
  pass rather than its bubble pass, so it runs before a callback the element or a child registered for
  the bubble pass, and stopping propagation there keeps the key from the input. See Fixed below for
  what the move repairs.

- Reusing a pooled `Button` keeps its `Clickable` and clears its click handlers and pressed state in place,
  where it used to replace the manipulator and pay a callback unregister and register for each reuse.

- Automatic memoization of a `[Component]` body now caches the parts of the body on their own as well as the
  body as a whole. A call that builds a `VNode`, with the code that builds its arguments, is keyed on only the
  values it reads, so a render in which one state changed builds again only the labels and subtrees that read it
  and hands the reconciler the rest by instance. A props record read through its fields or automatic properties
  is keyed member by member. A part that takes the result of a call the build cannot see, of a hook outside the
  weaver's allow-lists, or of a stable object a call may have changed is built on every render and its siblings
  are not, and neither is a child of `V.Portal` or `V.WorldSpace`. The memoization guide states what keeps a part
  from being cached.
- `Hooks.ReserveScopes`, `Hooks.TryGetScope` and `Hooks.StoreScope`, the runtime half of the above, which the
  build-time transform calls and nothing else should.
- A call the build cannot see (an interface or virtual call, or one into an assembly it cannot load) ahead of the
  last hook, a hook outside the allow-lists, and a hook result used in a shape the weaver cannot key on no longer
  leave the whole component without memoization: the body as a whole is not keyed, and the parts that read none
  of them still are. A whole tuple a hook returned and the body reads one element at a time is keyed on those
  elements.

- `StyleVariantClass.BreakpointPx` and `StyleVariantClass.IsResponsive` refuse a `StyleVariantKind` value naming
  no member with an `ArgumentOutOfRangeException` naming the parameter, where they threw the exception a switch
  expression raises for a value none of its arms matches.

- `VelvetFilters.Register` rejects a name holding `(` or `)` and the name `none`, with a warning. A
  `filter-[...]` body of that shape is CSS filter syntax (`filter-[blur(4px)]`, `filter-[url(#x)]`,
  `filter-[none]`), which stays an inert class beside the rest of the chain rather than naming a missing filter.

- A `[VelvetPreviewSetup]` that throws is logged as an error line naming the setup, followed by the exception
  itself as an exception entry. It used to be one error line carrying the exception's text.

- The preview window mounts a story on a runtime panel built from the chosen `PanelSettings` and shows the
  texture it renders, sending the pointer, wheel, keyboard and navigation events the canvas receives on to
  it. It used to mount the story on the window's own editor panel, so the editor's theme, font and scale
  applied there and the pixels it showed could differ from what a runtime panel on the game's settings
  renders. With no `PanelSettings` chosen the panel has an empty theme at a scale of one. A story's
  `sm:`…`2xl:` breakpoints read the panel, which is the simulated screen, or a story's explicit footprint
  when it has one; the window no longer marks the canvas with `VelvetResponsive.ViewportClass`. Zoom scales
  the rendered texture rather than re-rendering at the zoomed size, and Measure reports the panel's units.

- A player build hands managed code stripping a link.xml that keeps the engine members Velvet reaches by
  name: the focus state it clears from a panel an element leaves, the focus pseudo-state, the composite-root
  flag, UI Toolkit's internal property-change event, a stylesheet's `@import`s, and the cached opacity and
  transition lists used by `layoutId` crossfades. The file is generated during
  the build; a `link.xml` in the package would not be read.

- A render that calls `Hooks.UseEffect`, `Hooks.UseLayoutEffect`, `Hooks.UseInsertionEffect`,
  `Hooks.UseCallback`, `Hooks.UseMemo`, `Hooks.UseRef`, `Hooks.UseMutableRef`,
  `Hooks.UseImperativeHandle`, `Hooks.UseId`, `Hooks.UseDeferredValue`, `Hooks.UseOptimistic`,
  `Hooks.UseMutation`, `Hooks.UseTransition` or `Hooks.UseBlocker` a different number of times from
  the previous render now throws the hook-count `InvalidOperationException` `Hooks.UseState` throws, in
  every build, and reaches an enclosing error boundary. It logged an error in the editor alone and
  committed the render, or, for `Hooks.UseMemo`, `Hooks.UseRef`, `Hooks.UseMutableRef` and
  `Hooks.UseTransition`, was not checked. A hook built from them, such as `Hooks.UseFrame` or
  `Hooks.UseNavigate`, throws the same way, and the message names the inner hook — `UseCallback` for
  `Hooks.UseNavigate`.

- `LoaderMode.Await`, which is the default, awaits the loader. The route already on screen stays there,
  `Hooks.UseNavigation().State` reports `NavigationLifecycle.Loading`, and the location commits with the
  data — React Router's plain `loader` contract. It previously accepted only a loader that handed back
  an already-completed task: anything else was rejected with a framework-authored
  `InvalidOperationException` recorded as that route's loader error, so the naive port of a React loader
  navigated and then rendered the route's `errorElement`, or a blank subtree where the matched chain had
  none. A `LoaderMode.Suspend` loader of the route already on screen keeps running for the whole of that
  window — it keeps its cancellation token, and its result still reaches `Hooks.UseLoaderData` on the
  route the user is looking at — because the route is not left until the commit, and the commit is what
  cancels it. Two consequences to read before upgrading. An `Await` loader that never completes is now a
  navigation that never commits, where it used to fail fast. And a navigation whose loaders suspend
  leaves `NavigateAsync` genuinely asynchronous, so a caller that took its result synchronously has to
  await it. A navigation whose loaders all hand back already-completed tasks still completes
  synchronously.

- **Minimum supported Unity raised to 6000.3.23f1**, from 6000.3.11f1 — `unity` stays `6000.3` and
  `unityRelease` moves to `23f1`, so a project on an earlier release of the 6.3 line updates its editor
  before taking this version. Several of the UI Toolkit fixes Unity shipped between the two land on
  shapes Velvet itself ships: a `border-color` transition not running when the starting colour is
  transparent, which the bundled `transition-*` utilities covering `border-color` ask for over
  `border-transparent` or an alpha-zeroed border colour (6000.3.13f1); a programmatic scroll on a
  `ScrollView` overriding manual scroll input on every frame after it is requested, when any element
  in the panel updates its style every frame (6000.3.18f1); opacity not propagating in filtered
  elements, under the `blur-*`, `grayscale-*` and sibling utilities that compose one inline `filter`
  list, and `background-image` rendering solid white after the referenced asset's Addressables
  content was unloaded and reloaded, which is what `bg-[addr:…]` sets (both 6000.3.21f1); and an
  `ArgumentException` during world-space UI rendering — where `V.WorldSpace` mounts — when the
  internal draw range list grew while partially full (6000.3.23f1).

- The container a `V.Component` is written into is part of which instance it is, as the position of a
  component is in React. Two sibling containers each holding the same component now hold two instances
  with their own state, where they used to share one: the shared instance rendered its output into
  whichever container the reconcile reached last, so the other container held a copy that no later
  render ever updated, and a control inside that copy drove the surviving instance's state. Giving the
  two occurrences the same `key:` shared one instance too; a key now makes no difference between
  containers, because it separates siblings of one container rather than one container from another.
  The consequence to read before upgrading is the other direction: writing a component into a
  **different** container than the previous render did is now a fresh mount there and an unmount of
  the one it left, so its state, refs and effects do not travel. The entry on moving a component across
  a `V.Portal`'s boundary states the same of that boundary, which holds even where the two sides share a
  container, so neither rule subsumes the other. Keeping state across such a move means lifting it above
  both containers, to a `Store` or to a `UseState` in the component that declares them. The migration
  guide states what a position is.

- A `V.Memoized`'s dependency-cache entry belongs to where the memo is written: the component whose
  output holds it, the element its output lands in, a `V.Portal` it sits under within that output, and
  its slot there — its index among the siblings it is written beside, or its `key:`, which stands in for
  that index and nothing above it, under the chain of `V.Fragment`s, `V.Provider`s and other wrappers
  that enclose it inside that element and that output. The entry used to be keyed by a position string,
  which the first child of any two containers spelled alike, or by the memo's `key:` alone, wherever in
  the tree the key was written. So a sidebar and a main panel that each memoized their first section
  shared one entry, and so did two `V.ListFragment`s whose ids overlap: where the dependency arrays
  agreed, the memo reached second never ran its factory and rendered the first one's subtree, with
  nothing logged; where they differed, it handed the first one's subtree back to the pool while that
  subtree was still on screen. An entry is now dropped when Velvet tears down its component, its element
  or its `V.Portal`, where it used to stay for the reconciler's life.
  The consequence to read before upgrading: a memo's factory runs again wherever that slot is new,
  including places the old key reused the cached subtree from — another element, whether or not the
  memo carries a `key:`, a `V.Fragment` newly wrapped around it, or a sibling inserted ahead of an
  unkeyed wrapper that encloses it. A factory with side effects runs them again there, and the nodes it
  builds replace the cached ones. The migration guide states what a position is.

- A `UseTransition` transition now covers what its callback runs before that callback first suspends,
  rather than being inferred from the action still being in flight on the calling component. An update
  made after an `await` that suspended the action takes the lane it would have taken outside a
  transition, and an action that wants it deferred wraps it in the starter again, which joins the
  transition already running and keeps `isPending` lit. React's `startTransition` reference asks for the
  same wrapping, calling the restriction a known limitation it means to fix rather than the shape it is
  aiming for. That inference had no way to tell the action's own continuation from a timer tick, a
  `UseStore` notification or a `UseMutation` callback landing in the same window, so those took the
  Transition lane too and waited out the delayed tier's 100 ms instead of committing at the next frame
  boundary. `isPending` itself is unchanged: it
  stays true across the awaits until the task completes. One consequence to expect where the await does
  suspend: an action writing both before and after it now leaves two lanes rather than one, so those
  writes commit in two renders instead of coalescing into a single transition render.
  Which of the two an `await` is, C# decides at run time. An `await` of a task that had **already
  completed** does not suspend — the continuation runs inline — so the callback carries on inside the
  scope and the write after that `await` is a transition too, `isPending` staying lit until it commits on
  the delayed tier. `await VelvetTask.CompletedTask` reaches it, as does any `async VelvetTask` the action
  awaits that returns without suspending — a cache answering from memory being the shape to expect. One
  source line therefore takes either schedule depending on the data, and the counter-intuitive way round:
  the cache hit that answered instantly is the one whose write waits out the delayed tier, where the miss
  commits at the next frame boundary. Wrapping the post-`await` update in the starter makes the two paths
  agree, since a joined call is a transition on both. The migration guide's `useTransition` row states the
  rule and where React's behaviour stops being a guide to it.

- Moving a component across a `V.Portal`'s boundary is an unmount and a remount in every case now, so
  its state, refs and effects do not survive the move and the departing instance's cleanups run. A
  component written into a live portal's children that a previous render had outside them — and the same
  move back out — used to keep its instance whenever a patch of the portal's children had been the last
  thing to register the portal-side occurrence, the render that closes the portal included; only an
  occurrence the portal's own deferred mount had registered mounted fresh. Which of the two a given edit
  got was decided by which of those two entrances had last put the child in the portal, and neither the
  guide nor `createPortal` promised the surviving half. The portals guide states the contract that now
  holds in both directions. Keeping state across such a move means lifting it above the portal — to a
  `Store`, or to a `UseState` in the component that declares the portal.

- `V.Portal(null)` no longer compiles: a bare `null` first argument is ambiguous between
  `Portal(string, …)` and the `Portal(VisualElement, …)` overload this version adds. Naming the
  parameter — `V.Portal(targetId: null)` — or casting the literal says which was meant.

- `V.Portal(layer:)` no longer mounts its children for a `UILayer` value naming no layer, where it
  hosted them at the `Overlay` offset before. Every named layer hosts as it did, and only a cast outside
  the enum's range reaches this. What such a cast now produces: that portal's children do not mount, and
  the exception is reported to the console rather than raised out of the render — `UseFallback` does not
  catch it, and the message names the unmatched number rather than the argument it came from. A silent
  `Overlay` was how such a cast survived to put a portal on a layer nobody asked for.

- Six more `V.*` factory arguments are refused when they name no member of their enum, as
  `V.Portal(layer:)`'s already is: `focusOrder:` on `V.Portal` and on `V.WorldSpace`, `playOn:` on
  `V.Particles`, `movement:` on `V.Draggable`, `mode:` on `V.AnimatePresence` and `loaderMode:` on
  `V.Route`. Each throws `ArgumentOutOfRangeException` from the call itself, naming the parameter and
  carrying the value; every named member is accepted as it was, and only a value naming none of them
  reaches this. Such a value used to behave as `Isolated` for `focusOrder:`, `Manual` for `playOn:`,
  `None` for `movement:`, `Sync` for `mode:` and `Suspend` for `loaderMode:`, without a word — which is
  how a cast could put a route's loader on the background path nobody asked for. A caller wanting one
  of those behaviours names its member.

- A `[Component(Memoize = true)]` component's props bail decides a props value whole, the way React's
  shallow-equal comparison decides each key of a props object with `Object.is`. The member walk runs on
  the props bag and on nothing it finds: a props value that is not a bag — a value type, a string, a
  collection — is decided whole rather than through a member set, and a value type has the `float` and
  `double` fields it carries — directly, or inside a value type it holds — compared by raw bit pattern,
  as a `float` member already was.
  Before, a bare `decimal` props value compared equal to a different one — `1.0m` against `2.0m` — and
  so did two distinct `Guid`s, so a price or an identifier driven by that prop alone never changed. A bare
  `new List<int> { 1, 2 }` compared equal to a bare `new List<int> { 3, 4 }`, both holding two elements.
  And a `float` or a `double` inside a record struct props member bailed on a sign flip, `0f` becoming `-0f`,
  where the same flip in a bare `float` member re-renders.
  **A component handed a `List` prop of equal contents now re-renders where it used to bail.** A
  collection passed as the props value itself is decided by its instance — the reference check
  `Object.is` performs on an object, and what a reference-typed props member already got here — so a
  list the render builds afresh is a change however its elements compare. Holding that list in a
  `Hooks.UseMemo`, a `Store` or a field is what gives the bail back, and passing it as a member of a
  props record is unchanged.
  A bare value type is now decided the way a value-type member is, which runs the other way
  where a bare struct holds a record class: one of equal content bails where it used to re-render, the
  member walk having read that record by its instance.

- A `V.Motion(variants:)` entry carries a transition of its own. The map is
  `IReadOnlyDictionary<string, MotionVariant>` now — a `MotionVariant` being the utility-class string a
  label already named plus an optional `StyleTransitionConfig` for swaps INTO that pose — and the
  Motion's own `transition:` is the default such a pose overrides, still the `Fade` preset where the
  call site leaves it out. **Every caller that builds a variants map has to retype it**, from
  `Dictionary<string, string>` to `Dictionary<string, MotionVariant>`, and so does every field,
  parameter or props type one is handed around as; the entries themselves need no edit, because a
  `string` converts to a `MotionVariant` implicitly, and every `initial:` / `animate:` / `exit:` label
  reads as it did. Code reading `MotionNode.Variants` back gets `MotionVariant` values, whose
  `ClassName` is the string the map used to hold.
  What this buys is timing a single per-node config could not express: 0.5s in against 0.05s out on one
  Motion, and an `exit` whose duration is its own rather than the enter's. Whether a removal is held as
  an exiting ghost is decided by the same resolved config, so `transition: StyleTransitionConfig.None`
  beside a timed exit pose animates that pose instead of dropping the child instantly — a shape 2.1.4
  had no way to ask for. The child-orchestration knobs are read off that config too, so a coordinator's
  pose can carry `StaggerChildrenSec` and a `When = BeforeChildren` wait is measured from that pose's
  own span. The motion guide states which pose each play reads.

- An `exit:` label naming a pose that applies no class plays a variant exit: the removal takes the
  resting pose's classes off, on the timing that pose resolves — its own `MotionVariant.Transition`,
  else the Motion's `transition:`, the same choice a mount enter and a label change into a pose make.
  It played the classic exit instead — on the Motion's own `transition:` and with that transition's own
  exit classes, the `Fade` preset's where the call site left `transition:` out — so a variants map whose
  exit entry is `""` or `null` now removes the child differently. Where the resting pose applies no
  class either, as on a label coordinator whose poses only orchestrate its children, the exit changes
  nothing on the coordinator itself, which is held until that timing and its inheriting children's
  exits have finished and then removed, where the classic exit used to play the transition's exit
  classes on it.
  An `exit:` label the map has no pose for still plays the classic exit.

- `V.Outlet()` emits no element of its own: the matched route's own output takes the Outlet's position
  in the parent's child list, and an Outlet whose location matches no route at its depth takes no
  position there at all. It used to emit a layout-passthrough container — absolutely positioned and
  inset to its parent's box, carrying the `velvet-outlet` class — and mount the route inside that. An
  absolutely positioned child is out of its parent's flow, so the route it held was out of it too: a
  chrome of `flex flex-col` with a header above a `V.Outlet()` drew the header and the route's first
  row both at the top of the container, and a `gap-*`, `divide-*`, `[&>*]:` or grid parent placed its
  remaining children as though the route were not among them. React Router's `Outlet` is an ordinary
  function component
  returning the matched element wrapped in context providers, and this is that shape: `V.Outlet()`
  returns a `ComponentNode`, `RouteOutlet` is the body it names, and `Hooks.UseOutletContext` reads
  the value from a Provider that body renders rather than from a push around the mount. What a caller
  has to edit around: the declared return type, and a USS rule or an element query written against
  the container or its class, which are gone. `FiberOutletScope` holds the route scope against the
  Outlet's own fiber, which is what the container element used to key, and
  `IRouteScopeFactory.CreateScope` runs inside the Outlet's render where it used to run in the commit
  that built or patched that container.

- A `[Component]` that takes props and calls no hook is woven by the compiler transform now, keyed on
  its parameters alone. The weaver used to decline any body without a hook call, so such a component —
  the presentational leaf that only formats its props — ran in full, and was diffed again, each time its
  parent's render reached it. A render whose parameters each compare equal, under the rule
  `MemoNode.Dependencies` states, to those the cached tree was built from returns that tree instead of
  running the body. What a working application can notice is what that rule does not compare, which
  reaches the tree on no such render: a change made inside a parameter it compares by instance, a
  difference it leaves to a parameter's own `Equals` and that `Equals` ignores, and anything else the body
  reads — a static field, a store read directly rather than through `Hooks.UseStore`, `Time`. Anything
  else the body does, such as counting its own renders or logging, is skipped on such a render.
  `ComponentAttribute.Compiler` states the rule, and `[Component(Compiler = false)]` restores the
  previous behaviour for one component. A parent building a fresh record class instance on every render
  gets a rebuild on every render, one allocation dearer than before — the deps array — and a value-type
  parameter adds its box to that. One with `Memoize = true` is left unwoven because its props bail already
  suppresses equal-prop parent renders. Separately, a return the compiler reaches by a branch jumping
  straight to it, which a returned `??` or conditional expression can compile to, now stages the tree it
  returns, in a component with hooks as in one without: that path used to skip the commit, so a render
  repeating its inputs rebuilt the body there instead of reusing the tree. `Hooks.TryGetMemoizedVNode`,
  the gate the transform emits, takes the calling method's handle as its first argument. A woven component
  called as a plain method runs uncached; its gate reports a miss with slot `-1`, which
  `Hooks.StoreMemoizedVNode` takes as nothing to stage.

- `animate-pulse` and `animate-spin` start from their element's own value, as a CSS animation fills the
  keyframes it leaves unnamed. `opacity-75 animate-pulse` runs between 0.75 and 0.5 rather than between 1
  and 0.5, and `rotate-45 animate-spin` turns on from 45 degrees rather than from 0. The own value is the
  element's class value, a named one or an arbitrary one, re-read when its class list, its pointer, focus or
  press state, enabled state, the theme or an ancestor's classes change; an element with no opacity or rotation class runs as
  before. A Motion `Spring` or `Bezier` frame in a slot the play drives is still written over rather than taken
  for the element's own. `animate-spin` on an element with an uneven `scale` turns the squash axes with the
  element, where CSS leaves them.

- A field control's `className` background (`bg-*`, gradients included), border (`border`, `border-*`, and the
  `border-solid` / `-dashed` / `-dotted` line style), radius (`rounded`, `rounded-*`), padding (`p-*`),
  `shadow-*`, `ring-*` and `outline-*` utilities now paint the box the value is shown in, as the same classes do
  on an `<input>` or a `<select>`, on `V.TextField`, `V.IntegerField`, `V.DropdownField` and `V.Custom<T>` for a
  text-input or popup field type. They used to land on the outer control and leave the theme's own background
  and border on the box. The state, theme, responsive and relational variants written on them go with it, and
  `peer-*` / `group-*` still find their source from the control. A field that relied on the outer control
  carrying them (a padded, bordered or shadowed frame around a labelled field) now has them on the box and
  nothing on the frame. Layout and size utilities stay on the outer control.

- A `bg-radial-[…]` bracket whose comma-separated body reads as a CSS stop list, such as
  `bg-radial-[at_top_left,red,blue]`, now draws those stops, followed by any `from-` / `via-` / `to-` stops on
  the same element. It drew the `from-` / `via-` / `to-` gradient alone and read the bracket only for a centre.
- A gradient is laid out over the box's real proportions, as CSS lays it out. A diagonal angle
  (`bg-linear-45`) and a conic were drawn as if the box were square; in a box that is not square the
  diagonal line is now as long as the box makes it and a conic sweeps true angles. Under `animate-gradient`
  they are laid out over the twice-as-large background the pan slides across. A radial gradient off
  the middle of the box is now the farthest-corner ellipse CSS draws: its rings were stretched from a
  circle before, which was the same only for a centred one.
- A stop fading to `transparent` no longer darkens on the way. Colours interpolate with their alpha, as CSS
  interpolates them, so `from-red-500 to-transparent` stays red while it fades, where it passed through
  dark red before.
- `from-` / `via-` / `to-` positions are fixed up as CSS fixes them: a position behind an earlier stop is
  raised to it, so `from-60%` with `via-` at its 50% default puts via at 60%. Where two stops share a
  position the later one starts there, so `from-0% via-0%` begins on the via colour rather than the from
  colour.
- A bare `from-` / `via-` / `to-` position is a whole, unsigned percentage, as in Tailwind: `from--50%` and
  `from-12.5%` no longer move the stop, and are written `from-[-50%]` and `from-[12.5%]`.
- A `bg-radial-[…]` bracket is read as CSS reads it. One whose body is not a shape, size and position (an
  unknown token such as `bg-radial-[at_top_bogus]`, an ellipse with one radius) used to ignore what it did
  not know and draw a centred or positioned radial; it now leaves the class inert, as an invalid CSS gradient
  paints nothing. A radial's rings also run on past its radius where the box does not end, instead of
  holding the last colour at the radius, and a position may lie outside the box.
- The `/oklch` modifier interpolates along the OKLCH hue arc, as CSS does. It interpolated in OKLab before,
  which has no hue arc, so a gradient between hues now takes the arc through the intermediate hues.
- A gradient utility with no modifier (`bg-linear-*`, `bg-gradient-to-*`, `bg-radial`, `bg-conic`) interpolates
  in OKLab, as Tailwind's do, where it interpolated in sRGB: the midpoint of two hues is no longer the
  channel-wise mean, and a fade through black or white is brighter. `/srgb` keeps the old look. A gradient
  written in an arbitrary bracket still interpolates in sRGB.

- A `Label` Velvet creates — a `V.Label`, a `V.Text`, a `V.Custom<Label>` — starts with zero margin and
  padding, as Tailwind v4's preflight gives every element, through the class `velvet-label`. It used to keep
  the margin and padding the panel's theme gives a `Label`, so text centred in a box now sits on the box's
  centre and a layout that counted on that spacing loses it. A `p-*`, `m-*` or `px-[..]` on the label still
  applies. The same class takes CSS's `flex-shrink: 1` and the parent's `white-space`, so a label that a theme
  kept on one line (`nowrap`, `flex-shrink: 0`) now wraps and shrinks, and a `whitespace-nowrap` on an
  ancestor still reaches it; `whitespace-nowrap` and `shrink-0` on the label still apply. A `Label` UI Toolkit builds inside its own control, such as a `V.TextField`'s label, and a
  `Button` keep the theme's spacing; a `Button` Velvet creates takes the parent's `white-space` the same way. The reset the flexbox guide used to suggest for centred labels, which
  also set `flex-shrink: 0`, is no longer needed: the minimum size below replaces that half.

- A `Label` or `Button` Velvet creates cannot shrink below its content along its parent's main axis, as a
  CSS flex item cannot (`min-width: auto` / `min-height: auto`). In a row its minimum width is the widest
  run of text with no break opportunity, so a long label beside a fixed-width sibling still wraps, down to
  that run, and keeps the whole text under `whitespace-nowrap`; in a column its minimum height is the height
  the text takes at the width it was given, so a label in a box shorter than its text overflows instead of
  being squeezed. A declared `w-*` / `h-*` caps the minimum at that size and a `max-w-*` / `max-h-*` at the
  maximum. An item that is clipped (`overflow-hidden`, `truncate`), declares its own `min-w-*` / `min-h-*`
  on the axis, sits in a `grid-cols-*` column or has children keeps its cascade's value.

- An error that an element's `onCreated` or `wrapElement` throws while the element is created is a render error,
  as these callbacks are reads the element's render takes: an error boundary the same render is rendering at or
  above the element catches it, and the rest of that render commits around the fallback, as React commits the
  siblings of a boundary whose children threw while rendering. Rows the render adds, drops or moves beside the
  boundary, and the siblings after it, take that render's content, and nothing of the failed subtree commits. The
  catch used to abort that render, which left some of those rows as the previous render had committed them: a
  sibling after the boundary, a row the render added, dropped or moved, or the row of a child that an
  `AnimatePresence` after the boundary was removing, which stayed on screen. A boundary catching such an error as
  it mounted showed neither its fallback nor a sibling of the failing element.

- An error boundary catches the error of an element it renders itself, from the element's `onCreated`,
  `wrapElement` or `refCallback`, as React captures an element's error from the element's parent. The search used
  to start above that boundary. An element of the boundary's own fallback still leaves its error to the boundary
  above, where React's capture would offer it to the same boundary: that boundary would mount the same fallback
  again.

- An `AnimatePresence`'s `onExitComplete` for children removed with no exit to play runs from the passive-effect
  flush after the render that removed them, after that commit's layout effects, and no longer while that render
  is still going on. It does not run for a removal that render never committed. One whose exit plays runs when
  the last exit's animation ends. An error it throws goes to the nearest error boundary at or above the component
  rendering the presence, as Framer Motion's `PresenceChild` calls it from an effect. Where an exit played, or the
  child whose exit completed last holds a `V.Motion`, no boundary takes the error and it is logged, as Framer Motion
  calls it from that exit's promise. It used to go to the nearest boundary above the component rendering the
  presence, and a render removing a child with no exit animation stopped at that catch.

- An error a `V.Motion`'s `onEnterComplete` throws reaches no error boundary and is logged, as Framer Motion's
  `onAnimationComplete` runs in a promise. It used to go to the nearest boundary above the component rendering the
  Motion.

- An error a `V.Outlet`'s route-scope factory throws is a render error of the Outlet, as the Outlet's render reads the
  factory: an error boundary the same render is rendering at or above the Outlet catches it, and the rest of that
  render commits around the fallback. It used to stop that render at the catch, which left the rows after the
  boundary as the previous render had committed them.

- `Hooks.UseState` and `Hooks.UseReducer` queue their updates per hook, each with the lane it was written on, and
  a render folds in only the updates its lane renders, as React's update queues do. Urgent and Normal render
  together, so only a Transition update waits for a Transition-lane render. A write made inside `startTransition`
  is no longer on screen before the transition commits: an Urgent or Normal render of the component in between,
  and a pass of an ancestor that re-renders it, shows the state from before the write, and the Transition-lane
  render that follows applies it. A component an ancestor's non-transition pass re-renders keeps its queued
  transition writes and its `isPending` lit until that Transition-lane render commits them, where the ancestor's
  pass used to commit them and settle the transition. Code that read a transition's write from a render outside
  the transition, or that took such a pass for the transition's commit, now sees the earlier state until the
  Transition lane drains.
- A render outside any flush, such as the retry of a resolved `Hooks.Use` resource, no longer folds in a
  transition's queued writes.
- A functional updater (`setValue.Invoke(prev => next)`) written while another update of the same hook is
  scheduled runs in the render that applies it, over the state the updates before it produce, where it ran at
  the call. One written while none is scheduled still runs at the call, and a render that folds it replays the
  result rather than running it again. A replayed write that ran at the render runs again.
- A `Hooks.UseReducer` dispatch no longer runs the reducer at the call, and renders the component even when the
  result equals the state, as React's `dispatch` does. The reducer of the render that folds the dispatch runs it,
  so a reducer rebuilt by each render, such as a lambda closing over a value the component reads, decides the
  state with the values that render holds.

- Calling the `startTransition` function `Hooks.UseTransition` returns renders the declaring component with
  `isPending` true at once, on the Urgent lane and ahead of the transition's own render, as React does through
  the sync-lane optimistic update it dispatches before running the callback. The writes the callback queued
  stay on the Transition lane, so that render shows the state from before them, and the render that commits
  them follows. Before, nothing rendered purely because `isPending` turned true: a component showing a pending
  branch saw it only in a render something else produced, and otherwise in the render that committed the
  transition, so the indicator now appears earlier and a transition that leaves the flag lit costs the
  component one more render. A synchronous callback that queues nothing still settles as it returns and renders
  nothing.
- The passive effects (`Hooks.UseEffect`) of a component that a time-sliced transition pass has rendered wait for
  the commit that completes the pass. They ran between slices, before the transition committed, where React
  runs no passive effect for a render that has not committed.

- A Motion `Spring` or `Bezier` variant swap now animates the font-size presets (`text-sm` → `text-2xl`), the
  tracking presets (`tracking-tight` → `tracking-wide`), `rounded-full` and the `-full` lengths (`w-full`,
  `size-full`, `left-full` and the rest of the sizing, position and `basis-` families, as 100%), which landed
  instantly before. A swap that names one of them now runs a play where it used to apply the classes at once,
  so its completion callbacks and exit waits last as long as the play does.
- A percentage translate (`translate-x-1/2`, `translate-x-full`, `translate-1/2`, `translate-x-[50%]`) is a
  channel of a Spring or Bezier play, in percent of the element's own box, where it used to apply with the
  swap. An element that rests at `-translate-x-1/2` and plays a swap on its other axis keeps that percentage
  for the play; it was written back in pixels before.
- `scale-x-*` and `scale-y-*` are scale channels of a Spring or Bezier play, each axis read as the element
  draws it (`scale-50 scale-x-75` is 0.75 by 0.5), where a swap naming one used to apply with the swap.
- Semantic colours (`bg-primary`, `text-subtle`, `border-default`), `aspect-[…]`, `aspect-square` and
  `aspect-video`, and pixel-to-percentage pairs of offsets, padding and margin are driven where they used to land
  instantly.
- A Spring or Bezier play takes only the longhands it writes out of the element's own USS transitions, where it
  suspended every transition on the element until it settled; the element's other transitions keep running, and
  a variant turning on a transition class mid-play is picked up. A Tween on a clock other than the panel's still
  lands everything its swap changes.
- A Bezier play reads `PropertyOverrides` (each entry's delay, duration and easing for the channels it names) and
  an exit can run its own curve through the new `StyleTransitionConfig.ExitBezier`; a Bezier config carrying
  overrides ignored them before. A sequence step's hold counts a Bezier override that outlasts the play.
- An important utility on an element with an `animate-*` loop (`!opacity-50 animate-pulse`) holds its slot against
  the loop, where the loop overwrote it on every tick.
- A slot only the leaving pose names, or one the entering pose leaves `-auto`, is driven toward what the element
  shows there when it already carries the pose it plays into.
- A Spring play reads `PropertyOverrides` (the channels an entry names run a tween of its delay, duration and
  easing) and an exit can run its own physics through the new `StyleTransitionConfig.ExitSpring`; a Spring config
  carrying overrides ignored them before.

- A rotated `V.Motion(layoutId:)` inside another layoutId Motion that stretches by different factors on its two
  axes is drawn undistorted, as Framer Motion draws it: the counter-scale sits in a wrapper element outside the
  Motion's own rotate. That Motion's parent in the visual tree is the wrapper; its `name`, ref and styles stay on
  the Motion.

- A `V.Motion(layoutId:)` whose last holder of an id has left the tree leaves its box under that id until
  another Motion takes it, and a Motion that mounts under the id in a later batch tweens from that box, as
  Framer Motion's shared-layout stack keeps the snapshot of its last lead. It used to appear in place unless
  it mounted in the same batch as the one that left.

- A literal `[&>*]:` token on a control that redirects nothing, such as `V.TextField`, `V.Button` or a slider,
  no longer lands on the control's own parts (its label, its `#unity-text-input`). `& > *` selects the children
  a tree placed, and such a control has none. Put the utility on the control; a field routes its surface
  utilities to its input box.

- `skew-*` on an element with no other source of `rotate` or `scale` is drawn as the element's own transform, so the
  face, the border and the descendants shear together, `origin-*` moves the pivot, and a hidden overflow no longer
  cuts the sheared face's overhang. The element no longer carries the painted silhouette, the descendant seats or the
  suppressed native face; a pointer picks the bounding rectangle of the sheared box.

- `DndCollisionDetection` returns a ranked `DndCollision[]` (`Id`, `Data`, `Value`) instead of a single
  `string?`, as dnd-kit's `CollisionDetection` returns a ranked `Collision[]`. The first entry becomes the
  over target and an empty array means no collision. A custom strategy returns
  `new[] { new DndCollision(id, data, value) }` where it returned `id`, and `Array.Empty<DndCollision>()` where
  it returned `null`.
- The built-in strategies rank as dnd-kit's do. `DndCollisions.RectIntersection` ranks by the intersection
  ratio `intersection / (droppable + active - intersection)` rounded to four decimals, greatest first, where
  it ranked by the overlap area. `DndCollisions.PointerWithin` ranks the droppables containing the pointer,
  edges included, by the mean distance from the pointer to their four corners, smallest first, where it picked
  the smallest area, and a pointer on a droppable's right or bottom edge now collides. `DndCollisions.ClosestCenter` ranks by
  center distance. Equal values keep registration order. A drag whose active rect overlaps a large and a small
  droppable, or whose pointer sits in nested droppables, can now report a different `over` target.
- `DragOverArgs` and `DragEndArgs` take a further `Collisions` argument, the strategy's full ranking at the
  latest move, as dnd-kit's drag events carry `collisions`. Code constructing either record passes it; code
  reading the events needs no change.

- A `get` submission through `Router.SubmitAsync` or the function `Hooks.UseSubmit` returns reads its form
  data as React Router's URLSearchParams constructor does: a string, a dictionary, a sequence of pairs or an
  object's declared members become the query string, strings, dictionaries and pair sequences in the order
  written. A body that cannot be encoded, including one that throws while it is read, commits "Unable to
  encode submission body" as the leaf route's error and runs no action. Only a `get` encodes its body. It no
  longer throws `ArgumentException` for form data that is not an `ISearchParams`; a caller that caught the
  exception reads `Router.CurrentLoaderErrors` instead.

- `AnimationSequenceControls.Pause` holds the `Spring` and `Bezier` plays, and a `Tween` a non-panel `MotionClock` drives, a sequence's steps started, where it
  froze only the cursor and let them run on; `Play` resumes them. A play is the sequence's when its `V.Motion`
  is handed `AnimationSequenceState.CurrentTransition`, or takes its label from one that is, the mount enter
  of a child that mounts there included. A play a step starts while the sequence is paused, such as step 0's
  under `autoplay: false`, now holds at its start until `Play`. A `Tween` on the default clock is UI Toolkit's own transition and still runs on to its end.
- `AnimationSequenceState.CurrentTransition` is a copy of the step's transition, equal to it in every setting,
  rather than the instance the step was given: a comparison by reference against the step's config no longer
  holds.
- A `Spring` play on a sequence is sampled at its time rather than stepped frame by frame, so a frame longer
  than 1/30 s no longer slows it, and a heavily overdamped spring reaches the cap Framer Motion's spring
  generator puts on its hyperbolic argument (300) and lands on its target there, as Framer's does, where
  stepping never reached it.
- After `Cancel`, `Play` or `Restart` starts each new `Spring`, `Bezier` or driven `Tween` play from the values the cancel left
  on the channels its classes name, rather than from its pose's own, and an exit starts from them too; the held
  values no new play takes over come off once the reseed commits.

- A plain utility Velvet resolves inline (a bracket value, a fraction, `auto`, a negated step, a colour with an
  opacity, a logical side) is ordered against the bundled classes that write one of its longhands as Tailwind emits
  them, where the inline one always won: the one Tailwind emits later holds it, whichever is written first.
  `w-px w-[10px]` and `w-[10px] w-px` give 1px (they gave 10px), `w-1/2 w-4` gives 16px (it gave 50%),
  `mt-[10px] mt-auto` and `-mt-2 mt-1` leave the margin to the class, `bg-[#ff0000] bg-red-500` gives red,
  `border-s-2 border-l-4` gives a 4px left border (it gave 2px), `rounded-lg rounded-[2px]` gives `rounded-lg` (it
  gave 2px) and `text-lg/7 text-sm` gives the `text-sm` size (it gave the slash size). Where the inline token is the
  one emitted later it still wins (`w-4 w-[10px]` gives 10px, `scale-100 -scale-[0.5]` the inline scale). An inline
  token that the later classes write in full is no longer written; the class stays on the element. One they write in
  part keeps the rest: `border-[3px] border-l-4` gives the left border 4px and the other sides 3px, where the
  bracket gave all four 3px. Variants and the `!` modifier are unchanged.
- The bundled classes that carry Velvet's own name for a Tailwind utility are declared where Tailwind's name sorts:
  `rotate-n6` and the other `rotate-n<N>` classes ahead of `rotate-0`, as `-rotate-6` is emitted ahead of `rotate-6`, and
  `duration-fast`, `duration-normal` and `duration-slow` beside `duration-100`, `duration-150` and `duration-200`.
  `rotate-6 rotate-n6` and `rotate-n6 rotate-6` now give 6 degrees (they gave -6), and the same ranking applies against
  an inline utility (`rotate-[10deg] rotate-n6` keeps the inline rotation).

- The opacity modifier on `bg-` / `text-` / `border-` multiplies the colour's own alpha instead of replacing
  it, as Tailwind v4's `color-mix(in oklab, <colour> N%, transparent)` does. Only a colour that already has
  alpha changes: `bg-[#ff000080]/50` is now a quarter opaque where it was half, and `bg-transparent/50`, which
  resolved to black at half alpha, now stays fully transparent, as it does in Tailwind. `bg-red-500/50` and
  `bg-[#ff0000]/50` are unchanged.

- A commit attaches each `refCallback:` as it reaches the element in the walk React's layout phase makes,
  rather than all of them ahead of its first layout effect. An element's ref attaches after everything inside
  it and after the layout effects of the components below it, and a component's layout effects run after the
  refs of the elements it rendered. A `Hooks.UseLayoutEffect` on mount therefore no longer finds an ancestor
  element's ref set, nor a later sibling's, where it used to find both; it still finds the refs of its own
  component's elements, of its descendants' and of its earlier siblings'. A layout effect that needs an
  element above it reads it from an effect (`Hooks.UseEffect`) or from a layout effect of the component that
  rendered the element.
- The layout cleanups of a commit run after the refs it replaces detach and before the new ones attach, where
  the new refs used to attach first.
- The refs of every component a batched drain re-renders attach at the end of the drain, interleaved with the
  drain's layout effects, where each component's refs used to attach as its own pass ended. A render that reads
  a `Ref<T>` another component in the same drain sets now finds the previous commit's value, as a render does in
  React.
- The refs of a `V.Portal`'s elements attach where the portal sits among its neighbours, not after every ref
  of the pass that rendered it.

- Two base utilities that both resolve inline (a bracket value, a fraction, a negated step, a colour with an
  opacity, a logical side, a per-side border colour) and write the same property are ordered as Tailwind emits them,
  where the one written last used to win: `w-[2px] w-[1px]` gives 2px (it gave 1px), `w-[10px] w-1/2` gives 10px (it
  gave 50%), `bg-red-500/50 bg-[#ff0000]` gives the half-transparent red (it gave `#ff0000`),
  `border-b-red-500 border-b-[#0000ff]` leaves the bottom border red, and the same holds for `!w-[2px] !w-[1px]`,
  the transforms (`scale-[2] scale-[1.5]`, `translate-x-[8px] translate-x-[4px]`), `opacity-[.7] opacity-[.3]`, the
  filters (`blur-[4px] blur-[2px]`) and the other properties an inline token writes. The earlier token is no longer
  written, and taking the later one away brings it back. A custom filter is ordered per registered name.
- An inline border width that later inline tokens write in part is cut down to the sides they leave:
  `border-t-[4px] border-[3px]` gives the top 4px and the other sides 3px, where the bracket gave all four 3px.
- A stylesheet class written inline beside a narrower inline utility is ordered by the name it was written with, not
  by its inline form: `size-[10px] w-[10px] w-4` gives a 10px width (it gave 16px), `pt-[10px] pt-4 p-[3px]` gives a
  10px top padding (it gave 16px), and `pt-4 pt-2 p-[3px]` gives a 16px one (it gave 8px). The same applies behind a
  variant and under `!`.
- A slash text size (`text-[14px]/6`) beside a plain font size (`text-[16px]`) gives the plain size's font size and
  the slash token's line height in either order, as Tailwind emits the slash form first: `text-[16px] text-[14px]/6`
  gives 16px (it gave 14px).
- The utilities a C# resolver folds from the whole class list are put in the order Tailwind emits them, so the one
  Tailwind emits last wins whichever is written first, where the last token written used to: the font facets
  (`font-light font-bold` and `font-bold font-light` give light, `not-italic italic` gives not italic,
  `font-[700] font-[600]` gives 700), the text effects (`uppercase lowercase` gives upper case, `underline overline`
  underline, `text-pretty text-balance` pretty, `leading-6 leading-4` the 6 step), `z-*`, `gap-*` / `space-*`,
  `grid-cols-*`, `divide-*`, `shadow-*` (`shadow-sm shadow-lg` gives the small shadow), `ring-*` (an `outline-*`
  outranks it: `ring-0 outline-2` gives a 2px outline), `clip-path-*`, `skew-*`,
  the gradient shape and stops (a keyword or integer `bg-linear-*` loses to every other shape: `bg-linear-to-r bg-conic`
  gives conic), `pointer-events-*`, `bg-[addr:…]`, `border-solid` and the other line styles, and `animate-*`
  (`animate-spin animate-none` spins). A class added to an element in a later render takes the place a fresh mount
  gives it, so a clip written as `clip-path-[inset(10%)]` and re-rendered with `clip-path-[circle(40%)]` added
  resolves as it does when mounted that way. The nine-slice
  insets, which Tailwind has no utility for, follow the sort key: `slice-t-[2] slice-y-[8]` gives the top 2 and the
  bottom 8, where the top was 8.

- An element that holds a portal's target without being a logical ancestor of the portal's call site, the element
  the portal renders into included, no longer runs its `events:` handlers for an event raised in the portal's
  content, in either the capture or the bubble phase, whichever reconciler rendered the element, as the container
  of a React portal never sees them. They used to run through the engine's own dispatch. A handler on a portal
  container that relied on hearing its content's pointer, wheel, key, focus, value-change or click events now has to
  sit on a logical ancestor of the `V.Portal` call site instead. A `Button`'s `onClick` runs unless the press and
  the release were both in that content, as a browser raises a click on the common ancestor of the two. An element that is both a physical and a logical
  ancestor still runs once in each phase, except for a pointer-down or pointer-up that a physical-only `Button`
  between the content and it consumes.

- A stylesheet of your own that declares a class named like a spacing step Velvet now resolves (`.w-100`,
  `.p-30`, `.top-13`) is shadowed by the inline value that class name now produces. Rename the rule, or write the
  value as a bracket class and drop the name.

- A shorthand and a longhand of one family in one `className` (`pt-[2px] p-[8px]`) no longer leave the
  shorthand's value on the side the longhand names: the longhand holds it, as in Tailwind, and a stylesheet class
  that takes part in such a pair leaves the element's class list.

- `@container` no longer re-points `sm:`…`2xl:`. Those prefixes measure the viewport (the panel root, or for a
  portal's children the panel the portal was declared on) whether or not an `@container` ancestor is around them, as
  in Tailwind v4. A component that answered the width of its `@container` through `md:` writes `@md:`-style container
  variants instead; the container scale starts lower than the viewport's, so `@3xl:` (768 px), `@5xl:` (1024 px)
  and `@7xl:` (1280 px) are the names that sit where `md:`, `lg:` and `xl:` did, and `@min-[640px]:` or
  `@min-[1536px]:` stands in for `sm:` and `2xl:`. The marker does nothing without a container variant beneath it.
- Tooling that applied `VelvetResponsive.ContainerClass` to simulate a viewport, as the preview window's Viewport
  addon did, applies the new `VelvetResponsive.ViewportClass` instead, which makes `sm:`…`2xl:` follow an element's
  width the way `@container` used to.

- Unmounting the component of a `Hooks.UseMutation` call in flight no longer cancels the `CancellationToken`
  its `MutationFn` received, as TanStack Query v5 never cancels a mutation: the function runs to its own
  completion, and `OnSuccess`, `OnError` and `OnSettled` receive its real outcome instead of an
  `OperationCanceledException`. A write the server applied is no longer reported as failed, so a rollback in
  `OnError` that relied on the cancellation to find unmounted calls no longer sees them. `MutateAsync`
  resolves or rejects with the function's own outcome, and a `MutateAsync` called after the unmount runs
  instead of rejecting with an `OperationCanceledException`, writing no handle.

- Two bundled utilities that a stylesheet class resolves, and that write the same property, are ordered as Tailwind
  emits them rather than by where the stylesheet declared them: the one Tailwind emits later wins, whichever is written first. `w-px w-4` and
  `w-4 w-px` both give 1px (they gave 16px), `p-1 p-px` gives 1px, `bg-red-500 bg-blue-500` gives red (it gave
  blue), `text-sm text-lg` gives the small size, `items-center items-start` gives start, `flex-wrap flex-nowrap`
  wraps, `rounded-md rounded-lg` gives `rounded-md`, `transition-colors transition-opacity` transitions opacity,
  and `transition-none` follows every other `transition-*`. `truncate` now yields to `overflow-*`, `whitespace-*`
  and `text-ellipsis` where it used to outrank them. The `anim-*` presets keep their place after the base
  utilities, since the animation scheduler applies them to outrank those. Two utilities that both resolve inline in C# are
  not part of this: the font facets (`font-light font-bold`, `not-italic italic`) still go to the last class written.
  A label with `truncate overflow-visible` (or `overflow-hidden md:overflow-visible` above `md`) is no longer treated as
  clipped by the text item's automatic minimum size.
- The bundled partials were rearranged to declare that order: the semantic colours (`bg-primary`, `text-subtle`,
  `border-default`, …) are in `_palette.uss` beside the palette colours of their property, `truncate` is in
  `_layout.uss`, the `transition-*` rules are in `_effects.uss`, and the `font-*` weight rules are one rule each.
  `StyleUtilities.uss` imports the same partials as before.
- The gap polyfill, the shadow's corner radius and the ranking of several `text-<size>` classes on one element follow
  the same order, so they agree with the box: `flex-wrap flex-nowrap gap-4` takes the wrap path, `rounded-sm rounded-lg`
  casts the 2px shadow, and the size whose font size shows is the one Tailwind emits last.

- Each `text-xs`…`text-9xl` now sets Tailwind v4's paired line height with its font size, so text under one is
  laid out on the paired line height (`text-sm` is 1.4286 times its size, `text-5xl` and up 1). A
  `leading-*` on the same element still wins whatever the class order or `!`, and a nearer `text-<size>` replaces
  an ancestor's `leading-*`.
- The text a `Label` or `Button` displays now carries the `<line-height=…>` tag a `leading-*` already wrote, so
  code that reads it back off the element sees the tag, and a label with `enableRichText = false` shows the markup
  as literal text, the caveat `fonts.md` already gives for `leading-*`.
- `text-<size>/<leading>` (`text-sm/6`, `text-[14px]/6`, `hover:text-sm/6`) sets the size's font size and the matching
  `leading-*` line height, and `text-[length:14px]` reads as `text-[14px]`.

- Two `rounded-*` utilities that write different properties and share a corner (`rounded-t-[2px] rounded-r-[8px]`, `rounded-l-sm rounded-b-lg`,
  `rounded-r rounded-t-lg`) no longer leave the corner to whichever is written last or declared last in the
  stylesheet: the one Tailwind emits later holds it, and a stylesheet class that takes part in such a pair leaves
  the element's class list.

- A render of the children of a `V.Suspense` that shows its fallback over children it had shown is now an attempt,
  as React's attempt at a primary that suspends again is. The bodies run, but the render queues no insertion, layout
  or passive effect and commits no `UseEffect`, `UseLayoutEffect`, `UseInsertionEffect` or `UseBlocker` deps, so the
  reveal compares against what the last render a commit showed left. Every component such a render reached renders
  again for the reveal, so a component can render once more than it did before. Code that relied on a hidden
  render's deps being kept, such as an effect that ran at the reveal because its deps changed and changed back, no
  longer sees that. A Suspense that has not shown its children yet is unchanged.

- `Hooks.UseNavigate` returns a `NavigateFunction` in place of `Func<string, VelvetTask<NavigationResult>>`, and takes
  no `replace` argument: React Router's `navigate(to, options)` has none. Replace a `Hooks.UseNavigate(true)` call with
  `Hooks.UseNavigate()` and pass `new NavigateOptions { Replace = true }` to each call that replaces. A delegate
  field or variable typed `Func<string, VelvetTask<NavigationResult>>` becomes `NavigateFunction`. A call such as
  `navigate("/path")` is unchanged.

- `RouterLocation.Path` no longer contains a `#fragment`. A target such as `/docs#intro` used to match no route; it
  now matches `/docs` and the fragment is `RouterLocation.Hash`.

- `Router.NavigateAsync(path, NavigationMode.Back)` and `NavigationMode.Forward` commit the location of the entry they
  step onto and no longer read `path`; before, the path the caller passed was committed on a stepped history index.
  Pass a delta to `Router.NavigateAsync(int)` to move along the history.

- `Hooks.UseOptimistic` ties each optimistic entry to the transition whose `startTransition` callback added
  it, and discards the entry when that transition settles, or when no `async` action is left in flight (below): whether or not the pass-through
  state changed, and whether the action succeeded or faulted. An entry no render has folded yet when its
  transition settles is disowned instead, so the render `addOptimistic` requested still shows it, and the
  component's next Transition-lane render discards it. An entry added outside every
  `startTransition` callback while an `async` action is in flight, that action's own code after an `await`
  included, belongs to the actions in flight together and is discarded once none is left. As React entangles
  its async actions, a transition that settles while any is in flight hands its entries to them as well, and an
  action counts from its start until its task completes, whether or not the component that started it is
  still mounted. An action awaiting a task that never completes therefore holds every later transition's
  entries, its component's unmount included, until the next Play Mode entry; earlier an unmount gave the
  action up. The
  Transition-lane render that lands a transition's last work leaves that transition's entries out, so the
  commit already shows them gone. Each render folds
  the pass-through state through the entries still outstanding, so an entry whose transition is still
  pending is applied over a pass-through state that changed meanwhile instead of being dropped by that
  change, and one transition settling leaves another's entries in place. An entry nothing owns is shown by
  the render `addOptimistic` requests and discarded at the component's next Transition-lane render; added
  outside every `startTransition` callback with no `async` action in flight, it also logs a warning in the
  editor. The migration guide's `useOptimistic` row lists where this differs from React. The override used
  to last until the pass-through state changed, so an action that settled without changing it left the
  optimistic value on screen until something else did. A caller that called `addOptimistic` outside any
  transition, around a `UseMutation` call for instance, calls it inside the `startTransition` callback that
  performs the update, or after an `await` inside that callback's `async` action.

- An element that gets a transition from its styles now transitions a style a `refCallback:` or a
  `Hooks.UseLayoutEffect` writes to it on mount, as a browser does once a layout read has computed the
  element's styles. The commit applies the element's styles before the callback runs, and UI Toolkit starts a
  transition only for an element whose styles it has computed before; the write used to land in the element's
  first computed styles, which start none. A mount that should show the written value at once puts it in the
  element's `className` or `styles:` instead.

- Transition-lane work — an update a `startTransition` callback schedules, and a `UseDeferredValue`
  derivation — no longer waits 100 ms after it was scheduled. It renders in a later panel scheduler pass than
  the urgent render that asked for it; Velvet holds it to a later pass, not to a later frame. A request made
  inside Velvet's render drains, its scheduled `UseEffect` drain, a time-sliced render's resume, the notification
  of a resource that resolved while its reader was rendering, or a `UseFrame` callback, run by the panel the requesting tree is mounted on, renders on the
  next pass. A request made anywhere else — a discrete event handler, including the urgent render its
  synchronous flush commits, another panel's callbacks, Velvet's other scheduled work, or code outside every
  panel — renders on the pass after that, so a search box's `UseDeferredValue` list catches up two passes after the keystroke, and
  `isPending` reads true until then rather than for at least 100 ms. Normal- and Urgent-lane updates still
  queued when a transition drains commit first, in the same pass, and an urgent update to a component whose
  deferred value was queued for that pass moves the deferred value to a later one, up to
  `TransitionStarvationThreshold` (30) moves. A store mutation made inside a transition between the two tiers'
  drains now reaches both its readers by the end of the transition's drain; it previously left the
  transition's readers on the snapshot the earlier drain had pinned until the store changed again.
  [`Documentation~/react-migration.md`](Documentation~/react-migration.md) says when Transition-lane work
  renders.

- A `UseDeferredValue` render on the Transition lane commits the value it is handed rather than only the
  value the urgent render queued. A value built afresh on every render — a new list or record each time —
  never matched the queued one, so it never committed and re-requested a render every time the Transition
  tier drained; it now commits on that drain. A value that changes again between the urgent render and the
  transition render commits the newer one directly instead of deferring once more.

- A `V.Suspense` that has shown its children and suspends again keeps them in the tree, hidden with an
  inline `display: none` ahead of its fallback, as React hides them, and reveals the same elements when it
  stops waiting. The components in them keep their state, a component inside a host element of the children
  or a `V.VirtualList` row among them included, and their passive effects stay connected; their layout
  effects, imperative handles and element refs are taken down until the reveal, as React 18 does, and a
  focused element inside them is blurred. A `V.Portal`'s children are hidden with them, and each hidden element
  gets back the inline `display` it had on the reveal. The components of a Portal a render adds while the
  boundary hides, and of a `V.VirtualList` row the list mounts outside a render of the boundary, still set
  their layout effects and refs up as they mount. It used to remove those elements and dispose every
  component inside a host element of them, so the reveal created them again. A query over the tree, such as
  `Q<Label>()`, now finds the hidden elements while the fallback shows.

- `QueryOptions<T>` derives from the new `QueryOptions<TQueryFnData, TData>`, which declares every option, and
  `QueryProperties.All` includes the new `QueryProperties.IsPlaceholderData`.

- A mounted, enabled `Hooks.UseQuery` whose data is stale fetches again when the device comes back online
  and when the application becomes visible after being hidden, as TanStack Query v5's `refetchOnReconnect`
  and `refetchOnWindowFocus` default to. The default visibility reading is the platform's window state, as v5's
  `document.visibilityState`: a window that has lost focus but is still shown is visible, so a desktop
  application regaining focus does not refetch, and one restored from minimized does. Set
  `QueryClientOptions.RefetchOnWindowFocus` / `RefetchOnReconnect` to `QueryRefetchMode.Never` to stop
  those refetches.
- The same visibility reading now gates `QueryOptions.RefetchInterval`: while the application is not
  visible (minimized or hidden on desktop, backgrounded on mobile), a query with an interval fetches nothing, as v5's does
  while its document is hidden. Set `RefetchIntervalInBackground = true` on the query to keep it polling, or
  `NetworkSignals.IsVisible = () => true` to make the application always count as visible.
- When the last query reading an entry leaves while its request is in flight, the request is cancelled and
  the entry put back as it stood before it, as TanStack Query v5 does for a query function that read its
  `signal`. A query function that reads its token has read it: its token is now cancelled when its last
  reader unmounts, where the request used to run on and land in the entry. Build the options with a function
  that takes no context (`new QueryOptions<T>(key, () => ...)`) to keep a request running after its readers
  leave.

- A `Hooks.UseMutation` call now pauses while the device reads as offline, as TanStack Query's mutations do
  under their default `networkMode: 'online'`: a call started offline stays pending with `IsPaused` true
  and does not call `MutationFn` until a connection is back, where it used to run at once. The reading is
  `Application.internetReachability`, which reports a server on a local network as unreachable on some
  devices; set `NetworkSignals.IsOnline` once at startup to read connectivity another way, or give the
  mutation `Retry = new RetryPolicy { Retry = false, NetworkMode = NetworkMode.Always }` to never wait.

- On Windows and macOS a retry that comes due while the window is minimized (or, on macOS, the application is
  hidden) now waits until it is shown, for every `RetryPolicy`, a `RetryPolicy.RunAsync` around a `Hooks.Use`
  loader included. A window that has merely lost focus does not wait. A mutation started under the default
  `Online` mode from another thread now continues on the main thread, where the connectivity reading is taken.

- A `Hooks.UseMutation` call whose component unmounts while it is in flight runs its callbacks, as TanStack
  Query v5's option callbacks outlive the observer, where it ran none. Neither writes the handle or
  re-renders the component.

- A `[Component]` that calls through an interface, or calls a virtual method an override can replace, is now
  auto-memoized where that call follows every hook call the body makes, and one taking props with no hook at
  all is memoized on its props. Such a component was left unwoven before, so its body ran on every render
  that reached it. A render whose props and hook values compare equal now returns the tree the last miss
  built, so whatever the call reads that is neither a prop nor a hook value no longer reaches the tree on
  that render — the same as for a call to a non-virtual method. A call of this kind made ahead of a hook
  call, directly or inside a custom hook, still leaves the component unwoven, and so does a call whose
  target the build cannot resolve.
- Once a hook runs between the woven gate and the point the body returns — one reached through such a call
  — the component's body runs on every render from then on rather than reusing a tree, where a hit would
  have skipped that hook. `[Component(Compiler = false)]` remains the opt-out.
- A component compiled with optimization, as a player build compiles it, is auto-memoized where its Debug
  build is, except where the body stores a hook value only to discard it (`var r = Hooks.UseRef<T>(); _ = r;`),
  which the optimizing compiler turns into the bare discard the weaver refuses. A hook value read once
  rather than stored in a local, such as `var x = Hooks.UseContext(...)` followed by a single read of `x`,
  among other shapes, left the component unwoven in a player. A hook value passed straight as an argument —
  `V.Label(text: Hooks.UseStore(store, s => s.Name))` — is recognized in either build, where it left the
  component unwoven before.

- `StyleOverrides.BackgroundColor`, `StyleOverrides.Color` and `StyleOverrides.BackgroundImage` now win over a
  utility writing the same property — `bg-[#…]`, `text-[#…]`, `bg-red-500/50`, `bg-[addr:…]` and the gradient
  utilities — whichever was written last, as React's `style` prop wins over `className`; a keyword value
  (`StyleKeyword.Initial`, …) does too. Before, a utility applied after the override — a changed class on a
  patch, a `hover:` or other variant turning on, a gradient baked on mount — replaced it. An important utility
  (`!bg-[#…]`, `!bg-[addr:…]`) wins over the override, and removing the override hands the property back
  to the utility instead of clearing it. `StyleBackgroundImageResolver.Apply` and `Clear` now write the
  utilities' image, which loses to a `StyleOverrides.BackgroundImage` in the same way; under such an override a
  gradient no longer writes `background-size`, and an `animate-gradient` or `animate-shimmer` beside it does not
  pan. The `styling-backgrounds.md` guide states the rule.

- `pointer-events-none` / `pointer-events-auto` already present in markup now change hit testing. Both
  classes were accepted and did nothing before, so markup that already carries them — Tailwind markup
  ported as written, such as `disabled:pointer-events-none` on a button — now lets the pointer through
  the element and its subtree, or takes a descendant back.

- A `V.Slider`'s input is Radix's `Slider`. Home sets its `lowValue` and End its `highValue` whatever the
  direction and inverted flag, where a vertical or inverted slider set by hand from `refCallback:` or
  `onCreated:` used to take Home to its high value. PageUp and PageDown move ten steps and an arrow moves
  one, ten with Shift, with the arrows Radix's for each direction and flag, and an arrow moves the slider
  without Enter being pressed first. The step is the new `step:` parameter, 1 when it is null, and a value a key or a
  drag gives the slider lands on the grid of steps counted from `lowValue:`: a
  slider over 0 to 1 that used to drag continuously declares `step: 0.01` to keep doing so.

- A `SliderInt` mounted through `V.Custom<SliderInt>` or `V.Motion(elementType: typeof(SliderInt))` gets
  `V.SliderInt`'s keyboard, which is `V.Slider`'s. Home sets its `lowValue` and End its `highValue` whatever the
  direction and inverted flag, and PageUp and PageDown move ten steps of 1. An arrow moves one step, ten with Shift,
  with no state to enter first, and keeps focus on the slider, as an `<input type="range">` does. UI Toolkit's own handler, which
  such a slider used before, worked differently:
  - Home went to the high value on a vertical slider that is not inverted and on an inverted horizontal one.
  - PageUp and PageDown left the value where it was at the default `pageSize` of 0.
  - An arrow moved the value only while the slider was in its movable state, which focusing it enters and
    Submit toggles. Out of that state the arrow was left to focus navigation and could move focus away. In it, an arrow
    stepped by the power of ten closest to a hundredth of the range, at least 1.
- Such a slider now comes from the pool `V.SliderInt` uses rather than being built for each mount. A callback
  a `refCallback:` registers on it has to be unregistered by the cleanup that `refCallback:` returns, as on
  every pooled element.

- A `TransitionType.Spring` play now runs and ends as Framer Motion's spring animation does. Each channel is
  sampled from Framer's spring by the time since the play started, and the play ends when its slowest channel's
  spring has run the duration Framer measures for it. An opacity and a background color — the values Framer
  hands to the browser — are measured over a travel of 100 and cut at 20 s, so an opacity on the default spring
  ends at 1.05 s. Any other channel, and any channel under a mirrored or waiting repeat, runs as on Framer's
  main thread, a translate, scale, rotate or length over its own travel and a color over 100, and never ends if
  it has not rested within 20 s. It ended before once every channel came within Velvet's own epsilons, checked
  on every tick. A frame that arrives late now moves a spring as far as the time it covers, where an
  integrator clamped such a frame to 1/30 s.
- Interrupting a spring — a label change mid-spring, or an exit cancelled mid-flight — releases a new spring
  from the current value and velocity, as Framer does. A label change started the new spring at rest before. A
  color carries no velocity, and its reversal springs over a fresh travel of 100, as Framer mixes from the color
  it shows.
- A `layoutId` move on a spring springs its progress over Framer's travel of 1000 and ends when that spring's
  duration has run, where it ended once its edges had come to rest within 0.1 px of their layout.

- A `V.Div`, `V.Custom<T>`, `V.ScrollView` or `V.Button` shorthand call passing a literal `null` as its
  second argument with children after it, as in `V.Div("p-4", null, child)`, no longer compiles: it matches
  the new `(className, events, children)` form as well as `(className, children)`. Cast the `null` to
  `VNode` to keep the old meaning.
- Each factory that gained `events:` takes one more parameter, last, so a method group of one bound to a
  delegate type that spells out its parameters, as in a `Func<…>` taking `V.VirtualList`, needs
  `FiberEventBinding[]` added before the return type.

- `Router.Navigation` is React Router's `router.state.navigation`: the navigation in flight, from the
  moment its path has matched a route until it commits or gives up — its `NavigationLifecycle`, the
  location it is heading for, resolved against the route tree so it carries the destination's `Params`
  and `Matches`, and the submission it carries — and `Idle` with every other member null while none is.
  `Router.OnNavigationChanged` is raised as it changes, and `Hooks.UseNavigation` returns it. An attempt
  that is blocked or matches no route never appears there, and an attempt reports how it ended through
  the `NavigationResult` it returns or the exception it throws.

- `text-balance` and `text-pretty` break the lines of the text a leaf displays instead of narrowing its
  box, so the box keeps the width its classes and its parent give it, as with CSS's `text-wrap`.
  `w-full text-balance` and `w-64 text-balance` balance, where a declared width used to switch balance
  off; a box the cascade sizes (stretched, `w-*`, `max-w-*`) no longer shrinks, so a background, a border
  or an alignment relative to it sees the unbalanced box, and it no longer takes width from siblings in
  its row. A box sized from its text can still end up narrower than the unbalanced text. Where the lines break
  follows Chromium's score line breaker: `text-balance` acts on up to six lines, `text-pretty` on up to
  four, and neither on fewer than four words. The text a component passes in is not rewritten; the breaks
  exist only in the displayed string. A leaf that gets breaks is written `white-space: pre-wrap` over a
  string whose spaces, tabs and newlines Velvet collapses itself, as `white-space: normal` would, and
  the white space at a break is dropped; a leaf that gets none keeps its text and white-space as written.

- `text-balance` and `text-pretty` inherit, as CSS's `text-wrap-style` does. Put the class on a
  container and every text leaf under it breaks its lines; `text-wrap` or `text-nowrap` on a nearer
  element resets it. They used to act only on the element carrying the class.

- Breaks fall at white space, between CJK characters (with basic kinsoku), after hyphens and dashes and
  after a zero width space, a subset of UAX #14. Other rules of the standard are not read.

- A `V.VirtualList` no longer renders the item starting exactly at the viewport's far edge, which
  react-window's `getStartStopIndices` leaves out of the visible range: a list whose viewport is a whole
  number of items long renders one row fewer, overscan aside. It used to render that item as well.

- `StyleTransitionConfig.Repeat`, `RepeatType` and `RepeatDelaySec` apply to a `TransitionType.Tween` play, which
  used to play once and log a warning. A repeating `Tween` is now played by the per-frame driver a `Bezier` plays
  on, eased by the curve UI Toolkit's transition would ease it by, so code that already sets them on a `Tween`
  renders every pass, and a `Hooks.UseAnimationSequence` step or a `TransitionWhen.BeforeChildren` parent holds
  for all of them. The repeat warning is no longer logged.
- A `Tween` play started on a sequence's playback follows `AnimationSequenceControls.Pause`, `Speed` and `Cancel`
  on the default `MotionClock.Realtime` as well, where it was UI Toolkit's own transition and ran on to its end.
  It is driven, as a `Tween` on any other clock already was, so it lands every property its swap changes with the
  swap rather than leaving the element's other transitions to run it.

- `QueryOptions.Retry` and `QueryClientOptions.Retry` take a `RetryRule`, so a query retries as v5's `retry` given
  as `true`, `false` or `(failureCount, error) => boolean` as well as a number; a number still converts.
  `QueryClient.DefaultRetry` is a `RetryRule?`, null where the client's options set none, and
  `QueryClientOptions.Retry` defaults to null, which a mounted query reads as three retries. `FetchQuery`,
  `PrefetchQuery` and `EnsureQueryData` now retry as the client's `Retry` says where the options set none, as v5's
  `fetchQuery` does; they used to ignore it. A failed request asks `RetryDelay` for its wait before it decides
  whether to retry, as v5's retryer does, so the last failure asks it too, and one that throws there fails the
  request with its own exception.
- `QueryOptions.StaleTime`, `QueryClientOptions.StaleTime` and `QueryClient.DefaultStaleTime` are a
  `QueryStaleTime`, to which a `TimeSpan` converts, so that they can hold v5's `'static'`.
- A query's render builds the entry it reads when there is none, as v5's observer builds its query, so the
  client holds the entry from the first render on, a disabled query's included, and the client's commands and
  `GetQueryState` find it before the component commits. A render used to read the cache without writing to it.

- `Hooks.UseMutation` reads a `QueryClient` from `QueryClientContext.Ref`, or takes one as its new second
  argument, and throws when it finds none, as `useMutation` does without a QueryClientProvider. A component
  calling it now needs a client provided above it.
- `QueryClient.Clear` empties the client's mutation cache too.
- A mutation's `MutationFn` and callbacks, its cache callbacks and its per-call `MutateOptions` callbacks are
  handed a `MutationFunctionContext` (client, meta, mutation key) last, as v5's are. `MutationFn` no longer
  takes a `CancellationToken` of its own: the token is `MutationFunctionContext.CancellationToken`.
  `Retry` moves to `MutationOptionsBase`, and `MutationFn` may be null for the client's defaults to supply.
- A call keeps the options it was built with once a newer call supersedes it, where it used to call the
  latest render's function and callbacks; a later commit's options reach only the pending call the handle
  shows. A call is built from the options the component last committed.
- A call made after the component unmounts stays in the mutation cache until it is removed, as v5 keeps its
  observer on it.
- The first attempt of a `RetryPolicy` run, a mutation's included, waits for the connection its
  `NetworkMode` needs, and a mutation's for its scope, but not for the application to be visible, as v5's
  retryer starts.

- The read-only QueryInfo view is replaced by `Query`: the function forms of the query options (`EnabledFn`, `StaleTimeFn`,
  `RefetchIntervalFn` and the rest), `PlaceholderData`'s `previousQuery`, `QueryPlaceholder.KeepPreviousData`
  and `QueryFilters.Predicate` are handed the entry's `Query`, which reads the entry as it stands rather than
  as it stood at the call. A nullable `previousQuery` is tested with `!= null` rather than `HasValue`.
- The options every query shares move to `QueryOptionsBase`, which `QueryOptions` and `InfiniteQueryOptions`
  derive from. `QueryOptions.QueryFn` is a `QueryFunction<T>`, holding a function that takes the context or
  nothing; a `with { QueryFn = ... }` hands it `QueryFunction.Of(...)`.
- The `QueryOptions` constructors taking a function of a `CancellationToken` are removed: a query function takes
  the `QueryFunctionContext`, as v5's does, and reads `context.CancellationToken`, or takes nothing. Reading the
  token is what makes the last reader leaving cancel the request.
- `QueryState<T>.FailureCount` and `FailureReason` are named `FetchFailureCount` and `FetchFailureReason`, as v5's
  `QueryState` names them, and `QueryOptionsBase<TQueryFnData, TData>` derives from a non-generic `QueryOptionsBase`.
- `QueryFilters.Exact` is a `bool?`; `QueryCache.Find` reads null as true, as v5's `find` defaults `exact`.
- Invalidating an entry tells the queries reading it, as v5's invalidate dispatch does, so a result reading
  `IsStale` re-renders with the invalidation.

- `MutationOptions`' `OnMutate`, `OnSuccess`, `OnError` and `OnSettled`, on all four option records, return
  a `VelvetTask` (`OnMutate` a `VelvetTask<TContext>`) instead of `Action`s and a plain context, and the
  call waits for each before the next stage, as TanStack Query v5 awaits the promise they return: the context
  is the task's result, a faulted task fails the call, and the handle is committed after the last callback.
  A callback with nothing to wait for returns `VelvetTask.CompletedTask`, or is written `async`.
- `QueryOptions.PlaceholderData` is handed `(previousData, previousQuery)`, the previous entry as a nullable
  `Query`, instead of the previous `QueryKey`; the key is `previousQuery.QueryKey`. The signature of
  `QueryPlaceholder.KeepPreviousData` follows.
- `QueryOptions.StructuralSharing` is a `Func<object?, object, object>`, as v5's takes `unknown`, and a
  function given there is now also handed what a `Select` returns, which it was not when the type changed.
- Structural sharing reaches into the classes and records an application declares: an instance whose
  fields are all equal to the held one's is the held instance, and a changed one is a copy carrying the held
  instance of each equal field. Data that compared as a new instance, an equal record from a refetch for
  one, now keeps the earlier instance, so a component comparing it by reference no longer re-renders.
- `QueryClient.InvalidateQueries` returns a `VelvetTask` that completes once the refetches it started have
  settled.

- A navigation to a path no route matches commits, as React Router's data router does. `Router.CurrentLocation` becomes
  that path, a history entry is added, `Router.CurrentLoaderData` empties, and the root route (the only top-level route,
  else the first with an empty path or `/`) carries an `ErrorResponse` with status 404 in `Router.CurrentLoaderErrors`,
  so its `errorElement` renders, or the default error element where it has none. No guard, action or loader runs for it.
  `Router.NavigateAsync` still answers `NavigationResult.NotFound`, now after committing; a guard redirect to a path no
  route matches commits the same way instead of ending without a commit. A caller that treated `NotFound` as "nothing
  changed" must read `Router.CurrentLocation` instead. A null path still commits nothing.

- `RouteMatch.RouteId` is unique per route. It is `RouteDefinition.Id` when the route sets one, and otherwise its
  position in the tree, the indexes from the top-level array down joined by `-` (`"0"`, `"0-1"`), where it was the
  route's cumulative path (`"/users"`, `"/room/?index"`) and collided for sibling pathless layouts and for an index
  under `/`. Loader data, action data and errors are keyed by it, so `Router.GetLoaderData`,
  `Router.CurrentLoaderErrors`, `RouterContext.LoaderData` and `RouterContext.Errors` lookups written with a path-shaped
  key take the new one, and the "did not provide an `action` for route" message names it. A tree whose routes share an
  id throws `ArgumentException`.

- A pathless layout, a route with an empty path and children, no longer matches as the leaf of its own path. It stays in
  the chain of every path a child matches; a path only the layout would have taken now matches the route beside it, or
  nothing, where it used to render the layout with an empty `Outlet`.

- A `TransitionType.Bezier` play with `BezierX1` or `BezierX2` outside `[0,1]` now plays that curve, as Motion's
  `cubicBezier` does. It fell back to the default curve with a one-shot warning before, so a play that relied on
  the fallback now runs a different curve. A non-finite coordinate still falls back.

- A `[Component]` whose hook value is never read, such as `var (_, bump) = Hooks.UseState(0)` kept to force a
  render, is no longer left wholly unwoven. It has no gate on the body as a whole, since the render the hook causes
  changes nothing such a gate is keyed on, and its parts are cached on their own like any other body's. A render the
  setter causes now builds again only the parts that read something the build cannot track — a mutable static, a
  call whose target it cannot see, a stable object a call may have changed — and hands the reconciler every other
  part by instance, so a subtree that reads only parameters and hook values is not built again by it, as in
  React Compiler, where each reactive scope is recomputed according to the values it
  reads and not according to whether the component rendered.
- The code that computes what a local is given, such as `var visible = items.Where(Matches).ToArray();`, is cached
  in a part of its own when a cached part reads that local as a whole and nothing writes into the local afterwards.
  It keeps its identity while the values it reads are unchanged, so the parts that read it are no longer built
  again on every render in which something else changed, where the array was rebuilt each time and every part
  keyed on it missed. A body that expected a fresh array, list or delegate on each render, by comparing it with an
  earlier one, sees the same instance until its inputs change; a local the body writes into, or hands to any call
  other than a pure one, is still computed on every render. The memoization
  guide states which computations are cached.
- `Hooks.TryGetValueScope` and `Hooks.StoreValueScope`, the runtime half of the above for a part that caches a
  value rather than a `VNode`, which the build-time transform calls and nothing else should.

- A `font-[…]` family is read as Tailwind reads a bracket: `_` is a space and `\_` an underscore. A family used to
  keep its underscores; write `\_` for one whose name holds an underscore. An `addr:` key keeps its underscores.

- `Hooks.UseAnimationSequence` takes its dependency list as a required second parameter, `deps`, ahead of
  `autoplay` and `loop`, and reads it as the React migration guide's dependency-list section describes:
  null restarts the sequence on every render and an empty array plays it once per mount. The list used to
  be an optional last parameter whose omission, or null, played the sequence once per mount. A caller that
  left it out, or passed null, passes `Array.Empty<object>()` to keep that behaviour.

- An error a `startTransition` callback throws, or its `async` action faults with, no longer reaches the
  caller: it is thrown from the declaring component's next Transition-lane render to the error boundary
  above it, as React's `startTransition` dispatches it as the `isPending` update where the callback returns,
  and the outcome rendered is that of the call whose callback returned last, an `async` action's included. Once the declaring component has unmounted, the error is dropped. It
  used to propagate to the caller, or to be logged as an unobserved fault for an `async` action.

- Inside a `startTransition` callback, an `await` of a `VelvetTask` that had already completed suspends,
  as JavaScript's `await` resumes in a microtask once the code that called `startTransition` has returned:
  the continuation runs after a discrete handler returns, or on the main thread's next tick outside one, so
  the updates after it are no longer part of the transition and a handler's own writes after the call land
  first. They ran inline inside the transition. An `await` of a completed `Task`, a ValueTask or
  `Awaitable` still continues inline, and what follows it stays in the transition.

- A chain of layout commits that never settles — an error boundary whose fallback's layout effect throws back
  into it on every commit — stops after 50 follow-up commits, React's nested-update limit, where it stopped after
  100. The `InvalidOperationException` it throws reads "Maximum update depth exceeded", as React's error does.
- The passive effects (`Hooks.UseEffect`) of one commit, cleanups and then setups, run in tree order, children
  before their parent, as React's commit walks the tree. Components whose updates a drain flushed one by one ran
  in the order the drain flushed them, and a component its own update rendered ran after a later sibling its
  parent's pass rendered.
- Updates that keep coming from the commit phase — a callback ref or a layout effect writing a new value on every
  commit, or a `UseSyncExternalStore` whose `getSnapshot` builds a new value on every read — stop after 50 nested
  passes by throwing an `InvalidOperationException` reading "Maximum update depth exceeded" out of the drain, as
  React throws its error. The runaway update is still dropped. The drain used to log that error and return.
- After a keyed reorder moves elements holding components, a commit takes those components in the new tree order,
  as React's child list follows the move: their layout effects, refs and passive effects run in that order. They
  ran in the order the components were first created.

- An error boundary — `V.ErrorBoundary`, or a `[Component(IsErrorBoundary = true)]` registering
  `Hooks.UseFallback` — keeps showing its fallback once it has caught, until it remounts, as React's does. A
  later render of the boundary, its parent's or its own, renders the fallback again with the error it caught.
  It used to render its children again on the next render that did not throw. To show the children again,
  give the boundary a new `key`, which remounts it.

- An error thrown below a route's `element` — while rendering, from an effect, or from an element callback —
  renders the `errorElement` of the nearest route at or above it that declares one, and the root's default
  one where none does, as React Router's RenderErrorBoundary does; `Hooks.UseRouteError` returns it there.
  It used to propagate to the nearest error boundary above the router. The error clears when the router
  publishes a new location: a navigation, or a `Suspend` loader of the current location settling.

- After a throw while rendering its value, a `V.Await` keeps its `errorElement` for the deferred values it is
  handed next, as React Router's AwaitErrorBoundary does, until it remounts. It used to render the next
  value.

- An unkeyed element is matched by the index it is written at among its siblings, as in React, rather than
  by how many elements were emitted before it. A sibling turning to `null` no longer shifts the elements
  after it onto their predecessors' elements: in `cond ? V.Div(V.Component(Row)) : null` beside a second
  such `V.Div`, the surviving wrapper keeps its own element and the `Row` under it its own state, where it
  was patched onto the departing wrapper's element and took over the first `Row`'s state. A `V.Fragment` or
  a component written earlier among the siblings rendering more or fewer elements no longer remounts the
  elements after it. An unkeyed element moved into or out of an unkeyed `V.Fragment` or `V.Provider`
  written among its siblings is remounted, as React remounts a child whose parent fiber changes, where it
  used to be patched in place when its flat position matched. An unkeyed `V.Fragment` that is an element's,
  a `V.Motion`'s, a `V.Portal`'s or a `V.WorldSpace`'s only child is reconciled as its children, as React
  unwraps an unkeyed top-level Fragment: a `V.TextField` written alone, then first in such a Fragment beside
  a sibling, keeps its element and its text.

- A variant play on a `V.Motion` whose caller named no `transition`, `duration`, `easing` or `delay` runs each value
  on the transition Framer Motion picks for its type: a spring for translate, rotate and scale (500 stiffness and 25
  damping, scale on 550 and 30), and a 0.3 s ease for opacity, colours and lengths. It used to play the `Fade`
  preset's 0.2 s tween over every value. A value neither channel set covers lands with the swap, as it does on any
  spring. A Motion naming a transition, or a pose naming its own, is unchanged, as is the preset enter and exit of a
  Motion without variants.

- A `V.Motion` with variants that resolves an `animate` label and no `initial` label, its own or inherited, plays a
  mount enter from the look it has without its variant classes to its `animate` pose, as Framer Motion animates a
  component naming no `initial` from the values it has. Children inheriting the labels of such a Motion enter in the
  same way, and a presence child added later does too. They used to mount at the `animate` pose with no enter.
  `V.Motion(enterOnMount: false)` mounts the Motion and the Motions below it at their resting pose with no enter, as
  Framer's `initial={false}` does, and so does `transition: StyleTransitionConfig.None` for one Motion.

- A `whileHoverClass` outranks a `whileFocusClass` where both are on and set the same property, and `whileTapClass`
  outranks both, ranking the gesture states as Framer Motion's `variantPriorityOrder` does. The focus class used to
  outrank the hover one.

- A `V.Motion` naming none of `animate`, `initial` and `exit` plays a mount enter from the labels it
  inherits, as a Framer Motion variant child does: it takes the nearest ancestor Motion's `initial` label
  along with its `animate` one, mounts at its own pose for that `initial` and enters to its pose for the
  `animate`. Children mounting with an entering parent enter in the slots the parent's `StaggerChildrenSec`,
  `DelayChildrenSec` and `When = BeforeChildren` give them, a `V.AnimatePresence`'s keyed children
  included, so a presence listing inheriting Motions under a staggering parent staggers them in. One
  mounting under a parent already mounted enters on its own. Such a Motion used to mount at rest.

- A `V.Motion` naming any of `animate`, `initial` and `exit` inherits no label, as Framer treats a Motion
  naming a variant label as controlling its own. One naming only `exit` no longer follows its ancestor's
  `animate` label, and one naming only `initial` rests at that pose. The latter used to rest at the
  inherited label's pose, or at its own classes, and log a warning.

- `StaggerChildrenSec` numbers each Motion's own inheriting children, as Framer's `staggerChildren` does. An
  inheriting child with variants starts its own inheriting children with itself instead of handing them the
  next slots of its parent's sequence, and every inheriting child with variants takes a slot whether or not
  the label changes its pose. Label changes, mount enters and presence exits all number this way.

- An `initial` pose applying no class starts the mount enter from the Motion's own classes, as a Framer
  `initial` naming no value starts from each value's current one. It used to cancel the enter.

- `V.AnimatePresence(initial: false)` withholds the mount enter of every Motion that mounts under a child
  its first render created, for as long as that child stays, a later render's and a `V.Portal`'s included,
  as Framer's `PresenceChild` does; an inner presence's children answer to that presence. It used to reach only the Motions that first render created outside a
  portal.

- A tween's `PropertyOverrides` leave every property they do not name on the config's own
  `DurationSec`, `Easing` and `DelaySec`, as a value missing from Framer Motion's per-value transition map
  takes the default one. The overrides used to replace the swap's `transition-property: all`, so a property
  they did not name landed at once.

- A `V.AnimatePresence` keyed child whose Motion is created again under the same key, as an `elementType`
  change does, enters again, as Framer mounts the new child of a kept `PresenceChild` with its enter. It used
  to appear at rest, and one whose key returned mid-exit did too. A child the presence's first render created
  under `initial: false` still enters nothing.

- A presence child's Motion that inherits its labels from the Motion above the presence rests at its pose when
  its key returns mid-exit, or when it is added with no `initial` label to enter from, as one naming its own
  labels does. It used to start its transition preset's enter, such as `StyleTransition.Fade`'s, over that pose.

- A `Spring` `StyleTransitionConfig` that sets none of `Stiffness`, `Damping` and `Mass` takes its physics from
  its `DurationSec` (and `Bounce`), as Framer Motion's spring `duration` describes one, and each pass of a play
  on it ends on its target once that duration has passed, a `layoutId` move included. A channel released with
  a velocity solves its physics for that velocity, as Framer's `findSpring` does per value. A spring's
  `DurationSec` used to be ignored, so such a spring ran on the default 100 / 10 / 1 physics until it rested.
  This includes `V.Motion(transition: spring, duration: …)`, the `When = BeforeChildren` wait on such a spring,
  and a sequence `To` step on one, which holds until the first 50 ms sample at or past the duration.
- Any `Spring`, physics knobs included, with a `DurationSec` of exactly 0 and no `RepeatDelaySec` is instant,
  as Framer makes a zero-duration transition: it lands on its pose once its `DelaySec` and the stagger its play
  is given have run, at once where both are none, and repeats nothing. A `V.AnimatePresence` keeps a child
  whose exit is such a spring until that exit has landed, and removes it on the render after. Such a spring
  used to play on its physics. A sequence `To` step on one still
  plays, as in Framer's sequence: on its physics knobs where it sets any, and otherwise on the spring a 0.01 s
  duration describes.
- An enter or label change on a `Spring` or a `Bezier` that interrupts a running `Spring` or `Bezier` enter or
  label change on an element that is off its panel starts each channel the two share from the value the
  interrupted play has reached, as Framer Motion animates every value from the one it has; a `Tween` on a mount
  clock other than the panel's counts as a `Bezier` here. A color starts at the color drawn, and a length the two
  hold in different units keeps its own start. Such a change used to start from the pose the swapped-out label
  names. On an element on its panel the start stays the value on screen.

- A `V.Motion` whose transition sets `When = TransitionWhen.AfterChildren` starts its own play once the plays of its
  inheriting children have ended, and then waits out its own `DelaySec`, as Framer Motion's `when: "afterChildren"`
  does. A label change, a mount enter, the enter of a presence child naming its own `animate`, and an exit all wait,
  and the children start as under `Together`. A child's end is measured as `BeforeChildren` measures a parent's play,
  and includes the ends of the children it has of its own. `AfterChildren` used to log a warning once and play as
  `Together` does.

- A `Spring` or `Bezier` variant swap whose classes on one side write the same slot more than once
  animates each slot toward the class the cascade lets hold it at rest. A shorthand beside one of its own
  longhands (`p-8 pt-2`, `size-8 w-20`, `rounded-3xl rounded-tl-lg`) animates each slot both sides name
  toward its own holder; both classes used to land instantly with the swap. Two stylesheet utilities on one slot
  (`opacity-50 opacity-20`) animate toward the one the stylesheet declares later rather than the later
  one in the class string. Important stylesheet utilities suppress fully covered plain tokens;
  important inline tokens outrank plain tokens of the same property. Surviving inline tokens take
  their slots over stylesheet utilities, with later writes deciding across a shorthand
  and its longhand. These conflicts used to animate
  toward the later class in the string and jump to the cascade's value when the play ended. A slot held
  by a class no magnitude is read from (`rounded-tl-full`, `scale-x-[.5]`) lands with the swap instead of
  being driven toward another class's value. A slot whose inline holder depends on an existing class
  projection also lands with the swap.

- A `Hooks.UseAnimationSequence` `To` step with a `Spring` transition and no `holdSec` holds for the
  transition's `DelaySec` plus the duration Framer Motion's sequence gives the same spring: a travel of 100,
  sampled every 50ms until it is within 0.5 of its target and moving at no more than 2 per second, and at most
  20 seconds — 1.05 seconds for the default `Stiffness` 100 / `Damping` 10 / `Mass` 1. It used to log a
  warning and hold for a fixed 0.5 seconds whatever the spring, so a sequence whose spring steps relied on
  that now advances when Framer's would.

- A `Spring` or `Bezier` slot whose swapped-out classes name no value now starts from its current inline
  value, including a running Spring or Bezier frame, otherwise from its resolved value. A native
  transition on that slot supplies its displayed value before the custom play takes over. A color or length
  named only by the swapped-in classes used to land instantly, as did a pair in different units
  (`w-1/2` against `w-[60px]`); opacity, translate, scale and rotate started from their identity values.
- A `Spring` or `Bezier` variant swap leaves alone a slot the element's own classes hold through the swap. An
  inline-resolved class of its own (`pt-[2px]` beside a `p-0` → `p-8` swap) used to be driven toward the
  swap's value and jump back when the play ended.
- A nonuniform sampled scale or set of border colors is not collapsed into one target-only uniform channel.
- Computed plans retain resting important holders, including inline holders that have no class-list token;
  an important inline token the swap itself replaces still yields to its new target.

- Native transitions on a clip wrapper no longer intercept a custom length channel, including when
  the element wraps or unwraps during a play.

- A negative `StyleTransitionConfig.DelaySec` starts the animation that far into its run, as a CSS transition
  and a Framer Motion animation with a negative delay do. A tween writes it as its transition delay, in a
  per-property override as well, and a `Spring` or `Bezier` play starts partway and completes at once when
  the delay reaches past its end. A tween used to ignore it (except in a per-property override list that also
  held a positive delay), and a `Spring` or `Bezier` play started from the beginning. A
  `Hooks.UseAnimationSequence` `Spring` step's hold is shortened by a negative delay to match.
  Tween enter and exit plays combine orchestration offsets with their own and per-property delays,
  so a negative total advances the first animated frame rather than postponing the class swap.

- A skewed `V.Div`, `V.Button` or label is picked by its sheared shape instead of the bounding rectangle of the sheared
  box: a pointer over a corner the shear leaves bare reaches what is behind the element. The elements Velvet builds for those three are now `VisualElement`, `Button` and `Label` subclasses, so
  code that compares `GetType()` against `typeof(VisualElement)`, `typeof(Button)` or `typeof(Label)` must test with
  `is` instead.
- A radius that does not fit its box is scaled on the elements Velvet builds for element and motion nodes, whatever
  its source: a rule of your own stylesheet and a length your code writes to `style` are fitted when the element is
  laid out and each time its size changes, as a `rounded-*` class already was. An element that relied on overlapping
  radii shows them reduced.
- A class of your own is ranked by the properties its rule sets, read off a probe element once per class and panel:
  `my-card dark:my-card-dark` takes `my-card` off the element while `dark:` holds, when `my-card-dark` sets every
  property `my-card` does, where the two used to tie on the stylesheet's declaration order. A class starting `unity-` or `velvet-` is never probed.

- `skew-*` composes with `rotate-*`, `scale-*`, an `animate-*` preset and a `V.Motion` as CSS does, so an element
  that carries any of them is drawn as its own transform too: the individual `rotate` and `scale`, and then
  `skewX() skewY()`, are written together into the element's rotate and scale slots, and written again when a
  class, a variant, a play or a `layoutId` move changes either. The caster's text and descendants shear with it,
  `origin-*` moves the pivot, a pointer picks the drawn shape, and the angles a `V.Motion` animates are written into
  the same slots. A rule of your own stylesheet that sets `rotate` or `scale` on a skewed element is not composed.

- Two variant rules of one rank order as Tailwind emits them rather than by where the className writes them:
  by their variants' values, then by the first property they differ on in Tailwind's property order, then by
  the candidate itself with digits read as numbers. On every property an arbitrary value shares with another
  variant rule of its rank, the one emitted later takes it, except where a class emitted later meets a margin
  `space-*` holds or a border `divide-*` holds, which keeps the value.
  `data-[state=open]:w-[10px] data-[side=left]:w-[20px]` with both attributes set was 20 px wide and is now 10
  px; `hover:w-[20px] hover:w-[10px]` on a hovered element was 10 px and is now 20 px;
  `hover:bg-red-500 hover:bg-[#f00]` paints `bg-red-500` in either written order; and
  `hover:bg-red-500 hover:bg-blue-500` paints red, where the stylesheet's own order used to paint blue.

- A `ring-*` / `outline-*` band moves with its element's own transform, as a CSS outline or box-shadow
  does. A `translate-*`, `scale-*` or `rotate-*` on the ringed element, about any `origin-*`, and a
  transition or animation driving one, are copied onto the band once per frame; the band used to stay on
  the element's untransformed box.

- A `ring-*` / `outline-*` band takes its element's own opacity, `invisible` / `visible` and `hidden`, as
  an outline or box-shadow does. The band used to stay opaque, visible and displayed whatever the element
  set.

- A `ring-*` / `outline-*` on a `V.Motion` renders, and follows the Motion's animated transform and
  `layoutId` plays. It used to be ignored with a warning.

- A same-property tie between variants resolves the way Tailwind's generated CSS resolves it: by specificity,
  then by the order Tailwind emits the rules. A media or feature query adds no specificity, so `supports-[…]:`,
  the breakpoints and `dark:` sit with the base utility, followed by `[&>*]:`, which carries one class on each
  child. `group-*`, `peer-*`, `first:`/`last:`/`only:`/`odd:`/`even:`, the element states, `has-[…]:`,
  `aria-[…]:`, `data-[…]:` and `nth-N:` each add a pseudo-class or attribute selector and rank above all of those,
  in that order, with every arbitrary `[&:…]:` selector last. What changes for a working application:
  - `has-[…]:`, `aria-[…]:` and `data-[…]:` win over the element states and `group-*`/`peer-*`, where they lost:
    `hover:w-[10px] aria-[busy=true]:w-[20px]` on a hovered, busy element was 10 px wide and is now 20 px.
  - The structural variants win over `md:`, `dark:`, `group-*` and `peer-*`, where they lost to all of them, and
    `[&:nth-child(N)]:` and the other `[&:…]:` forms win over every single named variant.
  - A container's `[&>*]:` payload wins over a child's own `md:`, `dark:` and `supports-[…]:` payloads, where it
    lost to them; it still loses to the child's own pseudo-class variants.
  - A stacked variant carries the specificity of all its parts added together and ranks by every part, where it
    took its stronger part's place: `hover:focus:w-[20px] active:w-[10px]` on a hovered, focused, pressed element
    was 10 px wide and is now 20 px; `dark:hover:shadow-lg hover:shadow-sm` on a hovered element in dark mode
    paints `shadow-lg` whichever is written later; and a container's `[&>*]:hover:` payload now beats the child's own `hover:`.
  - An `aria-[…]:` and a `data-[…]:` payload no longer tie: `data-[…]:` wins, as Tailwind emits it later.

- While a `V.Motion` animates a length or a color, the animated value stands over the element's own
  variant layers (`dark:`, `sm:`, `hover:` and the rest) and over a `space-*`, `divide-*` or grid
  value, as an animation does in CSS, and a gap, a grid's column and row gaps included, adds to an animated
  margin as CSS adds an item's margin to its gap; a grid child moves inside its column rather than wrapping.
  An important divide value (`!divide-gray-200`) and an important declaration of the element's
  own (`!w-[80px]`, or `hover:!w-[80px]` turning on during the animation) keep what they win, as
  `!important` outranks an animation, and a gap adds to an important margin that wins. A plain variant layer
  toggling during the animation used to replace the animated value
  until the next frame, or for the rest of a delay, and a gap gave way to an animated margin.

- On the edge a `divide-dashed` or `divide-dotted` divider draws, a border width or color of the child's own
  applies as it does under a solid divider: a width class or bracket width is drawn dashed at that width, and
  the dash takes the child's own border color where it wins the edge. A color the child's own code writes over
  the edge, one its style rules give it (a theme class, a `hover` rule) and one a `V.Motion` drives there are
  painted in the dash and repaint it when they change. The dash used to be drawn in the divide color at the
  divider's width, and a width class drew that edge solid.

- `divide-dashed`, `divide-dotted`, `border-dashed` and `border-dotted` lay out their dashes and dots as
  Chrome does: from 3px up a dash is two widths long with a one-width gap rather than three and two, the gap
  is fitted so an edge starts and ends on a dash, an edge too short for two dashes is drawn solid, and a dot
  up to 3px is a square a width long rather than a round one. The dashes used to run at a fixed period from
  the start of the line, so the last one could stop short of the end or be cut off at it.

- A `border-solid`, `border-dashed` or `border-dotted` of a divided child's own sets the line style of its
  divider edge over the divide's, as Tailwind's zero-specificity divide rule leaves it: `border-solid` on a
  child of a `divide-dashed` row draws its divider solid. An important divide style (`!divide-dashed`) keeps
  the edge. The divide's style used to be drawn whatever the child's.

- A color a divided child's own code writes inline on a divided edge outranks a plain `divide-{color}`, on a
  solid divider and a dashed one, and the child's own color classes on a dashed one, as an inline style does.
  The divider used to write its color back over it on its next pass, and a dashed one painted the divide color
  or the child's classes' color instead.

- `border-dashed` and `border-dotted` paint each side on its own in that side's color and at its width: on a
  box with square corners from corner to corner, so each side starts and ends on a dash, and on a box with a
  rounded corner along the rounded outline. Each side is cut to its own corner at the miter, so a translucent
  color is laid once where two sides meet, and a side a dashed divider paints is left to it. They used to lay
  one pattern around the whole outline in the left side's color and at its width, overlapping a dashed divider
  on a rounded child, and drew nothing on a box with no left border. Releasing the outline's captured colors
  no longer clears a divider's color or mask from the child's edges.

- A dashed divider on a rounded child follows the child's curve rather than running straight to its corners. A
  child that gains `border-dashed` or `border-dotted` in a reversed dashed row takes its outline's color from
  the divider rather than drawing nothing, and a color the child's code wrote over a dashed edge stays on the
  edge when the divider turns solid.

- A thin dotted line at least three widths long starts and ends on a whole dot, as Chrome draws it.

- A `gap-*` adds to a child's own margin on the edge it spaces, as CSS adds a flex gap to an item's margin,
  where it used to replace it: `ml-2` on the second child of a `gap-x-4` row now puts `24px` before it rather
  than `16px`, a `[&>*]:ml-[2px]` on the row gives every child after the first `18px`, and a child's own margin
  on that edge wins over a `space-*` margin there with the gap added to it. `ml-auto` stays `auto`. Under
  `flex-wrap` the half-margins add to every margin a child declares, and the container's own margin keeps its
  value with the negated halves added, so `m-4 flex flex-wrap gap-4` writes `8px` on the container where it
  wrote `-8px`. A grid still writes its children's margins over their own.

- A direction or wrap utility on a composite widget — `V.ScrollView`, `V.Custom<Foldout>` and any other
  element that puts its children in an inner box — lays out that inner box, as a CSS flex utility lays out
  the element's own children. `V.ScrollView("flex gap-2", …)` now lays its content out in a row, since `flex`
  is a row, where the content stayed a column; write `flex flex-col` for a column. `flex-wrap` on the widget
  wraps its content and takes the gap's wrap path there. The class still applies to the widget's own box
  too.

- On a panel, a gap reads its container's direction and wrap from the resolved style once a layout pass has
  run, so a stylesheet rule of your own that outranks a direction class, or a `flex-direction` written from
  code, decides which edge the gap takes; the direction class used to win until the next reconcile. Off a
  panel, a horizontally scrolling `ScrollView` and a horizontal `TwoPaneSplitView` space their content as
  the row their stylesheet lays it out in, where the first application spaced it as a column.

- Before it is attached to a panel, an element carrying no direction class (`flex`, `flex-row`, `flex-col`
  or a reversed form) spaces a gap as the column it lays out in, as it does once attached: `gap-4` alone
  writes `margin-top` between its children, and `gap-x-4` alone, a column gap in a column, writes nothing.
  Both used to space it as a row, on `margin-left`, until it was attached.

- A wrapping gap container that declares its own width, height, minimum or maximum gives its items the room
  CSS gives them and takes its declared size among its siblings: `w-[150px] flex flex-wrap gap-4` holds two
  66px items on one line and its next sibling starts 150px along. The half-margins the wrap path writes used to
  leave such a container's items a gap less room, so the second item wrapped, and the container took a gap
  less than its declared size, so its next sibling started 134px along.

- A margin class a play shows on a child of a `gap-*` row, such as `EnterFromClass = "ml-[3px]"`, adds to
  the gap on that edge while it shows, as CSS adds a gap to an item's margin: under `gap-[10px]` the child
  starts the enter 13px from its neighbour and ends it 10px away. The gap used to replace it, so the
  child sat 10px away throughout.

- A wrapping `gap-*` container that a `clip-path-*` class clips adds the negated half-margins to its own
  margin and grows its declared size by the gap, as an unclipped one does. The half-margins used to take its
  own margin's place there, and its declared size stayed as written.

- `space-x-*` / `space-y-*` follow Tailwind v4's margin rule rather than CSS `gap`: `margin-right` /
  `margin-bottom` on every in-flow child except the last child — an absolutely positioned one counting, as
  it does for `:last-child` — moved to `margin-left` / `margin-top` by
  `space-x-reverse` / `space-y-reverse`, whatever the container's direction and whether or not it wraps.
  They used to put `margin-left` / `margin-top` on every child except the first, move to the trailing
  edge on their own in a reversed container, and switch to the half-margin strategy under `flex-wrap`. A
  reversed row that relied on `space-x-4` alone adds `space-x-reverse`, as it would with Tailwind.

- `divide-x-*` / `divide-y-*` follow Tailwind v4's divider rule: `border-right` / `border-bottom` on every
  in-flow child except the last child, counted the same way, moved to `border-left` / `border-top` by `divide-x-reverse` /
  `divide-y-reverse`. They used to rule the left / top edge of every child except the first and move to
  the trailing edge on their own in a reversed container. A `divide-{color}` colors all four edges of a
  divided child, as Tailwind's `border-color` does, where it colored only the divider's edge.

- A `space-*` margin and a divider give way to a class of the child's own that sets the same edge — `mr-2`,
  `mr-[5px]`, `border-r-4`, `border-red-500` — as Tailwind's zero-specificity rules do. They used to
  overwrite it.

- A `gap-*`, `gap-x-*` or `gap-y-*` no longer reads a `space-x-reverse` / `space-y-reverse` marker, since
  CSS `gap` has none. `flex flex-col gap-4 space-y-reverse` spaced its children on `margin-bottom`; it
  spaces them on `margin-top`, as `flex flex-col gap-4` does. A reversed container still moves a gap to the
  trailing edge.

- A `gap-*` and a `space-*` on one element both apply, as in Tailwind, adding up on an edge both write;
  the later of the two used to replace the other. On a `grid` container, `space-x-*` is a margin on the
  children taken out of their column width rather than the grid's column gap, and `space-y-*` a margin
  rather than its row gap.

- `gap-x-*` and `gap-y-*` are CSS's `column-gap` and `row-gap`, kept apart. A single-line row takes only the
  column gap and a single-line column only the row gap; under `flex-wrap` each axis takes half of its own gap.
  The last gap token used to replace the other, `gap-x-*` used to indent the children of a column and
  `gap-y-*` to push down those of a row, and `flex-wrap gap-x-4 gap-y-2` spaced both axes by the row gap.
  A `display: none` child no longer takes a gap slot.

- `flex-1` sets a basis of `0%`, as Tailwind's `flex: 1` does, where it set `0px`. A `flex-1` child of a column that
  sizes to its content now keeps its content height.

- A `V.Motion` animates a border color one side at a time, as CSS animates the four `border-*-color`
  longhands: sides showing different colors each start from their own, an important color on one side
  (`!border-t-[#f00]`) keeps that side alone out of the animation, and the per-side forms
  (`border-t-red-500`, `border-x-[#f00]`, `border-s-red-500`) animate. A side a `divide-*` color or a dashed
  divider holds starts from the color it shows and animates toward a border color class of the child's own
  where that class wins the side. The animation used to land at once wherever the four sides differed, a
  class target landed at once on a side a divider held, and a one-sided important color kept all four
  sides out.

- An important declaration of the element's own keeps what it wins through a play that starts off a panel,
  as it does on one; a `V.Motion` carrying `!w-[80px]` when it mounted used to have it taken by its enter.

- A dashed or dotted divider's edge runs a transition entry of the child's own that covers its border color,
  from the color the dash shows to the one it heads for, as the native border of a solid divider does. The
  dash used to change color at once.

- A `V.Motion` whose transition sets `When = TransitionWhen.BeforeChildren` holds its children back only
  for the channels its animation plays: a Bezier or Spring whose classes animate nothing, an important
  declaration keeping them out included, holds them for nothing, its delay included, as Framer Motion's
  animation promise for a variant with no value to animate resolves at once. Its children used to wait
  for the delay and the whole duration, or for a spring over channels that never played.

- `whileHoverClass`, `whileTapClass`, `whileFocusClass` and `whileDraggingClass` apply their classes as a
  payload, ranked above every rule of the element's own short of an important one — `StyleOverrides` and
  className variants such as `hover:` and `focus:` included — and above a play's classes but an exit's, as Framer
  Motion's `variantPriorityOrder` ranks gesture states above the element's style and `animate` and below `exit`;
  a Spring or Bezier play still running as the state ends moves the property on from where the state left it.
  Among themselves they rank as `hover:`, `focus:`,
  `active:` and `data-[…]:` do, so a tap or focus class outranks a hover one. `whileOverClass` and
  `whileDragActiveClass` apply theirs at `data-[…]:`'s rank, after every rule the className declares with that
  variant alone. The element's own value comes back when the state goes off. A bracket value such as `bg-[#f00]` applies through them, and on an element so do `shadow-*`,
  `ring-*`, `skew-*`, gradients, `animate-*`, `border-dashed`, `gap-*` and `uppercase`. These channels used
  to rank with the element's own classes, so a base class the stylesheet declares later, a base bracket
  value or a divider's color kept the property, and a bracket value or a paint Velvet draws itself did
  nothing.
- While a play shows its classes — a `V.Motion` enter, exit or variant change on any driver, or an
  `AnimatePresence` preset — they outrank the element's own utilities and bracket values, its `StyleOverrides`,
  a divider's or space margin's value on the same edge, variant payloads and the droppable channels for the
  same property, as a CSS animation's declarations do, and lose to an important utility, behind a variant or
  not, and, but for an exit's, to a gesture class: a property a gesture class sets stays where it puts it while
  the state is on, on every driver, and a spring or bezier play other than an exit starting during the state
  leaves it out.
  A variant's classes rank as the element's own once its play ends. A class the stylesheet declares later, a
  bracket value or a droppable's class used to keep the property while the play ran, a divider's color did not
  give way to a class a variant change brought, and did not come back once an enter's class left.

- A `V.Motion` draws `shadow-*`, `skew-*` and `border-dashed` / `border-dotted`, from its className, its
  variants and the `while*Class` and drag channels, as any element does. It used to draw none of them, and
  logged a warning for a shadow.

- A `shadow-*` or `skew-*` element draws its `border-dashed` / `border-dotted` border dashed or dotted, sheared
  with a skew, and a `divide-dashed` or `divide-dotted` divider on a `shadow-*` or `skew-*` child dashed or
  dotted, as Chromium does; both used to be drawn solid.

- A `shadow-*` or `skew-*` element whose overflow is hidden keeps its border, dashed or dotted included, and the
  background beneath it, and a `shadow-*` one its outer shadow layers, where they used to lose their border, a
  border-wide ring of their background and the shadow; a skew's overhang is still cut at the padding box.

- While a `layoutId` move draws a `shadow-*`, `skew-*` or `border-dashed` `V.Motion` scaled differently on each
  axis, the face, border, dashed outline and skewed gradient those layers paint take the corner radii the move
  draws, a horizontal and a vertical one a corner, as its background does, and its shadow shows circular corners
  and its offset as written, as Framer Motion's `correctBoxShadow` shows them (motion.md says where its blur and
  spread differ). They used to be drawn square, and a shadowed one kept square shadow corners once the move
  landed.

- The classes a play shows — a `V.Motion` pose's, an `AnimatePresence` enter or exit class — apply bracket
  values and the utilities Velvet realises itself (`gap-*`, `shadow-*`, `ring-*`, `skew-*`, gradients,
  `animate-*`, `border-dashed`, `uppercase`, …) while the play shows them, and one of the latter that the
  element's resting pose carries stops applying while the play has the pose's class off. A play's class used
  to apply only a utility backed by a plain USS rule.

- On a field control a class a play shows lands where the className's does: a surface utility on the input
  box, `truncate`, `text-ellipsis` and `text-clip` on the text the box draws as well, with `overflow-hidden`,
  and a transition utility on both. On a clipped element a `clip-path-*` class a play shows re-shapes the clip.
  A `V.Motion` whose `elementType` is a field control sends the surface utilities of its className and its
  variants to the input box, as `V.TextField` does; they used to stay on the outer control.

- Equal `z-*` values stack in tree order: the sibling declared later paints on top, also after a keyed
  reorder or when an earlier sibling mounts later. They used to stack in the order they first mounted.
- A `z-*` value beyond the 32-bit integer range stacks at that end of it, as CSS clamps it (`z-2147483648`,
  `z-[3000000000]`). It used to leave the class unresolved.

- `z-*` stacks an element in flex flow as it stacks an `absolute` one, as CSS stacks a flex item: an in-flow
  `z-10` paints over its later siblings and keeps its place in the row or column. It used to be ignored on an
  element that was not `absolute`.
- A positioned child with no `z-*` (`absolute`, `relative`, `V.Anchored`) paints over its unpositioned siblings,
  wherever those are declared, as CSS paints it. It used to paint in declared order, so an overlay declared
  before the content it covers painted under it.
- `z-0` stacks in the same place as no `z-*` on a positioned element, in declared order among them. It used to
  paint over every sibling without `z-*`, those declared after it included.
- A `z-*` element is reordered within its own parent rather than moved into a layer container the parent gains:
  code walking `hierarchy` children finds it among them, at the place it paints, with the zero-size
  placeholder at its declared position. The layer containers are gone.
- Gaining, changing or losing a `z-*`, including through a variant, keeps the element on the panel: its
  transitions keep running and it keeps pointer capture and focus. Gaining one, losing one, or moving between a
  negative and a positive value used to detach and reattach it.

- `group-*:` and `peer-*:` react to every marked ancestor and every marked preceding sibling, as Tailwind's
  `.group:hover x` and `.peer:checked ~ x` do, and hold the payload while any one of them is in the state.
  The set follows later renders: a source inserted, moved, removed, or gaining or losing its `group` / `peer`
  class is picked up or released. They used to bind only the nearest one, so hovering an outer `group` around
  an inner one lit nothing.
- `z-*` on a `V.Motion` stacks it the way it stacks a `V.Div`. It used to be ignored with a warning.

- `drop-shadow-*` is a CSS `drop-shadow()` filter, as Tailwind writes it. The shadow follows what the element
  draws, children and transparency included, rather than its box. Its third length is the standard deviation,
  as CSS reads it. The sizes are Tailwind v4's: `xs` to `2xl`, and the bare `drop-shadow`'s two shadows. It
  composes after the other built-in filter utilities and before `filter-[name]` customs, and it no longer shares
  a slot with `shadow-*`, so the two render together and neither one's `-none` removes the other. It was the
  painted box shadow on its own scale.
- On a field control, the filter utilities (`blur-*`, `grayscale-*`, `filter-[…]` and the rest) land on the
  input box, beside `drop-shadow-*` and the other surface utilities, as CSS's `filter` on an `<input>` filters
  the input alone. They were on the outer control, where they also filtered a declared `label:`.
- `drop-shadow` is a reserved name in `VelvetFilters.Register`.
- The box-shadow shader is named `Velvet/BoxShadow`; it was `Velvet/DropShadow`. A shader opted out under
  **Project Settings ▸ Velvet** is saved by name, so an opt-out made under the old name has to be made again.

- On a field control, the text colour, `opacity-*`, the transforms (`translate-*`, `rotate-*`, `scale-*`,
  `skew-*`, `origin-*`), `animate-*`, and the text styling (`font-*`, `italic`, the text size and alignment,
  `tracking-*`, the text effects such as `uppercase`, and `truncate`) land on the input box, beside the other
  surface utilities, as CSS applies them to an `<input>` alone. They were on the outer control, where they also
  reached a declared `label:`. The transition utilities (`transition-*`, `duration-*`, `ease-*`) land on both, so
  a change to a box's background or filter tweens; they were on the control alone, which left the box's changes
  instant.

- `shadow-*` presets are Tailwind v4's, layers and all: `shadow-lg` is `0 10px 15px -3px` and `0 4px 6px -4px`,
  both black at 10%, where it was one soft tinted glow on Velvet's own scale. `shadow-2xs` and `shadow-xs` are
  added. A layer's shape, spread, blur and stacking follow CSS Backgrounds 3: the spread grows or shrinks the
  box with CSS's corner-radius rule, the blur is a Gaussian of half the blur radius, an outer layer is cut out
  where the element's border box lies, and the first layer is on top. A translucent background no longer
  shows the shadow through it. Corner radii stay circular where CSS's can be elliptical.
  `shadow-[…]` takes a comma-separated list of shadows, and rejects a negative blur. Each corner of the
  shadow follows its own corner of the element, where all four followed the top-left one.
- `shadow-inner` and an `inset` layer in `shadow-[…]` draw an inner shadow inside the padding box.
- An element with both `skew-x-*` and `skew-y-*` is sheared as Tailwind's `skewX() skewY()` composes them, for
  its face, its bounds, its gradient fill and the seating of its children. It was sheared as the simultaneous
  matrix `[[1, tanX], [tanY, 1]]`, which differs from the product by `tanX·tanY` on the x axis and is singular
  where `tanX·tanY` is 1.

- A `filter-[name]` whose name nothing is registered under leaves the element drawing no filter, its other
  filter utilities included, as CSS ignores a filter chain whose `url()` names no filter. It was an inert
  class, and the element's other filters still drew. The same holds after `VelvetFilters.Unregister`, which
  left an element's resolved filter in place, and for a definition destroyed after registration, which the
  compose skipped.
- `VelvetFilters.Register`, a re-registration and `VelvetFilters.Unregister` resolve every element carrying the
  name again, at once. A class applied before its name was registered resolved only when some later pass
  re-applied the element's classes, and a re-registration left elements on the definition they had.
- A filter change where the element's filter holds a user `filter-[name:args]` before or after it applies at
  once, under `transition-filter` and under the engine's own filter transition alike, as CSS interpolates a
  filter list holding a `url()` discretely. So does the first filter utility written over a stylesheet `filter`
  holding a custom filter function. Velvet's filter tween ran a custom's arguments between two
  argument sets of one definition, faded a custom in or out from its declared defaults, and tweened a built-in
  filter beside a custom.

- Under `transition-filter`, a filter change eases by the curve a USS transition takes for the same
  `ease-*` value, the curve it takes under `transition-all`. The tween drew `ease`, `ease-in`,
  `ease-out` and `ease-in-out` as CSS's cubic-bezier keywords, where UI Toolkit eases `ease-in`,
  `ease-out` and `ease-in-out` quadratically, and drew every other easing mode linearly.

- Under `transition-filter`, a filter change interpolates by the CSS filter-list rule: functions pair by
  position, and a filter added or removed at the end of the list fades. A filter added or removed
  anywhere else, such as a `blur-*` added to an element carrying `grayscale` or a `filter-[name]` custom, now
  applies at once, as it does in CSS and under `transition-all`. It faded in or out.
- `hue-rotate-[N]` with a bare number other than `0` is no longer a filter and stays an inert class, as CSS
  `hue-rotate()` takes an angle. Write the unit: `hue-rotate-[30deg]`.

- A registered font family picks its weight in CSS's font matching order rather than by the nearest
  weight. A request from 400 to 500 now takes the lightest weight from the request up to 500, then the
  heaviest below the request, then the lightest above 500; a request under 400 the heaviest at or below
  it first, and one over 500 the lightest at or above it first. So `font-medium` over a family
  registering 400 and 600 now renders the 400 face, where it used to render the 600 one. The face's
  style also narrows before its weight, as in CSS: `font-bold italic` over a family whose only italic
  face is the regular one renders that italic with synthesized bold, where it used to render the bold
  upright with synthesized italic. Of two entries registered at one weight the later is used, as the
  later of two `@font-face` rules for one face is in CSS; the earlier used to be. `VelvetFontFamily.FindClosestWeight`
  returns the entry this order selects.

- `text-balance` alone makes text wrap, as CSS's `text-wrap: balance` sets the wrap mode to `wrap`.
  It used to set no white-space, so on a label that did not already wrap it had no effect without a
  wrapping `whitespace-*` or `text-wrap` beside it. `!text-balance` and `text-balance!` balance too.

- `text-wrap` and `text-nowrap` set the wrap mode and leave the collapse alone, as CSS's
  `text-wrap: wrap | nowrap` does, so
  text that inherits `whitespace-pre`, `whitespace-pre-wrap` or `whitespace-pre-line` keeps its spaces
  and newlines under them. They used to be `white-space: normal | nowrap`, which collapsed those too. The
  same holds for `text-balance` and `text-pretty`, and a white-space class on the same element, or on one
  nearer the text, still decides the white-space. Among `text-wrap`, `text-nowrap`, `text-balance` and
  `text-pretty` on one element, the later class wins.

- `truncate` now stops an ancestor's `whitespace-pre-line`, as its `white-space: nowrap` resets the
  collapse; the ancestor's pre-line used to override it on the text.

- `leading-[…]` takes CSS `line-height`'s values. A unitless number (`leading-[1.5]`) multiplies each
  text's own font size; an `em` length or a percentage (`leading-[1.5em]`, `leading-[150%]`) is a length
  computed from the font size of the element that declares it, which every text under it inherits, and
  it follows that size when it changes; `rem` is 16px. Only `px` used to be read, and every other value
  was ignored.

- `tracking-tighter`, `tracking-tight`, `tracking-wide`, `tracking-wider` and `tracking-widest` are Tailwind's
  em scale (-0.05em to 0.1em) of the element's own font size, where they were pixel values baked at a
  16px font. Text at any other size is spaced in proportion to its size, and a mounted element follows its
  font size when that changes. The classes no longer have a rule in the bundled stylesheets, so a panel that
  resolves them reads them from Velvet's resolver, as it does `tracking-[0.025em]`.
- The `overline` rule sits with its top edge on the first line's top, where it used to sit 15% of the font
  size below it.

- A text leaf keeps the text the app gave. Under `uppercase`, `lowercase`, `capitalize`, `underline`,
  `line-through`, `whitespace-pre-line`, `leading-*`, a `text-<size>`'s line height, `text-balance` and
  `text-pretty`, `label.text` and `button.text` read the original text, where they read the upper-cased,
  tagged, collapsed or broken string. The string a leaf shows is handed to the engine through
  `TextElement.experimental.SetRenderedText`; code that read the effect's result off `.text` reads the
  text it set.
- A leaf with `enableRichText = false` is shown without the underline, the line-through and the line
  height, where it used to show `<u>`, `<s>` and `<line-height=…>` as literal text. The text-transform, the
  white-space and the line breaks still apply.
- A text decoration an ancestor sets reaches the text below it, as in CSS: `no-underline` on a descendant
  ends the lines that descendant's own classes ask for and no longer takes an ancestor's line away from
  its text.

- `V.Anchored` renders a wrapper element, as drei's `<Html>` does, and puts the element carrying its `className`,
  `name`, `events`, ref, children and `props` inside it. The wrapper owns `position: absolute`, the projected
  `left`/`top`, the hide for a target behind the camera or occluded, and the `distanceFactor` scale; `z-*` classes
  move to it. A `scale-*` class or a Motion scale variant on the element no longer fights `distanceFactor` for
  one style slot. A query for the named element now finds the inner one, so what read its position, display or
  scale through that query reads `.parent` instead, and a selector that assumed the element was the positioned
  one has to allow for the extra level.

- A `Button`'s `onClick` and a field's `onValueChanged` on a logical ancestor of a portal fire for a click or
  a value change raised inside that portal's children, as React's `onClick` and `onChange` bubble out of a
  portal; a disabled `Button` refuses the click, as Clickable and a disabled DOM button do. They were left to
  the physical tree, so a `V.Portal(layer:)` or a `V.WorldSpace` inside a `V.Button` never reached its
  `onClick`.

- An `events:` handler on a portal's logical ancestor fires for an event raised on an element written straight
  into the portal, as React bubbles any portal child. Only a component among the portal's children carried
  the event past the portal's boundary.

- Unscoped `sm:`…`2xl:` in a `V.Portal(layer:)`'s or a `V.WorldSpace`'s children evaluate against the width of
  the panel the portal was declared on, as a page's portals answer its one viewport; a layer host answers the
  panel of the last portal to render into it. They read the width of the host panel.

- `V.Anchored(hideWhenBehindCamera: false)` keeps following its target's projection while the target is
  behind the camera, as drei's `<Html>` does once `onOcclude` takes the hide over. It held the last position
  it had resolved.

- `V.Anchored` in an editor panel (Velvet content in an `EditorWindow`) lays the camera's viewport over the
  panel and positions itself there, as drei's `<Html>` places a projection on its canvas. It was hidden, with
  a warning.

- `V.Anchored`'s `distanceFactor` scales its element by the factor divided by the world height the camera sees at
  the target, as drei's `distanceFactor` does: `2 * tan(fov / 2) * distance` for a perspective camera and
  `2 * orthographicSize` for an orthographic one. It used to divide by the distance alone, so every element that
  sets `distanceFactor` now draws at a different size. Scale the factor you pass by `2 * tan(fov / 2)` to keep a
  perspective element's size.
- A `V.WorldSpace` host no longer carries a `BoxCollider` of Velvet's own. The host's `UIDocument` keeps one, a
  trigger, while a Play session runs, and Unity's input system picks through that. A physics query that ignores
  triggers no longer meets the panel.

- A `contain` focus scope holds focus against every panel, as React Aria's contained `FocusScope` holds it
  against the whole document. Focus that moves to a `V.Portal(layer:)` or `V.WorldSpace` host panel, or to
  another mounted tree's panel, is pulled back into the scope on its panel's next scheduler tick, unless it
  lands in a portal declared inside the scope or inside a contained scope created after this one; before, focus
  in any other panel Velvet manages was left where it landed.

- Of two contained scopes, the one created later now keeps focus against the older one, in one panel as across
  panels and mounted trees. Before, a landing inside any contained scope stood, so focus could move from a
  newer modal back into an older one beneath it, and each of two mounted trees' contained scopes pulled focus
  back from the other's panel.

- Content of a portal declared inside a `contain` scope counts as inside it when the portal renders into
  the scope's own panel, as it now does when the portal renders into another panel. Before, focus that moved
  there was snapped back.

- Arrow and d-pad navigation stays inside a `singleTabStop` group, as it stays inside a WAI-ARIA composite
  widget. A spatial move that lands outside the group returns to the member it started from; before, the
  engine's geometric navigation could leave the group at its edge.

- Of `singleTabStop` groups nested in each other, the outermost one below the nearest `contain` scope decides,
  as the outermost of nested toolbars does in React Aria's `useToolbar`, and a plain focus scope nested in a group
  is part of the group. Tab from anywhere inside leaves all of them, arrows move across nested groups, and entry
  lands on the member last used at any depth. Before, the innermost scope decided: Tab from inside a plain scope
  in a group moved to the next member, a backward entry landed on the group's last member, and arrows could not
  cross from a nested group to the outer one's other members.

- Every focus scope around a focused element records the landing, not only the innermost one: a
  `singleTabStop` group re-enters at a member inside a nested scope, and a `contain` scope pulls focus back to
  one.

- A `restoreFocus` scope returns focus to the element focused when the scope mounted, React Aria's
  `nodeToRestore`. Before, it returned focus to the element focus first entered the scope from, and a scope
  first entered through a scope nested in it restored nothing.

- Focus leaving the content of a portal declared inside a `contain` scope, by Tab or by a move to another
  element, is pulled back into the scope, on the portal's own panel or across panels, as React Aria's contained
  scope pulls back focus leaving a child scope. Before, it escaped. A blur to nothing from that content is left
  alone, as React Aria's is.

- The `focus-visible:` variant and `UseFocusRing` follow React Aria's input modality, one reading written by every
  panel Velvet renders into. After a pointer press, release or move, a programmatic `Focus()` no longer lights
  the focus-visible state until a key press or release or a navigation move; before, only a press on the element
  itself kept it dark. While an element holds focus, a key press lights it and a pointer press darkens it.

- `V.FocusScope` renders no element around its children, as React Aria's `FocusScope` renders none: the
  children stand among the parent's own children, between two undisplayed elements marking where the scope
  starts and ends. It returns a `ComponentNode` and takes only `children`, `key` and the scope's settings;
  `className`, `name`, `props`, `styles`, `refCallback`, `data`, `aria`, `events` and the `while*Class`
  arguments are gone. A styled container that is the scope sets its `FocusScope` element prop instead,
  `V.Div(className: ..., props: new FiberElementProps { FocusScope = new FocusScopeSettings(...) })`.

- An arrow or d-pad move inside a `singleTabStop` group steps to the next member (right, down) or the
  previous one (left, up) in hierarchy order, as React Aria's toolbar and radio group do, instead of
  following the engine's on-screen geometry. An element outside the group lying between two members no
  longer stops the move, and past the group's last member focus stays where it is rather than taking the
  engine's wrap round to the group's first; `wrap: true` asks for that wrap.

- `V.Portal(layer:)` and `V.WorldSpace` take `PanelFocusOrder.Appended` by default, so their elements follow
  the declaring tree's panel in Tab order, as a portal into `document.body` follows the rest of the document:
  Tab past that panel's last element enters the host, and Tab past the host's last element goes on. Before,
  the default was `Isolated`, where Tab wrapped within the host panel; pass `focusOrder:
  PanelFocusOrder.Isolated` to keep that.

- A drag's translate starts at the press, as dnd-kit's does, rather than at the point the activation constraint
  was met. With a `DragActivation` distance or delay the source jumps by the travel made before activation on
  the first move after it, and `DragStartArgs.Origin`, `DragMoveArgs.Delta`, `DragOverArgs.Delta` and
  `DragEndArgs.Delta` count from the press. With no constraint the press is the activation and nothing differs.
- Every droppable is measured without the translate, scale and rotate of its own, as dnd-kit measures its
  droppables without their transforms, so a droppable moved by its own `translate` or `scale` collides where its
  layout box sits. Only a `V.Sortable` item was measured that way.
- A droppable in a `V.WorldSpace` panel is a collision candidate for a drag from another runtime panel of the
  tree, and a drag from a `V.WorldSpace` panel reaches the other panels' droppables, mapped through
  `Camera.main`; it was reachable only from a drag on its own panel.

- `V.DndContext` returns a `ComponentNode` rather than an `ElementNode`: it is a component rendering the scope
  element, as dnd-kit's `DndContext` is, and it provides the drag state to the new hooks. It takes
  `onDragMove:`, `sensors:`, `modifiers:`, `autoScroll:` and `accessibility:` after `activation:`, so a
  call passing `className:` or a later argument by position passes it by name. `DndContextSettings` takes a
  further `OnDragMove` constructor argument.
- `V.DragOverlay` returns a `ComponentNode` rather than a `PortalNode`. In a mount on an editor panel it
  renders its positioner in place on that panel, where it shows the drag, rather than portalling it into the
  Game view's Overlay layer, where it stayed hidden and logged a warning.
- When a drag ends or is cancelled, a `V.DragOverlay` away from its source moves back onto it over 250 ms
  while the source is held at opacity 0, and keeps showing its last children meanwhile, as dnd-kit's default
  `dropAnimation` does; it used to hide at once. `dropAnimation: DndDropAnimation.None` hides it at once.
- A Space or Enter key reaching a draggable, or an element inside one, starts a keyboard drag under the
  default sensors, as dnd-kit's default `KeyboardSensor` does: the arrow keys move it 25 px, Space, Enter or
  Tab drop it and Escape cancels it. A scope that wants pointer drags only passes
  `sensors: new DndSensor[] { new DndPointerSensor() }`.
- A drag announces its start, target changes, drop and cancel to an enabled screen reader, as dnd-kit's
  default `announcements` do.
- While a pointer drag is active, a `ScrollView` around it scrolls when the pointer nears its edge, as
  dnd-kit's default `autoScroll` does; `autoScroll: DndAutoScroll.Disabled` turns it off. A dragged source
  inside a `ScrollView` that scrolls keeps under the pointer.
- A droppable on another screen-space runtime panel of the tree, such as one inside `V.Portal(layer:)`, is a
  collision candidate for a drag from the main panel, where only droppables on the source's own panel were.
- `DragOverArgs.Delta`, `DragEndArgs.Delta` and the source's translate are the drag's translate after the
  scope's modifiers.
- `com.velvet.core` depends on `com.unity.modules.accessibility`.

- A draggable whose own settings and scope both leave `activation:` unset starts its drag on the press,
  as dnd-kit's `PointerSensor` does with no activation constraint, so a press on a draggable button
  drags it rather than clicking it. The default used to be 4 px of travel; a scope or draggable that
  wants a press to stay a click below that travel passes `activation: new DragActivation(Distance: 4f)`.
  `new DragActivation()` and `DragActivation.Default` now mean no constraint, the same as
  `DragActivation.None`.

- A press on an interactive child that captures the pointer at its own pointer-down, such as a button
  inside a draggable card, or on a draggable inside such a control, drags once the activation constraint
  is met, and the control's click is aborted. Such a control used to block the drag, so a distance
  constraint alone does not bring the old behaviour back: a control inside a draggable that keeps its own pointer gesture (a slider, a scroller,
  a text field's selection) now needs `FiberElementProps.NoDrag` on it or on an element around it.

- When draggables nest, the innermost enabled one under the press takes it, as dnd-kit's innermost
  activator does. Under a distance constraint the inner one already won; with no constraint the outer
  one would otherwise activate first.

- Every `V.DragOverlay` declared under the dragging `V.DndContext` and mounted when the drag starts shows
  the drag preview, as every dnd-kit `DragOverlay` under that context does, and one under another scope
  stays hidden. Only one overlay in the tree used to show it, whichever scope it sat under, and mounting
  a second logged a warning.

- `Hooks.UseNavigation().Location` is null while the navigation state is `Idle`, as React Router's
  `navigation.location` is `undefined` then. It used to carry the committed location, the value
  `Hooks.UseLocation()` returns; read that hook for it instead.

- A loader error that no route in the matched chain declares an `errorElement` for renders a default
  error element in place of the root route, as React Router's does: a heading, the exception's message
  and its stack trace, with the exception logged in the editor and in a development build. Nothing
  rendered there before.

- The routing hooks act on the router the nearest `V.RouterProvider` publishes, as React Router's act on
  the nearest `RouterProvider`'s: `Hooks.UseNavigate` (and `V.Link`, `V.NavLink`, `V.Navigate`),
  `Hooks.UseNavigation`, `Hooks.UseBlocker`, the `Hooks.UseSearchParams` setter, and the route scope
  factory an Outlet asks. They used to act on the most recently constructed router, so a tree under one
  provider read one router and navigated another. `Router.Current` stays, and constructing a second
  router no longer logs a warning: two live routers are each driven through a provider of their own.

- `Hooks.UseLocation`, `Hooks.UseNavigate`, `Hooks.UseMatch`, `Hooks.UseSearchParams`,
  `Hooks.UseNavigation`, `Hooks.UseBlocker`, `Hooks.UseLoaderData` and `Hooks.UseRouteError` throw
  `InvalidOperationException` beneath no `V.RouterProvider`, as React Router's refuse to run outside a
  router, and so do `V.Link`, `V.NavLink` and `V.Navigate`. They used to answer with a null location or
  empty data, and to navigate the most recently constructed router, if any. `V.Outlet`, `Hooks.UseParams` and
  `Hooks.UseOutletContext` still answer there, as React Router's do. A tree that publishes the routing
  contexts by hand publishes `RouterContext.Router`, which is new, beside them.

- A `V.RouterProvider` rendered beneath another throws `InvalidOperationException`, as React Router
  refuses a `<Router>` inside another. It used to render, with the Outlets beneath it reading the route
  depth the outer router's Outlets had published.

- Navigation blocking follows React Router's `useBlocker`. `Hooks.UseBlocker` takes a `bool` or a
  predicate over `BlockerFunctionArgs` (`CurrentLocation`, `NextLocation`, `HistoryAction`), which
  replaces the NavigationAttempt type; the asynchronous predicate overloads of `Hooks.UseBlocker` and
  `RouteBlockerManager.Register` are gone. `RouteBlockerState.Location` replaces `Attempt`, and
  `Proceed` and `Reset` are delegates, null unless the Blocker is `Blocked`. A kept `Proceed` throws
  unless the Blocker is `Blocked`, and run while the Blocker holds a newer block it releases the
  navigation it was handed out for; a kept `Reset` returns the Blocker to `Idle` whatever it holds. The
  component calling `UseBlocker` re-renders when its Blocker's state changes.

- A router consults only the Blocker registered last, and warns each time it does so with more than one
  registered. Every registered Blocker used to be consulted.

- The Blocker is consulted before the path is matched and before Guards run, so a path no route matches
  is put to it, a Guard is not asked about an attempt it stopped, a Guard's redirect is not put to it,
  and a blocked attempt no longer cancels the navigation already in flight. A block stands until a
  navigation commits rather than until the next attempt reaches the Blocker, and disposing a Blocker's
  registration returns its state to `Idle`. The ResetAllBlocked method of `RouteBlockerManager` is gone.
  A `Proceeding` Blocker is no longer returned to `Idle` when the navigation it released ends while one
  that took over from it is under way, as React Router's stays proceeding until a navigation completes;
  it used to return to `Idle` as soon as the released navigation ended.

- Stepping `GoBack` or `GoForward` runs the destination route's loaders as a push to it does, since React
  Router keeps no loader data per history entry. It used to serve the data and errors the entry's
  loaders had produced when it was last shown, without running them.

- A navigation runs only the loaders React Router's default `shouldRevalidate` would: a route at the same
  place in the committed chain, over the same pathname as the URL spells it, keeps its data and its
  loader's token, so a parent layout's loader no longer runs again when only its child changes. No route keeps
  its data when the search changes, when the URL is the one already committed, or on the first navigation
  to commit after a route action started, and a route with no settled data keeps none either. Every
  matched loader used to run on every push, Back and Forward.

- `RouteMatch.PathnameBase` spells a literal segment as the URL does, as React Router's `pathnameBase`
  does, so a relative navigation from a route matched case-insensitively keeps the URL's case. It used to
  take the route pattern's spelling.

- `NavigationLifecycle` has a `Submitting` member, reported while a submission's guards and action run.

- A `Proceeding` Blocker stays `Proceeding` when the navigation it released is blocked by a Blocker
  registered after it, as React Router's does. It used to return to `Idle`.

- `Router.Current` falls back, when the router it names is disposed, to the most recently constructed
  earlier router that is neither disposed nor unreferenced. It used to become null, even with an earlier
  router still alive.

- `V.Particles` draws every active particle system under the effect — child systems and sub-emitters as well
  as the root — each with its own renderer's texture, and a system whose renderer is disabled in the source,
  or renders in `ParticleSystemRenderMode.None`, draws nothing, the root included. It used to draw the root
  alone, whatever its renderer's state.

- `V.SceneView` renders its camera outside Play Mode, once per repaint tick while the camera is enabled and
  still targets the element's texture, so an editor-hosted SceneView shows the scene live. Feeding the
  texture there used to be the caller's job; a tool that still calls `camera.Render()` itself now renders the
  camera twice as often.

- `V.Particles` draws the geometry each drawn system's renderer bakes instead of one quad per particle: mesh
  particles, stretched billboards, texture-sheet frames and trails now draw, trails with the trail material's
  texture and a Sprites-mode texture sheet with its first sprite's texture, and each triangle is culled as its
  material's `_Cull` property says. A particle is held to its renderer's maximum size against the element.
- `V.Particles` draws as each particle material says: its color (`_BaseColor`, twice a legacy shader's `_TintColor`,
  or `_Color`) multiplies each vertex color, and a material whose factors add or multiply composites that way over
  what lies beneath, through a new `Velvet/ParticleBlend` UI Toolkit shader, where it used to be alpha-blended.
  Systems draw by sorting layer, order in layer, render queue and distance with each renderer's sorting fudge
  added, where they used to draw in hierarchy order.
- `V.Particles` draws a system's trails whenever its Trails module is enabled with a trail material, a renderer in
  render mode `None` included, and draws no trails where the renderer has no trail material.
- `V.Particles(null, …)` with a positional `null` no longer compiles, since `V.Particles` also takes a
  `VisualEffect`; cast the `null` to `ParticleSystem` or name the argument `effect:`.
- The hidden host of a `V.Particles` element stands at the source's world position and rotation instead of far
  below the scene, and a sub-emitter cloned from outside the effect keeps its own world position and rotation,
  so Collision and Trigger sub-emitters fire against the colliders, planes and trigger volumes the source's
  would.
- `V.SceneView` shows the camera through a child laid over the element's content box instead of through the
  element's background image. The element's own background image stays beneath the picture while the camera is
  live, the picture stays beneath every other child of the element, the texture is sized to the content box
  rather than the laid-out box, and each corner of the picture is rounded along the content edge.
- Several `V.SceneView` elements can show one camera: while the camera is enabled, an element it does not target
  renders it into its own texture on its tick, in Play Mode too, and an element whose camera targets nothing
  claims it on its tick.
- A `Hooks.UseFrame` callback with a positive priority takes rendering over, as react-three-fiber's `useFrame`
  does: while it is mounted and attached, a `V.SceneView` in the same panel holds its enabled camera disabled,
  and enables it again once no such callback is left, and before the editor saves a scene or reloads assemblies.

- An args edit in the preview window, and `VelvetPreviewHost.UpdateArgs`, re-renders the mounted story with
  the new args instead of disposing it and mounting it again, so a component in the story that keeps its
  type and position keeps its state across a knob edit, as a Storybook story's does. A story that relied
  on each edit starting from fresh state now keeps what it had.

- `VelvetPreviewRegistry.DiscoverStories` throws an `InvalidOperationException` when two stories share a
  `Group/Name` id, naming each story whose id an earlier one holds together with that earlier one, and the
  preview window lists no stories and shows that message in its status line, as Storybook refuses to index
  duplicate story ids. It used to keep the first story with each id and drop the others with a warning.

- Every valid `[VelvetPreviewSetup]` an assembly declares runs for a story mount, ordered by declaring type
  and then method name, and their teardowns run in the reverse order; a teardown that throws is logged and
  the others still run. Each setup's `VelvetStyleHints.PreviewStyleSheet` is attached in setup order. Only the
  first setup found used to run, and the rest were ignored with a warning.

- `VelvetPreviewRegistry.RunSetupFor` returns a `VelvetPreviewEnvironment`, whose `StyleSheets` holds the
  `VelvetStyleHints.PreviewStyleSheet` each setup published, in setup order, and whose `Dispose` runs the
  setups' teardowns in reverse order. It used to return an `IDisposable`, or `null` when no setup supplied a
  teardown, and left only the last setup's sheet in `VelvetStyleHints.PreviewStyleSheet` for its caller to
  read. Each setup's hint is now consumed as that setup returns, `Dispose` runs the teardowns once however
  often it is called, and the method returns `null` only for a `null` assembly.

- The panel Velvet creates for a `V.Portal(layer:)` or a `V.WorldSpace` now carries the project's stylesheets,
  as a DOM portal's children match every stylesheet of the document. Its root carries the stylesheets of the
  element the tree was mounted on and of that element's ancestors, less any the host's panel already holds
  such as its copied theme. A sheet that comes off there comes off the host. Velvet's utility stylesheet is
  carried only while the host does not reach it another way. For a tree mounted in an `EditorWindow`, the
  sheets carried include the editor's default control sheet on the window's root. These hosts took none of
  the project's own stylesheets before.
- That host's root takes the classes of the declaring panel's root and of the element under it, a
  `UIDocument`'s root, as a DOM portal's children match the classes of `html` and `body`. A `unity-` class
  is not taken. A class on an element between those and the mount element is not taken either, so a token
  such as `.theme-ocean { --color-primary: … }` reaches portal children from the document's root and not
  from inside the page.
- The `dark` class on that host's root comes from those two document roots, or from `VelvetTheme.IsDark`
  when the mount element or an ancestor is bound to the theme, rather than from the portal's own position. A
  portal inside a `dark` section of a light page now renders light.
- A carried `:root` rule, a document class, or the copied theme's own `.unity-ui-document__root` rule does
  not paint, move or size that host's root or move its content. The root's background, border, padding,
  margin, flex layout, opacity, display and overflow are held at their initial values inline, and its
  position, offsets and size at the values that fill its panel. A layer host's transform is held too; a
  world-space host keeps the size it was given and the transform its GameObject gives it. Custom and
  inherited properties still reach the portal's children. A project theme that pads or aligns
  `.unity-ui-document__root` no longer insets or aligns layer portals' content. A layer host under a theme
  without a document-root rule, such as the empty one an editor-hosted tree's hosts get, now fills its panel.

- `V.Mount` logs a warning, once per run, when its target is on a panel — or later added to one — that does
  not carry the bundled utility stylesheet on the target or any ancestor, directly or through an `@import`.
  A panel without the sheet used to resolve every utility the sheet declares to nothing with no diagnostic,
  while the families Velvet realises in C# kept working. A test that asserts no unexpected log around a
  mount onto a panel without the sheet sees this one whenever that mount is the run's first such mount, so
  its outcome can hang on test order; attach the sheet with `VelvetStyleUtilities.AttachTo` first. A
  project that leaves the sheet's holder out of its builds under **Project Settings ▸ Velvet** gets no
  warning.

- The panel Velvet creates for a `V.Portal(layer:)` or a `V.WorldSpace` gets the bundled utility stylesheet on
  its root when the element the tree was mounted on, or an ancestor of it, reaches the sheet and the host does
  not reach it another way, looked at when the portal mounts, on each patch of the portal and at each update
  of the panel the tree was mounted on. Its root also takes a `dark` class, following `VelvetTheme.IsDark`
  when one of those elements is a root `VelvetStyleUtilities.BindThemeTo` bound. These hosts took
  the declaring panel's theme and no other stylesheet, so a sheet attached with `AttachTo` did not reach a layer or world-space
  portal's children, and the utility classes the sheet declares resolved to nothing there. Children styled
  around that gap now resolve those classes.
- A `V.Portal` into an element the app passes or registers logs the warning `V.Mount` gives for a panel
  without the utility stylesheet, when that element's panel lacks it. The warning is still made once per run.
- The missing-sheet warning `V.Mount` gives is decided at the target panel's next update, and again at the
  next update after the target is added to a panel, rather than when `V.Mount` is called. A sheet attached
  after `V.Mount` but before that update no longer draws the warning, and a test asserting no unexpected log
  sees it only when that panel updates inside the test.

- An `OperationCanceledException` thrown by a `UseMutation` `OnError` handler is logged with
  `Debug.LogException`, as any other exception the handler throws is. It was not logged.

- `VelvetTaskCompletionSource.SetException` and `VelvetTask.FromException` handed an
  `OperationCanceledException` fault the task with it, as a TaskCompletionSource's `SetException` and
  Task.FromException do, where they cancelled it. `VelvetTask.WhenAll`, `AttachExternalCancellation`,
  `Preserve()` and `Forget()` treat such a task as faulted. An `async VelvetTask` method that throws one still
  ends cancelled, as an `async Task` method does.

- The `Task` `AsTask()` returns for a cancelled `VelvetTask` throws that task's own
  `OperationCanceledException`, as ValueTask.AsTask()'s does, where it threw a new TaskCanceledException.

- A `V.TextField` `maxLength:` no longer truncates a value set by code, or an edit a delayed field has not
  committed, when the limit changes or the field loses focus. Typing or pasting past the limit inserts only
  as many characters as fit at the caret.

- A `Hooks.UseQuery` request now pauses while the device reads as offline, as TanStack Query's queries do
  under their default `networkMode: 'online'`: a query mounted offline does not call its query function,
  reports `FetchStatus` `Paused` with `IsPaused` true and `IsFetching` false, and fetches once a connection
  is back, where it used to call the function at once and fail or hang. The reading is
  `Application.internetReachability`, which reports a server on a local network as unreachable on some
  devices; set `NetworkSignals.IsOnline` once at startup to read connectivity another way, or give a query
  (`QueryOptions.NetworkMode`) or every query (`QueryClientOptions.NetworkMode`) `NetworkMode.Always` to never
  wait, or `OfflineFirst` to make the first request whatever the connection.
- A query's retry now waits until the application is visible, and outside `NetworkMode.Always` until the
  device is online, as TanStack Query's retryer does. `QueryResult.IsFetching`, `Query.IsFetching` and
  `QueryState<T>.IsFetching` are false while a request is paused, and `QueryFilters.FetchStatus` `Fetching`
  does not select a paused entry; an entry with a paused request is not collected.
- A `QueryKey` part that is a tuple, a record or an anonymous type now compares by its members, each compared
  as a part is, as TanStack Query's `hashKey` serialises an object whole: a key holding a record that holds a
  list, an array or a dictionary is equal to another key holding an equal record, where it used to be equal
  only when the collection was the same instance, and keys holding the same instance are still equal. A record
  that declares its own `Equals`, and a class that declares none, compare as before. A filter key leads an entry's record the
  way it leads an array. `QueryKey.ToString` prints such a part's members rather than the part's own
  `ToString`.

- `blur-sm` blurs by 8px. A caller who wrote `blur-sm` for a 4px blur now gets 8px; `blur-xs` is the 4px step.

- `rounded-sm` is 4px, where it was 2px. `rounded-xs` (2px) and `rounded-4xl` (32px) resolve on the
  all-corners utility and on the logical and per-corner forms (`rounded-s-sm`, `rounded-ss-xs`,
  `rounded-e-4xl`).

- The palette is Tailwind v4's, where it was v3's: `bg-red-500` is `#fb2c36` rather than `#ef4444`, and a palette
  name takes its v4 colour wherever a colour is taken (`text-*`, `border-*`, `ring-*`, `divide-*`, a gradient stop,
  …). Each OKLCH colour is converted to sRGB with a channel outside 0–1 clipped. 185 of the 242 colours v3 had
  change; write the old value in brackets (`bg-[#ef4444]`) to keep one.
- `ring-<n>`, `outline-<n>`, `ring-offset-<n>` and `outline-offset-<n>` take any whole number of pixels, as Tailwind v4
  does. `ring-3` drew no ring and `ring-offset-3` no offset; both now draw 3px.
- `inset-shadow-2xs`, `inset-shadow-xs`, `inset-shadow-sm` and `inset-shadow-[…]` draw their inset layers, beside any
  `shadow-*`, and `inset-shadow-none` removes them. They drew nothing. On a text field they style the input box, as
  `shadow-*` does.
- `shadow-<color>`, `inset-shadow-<color>` and their `/N` colour the layers of their own family, and a size's `/N`
  (`shadow-lg/50`) sets its layers' alpha, as Tailwind v4 does. They drew nothing, and `shadow-[#f00]` now colours the
  shadow rather than reading as no shadow.
- `shadow-inherit`, `inset-shadow-inherit` and `inset-ring-inherit` take their family's colour from the parent
  element, as Tailwind v4's `inherit` does for the colour variable. They were ignored.
- `inset-ring`, `inset-ring-<n>`, `inset-ring-[…]` and `inset-ring-<color>` draw an inset band, in the element's text
  colour where no colour class names one. They drew nothing.
- `-outline-offset-<n>`, `-outline-offset-[…]` and a negative `outline-offset-[…]` draw the outline inside the
  element's edge. They drew it at the edge.
- The role colours of the default theme that were Tailwind v3 palette colours (`--color-primary`, `--color-danger`,
  `--color-text`, the light border and surface ladders, the dark background, …) take the matching v4 palette colour:
  `bg-primary` is `rgb(21, 93, 252)` rather than `rgb(37, 99, 235)`.

- A router stands on a location from construction, as `createMemoryRouter` does: on `/` with no options, its
  history's one entry keyed `"default"`, so a first navigation pushes a second entry with a key of its own where it
  used to make the first. A starting path no route matches commits its 404 at once, and one whose routes have
  a loader, a guard or a `redirectTo` runs them at once without reporting a navigation, the routes rendering
  their hydrate fallback, or nothing, until the first commit.

- The router's committed errors are React Router's `state.errors`: a loader's, an action's, a Suspend
  loader's and a refusal are keyed in `Router.CurrentLoaderErrors` by the nearest route with an `errorElement`
  at or above the route that failed, or by the root, where they were keyed by the failing route; of two reaching
  one route the higher failing route's stays, and a failed action takes the place of the first loader error.
  The highest matched route an error is keyed by renders it, and `Hooks.UseRouteError` reads the error keyed
  by the route it is called in. Loader data kept from the location before is dropped below that route, and a
  failed action's own route holds no data when no loader fails.

- The router commits an `ErrorResponse` as React Router's `getInternalRouterError` makes one: 405
  "Method Not Allowed" for a submission whose method no form takes and for one to a route with no action,
  400 "Bad Request" for a body it cannot encode, where it committed an `InvalidOperationException`. Its
  `Detail` is `"Error: "` and the message, the 404's included.
- `ISearchParams` and `SearchParams` are URLSearchParams: entries keep the order they were added in, a
  repeated name included, `Keys` names every entry, `Size` replaces `Count` and counts entries, and enumeration
  yields the entries. A query is written as URLSearchParams writes itself, a space as `+`, by the
  `Hooks.UseSearchParams` setter too.
- A relative target's leading `..` counts only the matched routes that contribute a path, as React Router's
  `getResolveToMatches` does, and a `..` after the first segment drops a URL segment rather than a route.
- `V.Route` takes a route with neither `element` nor `redirectTo`, which renders its matched child route in
  its place.
- JavaScript's `String` of an object read as a plain one is `[object Object]` in a submission body, where the
  object's own `ToString()` was written.

- A route whose `Path` is `""` is no longer an index route: it matches its parent's path like any other path,
  with no index score and no `?index` targeting. Mark an index route with `index: true`.
- A route whose `Path` is null is a pathless route, matched only through a child or as an index route, and
  `V.Route` accepts a null `path`. A route whose `Path` is `""` and has children now matches its own path with
  an empty `V.Outlet`, where it was treated as pathless.
- Routes are ranked as React Router's `flattenRoutes`, `rankRouteBranches` and `computeScore` rank them: each
  segment of a joined path scores one beside its own value, a path with optional segments makes a branch for
  each way of keeping or dropping them in place of a bonus for each optional segment, and equal scores are
  ordered as React Router's comparator orders them under V8 14.0's sort, over branches listed each child's ahead of
  its parent's where declaration order listed the parent first. An absolute child path must start with its
  parents' joined path and is matched after it; one that does not makes the tree throw `ArgumentException`.
- Every submission reads its body as React Router does, a `post` included: the default encoding reads it
  through the URLSearchParams constructor into a `FormData`, so a body it cannot encode commits "Unable to
  encode submission body" whatever the method. A `get`'s query is written as URLSearchParams writes itself,
  a space as `+`.
- `NavigationState.FormMethod` is upper-case, as React Router 7 reports it, and `NavigationState.FormData` is
  a `FormData`, null for a JSON or `text/plain` submission.

- `Animator.Animate` on one target with no transition runs each value on Framer Motion's default for it rather than a
  0.3 s ease-out: `X`, `Y` and `RotateDeg` on a spring of 500 stiffness and 25 damping, `Scale` on one of 550 and 30
  (critically damped toward 0), `Opacity` on a 0.3 s `cubic-bezier(0.25, 0.1, 0.35, 1)` ease, and more than two
  keyframes over 0.8 s. A transition setting only `DelaySec` and the repeat options counts as none. A sequence segment
  with no transition still runs 0.3 s on an ease-out.
- `Animator.Animate` honours `Repeat`, `RepeatType` and `RepeatDelaySec`, which it ignored, and eases more than two
  keyframes pair by pair rather than across the whole run. A `Spring` on a single call runs from the first keyframe to
  the last whatever their number, and a sequence segment's spring of two keyframes is measured over their travel.
- `StyleTransitionConfig.DurationSec` defaults to 0.3 s rather than 0, so a `Tween` or `Bezier` naming no duration
  animates rather than landing at once, and an exit on one animates too.
- A variant play whose transition sets nothing but `DelaySec`, the repeat options and the child-orchestration knobs
  (`StaggerChildrenSec`, `StaggerDirection`, `DelayChildrenSec`, `When`) runs Framer Motion's per-value defaults after
  that delay, as does `V.Motion` given only `delay:`, rather than a zero-length tween or the `Fade` preset.
- A `scale-y-*` channel on the per-value defaults runs on the scale spring rather than on the generic 100 / 10 one.
- A `StylePropertyTransition` replaces the enclosing config for its property, as Framer Motion's per-value
  transition does: a null `DurationSec` runs 0.3 s and a null `Easing` eases out rather than taking the
  enclosing config's, and a null `DelaySec` starts the value with the play rather than after the config's
  `DelaySec`, an entry with a shorter delay starting before the play's own. An entry naming neither duration nor
  easing runs its value on Framer Motion's default transition for it, a spring for a transform, and a play holding
  one, or a play of no duration holding an entry that animates, moves onto the per-frame driver. `Animator.Animate` reads `PropertyOverrides` as per-value transitions.
- A `Spring` on a single `Animator.Animate` call ends where it comes to rest rather than at 20 s.
- A variant play and a `layoutId` move on a `V.Motion` given `duration:`, `easing:` or `delay:` and no
  `transition:` run on a tween of those, 0.3 s on an ease-out where they name neither (and the per-value defaults
  for a variant play given only `delay:`), rather than the `Fade` preset's timing.
- A `UseAnimationSequence` `To` step naming no transition plays 0.3 s on an ease-out, as a segment of Framer
  Motion's sequence does, rather than reusing an earlier step's transition or the `Fade` preset.
- `AnimateControls.Finished` never settles once `Stop()` or `Cancel()` ends the animation, as Framer Motion's
  `finished` promise does not; it was cancelled.

- The ease-in-out each `transition-*` utility but `transition-none` bundles runs along Tailwind v4's default curve,
  `cubic-bezier(0.4, 0, 0.2, 1)`.
- The bundled `ease-in`, `ease-out` and `ease-in-out` classes, behind a variant or not, run their transition along Tailwind v4's curves
  (`cubic-bezier(0.4, 0, 1, 1)`, `(0, 0, 0.2, 1)` and `(0.4, 0, 0.2, 1)`) in place of the UI Toolkit keywords of the
  same names that approximated them. A project's `--ease-in`, `--ease-out` or `--ease-in-out` replaces the curve.

- `hue-rotate-*`, `-hue-rotate-*`, `hue-rotate-[…]` and `animate-hue` render the Filter Effects hue-rotation
  matrix on the pixel's sRGB value, as CSS `hue-rotate()` does, through a first-party `Velvet/FilterHueRotate`
  custom filter. They used UI Toolkit's own hue-rotate filter type, whose colours differ from CSS:
  `hue-rotate-90` turned `#ff0000` into `(0, 111, 0)` where CSS gives `(0, 91, 0)`. The element's `style.filter`
  now holds a `FilterFunctionType.Custom` function for them rather than `FilterFunctionType.HueRotate`, and the
  shader is one more that a player build includes unless Project Settings ▸ Velvet leaves it out.
- A `V.Label`, a text leaf or a `V.Button` is as tall as CSS makes it: one line box of its line height per line,
  single lines included. A single line used to take the font's own content height, so in the runtime theme's font
  `text-base` was 20px tall rather than 24px, `text-base leading-8` 20px rather than 32px, and `text-4xl` 44px
  rather than 40px. Text no `leading-*` or `text-<size>` reaches takes Tailwind's preflight line height of 1.5,
  where it used to take the font's own, and an `lh` length under it is 1.5 times the font size; an `rlh` length is
  24px, the preflight's line height at the 16px a rem is taken at, where it used to be the font's own line height.
  Once such text takes more than one line, by a newline, a `<br>` or wrapping, the string it shows carries a
  `<line-height=1.5em>` tag where it used to be the text as written; `leading-[normal]` takes it away. A
  single-line `V.TextField`'s input text is one line box tall too.
- The glyphs of a label's or a text leaf's text sit half the leading below the top of the first line box at any
  box height, as in CSS, including a box a `max-h-*` caps below its lines, whose text starts at its top. The
  element is aligned `middle-*` with the horizontal part of the alignment its rules or its parent give it, so
  `text-top-left`, `text-top-center` and the default alignment now place text as `text-left` and `text-center` do,
  and text in a box taller than its lines, which a `middle-*` alignment used to centre, sits in the first line
  box. A `V.Button`'s lines stay centred in the button, as a browser centres a button's content.
- A multiline `V.TextField`'s input lines, and the lines of a label or button with `enableRichText = false`, are
  one line height apart, as CSS spaces them, where they took the font's own line advance: in Inter, in an editor panel,
  a 16px multiline field's lines were 19.5px apart rather than 24px. Such text has no `<line-height>` tag to carry,
  so its element's inline `-unity-font-definition` is a copy of its font asset with the line height as its ascent
  and descent, and a label with rich text off is aligned `upper-*`, which puts its first line box at the top of its
  box. The faces the asset's weight and fallback tables name are copied the same way, a font stack's as it grows
  and is released, and the element's own rules' font is the one copied. The copy of a dynamic font asset has an
  atlas of its own. Turning `enableRichText` off or on after layout takes effect on the frame after.
- A `<voffset>` in the application's own rich text is shown that much above or below the line box placement, and
  its own `</voffset>` or `</line-height>` no longer ends the placement or the line height Velvet writes around it.

- A `V.Suspense` showing its children no longer swaps them for its fallback when a `Hooks.UseTransition`
  update suspends them. The children stay on screen, `isPending` stays true, and the update renders when the
  resource settles. Code that relied on the fallback appearing during a transition now sees the previous
  content instead. Updates that are not a transition's, and boundaries that show no children yet, show the
  fallback as before.

- A bracket the browser would reject in the CSS Tailwind writes for it now leaves the class inert, as it does
  in a browser: `rotate-[45]` and `rotate-[0]` (the `rotate` property takes no bare number), `bg-linear-[45]` (a
  bare angle other than `0`) and `bg-conic-[45deg]` (an angle without `from`). `-bg-linear-[…]` takes only a
  lower-case unit, as Tailwind's angle test does.

- A bare number other than `0` in a length bracket leaves the class inert, as the browser rejects the
  unitless length Tailwind writes for it: `w-[120]`, `p-[4]`, `rounded-[8]`, `translate-x-[10]`, `blur-[2]`,
  `gap-[8]`, `border-[2]`, `ring-[2]` and `shadow-[0_4_8_#000]` used to read it as pixels. `text-[16]` and
  `text-[0]` are a colour to Tailwind and inert too. `outline-[2]` and `outline-[50%]` draw a 3px outline, the
  width the browser falls back to when it drops theirs.

- `ring` is 1px, Tailwind v4's default ring width, where it was 3px, and a ring or outline naming no colour is
  drawn in the element's text colour, as v4's `var(--tw-ring-color, currentcolor)` and `outline-color`'s
  initial value are, where it was blue-500 (at half alpha for a ring). A colour class alone (`ring-red-500`) no
  longer draws a ring, and a ring colour no longer colours an outline.
- `grid-cols-[5]` leaves the class inert, as the browser rejects the `grid-template-columns: 5` Tailwind writes
  for it; it used to lay out five columns. `grid-cols-[repeat(5,minmax(0,1fr))]`, the list `grid-cols-5` writes,
  is five columns.

- A value Tailwind composes into an element's box-shadow and the browser rejects now discards the box-shadow,
  as the browser does: a ring colour or offset colour that is no colour (`ring ring-[5PX]`), a ring width that
  is no length (`ring-[length:2]`, `ring-[length:0]`, `ring-[length:50%]`), a `shadow-[…]` that is no box-shadow
  (`shadow-[0_4_8_#000]`, `shadow-[4px]`), a shadow colour that is no colour where a shadow reads it
  (`shadow-md shadow-[#ggg]`), and the same values written through `inset-ring-*` and `inset-shadow-*`, take the
  ring and every `shadow-*` on the element with them, where the ring used to draw. Each value is the one Tailwind
  emits last, whatever order the classes are written in, so `ring-[length:2] ring-4` discards while
  `ring-2 ring-red-500 ring-[5PX]` draws a red ring. A
  `from-` / `via-` / `to-` colour that is no colour leaves its stop transparent, and a position that is no length
  or percentage returns the stop to its initial one. A function, `var()`, `currentColor`, a colour name or a unit
  Velvet does not read is not taken for a rejected value.
- A value the browser rejects in one utility of a family Tailwind composes through one declaration — the
  per-axis translate and scale, the per-axis skew, the filters — now draws nothing for the whole family wherever
  it holds its property, as the browser drops the shared declaration: `translate-x-4 translate-y-[10]` and
  `!translate-y-[10] translate-x-4` no longer translate along x, and `drop-shadow-lg blur-[2]` no longer draws its
  drop shadow. Which value holds is decided as for any value, by importance, then variant rank, then Tailwind's
  order, so `blur-[2] blur-md` still blurs, `translate-x-4 hover:translate-y-[10]` stops translating only while
  hovered, and `translate-x-4 hover:translate-y-[10] focus:translate-y-2` translates while hovered and focused.
  `blur-[-2px]` is such a value too, as is a `drop-shadow-[…]` that is no shadow and no colour
  (`blur-sm drop-shadow-[foo]` no longer blurs), and a `drop-shadow-[…]` colour the browser rejects where the drawn
  shadow reads it (`blur-sm drop-shadow-lg drop-shadow-[#ggg]`, with or without an alpha modifier).
- A `scale-[…]` at or above the rank of the `scale-x-*` and `scale-y-*` on an element is now drawn alone, as the
  `scale` it declares replaces theirs: `scale-[1.4] scale-x-[.5]` scales by 1.4, where it used to scale x by 0.5.
  Below an axis utility it no longer stands in for the other axis: `scale-[1.5] hover:scale-x-[2]` scales y by 1
  while hovered, where it used to keep 1.5. A `V.Motion` pose animates such a `scale-[…]` toward its value, where
  the swap used to land it.

- An important modifier written ahead of the variants, which Tailwind generates no rule for, leaves the class
  inert: `!selection:bg-red-500` no longer colours a text field's selection. Write `selection:!bg-red-500`.

- A math function whose result falls below the range its property takes is raised into it, as CSS clamps a
  calculation: `w-[calc(-5px)]`, `p-[calc(-5px)]`, `rounded-[…]`, `border-[…]`, `text-[…]`, `basis-[…]` and
  `blur-[…]` stop at 0 where they wrote the negative value, `opacity-[calc(2)]` is 1 and `opacity-[calc(-1)]` 0
  where both were declined, and a negative `duration-[…]`, `grow-[…]` or filter amount is 0.
- A negative literal where the CSS property takes none leaves the class inert, as the browser rejects it:
  `w-[-5px]`, `p-[-5px]`, `rounded-[-2px]`, `border-[-2px]`, `text-[-4px]`, `basis-[-4px]` and `w-[-2em]` used to
  write the negative value. `opacity-[2]` is 1 and `opacity-[-1]` 0, where both were declined.
- A math function of a type the browser does not take in a family Tailwind composes through one declaration
  discards that declaration, as a rejected literal does: `translate-x-4 translate-y-[calc(45deg)]` translates
  nowhere, where the x translation used to apply.

- Pending passive effects (`Hooks.UseEffect` setups and cleanups, and the `V.AnimatePresence` `onExitComplete` calls
  that wait for the passive-effect flush) now run before each render the update scheduler drains, before each
  resumed slice of a time-sliced pass, and before a `V.VirtualList` renders a range as it scrolls or is resized, as
  React flushes them before it starts a render. Before, a drain rendered without flushing them first. The drains
  include the immediate tier's scheduled drain, the Transition tier's drain, and the follow-up pass that a layout
  effect or callback ref writing state asks for, on a mount as well. On the immediate tier, an update an effect
  schedules from that flush renders in the same pass, where it used to wait for the next drain.
  - A presence whose child left with nothing to exit, and whose key comes back before the passive flush, calls
    `onExitComplete` before the render that adds the key back; it used to be called after that render.
  - A component whose layout effect writes state on mount runs its `UseEffect` setups before the re-render that
    write asks for, as React does; before, the re-render committed first and the setups ran at the next frame.
- The passive effects of a commit React gives the sync lane now run as that commit ends instead of at the next
  frame: the commit of a discrete event (a click, a value change, a drag), the follow-up commit of a layout effect or
  callback ref writing state, and the render of a `Hooks.UseSyncExternalStore` change. An update one of those
  effects schedules waits for the next drain. A drain of ordinary updates still leaves its effects to the next frame.
- An update that a passive effect schedules while the Transition tier's drain, or the resume of a parked transition
  pass, flushes renders before that transition, as React renders the higher-priority lane first. It used to render
  after the transition.
- A passive-effect flush reached from inside a layout commit or a running passive drain, such as a click an effect
  dispatches, no longer runs the pending effects there, as React refuses to flush passive effects inside a commit.
  They wait for the passive drain under way to finish, or, from a layout commit, for the next flush.
- Pending passive effects stay per root, a deviation from React: React flushes every root's pending effects before
  any root renders, while a render of one `V.Mount` root flushes only its own.

- A weight or italic class with no `font-<name>` beside it keeps the family it inherits, as CSS's
  `font-weight` keeps an inherited `font-family`: `font-bold` inside a `font-serif` container renders
  `serif`'s `Bold` entry. It used to resolve against `VelvetFonts.DefaultFamily`, so the bold text
  switched to the default family whenever that named another one. The family is the one named by the
  nearest ancestor with a `font-<name>` class that Velvet rendered; `DefaultFamily` applies when there is
  none. A root carrying the class outside Velvet (a portal target, layer root, world-space panel root or
  another `V.Mount`) is not seen, so register its family as the default as well.

- A `layoutId` move takes the curve its transition's `Type` names, as a variant swap does: a tween by
  `DurationSec` / `Easing`, eased as a USS transition is, a bezier by its control points, a spring by its knobs, each after
  `DelaySec`, and a zero duration such as `StyleTransitionConfig.None` lands it at once. It always
  sprang on the transition's `Stiffness` / `Damping` / `Mass`, so a Motion whose transition is a tween
  for its variants — `StyleTransitionConfig`'s default `Type` — sprang on the default knobs. Set a
  spring as the transition, or as its `Layout`, to keep the spring.

- A `layoutId` Motion whose caller gives `V.Motion` no `transition`, `duration`, `easing` or `delay` moves
  on Framer's default layout transition, a 0.45 s tween eased by `cubic-bezier(0.4, 0, 0.1, 1)`, instead
  of a spring with stiffness 100, damping 10 and mass 1.

- An `AnimatePresence` child removed with nothing to animate — no `V.Motion` in it, or a `StyleTransitionConfig.None`
  or zero-duration exit — stays on screen for the render that removed it and leaves on the render its exit's
  completion asks for, as Framer Motion's `AnimatePresence` drops a child only on a render after its exit completes.
  That exit completes in the passive-effect flush after the removing render. It used to leave in the removing
  render itself. Under `AnimatePresenceMode.Wait` such a child now holds the new children back until that later
  render, as an animated exit does; they used to mount in the removing render.

- Under `AnimatePresenceMode.Wait`, while a removed child is exiting the presence renders the exiting children
  alone, as Framer Motion's `mode="wait"` does: a child that stays is not rendered until the exits complete, so one
  already mounted unmounts with no exit and mounts again with its enter. It used to stay mounted, and only a key
  new to the presence was held back.

- A key added to a propagating inner `AnimatePresence` while its enclosing child leaves now mounts even when it
  places nothing: its effects run and its exit completes after the commit, as Framer Motion's `PresenceChild` mounts
  it with `isPresent=false`. It used to be discarded unmounted.

- Disposing a mounted tree now first completes an `AnimatePresence` exit still waiting on the passive-effect flush,
  so its `onExitComplete` runs, as React flushes a root's pending passive effects before unmounting it. It used to
  be left to the panel's passive-effect tick after the tree was gone.

- An `AnimatePresence` that leaves the tree while an exit is still playing, whether its tree is disposed (a mounted
  tree or a bare `Reconciler`) or its parent stops rendering it, now completes that exit, and its `onExitComplete`
  runs once, as Framer Motion lets an unmounted presence's exit animations run out and then calls it. It used to
  run only on a root with no panel, and only for an exit played by a Motion below the child's own. An exit also
  waiting on a `layoutId` landing still does not complete.

- A ring and an outline are two bands, as they are two properties in the browser: `ring-4 outline-2` draws a 4px ring
  and a 2px outline, each with its own colour and offset. They shared one band whose width and offset either family
  wrote, so `ring-4 outline-2` was a single 2px band and `outline-offset-*` moved a ring. `ring-none` is no Tailwind
  utility and no longer cancels a ring.
- `ring-offset-<n>` fills the gap in `--tw-ring-offset-color` (white unless `ring-offset-<colour>` names another), over
  the ring, whose spread is its width plus the offset. A `shadow-*`, `inset-ring` or `inset-shadow-*` beside a
  `ring-offset-*` draws the fill too. The gap was transparent.
- A ring, a ring-offset fill and an outline take the shape CSS gives them. A ring's corner grows by the spread by CSS
  Backgrounds 3's formula, so the ring of a square element is square where it was rounded by the ring's width; an
  outline's corner grows by its offset and width where the element is rounded and stays square where it is not; the ring's
  hole keeps the element's radii. A `ring-inset` band takes the padding box's corners, each axis less the border beside it.
- An outline paints after all the content of its stacking context, over a later sibling's face, and is clipped by its
  own ancestors' overflow only. It was clipped by the nearest ancestor that clips, and content after that ancestor covered
  it.
- A ring, ring-offset and outline colour takes an opacity modifier in the forms Tailwind generates (`/50`, `/2.25`,
  `/[0.33]`, `/[33.3%]`), and `ring-inherit`, `ring-offset-inherit` and `outline-inherit` take the parent's value. A
  modifier outside 0–100% on a ring, ring-offset, `shadow-*`, `inset-shadow-*` or `inset-ring-*` colour discards the
  element's whole box-shadow, as the browser does, and on an outline palette colour leaves it the text colour. A shadow size's
  `/N` outside that range beside a shadow colour discards the box-shadow, and on the size alone clamps its own colour's
  alpha. They drew as if the modifier were within range or were ignored.
- A colour modifier that is not a form Tailwind generates (`/05`, `/.5`, `/50.0`) names no utility. `/05` read as 5%.
- A change of a `shadow-*` colour re-paints the descendants that inherit it with `shadow-inherit`, though their own
  classes did not change.
- `text-current`, `bg-current`, `border-current`, their `/N` modifiers and `text-inherit`, `bg-inherit` and
  `border-inherit` read the element's text colour and the parent's value of the same property, and follow them
  as they change, as Tailwind v4 writes `currentcolor` and `inherit`. `inherit` takes no modifier. They matched no rule.
- A colour utility whose opacity modifier lies outside 0–100% and which Tailwind writes through a variable (a palette
  name, a bracketed `var()`) leaves its property unset, as the browser computes the invalid `color-mix()`:
  `bg-red-500/150` is transparent over an earlier `bg-blue-500` (it was declined, so the earlier colour stood),
  `text-red-500/150` inherits the text colour and `border-red-500/150` is `currentcolor`. Any other colour
  (`bg-[#f00]/150`, `bg-transparent/150`) is declined, as the browser drops it, and `bg-current/150` is `currentcolor`.

- `Hooks.UseSyncExternalStore` and `Hooks.UseStore` subscribe in the passive effects of the commit rather than
  during render, as React subscribes in an effect. A render that throws or suspends, and an attempt a render-phase
  update re-runs, subscribe and unsubscribe nothing, so an unsubscribe error no longer reaches `OnCaughtError` for
  a subscription such a render made. A store change between the render and the subscription re-renders the
  component once it is subscribed; a store change after a first render but before the passive effects flush is no
  longer notified until then.
- What a render reads through either hook reaches a store-change notification and the passive effects only once
  that render settles, so a later render of the component that does not settle no longer changes the store,
  `subscribe` or selector an earlier commit's pending effects use.
- The re-render `UseSyncExternalStore` or `UseStore` forces when its subscription or snapshot check finds the store
  changed takes the sync lane and renders before the passive flush that ran the check returns, the scheduled
  post-paint flush included, counting toward the drain's update-depth limit, as React renders sync-lane work before
  its passive flush returns. It used to wait for a later drain. An ordinary update the same flush schedules renders
  with it, as React's sync flush renders the default lane too; one a flush schedules without such a re-render still
  waits for the next drain.
- A `Hooks.UseStore` store-change notification re-renders the component on the Urgent lane, as a
  `Hooks.UseSyncExternalStore` notification does and as Zustand's `useStore` does on React's
  `useSyncExternalStore`, including inside `startTransition`, and is flushed from the main thread's posted work. It
  used to take the caller's lane, so a store write inside `startTransition` was deferred to the Transition lane and
  held the transition's `isPending`; such a callback now queues no transition work and settles as it returns. A
  reader whose render read a drain pass's pin the store had moved past asks for its catch-up render the same way,
  from the commit's passive effects, where it used to ask for a Normal-lane render from the render itself.
- Under StrictMode in the Editor, `subscribe` is called, unsubscribed and called again when a component mounts, as
  React double-invokes a mount effect.

- An error no error boundary catches now unmounts the whole tree `V.Mount` mounted, as React's root does: its
  effect cleanups run as on any unmount, and the mount target is left without the tree's elements. It used to be
  logged while the tree stayed mounted, with the component that threw left showing its last committed output.
  This covers an error thrown while rendering, by an effect or its cleanup, by a ref callback, or by anything
  else Velvet hands to the nearest boundary; a click or other event handler's exception still reaches no
  boundary and unmounts nothing. Where a boundary's own fallback content throws and no boundary above catches
  that, only the fallback content's error is reported, as React discards the catch the boundary was showing.
  An effect cleanup that throws while `MountedTree.Dispose` runs is now reported, once every cleanup has run,
  rather than dropped.
- An error a cleanup or ref throws while an error boundary swaps its fallback in, or while it disposes the output
  that failed, is now that boundary's own, as React's commit-phase capture starts at the nearest mounted ancestor:
  it is reported to `OnCaughtError` after the error the boundary caught, and the fallback renders again with it.
  It used to go to the boundaries above, replacing the boundary that had just caught.
- An error a cleanup throws in a component that is being removed reaches the nearest boundary outside what is
  being removed, as React's commit-phase capture starts at the nearest mounted ancestor. A boundary removed along
  with the component used to catch it, so it was never reported, and one under an ancestor already torn down was
  dropped, which happened to the children of a `V.VirtualList` row.
- `ErrorInfo.ComponentStack` ends at the outermost component of the tree passed to `V.Mount`, as React's stack
  leaves out the root. It used to end with a line for the root `V.Mount` makes, which is no component of the
  application's; an error that root raises itself, such as a node its reconcile refuses, now has an empty stack.
- `MountOptions` takes every option as an `init` property, as React's `createRoot` takes them as properties of one
  object: `new MountOptions(handler)` is written `new MountOptions { OnCaughtError = handler }`.

- Reading a keyword variant class with no relational name (`hover:`, `sm:`, `dark:`, `group-hover:` and the like)
  no longer cuts its keyword out as a string, and asking only whether a class is one cuts out no payload either.
  Outside a theme scope, asking it of a class behind a plain word that is no variant, such as `first:` or
  `max-3xl:`, cuts out nothing.
- The clip-path, filter, filter-transition, animate-hue and border-inset reads pass over a class that names none of
  their utilities before peeling its variants, so they allocate nothing to peel such a class.
- An arbitrary value with no type hint, such as `w-[120px]`, no longer has its prefix cut out as a string when it is
  tried as a colour.
- Mounting a `V.Div`, `V.Label` or `V.Button` no longer allocates to watch such an element for a radius its
  stylesheet or code gives it: the element's type hands its size changes to the corner-radius fit, where a callback
  was registered on each one before.

- Callback refs that each write a colour to their element now share one computation of the panel's layout, within a
  commit and across the rows a `V.VirtualList` renders together. A ref or a layout effect behind a write that resizes
  an element still reads the element's new size.

- A patch of an element carrying `gap-*`, `divide-*`, a grid, a box shadow or a gradient no longer builds a closure or
  a delegate to keep a relative-length poll up when none of its lengths is a measured one.
- A patch of a `divide-*` container whose width is in pixels, and a write of a bracketed pixel border width such as
  `border-[3px]`, no longer allocates two objects to keep the element on the watch that snaps it again when the panel's
  scale changes.
- A patch of a shadowed element no longer cuts a preset's name out of its `shadow-*` or `inset-shadow-*` class as a
  string, `shadow-inner` aside, copies the layers of a single family into a list and an array, or records the colours
  its classes declare in a new object when they are the ones it recorded last time.
- The shadow and clip-path reads pass over an important class of another family, such as `!bg-red-500`, before
  copying it without its bang, and configuring an element's variant manipulators passes over an important class that
  can be neither a theme class nor a custom-property reader, such as `!flex`, the same way.
- Inside a theme scope, asking whether a class behind a plain word that is no variant, such as `first:`, or behind
  `max-`/`min-` and a breakpoint no sheet declares, is a variant cuts out nothing. Reading a theme variable's length
  no longer wraps its text in brackets first.
- The readers configuring an element's state, responsive, container and group/peer variant manipulators cut a variant
  class's payload out only where its kind is the one being read, where before each of them cut out the payload of
  every variant class; and a class holding fewer than two `:` is skipped when looking for a stacked one.

- On mount, a `V.Portal`'s children run their insertion, layout and passive effects and attach their refs where the
  portal sits among its siblings, as React's commit reaches them; they used to run after those of every component
  that follows the portal inside the component rendering it. A layout effect of a component after the portal
  therefore finds the refs of the portal's elements set. The same holds for the effects of a commit in which a
  portal's children and a component after the portal both re-render, where the later component's used to run
  first.
- A `V.Suspense` that hides children it has shown runs their components' layout cleanups and detaches their
  elements' refs in one walk of the hidden tree before it hides the elements, as React runs
  `disappearLayoutEffects` ahead of `hideOrUnhideAllChildren`: a component's layout cleanups run as the walk
  reaches it, ahead of the refs of the elements it rendered, and a `V.Portal`'s children and an element Velvet
  relocates to paint it in CSS's stacking order are reached where they are declared. A layout cleanup therefore
  finds the refs of its own component's elements, and of every element after them, still set, those elements
  still displayed and a focused one still focused; the hidden children's refs used to detach, their elements to
  be hidden and a focused one to lose its focus before the first layout cleanup ran.

- A Motion transition's `EaseIn`, `EaseOut` and `EaseInOut` now run on Framer Motion's `easeIn`, `easeOut` and
  `easeInOut`, `cubic-bezier(0.42, 0, 1, 1)`, `cubic-bezier(0, 0, 0.58, 1)` and `cubic-bezier(0.42, 0, 0.58, 1)`,
  where they ran on UI Toolkit's quadratics, up to 0.069 of the way apart for the first two. `EaseOutBack` and
  `EaseInOutBack` run on Framer Motion's `backOut`, `cubic-bezier(0.33, 1.53, 0.69, 0.99)`, and `backInOut`, the
  mirror of its `backIn`; `EaseInBack` runs on that `backIn`, `backOut` reversed, except on a value Framer hands the
  Web Animations API, an opacity or a filter, which takes the `cubic-bezier(0.31, 0.01, 0.66, -0.59)` Framer gives
  that API. They ran on UI Toolkit's own back curves, up to 0.024 of the way apart. On that API's path `EaseInCirc`
  and `EaseOutCirc` now take the `cubic-bezier(0, 0.65, 0.55, 1)` and `cubic-bezier(0.55, 0, 1, 0.45)` Framer gives
  it, up to 0.73 of the way from the circle curves they ran on, and a `UseAnimate` sequence segment's `Opacity` on
  `EaseInOutBack` or `EaseInOutCirc` runs on `easeOut`, which Framer gives each easing of a keyframes array that API
  has no curve for. That covers a `Tween` UI Toolkit's own transition plays, from its first frame, a driven `Tween`, a
  `PropertyOverrides` entry, whose opacity stays on that path whatever the play repeats, a `layoutId` move
  and the opacity a `layoutId` crossfade carries, `UseAnimate`, a timeline's step, the ease-out a transition naming
  none takes, and the transitions Velvet runs in the engine's place while a Motion's tween holds the element's timing;
  `EasingMode.EaseOut` is the default `StyleTransitionConfig.Easing`, so a Motion that names no easing is retimed too.
- `UseAnimate`'s single call eases `Opacity` keyframes on a `Tween` or a `Bezier` along one easing over the whole
  duration, as Framer Motion runs opacity through the Web Animations API, where it eased each pair of keyframes;
  three keyframes with no transition now run 0.8 s on one ease-out. A `RepeatDelaySec` or a `Mirror` `RepeatType`,
  or a `Bezier` with an x outside `[0, 1]`, which the browser refuses, keeps the pair-by-pair easing, as each keeps
  Framer on its keyframes generator. `X`, `Y`, `Scale` and `RotateDeg`, and every value of a sequence segment, still
  ease pair by pair.

- A component that is not memoized no longer renders again with its parent when the parent hands it back the
  element it last rendered from, as React with React Compiler bails such an element. A `[Component]` body's
  automatic memoization hands back the element it built for a part whose inputs did not change, so a child built
  there keeps its output on its parent's render: one that reads something no hook tracks, such as a static field
  or `Time.time`, shows the value it last read until an update of its own. Its own state update, a change to a
  context it reads, a read it is still waiting on and a render of its own that suspended still render it, inside
  an element or a component the parent handed back as well. An `Outlet` handed back by its parent does not
  render again at the same location either, so a route scope whose creation failed is asked for again only when
  the Outlet next renders.
- Automatic memoization now caches a call that builds a component from a static method, such as
  `V.Component(Row.Render)`. It used to build every such call again on each render, and the method's
  `MemoizationReportAttribute` listed it as writing a static: the field the C# compiler keeps that method's
  delegate in.

- An error boundary catching an error from a commit — a layout or passive effect, its cleanup, a ref callback — or
  from the render of its own update or of a child's own update mounts its fallback afresh, as React's boundary
  reconciles the fallback against none of the children it replaces. A component of the fallback with the type and
  key of one the boundary showed starts from its initial state, an element of it with the key of one the boundary
  showed is mounted again, the shown one's ref let go, and a `V.Suspense` or `V.AnimatePresence` of the fallback
  starts with nothing the boundary's shown one recorded. That component's state and that element used to be kept,
  and that `V.Suspense` or `V.AnimatePresence` went on from the shown one.

- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` keep an observer of its own for each query of a list that reads
  the same entry as another, the observers held over an entry going to its queries in the list's order, as TanStack
  Query v5 does since 5.90.11. Once the list committed again, such queries used to share the last observer held over
  the entry, so `Query.GetObserversCount()` counted one for all of them, and that commit re-rendered the component.
- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` with a `combine` hand it, at a change to one query, each other
  query's result as the hook last committed or notified it, as TanStack Query v5's `#onUpdate` does, where they used
  to compute every result afresh. A change a query's `NotifyOnChangeProps` leaves out reaches `combine` once a commit
  follows: two queries summed, the first listing only `Error`, whose data moves from 1 to 2 and then the second's
  from 10 to 20, render 21 and then 22, where they rendered 22 alone.
- A custom property in a USS file holding an `rgb()` of three numbers or an `rgba()` of at least four, whose arguments
  are anything but three integers, all separated by commas or all by spaces, and in `rgba()` a number after them,
  after a further comma where commas separate them and after a `/` where spaces do, is stored as the text it was
  written with, where UI Toolkit's importer stored the colour of the first three or four of those numbers alone: one
  with a percentage (`rgb(0 0 0 / 50%)`, stored as opaque black), a `var()`
  (`rgb(59 130 246 / var(--tw-ring-opacity))`), a fraction in a channel (`rgb(0.5, 0, 0)`, stored unscaled), a word or
  a hex colour among its numbers (`rgb(1, 2, 3, foo)`), a full stop no digit follows (`rgb(5., 0, 0)`), separators
  mixed, missing or out of place (`rgb(1, 2 3)`, `rgba(0 0 0 0.5)`, `rgba(255 0 / 0 0.5)`), or more numbers than its
  channels (`rgba(255, 0, 0, 1, 9)`). A stock USS `color: var(--x)` reads no colour from such a property.
- A custom property in a USS file that UI Toolkit's importer used to store as other tokens than CSS reads is stored
  as the text it was written with, as one holding a `rem` is, such as an integer a full stop follows with
  no digit after it (`3.px`, stored as `3 px`, and `3.`, stored as `3`), a unit a `-` or `_` continues (`1px_a`,
  stored as `1px _a`, and `1px-2px`, stored as `1px -2px`), a hex colour a name character follows (`#abcxyz`, stored
  as `#aabbccff xyz`), a colon (`a:b`, stored as `a b`), and a comment opening the arguments of `none()` or
  `filter()` (`filter(/* c */ a)`, stored with the comment's text as its first argument).

- Each subtree a render takes out of the element tree is taken down in one walk, in the order React's deletion takes a
  subtree in: a component's `UseInsertionEffect` and `UseLayoutEffect` cleanups run where the walk reaches the
  component, ahead of the components it rendered, and an element's `refCallback:` cleanup runs where the walk reaches
  the element, after the cleanups of the components above it and ahead of those inside it. A component's `UseEffect`
  cleanups run right after its layout cleanups, so they too precede those of the components it rendered, the order
  React's passive phase cleans a deleted subtree up in. The subtree's refs used to detach together, ahead of the
  cleanups of the components inside it that are not a `V.Portal`'s children, and a component its parent stopped
  rendering cleaned up after the components it rendered with no element between them.
- On mount, an element with a `refCallback:` that a `V.Portal`'s child renders after a component attaches its ref
  after that component's layout effects, as React's layout phase reaches it; it used to attach first.
- A `V.VirtualList` row inside a `V.Portal` runs its effects ahead of a component after the portal when both
  re-render in one commit, as React's commit reaches the portal's children where the portal sits; it used to run
  them after.

### Removed

- The `V.Outlet` node kind, and the reconciler paths that existed for it alone — its element factory
  and patch arms, its keying arm, and the context-spine reconstruction that re-pushed a depth around
  a route re-rendering on its own. `V.Outlet()` returns a `ComponentNode`, which those walks already
  handle; source declaring the result as the removed type, constructing one, or matching on it, stops
  compiling.

- `FiberUpdatePriority.Deferred`. Source naming it stops compiling; `FiberUpdatePriority.Transition`
  is what to write instead — the same delayed tier, the same fixed flush delay and the same
  time-sliced frame budget, plus the starvation promotion and `isPending` bookkeeping `Deferred`
  never took part in. Nothing in the framework ever scheduled it: every lane Velvet enrols is
  `Urgent`, `Normal` or `Transition`, and `UseDeferredValue` — whose name suggests otherwise —
  derives through `Transition`. `Transition` is now `2` where it was `3`, so a value kept as an
  `int` reads differently; no public member takes or returns the enum, so nothing else changes
  shape.

- Router.Status, Router.OnStatusChanged and the RouterStatus enum. Status mixed
  the navigation in flight with how the last attempt ended, so an attempt that matched no route overwrote
  the Ready a committed location had left. Read `Router.Navigation` and `Router.OnNavigationChanged` for
  the navigation in flight, `Router.CurrentLocation` for where the router is, and what an attempt
  returns or throws for how it ended.

- The painted skew layer: the sheared silhouette, the descendant `translate` seats and the gradient silhouette
  shader, which no element reaches now. A `shadow-*`, a gradient and a `border-dashed` on a skewed element are drawn under the element's
  transform with the rest of its face. Project Settings ▸ Velvet lists one shader fewer.

- The Velvet.PureAttribute type. Nothing in Velvet read it. Remove the attribute where it is applied, or
  declare one of your own if something else reads it.

- `RouteActionContext.FormData`. Read the body through `RouteActionContext.Request`.

### Fixed

- A radius a `rounded-*` class or an arbitrary radius declares larger than its box is scaled to the box the
  way CSS scales it, so `rounded-full` on a 330 × 34 button paints a pill with 17 px ends rather than an
  ellipse, and `rounded-r-[400px]` on a 75 px-tall tab ends in a half-disc. `Documentation~/styling-variants.md`
  states where the fit still differs from CSS.

- A `[&>*]:` arbitrary payload and a `gap-*`, `grid-cols-*` or `divide-*` container no longer erase
  each other's value on a child they both write. The container's value wins while it applies; where it
  stops — its utility is dropped, the child leaves, an edge is abandoned, or the child is the first of a
  `gap-*` or `divide-*` row and takes no gap or divider — the child shows its own arbitrary values and
  payloads rather than nothing.


- A `V.Motion` whose `layoutId` spring starts while one of its variant swaps is tweening no longer lands
  the swap at its target or has the swap's transition drag the spring's frames. The spring takes what
  it drives out of the swap's transition list instead of replacing the list, so both run, as in Framer
  Motion. `animate-spin` and `animate-pulse` get the same treatment under a swap a re-render starts.

- A component keeps the elements it rendered when a reorder or an inserted sibling moves it. Unless a
  key scope stood between the component and its container — a keyed `V.Fragment`, or a `V.Memoized`, a
  `V.Suspense` or a `V.AnimatePresence`, each of which opens one — a keyed component's unkeyed elements
  were matched by their index in the container, so moving it rebuilt them while the component itself
  kept its state: a component rendered inside them, or inside its `V.Portal`, remounted with its state
  lost, and the elements already on the Portal's target were left there beside the remounted ones. A
  keyed `V.Fragment` returned as a component's whole output did not count; `[Unreleased — breaking]`
  states what keeping its key changes. A reordered `V.List` of components rebuilt the element of each
  row whose index changed, the same way. An unkeyed element is now matched within the output of the
  component that renders it; `[Unreleased — breaking]` states how it is matched there.

- A Motion declaring `exit:` that an `AnimatePresence` child wraps where the presence does not look —
  behind a component, `V.Memoized` or `V.Suspense`, or inside another element — now warns, when the
  presence's own render creates it, that it is not the child's anchor and its exit is inert; the exit
  used to be dropped without a word. The motion guide states which Motion a keyed child's enter and
  exit play on, and that a Motion outside that rule keeps its own mount enter, which the presence's
  `initial: false` does not suppress.

- The preview-tooling guide's headless capture section now says when the target texture holds a
  story. In Edit Mode the panel writes it on a later editor frame rather than during
  `VelvetPreviewHost.Mount`, so a harness that reads the texture back in the frame it mounted in does
  not find the story there; one that waits editor frames first captures it with public API alone. The
  guide named no frame to wait for.

- One `V.Fragment` returned for several `V.List` items gives each item a row that keeps its element when
  the items are reordered or appended to. Each item's copy of the Fragment shares its children, and a row's
  key was read back from that shared child, so the rows resolved to one item's key: a row already on screen
  was built again, and a row left on screen had its ref cleanup run.

- A keyed element that leaves a keyed `V.Fragment` mounts a fresh element where an update that is
  time-sliced reaches the move after resuming; it patched the element it had inside the Fragment. That is
  the remount a synchronous update already gave, and the one React gives.

- A node held across renders — the same `VNode` instance on both — is matched by the position it is
  written at rather than by being the same node, wherever the previous render's child array held a
  `V.Fragment`, a `V.Provider`, a component or another wrapper, or a `null`. It mounts a fresh element
  when it leaves a keyed `V.Fragment`, keyed or not, and when as an unkeyed child it lands at a different
  index, where an insertion or a removal before it could let it keep its element; both are remounts React
  gives too. In a child array whose previous render held none of those, an unkeyed held node that lands at
  a different index can still keep its element.

- Two sibling components that each render an element under the same `key:` keep their elements when their
  parent re-renders. The two keys were compared as siblings of one list, so a re-render of the parent
  rebuilt both elements and logged a duplicate-key warning. A key is now compared only among the
  elements one component renders, as React scopes it.

- An element carrying a `clip-path-*` utility is placed and sized in its parent as the same element
  without the clip is, apart from the differences stated last, since `clip-path` changes painting only.
  The wrapper that hosts the clip was laid out in the element's place as a relative flex row centring
  it, so an in-flow clipped element sat centred across its parent,
  lost the parent's cross-axis stretch and its own `self-*`, resolved a percentage size or an auto margin
  against the wrapper, and came out with no size in a `grid-cols-*` cell or a `V.VirtualList` row, whose
  slot size landed on the wrapper. An absolutely positioned one resolved its edge offsets against the
  wrapper, so one offset from all four edges of a column parent came out with no height, and an
  `absolute inset-0` child with it. The wrapper now carries the element's classes and the inline values
  that place or size it, the element keeps its paint and fills the wrapper, and a parent's `[&>*]:`
  payload and `divide-*` border are applied to the clipped child rather than to its wrapper. Still
  different from CSS: a percentage padding on the clipped element resolves against its own width rather
  than its parent's, and a query by class finds the wrapper before the element.

- A `V.Suspense` or `V.AnimatePresence` that a component returns with no element above it keeps what it
  committed when that component re-renders on its own. That held only where nothing above the component
  opened a key scope; a keyed `V.Fragment` does, as do the children of a `V.Suspense`, a child of a
  `V.AnimatePresence`, and the result of a `V.Memoized` or `[MemoizeMethod]` wrapper around the component
  in its parent. Under any of those the component's own re-render did not find the boundary's record: a
  suspended boundary added a second fallback element beside the one on screen, an AnimatePresence added a
  second copy of its children, and a component inside the fallback that re-rendered alone read the
  context's default instead of a Provider the fallback placed above it.

- Two `V.Suspense` boundaries in one component's output no longer share one record where the second sits
  under an unkeyed `V.Fragment` or `V.Provider` at the same index as the first. A resolved one there cleared
  the suspended one's record, and the next render replaced the fallback element on screen with a new one.
  Two `V.AnimatePresence` with the second under an unkeyed `V.Fragment` that way shared one committed set,
  so each rendered the other's children beside its own once those children had exit animations.

- Keyed wrappers keep what they enclose apart. Under one keyed scope, a Fragment and a Provider carrying the
  same key, or a Fragment keyed `"1"` beside an unkeyed Fragment at index 1, no longer share an identity, so
  neither takes the other's elements on a re-render. A keyed component written under different wrappers in one
  container, such as two keyed Providers, now mounts one instance per wrapper instead of rendering only the
  first; moving it from one wrapper to another remounts it, as in React. A component rendered by a keyed memo
  keeps its state when the memo moves among its keyed siblings, while one rendered by an unkeyed `V.Memoized`
  is placed by that memo's slot: after a reorder, a keyed component there remounts and an unkeyed one takes
  over the state of whatever held that slot.

- A class string whose arbitrary value changes every render, such as `left-[{x}px]`, and a screen of
  more than 256 distinct class strings no longer log "ParseClassNames cache exceeded limit", and a
  moving value no longer pushes the class strings rendered beside it out of the parse cache, which
  cleared every entry on reaching 256. The cache now takes a class string in only when it is parsed a
  second time, so a moving value's strings, each parsed once, stay out of it barring a hash collision.
  Counting parses of strings the cache neither holds nor remembers, a string parsed again within 2048
  of them is cached, and one parsed again only after 4096 is not; a cached string keeps its parsed
  array while it is parsed again within 2048 of them and is released once 4096 pass without it. Past
  those counts the strings are parsed again every render, as before, without the warning. So a cached
  class string stays cached beside fewer than 2048 moving strings per render and is released beside
  4096 or more, and a screen shown for the first time settles only while its distinct class strings plus
  the moving strings of one render stay under about 4096: measured, 3000 strings beside 1000 moving ones
  settle and 3000 beside 1100 never do, while the same 3000 already cached stay cached beside 1100.

- A render that calls `Hooks.UseState` / `Hooks.UseReducer`, `Hooks.UseStore` or `Hooks.Use` a
  different number of times from the previous render now fails with a message that opens on the
  component's name and continues in React's words — `Rendered more hooks than during the previous
  render (UseState / UseReducer: 1 before, 2 now)`, or `Rendered fewer hooks than expected` — where it
  named no component. The call past the previous render's count throws from itself instead of after the
  body returns, so when that call is made inside a plain helper method, the helper is on the
  exception's stack.

- A component mounted through `V.Component(body, props)` or `V.Memo` is named by its method, or by its
  `DisplayName`, in `ErrorInfo.ComponentStack`, in the hook-type error, in the StrictMode double-render
  diagnostics, in the error for a `Hooks.UseStore` whose store changed, in the `Hooks.UseBlocker`
  warning for a missing router and in the DevTools window's label for a mounted root. Each named the
  compiler-generated closure the overload wraps the method in. The error for a hook called outside a
  render and that DevTools label now give the declaring type before the method name, as the other
  messages do, and honour `DisplayName`.

- Memoized components rebuild their compiled VNode cache when their props comparison detects a
  change, including a record struct float member changing from positive zero to negative zero.

- The Starter App remounts when its UIDocument root is replaced or reenabled, retaining the router's
  location while the host remains enabled. The previous host kept rendering into the departed root.

- Suspense boundaries in separate containers or Portals no longer overwrite each other's pending
  state. Offscreen updates retain the waiting boundary's fallback, and removing a boundary releases
  its pending state even when its component and container remain mounted.

- Recomputing `V.Memoized` no longer recycles properties or children still referenced by the current cache result.

- A `V.VirtualList` whose `keySelector` returns null for one item renders that row instead of throwing
  out of the range update that reached it. A null key is no key, which is the answer `V.List` gives the
  same selector: the row renders and reconciles by position, which for a virtualized list is its item
  index, so a range change that keeps it in view patches it in place and its `Hooks.UseState` values and
  effects survive. The controller looked a row up in a `Dictionary` keyed by string, whose lookup refuses
  a null key even against an empty dictionary, so the range update threw from inside the item loop — and
  the buffers it abandoned there were the live ones, since a window whose size has not changed reuses
  them in place.

- A `V.VirtualList` range update that throws — from the item renderer, or from creating or patching the
  row it describes, as a `V.Custom<T>` element whose constructor throws does — no longer leaves the list
  showing rows the controller has stopped naming. The throw still reaches the caller — a renderer is
  application code and a failure in it is not the framework's to swallow — but the range update that
  ends releases the rows it had placed and the prior rows it had not, the one whose create or patch threw
  among them, empties the visible container and leaves the tracked range naming nothing, so the next
  range update rebuilds from a state it can use. It previously ran to the throw and stopped: rows already
  rendered for the new range were orphaned with their fibers, the visible container still showed the
  previous range, and the tracked range still named it, so a further scroll threw from the same item and
  stranded another set. Measured on a five-row window scrolled by one row, the renderer refusing the row
  scrolling in: the container still showed the five rows of the window before it, the tracked range
  still named that window, and the row that window was leaving had its `refCallback` cleanup run by
  nothing, disposing the list included. The list going blank is the visible change to weigh — a working
  program does not reach this, since reaching it means something the range update ran threw.

- A `V.VirtualList` row whose renderer keys its own node is reused across a scroll rather than rebuilt.
  The controller tracks a row by the key `keySelector` returns, but indexed the previous range's rows by
  whatever key had ended up on the node — and it wrote that key with `??=`, so a node the renderer had
  already keyed kept the renderer's. Where those two strings differ the reuse lookup missed, the row read
  as scrolled out of the range it was leaving, and it was disposed and rebuilt on every range change. The
  list still rendered the right content, which is why this was silent; what it cost was identity. A rebuilt
  row renders from a fresh fiber, so its `Hooks.UseState` values return to their initial ones and the
  per-mount work its body does — a lazy fetch, an impression ping, a subscription a `Hooks.UseEffect`
  takes under an empty dependency array — runs again. Measured on a ten-item list with a five-row window
  scrolled by one row: all five rows ran their `refCallback` cleanup and a fresh setup, where the same
  scroll now runs one of each. The previous range's rows are now indexed by the key `keySelector` returned
  for them, the key a range change looks them up by, so a row still inside the range is patched in place.
  A renderer that returns a freshly built node and leaves its key alone, or that sets the same string the
  selector returns, is unaffected; a renderer whose node arrives already keyed is not exotic, though — a
  shared row component that keys itself, or a child built through a helper that keys for its own reasons,
  reaches this without the author thinking about the list's key at all.

- A `V.VirtualList` whose renderer returns one node for several items — a spacer, a separator, a
  placeholder while data loads — gives each of those items a row of its own, kept or released by a range
  change as a row built for its item alone is. The controller wrote the selector's key onto the node it
  was handed for each item and read the next range's reuse table back off the node, so a node standing
  for several items carried the last one's key at every slot, and all but one of the rows it filled were
  never found again. Measured on a ten-item list with a five-row window scrolled by one row, every row
  rendered from one node: four rows were built afresh, and four of the five before them were left
  detached with their `refCallback` cleanups not run even when the list was disposed, where the same
  scroll now builds one row and releases one. The controller keeps the selector's key beside the node instead, so the node
  stays as the renderer returned it — a key the renderer set on it included.

- An element whose creation fails partway — a `V.Custom<T>` child whose constructor throws, with a
  sibling built before it — no longer leaves that sibling's ref to be set up on an element no tree holds.
  The sibling's `refCallback` setup was queued for the end of the pass and nothing withdrew it when the
  creation threw, so a later drain ran it; a component mounted there stayed mounted, its effects run.
  The creation now releases what it had built before the throw leaves it. Measured on a `V.VirtualList`
  row failing half-built — an element, a `V.Provider` or a `V.Motion` scrolling in, or an element whose
  children hold a null and so are all built before any is placed — the child's ref was set up once and
  not cleaned up even when the list was disposed, and outside any list it was set up once; in each it is
  now never set up.

- An element that an abandoned render built and had not placed is released. Where the keyed diff
  matches rows by key rather than by their place at either end of the list, it builds every new row
  before placing any, and it released none of them when a later row's creation threw, when an error
  boundary caught a failure inside one of those rows, or when a new render discarded a time-sliced one
  parked partway. The walk that expands components, fragments and `null`s in place released what it had
  built on a throw, but not when a boundary's catch stopped the render. And where a list appends rows —
  unkeyed, or keyed in a time-sliced pass — or rebuilds a range it found shorter than its last render,
  the row whose creation raised the abort was dropped unplaced without being released. Each left a
  `refCallback:` set up once on an element no tree holds, and where a row's creation threw with no
  boundary above it, a component mounted inside an earlier new row ran its layout effect on the next
  render. Where an element's creation threw, a `V.Portal` among its children still mounted its children
  into the target, and a `z-*` absolute child still had its element placed into a layer container after
  that element had gone back to the pool — measured on a `V.Button`, the pool's next button came out
  with a parent.

- An error boundary's catch no longer disposes a component that the render it stopped had not reached
  yet. Such a component was taken as removed: its effect cleanups ran and it was disposed while its
  elements stayed on screen, and the next render mounted it afresh. Measured on a boundary followed by a
  counter at 1: the catch ran the counter's effect cleanup, and the next render showed `count:0` beside
  the `count:1` the disposed instance left. The counter now keeps its state and its effect.

- An error boundary above a `V.VirtualList` that catches a row's render during a range update no longer
  leaves that update's rows mounted. The boundary's fallback takes the list out of the tree from inside
  the update, which went on rendering the rest of the range for a list already gone: measured on the
  first render of a five-row window whose third row throws, the other four rows stayed mounted, their
  effects run, until the root unmounted. The update stops rendering rows once the list is
  disposed, and releases the ones it had rendered.

- A renderer throwing inside `V.List` no longer strands the node array it rented. `V.List` takes that
  array from `VNodePool` and fills it by calling the caller's `renderer` for each item, and a throw out
  of the mapping left it out on loan with nothing able to give it back, so `RentNodeArray` found the
  pool that much emptier on every pass — unbounded across a session, since an enclosing error boundary
  re-renders and the renderer throws again. The array is now given back and the throw continues: it is
  the one object the call can prove it owns, having just been popped from the pool with the local as its
  only reference. The props bags of nodes the renderer already built are not given back, and cannot be —
  the renderer is the caller's and may have returned a node the application still holds, which returning
  would clear and alias into the shared pool.

- `V.ListFragment` refuses a `key` holding a NUL (U+0000) before it maps the list rather than after.
  NUL is the delimiter the reconciler composes Fragment scope chains from, and both `V.ListFragment`
  overloads reached the refusal of it by handing `V.List(...)` to `V.Fragment`, an argument C#
  evaluates before the call. So a refused call had already run the renderer for every item,
  and the pooled child array `V.List` took for the result, together with whatever the mapped nodes
  took, was out on loan when the refusal fired. A refused call builds no Fragment, so no later
  retirement could carry those back, and nothing else gives a pooled object back. Both overloads refuse
  ahead of the mapping now: a refused call runs no renderer and takes nothing from a pool.

- A navigation to a path that matches no route no longer cancels the navigation already in flight, nor
  stops `Hooks.UseNavigation` reporting it. It used to cancel and to write its status before matching, so
  a stale deep link or a renamed `redirectTo` target made a navigation the user had actually asked for
  return `NavigationResult.Cancelled` with no error anywhere, and left the router reporting `NotFound`
  while still naming the destination that attempt was loading — the state `Hooks.UseNavigation` renders
  a pending branch from. An attempt now cancels its predecessor and publishes itself only once it has
  matched, so the navigation `Hooks.UseNavigation` reports spans the guards of a matched navigation
  rather than the match itself, and an attempt that matches nothing reports through its
  `NavigationResult` alone.

- A Loader's cancellation callback that throws no longer escapes the navigation or the disposal that
  ends its round. Ending a round cancels the source its Loaders ran under, which runs the callbacks they
  registered on that token — directly, where a commit, a following run or disposal retires the round,
  and through the link, where a navigation parked on an `Await` loader has the source it runs under
  cancelled by a newer navigation or by `Router.Dispose`. A failure there was raised at whoever was
  navigating or disposing and took the rest of that operation with it: a commit landed its location with
  the router still reporting the navigation as in flight, a navigation superseding a parked one raised
  at its caller with the router left reporting the navigation it had just cancelled, and
  `Router.Dispose` left the router in `Router.Current` with its loader rounds unreleased. It is reported
  through `Debug.LogException` now, on the terms a Suspend loader's subscriber failure already was, and
  the navigation or disposal runs to its end either way.

- A cancellation callback an application registered on a token Velvet owns no longer escapes the
  teardown that runs it, on the terms a Loader's already does. Three teardowns cancelled such a source
  unwrapped. Unmounting a component walks every in-flight `Hooks.UseMutation` call's source and cancels
  each, so a throw out of one call's callback left the sources after it in that walk uncancelled and
  undisposed, and abandoned the rest of the unmount below the walk. Discarding a `Hooks.Use` resource —
  on unmount, or on the render that hands it a new resource key — skipped the source's disposal and left
  the completion callback attached. `Store.Dispose` caught `ObjectDisposedException` alone, so any other
  failure skipped the state notifier's disposal and `OnDispose`, while an `ObjectDisposedException`
  skipped the source's release and was reported nowhere. Each failure is reported now — through
  `Debug.LogException` at the two fiber sites and through `StoreLogger.LogError` at the store — and
  the teardown runs to its end.

- A Loader or a Blocker still holding the `CancellationToken` the router handed it can go on reading it,
  rather than finding the source behind it gone. The router used to dispose a loader round's source when
  the round ended, and a navigation's source when the navigation ended and at `Router.Dispose` — while
  the Loaders below one that navigated, an `Await` loader a newer navigation superseded, a `Suspend`
  loader streaming into the route on screen, or a Blocker awaiting the navigation could still be reading
  it. `CancellationToken.WaitHandle` then raised `ObjectDisposedException`, which code written to catch
  `OperationCanceledException` does not catch. The router no longer disposes those sources. What it lets
  go of instead is each one's link to the token it was made from, so a navigation or a loader round that
  never finishes is no longer kept reachable through the token passed to `NavigateAsync` once it has been
  cancelled. A navigation started while the router is being disposed — from a cancellation callback the
  disposal runs — returns `NavigationResult.Cancelled`, as one started on a disposed router now does,
  where it used to run on and could commit after the disposal. A navigation a cancellation callback
  starts while another is taking over now supersedes it, where the one taking over used to overwrite it
  and leave it running, so that both could commit.

- A `V.VirtualList` whose item renderer returns a `V.Component` or a `V.Provider` stacks its visible
  items instead of starting every one of them at the same place. Velvet anchors such an item's fiber on
  an element of its own, and that element was pinned to the four edges of the container the list's
  controller stacks the visible items in, so each item covered that container: with a 30px item height
  the second visible item began at the first one's y rather than 30px below it. That anchor takes a slot
  in the container now, and its width comes from the container rather than from insets of its own.
  A renderer returning an element, a `V.Motion`, a `V.Portal` or a nested `V.VirtualList` builds that
  element directly and never reached the anchor either.

- An error boundary whose child throws during a subsumed re-render no longer logs a
  `NullReferenceException` from the reconciler. The boundary's inline re-render runs inside its host's
  pass, and the catch disposes that child — which clears the `Reconciler` the re-render is still
  holding, so the three steps after it dereferenced a field the disposal had already nulled. The same field is
  read for the same reason a few lines further on, where it was checked. The render now returns at that
  point instead: a disposed fiber has no pass left to be subsumed into, so its layout effect and its
  paint-tick effect are not queued.

- An error boundary's fallback is written into the rows its container is holding for the boundary now,
  instead of into the first rows of its mount point. A boundary that is not its container's first child
  had a catch rewrite the rows at the top of that container rather than its own, so the fallback replaced
  a sibling ahead of it. That holds where the pass raising the exception was itself moving the boundary — a
  reorder that puts a sibling in front of it, a sibling that grew or shrank ahead of it — because
  showing a fallback stops that pass from placing anything, so the container is still holding the rows
  where they were and the swap is written there.

- A component that shares its container with other components rewrites its own rows when it re-renders
  on its own, instead of a neighbour's, after one of these moved them: a component growing inside the one
  ahead of it, or ahead of the component holding it; a neighbouring `V.Portal` on the same target growing,
  whether through its own children, a component inside it or a parked time-sliced render its patch
  drained, or unmounting; an error boundary's fallback holding more or fewer rows than the boundary had;
  a pass that moved the component and then stopped on an error a boundary caught; and a parked
  time-sliced render drained inside the re-render of the component holding it, or inside a pass that then
  reached a boundary which caught. Where the rows began was recorded when a pass decided where they would
  go and moved afterwards only for a sibling's own row count changing, so after any of those moves the
  re-render renamed a neighbour's rows, added its own a second time or left a stale copy behind, and a
  fallback could replace a neighbour's row while the boundary's own stayed. A `V.Portal` whose child
  component grew also left a row behind on the target when it unmounted, and a second `V.Portal` on that
  target, patched after the first one's component grew, rewrote a row of the first. An empty `V.Portal`
  sharing its first row with another Portal on the target that holds rows, and mounted before it, now
  keeps its place
  when that Portal's own children grow or it unmounts: its first rows landed between the other Portal's
  rows, or its second row ahead of its first. Two Portals on one target that both held no rows keep the order they
  are written in when either gains rows, where both are written in one element tree and neither inside
  another Portal's children. Two components rendering
  nothing that a keyed reorder swapped keep the swapped order when they grow, where they could render their
  rows in the order they were created.

- A component's own re-render now reads the Providers of the container it is written into, where the
  declaring body writes the component into each container as its own occurrence. Two sibling containers
  each holding the same component at the same position already mounted two instances, but the walk that
  rebuilds a consumer's enclosing Providers for a re-render the consumer scheduled itself looked every
  candidate up under that consumer's own container — so each container holding the position answered,
  and the walk took the first. The instance in the second container read the first container's value
  where the two Providers carried one context, and the context default where they carried different
  ones, which is an instance losing the Provider it is written inside. A `V.Motion`'s active label
  reaches a descendant through the same walk, so a descendant of the second of two sibling Motions read
  the first one's label. A candidate is now matched against the node the instance last rendered from as
  well as against its key, and two occurrences of `V.Component` are two nodes — but only where the node
  it holds came from the tree being walked, which a render the reconciler discarded and a time-sliced
  pass that parked before reaching that component both leave untrue. Shapes that excludes read as they
  did before, among them: one `V.Component` node instance written into both containers is one node; a
  consumer inside a `V.Portal` or a `V.VirtualList` item, whose walk searches the children captured when
  that consumer mounted; and a row a `StartTransition` list left past its park point, which is matched
  by key alone until the resume commits it.

- An `IRouteScopeFactory.CreateScope` that throws no longer takes the reconcile with it. The scope's
  *disposal* was already contained; its creation was not, at any of the sites that asked for one —
  so an application whose factory failed on a route change lost the render as well as the scope. The
  route now renders without a scope and the exception is propagated: an application with an error
  boundary above the Outlet gets to refuse a scope-less route there, and one with none gets the
  `Debug.LogException` the propagation falls through to. The disposal beside it drops and logs
  instead, which is the deviation from React the two paths still carry between them.

- A navigation no longer allocates a snapshot of the registered blockers on every pass. The pass walks
  a copy because a decision taken in it may unregister, and the copy was a fresh array each time — so
  an application with any blocker registered paid one per navigation. One list serves every pass now,
  and a pass that begins while another holds it takes its own copy, since a blocker runs caller code
  and that caller may navigate again.

- Matching a URL no longer parses each route's path again to build the match's base. A branch already
  holds its whole pattern, parsed once when the tree was built; resolving the base walked
  `ParseRouteSegments` instead, which is an iterator, so every level of every match allocated an
  enumerator and a list. The branch records how many segments each of its routes contributed and the
  resolution reads that slice. Measured over a two-level route with a blocker registered: four fewer
  allocated blocks per navigation.

- Matching a URL no longer allocates an array for every route branch it walks past. The record of
  which path segment each pattern segment took is a buffer on the tree now, rewritten per probe,
  where it was a fresh array per probe — and `Match` probes the ranked branches until one matches, so
  the cost was one array per branch not taken. Nothing outlives a probe: what reads the record does so
  before the next branch is tried.

- `animate-hue` and the two gradient pan modes now suspend a native transition covering the slot they
  drive, as `animate-spin` and `animate-pulse` already did. The guard matches a driver's slot against
  the element's own transitionable set, and it had no flag for `filter` or for the background-position
  pair — so those three reported driving nothing and their per-frame writes were left to whatever
  transition covered them. A bare `duration-*` is enough to reach this, because UI Toolkit's initial
  `transition-property` of `all` covers every slot, which is how an element reaches the two new slots:
  no bundled utility names either of them on its own. What the suspension does not reach is the
  background size and repeat the pan modes set once at attach, above the call that takes it.

- A component inside one `V.Portal` no longer has its position rewritten by a sibling portal on a
  different target. Two portals declared side by side are siblings in the fiber chain whatever targets
  they name, and a child-count change committed on one shifted the recorded position of every following
  sibling — including one mounted somewhere else. Nothing moved at the time, so the target looked
  right; the damage showed on that sibling's next own re-render, which wrote at the shifted position
  and left its previous element behind instead of patching it. A delta now reaches only the siblings
  that share the target it was measured on, which is what the method's own doc said it did.

- A `V.Suspense` inside another one no longer lets the outer's fallback be patched away while its own
  resource is still pending. A component's flush is deferred while the slot it writes into is held by a
  fallback, and the check for that stopped at the nearest enclosing Suspense — which is set on every
  expanded boundary, resolved or not. With a resolved boundary between the component and the one that
  actually suspended, the check found a boundary showing no fallback and never read the mark the outer
  expansion had written, so the component flushed into the outer fallback's slot and replaced it. The
  walk now continues past a boundary that is not showing a fallback.

- `V.NavLink` with a relative `to` takes its active class. The click path resolved the target through
  the router — `..` against the enclosing route, a bare segment appended to it — while the active
  comparison used the string as written, so `to: ".."` was compared against a current path beginning
  with `/` and did not match: the link navigated to the right place and stayed unhighlighted. The
  target is now resolved through the same `Router` call the click makes, anchored at the caller's
  Outlet depth, before it is normalised and compared. An absolute target is returned by that call
  unchanged, so `end`, `caseSensitive`, a trailing slash and a query answer as before; with no router
  mounted to resolve through, the target is compared as written, as it was.
  One shape that was active is active elsewhere now: a query-only `to` such as `"?tab=1"` normalised
  to `/`, so with `end` left false it lit up wherever the user was. It now resolves against the
  current route and the comparison strips the query from what comes back, so it is active on that
  route's path and under it — what an absolute `to` naming that path gives. Where clicking it takes
  the user is unchanged.

- `SearchParams.Empty` hands back a new instance on each read. It was a `static readonly` field
  holding one instance for the whole process, and `SearchParams.Append` mutates the instance it is
  called on, so the shape the member invites — `var next = SearchParams.Empty; next.Append("q",
  term);` — wrote into that one instance, and a read of `SearchParams.Empty` anywhere else in the
  process carried `q` from then on. `SearchParams` declares no member that removes a value, so the
  written-into instance stayed that way. Reading the member is spelled the same and still yields an
  empty `SearchParams`; what two reads no longer share is the object. A reference comparison against
  `SearchParams.Empty` therefore no longer recognises an instance taken from an earlier read, and
  binding a readonly reference to the member — an argument written with an explicit `in`, a
  `ref readonly` local, a `ref readonly` return — stops compiling. Reaching that last one takes a
  declaration over the concrete `SearchParams` type, and no Velvet signature declares a
  `SearchParams` parameter.

- A route whose optional literal the URL did not carry no longer writes that segment into the match's
  `PathnameBase`. `Route("docs/intro?")` at `/docs` gave a base of `/docs/intro`, so `UseNavigate()`
  and `V.Link` resolved every relative target one segment deeper than the address the user was
  at — and nested, where the parent base is what `..` pops to, the wrong address still matched the
  parent, so the render looked right while each further hop compounded the error. An optional segment
  the match never took is now left out of the base, which is what an absent optional param and an
  absent splat already were.

- A `refCallback` setup, a `refCallback` cleanup cycled by a changed callback identity, an `onCreated:`,
  a `wrapElement:` and a `V.Motion` `onEnterComplete:` that throw reach the nearest error boundary
  instead of leaving the reconcile call — the containment a `UseEffect` cleanup and
  `V.AnimatePresence`'s own `onExitComplete:` already had. Each is application code the reconciler
  invokes for a component that is still live; with no boundary above it the exception is reported to
  the console and the pass carries on. The `wrapElement:` escape left an orphan: the element was fully
  built and its ref already queued, then dropped, so the ref was about to point at an element no tree
  held — it now takes the slot unwrapped. A ref setup that throws no longer strands the setups queued
  behind it, and a replacement setup still runs when the cleanup it replaced threw — React's own
  independence between `safelyDetachRef` and `safelyAttachRef`.

- A `V.AnimatePresence` that stops being rendered takes its bookkeeping with it. That bookkeeping is
  keyed by the component the presence renders in, the parent element its children expand into, and the
  presence's position — and only the component being disposed, or the whole tree being unmounted,
  retired it. So a `cond ? V.AnimatePresence(…) : null` flipping to `null` left an entry holding the
  committed children plus each key's exit anchor and Motion element, well after the removal had taken
  those elements out of the tree. Rendering the presence again at that position then started from
  that stale set: children that had left were spliced back into the DOM as exiting ghosts, and
  `initial: false` no longer suppressed the enter, since the second mount was not taken for a first
  render. The entry also survived its parent element being torn down — once per removal, so a panel
  mounted and unmounted repeatedly accumulated one per cycle — and a poolable parent (a `V.Button`)
  rented back for a fresh presence at the same position picked the stale entry up again. A presence
  written inside a `V.Portal`'s own children was the same defect reached a third way, since its
  bookkeeping is keyed by the container the portal renders into and that container belongs to the
  caller: closing the portal resurrected the children the presence had already let go, as did
  registering its `targetId` to a different element and back.

- A `refCallback` cleanup that throws while its element is being unmounted is reported and the unmount
  carries on, instead of the exception leaving the reconcile call. It used to escape from the middle of
  the teardown, so the rest of that element's release never ran and the element itself was never taken
  out of the tree, and the removal batch it interrupted left every row it had not reached yet standing
  too. An `AnimatePresence` whose own leaf the batch had already removed then kept committed state
  naming a child that was gone, and the next render at that position spliced it back as an exiting
  ghost. The delegate is the caller's, and reconciler disposal already contained it this way.

- An `IRouteScope.Dispose` that throws is reported the same way wherever a route scope is disposed. The
  scope is the application's, built by the `IRouteScopeFactory` handed to `Router`. From the route
  change the escape left the `V.Outlet` blank — the route navigated
  away from torn down, the route navigated to never mounted — and a later render at that position
  mounted the new route on the already-disposed scope. From the unmount it reached the same interrupted
  removal batch and the same resurrected `AnimatePresence` child as above. Both also left the scope
  registered for the teardown sweep to dispose a second time; from the sweep itself the escape left the
  rest of that sweep unrun, including every scope it had not reached yet.

- `UseTransition` no longer clears `isPending` when the first slice of a time-sliced transition commit
  parks. The flag now spans every parked slice and clears after the terminal commit; that commit asks for
  the render that takes an observed indicator down.

- A same-starter async `startTransition` call keeps the joined transition owned until its own task
  completes. If an outer action started that call without awaiting it and then completed first, its exit
  used to release the shared slot and clear `isPending` while the joined action was still awaiting.

- A child that moves from one `gap-*`, `divide-*` or `grid-cols-*` container to another keeps the
  spacing, divider or column sizing the container it joined wrote. Each of the three tracked the children
  it had written to by raw reference and reset the value on one no longer in the container, and the
  element pool hands a child straight from one container to another — so the container a child left could
  still be tracking it after the container it joined had written to it, and reset that write on its next
  pass. A row that took a pooled `Label` from a sibling `gap-4` row lost one gap, a `divide-x` row lost
  one rule, and a grid child lost its column gap and its width. Which container won was decided by the
  order their re-applies landed in: a reconcile pass re-applies a container right after reconciling that
  container's children and so never loses the race, and what reached this was the panel's own re-apply
  sources — a `GeometryChangedEvent` or an `AttachToPanelEvent` arriving after the other container had
  written. Each turn-off now asks a claim first, so only the container whose write is still on the child
  may take it back off. Gap and grid share one claim, since both write a child's margins: a child moving
  between a gap container and a grid one is one owner's or the other's.
  One case moves the other way with it. A child the reconciler *removes* now keeps the spacing its
  container wrote, where before the container's next pass cleared it: element cleanup drops the child's
  claim, and the container then finds no claim to release against. This is every reconciler-driven child
  removal, not an edge case. A removed element is either discarded with its subtree or scrubbed on its
  way into the element pool, so what this changes is the inline state of an element an application has
  kept a reference to and re-parented itself.

- `startTransition` defers the updates its callback schedules on other components' state, not only
  the calling component's own. A child deferring an expensive list update through a setter it
  received as a prop — one of the canonical React uses — had that update classified against the
  component that owns the state, which was in no transition: it took the Normal lane, or the Urgent
  lane inside a click, and so drained at the next frame boundary with the interaction the user was
  waiting on, instead of on the delayed lane the call asked for — where an update arriving in the
  meantime would have gone ahead of it. The same held for a `Store` write made inside the
  callback. The flag is now ambient for the callback's synchronous run, as React's is. `isPending`
  follows those updates wherever they landed: it stays lit until every component the callback
  scheduled an update on has discharged it — committed it, gone away, or had the scheduler drop it at
  the update-depth cap — and the declaring component re-renders on the commit that
  finishes them, so an indicator it renders comes down there. Where the callback wrote to two
  components, what finishes them is the last commit rather than the first. What the flag does not do is force a
  render of its own when the transition *starts* — nothing renders purely because `isPending` went
  true, in this case or in the local-state one, so a component that shows a pending branch needs a
  render from the same interaction (the urgent update a click also makes, or an ancestor's) to
  observe it.

- An `async` `startTransition` action takes its own pending indicator down when it finishes. Its
  `isPending` correctly outlasts the commit of whatever the callback queued before it first suspended
  — the action is still running — but the clear at the far end scheduled nothing, so the flag went
  false while the component carried on rendering its pending branch until some unrelated interaction
  re-rendered it. An action that writes before its `await` and then outlasts the transition tier's
  100 ms delay is where a user meets it, since that commit is the render that puts the indicator up.
  The completion now asks the declaring component for the render that observes the cleared flag: one
  render, or none of its own where that component was already being re-rendered. An action that never
  suspended is held to what the synchronous form does, so an `async` callback that only awaits an
  already-completed task costs what the synchronous form costs — see the entry below for what that is.

- A synchronous `startTransition` whose callback renders the declaring component now keeps its pending
  indicator up for as long as the transition runs, and takes it down when it ends. A callback that
  commits work of its own — driving a handler the component bound, a click or a value change or a
  focus, whose exit flushes the updates queued before the call — has that flush render the component
  while the transition is open.
  The settle that flush ran used to clear `isPending` first, and only the entry to `startTransition`
  ever sets it, so a callback that then deferred a write ran its whole transition with the indicator
  never lit. A settle now leaves a slot alone while a call still owns it. The other half of the same
  route follows from that: where such a callback defers nothing, the flag it raised is cleared at its
  own exit, and nothing renders because a flag moved — so the exit asks the declaring component for the
  render that takes the indicator down. That exit asks only where the declaring component's last render
  read the flag as true, so it costs nothing where that component was not showing the transition — and
  it settles only a transition with nothing outstanding. Where the callback's work is still queued
  somewhere, what ends the transition is the commit of that work, and the settle there asks for its
  render without that term, so a transition whose work commits on another component costs the declaring
  one a render whether or not it was showing anything.

- Two `UseTransition` slots whose transitions are open at once, but not nested one inside the other, no
  longer credit each other's writes. One slot's update was attributed to every slot on that component
  that still had a transition open, so a slot whose own callback had queued nothing kept `isPending`
  lit until the other's work committed — including an `async` action parked on an await, which had
  queued nothing yet by construction. An enrolment is now credited to the calls whose callback is
  running when it is made, and recorded against the component it enrolled.

- A `startTransition` whose own component has unmounted still marks what its callback writes on the
  components that have not. Both overloads short-circuited on a disposed declaring component and ran
  the callback with no transition open at all, so those updates took the Normal lane — or the Urgent
  lane inside a click — and the deferral the call asked for was gone. An `async` action that outlives
  the component that started it is where this is met, since wrapping its post-`await` write in the
  starter again is what the migration guide asks callers to write, and by then the component may have
  gone. The pending flag is still left alone on that path: it is read during a render, and a disposed
  component has none left.

- A component the reconciler carries to a different container now re-renders itself into the container
  it is in. Its slot index and the stamp a portal teardown disposes by both followed the move and its
  container did not, so its own `setState` reconciled its new output into the container it had left,
  while the container it had moved into kept the element from before the move.
  It also let the container it had left take it down: where the arriving container is reconciled first
  and the departing one leaves the tree in that same render, the departing subtree's teardown still
  found the carried component named as living inside it and ran its cleanups.

- A `V.Component` written inside a `V.Portal` now survives a patch of that portal's children. Its mount
  ran in the deferred pass that follows the reconcile, which registered it under a different parent from
  the one every later patch looks it up by, so the first patch failed to find it: the component was
  built a second time with fresh hook state and fresh effects, the first instance's effect cleanups
  never ran, and the element it had rendered stayed behind in the target. A modal whose content
  component held its own `UseState` lost it the first time anything else in that portal re-rendered —
  once, because the instance built to replace it registers where the patch looks, so it survives every
  patch after that. The element the first instance rendered stayed in the target for the life of the
  tree.

- A component a declaring render writes both inside a `V.Portal` and at the same position outside it now
  keeps its own instance on each side, whichever of the two mounts first. They used to merge into one, in
  both orders and each with its own damage. With the one outside mounted first, the first patch of the
  portal's children replaced the portal's child with it. With the portal's mounted first, the position
  outside took the portal's own instance over and rendered it in both places, and the next patch of that
  portal built a second child and left the elements of the previous slot range in the target with nothing
  to reconcile them away.

- A portal that resolved its id late no longer writes over the container's own first child. It
  recorded its range from slot 0 while the id was unregistered and never rebased it, so the healing
  patch reconciled its first child against whatever the container already held — an overlay root with
  a backdrop had the backdrop painted into the portal's content, and the portal's own child never
  arrived. The range is now taken from the end of the container's children, the same place a portal
  that resolved at mount takes it.

- A `V.Component` written directly under a `V.Portal` is now disposed when the portal's children leave
  the container — its own unmount and a move to another container alike, and whether or not it
  rendered anything of its own. Its effect cleanups never ran and its hook state was never released,
  so a subscription a closing modal's top-level component opened outlived the modal. The disposal
  follows the portal the component was written under, so on a container several portals share, one of
  them closing leaves the others' components mounted and takes only its own.

- A `V.Suspense` nested inside another one's children no longer loses its fallback when the outer
  boundary resolves. The outer boundary's expansion wrote its own answer over every fiber it had
  created, the inner boundary's hidden children included, so a state update inside content the inner
  boundary was still waiting on stopped being deferred and committed into the slot range the inner
  fallback occupied — the fallback left the tree and the half-loaded content took its place. Each
  boundary now keeps the answer it gave for the children it suspended over, so an outer boundary
  resolving says nothing about an inner one that has not. An outer boundary that suspends still hides
  the inner boundary's fallback, and releases it again on resolve.

- Two `V.Suspense` boundaries in one component's render no longer share a single suspended mark. One
  showing its fallback while a second, placed after it, rendered its children left the component
  unmarked, so a state update inside the first one's hidden children stopped being deferred and
  committed into the slot range its fallback occupied — the fallback disappeared from the tree.
  Whether a boundary is showing a fallback is now read off the per-boundary record each `V.Suspense`
  writes under its own position, so a sibling that resolves cannot clear it. Ordering decided it:
  the same two boundaries with the resolved one placed first were unaffected.

- A second `Mutate` call no longer cancels the first or drops its callbacks. Both run, each delivers
  its own `OnSuccess` / `OnError`, and `Status` / `Data` / `Variables` show the most recent — which
  is what TanStack Query does, and it hands `mutationFn` no signal at all. A double-tapped Buy used to
  abort a request the server may already have committed and then skip the `OnSuccess` that records
  it. The token survives for unmount, where a request does want cancelling. A starting call also
  clears `Data` along with `Error`, so a result belonging to the previous call is not read as this
  one's while it is still pending.

- A `TextField`'s `isPasswordField` was written back to `false` by any render that stopped declaring
  it, so a mask a `refCallback:` had switched on came off on the first render that dropped the prop.
  A member a render declared and a later one dropped now restores what the field carried when the
  prop was first declared, and one no render has declared is nobody's to write.

- A pooled widget carried the `Focusable` default recorded under a previous consumer into its next
  mount, so a render that dropped a declared `Focusable` prop handed the element that consumer's
  focusability rather than the one it carried when this consumer first declared the prop. The record
  is forgotten on the pool return every poolable type passes through.

- A pooled widget carried far more than its reset helper named. Most of the writable surface of
  `Button`, `Label`, `Toggle`, `Slider` and `TextField` survived a pool cycle and arrived on whatever
  mounted next: a read-only or multiline `TextField`, a placeholder string the next consumer had not
  written, a `Toggle` stuck showing a mixed value, a `Slider` whose direction was inverted, the
  rich-text and emoji-fallback flags on `Button` and `Label`, text-selection colours and behaviour on
  `Button`, `Label` and `TextField`, and a data-source binding on all five. A `TextField`'s stale
  placeholder is the sharp end: the pool's own contract is that the next consumer cannot observe the
  previous one's text.

- A recycled composite field stopped delegating focus, and a recycled `Slider` or `TextField` also
  started taking pointer picks on its own root. The shared reset writes the plain `VisualElement`
  defaults for both, and only `focusable` was written back afterwards. A recycled `Label` carried
  tab index 0 where its constructor sets -1, which shows once a consumer declares it focusable.

- A `Slider` still carrying its numeric input field is no longer pooled at all. That sub-element only
  tears down while the slider is on a panel, and a pool return has already detached it, so a recycled
  slider showed a stray input box that its own `showInputField` denied.

- A `Button` or `Label` no longer carries a paint delegate a consumer added onto whatever mounts next.

- A composite field pooled without a label lost the class its constructor adds for that case, so a
  recycled one no longer matched a fresh one's class list.

- `UseMutation`'s `Reset` now abandons the call in flight instead of only clearing what it had written
  so far. Pressing Save and then Reset used to leave the save owning the handle, so when it landed it
  wrote `Success` and its result straight back over the idle state the user had asked for. The
  abandoned call is still not cancelled and still delivers its own `OnSuccess` / `OnError` — what it
  no longer does is write the handle, which is what v5's `reset()` detaching the observer amounts to.

- A `UseMutation` handle no longer shows `Data` for a call it reports as failed. The result was
  written before `OnSuccess` ran, so a handler that threw — which correctly makes the mutation an
  error — left that result on show underneath the error, and a view rendering `Data` without checking
  `Status` first showed it. The outcome is now committed after the handlers on both paths, which is
  where v5 dispatches it, so a handler no longer reads its own call's outcome and `Data` stands only
  under `Status == Success`.

- `Hooks.Use(factory, resourceKey)` compares the resource key by value, so a key a render builds names
  the resource the previous render named instead of starting a new one. The comparison closed over the
  parameter's own `object` type and took the reference branch for every key whatever it held, so a
  string built at run time — the query id the parameter's documentation recommends — and an id boxed at
  the call boundary each tore the resource down and restarted it on every render. Under a `V.Suspense`
  that cost the boundary its resolve, since the render a landing loader asks for restarted the loader
  and suspended again. Nothing said so either: the warning about a resource restarting every render is
  gated on the key being omitted, and an omitted key is the factory delegate, which a delegate's own
  identity comparison covers either way. A key of any other reference type is still compared by
  instance, so that omitted-key behaviour is unchanged.

- A subscriber that throws while a `LoaderMode.Suspend` loader is resolving no longer corrupts the
  round that loader belonged to. The router answers a resolution by re-emitting the location, so a
  `Router.OnLocationChanged` subscriber can run behind that announcement — and a throw from one was
  caught by the clauses that exist for the loader itself. That counted the round's outstanding loader
  off a second time and recorded the throw as that route's loader error. So a caller reading the route's
  loader error saw a load failure that had not happened. Filing it as a failure also ran the router's failure
  handler, which writes the history entry's snapshot again — this time from a round the second count
  had corrupted — so the entry stayed unsettled and a Back or Forward to it re-ran the loader instead
  of being served from the cache. What that second count costs the round itself depends on how many
  loaders it holds: one leaves it permanently short of settling, and two leave it reporting itself
  settled while the second loader is still outstanding. The throw is now reported as the subscriber's own and the
  round stands as the loader left it, with its result recorded and no error. A subscriber throwing out
  of the failure announcement is reported the same way, where it used to be left to whatever observes a
  forgotten task.

- A `shadow-*` or `drop-shadow-*` behind a `rounded-full` element paints its halo. The silhouette was
  baked at `--radius-full` itself, which is larger than the element, and came out empty; it now takes the
  bound the face Velvet paints over it takes, half the element's shorter side. A radius the element can
  carry bakes as before.

- A render that suspends with no `V.Suspense` above it no longer commits the effects of the components that
  rendered ahead of the suspend. Their effects, including those of components the render mounted, and their
  imperative handles used to run for the render that was given up and then be skipped when the retry
  committed; they now run with the retry. An effect still pending from an earlier commit runs that commit's
  closure rather than the one a discarded render registered. A component that already existed keeps the props
  it last committed, so a render of it alone before the resource resolves uses them. The elements the walk
  had patched ahead of the suspend stay patched, and a suspend in a later slice of a time-sliced render still
  finds the components of the earlier slices committed.

- A delayed `Spring` or `Bezier` play starts as far into its motion as the frame that ends its delay has
  passed it, rather than from the start of that frame.
- A `filter-*` change back to where a running Velvet filter tween started reverses it over the shortened
  duration UI Toolkit gives a reversed transition, instead of the full duration.

- A re-render of a container whose children are expanded in place, such as components, fragments or context
  providers, finds each child's existing element by walking on from the one found before it wherever the
  children kept their order. It used to walk the container's children from the first one for every child it
  reused, a cost that grew with the square of the child count.

- A `[&>*]:` payload paints, shapes text and sets a font family on a child at mount whether or not the child declares
  a variant of its own, and on a `V.Text` child; it used to reach a child that declared none only from that
  child's next render, and a `V.Text` child not at all.

- A `border-dashed`, `border-dotted`, `divide-dashed` or `divide-dotted` rule on an element or child that clips its
  content with `overflow-hidden` is painted on a sibling over it instead of being cut away with the padding box.

- `has-[.foo]:` no longer matches a descendant whose `.foo` was put on it only by an active variant
  payload: with `hover:foo` on a hovered child, the parent's `has-[.foo]:` payload stays off, as CSS
  `:has(.foo)` tests the classes the author wrote and not the ones a variant switched on. A `.foo` the
  descendant's `className` declares still matches, including while a variant keeps it off the live class
  list.

- A container or viewport marker added to or removed from an element's class list in code, rather than through its
  `className`, re-points the descendants already attached at that element's next render.

- A side radius that a variant or a later class takes one corner of keeps its other corner: `rounded-l-lg md:rounded-t-lg`
  paints the top radius and the left one's bottom corner. The side class used to stay on beside the payload, and the
  stylesheet's declaration order decided the shared corner.

- A `gap-*` container reads an inline `flex-direction` and an inline `flex-wrap` ahead of its direction and
  wrap classes, as CSS ranks an inline declaration over a rule, so an inline reverse row on a
  `flex flex-row gap-x-4` container puts the gap on the trailing edge and an inline `nowrap` on a
  `flex-wrap` container keeps the half-margin path off. The classes used to win. The value is read when
  the container applies its spacing, among them its reconcile or patch, attach and a geometry change.

- Honor leading and trailing important modifiers on `divide-*` utilities. `!divide-x-4` and `divide-x-4!`
  were dropped, so the divider was not drawn. An important axis, width, colour or line style now beats a
  plain one wherever it sits on the element, including through an active variant, and an important width or
  colour is drawn over a divided child's own plain border width or colour instead of giving way to it.
  A child's own important border (`!border-r-[3px]`) wins over an important divide, as its higher specificity does in CSS.

- A font family whose entries all hold only italic faces now serves an upright request with an italic face,
  as CSS does when no face of the requested style exists. The request used to find no asset and fall back to
  the default font.

- `FiberPortalRegistry.Register` no longer warns that an id is "already registered" when it is given the element
  that id already points at. A different element still warns and overwrites.

- A `V.DragOverlay` that mounts during a drag, from the drag start callback's state update or a later
  render, shows the preview at the pointer on the commit that mounts it and tracks the pointer for the rest
  of that drag, as a dnd-kit `DragOverlay` mounted mid-drag does, instead of staying hidden until the next
  drag.

- `V.Particles` draws a sub-emitter that is not a child of the effect. Such a sub-emitter used to stay a
  reference to the source's own system, so its particles were not among those drawn; it is now cloned under
  the hidden host and the reference re-pointed.

- Changing `maxLength:` on a `V.TextField` with `isDelayed: true` no longer discards the text the user has
  typed but not yet committed. The edit stays on screen, is not reported early, and still commits on Enter
  or blur.

- VEL101 reports a hook in a `finally` block that sits inside a lambda or a condition. It previously stopped
  looking at the `finally` and reported nothing.
- VEL101 reports a hook after a conditional early return where a bare block, a lock, a using, a try or a finally
  stands between them. It previously read only the block holding the hook.
- A hook called by a bare name with type arguments, such as UseTab<int>(), is checked as one. VEL101 previously
  skipped it.
- `Hooks.UseLoaderData` reads the loader-data context before it returns early for a component outside a matched
  route, so it calls the same hooks on every render.

- The comma syntax of `rgb()` / `rgba()` follows CSS Color 4: either name takes three arguments or four, a
  channel may be a fraction or a percentage, and a channel or alpha outside its range is clamped where the
  class was previously inert.
- A colour function written with spaces inside `shadow-[…]` (`shadow-[0_4px_8px_rgb(0,_0,_0)]`) is read as
  one colour; the spaces previously split it and left the class inert.

- A percentage border width (`border-[50%]`, `border-t-[25%]`) is declined, as CSS declines it, rather than
  written as that many pixels.

- A `Spring` or `Bezier` label swap while a running `Spring` or `Bezier` play, or a `Tween` running as a native
  transition (realtime clock), is moving the slot now starts the new play from the value on screen. It used to
  restart from the swapped-out label's own class value, so the element jumped when the label flipped mid-play. A
  spring that interrupts a running `Bezier` play or a native transition now also starts with its velocity rather
  than at rest, scaled by the rate of the playback the bezier play was on; a native transition's velocity is not
  handed on where its unit differs from the spring channel's. A slot nothing is moving still starts from the swapped-out value,
  except where a `controls.Cancel()` hold supplies the start.

- Swapping one bracketed width (`w-[300px]`) for another (`w-[10px]`, `!w-[10px]`) on an element with a
  `transition-*` utility now runs the transition, instead of the new width landing at once.

- The insertion effects a render queued inside the children of a `V.Suspense` showing its fallback now run once
  when the boundary reveals them. A second render made while hidden, or a tree that never showed and was rendered
  again with unchanged deps, dropped them before.

- An `hsl()` saturation above 100% clamps to 100% as Chromium clamps it: in the comma syntax, and in the space
  syntax for a plain `hsl()` or `hsla()` read by a colour longhand (`text-[…]`, `bg-[…]`, the per-side
  `border-t-[…]` family, `outline-[…]`, `caret-[…]`, `selection:…`), so `text-[hsl(0_150%_75%)]` is
  `rgb(255 128 128)`, not `rgb(255 96 96)`. A shorthand (`border-[…]`), a colour with an opacity modifier, a
  ring, a shadow, a gradient stop, an upper-case function name, a `none`, an exponent, a `+` sign, a percentage
  alpha or a number in place of the saturation leaves it above 100%, as in Chromium. The guide spells out what
  plain means.
- The space syntax floors an `hsl()` lightness at 0 when it parses, as it already did the saturation, so
  `hsl(0_150_-50)` is black.
- A number past single precision's range in a colour function reads as the largest single-precision value of its
  sign, as Chromium's tokenizer reads one, rather than the largest double. A hue between `1e39` and `1e308` is now
  the hue of that value (`hsl(1e305_100%_50%)` is red), where the double-precision remainder gave an arbitrary one.

- A shorthand and a longhand of one family in one `className` (`p-[52px] pt-2`, `m-[52px] mt-2`,
  `m-[52px] mx-auto`, `size-[52px] w-full`) now resolve as Tailwind orders them, the longhand holding its side
  whichever is written first, where the one written last or the inline one held it. It covers padding, margin,
  the insets and `size-*` / `w-*` / `h-*`; a logical utility gives way to the physical one of its side; a
  `V.Motion` pose overrides the node's own spacing; and a spring or bezier play reads the same order. A
  stylesheet class that takes part is written inline, so it leaves the element's class list.

- Markup copied from Tailwind that writes `@md:` and the like resolves, where it used to leave an inert class on
  the element.

- `Hooks.UseOptimistic` takes a settled transition's entries down in the commit that lands the transition's
  last work, in every component holding them. A holder the landing drain does not render, and one its pass
  rendered before a descendant it subsumes settled the transition, used to keep showing the entries until a
  follow-up render after that commit, so a layout effect of the commit read the holder and the components
  reading its value with the entries still in place. Each is now rendered without them before the commit's
  layout effects run, in a drain's flush and in the terminal slice of a time-sliced reconcile alike. A holder
  that is unmounted, hidden under a fallback or in a detached host, and a settle that no flush lands, an
  `async` action completing, still revert in a later commit. The migration guide's `useOptimistic` row no
  longer lists this as a deviation.

- A `QueryClient` entry no query reads is now removed once its `GcTime` has passed on the client's clock,
  without waiting for a query to subscribe, `InvalidateQueries` to run or `SetQueryData` to write, as
  TanStack Query v5's `scheduleGc` timer removes it: an idle application no longer keeps every inactive entry
  and its data. The clock is polled once a frame, so removal lands on the first frame at or after the
  deadline. A query reading the entry again before then keeps it and the time starts over when its last
  reader leaves, `TimeSpan.MaxValue` sets no timer, and an entry with a request in flight is kept until a
  whole `GcTime` after the request settles, as before, with the clock read every frame meanwhile.

- Overlapping `rounded-*` utilities that write different properties in one `className` now resolve as Tailwind orders them, the one Tailwind emits
  later holding the shared corner whichever is written first: `rounded-t-[2px] rounded-r-[8px]` and
  `rounded-r-[8px] rounded-t-[2px]` both give the top-right corner 8px. It covers the shorthand against a side or a
  corner, a side against a corner, a side against a side (`rounded-l` and `rounded-b` on the bottom-left), and the
  logical forms (`rounded-s-*`, `rounded-e-*`, `rounded-ss-*`, `rounded-se-*`, `rounded-es-*`, `rounded-ee-*`)
  against the physical ones, for a bracket value and for a scale step alike. A `V.Motion` pose keeps the corners it
  writes: a base radius it partly overlaps is cut down to the corners the pose leaves. A shadow or ring follows the
  same corner radius as the box. A stylesheet
  class that takes part is written inline, so it leaves the element's class list. The bundled `rounded-*` rules
  are declared in Tailwind's order too, so two classes on one element no longer depend on the order of the sheet.

- A `V.Suspense` showing its fallback over children it had shown no longer lets a render of those children change
  the deps their effects are compared against. An effect whose deps changed and then changed back while the fallback
  showed no longer runs at the reveal.

- A `V.Portal` among the children a `V.Suspense` hides is now hidden with them, its components along with its
  elements, for a portal mounted while the fallback shows as for one that was already mounted. Their layout
  effects are torn down when the Suspense above re-suspends and set up again at the reveal, as React does, and
  their refs and state updates wait for the reveal too; they used to run while the tree was hidden.

- A `Hooks.UseStore` reader rendered later in a drain pass than a reader that pinned a `null` snapshot now
  reads that pinned snapshot, as it already did for a non-null one, instead of the store's newer value.

- `Hooks.UseLayoutEffect` reads the committed tree laid out, as a layout read inside React's `useLayoutEffect`
  forces the reflow its commit owes: `layout`, `resolvedStyle` and `worldBound` reflect the commit the effect runs
  in, where on mount they used to read an element not yet laid out and on an update the previous frame's box. A
  commit that runs a layout effect, builds a `Hooks.UseImperativeHandle` handle or attaches a `refCallback:`
  computes the layout of the panels its tree renders into before running it, its `V.Portal(layer:)` and
  `V.WorldSpace` panels included, so a measurement stored in state re-renders before the frame is painted. That
  holds for a commit made inside the panel's own layout pass, such as a tree mounted from a
  `GeometryChangedEvent` callback, whose `GeometryChangedEvent`s the pass still sends afterwards; what a
  `ScrollView` settles from those events, its scrollers among them, settles then too. A commit's
  `Hooks.UseInsertionEffect`s run before its `refCallback:`s attach, so a callback ref reads the box a layout
  effect of the same commit reads.
- A `V.VirtualList` row, and the components inside it, run their layout effects once the list holds the row,
  where they used to run on a row not yet in the panel.

- A `UseStore` reader rendered in the same batch drain pass as a store mutation, after it, no longer stays on
  the snapshot that pass pinned — whether it was already queued when the mutation notified it or was mounted
  later in that pass, after the notification it never received. Its render read the pinned value, consistent
  with the readers rendered before the mutation, and nothing asked for another render; when the mutation
  changed what its selector returns, it now renders again in a later pass and shows the store's current value.

- An exception that leaves a next-frame update batch, such as one a `Hooks.UseImperativeHandle` factory throws
  in that batch's layout commit, no longer stops the tree from scheduling later next-frame batches. A later
  Normal-priority update used to stay uncommitted until something flushed the batch synchronously, such as the
  end of a discrete event handler. An update queued in the batch before the exception left it now commits on
  the next frame. After three batches in a row have thrown, the updates still queued are dropped with an error
  log rather than retried on the next frame.
- A Transition update waiting for the callback that ran such a batch is rescheduled for a later frame, so its
  `isPending` no longer stays true with nothing left to commit it.

- `QueryClient.InvalidateQueries` refetches an entry with the options it was last handed — by the last query
  to commit over it or start a request for it — as TanStack Query v5 refetches a query with its own options.
  It used the first reader's, so where readers of one key pass different query functions, a different one
  can run now.
- A request that lands shares its data with the `StructuralSharing` of the options its entry holds when it
  lands, as v5's fetch does, rather than with those it started with.

- A `QueryClient` entry nothing reads is no longer removed while a request for it is in flight, which
  cancelled that request: as in TanStack Query v5 it stays, and readable, until a whole `GcTime` has passed since the
  request settled.

- Two custom hooks calling each other are each read as reaching every hook the pair reaches. One of them
  could be read as calling no hook, depending on which component the build met first, so a component
  calling it behind a condition, or calling it for a `UseMutation` the pair composes, was memoized where it
  should have been left unwoven.
- A component that deconstructs a custom hook's pair, `var (count, total) = Custom();`, is no
  longer auto-memoized on the first element alone, which served a stale tree after the second changed. A
  `ref var x = ref Custom();` over a custom hook leaves the component unwoven.
- A component the compiler weaver fails to process, because analyzing or weaving its body throws, is left
  unwoven and reported as a build warning naming it and the exception, where the failure used to break the
  assembly's compilation.

- A pooled element no longer carries `-unity-slice-*` values onto its next consumer.
- When an `animate-gradient` or `animate-shimmer` pan stops, the element's inline `background-size`,
  `background-position` and `background-repeat` come back instead of being reset, and a value written into
  one of them from outside while the pan ran stands.

- A `KeyDownBinding` or `KeyUpBinding` handed to `V.Motion(elementType: typeof(TextField), events:)`,
  or to any other text-input element type, saw none of the keys the field's input takes, because the
  input stops their propagation before a callback on the field's bubble pass runs. Both now run on
  the field's trickle-down pass, ahead of the input, as `V.TextField`'s `onKeyDown:` and `onKeyUp:` do.

- A `Call` step at index 0 of a `Hooks.UseAnimationSequence` that pauses the sequence from its callback
  keeps it paused, and one that plays it under `autoplay: false` keeps it playing. A mount or a `deps`
  restart applied `autoplay` after step 0 committed, which overrode what the callback had just set.

- `When = BeforeChildren` on a `TransitionType.Spring` transition held its children for the transition's
  `DelaySec + DurationSec`, though the spring did not play for `DurationSec`. They now wait as long as the spring's
  slowest channel takes to rest from the pose classes, measured as Framer Motion measures a spring's duration.

- A `Hooks.UseAnimationSequence` given an empty `steps` list reads `IsComplete` on its mount render. It read
  false there and true from the render after.

- A controlled value written to a bool control no longer allocates a copy of every stacked variant in the
  tree (`dark:hover:`, `dark:checked:` and the like) to settle the `checked:` and `peer-checked:` variants it
  feeds. The element-local settle now reads only the written element's own stacked variants, and both it and
  the `peer-checked:` offer, which still visits every stacked variant, walk a reused list rather than a fresh
  array. A drag release and a focus revert run the same element-local settle, over each element the press
  reached and over the reverted element, and no longer pay that copy either.

- A reconcile pass no longer allocates copies of the `group-*` / `peer-*` and stacked variant registries
  at its end, where it retargets those variants against the tree whenever the tree holds any of them; the
  retarget walks reused lists.

- A memoized component that suspended on `Hooks.Use` under a `V.Suspense`, and had an update queued while it
  waited, renders once when its resource resolves rather than twice, unless the render the resolve gives it
  asks for a further render of its own (a `Hooks.UseDeferredValue` whose input changed, for one). That render
  now satisfies the update, so the boundary's reveal reuses it instead of finding the component still marked
  for one; a lane the render itself asks for again survives it. A component holding a transition's work, and the component a `V.VirtualList` item renderer
  returns, keep the earlier behaviour.

- A navigation whose commit throws leaves `Router.CurrentLoaderData` and `Router.CurrentLoaderErrors` on
  the location already committed, as React Router writes `loaderData` and `errors` only with the location
  they belong to. It used to replace them with the results of the loaders it ran for the location it
  never reached.

- When a Guard starts a navigation that takes over and then redirects, the redirect ends with
  `NavigationResult.Cancelled`, as React Router ends a navigation that was aborted before its redirect
  is followed, and `Hooks.UseNavigation` goes on reporting the navigation the Guard started. A redirect
  to a path a route matches used to name its own destination there, which was never going to commit,
  and one to no route or past the redirect limit returned `NavigationResult.NotFound` or
  `NavigationResult.Error`.

- A takeover of a navigation parked on its loader goes straight from the one to the next, as React
  Router's navigation does. When the parked navigation's loader honoured its token, the router briefly
  reported `NavigationLifecycle.Idle` between them.

- A navigation started from a handler of a commit's own notifications, and that commits at once, is the
  last location `Router.OnLocationChanged` announces to every subscriber. The announcement it interrupted
  used to go on afterwards with the older location, to every subscriber when the handler was one of the
  commit's navigation-state ones and to the location subscribers after the handler when it was one of
  those, leaving `V.RouterProvider` on a location the router had left.

- A component that is not memoized, in the output of a memoized component that bailed because its props did not
  change, was rendered again on every re-render of the parent. That covers the component directly in the output
  (`[Component(Memoize = true)] Row(int id) => V.Component(Inner)` rendered `Inner` on every parent tick) and,
  when another component below the row had an update pending, the ones below its elements. Only a component
  with an update pending of its own renders now, as with `React.memo`.

- Removing a row that is a plain container, as clearing or replacing a list of such rows does, no longer
  unregisters the callbacks of the Buttons and text leaves inside it: they leave with the row. In the editor
  those unregistrations were most of the time clearing a list took, because the time one unregistration takes
  grows with the number made before it. A row that is itself a Button or a Label, or that holds a Portal's
  target, unregisters as before, since those elements are used again.

- Clearing or replacing rows of `V.Button`s no longer unregisters each pooled Button's click-gate callbacks,
  a cost that grew with the number of elements created before it. The gate stays registered on the pooled
  Button, with its press state cleared, and the next rent of that Button uses it again.
- Creating an element no longer runs the text-effect pass for one that carries no effect class of its own.

- Mounting a component inline no longer builds a node factory, patcher, element cleaner and child reconciler for it up front. They are built when the component first reconciles rows of its own, which a component whose render its parent's pass reconciles does not, so creating a list of components allocates fewer objects per row.
- A Button's click gate, which keeps a click from reaching a Button the React tree would not carry it to, registers its six callbacks once a portal's event bridge exists rather than for every Button with an `onClick`: with no portal mounted the callbacks have nothing to decide. A Button made before the first portal attaches its bridge is registered then.
- Creating an element looks for the marks of a variant, custom-property, arbitrary font or line-height token once per plain class (`flex`, `p-4`) instead of running a check for each kind, and remembers the class tokens that resolve to no inline style, so the static-scale scan does not repeat for a token it has already ruled out.
- Tearing an element down skips the side tables that hold nothing, and creating one with no style overrides no longer diffs them.
- Creating an element that declares no children, as a Label or a Button without children does, no longer runs a child reconcile into it, which had nothing to place.
- A component mounting or updating mid-play no longer replaces the context's paint leases, which dropped the holds of a running Motion play and let the class pass paint over it.

- Creating an element whose classes carry no variant (`hover:`, `dark:`, `group-`, `data-[...]:` and the other `:` prefixed forms) or custom-property token no longer runs the ten variant configure passes, each of which scanned and allocated for nothing. An element that already holds variant state is still visited, so a re-render that drops its last variant removes the manipulator as before.

- Removing many rows of a list at once, such as clearing a list or replacing all of it, takes time proportional
  to the number of rows. It used to grow with the square of the row count: each removed row tested every
  registered component against its own subtree, and found its slot by walking the container's children from the
  first one.

- A parent re-render no longer patches the element tree of a memoized component whose props did not change. It
  used to reconcile every descendant of each such row against itself, so updating one row of a list of many
  memoized rows cost a patch of every other row. The component, and a component below it that has an update
  pending, render as before; a component that is not memoized and sits below an element of the row is no longer
  rendered again by its ancestor's re-render, as with `React.memo`.
- Removing a row looks its element up at the index the removal already holds rather than searching the
  container's children for it, and a fiber appends a child without walking its existing children, so removing
  or creating many rows no longer grows with the square of the row count.

- Removing many components at once, such as clearing a list of them, takes time proportional to their number. Each
  removal used to walk the parent's chain of child components from the first to find the one before it.
- Tearing down elements no longer reads the Suspense and AnimatePresence records of every other element for each one,
  a cost that grew with the number of such records.
- A keyed list re-render that leaves every row where it was no longer scans the container again to confirm it.

- Adding the rows of a list at ascending positions places each from the one added before it, where it used to walk the container's children from the first one for every row it added. A component whose children grow from none, as a row of three children does when it is created, no longer pays that walk, and the cost of adding the rows of a long list no longer grows with the square of its length.
- Re-rendering an element whose class list changes no longer runs the checks for variant, custom-property, arbitrary font and line-height tokens on a plain class (`flex`, `p-4`) it adds or removes, as creating an element already skipped them.
- Removing a row that is a plain container no longer unregisters the event bindings of the elements inside it, which leave with the row, as the click gates and text-item callbacks already did. A binding on the row itself, or on an element a pool keeps, is unregistered as before.
- Removing an element no longer takes the class and callbacks that watch a background image's size off an element that declared no percentage slice.

- A component that suspends while mounting inside a host element, below a `V.Suspense`, no longer leaves the fibers of
  that abandoned attempt registered after the boundary reveals: the components mounted beside the reader are released
  when the attempt is abandoned, and the reader with the components above it when its resource resolves.

- A `V.Portal` whose children suspend with no `V.Suspense` above it, in the render that mounts it, now renders them
  once the resource resolves. The retry used to mount a second placeholder and a second copy of the children,
  which read the resource again and suspended again, so the Portal stayed empty and the host's changed effect never
  ran. The host's rows stay where the render put them, the Portal holds none of its children until the retry
  mounts them, and the Portals queued behind it in the same pass mount instead of being dropped.

- A time-sliced update that mounts a `V.Portal` whose children suspend with no `V.Suspense` above it, in a
  render that parked before the Portal mounted, no longer places its rows twice. The retry used to diff from the
  older tree while the rows the parked pass had placed stood on the target, and created them again.
- A time-sliced update that suspends with no `V.Suspense` above it no longer places more rows, or runs the commit
  of its final slice, before the retry: the retry resumes it. A slice that parks again and then suspends keeps the
  baseline the parked rows diff against instead of returning it to the pool.

- Structural variants (`first:`, `last:`, `only:`, `odd:`, `even:`, `nth-N:`, `nth-last-N:`) take effect on children
  rendered directly into the element a tree is mounted into, and into a portal's target. They were not evaluated
  there, so `V.Mount(host, V.Div(className: "first:bg-red-500"))` left the div unstyled. They are also re-derived
  after a sibling is added or removed there, after a Portal unmounts and leaves other children on its target, for
  the children of a context-provider wrapper element, and when a time-sliced reconcile finishes. A component that
  re-renders alone and adds a row beside siblings it shares a parent with moves `last:` to the new row. The host's
  or target's other children count as siblings, as in CSS, including ones this tree did not render.

- A component that suspends with no `V.Suspense` above it, inside a host element a retry creates again (a
  `V.Div` child of a `V.Portal`, for one), no longer leaves the fiber of each abandoned attempt registered. The
  components mounted beside the reader are released when the attempt is given up, and the reader and the
  components above it under that element are released once its resource resolves and asks for the retry.
A suspend a `V.Suspense` above the host element catches is unchanged.

- Restore the element's inline transition duration, delay, and easing after a Motion tween completes or is interrupted; the restore for an exit interrupted on a panel waits until the reversal it hands its timing to ends. If tween plays overlap on one element, each slot keeps the value of the latest one still playing that wrote it, and the last to end restores the element's own timing. Preserve code changes whose keyword or list entries differ from the value the tween timing last wrote when it next writes or releases a tween's timing; the tweens already playing stop supplying that slot until one of them writes it again, and identical reassignments and changes undone before observation do not replace the saved timing.

- A `V.Motion` label change tweens a `translate-*` pose — written as inline style, since translate has no USS
  form — on the transition of the pose it swaps into. It took whatever transition the element carried before
  the change: the previous swap's while that was still on the element, so a 0.3s `Hooks.UseAnimationSequence`
  step after a near-instant one arrived in a single frame; and, once it had been released, the element's own,
  so a step following a finished one jumped unless the element's own classes gave it a transition. A pose's
  inline values now land at the swap itself, alongside its USS classes and in its stagger slot.

- Under a tween transition, a `V.Motion`'s mount enter and `V.AnimatePresence` enter show a `translate-*`
  `initial` pose and tween from it to `animate`, and the exit a removed presence child's anchor plays tweens
  to a `translate-*` `exit` pose before the child is removed. None of the three moved a translate pose: the
  enters rested at `animate` from the first frame and the exit stayed there until removal. An exit interrupted
  by its key coming back leaves the child at the values the re-added child declares; so does one that finished
  just before its key came back, except that a re-add changing the variants map can keep the previous exit
  pose's USS classes, and its inline values too where the new map no longer resolves the exit label.

- A `V.AnimatePresence` child re-added mid-exit with a different `animate` label rests at the new label's
  pose. The exit's cancel put back the previous label's USS classes after the new ones had been applied, so a
  child re-added at `opacity-50` from `opacity-100` carried both and resolved to 1.

- The package no longer uses two APIs that fail to compile from the Unity 6.5 beta on. On 6.5 and later
  it names `UnityEngine.UIElements.WorldSpaceSizeMode` where it named `UIDocument.WorldSpaceSizeMode`,
  and on every version it tells a swapped particle effect from the same one by `GetEntityId()` rather
  than by the instance ID 6.5 withdrew. CI now compiles the package, its editor and test assemblies
  included, on a later editor as well as on the declared floor.

- An error boundary that catches a render error below it, while another component's render is rendering the
  boundary, replaces only its own output with its fallback, and the rest of that render commits, as React
  commits the rest of a render around a boundary's fallback. The catch used to abort the whole render: on mount
  a boundary inside an element left that element and everything around it unrendered and a sibling's passive
  effect never ran, and on an update the siblings the render had not reached kept their previous output. A
  boundary that is a `V.VirtualList` row, mounting in its host's render, no longer stops that render either.
  The fallback takes over no element the failed children held, and an `AnimatePresence` or a Portal target
  the failed children rendered into keeps nothing of them. One catch still stops the other render: a boundary
  that catches while finishing an update of its own a time-sliced pass had parked, which that render reached,
  discards that render as before. The catch is reported once the fallback has
  rendered. Where the fallback's own content throws, that error goes to the boundary above, as in React; the
  original error goes on after it only where no boundary above caught the content's, and is logged where there
  is none.

- A `V.AnimatePresence` child's enter that plays nothing — under `initial: false`, a child inheriting its
  enter from a `V.Motion` above the presence included, or a variant `V.Motion` with no `initial` — calls its
  `onEnterComplete` once the render that reached it has ended rather than while it is rendering, so a render
  an error boundary above it catches calls none, whether the boundary catches inside another component's
  render or on its own. A `V.VirtualList` row rendered
  as the list scrolls still calls it before the range update returns.

- `MountOptions.OnCaughtError` is called in the commit that shows the catching boundary's fallback, after
  the fallback's layout effects and before those of the boundary's ancestors, as React calls
  `onCaughtError`. It was called as soon as the fallback was reconciled, before any of its layout effects. A
  boundary that an ancestor boundary replaces before that commit no longer reports the error it caught,
  matching React. A boundary that a time-sliced transition mounted or re-rendered, catching before the
  transition completes, reports in the commit that completes it, after its own layout effects.
- A fallback an error boundary shows after catching an error thrown from a `UseEffect`, a `UseFrame`
  callback, or a `V.VirtualList` row that a scroll or a resize renders runs its layout effects in that same
  commit. They waited for the next commit of some other component.
- When a scroll or a resize renders new `V.VirtualList` rows, the components inside an element a row
  returns run their layout effects before that range update returns. They waited for the next commit of
  some other component.
- A component that suspended with no `V.Suspense` above it commits the render its resolved resource asks
  for: that render's layout effects run, and so do those of the components it mounts. Only its imperative
  handles ran, and none of those layout effects ran in that commit.
- Components mounted while a commit runs its layout effects — the fallback an error boundary shows for a
  layout effect's error, for one — commit their own layout effects as a commit of their own, after every
  commit of the same flush. They ran inside the commit that mounted them, ahead of the flush's other commits.
  A chain of such commits that does not settle — a fallback whose layout effect throws into its own boundary
  on every mount, for one — stops after 100 of them with an `InvalidOperationException`, and the next update
  does not run it again.
- A batch drain that re-renders several components commits their layout effects as one commit, matching
  React's all-cleanups-before-all-setups across it: the layout-effect cleanups of every component the drain
  re-rendered run before any of their setups, each pass child before parent and sibling components in tree
  order, whatever order their updates were made in. The first component's commit ran
  the layout effects of every inline child the drain had re-rendered, whichever component it belonged to, and
  each component then ran its own cleanup and setup before the next one's cleanup.
- A component mounted into a wrapper element, as a `V.VirtualList` item is, commits the layout effects of its
  own subtree only. Its mount also ran those of the components the enclosing reconcile had mounted and not yet
  committed, before that reconcile had attached their refs.
- After an error boundary caught an error thrown outside a reconcile — from a layout effect, a `UseEffect`, a
  `UseFrame` callback or a component's own re-render — the next reconcile to start in the tree was discarded,
  so that component's update did not show until it rendered again, and a second boundary catching before then
  kept the content that had thrown in place of its fallback. Both now commit.

- A `Hooks.Use` loader written without a `resourceKey` as a lambda rebuilt each render is kept through the
  StrictMode re-run of the render that read it, as React's second invocation keeps its thenable, where the
  re-run restarted it.

- A render that a component's own update starts no longer throws the Suspense signal out of the frame
  when something in it suspends, in any slice of a time-sliced render. The nearest boundary above renders
  again, and the updated component renders again inside it; with none, each render that suspended is
  retried when a resource of the component whose read suspended it resolves.

- A render that suspends with no boundary above leaves none of the props it passed to the component that
  suspended committed, as React discards its work in progress, and a component that render first mounted is
  rendered by the next render that reaches it: a later update renders the component again and suspends again
  while the resource is pending, where it skipped a memoized one and committed the rest around it.

- A component whose render suspends while another component's render reaches it below a `V.Suspense`, with
  no host element between it and the Suspense, keeps its state while the boundary shows its fallback, as
  React keeps it offscreen, and the boundary reveals it when its resource resolves. That render's pass
  disposed it, losing its state and the read the reveal waited on; one inside a host element of the
  boundary's children is still disposed with that element. A component the boundary had shown has its layout
  effects and imperative handles taken down in the commit that shows the fallback, none of the layout work of
  the render that hid it commits, and one that render first mounted runs none of its effects, passive ones
  included, and creates no handle until the reveal; on reveal the layout effects run again and the handles
  are created again, as React disconnects and reconnects them.

- A component that renders its own `V.Suspense` and whose read suspends below an outer boundary reveals
  through that boundary when the resource resolves, as React takes the nearest Suspense above the component
  that suspended. It committed its own rows while the outer boundary kept showing its fallback.

- A resource that faults below an error boundary inside a `V.Suspense` has the Suspense render again, so the error
  boundary catches the error in that render and the Suspense reveals the error boundary's fallback, as React's
  retry does. The Suspense stayed on its fallback, and the error boundary's fallback was reconciled outside it.
- A component in a `V.Suspense`'s fallback whose own update suspends goes to the Suspense above that one, as
  React renders a fallback under the Suspense above it, and so does a resource of it that resolves. The component
  rendering the inner Suspense was asked to render first, and rendered its children again.
- A component whose first render throws in the update of a component below the error boundary that catches it
  is disposed, as React discards it: what its hooks took before the throw, a `Hooks.UseStore` subscription among
  them, is released. It stayed registered and queued for a layout commit.

- Two sibling components that share a key both render, each with its own state, under a warning. A later
  render in which the repeats keep their relative order keeps each sibling's elements. The repeat was skipped.
- `V.AnimatePresence` renders every child that shares a key, under a warning, and a later render in which
  the repeats keep their relative order keeps them all. When fewer children with that key remain, the surplus
  repeats leave at once without playing an exit. The later child replaced the earlier one.

- A `V.VirtualList` item whose key repeats one another item returns renders, under a warning naming the
  key when both render in one range. Items sharing a key are told apart by their item index: each keeps the
  row rendered at its own index while it stays in the range, including across a range change that scrolls
  the other out of the range or back into it. The repeated item was left out of the range.

- Two siblings sharing a key in a container holding a `V.Fragment`, a `V.Provider` or a component each keep
  their own element across a render that leaves them, and every sibling before them, in the same order, as
  React's linear pass keeps them.
  The repeat mounted a fresh element on every render, and the first was patched onto the last one's element.
  The repeat is reported with a warning on every render, mount included.
- A `V.VirtualList` scrolled past its end — its items shrank while it was scrolled down — renders its last
  item. The range update threw.

- A keyed `V.Fragment` is accepted as a `V.AnimatePresence` child, as Framer Motion's `AnimatePresence`
  accepts one: its first Motion is its anchor, and every element it places is held until its Motions' exits
  finish. Under `AnimatePresenceMode.PopLayout` it exits in flow, unpinned. It used to be dropped with an error.

- `animate-pulse` eases each half of its loop with Tailwind's `cubic-bezier(0.4, 0, 0.6, 1)`. It followed a
  cosine, which passed through full and half opacity at the same moments but between them strayed from
  Tailwind's curve by up to 0.006 of opacity.

- An `animate-*` loop and a per-frame writer on the same slot of one element resolve the way they do beside
  Framer Motion. The loop's frame is written over a `transition-filter` tween and over a `Spring` or `Bezier`
  `V.Motion` `rotate` channel, as a CSS animation outranks an element's ordinary declarations; a `Spring` or
  `Bezier` `opacity` channel shows over `animate-pulse` while its play drives it and hands the slot back when
  the play lets go, as Framer runs opacity on the browser's own animation engine. The slot used to show
  whichever of the two had written last.

- A `Spring` `V.Motion` channel, and a `layoutId` move, settle for any positive, finite `Stiffness`, `Damping`
  and `Mass`. Each tick now takes the spring's exact motion over that tick, the solution Framer Motion's
  spring evaluates; the step it replaced ran away once `Damping` times the tick over `Mass` reached 2 —
  `Damping` 125 at `Mass` 1 at 60 frames a second — and the play never completed.

- A `V.Motion` moving under a `layoutId` while drawn at a different scale on each axis draws its pixel corner radii
  circular, as Framer's shared layout does, rather than elliptical. Its background colour, background image (a
  `bg-gradient-*` included), border and ring are painted rounded by each axis's own scale, its colours following
  its classes and the transitions it declares for them, and one that clips its content (`overflow-hidden`) clips
  it to that rounded edge — at its border edge, where it has a border. Its inline background colour, border
  colours and background image, and for one that clips its background position, repeat and size, hold Velvet's
  values while it is drawn so, and are handed back as the move lands.
  A Motion whose background image is repeated, positioned, sized in lengths, nine-sliced or a vector image keeps
  the elliptical corners.
- The face a `skew-*` or upright `shadow-*` element repaints draws each border side in its own colour and width,
  rather than every side in the left one's, two sides meeting in a corner divided on the line from its outer corner to its inner one, as a dashed side is.

- A `V.Motion` that takes a `layoutId` another holder still holds crossfades with it over the move, as
  Framer Motion's shared layout does: it fades in while the holders behind it are drawn over its box and fade
  out from the previous holder's opacity, taking no pointer while they are drawn. The fade is written over the
  opacity the element's classes, variants and drivers give it, which keeps running under it and past it. Alone
  under the id, the lead mixes its opacity from the previous holder's.

- A filter cleared from a `transition-all` element while a Motion's own animation has its transitions suspended
  is cleared at once. The check for a running filter transition read the suspension's `transition-property:
  none` as naming filter.

- A `layoutId` Motion whose bundled `scale-*`, `rotate-*` or `origin-*` class changes in the render that hands it
  the lead, or while its move runs, is drawn at its new scale and scaled about its new origin from the frame of that
  render, and a lead moving from another holder's box mixes towards its new rotate from that frame. This holds where
  the bundled stylesheet is attached at or above the Motion and no rule other than an ungated bundled utility could
  give it that property; a pseudo-class-gated variant, an `anim-*` class or a rule of another stylesheet is taken
  once the styles have. That frame drew it with the classes it had before the render.

- Several `V.Motion`s may hold one `layoutId` at once, as Framer Motion's shared layout lets them. The one
  that took the id last leads and the others are hidden; a render that moves one of them no longer takes the
  id over and tweens it from the lead's box. When the lead leaves, the holder of those left that took the id
  last tweens from the box the lead left, whether or not its own layout changed; a holder inside a
  `V.AnimatePresence` child hands the lead to the latest holder before it that is not exiting as its exit
  starts, and is removed once that holder has landed. A Motion whose `layoutId` becomes null stops holding the
  id. Before, whichever of them a render patched last took the id, and a lead that left handed it to nobody
  still mounted.

- A `layoutId` Motion mounted inside one that is growing or shrinking is corrected for that change of size on
  the frame it is first laid out. It was drawn stretched with the outer one for that frame.

- A `layoutId` crossfade that starts while the element's own opacity is running on a transition runs it on as
  UI Toolkit runs it, from where it had got, inside its delay or past it, and shortens a change back towards where
  it started as UI Toolkit shortens a reversal. It ran only what was left of the duration, without the delay.

- A lead alone under its `layoutId` whose own move interrupts the opacity mix from the previous holder holds the
  opacity the mix had reached until that move lands.

- While a `layoutId` move runs, the transitions the element's classes declare go on running for every property
  but the ones the move writes, as Framer leaves the CSS transitions of what a layout animation does not animate,
  and a change of those classes mid-move is taken as it comes. The move suspended all of them.

- A `V.Motion` that takes a `layoutId` from another holder mixes its rotate from that holder's to its own over
  the move, as Framer Motion's shared layout does, and the holders drawn behind it take the lead's. Its own
  rotate still running on a transition as the move lands runs on to its end, through any move that starts
  meanwhile, and a running `animate-spin` on it turns on top of the rotate the move mixes.

- A `V.Motion` that takes a `layoutId` from another holder mixes its border radius from that holder's to its own
  over the move, as Framer Motion's shared layout does, without taking it below zero or mixing a pixel radius
  with a percent one, and the holders drawn behind it take the lead's. A layoutId Motion drawn scaled has a
  pixel border radius divided by that scale, by the geometric mean of the two axes where they differ, and a
  percent radius left as it is. Its own radius still running on a transition as the move lands runs on to its
  end, through any move that starts meanwhile.

- A `V.Motion` moving under a `layoutId` composes its move with its own translate and scale as they are now,
  rather than as they were when the move began. A change of either during the move, by a class or an inline value,
  runs on the transition the element declares for it, as UI Toolkit would run it. One already running as the move
  starts runs on from where it had got rather than jumping to its end, and one still running as the move lands runs
  on to its end rather than starting again. A translate given as a percent of the element's size is drawn at that
  percent of the size it has on each frame rather than transitioning towards a new pixel value, and a change of
  either axis of the translate or the scale starts both axes over, as UI Toolkit runs each as one value. A follower
  that takes the lead back as its leader leaves starts from its own translate, scale and rotate rather than from where
  it was drawn over the leader. A holder that takes the lead in the render that changes its classes moves at its new
  translate and scale from its second frame on. A corner radius of its own already running from pixels into a
  percent, or back, as the move starts runs on from where it had got, and a rotate of 0deg after one of none, or of
  45deg after an eighth of a turn, starts over on its transition rather than running back, as UI Toolkit runs both.
  A transition of its own rotate or radius runs on while it is drawn over a lead or held at a mix an interrupting
  move left, rather than standing until those values are its own again.

- Two variant rules of one rank that name the same class keep it while either holds:
  `nth-1:opacity-50 nth-2:opacity-50` dims the first row, and `data-[selected=true]:bg-blue-500
  data-[highlighted=true]:bg-blue-500` stays blue on a selected row that is not highlighted.
- An element no longer gathers another variant manipulator each time its className changes a `group-*:` or
  `peer-*:` rule stacked over another variant (`group-hover:hover:bg-red-500`), each time a className drops a
  stacked rule and brings it back (`group-hover:hover:bg-red-500`, `hover:focus:bg-red-500`), nor each time
  the outer variant of a three-deep stack whose middle is `dark:`, a breakpoint or `disabled:` turns off
  (`hover:dark:focus:bg-red-500`).
- Above `md`, `p-[12px] md:pt-6` takes its top padding from `pt-6` and the other three sides from `p-[12px]`,
  where the shorthand used to keep all four.

- `origin-[…]` takes CSS `transform-origin`'s grammar inside the brackets: the `left` / `center` / `right` /
  `top` / `bottom` keywords, alone or mixed with a length (`origin-[left_20px]`), a keyword-only pair in
  either order (`origin-[bottom_left]`), and a third component as the z length (`origin-[50%_50%_10px]`).
  Each of those was refused. A pair CSS declares invalid, such as `origin-[top_20px]`, is still refused.

- Adding or removing the marker of a responsive scope in an element's `className`, `@container` for the
  container variants and `VelvetResponsive.ViewportClass` for `sm:`…`2xl:`, re-points the variants of
  descendants that are already attached, as a CSS container query re-evaluates when an ancestor gains or
  loses `container-type`. They used to keep the width source they bound when they attached.

- `has-[.foo]:` matches a descendant whose `className` holds `foo` while a variant or an important utility
  keeps `foo` off that descendant's class list, as `:has(.foo)` tests the class attribute rather than which
  declarations won. It used to stop matching.

- Two variant rules that set the same inline-resolved property keep a value each, so one turning off no longer
  removes the other's: `odd:bg-[#fff] even:bg-[#eee]` colours the odd rows, and
  `data-[state=open]:w-[10px] data-[state=closed]:w-[20px]` keeps 10 px while the state is open.

- An element the reconciler removes from a `gap-*`, `grid-cols-*`, `divide-*` or `[&>*]:` container no
  longer keeps the margin, width, divider or payload that container wrote on it. The value stayed on an
  element that was discarded rather than pooled, so code that kept a reference to one and re-parented it
  carried its old container's styling along.

- Dropping `flex-wrap` from a `gap-*` container without writing `flex-nowrap` in its place ends the wrap
  spacing on that patch, and dropping every direction class (`flex` included) moves a plain `gap-*` to the
  column axis the container then lays out on. Both verdicts fell back to a `resolvedStyle` that could still
  hold the removed class, so a fixed-size container kept the old spacing until something unrelated
  re-applied the gap.

- Honor leading and trailing important modifiers on preset and arbitrary gap utilities. Important gaps
  beat ordinary declarations independently for each axis, including active variants, and restore when
  a variant turns off.

- Resolve variant font weights and Addressables font keys whose bracketed values contain a colon,
  including stacked variants and important font payloads. Turning the variant off clears the font.

- A `group-*:` or `peer-*:` consumer lights at once when its source is already under the pointer, or already
  holds focus inside it, as the consumer mounts or a keyed reorder moves it. It used to wait for the next
  pointer or focus event on the source, and a moved consumer dropped a lit hover until then.
- A `group` or `peer` source that code outside a render adds to, removes from or reorders within a panel is
  picked up or released at the panel's next scheduler update. It used to wait for the next render.
- An important `shadow-*`, `inset-shadow-*` or `inset-ring-*` (`!shadow-lg`, `shadow-lg!`, `dark:!shadow-xl`) and an
  important `clip-path-[…]` apply, and each wins over a plain one writing the same part of the element wherever it
  sits in the class list. On the element itself they used to be dropped, and behind a variant read as plain (an
  important variant clip on an element with no other clip was dropped too).

- An important `z-*` (`!z-10`, `z-10!`, `!z-[5]`, `z-[5]!`) wins over every plain `z-*` on the same element,
  wherever it sits in the class list, as Tailwind's `z-index: 10 !important` does. The modifier was accepted
  and ignored, so `!z-10 z-40` stacked the element at 40.

- A `peer-*:` consumer reacts to a preceding `peer` source that is itself z-managed (such as `absolute z-10`).
  The search read the placeholder the source leaves at its declared slot, which carries none of the
  source's classes, so the consumer never found it.

- `z-auto` resets the element to unstacked, so `z-10 z-auto` no longer stacks at 10.
- An important `font-*` or text-effect utility (`!font-[weight:700]`, `!leading-[24px]`, `!uppercase`,
  `!whitespace-pre-line`) applies and wins over the element's plain ones, and an important variant payload
  (`hover:!font-[…]`) wins over an important base one. `!font-[…]` and every important text-transform,
  text-decoration, `leading-*` and `whitespace-pre-line` token used to be dropped entirely.
- A `peer-*:` consumer, or a `peer` source, carrying `clip-path-*` is matched through the wrapper that holds
  its slot. A consumer's search started inside the wrapper and found no sibling, and a clipped
  `peer-checked:` source's checked state was never read.

- A filter change that lands at once no longer ends a running transform-origin transition on its element.
- A gradient, a clip path or a projected corner face sizing an element's background no longer ends a running
  transform-origin transition on it.
- A `z-*` element whose skew or shadow paints its border on an overlay keeps that overlay directly after it when
  the layer resorts; the overlay went to the end of the layer, over the elements stacked above.

- A filter transition that fades out an `opacity()` the element's inline filter held, written there by code other
  than the filter utilities, fades it to `1`, CSS's no-op value. It faded it to `0`, hiding the element on the
  way out.

- A hand-authored `transition-property` naming `filter` together with `all`, `background-size` or
  `-unity-background-scale-mode` animates a filter change once. Velvet's `transition-filter` tween ran on the
  same property at the same time as UI Toolkit's own animation.
- Under `transition-all` or a bare `duration-*`, a `contrast-*` added or removed at the end of the filter
  list fades from or to `1`, CSS's identity, where it faded from or to `0`, which is flat grey.
  Under `transition-filter`, removing a `contrast-*` that leaves no filter no longer fades it toward `0`
  again once the tween has ended.
- The `transition-filter` tween waits out a `transition-delay`, takes its duration, delay and curve from
  the `transition-*` entry naming `filter` rather than from the first, and reads a duration written in
  `ms` as milliseconds rather than seconds.

- A filter change on an element whose hand-authored `transition-property` names `filter` animates without the
  element carrying `transition-filter`, removing a filter as adding one does. Adding a filter applied at once
  and removing every filter was animated by UI Toolkit.
- A `transition-property` naming `background-size` or `-unity-background-scale-mode` leaves filter changes
  instant where no entry runs for `filter`, and where one does, the filter change runs for that entry's
  duration, delay and curve. UI Toolkit animated them by the `background-size` entry.
- `grayscale-[N]`, `invert-[N]` and `sepia-[N]` clamp an amount above 1 to 1, as CSS does.
- Under `transition-all`, a `brightness-*` and a `contrast-*` added to the end of the filter list together
  fade the contrast in from `1`, and an element leaving the panel part way through a removed contrast's fade
  is left with no inline filter rather than `contrast(1)`.
- `grayscale-[N%]`, `invert-[N%]`, `sepia-[N%]`, `contrast-[N%]`, `brightness-[N%]` and `saturate-[N%]` take a
  percentage, as Tailwind writes them.
- Under `animate-hue`, a filter utility's change starts no filter transition. When `animate-hue` ends, the filter
  under it, a hovered or other variant filter included, is written at once, and a filter transition still running
  shows again until it ends. The variant filter was lost, a filter-free element faded its hue out, and a running
  transition was cut to its end or restarted.
- Removing `animate-pulse` or `animate-spin` leaves a running filter transition alone, where it restarted.

- Turning `enableRichText` on or off on a text leaf builds the string it shows again at its next repaint,
  where the change was read only at the next time the leaf's text effects resolved.
- A text leaf with a text effect (`uppercase`, `underline`, `leading-*`, …) and an ellipsis from the start or
  the middle (`-unity-text-overflow-position: start | middle`) elides the string it shows, where the engine
  elided the original text and dropped the effect. Such a leaf holds the shown string as its `text`.

- `text-[150%]` and `tracking-[50%]` are measured against the font size they are a percentage of, where they
  were written as 150 and 50 pixels.

- A portal into a container Velvet renders keeps patching its own children when a render of the same tree
  adds or removes the container's own children ahead of them — the element's own, or a component's among
  them rendering again. The next patch wrote over the container's own child and left the portal's behind.

- `V.Anchored` among a `V.WorldSpace` panel's children sits where the camera's ray to its target crosses that
  panel's plane, and hides where the ray does not cross it. It was placed by a screen-space projection,
  which misplaces it on a world-space panel.

- A portal declared among the children of the container it targets keeps patching its own children when the
  container renders its children again. Its patch logged that the target must not be the element being
  reconciled, and a change to the container's children ahead of it in that render put its children over
  theirs.

- A logical ancestor's `events:` handler fires once for an event raised inside a portal whose target sits in
  another portal's content. The event reached it through both portals.

- A portal whose id is registered from a `refCallback` written as an inline lambda, a new delegate at every
  render, mounts its children. The old callback's cleanup ran where the element was patched, unregistering
  the id ahead of the portal's patch in that render, and the registration that followed asked for another
  render, which did the same again. A callback whose identity changes now has its old cleanup run after the pass,
  where the layout commit that attaches the new callback begins, ahead of every setup that commit runs, as React
  detaches and attaches refs in its commit.

- A ref cleanup that throws into an error boundary when its callback's identity changes leaves the rest of
  that render to commit. It ran inside the render, where the boundary's catch stopped the components after it
  from rendering.

- `V.Anchored` inside a parent that is scaled or rotated lands on its target's projection. The panel point is
  taken into the parent's local space rather than reduced by the parent's on-panel origin, which only a parent
  without a transform leaves the same.
- A `V.Portal(layer:)` or `V.WorldSpace` host that a scene unload destroyed is replaced by the next patch of a
  portal on it, which mounts the children into the replacement. The portal used to warn and render nothing until
  the node was remounted. The state of children that mounted into the dead host does not survive the move.
- The warning for a portal whose id is not registered yet says its children mount once an element is registered
  under that id, which the portal does, rather than that they will not be rendered.
- A portal into a container finds its children by the rows it holds, so a row put ahead of them by other code or
  by another mounted tree rendering the container no longer sends the portal's next patch, or its unmount, onto
  that row.

- Tab across a `PanelFocusOrder.Chained` host's boundary hands focus to the other panel inside the dispatch of
  the key, as a document's single focus owner moves. Before, the landing took focus on its panel's next
  scheduler tick, and no element held focus until then.

- An element focused in a panel Velvet renders into and then moved to another panel by user code is dropped
  from the first panel's focus memory, as an element leaving a document takes no focus with it. Before, that
  panel losing focus blurred the element in its new panel, and the panel regaining focus focused it there.

- An element released while it was gaining focus, and never mounted again, is dropped from its panel's focus
  chain on the panel's next tick, with its focus pseudo-state. Before, it stayed there until the panel's next
  focus change, and its re-scrub callback stayed on the pooled instance.

- A `contain` scope pulling focus back from a `UIDocument` panel no mounted tree manages now blurs the landing
  first, as it already did for a panel a tree manages, so that panel does not take the landing back for a frame.

- A tree disposed after the element it was mounted on left its panel leaves no focus behind in that panel: a
  pooled `Button` from it no longer takes focus back when focus returns to the panel it left.

- A pooled element such as a `Button` that held focus when it was unmounted, or was taking it as it was
  unmounted, behaves as a new one when it is mounted again. Mounted again on the same panel, it was already that
  panel's focused element without anything focusing it. Mounted on another panel, its first `Focus()` was undone
  when the panel it left was the one UI Toolkit's runtime event system had focused last, and focus returning to
  that panel raised a `FocusInEvent` on the Button in its new panel. A composite field such as a `Toggle` whose
  focused child is unmounted no longer keeps its focus state.

- An element on a drag's press path that leaves the tree mid-drag, a pooled one included, drops the drag's
  callbacks and pointer hold at once and is left out of the release settle; it kept them until the drag
  ended, and the settle then settled whatever the element had become.
- A raw `DragOverlay` prop that a component mounts on its own re-render inside a portal pairs with the
  `V.DndContext` around the portal's call site and shows the drag; it stayed hidden.
- A draggable inside an engine `ListView` with `reorderable: true`, or inside a touch `ScrollView`, keeps its
  press: every move a drag observes carries UI Toolkit's draggable claim, so neither control starts a
  gesture of its own under it.

- Honor `FiberElementProps.NoDrag` on the draggable itself for presses on it or its children. Clearing
  the marker on a later render restores dragging, while a nested draggable keeps its own activation.

- A `Router`'s history keeps every entry, as a memory router's does. It used to drop its oldest entry
  once it held 50, so walking back from a long session stopped short of where it began.

- A `V.RouterProvider` handed a different router publishes that router's location in the render that
  hands it over. It used to publish the previous router's location until its effect subscribed to the
  new one.

- `AttachExternalCancellation` settles as .NET's Task.WaitAsync does. Over a `VelvetTask.WhenAll` it keeps
  every member's fault, which `AsTask()` then hands on, where it kept the first alone. A cancellation of the
  task it waits for comes out as that task's own `OperationCanceledException`, where it came out as a new one
  carrying the attached token. It settles on the thread that completes that task. Attached on the main thread
  to a task another thread completed, it settled only once Unity ran the main thread's posted work, so
  waiting there on its `AsTask()` never returned. A task already complete comes back as it stands when the attached token is already
  cancelled, where it came back cancelled. A fault arriving after the attached token has cancelled is logged,
  where it was dropped.

- `Forget()` logs every fault a `VelvetTask.WhenAll` holds, where it logged the first alone, and logs nothing
  for a cancelled task, where it logged its `OperationCanceledException`.

- A `VelvetTaskCompletionSource`'s `Task` may be awaited and read any number of times, before and after it
  completes, as a TaskCompletionSource's may. A second awaiter while it was pending threw, and so did an
  await or a status read of a held task that had already been consumed.

- `V.Particles` draws a world-space or custom-space source at the element, as it draws a local-space one.
  Such a source used to warn on mount and draw its particles far outside the element, read as local
  positions of a host parked away from the origin.

- `V.Particles` draws every live particle of a system. It drew at most 2048 and warned on mount when an
  effect declared more.

- `V.SceneView` on a runtime panel re-derives its texture when the panel's pixel density changes, as it did
  on an editor panel. A panel-scale change moves no geometry, so the texture kept its old size until the
  element's next layout or props change.

- The preview window's **Refresh** button discovers the stories again. It re-listed the set discovered first,
  so a story an assembly loaded since then did not appear.

- The DevTools window keeps its selection on the selected fiber when another entry leaves the registry. It
  kept the selection by position, so removing an entry above it moved the selection to another tree.

- A `V.Portal(layer:)` declared on a panel that renders into a target texture draws into that texture, sorted
  around the panel by its layer. Its host panel used to draw on the screen, so the portal was missing from
  the texture, and from the preview window. A `VelvetPreviewPanel` clears its texture below every layer, so
  a Background portal is not erased by the panel above it.

- An instance `[MemoizeMethod]` method whose `_Impl` is an instance method keys its memo on the instance it is
  called on, as well as on its arguments. Called at one position on another instance with equal arguments, it
  served the node the first instance built.

- `[MemoizeMethod]` escapes a contextual keyword Roslyn classifies as one — `record`, `required`, `scoped` and
  `file` among them — used as the name of a type, a type parameter, a namespace, a parameter or the method in
  its generated wrapper. A type named `@record` drew CS8860 from the generated code, and one named
  `@required`, `@scoped` or `@file` failed to compile there from C# 11 on.

- VEL100 no longer reports a get-only auto-property a hook lambda captures. It is assigned only at
  construction, as a `readonly` field is, which VEL100 already left out.

- `[MemoizeMethod]` generates its wrapper for a method of any number of parameters, and for a generic method,
  repeating its type parameters and their constraints. Nine or more parameters were reported as VEL002 and a
  generic method as VEL003, and neither got an implementation, so the declaration failed to compile. Both
  diagnostics are gone. A generic method's type arguments are part of the memo's dependencies, so a call at
  the same position with another type argument rebuilds.

- `[MemoizeMethod]` accepts an `in` parameter, memoizing on a copy of it, including an extension method's
  `this in` receiver; VEL005 now reports only `ref` and `out`. A `params` parameter, an extension method's
  `this`, and the `new`, `virtual`, `override`, `sealed`, `readonly` and `unsafe` modifiers are repeated on the
  generated implementation, as are the `class` and `default` constraints a generic override states; a C#
  keyword used as the name of a parameter, a type parameter, a type or a namespace is escaped there; and a
  method declared in an interface is implemented in a partial interface. Each of these left the declaration
  without an implementation that compiles.

- A `[MemoizeMethod]` `params` array is compared element by element, behind its length, so a call with equal
  arguments reuses the cached node. The array the compiler builds afresh at every call was the dependency
  itself and never compared equal once the method had another parameter or the array's elements were a value
  type. Any other array parameter is one dependency compared by instance; where it was the only parameter and
  its elements were a reference type, it became the whole dependency list, compared element by element, or,
  passed null, no list at all, which rebuilt every render.

- A `[MemoizeMethod]` method taking no parameters no longer warns (VEL001) when the generator cannot prove its
  `_Impl` pure, and VEL001 is gone. Where its `_Impl` is static, its empty dependency list is a complete
  declaration, as `useMemo(factory, [])` is in React; an instance one keys on the instance it is called on.

- VEL008 reports a `[MemoizeMethod]` return type deriving from `VNode` other than `MemoNode`, and a return by
  reference. It accepted both, and the generated body, which returns a `MemoNode` by value, then failed to
  compile.

- VEL010 reports a `[MemoizeMethod]` parameter of a ref struct type such as `Span<T>`, of a pointer type, or of
  an array of pointers. None of them can be a dependency, and the generated wrapper failed to compile instead.

- VEL011 reports a `[MemoizeMethod]` instance member of a struct that is not `readonly`, or of a `ref struct`.
  The factory calls `_Impl` on a copy of `this`, which a lambda in a struct cannot capture, so a write to the
  struct would be lost; a ref struct cannot be copied anywhere the factory can reach. A `readonly` member of
  any other struct is supported.

- A `[MemoizeMethod]` method returning `VelvetTask<VNode>` is reported as VEL004, as one returning
  `Task<VNode>` is, rather than as VEL008.

- VEL100 no longer reports object-initializer member names as missing hook dependencies. Dependencies read on the right-hand side are still checked, and the code fix adds only a missing captured value.

- A stylesheet whose `@import` list is edited in place during an editor session is read again the next time
  the missing-stylesheet warning or a portal host looks at it, rather than answered from what it imported
  before the edit.

- A player build no longer fails on a read-only `ProjectSettings/GraphicsSettings.asset` or
  `ProjectSettings/ProjectSettings.asset` when it has nothing to write there: a project that lists the
  shaders in Always Included Shaders, or preloads the stylesheet's holder, itself — or opts out of them —
  and has no injection left over from an earlier build builds from a read-only file. The build still
  refuses a read-only file it would have to write.

- The missing-stylesheet warning looks when the target's panel next updates rather than at the `V.Mount`
  call, so a sheet attached right after `V.Mount` no longer draws the warning.
- Disposing one of two mounts on the same target no longer stops the missing-stylesheet warning for the
  other.
- `VelvetStyleUtilities.AttachTo` and `BindThemeTo` no longer keep a panel's root alive after the panel is
  disposed; the static theme event held it for the rest of the domain. A bound root now follows
  `VelvetTheme.IsDark` whether or not it is on a panel.

- Siblings sharing a key in a container of plain elements are reported with a warning on every render that
  repeats the key, mount included, as React's “Encountered two children with the same key” is. A repeat
  went unreported at mount and on a render keeping the siblings in the same order, and a render whose
  children no longer repeated a key was reported for the render before it.

- A `VelvetTask.WhenAll` whose first cancelled member decides it throws that member's own
  `OperationCanceledException`, as `Task.WhenAll` does, where it threw a new one carrying the member's token.

- An `async VelvetTask` method that throws an `OperationCanceledException` before it first suspends
  returns a task that throws that same exception, as an `async Task` method's does, where it threw a new one
  carrying its token.

- `VelvetTaskMethodBuilder.Task` and `VelvetTaskMethodBuilder<T>.Task` return the same task on every read
  for one call, as AsyncTaskMethodBuilder's does. A read after the task had been consumed returned a
  pending task, and once another call of the same method took the pooled state machine, that call's task.

- A theme token written in `rem` or in a CSS math function (`--spacing: 0.25rem`, `--breakpoint-3xl: 120rem`,
  `--radius-card: calc(...)`) now resolves as Tailwind v4 reads it, with 1rem as 16px. UI Toolkit's importer used to reject a `rem` in a custom property and leave the whole file empty, and to drop a
  math function, so tokens had to be written in `px`. Velvet's importer, which `Velvet.Editor` selects in the meta file of
  a USS file that needs it, stores those custom properties as the text they were written with.

- `caret-*`, `selection:bg-*` and `selection:text-*` multiply a colour's own alpha by an opacity modifier, as
  `bg-`, `text-` and `border-` do and as Tailwind's `color-mix` with `transparent` does, so
  `caret-[#ff000080]/50` is a quarter opaque rather than half.
- A bracketed length's unit is read in any case, as CSS reads it, so `w-[10PX]`, `top-[1IN]`, `w-[2EM]`
  and `leading-[1.5EM]` apply where they used to leave the class inert. `border-*`, `text-*`, `ring-*`,
  `outline-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` still take a length only in Tailwind's case,
  as Tailwind does, except inside a math function or behind `text-[length:…]`.
- An angle or a time in a bracket is read in any case, as CSS reads it: `rotate-[45DEG]`, `hue-rotate-[.5TURN]`,
  `duration-[150MS]`, `animate-spin-[2S]`. `skew-x-[…]` takes any angle unit, not only `deg`, and a bare `0`.
- A math function is spaced as Tailwind spaces it before it is read, so an operator spaced on one side,
  `calc(1px_-2px)`, subtracts where it was declined, and a function name is read in any case: `cALC(1px_+_2px)`
  is 3px, while `cALC(1px+2px)`, which Tailwind leaves unspaced, is declined as CSS declines it. A space at
  either end of a length (`top-[_calc(1px)]`, `w-[1px_]`) is trimmed as the browser trims it.

- `border-[thin]`, `border-[medium]` and `border-[thick]` are 1px, 3px and 5px, and `text-[…]` takes the
  font-size keywords (`text-[x-large]`, `text-[larger]`, …), as Tailwind's type tests hand them to
  `border-width` and `font-size`.

- Tailwind's bracket type hint (`[length:…]`, `[color:…]`) is read beyond `text-[length:…]`: a utility that writes
  its bracket through drops it (`w-[length:10px]`, `gap-[length:8px]`; an empty hint, `w-[:10px]`, is none), and
  `border-*`, `text-*`, `ring-*`, `outline-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` take the branch it
  names (`border-[length:2PX]` is 2px, `text-[size:16px]` a font size, `text-[color:#f00]` a colour,
  `bg-[color:#f00]/50` a half-transparent red). `drop-shadow-*` and `font-[…]` take no hint, and `filter-[name:args]`
  and `bg-[addr:key]` are Velvet's own colon forms.
- `text-[length:red]/50` is no longer red text at half alpha: the hint names a length, which `red` is not, so the
  class is inert.

- A uniform `scale-<n>` keeps the axis a `scale-x-*` or `scale-y-*` variant above it leaves, where a class
  variant on the same element used to drop it to 1.
- A variant's translate or scale of one axis no longer drops the other axis's value once a class variant on the
  element is active: `translate-8 hover:translate-x-2 hover:bg-red-500` keeps its y at 32 while hovered.
- A `V.Motion` pose whose translate holds a value the browser rejects (`translate-x-[10]`) plans no translate
  channel, so the swap lands at once rather than springing one axis and then dropping the translation.
- A `V.Motion` swap adding a `translate-<n>` beside a resting `translate-x-*` or `translate-y-*` leaves that axis
  where the element draws it, important or not, instead of springing it to the both-axis value and snapping back;
  beside an important one the other axis now animates where it used to land.
- A `V.Motion` swap no longer animates a slot that a resting class or value in force under a variant holds
  (`focus:scale-50`, `focus:translate-8`, `focus:opacity-50`, `focus:bg-red-500`); the element keeps drawing that
  value, so the slot lands. A pose class the swap adds stays off the element while such a holder outranks it, as
  in CSS, where it used to be put back and drawn over the holder, and a pose class the swap removes stays while a
  variant in force names the same class (`opacity-50` beside `hover:opacity-50`).
- A `V.Motion` swap beside a resting translate or scale axis holding a value the browser rejects
  (`translate-x-[10]`) lands, where it used to animate the other axis and then drop the translation.

- An important modifier written behind its variants, as Tailwind spells it, is honoured where it used to be
  ignored: `selection:!bg-red-500` colours a text field's selection, `hover:!pt-2` beside `hover:!p-[52px]`
  pads the top by 8px while hovered rather than 52px, and the space a silhouette paint reserves for a state
  border counts `hover:!border-8` as it counts `hover:border-8`.

- A `[Component]` calling a hook inside another call's arguments, such as
  `=> V.Label(text: Hooks.UseStore(store, v => v).ToString())`, no longer throws an InvalidProgramException on
  its first render. Auto-memoization placed its cache check right after the hook, where the arguments evaluated
  ahead of it were still on the evaluation stack, and returned the cached tree over them. The check now
  discards them first, so such a component keeps its auto-memoization.

- A `[Component]` declaring two locals from `Hooks.UseStore(...).ToString()`, whose hook results the compiler
  stores into one local, re-renders when the first hook's value changes and the second's does not.
  Auto-memoization keyed its cache on that local twice, so it held only the second value, and the component
  showed its previous output.

- An auto-memoized component that builds elements ahead of its last hook no longer leaves their pooled parts
  counted as rented when a render reuses the cached tree. Those parts are released to the garbage collector
  instead of staying held by the pool for the rest of the session.

- A container variant (`@md:`, `@container/name`'s `@md/name:`) follows a container marker written straight to
  a class list, with `AddToClassList("@container")` or `RemoveFromClassList`, within a frame. It re-pointed at
  the marked element's next render, which never came for an element that renders nothing of its own.

- The bracket grammar for lengths, angles, numbers and times reads every math function of CSS Values 4, not
  only `calc()`, `min()`, `max()` and `clamp()`: `round()`, `mod()`, `rem()`, `abs()`, `sign()`, `hypot()`, the
  trigonometric functions, `pow()`, `sqrt()`, `log()`, `exp()` and the constants `e`, `pi`, `infinity` and
  `NaN`, with CSS's typed arithmetic (`calc(100px/2px*1px)` is 50px). A math function is read in an angle
  (`rotate-[calc(45deg*2)]`, a gradient's angle), a number (`scale-[…]`, `opacity-[…]`) and a time
  (`duration-[…]`) as well as in a length, and inside a `clip-path-[…]` or a radial or conic gradient's line it
  may be written with spaces (`clip-path-[inset(calc(1rem_+_4px))]`). A division by zero and a length past a
  float's range are read as CSS reads them, where they left the class inert: an infinity is the largest float
  of its sign and a NaN is 0.
- `ex`, `ch`, `cap`, `ic`, `lh` and the root-relative `rex`, `rch`, `rcap`, `ric` and `rlh` are measured from
  the font the element's text is drawn in and the line height Velvet gives it, in the sizing, position,
  spacing, `text-[…]` and `tracking-[…]` utilities that measure an `em` on the element, where they left the
  class inert. `outline-[2em]` draws a 2em outline, where it fell back to 3px.
- A literal past a float's range is the largest float of its sign, as Chromium reads it, where it left the class
  inert: `w-[1e39px]`, `blur-[1e39px]`, `rotate-[1e39deg]`, `duration-[1e39s]`, `scale-[1e39]`.
- `scale-[…]`, `opacity-[…]` and the filter amounts take a percentage, written or from a math function, as CSS
  does: `scale-[150%]` and `scale-[calc(150%)]` are 1.5, where `scale-[150%]` and `opacity-[50%]` left the class
  inert.
- A colour function reads a math function in a channel, a hue or the alpha, as CSS does:
  `bg-[rgb(calc(255/2)_0_0)]`, `text-[hsl(calc(60deg*2)_100%_50%)]`, where it left the class inert.
- A radial or conic gradient's `at_` position reads CSS's whole `<position>`: lengths (`at_20px_30px`), edge
  offsets (`at_right_10px_bottom_20px`) and a math function adding a percentage and a length, where only keywords
  and percentages were read.
- An outline's width is snapped to the panel's device pixels as the browser snaps a border width
  (`outline-[2.75px]` is 2.5px at two device pixels a point), where it was drawn as written.
- A length only an element can measure — a font-relative or viewport unit, or a math function reading one — is
  read by these utilities too, where it left the class inert: border widths, `rounded-*`, `translate-*`,
  `origin-[…]`, `shadow-[…]`, `gap-*`, `space-*`, `divide-*`, `ring-*`, `ring-offset-*`, `outline-offset-*`,
  `clip-path-[…]`, and a gradient's stops, radii and centre. A `translate-*` and an `origin-[…]` read a percentage of the element's own
  box beside one, and a clip-path or gradient length a percentage under a math function (`calc(100%_-_10px)`,
  `max(10%,30px)`), against the box it is drawn in.
- A bracketed border width or `divide-*` width is snapped to the panel's device pixels as the browser snaps a border
  width (`border-[2.75px]` is 2.5px at two device pixels a point, `border-[0.25px]` one device pixel).

- Preserve inline shorthand and longhand token order at rest, including reordered class lists, so Motion targets agree with the mounted values.

- A `V.Motion` whose `layoutId` moves to a new element while the element it leaves is still mounted — a
  move to a parent that reconciles before the one it leaves — tweens from the box that element stands at.
  When that element had not been patched since it mounted, the new one tweened from a zero-size box at its
  parent's origin, growing out of that corner, even where it took the old element's place exactly.

- A same-key type flip of a `V.Motion` carrying a `layoutId` tweens from the box the replaced element stood
  at. The replaced element's teardown dropped the id before its replacement registered, so the flip
  jump-cut. Once an element has left the tree, its id still hands nothing to a Motion that mounts under it
  in a later render.

- A `layoutId` move between two parents compares the old and new boxes in panel space. It compared them
  relative to each element's own parent, so a move between parents placed apart jump-cut wherever the box
  sat at the same place inside each, and a move out of a scaled parent started at the element's unscaled
  size rather than the size it was drawn at.

- A `layoutId` handed from one component to another by one state or store update tweens whichever of the
  two re-renders first. When the component losing it re-rendered first, its element's teardown dropped the
  id before the other component's Motion registered, and the move jump-cut.

- A `layoutId` Motion of an element type the pool reuses, such as `Button` or `Label`, removed while its
  move was waiting for layout no longer plays that move's tween on the element the pool hands out next.

- A `layoutId` tween that resizes the element starts over the old box. Its translate was the difference
  between the two boxes' top-left corners while the scale held the element's centre still, so the first
  frame sat off the old box by half the change in size; it now starts over the old box whatever the
  transform origin.

- A `layoutId` tween composes with the element's own `scale-*` and `translate-*` instead of replacing
  them. A resizing tween wrote its scale over the element's own for the whole tween, so a `scale-[1.5]`
  element started at the inverse alone and snapped back to its own scale at the end, and a move wrote its
  translate over the element's own `translate-*` the same way.

- A `layoutId` Motion inside another that grows or shrinks keeps its own size and its place inside the
  outer one while the outer one tweens. It was drawn scaled with the outer one — at half its size inside a
  Motion that doubled — and, when both were recreated, started from a box that ignored the outer one's scale.

- A `layoutId` Motion that moves again while its tween is running starts the new tween from where it is
  drawn. It started from its last layout box, so a fast second move visibly jumped before tweening.

- A `layoutId` handed to a new element no longer keeps the parent the old element stood in alive, which it
  did until the Motion patched again or left the tree even when that parent had been removed.

- A `layoutId` tween whose width and height change by different factors scales each axis by its own
  factor. It averaged the two into one scale, so it started off the old box on both axes.

- A `V.Motion` carrying a `layoutId` tweens every move rather than every other one. Starting a tween
  dropped the id's registration, so the next move found nothing to tween from and jumped to its new
  place; the move after that tweened again.

- A `layoutId` tween that a later move supersedes mid-flight hands back the transition suspension it
  took. On an element whose own classes transition translate or scale (such as `transition-transform`,
  `transition-all`, a `duration-*` utility), it kept `transition-property: none` on the element after
  the last tween settled, so the element's own transitions stayed off.

- A `V.AnimatePresence` child `V.Motion` that comes back in the render that changes its label rests at the new
  pose's `translate-*` value. Its own enter cancelled the pose swap that would have written the value, so the
  element kept the old translate beside the new pose's class. This held for a child following its parent's label
  whose `transition` is a timed classic one such as `StyleTransition.Fade`, or a spring or a bezier, and for a
  child with an `initial` pose whose translate matches the new label's.

- A presence child `V.Motion` whose `transition` is a spring or a bezier, and which follows its parent's label,
  no longer cuts short the pose swap its label change starts in the render that adds it back. That swap now
  tweens on the pose's transition, as it does for `StyleTransitionConfig.None`.

- A `V.Motion` label change into a pose whose transition has zero duration, such as `StyleTransitionConfig.None`,
  lands the properties that pose names within two frames. A tween swap that had already swapped went on tweening
  them on its own transition, and a spring or bezier play or reversal still running kept driving them toward its
  own target until it settled, where a `translate-*` value went back to the earlier pose's. A `filter` value
  lands the same way, and so does a value the tween was already headed for, where the pose spells it the way the
  tween's target does or as the same arbitrary value (`translate-x-4` for `translate-x-[16px]`); a stylesheet
  class and an arbitrary spelling of one value, such as `opacity-100` and `opacity-[1]`, are not recognised as
  one, and that property finishes the tween. A property a variant layer such as `hover:opacity-[0.3]` holds shows
  that layer's value at once rather than after the play settles. Properties the pose does not name keep moving
  on the play's timing. A bezier transition with zero duration now counts as zero duration here: it stopped the
  running play and snapped every property. A `MotionNode` built without `V.Motion`, which carries no
  transition, lands a label change the same way.

- A `V.Motion` whose tween, spring or bezier play, or a presence child's spring exit reversal, is still running
  when a zero-duration pose lands no longer ends carrying that play's earlier pose classes beside those of the
  timed pose that follows. The next pose's swap cancelled the play, and the cancel put back the classes the play
  had been started toward.

- A presence child `V.Motion` brought back mid-exit at a new label, or a `V.Motion` whose label changes into a
  pose whose transition has zero duration, no longer gets the earlier pose's arbitrary values, such as
  `translate-x-[40px]`, back when a later spring or bezier pose that does not name them settles. Where a spring
  or bezier was still moving such a value, the Motion rests at the play's target until its next label change,
  which now takes it away as the earlier pose's; an `opacity-[0.8]` left that way used to outrank a later pose's
  `opacity-50` for good.

- A descendant `V.Motion` of a removed `V.AnimatePresence` child — one that is not the child's anchor —
  moves to a `translate-*` `exit` pose at its exit's swap, as the anchor does. Its exit moved the pose's USS
  classes only, so it stayed at its resting translate until the child was removed.

- A descendant `V.Motion` of a `V.AnimatePresence` child whose key comes back rests at the pose the re-added
  child declares. Its exit put back the pose it had started from after the new one had been applied, so a
  descendant re-added at `opacity-50` from `opacity-100` carried both classes, whether its exit was still
  playing or had completed before the render that would have removed it.

- A `V.Motion` label change into a pose whose transition has zero duration, such as
  `StyleTransitionConfig.None`, lands on that pose alone when it follows a tween swap that has not swapped yet.
  The earlier swap still ran afterwards and put its own pose's classes back beside the new one's.

- A named uniform scale such as `scale-50` now supplies the untouched axis beside `scale-x-*` or
  `scale-y-*`, and changing or removing that preset updates the composition. Uniform presets retain
  their stylesheet order and existing inline and important priority rules.

- A `ring-inset` band paints under the element's children, above the element's own background, as CSS's
  inset box-shadow does. It painted over an opaque full-bleed child. The band is now the element's own first
  child, over its padding box, and rounds to the padding box's corners; it was a sibling that followed the
  element.

- Starting a width-only spring or bezier play no longer removes height from a held inline
  `transition-property` list, including on a clip wrapper.

- The preview window's Measure label is written when the overlay refreshes rather than while the overlay generates
  its visual content, where a panel does not accept a change that dirties an element.

- A removed `AnimatePresence` child plays the exit of each Motion in it that declares or inherits one,
  as Framer's does: one behind a component, `V.Memoized` or `V.Suspense`, or inside another element or
  Motion, exits too, and the child stays mounted until the last of those exits completes. Only the
  child's anchor used to exit, so a keyed `V.List` row rendered by a component vanished at once. A
  Motion inheriting its labels takes its coordinator's exit label, staggered by the coordinator's exit
  pose, and `initial: false` suppresses the mount enter of the Motions the presence's first render
  creates, where it reached the anchor alone. The children of an inner `AnimatePresence` stay that
  presence's. A variant Motion whose classic exit completed no longer keeps that exit's class when its
  key returns before the removal.

- An error an error boundary catches is logged, as React logs it. It was swallowed: the boundary showed
  its fallback and nothing reached the console, so a mistake a boundary caught — a hook-count violation
  among them — went unseen wherever the fallback looked plausible. The entry is a `Debug.LogException`
  that reads as the caught exception itself, and its stack trace goes on to name the boundary and the
  component stack. What a working application notices is that entry, and a Unity Test Framework case
  that arranges a catch fails on it as an unhandled log: pass `V.Mount` a `MountOptions` whose
  `OnCaughtError` handles the error. Expecting the log with `LogAssert.Expect` also passes, but the
  entry matches the one an uncaught error leaves, so it no longer tells a catch from an escape.

- A keyed `V.Fragment` that a component returns as its whole output keeps its key. Its key was dropped
  and its children taken as the component's output, so changing that key kept the components inside it
  where React remounts them; they now remount, and so do the elements it holds. React unwraps only an
  unkeyed top-level Fragment, and Velvet still unwraps that one. Such a component's own re-render now
  reconciles its output through the general path, which is not time-sliced, where one whose Fragment
  holds only elements took the flat diff, which a `StartTransition` update could spread across frames.

- A node the renderer of `V.List` returns that a list has placed before — returned for an earlier item
  of the same call, or held from an earlier render — keeps the key it was placed under, and the slot of
  an item whose selected key differs takes a copy of the node carrying that key. `V.List` wrote each
  item's key onto whatever node the renderer returned, so one node standing for several items carried
  the last item's key at all of them, and a node held across renders had its key changed under the tree
  that had committed it. Measured: five items sharing one `V.Component` node rendered one row, the other
  four skipped under a duplicate-key warning; one held `V.Component` node whose item's key moved on left
  the row it had rendered mounted beside the new one, on every move, until the tree unmounted; and five
  items sharing one element node in a list that takes the general walk had every row but one rebuilt on
  each render. Items keyed apart now have a row each, and a node the renderer has only just built is
  keyed in place, as before, rather than copied.
  What a working application notices is an element node held across renders — a placeholder or a
  separator kept in a field or a `Hooks.UseMemo` — whose item's key changes: that item is a different row
  now, its element built again rather than patched, as React rebuilds an element whose key changes, so
  its `refCallback` cleans up and sets up again. Keep the key stable where the element has to survive.

- A `V.VirtualList` updated to an empty item list releases the rows it was showing. The controller drops
  its tracked range before it renders an updated list, and the branch for an empty list released rows
  only while that range named some, so every row of the previous range stayed in the visible container
  over a spacer now zero high, released by nothing while the list stayed empty — their `refCallback`
  and `Hooks.UseEffect` cleanups with them. Rendering items again then reused those rows, and that is what a working
  application notices changing: a list that passes through empty on its way to new items — cleared while
  a reload is in flight, say — now shows no rows while it is empty, and its rows mount afresh afterwards,
  their hook state back at its initial values and their mount effects run again, as React unmounts and
  remounts them. State to keep across the empty render is lifted above the list, into a `Store` or a
  `Hooks.UseState` in the component that renders it. A list the node holds, cleared in place and later
  refilled, no longer brings a released row back either: the range update over the emptied list released
  the rows but left the render buffers naming them, so the one after the refill patched a released
  `V.TextField` back in with what had been typed into it.

- A slot whose component changes builds the arriving component's element rather than patching the
  departing one's into it. A component's subtree is expanded away before the host leaves are matched,
  so the diff saw two plain containers at one position and patched the first into the second, carrying
  children the arriving component had not declared. Where the departing body held a
  `V.AnimatePresence`, those were its committed children, and a second copy arrived with the next
  swap: the composition an old side reproduces them from is held against the boundary that rendered
  it, which the arriving component's own boundary cannot name. A leaf patches in place only where the
  same component instance emitted both sides of the match — the remount the position rule in
  `Documentation~/react-migration.md` already stated for hook state, now reaching the element as well.
  A component re-rendering itself keeps the element it emitted, unchanged. A navigation between two
  different route components is such a swap, since a matched route renders at the Outlet's own position.
  A container whose old side reached a component takes the same walk even where its new children are
  all host leaves, since the time-sliced diff carries no owner to compare: a `Transition`-lane pass
  over such a container runs to completion, where the time-sliced one could park at the frame budget.

  What a caller has to edit around, in two places. A `refCallback` on the arriving component is
  handed a freshly built element where it used to be handed the departing component's, so a reference
  taken on an earlier render is stale — read the element from the callback each time it fires rather
  than holding one across renders. What the element instance itself carries does not survive a freshly
  built one — focus, a `V.ScrollView`'s scroll offset, a `V.TextField`'s caret and selection among
  them — so state to keep is lifted above both components, into a `Store` or a `Hooks.UseState` in the
  component that declares the slot.

- An Outlet navigating to a location that matches no route at its depth no longer keeps the route it
  was holding. Every clearing path in the Outlet's own patch sat below an early return taken on a
  failed match, so the departed route's elements stayed in the tree, its Component's fiber was never
  disposed — its hook state and effect cleanups with it — and the scope built for that route was never
  released. Rendering nothing there is the same walk that drops a `null` child, so the route's elements
  leave and its fiber is swept as an orphan, and `RouteOutlet` releases the scope itself before it
  returns that empty render. A route change disposed all three before, and so did an Outlet unmounting
  and a whole-reconciler teardown; a location that stopped matching disposed none of them.

- A route whose path ends in a splat no longer accepts children. `ParseRouteSegments` refused a splat
  that was not last within one route path, but a branch is every ancestor's segments joined, and
  nothing re-read the position across that join: `Route("files/*", children: new[] { Route("download") })`
  built the branch `[files, *, download]`, which outscored its splat-only parent and won for every
  path the parent would have taken — the child's own segment never compared against anything, because
  a splat stops the walk where it matches. A splat is a leaf, as it is in React Router, and declaring
  children under one is now refused where the mid-path spelling already was.

- Every public signature that named a UniTask type now names `VelvetTask` / `VelvetTask<T>`, and they do
  not all name it in the same position. Returning one: `Router.NavigateAsync`, `Router.GoBack`,
  `Router.GoForward`, `MutationResult.MutateAsync` and the no-argument `MutationResultExtensions`
  shorthand beside it. Returning a delegate that returns one: `Hooks.UseNavigate`. Taking a delegate that
  returns one: the asynchronous `Hooks.UseBlocker` overloads, both `Hooks.Use` overloads,
  `V.Route(loader:)`, `RouteBlockerManager.Register`, each `MutationOptions` constructor, and
  `TransitionStarter.Invoke` — the async form of the starter `Hooks.UseTransition` hands back, which takes
  a `Func<VelvetTask>` and returns `void`, so it changes type without returning a task at all. Holding one:
  `RouteDefinition.Loader` and each `MutationOptions.MutationFn`. Velvet ships the awaitable
  itself under `Velvet`, so UniTask is no longer a package dependency and installing Velvet is a single
  package add. A caller's own loaders, blockers, mutation functions and transition actions change type with
  them: a declaration becomes `async VelvetTask<T>`, a value already to hand comes from
  `VelvetTask.FromResult`, and a source the caller completes by hand is a `VelvetTaskCompletionSource<T>`.
  Inside an `async VelvetTask` body a `UnityEngine.Awaitable` and a `System.Threading.Tasks.Task` are
  awaited as they stand, with no conversion. Reading a suspended task's result a second
  time throws `InvalidOperationException`, where UniTask returned reset state; a task completed inline —
  `VelvetTask.FromResult`, and an `async` method that returned without suspending — may be read more than
  once, as UniTask allowed. A suspended task also carries one awaiter, so any delegate Velvet invokes more
  than once — a route loader, a blocker predicate, a mutation function, a `Hooks.Use` factory — hands back
  a fresh one each time rather than a task it returned before, which throws on the second invocation instead
  of resolving again. The Back/Forward re-run of a suspend-mode loader is where a shared task meets that.
  The async guide states the type's surface and how its continuations are driven.

- Registering a portal target id again with a different element now moves the portals already mounted
  into the old one, instead of leaving them writing into an element the UI has replaced. A
  `"modal-root"` that a screen owns — torn down on navigation and re-registered from the rebuilt
  screen's `refCallback` — used to leave every live portal's children on the destroyed element, with
  no way back short of remounting the portal. `Register`'s overwrite warning fires only where the id
  is still registered, and Velvet unregisters no portal target of its own; neither the warning nor
  its absence reported the portals left behind on the element the id no longer named. The move is an
  unmount and a remount, the same as a changed `createPortal` container, so state, refs and effects
  under the portal do not survive it. Registering the same element again, and unregistering the id,
  still leave a live portal exactly where it is. A portal declared before its id existed follows the
  same signal: its children now appear on the first registration, where they used to wait for an
  unrelated re-render of the declaring component and stay invisible until one happened.

- `animate-pulse` now suspends the element's native transitions while it runs, on the same terms
  `V.Motion`'s own per-frame drivers already do. An element whose own utilities declare a transition
  covering opacity — including the bare `duration-*` that leaves UI Toolkit's initial `all` standing —
  previously left the pulse's per-frame writes to that transition. The suspension is element-wide
  while it lasts, so such an element's other transitions land instantly for the length of the pulse;
  it is handed back as soon as a re-render leaves nothing transitioning opacity, and an element that
  transitions nothing over opacity is left alone. A `V.Motion` variant swap on the same element owns
  the slot for its own length: the suspension stands aside for the swap, which therefore still tweens,
  and is back once the swap ends.

- A component keeps its own state when a sibling written as `cond ? node : null` stops rendering. An
  unkeyed component written inline among siblings was keyed by how many components of its identity the
  walk had passed rather than by the slot it sat in, so a sibling turning to `null` shifted the
  components of that identity after it onto the slot keys their predecessors held: the second instance
  re-bound onto the first one's fiber and rendered the first one's state under its own props, while the
  fiber it should have kept was disposed in its place. No remount marked the change — the component
  kept rendering and the state was simply the wrong instance's. The dropped sibling now unmounts and
  the ones after it keep their slots, which is what React does with the same tree.
  Three neighbouring shapes moved with it, each a case of that same count standing in for a position:
  two different components swapping places among siblings kept their own state where React remounts
  both; a fragment gaining a child handed the newcomer the following sibling's instance and remounted
  that sibling; and a component inside a `V.Suspense`'s children collided with the one at the same
  index of the body around it, which the duplicate-key guard answered by warning and dropping one of
  the two.

- A `TabIndex` or `DelegatesFocus` prop that a later render stopped declaring was written back as `0`
  or `false` rather than as the value the element was constructed with — neither of which is the
  right answer for every type. A `Label` is built out of the tab ring at -1, which shows once a
  consumer declares it focusable; a `TextField`, `Toggle` or `Slider` is built delegating focus to
  the input beneath it, so dropping that prop stranded focus on the field's own root. `Focusable` already restored its
  constructed value; all three do now.

- `RouteBlockerState.Proceed()` now runs the blocked navigation again, from the request the caller made,
  so the confirm-dialog flow it exists for reaches the destination: the user clicks "Leave" and the
  router goes there. It used to clear the state and invoke a callback nothing assigned, leaving the
  dialog closed and the navigation gone — reaching the destination meant copying the attempt's next path
  and navigation mode out before calling `Proceed()`, re-issuing them by hand, and arranging for the
  predicate to stop blocking, because a re-issued attempt was put to that predicate again. Code written
  around that is what makes this a break: the hand-rolled re-issue now runs on top of the one `Proceed()`
  performs. What is re-issued is the navigation the caller asked for, so a blocked Back or Forward
  resumes as the same history step, and one a Guard redirected takes the redirect again from that step
  rather than committing the redirect target over the entry the user was standing on.
  `RouteBlockerStatus` has a third member, `Proceeding`, for the span between `Proceed()` and the
  re-issued navigation settling: over it the Blocker still reports its destination and is consulted about
  nothing, and it returns to `Idle` — which is what arms it for the next navigation — once that
  navigation has committed, ended without committing or been abandoned, and no Blocker is left blocking.
  A second Blocker vetoing the re-issue is what leaves one, and the first waits on that one being
  answered.
  Disposing a registration stops its predicate immediately without stranding a Blocker already holding
  or releasing that attempt; a saved dialog's `Proceed()` still settles back to `Idle`. A disposed
  Blocker holding the re-issued attempt also no longer keeps the registered Blockers that already
  consented in `Proceeding`; they are armed for the next navigation while its saved handler stays
  usable. `Reset()` still abandons the attempt and leaves the router where it is, and now ends it for
  the Blockers holding it alongside: their dialogs close too, and a `Proceed()` on one of them no longer
  sends the router at a destination the user declined. An exhaustive `switch` expression over
  `RouteBlockerStatus` needs an arm for the new member. The navigation-blocking guide covers the whole
  flow, including what changes with more than one Blocker registered, where React Router supports a
  single one.

- When an element callback fails during a component's own update and the error boundary that catches it is above
  that component, the rest of the update's render no longer writes its later rows onto the rows the boundary's
  fallback left in their place. A sibling after the boundary showed the updating component's row instead of its
  own.

- A render an abort stopped before its removals ran no longer leaves an `AnimatePresence` believing a child it could not
  remove is gone: the next render takes that removal, so the child's element leaves and `onExitComplete` runs once.
  It used to leave the element on screen with no later render removing it.

- An error thrown while a boundary's catch releases what the failed output mounted, such as a store subscription's
  unsubscribe, goes to the boundaries above that boundary, or is logged where there is none, and the boundary's fallback
  shows. It used to be caught by the boundary itself on the aborting path, which released the element being mounted
  around it and left the fallback unshown.

- On an element that carries a `rounded-*` class or an arbitrary radius, the radius fit scales the radius a rule of your
  own gives a corner that outranks the class there, and scales a radius your code writes to `style` with the others,
  as CSS does. Both used to be left unscaled, and the class's radius replaced the rule's once it was fitted.

- A `UseDeferredValue` in a component that also had an update of its own queued — a store its parent reads
  too, or a state setter called alongside the parent's — committed its new value in the same pass as the
  parent's urgent render that passed the changed input down, so the deferral did nothing. That value now
  commits on the Transition tier, behind the urgent render, as one without an update of its own already did;
  the urgent render shows the previous value. A discrete event handler's synchronous flush no longer commits
  it either.

- A render given up because a component outside every Suspense suspended no longer leaves a Suspense it
  rendered recorded as showing its fallback while the screen still shows its children.
- A component written beside a `V.Suspense` in the component that renders it, rather than inside it, shows
  its resolved read once it resolves after the render it suspended was given up; it used to keep showing what
  it rendered before.
- A `V.Suspense` inside another one that the same component renders no longer makes the outer one show its
  fallback for a child only the inner one waits on.
- `gap-*` respaces a container's children at its next geometry change when one of them has become
  `display: none`, or stopped being so, with no child added or removed; it used to keep the spacing it had.

- An important skew (`!skew-x-6`, `skew-x-6!`) applies and outranks every plain skew on its axis; it was ignored,
  and a plain skew beside it was drawn instead.
- A skew bracket whose angle the browser rejects leaves the element unskewed when it is important (`!skew-x-[5px]`)
  or writes both axes (`skew-[10]`), as it already did for a plain `skew-x-[…]` or `skew-y-[…]`; it was ignored,
  and the other skew classes on the element were drawn.
- A bare `skew-x-*` or `skew-y-*` value with a leading zero (`skew-x-07`) is declined, as Tailwind generates no rule
  for it; it was read as that many degrees.

- A `V.Motion` hovered through a `hover:` rule of its className while its enter played kept the class of the
  pose it entered from once the pointer left, and showed that pose instead of the one it entered. A variant
  change played while a `V.Motion` was hovered let the new variant's class take the property from its
  `whileHoverClass` class.
- A `has-[.foo]:` ancestor did not match a `.foo` a play showed on a descendant whose variant payload, such as
  `first:foo`, also put `.foo` there.
- A variant payload such as `hover:foo` turning off could take `.foo` off the element while a play still showed it.

- A skewed, `shadow-*` or `border-dashed` element kept painting the background and border colours it had when
  its skew, shadow or dashed border attached after a variant, gesture or drag class, a theme change or a play
  changed them, until an unrelated re-render.

- A direct child of a skewed element showed either the skew's seat or its own translate — `translate-x-*`, a
  `V.Motion` translation, a drag offset — whichever was written last. The two compose, as CSS composes a
  parent's transform with the child's, and a translate play on such a child starts from its own translate
  rather than offset by the seat, as does a `layoutId` move.

- A clipping `layoutId` `V.Motion` drawn at a different scale on each axis could show its content past its rounded
  corners for a frame after code gave it an inline background image.

- A `z-*` change that keeps an element in the same layer (`z-10` to `z-30`) re-sorts it without detaching it
  from the panel, so it raises no `DetachFromPanelEvent`.

- Removing a named filter utility that shared a filter with a bracketed one, such as `blur-none` beside
  `blur-[2px]`, brings the bracketed value back instead of leaving no filter.

- `text-ellipsis` alone on a field elides the input's text while the field is unfocused, as it does on an
  `<input>`: the box and its text take `overflow-hidden` beside `text-ellipsis` and `text-clip`, as `truncate`
  already gave them.

- A `shadow-*` or `skew-*` beside a filter utility on a field, a variant one included, reserves room for its
  paint under the filter, as it does on any other element.

- A `shadow-*` on a `skew-y-*` element follows the y shear as well as the x one, composed as Tailwind's
  `skewX() skewY()` transform; it sheared on x alone. Its offset shears with the element, as CSS transforms a
  shadow, and a steep skew no longer cuts the shadow off at the edge of the area it is painted into.
- A shadow layer on an element under 8px tall or wide, or a layer larger than 2048px, is drawn at its real
  size; its quad was clamped to between 8px and 2048px on each side, so the layer no longer matched the rect
  it was baked for.

- `!whitespace-pre-line` and `whitespace-pre-line!` collapse spaces like `whitespace-pre-line`; the
  important modifier used to leave the class unread.

- A drag pressed on a descendant of the draggable releases that descendant's `whileTap` class and
  `active:` variants when it ends. They stayed applied, since the session swallows the release before
  the descendant sees it and only the draggable and its ancestors were settled.

- A fiber registered with `VelvetDevToolsRegistry` leaves the registry when it is disposed. An interior
  fiber registered by hand used to stay for the session as a greyed-out row until it was unregistered by
  hand. A disposed fiber is not registered, and registering a registered fiber again relabels its entry and
  keeps its registration time rather than replacing the entry.

- A text item carrying `min-w-[auto]` or `min-h-[auto]` now keeps the automatic minimum size along its parent's
  main axis, as it does with no minimum class. The `[auto]` form stood the floor down, so a `Label` or `Button`
  in a row could shrink below its widest word. Code that set `min-w-[auto]` to let such an item shrink must
  write `min-w-0` instead.
- `min-w-auto` and `min-h-auto` are now bundled utilities, and a variant that lights a size or overflow class
  (`md:min-w-0`, `md:overflow-hidden`) re-derives a text item's automatic minimum at once instead of at its next patch.

- A `rotate` or `scale` that a stylesheet of your own declares for a skewed element is composed into the skew, as the
  individual `rotate` and `scale` properties compose with `transform`, and is composed again when a restyle moves it.
  It used to be dropped: the element was drawn with its skew alone. An application whose skewed elements carry such a
  rule now sees them rotated or scaled. A `rotate` about an axis other than z is declined, and the element keeps its
  skew and the classes' rotate.
- A skewed element carries a `velvet-skew-observed` class while its skew is composed, which the bundled stylesheet gives
  the custom property that tells Velvet a restyle happened.

- A border-width utility (`border`, `border-2`, `border-x-4`, `border-s-2`, `border-[3px]`, under any variant) now draws
  in the element's text colour, as Tailwind v4's preflight `border-color: currentcolor` does, and follows the text colour
  as it changes. So do the children of a `divide-*` parent (dashed dividers included), the target of a child-scoped width
  (`*:border`, a field's input box), and an element a USS rule of a stylesheet that is not a control theme's gives a border width when the rule selects it by a
  class, its name, its type or `*`. It gives way, side by side, to any rule of a stylesheet that is not a control theme's that sets
  that side's border colour (a `border-*` colour utility or variant, a USS rule of any selector, `:hover` and
  `border-color: transparent` included) and to a colour your code writes inline, whatever its value, the text colour
  included. A control theme's border does not hold it back, as preflight outranks a browser's default styles.
  A width written inline by code gives no preflight colour to an element no rule selects. A stylesheet added to an
  ancestor of an element that has attached is noticed within a frame, and the elements under it are asked again.
- Bundled border widths (`border`, `border-2`, `border-x`, `border-t-4`, …) are snapped to device pixels at a panel
  scale that is not a whole number, as a browser snaps them, and again when the scale changes: `border` is two thirds of
  a point at 1.5 device pixels a point. A stylesheet placed directly after the bundled utilities on the root they are
  attached to (`VelvetStyleUtilities.AttachTo`, a portal host, or a root a project attached them to itself, found from
  its first element with a border-width utility) carries the snapped rules, so a project that reads the root's style
  sheet list sees one more entry there.

- An `lh` inside a `leading-[…]` is the parent's line height, as CSS takes it: `leading-[2lh]` under a 16px
  parent is 48px, where it was twice the font's own line height.
- `brightness-*`, `saturate-*` and `hue-rotate-*` filter a translucent pixel's colour divided by its alpha, as CSS
  filters a colour that is not premultiplied. They filtered the colour multiplied by the alpha, so the next filter
  read a value CSS clamps (`brightness-150 saturate-0` over half-transparent green gave a gray of 128 over black
  where CSS gives 91), and in a Linear project the colour space conversion darkened a translucent colour.

- Spring and Bezier target selection follows important declarations per longhand. Important shorthand and longhand targets match their applied values without depending on a class projection, while independent translate axes retain their own targets.

- A component that the scheduled resume of a parked time-sliced render reaches reads the `V.Provider` values
  above the component whose render parked, as the components the render reached before parking do, where no
  `V.Portal` and no `V.VirtualList` item encloses the parked component. A `Hooks.UseContext` call there returned
  the context's default value instead.

- A time-sliced render parked inside the children of a `V.Suspense` that has hidden them to show its fallback
  is not continued on its own while they are hidden; the next render that reaches it continues it under the
  `V.Provider` values above it. Continued on its own while they were hidden, it rendered components that read a
  context's default value in place of a `V.Provider` above them, and where the reveal did not render the parked
  component again, they kept that value.

- An error a component throws when the scheduled resume of a parked time-sliced render reaches it, or when an
  update of the parked component itself completes the parked render first, and that an error boundary above the
  parked component catches, is reported once, by that boundary. A `NullReferenceException` was logged beside
  it; after the scheduled resume, the boundary's own report also waited for a later commit.

- An error boundary's fallback reads the `V.Provider` values above the boundary when the error it catches is
  thrown by a `Hooks.UseEffect`, or by a render that did not start at the boundary or above it, such as an
  update of a component below the boundary, a time-sliced render resuming below it, or the first render of a
  `V.Portal`'s child, where no Portal and no `V.VirtualList` item encloses the boundary. For an effect's error it
  read each context's default value. For a render's, it read the values where the error was thrown: a Provider
  below the boundary, one the boundary renders included, took precedence; under the scheduled resume a Provider
  above the boundary read as its context's default; and for a Portal's child, a Provider that another boundary's
  fallback had just placed above the boundary gave way to one of the same context above that other boundary.

- A component that a `V.Portal` mounts reads, in that first render, the `V.Provider` value around the Portal
  rather than one of the same context farther up, whether the Portal mounts with the component rendering it or
  when that component updates, where no other Portal and no `V.VirtualList` item encloses the Portal. It read the
  farther Provider's value on an update, and at the mount where a component above rendered it.

- A component in a row of a keyed list whose time-sliced render parked reads the `V.Provider` value its own row
  last committed when it renders again on its own before the render reaches that row. It read the value the
  parked render gave the Provider of the first row holding a component at the same position.

- The compiler no longer caches the value a local is given where the only cached scope reading that local sits
  inside the computation of the value, which reads what the local held before. Such a value took a cache slot
  and a key of its own on every render with nothing reading what it stored.

- A rounded layoutId `V.Motion` that does not clip its content and whose background image is set inline keeps
  painting that image while its move is drawn scaled differently on each axis, and has it back once the move
  lands. A texture or sprite image used to be painted for one frame of the move, show none for the rest of it, and
  be lost for good when the move landed. An inline `RenderTexture` image, on a Motion that clips its content or
  not, is now painted for the whole move and handed back too, where it used to be painted on no frame of the move
  and lost when it landed.

- A bracket whose value opens on a parenthesis with no function name before it, such as `brightness-[(2)]`,
  `hue-rotate-[(2deg)]` or `translate-x-[(10px)]`, is rejected as the browser rejects what Tailwind writes for it,
  so the element draws none of that family's utilities; it used to be taken as a function that might come to
  anything. A negated one, such as `-hue-rotate-[(2deg)]`, is still taken, since Tailwind writes it inside
  `calc()`. A gradient stop position written that way, such as `from-[length:(10px)]`, puts the stop at its
  initial position, as another position the browser rejects does.
- An element with a border-width utility and a colour transition, such as `border transition-colors`, shows its
  text colour on every side as soon as it mounts, where every side after the top used to fade in from
  transparent. A border colour code writes inline and clears again transitions back to the text colour, where it
  used to fade to transparent first.

- A transition UI Toolkit runs on a function the element's classes give it now runs on that function from its first
  frame: Tailwind's curve for an `ease-in`, `ease-out` or `ease-in-out` class or the `ease-in-out` a `transition-*`
  utility bundles, or the function an `ease-[…]` class or a theme's `ease-<name>` names. The first frame was eased by
  UI Toolkit's own curve for the keyword and the function took over from the second: 0.15 s into a 0.3 s
  `transition-all`, a blur stood half way where Tailwind's curve puts it at about 78%, and a `steps(…, jump-start)`
  had not yet taken its first step.
- A transition already running on an element keeps the curve it started on when another transition starts there, as
  a CSS transition keeps its timing function. Velvet moved every transition still running on UI Toolkit's own curve
  onto the function the element's classes gave by then, mid-run: an opacity change running on `ease-linear` turned
  onto Tailwind's ease-out part way through when an `ease-out` class arrived with a change of another property.

- A radius a `V.Div`'s, `V.Label`'s or `V.Button`'s own `GeometryChangedBinding` writes when the element's size changes
  is fitted on that change. On a label or button reused from the pool after an earlier mount as one, and on any of the
  three given the binding on a later render, it kept its unfitted value until the size next changed.

- A child whose `shadow-inherit` reads a colour its parent mixes with an opacity modifier outside 0..100%
  (`shadow-lg/150 shadow-red-500`) draws no box-shadow, rings included. Tailwind mixes the colour through
  `--tw-shadow-alpha`, so the browser finds it invalid, the child inherits that invalid value, and its box-shadow
  computes to none; it used to draw the parent's colour at full strength. The same applies to
  `inset-shadow-*`.
- `shadow-initial` leaves each layer its own colour where it outranks the shadow colour class beside it, as it
  does when important (`!shadow-initial`) or when Tailwind emits it after that class (`shadow-black`); it used to
  be ignored.
- `shadow-lg/150 shadow-inherit` draws its shadow. `shadow-inherit` reads no `--tw-shadow-alpha`, so the browser
  keeps the box-shadow; it used to be discarded as an invalid colour would discard it.

- The skew pass no longer cuts the sign off a class that opens with `-` and is not a skew utility, such as `-mt-2`
  or `-translate-x-1/2`. It read each class of an element on every patch and allocated a string for each such class
  before checking for `skew-`.

- A `backdrop-*` change, a dashed or dotted divider's colour, and a background or border colour repainted under a
  `shadow-*`, a `border-dashed` or `border-dotted`, or a layoutId Motion's per-axis corners now ease along the
  function their transition's class gives, as the transition UI Toolkit runs for the same class does: Tailwind's curve
  for `ease-in`, `ease-out` and `ease-in-out` (`transition-all` and `transition-colors` give `ease-in-out`), and the
  function an `ease-[…]` class or a theme's `ease-<name>` names. Velvet runs each of these transitions itself and eased
  them by UI Toolkit's own curve for the keyword the element resolves instead: half way through a
  `transition-colors`, a dashed divider's colour stood half way where Tailwind's curve stands at about 78%. A change
  back part way through is shortened by the curve it ran on.
- An element whose classes give its transitions `ease-in`, `ease-out` or `ease-in-out`, and whose rules give them more
  than one duration, delay or timing function, keeps Tailwind's curve while a Motion narrows its transitions around
  the properties it drives. The timing lists the narrowing writes inline were read as the element's own, which left
  those transitions on UI Toolkit's curve for the keyword.
- A Motion's tween on an element carrying an `ease-[…]` class, or a theme's `ease-<name>`, now runs on the Motion's own
  `Easing` where that is the keyword nearest the class's function. The class's function replaced it.
- A `transition-filter` tween whose `ease-[…]` class runs `steps(…, jump-start)` or `steps(…, jump-both)` holds its
  start through a positive `transition-delay`, as CSS does; it took its first step as the delay began.
- A `transition-filter` tween whose filter is changed back to the one it shows, as when a hover ends inside the
  delay, now stops there. It ran on to the filter it had been heading for and stayed on it.
- A sortable item whose transition lists are written inline, by an `ease-[…]` class or by a Motion narrowing its
  transitions, gets them back when a drag that shifted it ends, and a narrowing that ends afterwards hands back the
  item's own. The drag's `translate` and `scale` over 200 ms on an ease stayed in their place.

- A shadow size's `/N` keeps scaling the colour class beside it when another size without one holds the layers:
  `shadow-lg/50 shadow-xl shadow-red-500` draws `shadow-xl` in red at 50%, and `shadow-lg/150 shadow-xl
  shadow-red-500` draws no box-shadow, as the browser does with Tailwind's `--tw-shadow-alpha`. A later size used
  to reset the scale to 100%. The same applies to `inset-shadow-*`.
- A shadow size without a `/N` takes the layers from a plain size with one, whichever is written first, as
  Tailwind emits the size with the `/N` first: `shadow-lg/150 shadow-lg` draws `shadow-lg`'s layers at their own
  10%, and so does `hover:shadow-lg hover:shadow-lg/150` while hovered. The size with the `/N` used to win
  wherever its name sorted after the other's. The same applies to `inset-shadow-*`.
- A bracketed shadow, or a bracketed shadow colour beside a size, with a bracketed opacity modifier
  (`shadow-[0_0_5px_red]/[50%]`, `shadow-lg shadow-[#f00]/[50%]`) is drawn at that alpha. It used to discard the
  whole box-shadow, a ring beside it included. The same applies to `inset-shadow-*`.
- A bracketed ring, inset ring, ring offset or outline colour with a bracketed opacity modifier
  (`ring-[#f00]/[50%]`) is drawn at that alpha. It used to discard the whole box-shadow, or for an outline to be
  ignored. A bracketed ring width with a modifier (`ring-[2px]/[50%]`), which Tailwind writes no rule for, no
  longer discards it either.
- A `-none` with an opacity modifier (`shadow-none/50`), which Tailwind writes no rule for, no longer stops the
  size before it from reading a rejected colour: `shadow-lg shadow-none/50 shadow-[color:0_4px_8px]` draws no
  box-shadow, as the browser does, where it used to draw `shadow-lg`. The same applies to `inset-shadow-*`.
- `shadow-inherit` or `shadow-initial` with an opacity modifier (`shadow-inherit/50`), which Tailwind writes no
  rule for, no longer replaces a rejected shadow colour beside it: `shadow-lg shadow-inherit/50
  shadow-[color:0_4px_8px]` draws no box-shadow, as the browser does, where it used to draw `shadow-lg`. The same
  applies to `inset-shadow-inherit` and `inset-shadow-initial`.
- A rejected shadow colour class discards the box-shadow beside a bracketed shadow layer where Tailwind 4.3.3 writes
  the colour variable into that layer. A layer whose offsets are `calc()`, `clamp()`, `min()` or `max()` takes it:
  `ring-2 shadow-[calc(1px)_calc(2px)] shadow-[color:0_4px_8px]` draws no box-shadow, a ring included, where it used
  to draw the ring. A layer with two lengths and two further parts that are no keyword, length or colour does not:
  `ring-2 shadow-[0_0_hypot(1px)_hypot(2px)] shadow-[#ggg]` draws the ring and the shadow, where it used to draw
  neither. A layer that also holds a `var()` (`calc(1px)_calc(2px)_var(--c)`) is read as before.
- The bare `shadow` with an opacity modifier (`shadow/50`) draws its layers at that alpha. It used to draw
  nothing.
- The outer box-shadow of an element that clips its content (`overflow-hidden`) takes `shadow-current` and `em`
  lengths from the element itself. It used to take them from its parent.

- `VelvetFonts.DefaultFamily` reaches a label with no font class, as the `font-family` Tailwind's preflight gives
  `html` reaches a `<p>` with no class: Velvet writes the family inline on the element a tree is mounted on, on each
  element a portal renders into its target rather than on the target, whose other children keep their font, and on
  the root of a layer or world-space panel, and what is under them inherits it. One of them inside another inherits
  too. It used to reach an element with a weight, italic or width class and what was under it, so a label with no
  font class elsewhere kept whatever font its stylesheets and ancestors gave it. A family an element names still
  decides for what is under it, and where a font an author's stylesheet rule or inline style gives one of those
  elements, or an element above it, is what would show there without the default family, it is inherited instead. A
  change to the registry reaches the written elements with no render.

- Patching every row of an unkeyed list checks that each row's slot is still occupied by walking on from the slot
  it checked before, where nothing moved that one, rather than from the container's first child. That walk asked
  every child it passed whether it was one of the children Velvet
  hosts that hold no slot, so the cost grew with the square of the row count and with each kind of those children
  Velvet added. In the editor, reordering a keyed list counted all of the container's children twice for each row
  it kept from the first one out of place, and now checks that row's slot from the one checked before it.
- A class change whose families of classes are already in the order Tailwind emits them no longer sorts the
  element's classes, or copies its class list once for each family, to find that out.

- A `VelvetPreviewPanel` built from `PanelSettings` that clear their own panel's colour no longer clears over a
  `V.Portal(UILayer.Background)` drawn into its texture, so the texture is cleared to transparent below everything
  drawn into it, as the class describes. The panel also takes its texture's size and scale when it is built rather
  than on its first update.

- A USS file holding a custom property with one of these, each of which UI Toolkit's importer fails the file on, is
  imported, the value stored as the text it was written with, as one holding a `rem` is: a number with an exponent
  (`1e3px`, `1e3`), one a `-`, a `_` or a non-ASCII character directly follows (`1-2`, `1_a`, `1-a`), most
  delimiters, such as the `/` of Tailwind's `--aspect-video: 16 / 9` or a full stop no digit follows after a fraction
  (`.5.p`), a hex colour of other than three, four, six or eight digits (`#zzz`, `#12345`), an `rgb()` of other
  than three plain numbers or an `rgba()` of fewer than four, a percentage or a range such as `u+1` counting as none
  (`rgb(100%, 0%, 0%)`, `rgba(0, 0, 0)`, `rgb(u+1 2 3)`), a function with nothing between its parentheses other than
  `none()`, `filter()` and the URL functions `url()`, `url-prefix()` and `domain()` (`blur()`, `var()`), a `var()`
  whose arguments the importer refuses (`var(--a,)`, which Tailwind v4 writes, `var(--a, 1px, 2px)`, `var(a)`,
  `var(--a b)`), a comment among the arguments of a function such as `blur()`, `var()` or `linear-gradient()`
  (`blur(/* x */ 4px)`, `var(--a /* c */)`), a range (`u+1`, `U+12`), and a URL function whose path the importer's
  lexer cannot read (`url(a b)`, `url(a /* c */)`, `url("a" /* c */)`, stored as `url("a"  )`). The importer used to
  leave the whole file empty.
- A custom property stored as the text it was written with keeps a `/*` inside the unquoted path of a `url()`, which
  CSS reads as part of the path (`url(a/*c*/) 1rem`), where it used to be taken out as a comment. A `/*` or a quote in
  the unquoted path of a `url()`, in any declaration, used to leave the custom properties after it as written up to
  the next `*/` or quote of its kind, a `rem` among them failing the file (`url(icons/*.svg)` or
  `url(images/don't-panic.png)`, then `--gap: 1rem`), and with no `*/` after it the custom property holding a `/*` as
  well (`url(icons/*.svg) 1rem`). Such a path now ends at its first `)` no backslash escapes, as CSS ends it.
- A custom property stored as text whose value holds a form feed no longer fails the file. The form feed used to be
  written inside the quotes as it was, which UI Toolkit's importer refuses.
- `Hooks.UseQueries` and `Hooks.UseSuspenseQueries` run `combine` again once a commit changes only a query's
  `Select`, as TanStack Query v5 does. The value from before the change used to stay until another change ran it
  again.
- A custom property whose `!important` is written in another case, which UI Toolkit's importer fails the file on,
  has it written in lower case.
- A custom property in a USS file calling a function UI Toolkit's importer does not know, such as `mysteps(3)` or
  `rgb()` written in capitals, is stored as the text it was written with, as one calling `cubic-bezier()` is. The
  importer used to drop the function with a warning.

- A time-sliced update of a keyed list (a transition, for example) no longer leaves an extra, empty element among
  the list's children when an `absolute` row stacked by `hover:z-*` stops being hovered while the update is paused
  after reaching that row.

- The end of a pass no longer looks through every child of each parent whose children it reconciled for one that
  carries an `absolute`, `relative`, `z-*` or `order-*` class (behind a variant too), is a `V.Anchored` element or
  is a pinned `AnimatePresenceMode.PopLayout` exit. It looks only under a parent such a child has stood in since a
  look there last found none. A time-sliced render looked after every slice, which added a cost growing with the
  square of the list's length.
- An element an `AnimatePresenceMode.PopLayout` exit pinned out of flow gives the pin up when it is removed, so a
  pooled `V.Label` or `V.Button` no longer brings one into its next mount.
- An element wrapped for a `clip-path-*` class or by a `wrapElement`, which a time-sliced pass created and a fresh
  pass discarded before placing it, no longer stays in the reconciler's stacking tables where its stacking classes
  left it in its slot, such as `relative`, `absolute`, `z-0`, `z-auto`, `order-0` or `order-none` with no non-zero z
  or order beside them, or a `z-*` or `order-*` reading a custom property that had not resolved.

- A gradient's `from-`, `via-` or `to-` palette colour reads as its own mount's sheets give it. The reading of the
  first mount to render the class was handed to later ones, so a mount whose sheets set `--color-*: initial` could
  take the palette colour, or another mount lose it. A gradient re-measured after its font or the panel changed now
  reads its stops under its mount's sheets too.

- A `V.VirtualList` row that stacks by a `z-*` class is now placed in front of or behind the other rows as its z
  says, in each of these cases where it was not. A row a render gave the class went on painting in its own slot, and
  the reconciler kept holding it after it left the list; a row created with the class disappeared at the list's next
  render of its range; and a row a scroll brought into view was not placed by that scroll. Such a row also takes the
  list's item length, which went to the element standing in its slot.
- A `V.VirtualList` row that is a `V.Portal` now mounts its children in the target before the scroll that brings it
  into view returns, in the order a render mounts them, a Component row beside it in that range included.
- A `V.VirtualList` row that a scroll mounts inside a `V.Provider` of its own now reads that Provider's value. Where a
  Provider of the same context enclosed the component rendering the list, the row read that one's value instead.
- A `V.Motion` holding a `layoutId` now runs a change of its own translate during its move, and after the move while
  that translate still runs, on the curve the same change runs by on an element holding no id where a class gives
  that one Tailwind's curve, as `ease-out` and `ease-in-out` do. It ran UI Toolkit's own curve for the keyword
  instead, and on a one-second `ease-out` fell some 5px behind over a 40px translate.

- A top-level element of a `V.Portal`'s children that Velvet relocates to paint it in CSS's stacking order, one
  with a `z-*` class among them, is hidden and has its ref detached with the rest of the portal's children when a
  `V.Suspense` hides them. It used to stay displayed, with its ref set, under the fallback.

- `Hooks.UseQueries` with a `combine` now re-renders with what the combine makes of a result that changed with
  no event from its entry, such as data turning stale as time passes, once the render that read it commits, as
  TanStack Query v5's `setQueries` does. It used to keep returning the earlier combined value until the
  component rendered again for another reason. Without a `combine`, such a commit re-renders the component as
  v5's does, and so does a commit that changes which queries the hook holds — a key that changed, a query
  added or removed — whether or not any result changed.

- A render that changes an element's `bg-[…]` or `border-[…]` colour and removes its box shadow no longer puts the
  colour it had before back over the new one. The same holds for a render that changes a `border-[…]` colour and
  removes a dashed or dotted border.
- A colour written through a ref over the background or border of an element with a box shadow, or over the border
  of an element with a dashed or dotted border, now stays when a later render removes the shadow or the border
  style. It used to revert to the colour from before the write.
- An inline `border-[…]` colour on an element with a box shadow stays the element's own after a variant such as
  `dark:bg-[…]` writes its background inline. A border colour from a class rule no longer paints in its place, and the colour
  is no longer dropped when the shadow is removed.
- A dashed or dotted border whose `border-[…]` colour was removed by the render that added a box shadow no longer
  takes that colour back when a later render removes the shadow.

## [2.1.4] - 2026-08-30

### Highlights

- A `V.NavLink` whose `to` is relative navigated correctly and never lit up. The click resolved the
  target through the router while the active comparison used the string as written, so `to: ".."`
  was measured against a path that starts with `/` and never matched. Both sides resolve the same
  way now.

- A pending indicator could stay on screen after its transition finished, or never come up at all.
  `isPending` answered for the lanes of the component that called `startTransition` rather than for
  the work the callback did, so a transition whose writes all landed elsewhere read as settled the
  moment the callback returned, and two concurrent transitions credited each other.

- `startTransition(() => setItemsFromParent(heavyList))` — the shape a child uses to defer an
  expensive update through a setter it was handed — did not defer. The update was classified against
  the component that owns the state, which is in no transition, so it took the Normal lane, or the
  Urgent one inside a click. `Store` writes made in the callback behaved the same way.

- An update that merely happened to land while an async action was awaiting — a timer tick, a
  `UseStore` notification, a `UseMutation` callback — was taken for the action's own continuation and
  given the Transition lane, where it waited out the delayed tier's 100 ms instead of committing at
  the next frame boundary. Nothing is inferred from in-flight fiber state any more.

- The async action the migration guide asks callers to write — one that wraps its post-`await` update
  in the starter again — did nothing when the component that started it had gone away. The starter
  short-circuited before opening a scope, so every update it made, on components still alive, took
  the Normal lane. React sets its ambient flag with no fiber in hand and this now does too.

- **Breaking:** `SearchParams.Empty` hands back a fresh instance on every read instead of one shared
  for the process. It was a `static readonly` field, and `Append` mutates in place, so the shape the
  member invites wrote into the instance every other reader saw. Reading it is spelled the same and
  still yields an empty `SearchParams`; what two reads no longer share is the object. A reference
  comparison against `SearchParams.Empty` therefore no longer recognises an instance from an earlier
  read, and binding a readonly reference to it — an explicit `in` argument, a `ref readonly` local or
  return — stops compiling. No Velvet signature declares a `SearchParams` parameter, so reaching the
  last of those takes a declaration of your own over the concrete type.

### Fixed

- `startTransition(() => setItemsFromParent(heavyList))` — a child deferring an expensive update
  through a setter it received as a prop — had that update classified against the component that
  *owns* the state, which was in no transition, so it took the Normal lane, or the Urgent lane
  inside a click. The same held for a `Store` write made inside the callback.
- A starter whose declaring component had gone away short-circuited before opening any scope, so
  every update its callback made — on components still alive — took the Normal lane. React has no
  such short-circuit; it sets its ambient flag with no fiber in hand. The scope now opens on that
  path too and only the bookkeeping is skipped. That is the shape the migration guide asks callers
  to write: an `async` action wrapping its post-`await` update in the starter again, having outlived
  the component that started it.
- `isPending` answered for the calling component's lanes rather than for the callback's work, so a
  transition whose writes all landed elsewhere settled the moment its callback returned; an `async`
  action's completion cleared the flag and scheduled nothing, stranding the indicator on screen; a
  synchronous callback that drove a flush of its own ran the transition with the indicator down; and
  two concurrent transitions credited each other's writes.
- An unrelated update landing while an async action awaited — a timer tick, a `UseStore`
  notification, a `UseMutation` callback — was indistinguishable from the action's own continuation
  and got the Transition lane, so it waited out the delayed tier's 100 ms instead of committing at
  the next frame boundary. Nothing is inferred from in-flight fiber state now.

React sets its transition flag before the callback and restores it in a `finally`, with no `await`
in between — so for an async scope the restore runs the moment the callback hands back its promise,
and nothing re-establishes it for the continuation. Its lane decision reads that ambient flag rather
than the component the update belongs to, apart from one legacy branch its current default flag set
turns off. react.dev states the same contract from the outside and files the post-`await` case as a
known limitation it means to fix.


- `SearchParams.Empty` hands back a new instance on each read. It was a `static readonly` field
  holding one instance for the whole process, and `SearchParams.Append` mutates the instance it is
  called on, so the shape the member invites — `var next = SearchParams.Empty; next.Append("q",
  term);` — wrote into that one instance, and a read of `SearchParams.Empty` anywhere else in the
  process carried `q` from then on. `SearchParams` declares no member that removes a value, so the
  written-into instance stayed that way. Reading the member is spelled the same and still yields an
  empty `SearchParams`; what two reads no longer share is the object. A reference comparison against
  `SearchParams.Empty` therefore no longer recognises an instance taken from an earlier read, and
  binding a readonly reference to the member — an argument written with an explicit `in`, a
  `ref readonly` local, a `ref readonly` return — stops compiling. Reaching that last one takes a
  declaration over the concrete `SearchParams` type, and no Velvet signature declares a
  `SearchParams` parameter.

- `V.NavLink` with a relative `to` takes its active class. The click path resolved the target through
  the router — `..` against the enclosing route, a bare segment appended to it — while the active
  comparison used the string as written, so `to: ".."` was compared against a current path beginning
  with `/` and did not match: the link navigated to the right place and stayed unhighlighted. The
  target is now resolved through the same `Router` call the click makes, anchored at the caller's
  Outlet depth, before it is normalised and compared. An absolute target is returned by that call
  unchanged, so `end`, `caseSensitive`, a trailing slash and a query answer as before; with no router
  mounted to resolve through, the target is compared as written, as it was.
  One shape that was active is active elsewhere now: a query-only `to` such as `"?tab=1"` normalised
  to `/`, so with `end` left false it lit up wherever the user was. It now resolves against the
  current route and the comparison strips the query from what comes back, so it is active on that
  route's path and under it — what an absolute `to` naming that path gives. Where clicking it takes
  the user is unchanged.

## [2.1.3] - 2026-08-27

### Highlights

- Hiding a `V.AnimatePresence` and rendering it again no longer brings back the children that had
  already left. The record a presence keeps outlived the node that owned it, so the second render
  started from a set naming elements the tree had let go — they spliced back in as ghosts already
  exiting, and the enter animation `initial: false` exists to suppress ran anyway. The record also
  outlived the element it expanded into, one per removal, and a pooled parent rented back at the
  same position adopted the one left behind.

### Fixed

- A `V.AnimatePresence` that stops being rendered takes its bookkeeping with it. That bookkeeping is
  keyed by three terms — the fiber the expansion ran under, the parent element its children expand
  into, and the presence's position — and only that fiber being unregistered or the whole tree being
  unmounted retired it. So a `cond ? V.AnimatePresence(…) : null` flipping to `null` left an entry
  holding the committed children, and per key the exit anchor and the Motion element it had
  recorded, well after the removal had taken those elements out of the tree: measured on a presence
  under a `V.Button`, the entry left behind named both the departed child's element and the Button,
  which had already gone back to the element pool. Rendering the presence again at that position
  then started from that stale set — children that had left were spliced back into the DOM as
  exiting ghosts, and `initial: false` no longer suppressed the enter, since the second mount was
  not taken for a first render. The entry also survived its parent element being torn down, one left
  behind per removal, and a poolable parent rented back for a fresh presence at the same position
  picked the stale entry up again.
  A presence written directly in a `V.Portal`'s own children — so its children expand straight into
  the registered target element — is outside what this release answers for. Closing the portal
  empties the target and leaves the entry behind, and the fiber term the first reopen records is not
  the term the mount recorded, so it starts a second entry rather than finding the mount's, which
  stays behind unchanged; that reopen shows only the new child. The second reopen brings a child
  whose key changed between opens back beside the new one, while one stable child key was measured
  holding exactly one child after each of three reopens. Leaving the portal open and hiding only the
  presence reads differently again: the departed child stays in the target straight away, and is
  beside the new one on the next show. Placing the presence under an element inside the portal's
  children covers the close, not that hide, which leaves the departed child under the wrapper the
  same way. `Documentation~/motion.md` owns the placement advice.

## [2.1.2] - 2026-08-23

### Highlights

- A `Hooks.Use` resource key rebuilt on each render — a run-time string, or an id boxed on its way to
  the parameter — named a resource nothing had loaded yet, so the fetch was torn down and started
  again every render. Under a `V.Suspense` the boundary never came out of its fallback: the render a
  landing loader asked for restarted that loader and suspended on it again.

- A `Router.OnLocationChanged` subscriber that threw while a Suspend-mode loader was resolving had
  its exception recorded as that route's load failure. The route reported an error no loader had
  raised, and filing it as a failure ran the router's failure handler, which rewrites the history
  entry's snapshot from a round the same mistake had miscounted — so a Back to that entry re-ran the
  loader instead of being served from the cache.

### Fixed

- `Hooks.Use(factory, resourceKey)` compares the resource key by value, so a key a render builds names
  the resource the previous render named instead of starting a new one. The comparison closed over the
  parameter's own `object` type and took the reference branch for every key whatever it held, so a
  string built at run time — the query id the parameter's documentation recommends — and an id boxed at
  the call boundary each tore the resource down and restarted it on every render. Under a `V.Suspense`
  that cost the boundary its resolve, since the render a landing loader asks for restarted the loader
  and suspended again. Nothing said so either: the warning about a resource restarting every render is
  gated on the key being omitted, and an omitted key is the factory delegate, which a delegate's own
  identity comparison covers either way. A key of any other reference type is still compared by
  instance, so that omitted-key behaviour is unchanged.
- A subscriber that throws while a `LoaderMode.Suspend` loader is resolving no longer corrupts the
  round that loader belonged to. The router answers a resolution by re-emitting the location, so a
  `Router.OnLocationChanged` subscriber can run behind that announcement — and a throw from one was
  caught by the clauses that exist for the loader itself. That counted the round's outstanding loader
  off a second time and recorded the throw as that route's loader error. So a caller reading the route's
  loader error saw a load failure that had not happened. Filing it as a failure also ran the router's failure
  handler, which writes the history entry's snapshot again — this time from a round the second count
  had corrupted — so the entry stayed unsettled and a Back or Forward to it re-ran the loader instead
  of being served from the cache. What that second count costs the round itself depends on how many
  loaders it holds: one leaves it permanently short of settling, and two leave it reporting itself
  settled while the second loader is still outstanding. The throw is now reported as the subscriber's own and the
  round stands as the loader left it, with its result recorded and no error. A subscriber throwing out
  of the failure announcement is reported the same way, where it used to be left to whatever observes a
  forgotten task.

## [2.1.1] - 2026-08-21

### Highlights

- A widget taken from the pool arrived carrying what the last consumer had written on it. The reset
  helper covered a fraction of the writable surface of `Button`, `Label`, `Toggle`, `Slider` and
  `TextField`, so a recycled one could show a placeholder nobody had written, a `Toggle` stuck on a
  mixed value, a `Slider` running backwards, or a paint delegate belonging to a component that had
  already unmounted — and could stop delegating focus, or start taking pointer picks on its own root.
  The whole surface is scrubbed now, and a `Slider` still holding its numeric input field is kept out
  of the pool entirely.

- Two `V.Suspense` boundaries could not tell each other apart, and a fallback vanished either way. One
  nested inside another's children lost its fallback when the outer boundary resolved; two in a single
  render shared one suspended mark, so a second boundary rendering its children cleared the mark the
  first one's fallback was standing on. Both let a state update inside hidden children stop being
  deferred and commit over the slot range the fallback occupied. Each boundary now keeps its own
  answer.

- A child moved from one `gap-*`, `divide-*` or `grid-cols-*` container to another lost the spacing,
  divider or column sizing the container it joined had just written. Each of the three tracked the
  children it had written to by raw reference, and the element pool hands a child straight from one
  container to the next, so the container a child had left could still reset a write that was no
  longer its own. Only the container whose write is still on the child may take it back off now.

- `UseMutation`'s `Reset` cleared what the call had written so far and left the call itself in flight,
  still owning the handle — so when it landed it wrote `Success` and its result back over the idle
  state the reset had asked for. `Reset` now abandons the call. A handle also no longer shows `Data`
  for a call it reports as failed.

- A second `Mutate` call no longer cancels the first or drops its callbacks. Both run and each
  delivers its own `OnSuccess` / `OnError`. A double-tapped Buy used to abort a request the server may
  already have committed, and then skip the handler that would have recorded it.

### Fixed

- A child that moves from one `gap-*`, `divide-*` or `grid-cols-*` container to another keeps the
  spacing, divider or column sizing the container it joined wrote. Each of the three tracked the children
  it had written to by raw reference and reset the value on one no longer in the container, and the
  element pool hands a child straight from one container to another — so the container a child left could
  still be tracking it after the container it joined had written to it, and reset that write on its next
  pass. A row that took a pooled `Label` from a sibling `gap-4` row lost one gap, a `divide-x` row lost
  one rule, and a grid child lost its column gap and its width. Which container won was decided by the
  order their re-applies landed in: a reconcile pass re-applies a container right after reconciling that
  container's children and so never loses the race, and what reached this was the panel's own re-apply
  sources — a `GeometryChangedEvent` or an `AttachToPanelEvent` arriving after the other container had
  written. Each turn-off now asks a claim first, so only the container whose write is still on the child
  may take it back off. Gap and grid share one claim, since both write a child's margins: a child moving
  between a gap container and a grid one is one owner's or the other's.
  One case moves the other way with it. A child the reconciler *removes* now keeps the spacing its
  container wrote, where before the container's next pass cleared it: element cleanup drops the child's
  claim, and the container then finds no claim to release against. This is every reconciler-driven child
  removal, not an edge case. A removed element is either discarded with its subtree or scrubbed on its
  way into the element pool, so what this changes is the inline state of an element an application has
  kept a reference to and re-parented itself.

- A `V.Suspense` nested inside another one's children no longer loses its fallback when the outer
  boundary resolves. The outer boundary's expansion wrote its own answer over every fiber it had
  created, the inner boundary's hidden children included, so a state update inside content the inner
  boundary was still waiting on stopped being deferred and committed into the slot range the inner
  fallback occupied — the fallback left the tree and the half-loaded content took its place. Each
  boundary now keeps the answer it gave for the children it suspended over, so an outer boundary
  resolving says nothing about an inner one that has not. An outer boundary that suspends still hides
  the inner boundary's fallback, and releases it again on resolve.

- Two `V.Suspense` boundaries in one component's render no longer share a single suspended mark. One
  showing its fallback while a second, placed after it, rendered its children left the component
  unmarked, so a state update inside the first one's hidden children stopped being deferred and
  committed into the slot range its fallback occupied — the fallback disappeared from the tree.
  Whether a boundary is showing a fallback is now read off the per-boundary record each `V.Suspense`
  writes under its own position, so a sibling that resolves cannot clear it. Ordering decided it:
  the same two boundaries with the resolved one placed first were unaffected.

- A second `Mutate` call no longer cancels the first or drops its callbacks. Both run, each delivers
  its own `OnSuccess` / `OnError`, and `Status` / `Data` / `Variables` show the most recent — which
  is what TanStack Query does, and it hands `mutationFn` no signal at all. A double-tapped Buy used to
  abort a request the server may already have committed and then skip the `OnSuccess` that records
  it. The token survives for unmount, where a request does want cancelling. A starting call also
  clears `Data` along with `Error`, so a result belonging to the previous call is not read as this
  one's while it is still pending.

- A pooled widget carried far more than its reset helper named. Most of the writable surface of
  `Button`, `Label`, `Toggle`, `Slider` and `TextField` survived a pool cycle and arrived on whatever
  mounted next: a read-only or multiline `TextField`, a placeholder string the next consumer had not
  written, a `Toggle` stuck showing a mixed value, a `Slider` whose direction was inverted, the
  rich-text and emoji-fallback flags on `Button` and `Label`, text-selection colours and behaviour on
  `Button`, `Label` and `TextField`, and a data-source binding on all five. A `TextField`'s stale
  placeholder is the sharp end: the pool's own contract is that the next consumer cannot observe the
  previous one's text.

- A recycled composite field stopped delegating focus, and a recycled `Slider` or `TextField` also
  started taking pointer picks on its own root. The shared reset writes the plain `VisualElement`
  defaults for both, and only `focusable` was written back afterwards. A recycled `Label` carried
  tab index 0 where its constructor sets -1, which shows once a consumer declares it focusable.

- A `Slider` still carrying its numeric input field is no longer pooled at all. That sub-element only
  tears down while the slider is on a panel, and a pool return has already detached it, so a recycled
  slider showed a stray input box that its own `showInputField` denied.

- A `Button` or `Label` no longer carries a paint delegate a consumer added onto whatever mounts next.

- A composite field pooled without a label lost the class its constructor adds for that case, so a
  recycled one no longer matched a fresh one's class list.

- `UseMutation`'s `Reset` now abandons the call in flight instead of only clearing what it had written
  so far. Pressing Save and then Reset used to leave the save owning the handle, so when it landed it
  wrote `Success` and its result straight back over the idle state the user had asked for. The
  abandoned call is still not cancelled and still delivers its own `OnSuccess` / `OnError` — what it
  no longer does is write the handle, which is what v5's `reset()` detaching the observer amounts to.

- A `UseMutation` handle no longer shows `Data` for a call it reports as failed. The result was
  written before `OnSuccess` ran, so a handler that threw — which correctly makes the mutation an
  error — left that result on show underneath the error, and a view rendering `Data` without checking
  `Status` first showed it. The outcome is now committed after the handlers on both paths, which is
  where v5 dispatches it, so a handler no longer reads its own call's outcome and `Data` stands only
  under `Status == Success`.

## [2.1.0] - 2026-08-09

### Highlights

- A torn-down tree no longer keeps something running. Disposing a reconciler could leave an animation
  driver ticking or a drop-shadow silhouette attached, on elements the unmount had already finished
  with — the teardown re-derived their styling into tables it had emptied moments before.

- A child-combinator payload survives a child moving between containers. An element pooled out of one
  container and re-rented under another sat in two containers' lists at once, and the one it had left
  stripped what the one it had joined had just written.

- Text behind a child-combinator finally changes. `[&>*]:uppercase` over mixed children transformed the
  element children and left a `V.Text` one exactly as it was, the class having landed on it while the
  two resolvers stood down for it at every render.

- A pivot can be any point in the box. `origin-[33%_75%]` writes `transform-origin`, so a needle that
  turns at 90% of its height or a bubble that scales out of its tail corner no longer needs a
  `refCallback` — the escape hatch the migration guide ranks last of three.

- A proportional split is a utility again. `grow-[N]` and `shrink-[N]` take an arbitrary factor, where
  the vocabulary previously stopped at 0 and 1 and forced the split to be re-expressed as a basis that
  stops matching once siblings have minimum sizes.

### Fixed

- Disposing a reconciler no longer hands a torn-down tree a live paint or animation binding. Releasing
  a manipulator turns its payloads off, and moving a gated token that way re-derives the element's
  passes — into the very tables the dispose emptied a few lines earlier, so an element could come out of
  the teardown with a driver still ticking. What it takes is a plain painted utility beside a variant
  whose payload is one too: `shadow-lg hover:shadow-sm`, `animate-pulse dark:animate-hue`. Any variant
  family that turns its payloads off at release reaches it, state and relational ones included, and a
  variant carrying anything else (`shadow-lg dark:text-white`) does not. It reaches whatever the unmount
  reconcile did not clean first.

- A `[&>*]:` payload stays on a child that moved between two containers. The walk turning a payload off
  tracked the children it had written to by reference alone, so a child pooled out of one container and
  re-rented under another was in both walks' lists at once — and the container it had left turned the
  payload off on it, on the next event that reached that container, undoing what the one it had joined had
  just written. Only the walk that last wrote a payload to a child may turn it off now. A class payload
  needed the two containers to carry the same token for anything to be taken away; an arbitrary one did
  not, since an inline layer is keyed by property and priority, so `[&>*]:w-[8px]` took `[&>*]:w-[12px]`'s
  width with it.

- A `[&>*]:` font or text-effect payload now reaches a `V.Text` child. The payload already landed on that
  child's class list — a plain `[&>*]:bg-red-500` styled it — but the two resolvers stood down for it at
  every render, so `[&>*]:uppercase` over mixed children transformed the element children and left the text
  one alone. The paint families (`shadow-*`, `ring-*`, `skew-*`, the gradients, `animate-*`,
  `border-dashed`) still do not reach such a child at all: they run behind a paint verdict only an
  element's own class pass records.

### Added

- `origin-[x]` and `origin-[x_y]` arbitrary values for `transform-origin`. The pivot a rotation or a scale
  turns about could only be one of nine keywords, so a gauge needle at 90% of its height, or a bubble
  scaling out of its tail corner, had to be written from a `refCallback` — the same escape hatch the guide
  ranks last. A single component is the x alone and leaves the y at 50%, as CSS does. Two deviations from
  Tailwind, which passes the bracket contents through to CSS: a keyword inside the brackets is refused,
  since the nine keyword pivots are their own classes (`origin-top-left`, and `origin-[0%_75%]` for the
  mixed keyword-and-length case they cannot spell), and a third component is refused: the
  engine's transform-origin does carry a z, so this is a decision about the value shape rather than a
  limit. Tailwind declares no negative variant either, so `-origin-[…]` is refused there too; a minus
  inside the brackets is how to write one. The pivot does not move a skewed element's painted silhouette,
  which stays at the box centre.

- `grow-[N]` and `shrink-[N]` arbitrary values for `flex-grow` / `flex-shrink`. The utility vocabulary
  stopped at 0 and 1, so a proportional split had to be re-expressed as a fixed or percentage basis —
  which stops matching once siblings have minimum sizes — or written from a `refCallback`. See
  `Documentation~/styling-flexbox-and-gap.md`.

## [2.0.1] - 2026-08-08

### Highlights

- Passive effects no longer stop firing for the whole tree. One removed subtree could take the scheduled
  drain down with it, and every `UseEffect` in the application went quiet from that point on — the drain
  now hangs off something a subtree cannot remove.

- History no longer moves while a navigation is still asking permission. A Blocker or a Guard redirect that
  never arrived could still leave the router pointing somewhere the user never went, and an abandoned
  attempt could leave an entry behind for the path it started from. The index is written when the
  navigation commits and not before.

- A variant stacked three deep no longer throws out of the interaction that closes it. Releasing a drag,
  losing focus, or writing a controlled value on a class like `dark:active:sm:bg-on` ended the operation
  with an exception rather than a style change.

- Utilities behind a variant reach the properties they name. A font family, a text transform, a decoration
  or a line height written behind `dark:`, `hover:` or `[&>*]:` resolved to nothing and reported nothing —
  the class landed and the text did not change.

- A pooled `Label`, `Toggle`, `Slider` or `TextField` no longer hands the next mount the previous one's
  children. Giving one children through `V.Custom<T>` sent them into the pool, and they reappeared inside
  an unrelated element later.

- `isPending` follows the transition that set it. A spinner could outlive the work it described, held up by
  a different slot's action or by a deferred value that had nothing to do with it, and two transitions in
  one component stopped being independent while either was awaiting.

- Loader data belongs to the location that asked for it. A navigation started from inside a loader wiped
  the previous location's data, a Suspend loader could deliver to a location that had moved on, and
  `CurrentLoaderData` handed out a dictionary the runner went on writing into.

- A dependency list means one thing everywhere. An explicit `null` froze a memo and a callback while
  re-running an effect, so the same spelling did opposite things depending on which hook read it, and
  omitting the list entirely had no way to say "recompute every render".

- Twelve declarations now say what they return. `UseLocation`, `UseContext`, `ISearchParams.Get` and the
  sixteen generated `V.Memoized` overloads documented a null they did not admit, so a consumer with
  nullable reference types enabled got neither the warning nor the guarantee.

### Fixed

- A `Toggle`, `Slider` or `TextField` given children through `V.Custom<T>` no longer carries them into the
  element pool. Each of the three builds its own input into the very container those children expand into,
  so the reset could not simply empty it as the `Button` and `Label` resets do — it now detaches what the
  control did not construct and leaves the control itself intact. Without that, the next `V.Toggle` mount
  rented an element still showing the previous subtree's content beside its own.

- `checked:` and `peer-checked:` now apply at mount to a control that reports a bool without being a
  `Toggle` — `RadioButton` and `Foldout` among the built-in ones. The change registration beside each seed
  never restricted the type, so such a control styled correctly from the first interaction onward and only
  started wrong. A `Foldout` reports itself checked from its own constructor, so `checked:` on one now
  paints at mount without anyone having set a value.

- A variant stacked three deep no longer throws out of the operation that settles its outer gate. Settling
  a consumer applies its payload, and a payload that is itself a variant registers a further manipulator in
  the registry the settle was walking, which ends the walk with an `InvalidOperationException`. All three
  settles could reach it: `dark:active:sm:bg-on` on a drag source or any of its ancestors threw on the drag
  release, `dark:focus:sm:bg-on` on the focus revert a containment snap-back performs, and
  `dark:checked:hover:bg-on` on a value written through a controlled prop.

- A `font-*` utility a container imposes on its children with `[&>*]:` now reaches them. The font layer
  was re-derived only when a child's own class list changed content, and a `[&>*]:` payload lands on the
  child's live list without touching that array — so `[&>*]:font-mono` over a child that declares no
  variant of its own and whose own classes never change was lost for that element's whole life, while
  `[&>*]:uppercase` in the same markup appeared on the next render. Both now land from the child's next
  render, for a child rendered as an element; neither lands at mount for a child that declares no variant
  of its own, which is unchanged. A `V.Text` child never gets either: the payload reaches its class list
  and neither resolver ever reads it, at any render, so put the utility on a `V.Label` there.

- A `UseTransition` slot's `isPending` now reports its own transition rather than whatever holds the
  Transition lane. Two cases showed a spinner after the work it described was over: a `startTransition`
  whose callback scheduled no update stayed pending for as long as anything else on the component was
  in flight, and a component holding both a `UseTransition` and a `UseDeferredValue` stayed pending
  after its transition had committed, until the deferred value's own lane drained.

- A `V.Motion` now applies the text-effect cascade when it mounts, not only when it next patches.
  `uppercase` / `lowercase` / `capitalize`, `underline` / `line-through` / `overline`,
  `whitespace-pre-line` and `leading-*` as a plain class on a Motion left its own text and its descendant
  text leaves untransformed until some later render happened to patch it, so a Motion nothing re-renders
  showed the wrong text for the element's whole life.

- A `Label` given children through `V.Custom<Label>` no longer carries them into the element pool. The
  Label reset cleared the text but not the child list, so the next `V.Label` mount rented an element that
  still showed the previous subtree's content on top of its own. Both the ordinary unmount and the
  Suspense rollback reclaim now treat a child-bearing Label exactly as they already treat a child-bearing
  `Button`.

- A navigation waiting on a Blocker no longer takes the history with it. `Router` moved the history index
  before running the Guard and Blocker phases, so a Back parked on a confirm dialog left the router
  pointing at the entry the user had not gone to yet: clicking a link before answering the dialog pushed
  onto that position and deleted the page the dialog was covering. The destination is now resolved per
  attempt and applied when it commits, so a second navigation started meanwhile reads the position the
  user is actually on. `Router.CanGoBack` / `CanGoForward` describe that position during the wait too.

- A Guard redirect abandoned before it arrives no longer leaves an entry for the path it started from.
  The originating path was appended up front for the redirect target to overwrite, so a redirect that a
  Blocker parked and a newer navigation superseded stranded that entry for the rest of the session, with
  Back onto it re-running the guard and landing on the redirect target. The pair now records only the
  target, with the originating navigation's own Push/Replace effect.

- Going Back to a route whose `LoaderMode.Suspend` loader had not resolved runs its loaders again instead
  of restoring an empty snapshot. The history entry recorded the loader data as it stood at commit time
  with nothing marking it unfinished, so a route left before its loader resolved was cached in that state
  and rendered empty on every later visit. Entries now record whether their loader round finished, and
  only a finished one is served from the Back/Forward cache.

- A `RouteDefinition.Guard` that throws, or a redirect target that declares both `RedirectTo` and `Guard`,
  no longer leaves the router mid-navigation. The exception still reaches the caller, but `Router.Status`
  now becomes `Error` instead of staying at `Matching` — which every `UseNavigation()` consumer rendered
  as a pending navigation that would never finish — and the history is left as the throwing attempt found
  it. This covers an exception from the commit itself, which previously escaped past the unwind and left
  the status at `Loading`.

- A `LoaderMode.Suspend` loader that answers immediately now delivers its result to the location it was
  loaded for. Its value arrived while the navigation was still running and was then overwritten by the
  loader results the commit takes, so `UseLoaderData` read null on the first render and every later visit
  was served that empty snapshot from the history cache. The same early arrival was also written into the
  entry the user was navigating away from, which then carried loader data for a location it never was.

- `NavigateAsync` in `NavigationMode.Back` or `NavigationMode.Forward` with no entry to step onto now
  returns `Cancelled`, as `GoBack` / `GoForward` already did for the same request. It previously ran the
  navigation against a history slot that does not exist: with a Guard redirect on the route it appended an
  entry while leaving the index on the previous one, so `CanGoForward` pointed at the page already on
  screen, and without one it threw out of the commit. The refusal is now made before the navigation starts,
  where `GoBack` / `GoForward` make theirs, so the request also stops announcing a `Matching` that nothing
  finishes, stops leaving `Router.Status` at `Idle` over a location that is committed and rendering, and
  stops cancelling whatever navigation was already in flight.

- `Router.CurrentLoaderData` now hands out a snapshot that stays as it was handed out. It returned the
  loader round's own result dictionary, which the runner goes on writing into when a `LoaderMode.Suspend`
  loader resolves after the navigation has committed — so a caller that held the returned
  `IReadOnlyDictionary` across that resolution saw it gain an entry underneath, ahead of the location
  re-emit that is supposed to deliver it. `UseLoaderData`, which looks its key up on each render rather
  than holding the dictionary, was unaffected either way.

- A loader that starts a navigation no longer wipes the loader data of the location that navigation
  commits. The inner navigation cancels the attempt whose loader started it, and that attempt cleared
  `Router.CurrentLoaderData` and `CurrentLoaderErrors` as it unwound — by then the data of the page the
  user had just landed on, leaving `UseLoaderData` and `UseRouteError` empty there. The cancelled attempt
  now writes neither, and likewise leaves `Router.Status` on the `Ready` the committed navigation
  published instead of resetting it to `Idle`.

- A loader that starts a navigation no longer hands its round's remaining loaders the new navigation's
  cancellation token. The runner re-read the field holding the current round's token source once per
  loader, so every loader after the navigating one launched under the nested round's live token and came
  back looking current for a round nobody was waiting for. A round now captures its token once, so the
  loaders left over from a superseded round observe the cancellation that superseded them.

- `V.NavLink` with `to: "/"` no longer renders active when there is no location for it to be active for.
  The current path stood in as the empty string whenever none was available — before the first navigation,
  or in a tree with no router above it, such as a preview — and an empty path normalises to the root, so a
  navigation bar's home link highlighted itself as the page the user was on. No `NavLink` is active until
  there is a location.

- `Hooks.UseDeferredValue` now hands its new value over only on the render that drains the Transition
  lane. Previously any re-render still carrying the same input promoted it — a sibling `UseState` setter
  firing before that lane drained was enough — so the expensive subtree the deferral exists to keep off
  the urgent path was reconciled there anyway. A re-render that is not that flush now keeps returning the
  previously committed value and re-queues the lane.

- A re-render request a component makes from inside its own render is no longer discarded when a parent
  re-render subsumed that component into its own pass. The settle after such a subsuming render dropped
  the component's entire pending-lane queue on the premise that the render had just satisfied all of it,
  which is true only of updates pending before it ran. `UseDeferredValue` is the visible case: fed from a
  prop, it queues its Transition lane during the parent's render, so the deferral had nothing left to
  commit on. This holds equally when the request lands on a lane an earlier render already queued — two
  quick keystrokes into a search box, where the second arrives before the first has drained.

- Two `UseTransition` slots in one component are independent again while one of them is awaiting. A
  second slot started during another slot's async transition took a re-entrancy path meant for a
  genuinely nested `startTransition`, so its `isPending` was never set and its spinner never appeared;
  the re-entrancy join is now scoped to the slot whose transition is running rather than to the whole
  component.

- An ordinary state update made from a click, a value change or another discrete input keeps its urgent
  priority while an async transition is in flight on the same component. It was routed to the Transition
  lane for the whole in-flight window, so it missed the discrete event's synchronous commit and landed on
  the delayed tier roughly 100 ms later. Updates the async action makes after an await are still
  transition-lane updates, as is anything a handler wraps in a `startTransition` of its own — with one
  exception the framework cannot see past: completing the awaited task from inside a discrete handler
  resumes the action within that handler, and its updates take the handler's urgent priority.
- `checked:`, `peer-checked:`, `group-focus-within:` and `peer-focus-within:` now work as the *inner*
  half of a stacked variant. `dark:checked:bg-primary` and `dark:group-focus-within:ring-2` applied
  nothing, while the same pair written the other way round (`checked:dark:bg-primary`) applied — so the
  documented rule that stacking order does not matter held for every family except these four. The
  stacked manipulator classified its inner kind with a set of independent bool predicates, and a set of
  bools has no way to report a kind matching none of them: all four fell past every branch into the
  relational one, which mapped hover / focus / active only, so their gate had no signal that could open
  it. A `checked:` inner now reads the target's own `ChangeEvent<bool>` and a `peer-checked:` inner the
  resolved peer's, both seeded from an already-checked control when the gate is built, the same way the
  top-level variants are.

- A `Focusable` prop that a later render stops declaring now hands the element back the focusability it
  was constructed with, instead of making it focusable. Dropping the prop compares unequal to the
  declared value, and the absent case coalesced to `true`: a `V.Div` that carried `Focusable = true` for
  one render stayed a Tab stop and a 2D navigation target until some later render declared the prop
  again, and one that carried `Focusable = false` was *granted* focusability by the render that removed
  the prop — on a runtime panel, an invisible container catching gamepad navigation. Mounting already left
  an absent `Focusable` alone, so the two paths disagreed about what "not declared" means. The value the
  element carries before Velvet writes the flag at all is now recorded and restored — including before a
  drag session's transient keyboard-focus anchor writes it, which would otherwise be handed back as the
  element's own — and that answers for a `V.Custom<T>` type whose default no table could know. `TabIndex` and `DelegatesFocus` are
  unchanged and still coalesce to `0` / `false` when dropped.

- A Suspense primary that suspends after creating a `V.Custom<T>` element now disposes the component
  fibers mounted inside that element, whatever `T` is. The rollback's fiber sweep skipped any element
  matching `Label`, `Toggle`, `Slider` or `TextField` as a childless primitive, which `V.Custom<T>`
  reaches with both a subclass of one of them and one of them itself — and `V.Custom<T>` declares
  children for any `T`. A `V.Component` declared in such an element's `children` therefore kept its
  `ComponentRegistry` entry pointing into a subtree dropped for GC, ran its layout effect against that
  dead element while the boundary was showing its fallback, and left a `UseStore` subscription taken
  during the speculative render live. The sweep no longer tests the element's type at all: what decides
  whether an orphan can hold a fiber is whether its node declared children, which the element cannot say.

- A `Hooks.Use` loader cancelled through a token the caller owns now surfaces the cancellation to the
  nearest error boundary, instead of leaving its Suspense boundary in fallback with no way out. A
  cancellation that reached the awaiting frame was swallowed without recording an outcome, which is
  correct only for Velvet's own cancellation; a logout CTS, a linked token in a data layer or a
  superseded request left the resource `Pending`, and nothing restarts a resource in that state while
  its key is unchanged. Only a cancellation Velvet itself requested is silent now.

- `font-<family>` and the text-transform / text-decoration / `whitespace-pre-line` / `leading-*`
  utilities now take effect behind a variant. `dark:font-mono`, `md:font-display`,
  `hover:uppercase`, `md:dark:hover:underline` and `md:leading-loose` all changed nothing before:
  both families are realised from the class array Velvet last reconciled, and a variant writes its
  payload onto the element's live class list instead, so the resolver never saw it — and neither
  family has a USS rule the bare class could fall back on, which made the failure silent. Both are
  now re-derived on a variant toggle exactly as the layout and paint utilities already were, at
  mount and in both directions. For a child that a `[&>*]:` container rule reaches, both stand down
  at mount instead: the only class array available there is the child's live list, which cannot see
  that child's own `font-[…]` / `leading-[…]`, and both resolvers rewrite unconditionally — so such a
  rule leaves the child's own font and line height alone rather than resolving over them.

  `font-<weight>` and `italic` behind a variant were only partly working, through the coarse
  `-unity-font-style` fallback in the bundled stylesheet; they now go through the resolver and get
  the full weight scale and a weight-specific Font Asset.

  An arbitrary `font-[…]` or `leading-[…]` payload behind a variant also no longer leaves its raw
  bracket token sitting on the USS class list while the variant is lit — the reconciled path already
  kept those two families off it.

- A Back or Forward navigation served from the history's loader cache now cancels the loaders still in
  flight from the round it left. That branch commits without running `RunLoadersSync`, which is where a
  previous round is superseded, so a `LoaderMode.Suspend` loader belonging to the page the user navigated
  away from still counted as the current round: its result landed in the live loader data of the entry
  the user had gone *back* to, re-rendered that page showing the other page's data, and was written into
  the history entry so the wrong data persisted. Two entries matching the same route pattern share a
  route id, so neither the re-publish check nor the history write-back could separate them — `/users/1`
  re-rendered with user 2's record and kept it.

  A separate defect in the same area remains open: a history entry left before its Suspend loader resolved
  is cached as an empty-but-complete snapshot, and Back or Forward serves that snapshot without re-running
  the loader, so the page shows no data.

- A navigation abandoned while a Blocker or a Guard redirect was still awaiting now leaves the router as
  it found it. A blocker that honors its token raises `OperationCanceledException` out of the await,
  which jumped over the rollbacks that the blocked and superseded paths run:

  - `GoBack` / `GoForward` left the history index on the entry they had provisionally moved to, so
    `CanGoBack` described a location the user was not on and the next `Push` truncated the entry they
    were still looking at. From `/about`, a cancelled `GoBack` followed by `Push("/contact")` and
    `GoBack()` landed on `/home` rather than `/about`.
  - A cancelled Guard redirect left on the stack the provisional Push entry it had appended for the
    redirect target to overwrite.
  - `Status` stayed at `Matching`, so every component calling `UseNavigation` rendered its pending
    branch indefinitely with no navigation in flight.

  Each of those rollbacks now runs only while the attempt is still the current navigation. Cancelling a
  token does not oblige a blocker to resume at that moment, so an abandoned attempt can reach its rollback
  after the navigation that superseded it has committed; it would then put back an index, a `Status` and a
  history snapshot describing a router that no longer exists, destroying entries the newer navigation had
  pushed. Disposing the router retires the claim the same way, so a blocker resuming after teardown no
  longer writes to it.

- `UseEffect` no longer stops running across the whole tree after a subtree is removed. One scheduled
  callback drains every pending passive effect in a mounted tree, and it used to be registered on the
  mount point of whichever fiber staged an effect first — routinely a container inside a subtree that
  a route change, an error-boundary fallback or a conditional render then removed before the next
  frame. Removing that container took the callback out of the panel's scheduler, and the flag marking it
  as already registered was never cleared, so nothing registered a replacement. From then on the frame
  tick ran no passive effect anywhere in the tree — subscriptions did not attach, fetches did not fire
  and cleanups did not run — until something else forced them: a click or another discrete event, which
  flushes pending passive effects synchronously, or the removed container being rented back out of the
  element pool and re-attached, which resumed the callback at an arbitrary later moment. The drain is now
  registered on the root mount element, the same tree-stable host the batch scheduler already uses for
  its own drains and for the same reason.

- An exception thrown by a mutation's `onError` handler no longer changes what the mutation reports.
  Through `MutateAsync` it used to replace the mutation's own exception, so the caller awaited a
  failure and received the handler's error instead of the one the mutation function raised, and
  nothing was logged; through `Mutate` the same exception was already logged and the outcome was
  already unaffected. The handler's exception now goes to the unobserved-exception channel in both
  cases, and `MutateAsync` rethrows the mutation's own exception.

  A throwing `onSuccess` handler is unchanged and still makes the mutation an error, which is what
  React does — the success state is dispatched after the handler runs, so a handler that throws never
  reaches it.

- `Hooks.UseBlocker` now has a single-argument overload, and calling it without a dependency array
  re-registers the predicate on every render. Because deps were declared `params`, omitting them used
  to bind an *empty* array rather than the null that means "re-register every render", and an empty
  array compares equal to itself on every subsequent render. The blocker therefore kept the predicate
  closure the mount render created, and answered forever with the state that render had captured: the
  idiomatic `Hooks.UseBlocker(attempt => isDirty)` never fired an unsaved-changes prompt, or, if the
  first render had `isDirty` true, blocked every departure permanently. Nothing reported it — the VEL100
  exhaustive-deps analyzer skips a call with no deps argument, correctly for the sibling hooks whose
  omitted form is already safe. `UseCallback`, `UseMemo` and `UseImperativeHandle` carry the same
  deps-less overload for the same reason, and `UseEffect`, `UseLayoutEffect` and `UseInsertionEffect`
  reach the same place through an optional `deps = null` parameter. The deps-taking overloads now also
  accept a null array without a nullable warning; that is `UseMemo`'s annotation, not its behaviour —
  `UseBlocker` guards on `deps != null` and re-registers, where `UseMemo` and `UseCallback` compare a
  null against a null, find them equal and freeze.

- `Hooks.UseLocation()` is declared `RouterLocation?`. Its documentation already said it returns null
  with no router mounted, and Velvet's own `RouteNavLink` already reads it that way, but the
  declaration promised non-null, so `Hooks.UseLocation().Path` compiled without a warning in a
  nullable-enabled assembly and threw `NullReferenceException`. The documentation now also names a
  second case it had left out: a mounted router returns null until it publishes its first location,
  which is the state `Samples~/StarterApp` starts in, seeding the location context from
  `Router.CurrentLocation` before the first navigation. The sibling router hooks `UseMatch`,
  `UseOutletContext`, `UseLoaderData` and `UseRouteError` were already annotated nullable.

- `ISearchParams.Get` is declared `string?`, matching the null it documents for an absent key and the
  `SearchParams` implementation that returns it. `Hooks.UseSearchParams()` hands back the interface,
  so the interface declaration is the one a consumer reads: `searchParams.Get("id").Length` compiled
  without a warning and threw for any query string lacking the key. The mismatch was also raising
  CS8766 in the package's own build.
- `TransitionType.Spring` and `TransitionType.Bezier` no longer tell you in IntelliSense that colours
  and lengths are out of scope. Both have been driven channels since 2.0.0 and the tooltip's exclusion
  list was left behind — a consumer who read it hand-rolled a separate tween for a background colour or
  a width that the spring config was already animating. Both members now point at
  `Documentation~/motion.md`'s "Driven channels", which owns the list; the guide gains the two
  exclusions it was missing (percentage-based translate and per-axis `scale-x-` / `scale-y-`), and both
  are now pinned by tests.

  Two guide corrections ride along. `Documentation~/react-migration.md`'s Store example did not
  compile: it passed `onChange:` to `V.Slider`, whose handler parameter is `onValueChanged`, and its
  `Store<T>` subclass left `ResetCore` unimplemented. `Documentation~/styling-variants.md` claimed a
  stacked variant sits above either of its parts alone; it layers at the higher of the two, so against
  that stronger part it ties rather than wins, and the tie is settled the way the same file's *Same
  family, different values* bullet already describes.

- A dependency list now means the same thing wherever one is accepted. Passing an explicit `null` froze
  `Hooks.UseCallback` and `Hooks.UseMemo` — the value was computed once and never recomputed, so a
  callback written that way captured the state of the render that built it — while the same `null` made
  `Hooks.UseEffect` and `Hooks.UseBlocker` re-run every render. All of them now read `null` as the absence
  of a dependency list, which is what omitting the argument already meant, so the freeze is no longer
  reachable by writing `null` instead of leaving a dependency out. `Documentation~/react-migration.md`
  §1-4 owns the table of what each spelling means.

- `V.Memoized(factory)` and `V.MemoizedWithKey(key, factory)` with the dependency list left off now
  rebuild the subtree on every render instead of caching it for the element's whole life. Omission was
  indistinguishable from an empty array at the call site, and the empty array the compiler supplied could
  never be perturbed, so a subtree written that way was frozen at its first render — the opposite of what
  `Hooks.UseMemo(factory)` means with its deps left off. Both now carry a companion overload that takes no
  deps parameter, and "compute once and keep it" is spelled with an explicit `Array.Empty<object>()`. A
  `[MemoizeMethod]` method taking no parameters is unaffected: its generated wrapper now passes that empty
  array, which is the caching it always described.

- The exhaustive-deps analyzer no longer asks for `Hooks.UseTransition`'s starter,
  `Hooks.UseSearchParams`' setter or a `Hooks.UseMutation` handle in a dependency array. All three are
  documented as reference-stable across renders, so VEL100 was demanding a dependency React would not; the
  analyzer's exemption list and the runtime's own `<returns>` documentation are now pinned against each
  other in both directions.

- The sixteen generated `V.Memoized<T1..T8>` / `V.MemoizedWithKey<T1..T8>` overloads now carry nullable
  reference annotations, so a consumer with nullable reference types enabled gets the same key and
  dependency nullability from them as from their hand-written non-generic siblings, which declare
  `string? key`. The same applies to the wrappers `[MemoizeMethod]` generates into user assemblies, which
  were nullable-oblivious for the same reason.

- `Hooks.UseContext<T>` is declared `T?`. It hands back `ComponentContext<T>.DefaultValue` when no
  Provider is above the caller, and that property is `T?` — `ComponentContext<T>.Create` takes a null
  default, which is how `RouterContext.Location` and `RouterContext.OutletContext` are both built — so
  `Hooks.UseContext(RouterContext.Location).Path` compiled without a warning in a nullable-enabled
  assembly and threw `NullReferenceException` with no router above it. This is the read path underneath
  `Hooks.UseLocation()`, which was annotated in isolation. Throwing when no Provider is found was
  rejected for the reason `UseLocation` was not made to throw: a Velvet context is seeded with a
  default rather than being absent, so a context legitimately created with a null default and a
  missing Provider are the same read, and `UseOutletContext` documents the first of those as normal.
  Nothing changes at runtime, and a context of a value type is unaffected either way, since `T?` on an
  unconstrained type parameter is an annotation rather than `Nullable<T>`.

- Stepping onto a history entry whose `LoaderMode.Suspend` loaders all answer immediately now leaves that
  entry servable. Such an entry is recorded unfinished and re-runs its loaders when stepped onto, which is
  correct — but a loader handed an already-complete task resolves inside the run that launched it, before
  the step has a location to record its result under, so the write-back that marks an entry finished never
  arrived. The entry stayed unfinished, and a route whose loaders all answer that way could therefore never
  be served from the Back/Forward cache: its loaders ran again on every step onto it. A Back or Forward
  whose loader round finished before the step committed now records the entry from that commit.

- `checked:` now tracks a control whose value is owned by a fully-controlled `value:` prop. That value is
  written to the control without notification, so the `ChangeEvent<bool>` the variant listens for never
  fired and the payload stayed on whatever the last user interaction had left it at — in the shape a React
  developer reaches for first, a parent holding the state and passing it down. The stacked forms
  (`dark:checked:` and the rest) get it too, and so does `peer-checked:` — a peer written through a
  controlled prop restyles the siblings that consume it, in the plain and the stacked spelling alike.

- A render that drops a `Focusable` prop from a drag source no longer leaves the source unfocusable for
  the rest of the drag. A session anchors keyboard focus on a source that carries no focusability of its
  own, so Escape reaches it; dropping the declaration put the element's own default back over that anchor
  and told the session the flag had been *declared* in the same breath, so the anchor was neither in force
  nor owed a restore. The session now takes the flag back when the declaration goes away, and still hands
  it back at the drop.

- `VirtualListNode`'s type-erased item list, key selector and renderer now admit a null element
  (`IReadOnlyList<object?>`, `Func<object?, …>`). The source element type of `V.VirtualList<T>` may itself
  be nullable and each item goes straight back to the caller's own selector and renderer, so the
  non-nullable erasure claimed something nothing upheld — and was what the compiler reported against the
  wrapper implementing it. The `V.VirtualList<T>` signature is unchanged; code that hand-builds a
  `VirtualListNode` from a `Func<object, …>` gets a nullability warning until the delegate is widened.

### Changed

- A Blocker registered during a Guard redirect is now told the attempt is a `Push` where it was told
  `Replace`. The redirect target is committed with the originating navigation's history effect, so a
  leave-confirmation asked about a pushed redirect now describes the step the history actually takes.

## [2.0.0] - 2026-08-02

### Highlights

- **Player builds now render what the editor rendered.** The bundled utility stylesheet reaches a build
  through the new `VelvetStyleUtilities.AttachTo(root)`, and the four shaders behind drop shadows,
  skewed gradients and the `brightness-*` / `saturate-*` filters are put in front of every build. Each
  previously resolved to nothing in a player.
- **A variant payload spelled as a class now overrides the base utility it names.** `bg-white
  dark:bg-neutral-900`, `w-full md:w-64` and `items-center md:items-start` were silent no-ops decided by
  stylesheet declaration order; each element now ranks its classes per priority layer. The important
  modifier (`!bg-red-500`) applies to class-only utilities too.
- **Variants drive the utilities Velvet paints itself.** `md:gap-4`, `hover:divide-y`, `md:grid`,
  `dark:bg-gradient-to-r`, `focus:-skew-x-6`, `hover:animate-pulse`, `md:shadow-lg` and
  `dark:text-balance` did nothing until an unrelated re-render happened to bring the bare class in.
- **`ring-*` / `outline-*` behind a variant renders, and a ring no longer wraps its element.** The band
  is drawn on a separate element, so `w-full`, `absolute`, `self-*`, `mx-auto` and grid sizing behave on
  a ringed element exactly as they do without the ring.
- **Spring and bezier transitions animate colours and lengths**, not just opacity and the
  translate/scale/rotate trio, and they suspend an element's own USS transitions for the play.
- **A light theme, and it is the default.** The semantic colour tokens are two opaque sets — light on
  `:root`, dark on `.dark` — so nested `bg-surface` elements land on one colour. An application built
  on the old dark-only tokens sets `VelvetTheme.IsDark = true`.
- **Starter App**, the package's first importable sample: a scene to open and press Play on, with the
  panel host, the stylesheet attach and `V.Mount` already assembled.
- **`VEL500`–`VEL503`**, compile-time analyzers for nesting depth, branch count, parameter count and a
  tolerance NUnit silently drops. All four are opt-in per assembly and cannot break your build.
- **Breaking:** `[Memoize]` is renamed `[MemoizeMethod]`; a narrower utility now wins over a broader one
  (`size-8 w-4` lays out at 16px); `gap-*` / `divide-*` on a composite widget read the direction of the
  box their children are in; and `flex flex-col md:flex-row` lays out as a row above the breakpoint.

### Added

- **Starter App**, the package's first importable sample, offered by Package Manager's Samples section.
  It brings its own scene, `PanelSettings` and theme, so opening `StarterApp.unity` and pressing Play
  renders with no further setup: a `UIDocument` host, the `VelvetStyleUtilities.AttachTo` call and
  `V.Mount`, already assembled. Its screen is a two-route app over a `Store<T>`: a keyed `V.List`,
  `Hooks.UseState` on a text field, a `Hooks.UseEffect` subscription with its cleanup, `hover:`
  variants, and `V.Motion` inside `V.AnimatePresence` for the row enter and exit.
- `VelvetStyleUtilities`, a runtime resolver for the bundled utility stylesheet:
  `VelvetStyleUtilities.AttachTo(root)` puts it on a panel from the editor and from a player alike,
  and `VelvetStyleUtilities.Sheet` returns the asset. Until now the sheet was reachable only through
  `AssetDatabase`, which does not exist in a build, so a shipped game resolved every utility the sheet
  declares to nothing, while arbitrary values and the many families Velvet resolves itself rather than
  declaring — the `gap-*` / `divide-*` spacing, the painted and filter families, `animate-*` and more —
  kept working: a missing sheet that reads as a partial styling bug. `Documentation~/setup.md` carries
  the command that answers it per class. The sheet keeps its location under `Runtime/Styles/`; a build step
  puts a holder asset that references it into PlayerSettings' preloaded assets for the duration of a build,
  which is what makes it reachable, and the sheet is part of every build of a project with the package
  installed. `Documentation~/setup.md` covers this and the alternative of referencing the asset from a
  scene, and `Documentation~/player-builds.md` covers what the build step does to your project settings and
  what the sheet costs.
- `VEL500`, a compile-time analyzer that reports a member body nesting control flow more than four levels
  deep, at error severity. It does not fire on your code: the rule is opt-in per assembly and only the
  package's own assemblies opt in, so upgrading cannot break a build that compiled before. An assembly that
  wants the limit declares `[assembly: System.Reflection.AssemblyMetadata("Velvet.CodeShape", "enforce")]`.
  [`Generators~/README.md`](https://github.com/s4k10503/velvet/blob/main/Packages/com.velvet.core/Generators~/README.md)
  defines what counts as a level — linked absolutely because `Generators~` is stripped from the published
  package.
- `VEL501`, a compile-time analyzer that reports a member body making more than twenty branching decisions,
  at error severity. It shares VEL500's opt-in marker, so it likewise does not fire on your code unless the
  assembly declares it, and the same README defines what counts as a branch.
- `VEL502`, a compile-time analyzer that reports a declaration demanding more than six arguments from every
  caller, at error severity. It shares VEL500's opt-in marker, so it likewise does not fire on your code
  unless the assembly declares it. A parameter carrying a default value, and a trailing `params` array, are
  not counted — which is what leaves the `V.*` factories, whose long optional named lists stand in for JSX
  props, untouched. The same README defines what counts. One consequence for an assembly that opts in:
  `[MemoizeMethod]` supports 1-8 parameters, so its top two arities are unreachable there.
- `VEL503`, a compile-time analyzer that reports an NUnit tolerance — `.Within(...)` — chained onto an
  equality whose expected value is a `ValueTuple`. NUnit has no comparer for one, so the tolerance never
  reaches the members and the assertion is bit-exact equality while its failure message still prints the
  tolerance. It shares VEL500's opt-in marker, so it likewise does not fire on your code unless the assembly
  declares it. Unlike its three siblings it is a warning, and the same README says why.
- `TransitionType.Spring` and `TransitionType.Bezier` variant transitions now animate the
  color-valued (`background-color`, `color`, `border-color`) and length-valued (sizing, padding,
  margin, inset, flex-basis, border width, `border-radius`) properties of a variant delta, not just
  `opacity` and the `translate`/`scale`/`rotate` trio — closing most of the gap to Framer Motion,
  which animates any animatable property. Values are still derived entirely from the class strings
  (palette tokens, the `/alpha` modifier, the spacing/radius scales, and the bracket forms), so a
  property named by only ONE side of the delta, a pair mixing units (`w-1/2` → `w-[200px]`), a
  semantic theme token, or a keyword length still lands instantly instead of animating. A shorthand
  and one of its own longhands naming the same slot in one delta (`p-8` with `pt-2`) both snap,
  since which of the two holds that slot at rest is not derivable from the utilities alone.
- A spring or bezier play now suspends the element's own USS transitions for its duration when
  those transitions cover a property it drives — decided from the element's class list, so a
  `transition-transform` or `transition-all` element no longer paints a translate that trails the
  driver for the whole play and then eases in, while a `transition-colors` element running a
  fade/slide keeps its hover fade. The class list is read the way the cascade reads it:
  `transition-property` holds one value, so the utilities that set it do not combine and the
  last-declared one present wins outright — `transition-all transition-colors` transitions the
  colours only, and `transition-all transition-none` transitions nothing. `transition-filter` names
  `filter`, which no driver writes, so it never triggers a suspension. A bare `duration-*` with no
  such utility leaves `transition-property` at UI Toolkit's `all` default and so counts as covering
  everything. Overlapping plays each hold their own claim, so the first to settle cannot un-suspend
  the second.
- `space-x-reverse` / `space-y-reverse` are recognized alongside the existing `space-x-*` /
  `space-y-*` aliases: each is a per-axis marker that moves the gap-polyfill margin to the axis's
  trailing physical edge (`margin-right` / `margin-bottom`) instead of the leading one, matching
  Tailwind's own `space-*-reverse` semantics. See `Documentation~/styling-flexbox-and-gap.md` for
  the combination rule with a `flex-row-reverse` / `flex-col-reverse` container (OR, not XOR) and
  the one case where the marker ends up a no-op.
- `divide-x-reverse` / `divide-y-reverse` are recognized, previously documented as an explicit cut.
  Each moves its axis's divider border to the trailing physical edge (`border-right` /
  `border-bottom`), and combines with a reversed container the same way the `space-*-reverse` markers
  do: OR per axis, never XOR. A lone marker with no `divide-x` / `divide-y` stays inert, since it has
  no width to move.

### Changed


- The semantic colour tokens are two opaque theme sets instead of one translucent one, and a light theme
  now exists. `_tokens.uss` declared 27 of its 31 `--color-*` values with an alpha — twelve as white overlays, twelve
  as translucent accents and three near-black — and no background at all, so the layer could not render itself on a bare panel, `bg-surface` inside
  `bg-surface` composited to a third colour neither of them declares, and `--color-text` being an opaque
  near-white left the whole layer dark by construction with nothing to switch to. It now declares
  `--color-background` and a light set on `:root`, a dark set on `.dark`, and every colour that varies by
  theme is opaque — two nested elements carrying one background utility land on one colour. **The light
  set is the default**: an application built on the old dark-only tokens sets `VelvetTheme.IsDark = true`,
  which is the same flag the `dark:` variants already answer to —
  `VelvetStyleUtilities.AttachTo` binds the root it attaches the sheet to so the class the dark set keys
  on follows that flag, and `VelvetStyleUtilities.BindThemeTo(root)` does the binding for a panel that
  gets the sheet from a scene reference instead. The new `bg-background` utility paints the page colour.
  The tokens naming a strength rather than a role — the `--color-white-*` ladder, `--color-overlay*`,
  `--color-shadow` — keep their alpha, and are declared once for both themes.
  Utilities drawn from `_palette.uss`'s Tailwind scale are untouched. The preview window's Dark toggle now
  defaults on, matching the dark stage backdrop it already defaulted to.
  `Documentation~/styling-variants.md` owns the selection mechanism.
- `ring-*` / `outline-*` behind a variant now renders. `focus:ring-2`, `hover:ring-1`,
  `dark:outline-2` and every other variant form used to toggle a class that drew nothing; they now
  raise and drop the band as the state changes. The band no longer wraps the ringed element either:
  it is drawn on a separate element positioned over it, so a ringed element keeps every LAYOUT
  relationship with its parent that it declares — `w-full`, a parent's cross-axis stretch,
  `absolute` / `inset-0`, `self-*`, `mx-auto`, grid cell sizing and a parent's `[&>*]:` rules all
  behave on a ringed element exactly as they do without the ring, where the wrapper altered all of
  them for the element's whole lifetime. The band is placed directly after its own element rather than
  after all of them, so it paints in that element's own position: overlapping `-space-x-*` avatars
  carrying `ring-2 ring-white` occlude the previous one's band as they do on the web, and two
  `focus:ring-2` siblings render the same whichever was focused first. The deviations from CSS this
  hosting still carries — `ring-inset` painting over an opaque full-bleed child, a transform on the
  ringed element moving the element and not the band, and a ring on a `V.Motion` being ignored with a
  warning — are documented in `Documentation~/styling-variants.md`. A ring on an ordinary element
  inside a `V.AnimatePresence` fades with its element's enter and exit.
- A variant payload spelled as a **USS class** (`dark:bg-neutral-900`, `md:flex-col`) now overrides a
  base utility writing the same properties regardless of the order the bundled stylesheets declare
  them in. Previously the payload was added to the live class list as a bare utility, where it tied
  with the base on specificity and won or lost purely by source order — so half of every override pair
  was a silent no-op (`bg-white dark:bg-neutral-900`, `w-full md:w-64`, `items-center md:items-start`
  and `flex flex-row md:flex-col` among them). Each element now carries a model of which class every
  priority layer wants, ranked by the precedence the variant already had, and for each USS longhand
  only the highest-priority class holding it stays on the element; the losers come off and return when
  they stop losing, including with several variants of different precedence active at once. Class and
  arbitrary-value payloads are ranked against each other too, so `bg-[#fff] dark:bg-neutral-900` and
  `bg-white dark:bg-[#171717]` both work. A payload only displaces a class whose properties it wholly
  covers, so three shapes still fall to declaration order: two utilities at the same priority; a base
  whose property set is a strict *superset* of the payload's, which the stylesheets order safely
  (`size-8 md:w-4` resolves the width, the shorthand keeping the height); and two sets that merely
  *overlap*, which nothing orders — `rounded-l` and `rounded-t` share one corner and neither contains
  the other, so `rounded-l md:rounded-t` can be a silent no-op. A class Velvet does not ship carries no
  known properties and is never ranked at all. See `Documentation~/styling-variants.md`; the
  direction-override table in `Documentation~/styling-flexbox-and-gap.md` is gone, both halves of every
  pair now working.
- The **important modifier** (`!bg-red-500`, `dark:bg-red-500!`) now applies to class-only utilities,
  where it was previously stripped and otherwise inert — only utilities with an inline form honoured
  it. An important utility beats every non-important one on the same property whatever their
  priorities, and two important utilities fall back to the ordinary ladder, so
  `!bg-blue-500 dark:!bg-red-500` layers like the plain pair instead of the previous last-wins. It
  settles a same-priority tie (`flex-row !flex-col` lays out as a column) and nothing else: an overlap
  that is not containment, and a class whose properties are unknown, are decided by declaration order
  with or without the bang.
- A variant payload naming a class the element already carries no longer takes that class with it when
  it turns off: `gap-4 md:gap-4` keeps `gap-4` below the breakpoint, and so does `dark:gap-4 md:gap-4`
  when either variant deactivates. This previously also misfired for a payload that turns off without
  ever having turned on — the structural, `has-[…]:`, `data-`/`aria-` and `supports-` families evaluate
  an unconditional off — so `"gap-4 first:gap-4"` lost its literal `gap-4` on every child but the
  first. Two payloads of the *same* precedence still share one slot.
- The `Visible` prop's `hidden` class is now ranked with the utilities rather than written past them,
  at the important layer, so an element declared `Visible = false` stays hidden beside an `md:flex`
  payload by decision rather than by the stylesheet's declaration order. `Visible = true` clears only
  the prop's own layer, so a `hidden` written literally in `className` keeps hiding the element.
- A `has-[.foo]:` variant now reflects whether `foo` is on the element rather than whether it was
  written: a `foo` that lost every property it writes to a higher-priority class no longer satisfies
  the condition. This is a deviation from CSS, where `:has(.foo)` tests class-attribute membership and
  a losing declaration never removes the class from the DOM.
- `transition-filter` now declares `transition-property: filter`, so Velvet's tween owns the motion
  outright. Previously it left `transition-property` at its initial whole-property value, under which
  the engine's inline-filter setter runs an inline write as its own animation that no API can cancel:
  every frame the tween wrote restarted that animation over the same value, and what was painted
  lagged the tween that was supposed to drive it. The resolved value is now what decides which
  animator runs — a value naming `filter`, alone or among other property names, leaves the setter on
  its plain direct-write path so the tween can paint its own frames; a whole-property value hands the
  change to the engine and the tween stands down; anything else keeps the change instant, except a
  value naming `background-size` or `-unity-background-scale-mode`, which is what the setter actually
  gates on and so animates a filter change with nothing in the declaration mentioning filters. The tween
  re-checks that value on every frame, so a `transition-property` rewritten mid-tween (a Motion play,
  a class swap) hands over once instead of restarting the engine's animation on every tick. Because
  every `transition-*` utility sets the same property, `transition-filter` does not combine with
  another one — the later-declared utility wins — and it transitions only `filter`.
- A user-authored `filter-[name:args]` custom filter now interpolates under `transition-filter` when
  both sides are the same registered definition with the same number of arguments and matching
  argument types per slot: a color argument cross-fades and a float argument lerps. Previously a user
  custom on either side always forced an instant write. One added or removed on its own now fades
  from the neutral its own `FilterParameterDeclaration.interpolationDefaultValue` declares — the same
  value the engine pads its filter-list transitions with — and another filter may be added or removed
  alongside one that pairs, so `filter-[glow:1]` → `blur-4 filter-[glow:2]` fades the blur in while
  the glow lerps. A user custom still snaps when it cannot be paired: a different definition on each
  side, a differing argument count, a slot whose type differs across the change, and two or more
  distinct user customs when a filter is added or removed (they all compose last, leaving no order to
  place them in). A definition destroyed mid-tween now drops out of the frames the tween paints
  instead of throwing, mirroring how the compose path already skips dead definitions.
- Scheduling a fiber re-render no longer allocates a per-fiber sorted set for the pending-lane
  queue; the four update priorities now live in an inline bit mask with identical enrollment,
  drain-order, and starvation-promotion semantics.
- Recycling a pooled primitive widget (`Label`/`Button`/`Toggle`/`Slider`/`TextField`) no longer
  allocates a class-list array on every pool return; the element reset path now uses fixed-arity
  overloads so steady-state list churn stays allocation-free.
- `UseFrame` dispatch no longer allocates a snapshot array every ticked frame; the dispatcher reuses
  a grow-only buffer guarded against re-entrant ticks, so a panel with stable subscribers ticks with
  zero allocations.
- The `Memoize` props-bail comparison compiles a cached, typed per-props-type comparer on JIT
  platforms instead of reading members through reflection, removing the per-render boxing of
  value-type props while preserving `Object.is` member semantics exactly — including members declared
  as `object`/interfaces holding boxed values, and the sign-of-zero distinction for nullable floats.
  IL2CPP (AOT) players keep the reflection implementation.
- **BREAKING:** The method-level `[Memoize]` attribute is renamed to `MemoizeMethodAttribute`
  (`[MemoizeMethod]`) so it no longer collides in name with the unrelated `ComponentAttribute.Memoize`
  props-bail flag (`[Component(Memoize = true)]`, which keeps its name and behavior unchanged). Migrate
  by replacing `[Memoize]` with `[MemoizeMethod]` on annotated partial methods.

### Fixed

- `text-balance` on an element with horizontal padding or a border no longer wraps the text one line
  further than an unbalanced one. The search measures text, which is laid out inside the content box,
  while the value it writes is a `width`, which in UI Toolkit covers the padding and the border; the two
  were the same number, so the text was handed less room than the search had assumed. Measured on a
  themeless panel, a balanced label with three pixels of horizontal padding came out one line taller than
  its unbalanced sibling at four of five wrapper widths, and the width written was identical to the
  unpadded case. Velvet's own utility model makes a padded text element ordinary — any non-zero `px-*` or
  `p-*` on a balanced label reached this.

- The four shaders behind drop shadows, the gradient silhouette a `bg-gradient-*` gets on a `skew-*`
  element, and the `brightness-*` / `saturate-*` filters are now put in front of every player build.
  They are reached by name from C# alone and none is in a scene, so a build had nothing keeping them:
  `Shader.Find` returned null in a player and those three paints drew nothing, after working in Play
  Mode for the whole life of a project and announced only by a warning in the player log. That they
  resolve from inside a running player has not been observed here — see
  `Documentation~/player-builds.md`. A build step now adds the four to
  Graphics Settings' Always Included Shaders before the build and removes them afterwards, so a
  consumer installs the package and does nothing else and their `ProjectSettings` reads as it did.
  The cost, stated plainly: those four shaders are compiled into every build of every project that
  installs the package, used or not, and a build whose injection does not take now fails rather than
  producing a player that draws nothing. A shader that is missing anyway now names itself in one
  warning for the run; the drop shadow logged one every time a caster regenerated its content.
  [`Documentation~/player-builds.md`](Documentation~/player-builds.md) is new and covers it.
- A `shadow-*` / `drop-shadow-*` paint no longer fades to the square of its caster's opacity during an
  enter or exit. The scheduler scaled the shadow by the caster's sampled opacity each frame on the
  premise that a baked shadow quad ignores UI Toolkit opacity; pixel readback shows the renderer scales
  that quad exactly as it scales the element's other content, so the correction applied the same opacity
  a second time — a card halfway through a fade painted its shadow at a quarter strength rather than
  half, and a staggered `AnimatePresence` list showed it on every row. The per-frame drive is gone; a
  `ring-*` band, which is hosted beside its element rather than inside it, still needs and keeps one.
- Two variants naming one of the utilities Velvet realises itself — `gap-*` / `space-*`, `grid` /
  `grid-cols-*`, `divide-*`, `text-balance`, `skew-*`, `shadow-*`, the gradients, `animate-*`,
  `border-dashed` / `border-dotted` — now rank against each other by the precedence table, and within
  one precedence layer by their order in the className, instead of by the order their signals happened
  to fire. Two variants supplying the SAME utility with no literal base (`"dark:gap-4 md:gap-4"`) kept
  the class on the element but stopped driving it as soon as either turned off, dropping the spacing;
  two payloads of one family (`"bg-white md:shadow-sm dark:shadow-lg"`) resolved to whichever lit
  last, so one end state rendered two ways depending on the path the user took to reach it; and that
  second shape held equally for two payloads the precedence table cannot separate, such as two
  `data-[…]:` rules or two `has-[.class]:` rules on one element. A literal base re-asserted by the
  stronger of two variants (`"shadow-lg dark:shadow-sm hover:shadow-lg"`) was likewise outranked by
  the weaker payload. Only a rule that actually applies takes part in a tie, so adding one that cannot
  — a `lg:` rule below the breakpoint, a `peer-` rule with no peer, a `[&>*]:` rule that lands on the
  children — never moves what the element paints. A stacked variant is ranked by the position of the
  stacked class itself, so `"dark:hover:shadow-lg hover:shadow-sm"` resolves to the later-written
  payload rather than to whichever manipulator attached first.
  See `Documentation~/styling-variants.md`.
- **BREAKING:** A utility whose property set contains another's is now declared before the narrower
  one in the bundled stylesheets, so the narrower utility wins the properties they share. Bundled
  utilities are single-class selectors, so two on one element tie on specificity and the later
  declaration wins; wherever the order was inverted, the narrower utility could never take effect.
  `"size-8 w-4"` laid out 32px wide and now lays out 16px wide, matching the web. The families
  corrected are `size-*` (now before `w-*` / `h-*`), the flex shorthands `flex-1` / `flex-auto` /
  `flex-initial` / `flex-none` (now before `grow-*` / `shrink-*`), the single-edge `mt-auto` /
  `mr-auto` / `mb-auto` / `ml-auto` (now opening their own edge bands, after `mx-auto` / `my-auto`),
  `border-x-*` / `border-y-*` (now before `border-t/r/b/l-*`), the bare `rounded-tl` / `rounded-tr` /
  `rounded-bl` / `rounded-br` corners (now after every scaled side utility), and `.grid` (now before
  `.flex`; both resolve to `display: flex` under UI Toolkit, so nothing renders differently).
  Anything written against the old order — `"size-8 w-4"` expecting 32px — now resolves the other way.
  Three families keep their position because the reference cascade puts them there: the `anim-*`
  presets (scheduler-applied, and meant to outrank the base `opacity-*` / `scale-*` / `transition-*`
  utilities they animate), `truncate` relative to `overflow-*` and `whitespace-*`, and
  `transition-none` relative to the other `transition-*` utilities. `truncate md:whitespace-normal`
  and `transition-colors md:transition-none` are inert as a result.

- **BREAKING:** `gap-*` / `space-*` and `divide-*` on a composite widget picked their physical edge
  from the widget's own flex-direction instead of from the container the spaced children are in. A
  composite widget — `ScrollView`, `Foldout`, `Tab`, `TabView`, `TwoPaneSplitView`, `RadioButtonGroup`,
  `ToggleButtonGroup`, `PopupWindow`, and anything else that redirects — reconciles its children into
  an inner box, so a `flex-row-reverse` / `flex-col-reverse` class on one reverses only the widget's
  own box (a ScrollView's viewport and scrollers, a Foldout's toggle above its content) while the
  content still paints in source order. The gap margin and the divider border moved to the trailing
  edge anyway, leaving every visually adjacent pair unseparated and a margin or a rule stranded on an
  outer edge. Both manipulators now read the direction from the inner box, and which container that is
  is decided by the redirect rather than by a widget type, so it holds for anything mounted through
  `V.Custom<T>`. `flex-wrap` is read from there too, so a `flex-wrap` on such a widget — which wraps
  the widget, not its content — no longer switches the content to the four-side half-margin polyfill,
  whose negative container margin bleeds `gap/2` outward *inside* the widget over the box that clips
  the content. A plain `gap-*` now follows the inner box's direction (vertical for a default
  `ScrollView`). **This changes existing layouts with no compile error**: a class string that used to
  space horizontally may now space vertically, or move its margin to the opposite edge. Plain elements
  parent their own children and are unaffected; nest a plain container inside the widget and put the
  direction or wrap class there when the content needs one.
  Off-panel, an inner box now defaults to the engine's column rather than `.flex`'s row so that the
  off-panel and on-panel answers agree — *except* on a widget whose built-in USS lays its inner box out
  as a row (a horizontally scrolling `ScrollView`, a `TwoPaneSplitView`, a `ToggleButtonGroup`), which
  only a live panel reports. Those now flash for one frame: the first application writes the column
  edge before attach and moves to the row edge on the first geometry event. The manipulators also watch
  the inner box for geometry changes now, since `GeometryChangedEvent` neither bubbles nor trickles.
- Two latent bugs on composite widgets other than `ScrollView`, from the same root: the manipulators'
  "is this element still my child" test compared against the widget rather than the box its children
  are in, and so was permanently false. `gap-*` reset **all four margins on every tracked child** on
  any input change, including the out-of-flow children it must leave alone — visibly shifting an
  `AnimatePresence` exit ghost mid-flight. A skewed container (`skew-x-*` / `skew-y-*`) re-captured the
  *previous* pass's shear as each child's baseline translate, so removing the skew restored a stale
  shear instead of the child's original position.
- **BREAKING:** `"flex flex-col md:flex-row"` — the documented "stack on narrow screens, row on wide
  ones" idiom — laid out as a column at every width. A responsive or state variant adds the BARE
  utility to the live class list, so above the breakpoint the element carried both `flex-col` and
  `flex-row`; both are single-class selectors, and `_layout.uss` declared `.flex-col` after
  `.flex-row`, so the base column won the cascade and the variant row never took effect. The
  direction utilities are now declared column family first (`.flex-col`, `.flex-col-reverse`,
  `.flex-row`, `.flex-row-reverse`), so a variant can turn a column into a row and, within an axis,
  turn a plain direction into its reversed form. The opposite overrides — `"flex flex-row md:flex-col"`,
  `"flex flex-col-reverse md:flex-col"`, `"flex flex-row-reverse md:flex-row"` — need no rewrite, and it
  takes both mechanisms together: the class-payload ranking listed under *Changed* above displaces the
  direction base outright, while the `flex-direction` that `.flex` sets alongside `display` is beaten by
  declaration order instead, since a direction utility holds only part of what `.flex` writes and so can
  never displace it. What the order still settles is the case the ranking leaves open: two direction
  utilities at the **same** priority, where the later declaration wins — so a literal
  `"flex-col flex-row"` lays out as a **row**. See `Documentation~/styling-flexbox-and-gap.md`. The
  `gap-*` polyfill resolves its axis from the same precedence and was updated in lockstep, so spacing
  still follows the axis the container actually renders on.
- A consumer below a `V.Provider` could keep rendering the previous context value indefinitely when the
  set of Providers around it changed in the same render as the value. It showed up wherever a Provider
  shares a parent with something that can add or drop a Provider of its own — a `V.Suspense` whose
  primary subtree provides a value while the fallback is showing, a Provider inside a fallback beside one
  in the primary, or simply a conditional `cond ? V.Provider(...) : null` sibling appearing — and only
  below a memoized boundary (a `[Component(Memoize = true)]` consumer, or one behind `V.Memoized`), since
  an ordinary consumer is re-rendered by the surrounding walk anyway. A theme or locale change while a
  route's data is still loading, with the skeleton widgets memoized, is the everyday shape: the skeleton
  kept the old theme until the fetch resolved. Providers on the two sides of the diff are now matched by
  their position in the tree first, falling back to the previous order-of-appearance matching when a
  position has no counterpart, so a Provider appearing or disappearing elsewhere no longer displaces the
  comparison. A provider's position includes its own sibling index, which a conditional sibling rendered
  as `null` preserves; give it an explicit `key` when the index itself moves, such as a provider appended
  after a variable number of siblings — keying the provider pins its own place among its siblings, so an
  unkeyed fragment or component enclosing it needs a key of its own if that is what moves.
- `divide-x-*` / `divide-y-*` drew their rule on the wrong side of every pair inside a
  `flex-row-reverse` / `flex-col-reverse` container. The divider edge was picked from the axis alone
  and never consulted the container's direction, so the rule between the two visually adjacent
  children was missing while an extra rule appeared along the container's own outer edge. **This moves
  the rules of every reversed `divide-*` container.** An app that compensated for the old placement —
  an extra `border-*` on a child, a spacer, a hand-rolled divider — now gets that compensation *plus*
  the correctly placed rule, and should drop it. Non-reversed containers are unaffected. The direction
  is read from the direction classes first and `resolvedStyle` only as a fallback, so a runtime
  `flex-row` ↔ `flex-row-reverse` toggle converges on the patch itself rather than waiting for a
  geometry event that a flip between two same-size directions never fires. See
  `Documentation~/styling-flexbox-and-gap.md`.
- A `gap-*` / `space-*`, `grid` / `grid-cols-*`, `divide-*` or `text-balance` class reached through a
  variant (`md:grid`, `dark:gap-4`, `hover:divide-y`, `[&>*]:gap-2`, …) had no effect: those four
  utilities are realised by manipulators the reconciler attaches from the class array it reconciled,
  and a variant payload is written straight onto the element's live class list when its signal
  activates, never passing back through the reconciler. So `className="gap-4 md:grid md:grid-cols-3"`
  kept its flex-style gap margins above the breakpoint and was never laid out in columns at all; the
  same shape left `md:gap-4` spacing nothing. Each of the four is now re-derived when a variant
  toggles it, in either direction, so a variant-gated class behaves exactly like a literal one —
  including the ownership handoff between gap and grid (the grid owns its children's margins, and the
  gap manipulator is suppressed for it). One limitation to know, shared with the paint utilities
  below: two variants naming the same one of these utilities with **no literal base**
  (`dark:gap-4 md:gap-4`) keep the class when the first of the two turns off — the class list is
  ranked per priority layer, so the other layer still holds it — but stop driving the manipulator, so
  the spacing goes while `gap-4` is still on the element. A literal base (`gap-4 md:gap-4`) is
  unaffected. The same missing ranking shows a second way: two payloads of one family
  (`md:shadow-sm dark:shadow-lg`) resolve by the order their signals fired rather than by the
  precedence table, so going dark then widening past `md` leaves `shadow-sm` winning while the reverse
  order leaves `shadow-lg` — the same end state rendered two ways. Declaring the base once and letting
  one variant override it (`gap-4 md:gap-8`) hits neither — see `Documentation~/styling-variants.md`.
- A `skew-*`, `shadow-*` / `drop-shadow-*`, gradient (`bg-gradient-*` and its `from-` / `via-` / `to-`
  stops), `animate-*` or `border-dashed` / `border-dotted` class reached through a variant
  (`md:shadow-lg`, `hover:animate-pulse`, `dark:bg-gradient-to-r`, `focus:-skew-x-6`) painted nothing
  until some unrelated re-render happened to bring the bare token in literally. Same root as the layout
  fix above: UI Toolkit has no property for any of these, so Velvet paints them from the class array
  the reconciler reconciled, which a variant payload never passes through. All five now resolve at
  mount and on every toggle in both directions, and they keep the order they compose in on a toggle
  just as on a render — a skewed caster's shadow still follows the sheared silhouette, and a
  co-arriving `border-dashed` still defers to a shadow that owns the face. This covers every variant
  family whose payload is spelled on the element's own class list, which is all of them except
  `[&>*]:`. A `[&>*]:` paint lands inconsistently, because a child is fully built before its container
  applies the rule: `[&>*]:shadow-lg` paints at mount on a child that declares some variant-gated
  payload of its own (any one — `hover:gap-4` suffices), and only from the child's next render on a
  child that declares none. Put the utility on the child rather than relying on either.
- The class channels that are not variants — `whileHoverClass` / `whileTapClass` / `whileFocusClass`,
  the transient enter / exit classes an `AnimatePresence` play applies, and drag-and-drop's
  `whileDraggingClass` / `whileOverClass` / `whileDragActiveClass` — write their utilities onto the
  element without notifying the reconciler, so none of them drive the utilities above either:
  `V.Div(whileHoverClass: "shadow-lg")` toggles a class nothing paints. Use `hover:shadow-lg`. Any
  utility backed by a plain USS rule still works on every channel — the split is that list versus
  everything else, not whole categories, since `gap-4` is spacing and `skew-x-6` is a transform yet
  neither works there. See `Documentation~/styling-variants.md`.
- Losing a `grid` class while keeping a `gap-*` one (`grid grid-cols-3 gap-4` → `flex gap-4`) left the
  children with no spacing at all: the arriving gap manipulator wrote its margins before the departing
  grid manipulator cleared the margins IT had written, and that clear took the new ones with it.
  Whichever of the two is departing now releases its writes first.
- Removing a `text-balance` class from an element whose size comes from a VARIANT
  (`dark:w-[80px] text-balance` with the dark theme active) left the element with no such value at all.
  `text-balance` borrows one inline slot while it is attached and nulls it on detach, and the restore
  re-derived the value by scanning the element's class list — which skips variant tokens, since a
  variant's payload is not a utility of the element itself. Nothing else re-asserts that layer, so the
  value was gone for good. The restore now reads the recorded value directly and no longer cares how the
  token was spelled. A value from the element's own bracket utility was already restored correctly and is
  unchanged.
- `text-balance` no longer violates a declared `max-width`, and no longer destroys a co-present sizing
  utility. It writes the element's inline **`width`** now instead of its `max-width`: the engine clamps
  that write to whatever `max-width` the element declares, so the ceiling holds by construction, and the
  declared `max-width` — no longer the slot balance overwrites — stays readable, so the search is bounded
  by it and the text is balanced *inside* the ceiling rather than cut off by it. Previously the balanced
  width came from the parent's content width alone, so a `text-balance max-w-[120px]` label in a
  380px-wide parent rendered ~284px, and a `max-w-*` utility beside `text-balance` was erased outright
  whenever balance declined to act (empty text, or text that fits one line). Every spelling of a ceiling
  now behaves identically and applies on every pass: `max-w-[…]`, a variant's `dark:max-w-[80px]`, the USS
  scale forms (`max-w-32`, `max-w-full`), and percentages. `max-w-0` releases the box instead of widening
  past it. **Balanced labels carrying a max-width now render narrower**, since they previously rendered
  wider than they asked to be.
- **`text-balance` now stands down on an element that declares its own width.** Any `w-*` or `size-*`
  class — `w-full` included, in every spelling: scale, bracket, fraction, `!`-important, and one a variant
  supplies — leaves the box exactly as declared, as does a child of a `grid` container, whose width the
  grid writes. `w-auto` declares nothing and does not count. Balance is approximated by narrowing the box,
  so a declared width leaves nothing to narrow, and the width is the contract other layout depends on.
  **`w-full text-balance` no longer balances** — drop the `w-full`, since a column child already fills its
  parent. `w-32 text-balance` now renders at 128px rather than at a balanced width. See
  `Documentation~/fonts.md`.
- On an element that already carries a clip — a base `clip-path-*`, or one from a state, theme,
  responsive or relational variant — a `clip-path-*` payload carried by a structural (`first:`),
  `has-[.class]:`, `data-`/`aria-` or `supports-` variant now re-resolves the mask when it toggles.
  UI Toolkit has no `clip-path` property, so the class toggle alone does nothing and the mask has to be
  re-derived from the live class list, which only the first group's own toggles used to do. A clip
  declared ONLY by one of these four families is still inert: nothing gives such an element the wrapper
  the mask is painted into.
- The VEL100 exhaustive-deps analyzer now also covers `Hooks.UseInsertionEffect` and `Hooks.UseBlocker`.
  Both take a closure plus a deps array, but neither was listed as a deps-comparing hook, so a captured
  value missing from their deps went unreported.
- VEL100 now inspects deps-comparing hooks whose lambda takes parameters, which it previously skipped
  outright. This matters most for `Hooks.UseCallback`, whose whole purpose is memoizing the caller's own
  delegate: the everyday `UseCallback<Action<ClickEvent>>(evt => Save(draft), draft)` shape was never
  checked, matching React's `useCallback` only in the parameterless case. `UseBlocker`'s predicate (which
  receives the navigation attempt, and a cancellation token on the async overload) is covered for the same
  reason. A lambda's own parameters are supplied per invocation and are never treated as dependencies, and a
  lambda wider than the hook's widest overload is still not read as that hook's factory.
- `Documentation~/styling-filters.md` claimed that UI Toolkit's transition system cannot repaint an
  inline `filter` at all, and that `transition-all` and `transition-property: filter` therefore leave
  a filter change snapping. Both were wrong: the engine animates an inline filter under a
  whole-property `transition-property`, and pinning the property does not stop it repainting. The
  guide now describes the measured behavior — which of the two animators runs, and why — instead.
- The pooled-element inline-style reset neutralised the transition longhands only at the end of its
  scrub, so on an element still attached to a panel every clear before that point — `filter`,
  `opacity`, the transforms, the colors, the geometry longhands — became a running animation that
  kept painting the value the reset had just removed, and the trailing nulls restored the
  matched-rules value rather than disabling the transition, so nothing cancelled them. The reset now
  writes a zero transition duration before its inline scrub, which makes every clear in that scrub
  cancel instead of animate. No shipped caller could reach the defect: they all detach before
  resetting, and a detached element is not style-initialized, which already takes every clear down
  the cancel path. The write is therefore skipped when the element is off-panel, keeping the
  (always-detached) pool return free of the inline entry the trailing null would then tear down
  again; what changed is that the reset no longer depends on that call ordering.
- `gap-*` (and its `space-x-*`/`space-y-*` aliases) placed its margin on the wrong physical edge
  inside a `flex-row-reverse` / `flex-col-reverse` container: `StyleGapManipulator` folded
  `RowReverse` into `Row` and `ColumnReverse` into `Column` when picking the leading edge, so a
  plain `gap-4` (no reverse marker at all) wrote a leading `margin-left`/`margin-top` that showed up
  as extra space on the container's OUTER edge, with no gap on the visually-adjacent boundary it was
  reversed away from. Off-panel (EditMode, and the pre-attach mount pass), a `flex-col-reverse`
  container's axis itself resolved wrong (the class-marker fallback checked only the literal string
  `"flex-col"`, which `"flex-col-reverse"` never matches), so the gap landed on the wrong AXIS
  entirely, not just the wrong edge. `gap-*` is now direction-correct on a reversed container without
  needing a `space-*-reverse` marker — see `Documentation~/styling-flexbox-and-gap.md` for how
  direction is resolved. This is a visible behavior change for any app that was compensating for the
  old misplaced margin.
- A `FocusScope` with `restoreFocus: true` did not hand focus back to the prior element when the
  scope's ROOT element itself (rather than a descendant) held focus at unmount: the guard used
  `VisualElement.Contains`, which does not count an element as containing itself, so a focused root
  read as "focus already left the scope" and the restore was skipped. The guard now uses a
  self-inclusive check, so a directly-focusable scope root restores focus the same way a focused
  descendant already did.

## [1.6.0] - 2026-07-25

### Highlights

- **`z-0`…`z-50`, `z-[N]` and their negative forms** bring CSS `z-index` to `absolute` descendants,
  compared among siblings under one parent. A no-op on an in-flow element and on `V.Motion` — wrap the
  Motion in a z-managed `Div`.
- **`text-balance`**, approximating CSS `text-wrap: balance` by narrowing the box a wrapped label lays
  out in, so the last line is no longer near-empty.
- **`leading-none`…`leading-loose` and `leading-[Npx]`** (line-height), realised through UI Toolkit's
  rich-text tag, so they compose with any `text-*` size.
- **`whitespace-pre`, `whitespace-pre-wrap` and `whitespace-pre-line`**, filling out the previous
  normal/nowrap pair, and **`overline`**, joining the text-decoration axis.
- **`V.Portal(targetId:)` bubbles `events:` handlers to the logical ancestor chain**, matching what the
  layer and world-space portals already did. Across all three forms, children a later patch adds bubble
  too; on the other two, a handler calling `StopPropagation()` partway up now stops the walk, and one
  portal mounted inside another's content reaches past the inner boundary.
- **`Hooks.UseFrame(priority:)`** (r3f `useFrame` ordering parity), backed by one per-panel dispatcher
  so firing order survives a keyed reorder, and **`V.Anchored(occlude:, distanceFactor:)`**, closing two
  drei `<Html>` gaps.
- Fixed: a variant Motion's resting `variants[animate]` classes survived neither presence interruption
  window, and a wrapped Motion inside an `AnimatePresence` child never had its variants applied at all.
- Fixed: a `Transition`-lane re-render could be starved indefinitely — the anti-starvation clock
  restarted on every coalesced re-signal, and the promotion it eventually made was to a tier that
  sustained `Normal` work still outranks.

### Added

- `z-0`…`z-50`, `z-[N]`, and their negative forms (`-z-10`, `z-[-5]`) — each also accepting the
  important modifier (`!z-10`, `z-10!`, `!z-[5]`, `z-[5]!`, itself a no-op for this utility) —
  bring CSS `z-index` to `absolute` descendants, compared among siblings sharing one direct parent.
  UI Toolkit ties paint order, pointer-pick order, and Yoga flex placement to a single physical
  child list, so physically reordering children for paint would also reorder their layout and
  corrupt the reconciler's own diff of every other sibling on the very next render. Instead, a
  z-marked absolute element's real content relocates into a lazily-created per-stacking-parent
  layer container — one for non-negative z (painted last) and one for negative z (painted first, so
  it still sits behind ordinary siblings, though never behind its own parent's background, which is
  a genuine engine dead end: UI Toolkit has one paint traversal, and a child can only paint after
  its own parent's background) — while a hidden, zero-footprint placeholder holds the element's
  declared slot for the reconciler, structural variants (`first:`/`last:`/`odd:`/`even:`/`nth-child`),
  and Tab order, reusing the Portal placeholder pipeline and `SilhouetteBoundsSpacer`'s
  reconciler-invisible-child convention; a resort's own detach-and-reinsert (a mount-order tie, a
  sign flip, or a patch-time z change) rescues and restores focus when the moving element holds it,
  so interacting with a focused element never silently drops focus. `z-*` on an in-flow
  (non-`absolute`) element is a documented no-op — Yoga has no flex `order` analog, so reordering an
  in-flow child for paint would also reorder its layout position — and so is `z-*` on `V.Motion`
  (now with a warning): a Motion's create path never relocates it, since the element identity its
  own enter/exit tween is bound to must stay put — wrap it in a z-managed `Div` instead, which an
  `AnimatePresence` keyed child built that way (an animated, top-most modal) can do: its enter,
  `PopLayout` exit pin, and exit-cancel all target the real, relocated element for its whole
  lifetime, not a placeholder standing in its declared slot. The `variants` enter/exit classes
  resolve against the wrapped Motion's own element, so the wrapped shape animates identically to a
  direct Motion child. Comparison is scoped to direct siblings under one immediate parent only; there is no CSS
  stacking-context nesting (opacity, transform, filter, and isolation do not open a new context
  here). A `peer-` source that is itself z-managed is not found by a `peer-` consumer (its
  placeholder carries none of its marker classes) — the reverse, a z-managed consumer resolving an
  ordinary `peer-`/`group-` source, works.
- `overline` (text-decoration), joining the existing `underline` / `line-through` / `no-underline`
  decoration axis. UI Toolkit rich text has no overline tag, so unlike the other three (a string rewrite)
  it is PAINTED: a solid rule stroked above the leaf `TextElement`'s first line via
  `generateVisualContent`, colored from `resolvedStyle.color`, sized to the text's natural width (clamped
  to the content box), and honoring both components of `-unity-text-align` — its horizontal start, and,
  for a middle/lower vertical anchor, where that first line actually sits. It cascades and resets
  through the same axis as the others (`no-underline` clears it too), but the axis stays single-valued by
  this subsystem's pre-existing design, so `underline overline` on one element resolves to the last
  token rather than both lines composing as CSS allows. v1 scope: one rule positioned above the first
  line only — a wrapped label's later lines carry no rule of their own yet.
- `text-balance` approximates CSS `text-wrap: balance` on a `TextElement` (Label / Button / …). UI
  Toolkit's text engine exposes no line-break hook, so `StyleTextBalanceManipulator` narrows the box
  instead: it binary-searches the public `TextElement.MeasureTextSize` — the same method the engine's own
  autosize pass already calls — for the narrowest inline `maxWidth` that keeps the measured height at or
  under a normal (unbalanced) layout's height at the same available width, so the existing line count
  redistributes more evenly instead of leaving a near-empty last line. Applied only when the text actually
  wraps to 2+ lines; a single-line label's box is left untouched, matching CSS balance being a no-op
  there. Unlike real `text-wrap: balance`, this approximation can shrink the element's own box (narrowing
  via `maxWidth` rather than only moving line breaks within a fixed box) — the full deviation writeup is
  in `Documentation~/fonts.md`. Needs a wrapping white-space (`text-wrap` / `whitespace-normal`, …)
  alongside it — Velvet's `Label` default is `nowrap`, so `text-balance` alone is a no-op there.
- `V.Portal(targetId:)` (the same-panel registry portal) now bubbles `events:` handlers to the
  logical ancestor chain outside the call site, matching the synthetic bubbling
  `V.Portal(layer:)`/`V.WorldSpace` already had. `PointerDown`/`Up`/`Move`/`Enter`/`Leave`,
  `Wheel`, `KeyDown`/`Up`, and `FocusIn`/`Out` bindings on an `events:` prop now reach a logical
  ancestor's handler the same way in all three portal forms; an element that is both a physical
  ancestor of the resolved target and a logical ancestor of the call site still fires exactly
  once (native bubbling already covers it, so the synthetic walk stops there instead of
  double-firing). `ClickedBinding`/`ChangeEventBinding<T>` (no underlying bubbling event object)
  and `FocusEvent`/`BlurEvent` (UI Toolkit dispatches them target-only, never bubbling to a
  bridge listener) stay physical-tree-only, as documented.
- `Hooks.UseFrame` gains a `priority` parameter — r3f's `useFrame(callback, renderPriority)` ordering
  parity. Lower runs earlier within the same panel; equal priorities fall back to subscription (mount)
  order. Backing this, per-frame callbacks now subscribe to a single per-panel dispatcher instead of
  each scheduling their own engine tick, so firing order also stays stable across a keyed reorder of a
  callback's host, which the previous per-element scheduling did not guarantee.
- `V.Anchored` gains `occlude` and `distanceFactor`, closing two of drei `<Html>`'s parity gaps.
  `occlude: true` hides the element while a solid (non-trigger) collider sits between the camera and
  the target — an opt-in physics query every tick, off by default — via the new `occludeLayerMask`
  (defaults to `Physics.DefaultRaycastLayers`). `distanceFactor` scales the element by that value
  divided by its current camera distance, faking perspective size falloff for otherwise-flat
  screen-space content; it is the reference distance at which scale is exactly 1 and must be positive.
- `whitespace-pre`, `whitespace-pre-wrap`, and `whitespace-pre-line` utilities, filling out the previous
  `whitespace-normal` / `whitespace-nowrap`-only pair. The first two map directly onto
  `UnityEngine.UIElements.WhiteSpace`'s two other native values. `whitespace-pre-line` has no matching
  engine value, so it is realised the same way as `uppercase` / `underline` — a display-string rewrite
  (collapse space/tab runs to one space, keep newlines, drop whitespace sitting at a line edge — a CRLF
  pair or a lone CR counts as a newline too, per CSS's segment-break normalization) plus an inline
  `white-space: pre-wrap` write on every text leaf whose resolved axis is pre-line (not just once on the
  class-bearing element — `Label`/`TextElement`'s own default theme rule for `white-space` always beats an
  inherited value, so only a per-leaf write reaches a descendant Label) so the preserved newlines still
  render as breaks and the text still wraps — and it inherits/cascades the same way `uppercase` /
  `underline` do. An explicit `whitespace-*` class on the same element wins over `whitespace-pre-line`
  there, and — like `normal-case` / `no-underline` — also blocks a farther ancestor's
  `whitespace-pre-line` from reaching that subtree.
- `leading-none`…`leading-loose` and `leading-[Npx]` (line-height). USS has no line-height property, so
  these realise it through UI Toolkit's rich-text `<line-height=X>` tag instead: the named presets emit
  their Tailwind multiplier verbatim as `<line-height=1.625em>`, which the text engine itself resolves
  against whatever font size is in effect at that point in the string — unlike `tracking-*`'s em scale,
  which had to be pre-baked to px at a fixed 16px root, this composes correctly with any `text-*` size at
  any value, with no lookup table. `leading-[Npx]` emits an absolute `<line-height=Npx>`; only the `px`
  unit is accepted inside the bracket, and a malformed or unsupported-unit value is silently ignored.
  Inherits and cascades the same way `uppercase` / `underline` / `whitespace-pre-line` do, with no reset
  form of its own — Tailwind has no `leading-auto`, and every preset (`leading-none` included) is already a
  real value rather than a sentinel.

### Fixed

- A variant Motion's resting `variants[animate]` classes now survive both presence interruption
  windows that used to strip them. Cancelling an in-flight variant enter (an exit starting
  mid-enter) restores the resting classes the enter's strip had removed — previously a
  configuration without an `exit` label played its classic exit on an element missing its resting
  variant. And a re-entry landing after a completed variant exit's class swap but before its drop
  render found the still-attached element parked at `variants[exit]` with nothing restoring it;
  the re-entry now puts the resting pose back — including un-pinning a `PopLayout` exit's
  out-of-flow geometry — before the enter branches run, so it starts from the same state a fresh
  mount would. A completed tween exit also clears its inline `transition-*` styles now (as the
  enter completion always has), so an element that outlives its drop no longer tweens unrelated
  later class changes through the exit's leftover timing.
- A `V.Motion` nested under a transparent wrapper inside an `AnimatePresence` keyed child — a
  z-managed `Div` (the animated top-most modal shape, since `z-*` is a documented no-op on a
  Motion itself) or a `ContextProviderNode` — now has its named `variants` enter/exit classes applied,
  resolved against the Motion's own element, where the resting `variants[animate]` classes live.
  Previously variant resolution required the keyed child itself to be the Motion, so only the
  transition's timing and `onEnterComplete` were honored for the wrapped shape: a wrapped modal
  faded/scaled only when its transition carried timing-preset classes, never its declared
  variants. Exit-cancel (the key re-added mid-exit) likewise restores the resting variant on the
  Motion's own element now. Deliberate consequence: a wrapped Motion no longer also plays the
  classic preset alongside a variant enter/exit — a variant-driven animation manages its state
  through variant classes alone, exactly as a direct Motion child always has (a configuration
  without an `exit` label keeps its classic preset exit on the wrapper, same as before). Style the
  Motion, not the wrapper, for anything that should animate with the variants.
- A `Transition`-lane re-render can no longer be starved indefinitely by sustained higher-priority
  work, on either of two fronts. The anti-starvation clock — which promotes a Transition lane once
  its 30th flush pass still finds it pending — used to restart on every schedule, including a
  coalesced re-add onto the already-pending lane, so a component that kept re-signalling
  transition intent (e.g. a per-frame transition-tier update) kept resetting it and the promotion
  never fired; the clock now starts once when the lane first becomes pending and runs until the
  lane flushes, and only a genuine re-enrol after a drain restarts it. And the promotion itself
  used to hand the lane to `Deferred`, which a sustained stream of `Normal` updates still
  outranks — promoted work could go right back to starving; a starved lane is now promoted to
  `Normal`, draining in the flush that reaches the threshold or right after any co-pending
  Urgent drains, so `useTransition`'s `isPending` also clears at (not before) the commit that
  renders the transition's content — including across that Urgent parking window, which the
  settle sweep used to misread as the transition having settled once promotion erased the
  Transition label. An `isPending` flag held by an **async** `startTransition`
  still awaiting its action is additionally no longer wiped when a drain callback fires on the
  fiber while no lane is pending (the awaiting action has not scheduled its updates yet, so an
  empty lane queue does not mean the transition settled).
- `V.Portal(layer:)`/`V.WorldSpace` synthetic event bubbling now stops when a handler calls
  `StopPropagation()` partway up the logical ancestor chain, instead of continuing to invoke
  every remaining ancestor regardless. Also fixes nested portals/world-space (one mounted inside
  another's content): the outward walk now escapes every enclosing boundary to reach the
  outermost logical ancestor, instead of stopping at the inner boundary's own physical
  target/host root.
- `V.Portal(targetId:)`/`V.Portal(layer:)`/`V.WorldSpace` children added by a patch AFTER the
  portal's first mount now bubble `events:` handlers to the logical ancestor chain too — e.g. a
  `V.Portal` that renders no children until some later state flips true. Previously this only
  worked for children present at the very first mount (or, for `V.Portal(targetId:)`, a registry
  target's one-time late-registration heal); anything mounted by a later patch of an
  already-mounted portal reached its physical ancestors but never its logical ones.
- A time-sliced (`Transition`/`Deferred` priority) reconcile that enqueues a `V.Portal`/`V.WorldSpace`
  mount and then pauses on budget exhaustion no longer has that paused state destroyed by its own
  same-pass drain. The drain resolves the portal's target children through the very same reconciler
  instance, whose entry unconditionally cleared any paused state as though it were leftover from a
  finished pass — silently truncating the list (the remaining rows were never created, with no error)
  instead of resuming once drained.

## [1.5.0] - 2026-07-19

### Highlights

- **`transition-filter`** transitions the filter utilities (`blur-*`, `brightness-*`, …) smoothly when
  they change, matching CSS `transition: filter`. A scheduler tween drives the filter parameters
  frame-by-frame; opt in with the class, which honours `duration-*` and the easing longhand.
- **`TransitionType.Bezier`**, a third transition model sampling an exact CSS
  `cubic-bezier(x1,y1,x2,y2)` curve rather than one of the five `EasingMode` keywords. Defaults to
  Tailwind's own curve, which the bundled USS only approximates.
- **`skew-x-*` / `skew-y-*` now shear their descendants**, not only the caster's painted silhouette —
  the per-row counter-translate a CSS author would otherwise hand-write, applied automatically.
- **The `[&>*]:<utility>` child-combinator variant**, CSS's `& > *` applied to Velvet's utilities
  (`[&>*]:mt-2`, `[&>*]:hover:bg-red-500`).
- **`border-dashed` / `border-dotted`** and their `divide-*` counterparts, stroked by the element itself
  since UI Toolkit has no `border-style`, with the layout gutter a solid border reserves.
- **`brightness-*` and `saturate-*` cover the full CSS range**, each through a first-party custom-filter
  shader, so a Linear project matches the browser instead of over-darkening.
- Fixed: hook-state writes landing in a commit phase were silently discarded, and callback refs
  re-invoked on every render instead of only when their identity changed.
- Fixed: the fiber-tree recycle path stranded one pooled props bag per re-render for every element
  nested below the top level, pinned forever by the pool's ownership tracking.
- Fixed: four Tailwind defaults were off — the default ring colour's alpha, the default transition
  easing, the `tracking-*` scale, and the `checked:` variant's rank against the interaction states.

### Added

- Filter utilities (`blur-*`, `brightness-*`, …) now transition smoothly when they change on an element
  carrying the new `transition-filter` class (e.g. `transition-filter hover:blur-md`), matching CSS
  `transition: filter`. UI Toolkit cannot transition the inline `filter` property natively, so a scheduler
  tween drives the filter parameters frame-by-frame; opt in with `transition-filter` (honoring `duration-*`
  and the easing longhand). The built-in `brightness-*` / `saturate-*` filters interpolate like the others
  even though they render as custom-filter functions; only a user `filter-[name:args]` custom filter, an
  ambiguous add/remove, or the off-panel / zero-duration cases fall back to an instant write.
- A third transition model, `StyleTransitionConfig { Type = TransitionType.Bezier, BezierX1,
  BezierY1, BezierX2, BezierY2 }`: variant enters / exits sample an EXACT CSS
  `cubic-bezier(x1,y1,x2,y2)` curve every tick instead of one of the five `EasingMode` keywords,
  which cannot express an arbitrary numeric curve. Sibling to the spring model — it shares its
  channel scope (opacity and the translate/scale/rotate transform trio) and its
  one-curve-drives-both-directions contract — but keeps a fixed `DurationSec` like a plain tween;
  only the easing shape differs. Defaults to Tailwind's own default curve,
  `cubic-bezier(0.4, 0, 0.2, 1)`, the exact curve the bundled USS only approximates with the
  `ease-in-out` keyword. `BezierX1`/`BezierX2` outside `[0,1]` is invalid per the `cubic-bezier()`
  spec (a timing function must stay monotone in time) and falls back to that default curve with a
  one-shot console warning instead of being silently clamped into range.
- `skew-x-*` / `skew-y-*` now approximate CSS `skewX()` / `skewY()`'s **descendant shear**, not only the
  caster's own painted silhouette. UI Toolkit's transform has no shear, so each in-flow direct child is given
  an inline `translate` that seats its centroid where the shear would carry it — the per-row counter-translate
  a CSS author would otherwise hand-write, applied automatically. The seat re-runs on child add / remove /
  reorder and as layout settles; it is exact at each child's centroid and piecewise-constant across the child
  (a real shear also rotates it), so a child large relative to the frame reads slightly off at its far corners
  and a nested transform on the child is not composed. Out-of-flow children (`.absolute`, a `PopLayout` exit
  ghost, the filter bounds-spacer) hold no seat and are skipped, and a child's own static `translate-x-*` /
  `translate-y-*` is preserved when the parent later loses its skew — including a translate the child acquires
  only after it moves out of flow, which is released untouched rather than reset to its pre-shear value.
- The `[&>*]:<utility>` child-combinator variant, CSS's `& > *` "every direct child" rule applied to
  Velvet's utility classes (`[&>*]:mt-2`, `[&>*]:mt-[8px]`, `[&>*]:hover:bg-red-500`): the wrapped
  utility — a plain class, an arbitrary value, or a state variant — is applied to every direct,
  in-flow child of the element that carries the token, instead of the element itself. Runs before
  `gap-*` / `divide-*` / `grid-cols-*`, so those still own a margin/border/width edge they also set.
- `border-dashed` / `border-dotted` border styles and their `divide-dashed` / `divide-dotted` divider
  counterparts. UI Toolkit has no CSS `border-style`, so a non-solid border is drawn by the element's own
  `generateVisualContent`: an arc-length marcher strokes the rounded-rect outline as dash / dot runs, the
  native border color is masked with a near-invisible sentinel, and the border WIDTH is left untouched so the
  box reserves the same layout gutter a solid border would (`border-2 border-red-500 border-dashed` composes
  as CSS width + style + color). A `divide-x` / `divide-y` divider paints the same dashed / dotted stroke on
  each divided child's own leading edge, layout-identical to a solid divider; `border-solid` / `divide-solid`
  reset to the plain native border. When the same element is also skewed or shadowed that layer owns the whole
  face and repaints a solid border, so a dashed border there stays solid — a documented limitation, the same
  tier as the clip + shadow / clip + ring mutual exclusions.
- `brightness-*` and `saturate-*` now cover the full CSS range, matching Tailwind. Each renders through a
  first-party custom-filter shader (`Velvet/FilterBrightness`, `Velvet/FilterSaturate`) bound as a
  `FilterFunctionType.Custom` definition, rather than the previous approximations (`brightness` through the
  built-in Tint, `saturate` as `grayscale(1 - N)`) that clamped to the darken / desaturate range. The
  over-bright presets `brightness-105/110/125/150/200` and over-saturate presets `saturate-150/200` are now
  recognized, and the bracket forms `brightness-[N]` / `saturate-[N]` accept any `N >= 0` (only negative
  amounts are rejected, as CSS disallows them). The shaders apply the multiply / lerp-toward-luminance on
  the encoded pixel before the engine's Linear-colorspace conversion, so a Linear project matches the
  browser exactly instead of over-darkening.

### Fixed

- A parent's layout-effect (`Hooks.UseLayoutEffect`) cleanup now runs before an inline child's layout-effect
  setup when both re-run on one commit, matching React's all-cleanups-before-all-setups across the whole
  subtree — previously that held only within a batch of inline siblings, and a parent committed after its
  inline children were fully committed (their setups included), so a parent cleanup could read state a child
  setup had just written. The inline-effect drain now splits into a cleanup pass and a setup pass with the
  parent interleaved between them; a layout effect that mounts more inline children commits them as a
  follow-up pass, as React runs effect-mounted work in a subsequent commit rather than the current one.
- A `checked:` variant value no longer beats a concurrent `hover:` / `focus:` / `active:` value on the
  same property. Tailwind's variant order emits `checked` before the interaction states, so on a hovered
  checked control the interaction state wins the tie; the layer priority now ranks `checked` below them
  to match (it previously ranked highest).
- A prior commit's pending passive effect (`Hooks.UseEffect`) now runs BEFORE a discrete event's
  re-render, matching React's flush-passive-effects-before-update: a click handler that re-renders no
  longer commits its render ahead of an effect that has not run yet. Scoped to the discrete-event
  boundary, so a mount / commit-phase flush still leaves passive effects pending for the scheduler tick.
- The default ring color (`ring` / `ring-2` with no explicit `ring-<color>`) is now blue-500 at 0.5
  alpha, matching Tailwind's `--tw-ring-color`, instead of fully opaque. An explicit ring color stays
  opaque.
- The default `transition-*` timing function is now `ease-in-out`, the closest UI Toolkit keyword to
  Tailwind's default `cubic-bezier(0.4, 0, 0.2, 1)`, instead of the fast-start `ease-out`. An explicit
  `ease-*` class still overrides it.
- The `tracking-*` (letter-spacing) scale is baked at Tailwind's 16px root font (`tracking-widest` =
  0.1em → 1.6px, etc.) so it matches Tailwind at the default text size, instead of the previous
  ~25%-too-wide values.
- A `skew-*` sheared silhouette and a `shadow-*` / `drop-shadow-*` bleed no longer clip to the layout
  rect when the same element carries an inline filter (`blur-*`, `hue-rotate-*`, `animate-hue`, or a
  variant such as `hover:blur-sm`). A filter renders the element through an offscreen tree sized to its
  layout box, which dropped the paint drawn outside that box; a transparent, non-interactive spacer
  child sized to the paint's extent now widens the element's render bounds so the overflow survives,
  matching how CSS composes `filter` with `transform: skewX()` and `box-shadow`.
- `V.Particles` quads that draw beyond the host rect survive an inline filter the same way, tracked to
  the live particle extent as the simulation moves; the reserved bounds return to the box when the
  effect drains and are skipped entirely when no filter is present.
- The filter bounds-spacer now offsets itself by the caster's border width (parsed from the class list,
  so state borders like `hover:border-8` count too), so an element whose border is thicker than the
  paint's overhang no longer clips a strip of the sheared silhouette / shadow / particle overflow.
- Callback refs follow React's re-invocation contract: a ref cycles (cleanup, then setup) only
  when its callback identity changes or the host element remounts — a patch carrying the same
  delegate no longer re-invokes it on every render, so a reference-stable ref (`Hooks.UseCallback`)
  installs once and its cleanup means the element is genuinely going away. `Ref<T>.SetElement` is
  now an identity-stable delegate for the Ref's lifetime (a method group converted to a fresh
  delegate per render, so the object-ref pattern could never benefit from the gate), hook calls
  from commit-phase code fail fast with the invalid-hook-call error instead of corrupting the slot
  cursor, and reconciler disposal detaches every still-installed ref that a diverged teardown
  skipped.
- Hook state writes landing in the COMMIT phase of the same fiber's flush (a callback ref invoked
  during a patch, an event dispatched from a detach) are no longer silently discarded: the
  render-phase-update window now covers only the component body, so commit-phase writes schedule an
  ordinary follow-up render — and the flush keeps draining until the queue is quiet (React's
  setState-in-commit semantics) whichever entry point ran it: the frame drain, a delayed-tier
  drain, a discrete-event flush, or the initial mount. Runaway commit-phase loops hit React's
  maximum update depth (50); the overflow logs an error and DROPS the runaway update instead of
  throwing — a throw here cannot reach an error boundary and a deferred runaway would re-arm every
  frame, while a drop keeps every other component's work alive. Dropped writes used to desync the
  slot value from the committed UI and poison the setter's equality bail for the next genuine edge
  with the same value; `Hooks.UseFocusRing` sheds its deferred-correction workaround accordingly
  (its cleanup writes the flags directly; when composing its `Ref` with other per-element work,
  wrap the composed lambda in `Hooks.UseCallback` — a fresh-identity ref cycling per patch is the
  same re-render feedback an inline ref writing state produces in React).

- The fiber-tree recycle path now returns factory-rented props bags / event arrays / child arrays
  from EVERY nesting level of a retired tree — previously only the top level was recycled, so any
  props-carrying element nested under another element (or under `V.Portal` / `V.WorldSpace` /
  `V.Suspense` / provider children) stranded one pooled bag per re-render, pinned forever by the
  pool's ownership tracking. The recycle is a mark-and-sweep: nodes still reachable from committed
  state are spared, so renders that legitimately share node instances keep their baselines intact.
  The live roots cover the committed and parked baselines, hook-slot-held node roots (compiler
  auto-memo slots, plus `Hooks.UseMemo` / `UseState` / `UseRef` values that are a node or a list of
  nodes) along the LOGICAL ancestor chain (portal-drained fibers hop back to their declaring
  component), provider values, and exiting `AnimatePresence` ghosts (whose nodes presence
  bookkeeping re-reads until the exit completes). Holding a factory-built node anywhere else across
  renders — inside a user record or tuple, a component props record, a `Store` — is outside the
  tracked surface and documented as unsupported.
- Pooled-object lifetime hardening around the same recycle path: pool returns are idempotent
  (rent-scoped ownership) and pass-deferred (a mid-pass return cannot be re-rented within the same
  reconcile pass, so a second retirement of a shared node can never recycle a NEW renter's live
  object); an aborted reconcile no longer recycles the retained baseline's own pooled parts; a
  fiber unmounting mid-pass reclaims its deferred baselines while its mark roots are intact; a
  replaced `V.Memoized` inner tree, a replaced VNode-valued provider value, and the memo cache's
  disposal now retire their cached subtrees; an `AnimatePresence` child retires when it leaves the
  presence set (exit completion, instant removal, mid-exit re-entry); a disposed fiber retires its
  element-in-state roots (unmount keeps them for a remount); discarded render-phase attempts retire
  their throwaway output; and the editor-only StrictMode double-invoke pass neither recycles
  committed subtrees a memo hit shared into its diagnostic tree nor stages that tree into the
  auto-memo slot. `V.DragOverlay`'s positioner props now come from the pool too (the workaround
  for the old leak).

## [1.4.0] - 2026-07-17

### Highlights

- **A focus / gamepad navigation layer** (React Aria parity): `V.FocusScope` with `contain` /
  `restoreFocus` / `autoFocus` / `singleTabStop`, `TabIndex` / `DelegatesFocus` element props, and
  `Hooks.UseFocusRing` — composing with the engine's own focus ring and spatial 2D navigation rather
  than reimplementing them.
- **Drag and drop** (dnd-kit core parity): `V.DndContext` / `V.Draggable` / `V.Droppable` /
  `V.DragOverlay`, with pluggable collision detection and activation constraints that keep clicks
  working on a draggable control.
- **Cross-panel input routing** for layer and world-space panels: `events:` bindings bubble
  synthetically across the panel boundary, overlapping layers are arbitrated by `sortingOrder`, and a
  world-space host gets the collider Unity's input system needs to pick it.
- **Cross-panel Tab order**: `V.Portal(layer:)` / `V.WorldSpace` accept
  `focusOrder: PanelFocusOrder.Chained` (iframe semantics) as the explicit cross-panel focus escape.
- **`V.Anchored(target:)`** — drei `<Html>` parity: a screen-space element tracking a 3D transform.
- **`Hooks.UseAnimationSequence`** (Framer `useAnimate` timeline parity) and **`V.Motion(layoutId:)`**
  shared-element FLIP animation.
- Fixed: a pooled `Button` / `Slider` / `TextField` / `Toggle` silently lost its focusability on reuse
  and dropped out of Tab and gamepad navigation entirely.

### Added

- Drag-and-drop primitives, dnd-kit core parity: `V.DndContext` (the scope — `onDragStart` /
  `onDragOver` / `onDragEnd` / `onDragCancel` callbacks, a pluggable `DndCollisionDetection`
  delegate with `DndCollisions.RectIntersection` / `ClosestCenter` / `PointerWithin` built-ins,
  and a scope-wide activation default), `V.Draggable(id:)` (activation constraints defaulting to
  4 px of travel so clicks keep working on draggable controls; inline-translate or stay-put
  movement; `whileDraggingClass:`), `V.Droppable(id:)` (`whileOverClass:` /
  `whileDragActiveClass:`; live-rect collision, so mid-drag layout shifts are picked up
  automatically), and `V.DragOverlay` (a portal-rendered, picking-ignored preview that tracks the
  pointer on the Overlay layer). Escape cancels; drop/cancel callbacks commit state synchronously
  like click handlers; a real drag ending on a Clickable source suppresses its `clicked` and
  settles the press-derived `whileTap`/`active:` styling synthetically; everything a session
  writes (capture, inline translate, classes) is restored on drop, cancel, and teardown —
  including a source unmounting mid-drag, whose user cancel callback is deferred past the flush.
- Focus / gamepad navigation layer, React Aria parity — composing with (never reimplementing)
  the engine's own focus machinery:
  - `V.FocusScope(contain:, restoreFocus:, autoFocus:, singleTabStop:)` (and the same knobs as a
    `FocusScope` element prop on any container): scoped Tab containment with same-flush snap-back
    for spatial/pointer exits, focus restore on unmount, mount autofocus, and the WAI-ARIA
    composite-widget single-tab-stop (roving) contract — engine spatial 2D navigation inside a
    group stays untouched.
  - `TabIndex` / `DelegatesFocus` element props (with the documented engine trap that -1 removes
    an element from BOTH the Tab ring and 2D navigation on runtime panels).
  - `Hooks.UseFocusRing`: keyboard/gamepad-visible focus (vs pointer focus) as re-rendering
    component state, riding the same element-local heuristic as the existing `focus-visible:`
    styling variant.
  - `V.Portal(layer:)` / `V.WorldSpace` accept `focusOrder: PanelFocusOrder.Chained` to join the
    declaring panel's Tab order at the call site (iframe semantics) — the explicit, opt-in
    cross-panel focus escape; `Isolated` (default) keeps today's internal wrap.
  - All sequential interception rides one pinned engine contract (a TrickleDown
    NavigationMoveEvent listener + `FocusController.IgnoreEvent` deterministically preempts the
    post-dispatch default move), tripwired by dedicated PlayMode tests.
- `V.Anchored(target:)`: drei's `<Html>` parity in its default screen-space projection mode — a
  plain 2D element whose `left`/`top` track a 3D scene Transform's projected position every frame
  via `RuntimePanelUtils.CameraTransformWorldToPanel`. Not depth-tested against scene geometry
  (unlike `V.WorldSpace`, which renders content INTO the 3D scene): ordinary screen-space UI,
  positioned dynamically. Forces `position: absolute`; hides itself while the target is behind the
  camera rather than jumping to a wrong spot. Raycast-based occlusion (drei's `<Html occlude>`) is
  an explicit scope cut, not yet implemented.
- `Hooks.UseAnimationSequence(steps:)`: Framer Motion's `useAnimate` timeline parity — plays an
  ordered `AnimationSequenceStep` array (`To` label changes, `Wait` gaps, `Call` callbacks) over
  time and exposes the active step's label/transition to feed straight into a coordinator
  `V.Motion(animate:, transition:)`, so a multi-stage animation no longer needs to be hand-rolled
  with `UseEffect` + a timer + `UseState`. Descendant Motions inherit the coordinator's label
  exactly as they already do for any hand-toggled label, so "animate several elements one at a
  time" is just `StaggerChildrenSec` on a step's own transition — no new reconciler wiring.
  `autoplay` / `loop` / `deps` and imperative `Play`/`Pause`/`Restart` controls round out the API.
- `V.Motion(layoutId:)`: Framer Motion's shared-element layout animation parity. When a Motion
  carrying the same `layoutId` string patches at a resolved layout rect different from the rect
  that id last settled at — including across a same-key type flip or a move to a different
  parent — it tweens from the old rect to the new one (FLIP: capture, invert, spring back to
  zero) instead of jump-cutting. Reuses the existing spring physics driver; scoped to uniform
  scale (UI Toolkit's `scale` style has no independent X/Y factor).
- Cross-panel input routing for `V.Portal(layer:)` and `V.WorldSpace`: a layer or world-space
  host panel is a wholly separate UI Toolkit `Panel` from the panel its content logically
  belongs to, so native input delivery, propagation, and focus were previously scoped entirely
  per-panel. Now:
  - `events:` bindings (`PointerDown`/`Up`/`Move`/`Enter`/`Leave`, `Wheel`, `KeyDown`/`Up`,
    `FocusIn`/`Out`/`Focus`/`Blur`) bubble synthetically across the panel boundary to the
    logical ancestor chain, mirroring React's own root-level event delegation.
  - Overlapping screen-space layer panels are arbitrated explicitly by `sortingOrder` using each
    panel's own `IPanel.Pick()`, since Unity's own runtime input system's arbitration isn't
    reliable enough to depend on for this.
  - `V.WorldSpace` hosts get an automatically-sized `BoxCollider` so Unity's own runtime input
    system can pick and route pointer input into them (the panel-local coordinate APIs that look
    like the natural tool for this, `RuntimePanelUtils.ScreenToPanel`/
    `CameraTransformWorldToPanel`, are actually for a different, older workflow and don't apply
    here).
  - A focusable element inside a host panel is tracked correctly by that panel's own
    `FocusController`, and a host torn down while it holds focus hands focus back to the main
    panel instead of leaving it dangling. Automatic Tab/Shift-Tab focus chaining across panel
    boundaries is intentionally not implemented — see the portals guide.

### Changed

- `V.SceneView`: the owned RenderTexture's backing resolution now rounds its larger axis up to the
  nearest 16px step (rescaling the other axis by the same factor, so the texture's aspect ratio
  still matches the element's) instead of matching the element's laid-out pixel size exactly, so
  small, rapid resizes that keep the element's aspect ratio unchanged (a drag-resize, an animated
  layout) reuse the existing texture instead of reallocating on every change.

### Fixed

- A `Button`/`Slider`/`TextField`/`Toggle` recycled through the element pool silently lost its
  focusability (the pool's common reset scrubs `focusable` to the plain-VisualElement default,
  which is false, and nothing restored the type's own constructor default) — a recycled control
  dropped out of Tab/gamepad navigation entirely. The type-specific pool resets now restore it.
- A `ComponentNode` nested inside a tree reconciled via a direct `Reconciler.Reconcile()` call
  (rather than `V.Mount`) no longer bootstraps its own isolated `ReconcilerContext`. Its fiber now
  always joins the context of the `ComponentRegistry` that created it, instead of deriving one from
  `fiber.Parent?.Reconciler?.Context` — which resolved to nothing whenever nothing had yet been
  pushed onto the shared `FiberStack` (any hand-authored tree that reconciles directly instead of
  through `V.Mount`). The gap silently detached the nested fiber from the caller's own registries
  and `IsAborted` flag, so an error boundary nested this way could catch its child's exception and
  render a fallback, but the caller's own reconcile pass never observed the abort and kept
  processing later siblings as if nothing had failed.
- `ChildReconciler`'s same-key type-flip replacement (the Common-phase indexed loop, and both keyed
  Pass-1 linear scans — sync and time-sliced) now always inserts the newly built replacement element
  even when building it triggers an error-boundary abort, instead of discarding it and leaving the
  slot empty — the abort only fires once the boundary's fallback has already rendered successfully,
  so the replacement being discarded was always holding valid content. The fully-synchronous keyed
  diff also now stops scanning the remaining siblings once such an abort is observed, instead of
  continuing to patch/replace later slots — matching every other `CanPatch`-gated call site (the
  Common-phase indexed loop and the time-sliced keyed scan already did).

- `V.VirtualList`: a same-key item whose node type changes across a re-render (e.g. a slot
  swapping from `V.Label` to `V.SceneView` while keeping the same key) is now created fresh
  instead of patched onto the old element — the fast path was missing the type-compatibility
  check the general keyed reconcile path already applies before reusing an element.

## [1.3.0] - 2026-07-13

### Highlights

- **`V.Portal(layer:)`** — framework-managed screen-space layer panels (`Background` / `Overlay` /
  `Topmost`) sorted around the app's main panel, created lazily and destroyed with the tree.
- **`V.WorldSpace(position, rotation, panelSize)`** — children rendered into a world-space panel
  positioned by a scene transform and depth-tested against scene geometry. Display-only in this
  release.
- **`V.SceneView(camera)`** — a Camera's output as an element (`<canvas>` parity). The framework owns
  the RenderTexture, sizes it to the element and releases it on unmount.
- **`V.Particles(effect)`** — a ParticleSystem's live simulation drawn as textured quads inside the
  element, with no camera, RenderTexture or render-pipeline coupling.
- **`Hooks.UseFrame(dt => …)`** — a per-frame callback that always invokes the latest render's closure,
  so per-frame data flows without touching component state.
- **A custom filter registry**: `VelvetFilters.Register("dissolve", definition)` exposes a Unity 6.3
  filter shader to class strings as `filter-[dissolve:0.4]`, with variant layering and the same
  transition behaviour as any other filter.
- Fixed: an error boundary whose own fallback content threw could escape uncaught, recurse into itself,
  or falsely report the original exception as caught.

### Added

- `V.Portal(layer:)`: framework-managed screen-space layer panels (`UILayer.Background` /
  `Overlay` / `Topmost`) sorted around the app's main panel — one host per layer per mounted
  tree, created lazily, copying the declaring panel's theme and scale when resolvable,
  destroyed with the tree, and kept in sync with the declaring panel's settings. The shared
  portal semantics apply: context and state cross the logical boundary; events,
  relational variants and focus-within do not, and responsive breakpoints evaluate per panel.
- `V.WorldSpace(position, rotation, panelSize)`: children rendered into a framework-owned
  world-space panel positioned by a scene transform — depth-tested against scene geometry (the
  screen-space layers always composite over the scene), following position/rotation updates,
  destroyed on unmount. Display-only in this release (no world-space input routing). A portals
  guide (`Documentation~/portals.md`) covers all three portal forms and the shared boundary
  semantics.

- `Hooks.UseFrame(dt => …)`: a per-frame callback (elapsed seconds) that runs while the
  component stays mounted and stops on unmount. The latest render's closure is always the one
  invoked — re-renders swap the callback without re-subscribing — so per-frame data flows
  without touching component state.
- `V.Particles(effect)`: a ParticleSystem's live simulation drawn as textured quads inside the
  element — no camera, no RenderTexture, no render-pipeline coupling. The framework clones the
  effect into a hidden host (renderer disabled, source untouched), plays it per
  `playOn: PlayTrigger.Mount | Manual`, maps world units to element pixels via
  `pixelsPerUnit`, and destroys the host on unmount or effect swap. Simulation-module features
  only (one texture per system, local space, up to 2048 particles); VFX Graph and
  renderer-module features route through `V.SceneView` composition — a guide
  (`Documentation~/particles.md`) documents both paths and the decision matrix.

- `V.SceneView(camera)`: a Camera's output as an element (`<canvas>` parity). The framework
  owns the RenderTexture — created at the element's laid-out size (times `resolutionScale`),
  resized with the element, assigned to `camera.targetTexture` while mounted, and released on
  unmount (a user-reassigned camera target is left intact). The output arrives through the
  element's background image, so `rounded-*` / `border-*` and sizing utilities compose with
  it, and the element samples the live texture — camera motion needs no re-render. A guide
  (`Documentation~/scene-view.md`) documents the contract.

- Custom filter registry: `VelvetFilters.Register("dissolve", definition)` exposes a Unity 6.3
  `FilterFunctionDefinition` (custom filter shader) to class strings as `filter-[dissolve:0.4]` —
  colon-separated arguments parsed by the declaration's parameter types (floats / colors) and
  padded from the declaration defaults, composed into the one inline `filter` list after the
  built-in filter utilities, with per-name variant layering
  (`hover:filter-[dissolve:0.9]` restores the base arguments on hover-off) and the same
  transition behavior as any other filter change. A filters guide
  (`Documentation~/styling-filters.md`) documents the built-in utilities and the registry.

### Fixed

- `V.Portal(targetId:)` target lifecycle: a live portal keeps the target its children mounted
  into when the id is re-registered (re-registration routes future portals only), and a portal
  mounted before its target registered heals on its next patch and records the healed target —
  previously a re-registration could diff one portal's slot range against another element's
  children, and a healed mount could leak its cleanup.
- Deferred portal mounts whose subtree rolled back before the drain (a suspended Suspense
  primary, an interrupted pass) are skipped instead of mounting content for a subtree that no
  longer exists.
- An error boundary's abort no longer discards the layer/world-space portal mounts its own
  fallback enqueued in the same pass — an error toast rendered by a fallback now reaches its
  layer — while the failed subtree's pending portals still never mount.
- `Hooks.UseFrame` ticks once per frame (a fixed 16 ms interval previously skipped frames above
  ~60 FPS) and contains callback exceptions the way effects do: routed to the nearest error
  boundary instead of escaping into the panel's scheduler update.
- `V.SceneView`: class-driven backgrounds (gradients, `bg-[addr:…]`) and `styles:` posters no
  longer clobber a live camera feed — the camera owns the background while its texture is
  live, other writers defer and are restored on release; a `camera.targetTexture` reassigned
  by user code survives layout-driven resyncs, not just unmount; the texture-size ceiling
  preserves the aspect; and a pixel-density change re-derives the texture on editor panels.
- `V.Particles` simulates outside Play Mode (editor preview panels previously repainted one
  frozen frame), parks its repaint tick on the drawn root's own liveness, and rate-limits its
  advisories per source name so an unstable effect reference cannot repeat them per rebuild.
- Layer and world-space hosts re-copy the declaring panel's configuration when it changes at
  runtime (theme swaps, scale changes, the ConstantPhysicalSize DPI pair) and survive a scene
  unload killing a host: patches skip dead records instead of throwing out of the pass.
- An error boundary whose own fallback content throws while rendering no longer escapes
  uncaught or recurses into itself — it declines and propagation continues to the next
  ancestor boundary, the same as a fallback factory that throws.
- `AnimatePresence`'s `onExitComplete` no longer escapes into UI Toolkit's scheduler update
  when it throws: the exception is contained and routed to the nearest error boundary, and the
  ghost-drop re-render it sits beside still runs.
- An error boundary whose own fallback content fails no longer falsely reports the original
  exception as caught — it now correctly propagates to the next ancestor boundary, which no
  longer stops short of that ancestor when the failed attempt disposed everything in between,
  runs its fallback exactly once for the whole cascade rather than once per exception, and no
  longer leaks a stale entry on the shared fiber stack when that disposal happens mid-attempt.

## [1.2.0] - 2026-07-12

### Highlights

- **Opt-in spring physics**: `StyleTransitionConfig { Type = TransitionType.Spring, Stiffness, Damping,
  Mass }` drives variant enters and exits with a velocity-preserving integrator, so an interrupted
  spring retargets from where it is instead of restarting.
- **`V.AnimatePresence(mode: AnimatePresenceMode.PopLayout)`** — an exiting child is pinned out of flow
  at its last laid-out rect so its siblings reflow immediately (Framer's `mode="popLayout"`).
- **Standalone mount enters**: a `V.Motion` outside `AnimatePresence` plays its `initial` → `animate`
  enter on mount, matching Framer, where `initial` / `animate` work on any `motion.*` element.
- **Orchestration for plain variant propagation**: `StaggerChildrenSec` / `DelayChildrenSec` / `When` on
  a parent Motion's transition stagger its inheriting children with no `AnimatePresence` boundary.
- **Runtime variant swaps ride the Motion's own transition config**, so a changed `animate` label tweens
  with no `transition-*` utilities required — Framer parity, where `transition` applies to every update.
- **Per-property transition overrides**: `StyleTransitionConfig.PropertyOverrides` gives individual USS
  properties their own duration, easing and delay within one variant transition.
- **A Motion & AnimatePresence guide** (`Documentation~/motion.md`).
- Fixed: a classic tween enter could snap straight to its end pose on a runtime panel, and `gap-*` /
  `grid-cols-*` / `divide-*` counted absolutely-positioned children in their spacing.

### Added

- Standalone mount enters: a `V.Motion` outside `AnimatePresence` now plays its `initial` →
  `animate` variant enter on mount (Framer parity: `initial` / `animate` work on any `motion.*`
  element).
- `V.AnimatePresence(mode: AnimatePresenceMode.PopLayout)`: an exiting child is pinned out of
  flow at its last laid-out rect so siblings reflow immediately (Framer's `mode="popLayout"`);
  the `gap-*` / `grid-cols-*` / `divide-*` emulations skip the pinned ghost in their index math.
- Per-property transition overrides: `StyleTransitionConfig.PropertyOverrides` gives individual
  USS properties their own duration / easing / delay within one variant transition, with
  completion sized off the slowest overridden property.
- Orchestration for plain variant propagation: `StaggerChildrenSec` / `DelayChildrenSec` /
  `When` on a parent Motion's transition stagger its inheriting children without an
  `AnimatePresence` boundary (`When = AfterChildren` warns and falls back to `Together`).
- Opt-in spring physics: `StyleTransitionConfig { Type = TransitionType.Spring, Stiffness,
  Damping, Mass }` drives variant enters / exits with a velocity-preserving integrator — an
  interrupted spring retargets from its current value and velocity instead of restarting.
- Runtime variant swaps ride the Motion's own transition config: a mounted Motion whose
  `animate` label changes — directly or through label inheritance, including every orchestrated
  stagger child — now tweens (or springs) on its `StyleTransitionConfig`, with no `transition-*`
  utilities required (Framer parity: `transition` applies to every animate update). Pass
  `transition: StyleTransitionConfig.None` for an instant swap.
- A Motion & AnimatePresence guide (`Documentation~/motion.md`): variants and label
  inheritance, enters / exits, `PopLayout`, orchestration, per-property overrides, springs, and
  the one-config-every-update transition semantics.

### Changed

- Orchestrated stagger slots now delay the child's class swap itself instead of pre-swapping the
  classes behind an inline `transition-delay`: the target classes land when the slot elapses,
  and the swap then plays on the child's own config.

### Fixed

- A classic (tween) enter could snap straight to its end pose on a runtime panel: the class
  swap now defers one nominal frame so the from-state survives a style pass and the transition
  actually fires.
- `gap-*` / `grid-cols-*` / `divide-*` spacing no longer counts absolutely-positioned children:
  an out-of-flow child neither receives inter-child margins nor shifts its siblings' spacing.

## [1.1.0] - 2026-07-11

### Highlights

- **`data:` / `aria:` parameters on every element factory** that takes a class string, so
  `data-[...]:` / `aria-[...]:` styling no longer needs a hand-built `FiberElementProps`.
- **The `transition-*` utilities bundle a default duration and easing**, matching Tailwind's
  standalone-utility contract: a property change with no `duration-*` class beside it now animates.
- Fixed: a duplicate key among new siblings silently dropped a row and desynced the committed child
  count, and an inline component displaced by a keyed reorder could insert a permanent duplicate.
- Fixed: a render that threw — a routine Suspense re-suspend included — discarded an earlier commit's
  pending `UseEffect` work and the fiber's context dependencies, which could detach a memoized consumer
  from its Provider forever.
- Fixed: several element-pool leaks and mis-returns, including a `V.Custom<T>` subclass of a poolable
  primitive being recycled with its constructor-wired callbacks still live.
- Fixed: `Nullable<T>` values compare by value in the identity comparer, so an unchanged `int?`-selected
  store slice no longer re-renders its subscribers on every unrelated update.
- Fixed: stacked variants (`dark:hover:` and friends) keep a continuously-held hover, focus or active
  across the outer condition closing and reopening.
- Reworked the preview window's zoom and resolution handling.

### Added

- `data:` / `aria:` parameters on every element factory that takes a class string (and
  gesture-class parameters where they were missing), so `data-[...]:` / `aria-[...]:` styling no
  longer requires a hand-built `FiberElementProps`.

### Changed

- Reworked the preview window's zoom / resolution handling (device-resolution viewport and
  fit-to-window no longer break layout).
- The `transition-all` / `transition-opacity` / `transition-colors` / `transition-colors-scale` /
  `transition-colors-scale-opacity` utilities now bundle a default `transition-duration`
  (`var(--duration-normal)`, 0.15s) and `ease-out` timing, matching Tailwind's standalone-utility
  contract. Property changes that previously snapped (no `duration-*` class alongside) now
  animate; explicit `duration-*` / `ease-*` classes still override.

### Fixed

- Keyed reconciliation: a duplicate key among new siblings warns and mounts a fresh element
  instead of silently dropping a row and desyncing the committed child count; duplicate sibling
  keys on inline components warn and skip instead of double-emitting one fiber's DOM with shared
  hook state.
- An inline component displaced by a keyed reorder no longer inserts a permanent duplicate element
  when it later re-renders from its own state.
- A render that throws (including a routine Suspense re-suspend) no longer discards an earlier
  commit's still-pending `UseEffect` work, and no longer drops the fiber's recorded context
  dependencies — a memoized consumer could stay detached from Provider updates forever.
- The StrictMode double-invoke diagnostic no longer corrupts the committed tree of a
  directly-built `V.Mount(root, V.Div(...))`.
- Orphaned nested components run their effect cleanups bottom-up (child before parent), matching
  the commit-phase deletion order.
- Element pools: returns dispatch on the exact runtime type, so a `V.Custom<T>` subclass of a
  poolable primitive can no longer be recycled into the shared pool with its constructor-wired
  callbacks still live; a ring-*/clip-path-wrapped widget is reclaimed on ordinary removal; Outlet
  container registrations are released per element instead of accumulating until dispose; and
  caller-supplied `props:` bags / event arrays are never cleared and recycled (pool-ownership
  tracking, mirroring the children-array pool).
- `Nullable<T>` values compare by value in the identity comparer, so an unchanged `int?`-selected
  store slice no longer re-renders its subscribers on every unrelated update, and equal-value
  `UseState` sets bail as intended.
- A store listener that re-entrantly pushes a newer value no longer leaves the remaining
  listeners' final delivery on the superseded value.
- Route blockers no longer transition to Blocked for a registration disposed mid-check or for a
  navigation attempt already superseded by a newer one.
- Stacked variants (`dark:hover:` and friends) keep a continuously-held hover / focus / active
  across the outer condition closing and reopening — a theme or breakpoint toggle no longer
  requires a physical re-hover.
- Arbitrary `rgb()` / `rgba()` color values honor the underscore-for-space convention
  (`bg-[rgb(0,_0,_0)]` parses like `rgb(0, 0, 0)`).
- AnimatePresence: an exiting non-last child holds its slot for the whole exit instead of jumping
  behind its later siblings; cancelling an exit blends back from the element's current value (the
  transition survives the cancel, and a declared `initial` is never replayed on re-entry); and
  `initial` / `exit` on a Motion outside AnimatePresence warns instead of being silently inert.
- Auto-memo weaver soundness: `UseMemo` participates in positional-slot accounting, open
  virtual / interface dispatch outside the BCL/Unity carve-out bails instead of caching unsoundly,
  hooks inside do-while loops are detected, and assembly-resolution failures surface as
  diagnostics instead of silently leaving every `[Component]` unwoven.
- Reconciler teardown: outlet route scopes are disposed on whole-reconciler teardown,
  dropdown / radio choices reset when the prop returns to null, and the inline filter list that
  survives a Null reset is emptied.
- Styling: drop-shadow texture eviction routes through the play-mode-aware destroy helper, the
  gradient class gate can no longer false-negative on parser-accepted shapes, and
  `StyleSlotRecipe.Apply` no longer allocates per call.

## [1.0.0] - 2026-07-05

### Highlights

- **The initial public release of Velvet**, a React-style declarative UI framework for Unity UI Toolkit.
- **A virtual DOM and reconciler** with lane-based priority scheduling.
- **React-parity hooks** — `UseState`, `UseReducer`, `UseEffect`, `UseLayoutEffect`, `UseCallback`,
  `UseMemo`, `UseContext`, `UseTransition`, `UseDeferredValue`, `UseId`, `UseRef` and
  `UseImperativeHandle`.
- **Velvet-only hooks** — `UseService`, `UseBlocker`, `UseMutation` and `UseStore` — over a
  Zustand-style `Store` with selector-based reactive binding.
- **Utility-first styling**: `StyleUtilities`, `StyleClassNames`, `StyleRecipe` / `StyleSlotRecipe`, and
  an arbitrary-value resolver.
- **Compile-time memoization**: a source generator plus an IL post-processor for static expansion.
- **Minimum supported Unity is 6000.3 (Unity 6.3 LTS)**, matching what the bundled USS actually uses.

### Added

- Initial public release of **Velvet** — a React-style declarative UI framework for Unity UI Toolkit.
- Virtual DOM and reconciler with lane-based priority scheduling.
- React-parity hooks: `UseState`, `UseReducer`, `UseEffect`, `UseLayoutEffect`, `UseCallback`,
  `UseMemo`, `UseContext`, `UseTransition`, `UseDeferredValue`, `UseId`, `UseRef`, `UseImperativeHandle`.
  `UseTransition` returns `(isPending, startTransition)`, matching the element order of React's
  `[isPending, startTransition]`.
- Velvet-only hooks: `UseService`, `UseBlocker`, `UseMutation`, `UseStore`.
- Zustand-style `Store` with selector-based reactive binding.
- Utility-first styling: `StyleUtilities`, `StyleClassNames`, `StyleRecipe` / `StyleSlotRecipe`,
  and an arbitrary-value resolver.
- Source Generator-driven memoization (`[Memoize]`, `[Component(Memoize = true)]`) and an
  IL post-processor for static expansion.

### Changed

- **Minimum supported Unity raised to 6000.3 (Unity 6.3 LTS).** The bundled USS uses properties
  added in Unity 6.3 (e.g. `aspect-ratio`), so the declared minimum now matches actual usage
  (`package.json`, READMEs, `_animations.uss`).
- Align nullable reference type contracts across Runtime (`#nullable enable`); eliminate CS86xx
  compile warnings. Tests, Editor, and CodeGen use `-nullable:annotations`.

### Removed

- Removed non-functional USS utilities that target properties UI Toolkit does not support at
  runtime:
  - `z-base` / `z-overlay` / `z-modal` / `z-tooltip` and the `--z-*` tokens — USS has no `z-index`;
    use sibling order or `VisualElement.BringToFront()` / `SendToBack()` instead.
  - `cursor-link` / `cursor-arrow` / `disabled-cursor-arrow` — USS cursor keywords are Editor-only
    and inert at runtime; use a cursor texture or `UnityEngine.Cursor.SetCursor`.

### Fixed

- Preserve `StyleAttributeVariantClass` presence matching for `data-[key]:` variants (do not coerce
  to empty-string equality).
- `V.When` throws `ArgumentNullException` when the condition is true but the factory is null.
