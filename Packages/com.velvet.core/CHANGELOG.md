# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Filter utilities (`blur-*`, `brightness-*`, …) now transition smoothly when they change on an element
  carrying the new `transition-filter` class (e.g. `transition-filter hover:blur-md`), matching CSS
  `transition: filter`. UI Toolkit cannot transition the inline `filter` property natively, so a scheduler
  tween drives the filter parameters frame-by-frame; opt in with `transition-filter` (honoring `duration-*`
  and the easing longhand). Non-interpolable changes (a custom filter, or an ambiguous add/remove) and the
  off-panel / zero-duration cases fall back to an instant write.
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

### Changed

- `V.SceneView`: the owned RenderTexture's backing resolution now rounds its larger axis up to the
  nearest 16px step (rescaling the other axis by the same factor, so the texture's aspect ratio
  still matches the element's) instead of matching the element's laid-out pixel size exactly, so
  small, rapid resizes that keep the element's aspect ratio unchanged (a drag-resize, an animated
  layout) reuse the existing texture instead of reallocating on every change.

### Fixed

- `V.VirtualList`: a same-key item whose node type changes across a re-render (e.g. a slot
  swapping from `V.Label` to `V.SceneView` while keeping the same key) is now created fresh
  instead of patched onto the old element — the fast path was missing the type-compatibility
  check the general keyed reconcile path already applies before reusing an element.

## [1.3.0] - 2026-07-13

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
