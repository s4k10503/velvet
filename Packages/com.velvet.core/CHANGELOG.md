# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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

### Changed

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

- `StyleVariantClass.BreakpointPx` and `StyleVariantClass.IsResponsive` throw for a `StyleVariantKind`
  value naming no member of the enum, where they returned `0f` and `false`. Both have done so since
  2.0.1.

### Fixed

- A route whose optional literal the URL did not carry no longer writes that segment into the match's
  `PathnameBase`. `Route("docs/intro?")` at `/docs` gave a base of `/docs/intro`, so `UseNavigate()`
  and `V.Link` resolved every relative target one segment deeper than the address the user was
  at — and nested, where the parent base is what `..` pops to, the wrong address still matched the
  parent, so the render looked right while each further hop compounded the error. The base is now
  built from the segments the match consumed, which is what an absent optional param and an absent
  splat already did.

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

- An `IRouteScope.Dispose` that throws is reported the same way, at each of the three places a route
  scope is disposed: the patch that swaps in a newly matched route, an unmounting `V.Outlet`, and the
  whole-reconciler teardown sweep. The scope is the application's, built by the `IRouteScopeFactory`
  handed to `Router`. From the route change the escape left the `V.Outlet` blank — the route navigated
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

## [Unreleased — breaking]

### Added

- `V.Portal(target, …)` takes the element itself, the way `createPortal` does — no registration, no
  shared name, and an element from a `refCallback` is a valid container. Passing a different target on a
  later render moves the children — an unmount and a remount, so their state does not survive. The
  portals guide states which containers a portal of either form may target.

### Changed

- The container a `V.Component` is written into is part of which instance it is, as the position of a
  component is in React. Two sibling containers each holding the same component now hold two instances
  with their own state, where they used to share one: the shared instance rendered its output into
  whichever container the reconcile reached last, so the other container held a copy that no later
  render ever updated, and a control inside that copy drove the surviving instance's state. Giving the
  two occurrences the same `key:` shared one instance too; a key now makes no difference between
  containers, because it separates siblings of one container rather than one container from another.
  The consequence to read before upgrading is the other direction: writing a component into a
  **different** container than the previous render did is now a fresh mount there and an unmount of
  the one it left, so its state, refs and effects do not travel. The entry below states the same of a
  portal's boundary, which holds even where the two sides share a container, so neither rule subsumes
  the other. Keeping state across such a move means lifting it above both containers, to a `Store` or
  to a `UseState` in the component that declares them. The migration guide states what a position is.

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
  the delayed tier. `await UniTask.CompletedTask` reaches it, as does any `async UniTask` the action
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

### Removed

- `FiberUpdatePriority.Deferred`. Source naming it stops compiling; `FiberUpdatePriority.Transition`
  is what to write instead — the same delayed tier, the same fixed flush delay and the same
  time-sliced frame budget, plus the starvation promotion and `isPending` bookkeeping `Deferred`
  never took part in. Nothing in the framework ever scheduled it: every lane Velvet enrols is
  `Urgent`, `Normal` or `Transition`, and `UseDeferredValue` — whose name suggests otherwise —
  derives through `Transition`. `Transition` is now `2` where it was `3`, so a value kept as an
  `int` reads differently; no public member takes or returns the enum, so nothing else changes
  shape.

### Fixed

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
  the two. What still does not follow React is a conditional wrapped in an element of its own —
  `cond ? V.Div(V.Component(Row)) : null` beside a second such `V.Div` — where the element diff patches
  the surviving wrapper onto the leaving one's element and the component inside it re-binds along with
  it. A `key:` on those wrappers matches each with itself; the migration guide says so.

- A `TabIndex` or `DelegatesFocus` prop that a later render stopped declaring was written back as `0`
  or `false` rather than as the value the element was constructed with — neither of which is the
  right answer for every type. A `Label` is built out of the tab ring at -1, which shows once a
  consumer declares it focusable; a `TextField`, `Toggle` or `Slider` is built delegating focus to
  the input beneath it, so dropping that prop stranded focus on the field's own root. `Focusable` already restored its
  constructed value; all three do now.

- `RouteBlockerState.Proceed()` now runs the blocked navigation again, from the request the caller made,
  so the confirm-dialog flow it exists for reaches the destination: the user clicks "Leave" and the
  router goes there. It used to clear the state and invoke a callback nothing assigned, leaving the
  dialog closed and the navigation gone — reaching the destination meant copying `Attempt.NextPath` and
  `Attempt.NavigationMode` out before calling `Proceed()`, re-issuing them by hand, and arranging for the
  predicate to stop blocking, because a re-issued attempt was put to that predicate again. Code written
  around that is what makes this a break: the hand-rolled re-issue now runs on top of the one `Proceed()`
  performs. What is re-issued is the navigation the caller asked for, so a blocked Back or Forward
  resumes as the same history step, and one a Guard redirected takes the redirect again from that step
  rather than committing the redirect target over the entry the user was standing on.
  `RouteBlockerStatus` has a third member, `Proceeding`, for the span between `Proceed()` and the
  re-issued navigation settling: over it the Blocker still reports its `Attempt` and is consulted about
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
