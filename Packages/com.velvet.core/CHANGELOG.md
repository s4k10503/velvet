# Changelog

All notable changes to this package are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- `ChildReconciler`'s same-key/same-index type-flip replacement path now checks for an
  error-boundary abort (and leaves the old element in place) before removing it, instead of after
  — a descendant render failure caught during the replacement's construction no longer strands the
  slot empty. Applies to the Common-phase indexed loop and both keyed Pass-1 linear scans (sync and
  time-sliced); the fully-synchronous keyed scan additionally now stops scanning the remaining
  siblings once an abort is observed, matching every other CanPatch-gated call site.

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
