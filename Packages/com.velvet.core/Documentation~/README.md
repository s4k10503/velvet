# Velvet documentation

Design and usage guides for **Velvet**, a React-style declarative UI framework for Unity UI Toolkit.
For the package overview and installation instructions, see the repository root README.

## Start here

Import the **Starter App** sample from the package's Samples section in Package Manager, open
`StarterApp.unity` and press Play. It is the shortest path to a running screen: a `UIDocument`, a
`PanelSettings`, the stylesheet call and `V.Mount`, already assembled, over a two-route app with a
store, hooks, hover variants and transitions. Its own `README.md` maps each file to the part it plays.
The guides below go deeper on one subject each; [setup.md](setup.md) is the one to read next.

## Guides

| File | Contents |
|------|----------|
| [setup.md](setup.md) | Getting the bundled utility stylesheet onto a panel — `VelvetStyleUtilities.AttachTo`, which utilities stop working without it and which are unaffected because Velvet resolves them itself (with the command that answers it per class), and the scene-reference alternative |
| [react-migration.md](react-migration.md) | Naming alignment and API mapping for developers coming from React, including what a component's position is — the container it is written into is part of it — when a `refCallback:` attaches, and which `V.TextField` parameters are undeclared rather than reset when null |
| [memoization.md](memoization.md) | The `[MemoizeMethod]` attribute — usage, constraints, and diagnostic IDs (Source Generator-driven partial-method memoization) |
| [styling-flexbox-and-gap.md](styling-flexbox-and-gap.md) | Flexbox direction (the engine's raw flex default is a column; the `.flex` utility corrects it to row), what still ties once a variant outranks the base direction (the bare `flex`, two utilities at one priority), and `gap-*` / `divide-*` gotchas vs Tailwind — the inter-child margin and border polyfills, their edge flip on `flex-row-reverse` / `flex-col-reverse` (and the `space-*-reverse` / `divide-*-reverse` markers), the wrap half-margin hybrid, and the `grow-[N]` / `shrink-[N]` arbitrary factors a proportional split needs |
| [styling-z-index.md](styling-z-index.md) | `z-*` stacking for `absolute` descendants — the layer-container + placeholder mechanism, sibling-scope-only comparison, and the in-flow/negative-z/`peer-` deviations from CSS |
| [styling-variants.md](styling-variants.md) | The variant set (state / theme `dark:` / responsive `sm:`…`2xl:` / relational `group-`·`peer-` / stacked), how the semantic token set is selected beside the `dark:` variant (`:root` / `.dark`, bound to `VelvetTheme.IsDark`), the precedence order a class or arbitrary-value payload is ranked by against the base utility, the `!` important modifier, the arbitrary pivot (`origin-[x_y]`), which payloads Velvet realises itself (hence where `ring-*` deviates from CSS, what an element's own hidden overflow (`overflow-hidden` or `truncate`) costs each painted utility, and the limits on the `while*Class` channels), and container queries (`@container`, the `container-type: inline-size` equivalent) |
| [styling-filters.md](styling-filters.md) | Filter utilities (`blur-*` … `sepia-*`, the shader-backed full-range `brightness-*`/`saturate-*`) and the `VelvetFilters` custom filter registry (`filter-[name:args]`) |
| [drag-and-drop.md](drag-and-drop.md) | `V.DndContext` / `V.Draggable` / `V.Droppable` / `V.DragOverlay` — the dnd-kit parity guide: activation constraints, pluggable collision detection, and callback-driven drag state |
| [focus.md](focus.md) | `V.FocusScope` and the `FocusScope` element prop — the React Aria parity guide: `contain` / `restoreFocus` / `autoFocus` / `singleTabStop`, `Hooks.UseFocusRing`, and cross-panel Tab order (`PanelFocusOrder`) |
| [scene-view.md](scene-view.md) | `V.SceneView` — a camera's output as an element (`<canvas>` parity): the framework-owned RenderTexture contract, styling composition, live sampling |
| [routing-blockers.md](routing-blockers.md) | Navigation blocking (`Hooks.UseBlocker`, React Router's `useBlocker` parity guide): the `Idle` / `Blocked` / `Proceeding` states, what `Proceed()` re-issues and what `Reset()` abandons, when a Blocker is armed again, and what several Blockers on one attempt do |
| [portals.md](portals.md) | Portals four ways — an element you hold, registry targets, framework-managed screen-space layers (`V.Portal(layer:)`), and depth-tested world-space panels (`V.WorldSpace`) — the shared boundary semantics, and `V.Anchored` screen-space elements tracking a scene Transform (drei `<Html>` parity, `occlude` / `distanceFactor`) |
| [particles.md](particles.md) | `V.Particles` (a hidden ParticleSystem simulation drawn as in-element quads — no camera, no render-pipeline coupling), the VFX-Graph-via-SceneView decision matrix, and the `UseFrame` per-frame hook |
| [motion.md](motion.md) | `V.Motion` / `V.AnimatePresence` Framer Motion parity: variants & label inheritance, mount enters, exits & `PopLayout`, `staggerChildren` / `delayChildren` / `when` orchestration, per-property overrides, springs, the channels the spring/bezier drivers animate, the one-config-every-update transition semantics, and the looping `animate-*` utilities |
| [preview-tooling.md](preview-tooling.md) | Editor-time preview suite (the Storybook equivalent): `[VelvetPreview]` stories, `[VelvetPreviewSetup]` environments, the Controls / Viewport / Theme / Backgrounds / Zoom / Outline / Measure addons, and the registry-driven headless screenshot-capture pattern |
| [player-builds.md](player-builds.md) | What the package adds to a built player — the bundled shaders behind drop shadows, skewed gradients and the shader-backed filters, the build step that puts them in and takes them out again, the utility stylesheet and the holder that carries it, and what each costs every consumer |
| [fonts.md](fonts.md) | Font utilities (`font-<family>` / weight scale / `italic`), the `VelvetFonts` registry (representation-agnostic), Addressables fonts, CJK fallback, and Tailwind text-utility parity (the `overline` / `whitespace-pre-line` / `leading-*` / `text-balance` polyfills and their limits, plus what UI Toolkit has no property for) |

The React API quick reference (Hooks / Zustand / JSX → V.* / lifecycle / styling) is consolidated in
[react-migration.md](react-migration.md).
