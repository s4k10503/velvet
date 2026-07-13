# Velvet documentation

Design and usage guides for **Velvet**, a React-style declarative UI framework for Unity UI Toolkit.
For the package overview and installation instructions, see the repository root README.

## Guides

| File | Contents |
|------|----------|
| [react-migration.md](react-migration.md) | Naming alignment and API mapping for developers coming from React |
| [memoization.md](memoization.md) | The `[Memoize]` attribute — usage, constraints, and diagnostic IDs (Source Generator-driven partial-method memoization) |
| [styling-flexbox-and-gap.md](styling-flexbox-and-gap.md) | Flexbox direction (`flex` defaults to column, not row) and `gap-*` gotchas (single-axis, trailing margin) vs Tailwind |
| [styling-variants.md](styling-variants.md) | The variant set (state / theme `dark:` / responsive `sm:`…`2xl:` / relational `group-`·`peer-` / stacked) and container queries (`@container`, the `container-type: inline-size` equivalent) |
| [styling-filters.md](styling-filters.md) | Filter utilities (`blur-*` … `sepia-*`, the UITK-imposed brightness/saturate ranges) and the `VelvetFilters` custom filter registry (`filter-[name:args]`) |
| [scene-view.md](scene-view.md) | `V.SceneView` — a camera's output as an element (`<canvas>` parity): the framework-owned RenderTexture contract, styling composition, live sampling |
| [portals.md](portals.md) | Portals three ways — registry targets, framework-managed screen-space layers (`V.Portal(layer:)`), and depth-tested world-space panels (`V.WorldSpace`) — plus the shared boundary semantics |
| [particles.md](particles.md) | `V.Particles` (a hidden ParticleSystem simulation drawn as in-element quads — no camera, no render-pipeline coupling), the VFX-Graph-via-SceneView decision matrix, and the `UseFrame` per-frame hook |
| [motion.md](motion.md) | `V.Motion` / `V.AnimatePresence` Framer Motion parity: variants & label inheritance, mount enters, exits & `PopLayout`, `staggerChildren` / `delayChildren` / `when` orchestration, per-property overrides, springs, and the one-config-every-update transition semantics |
| [preview-tooling.md](preview-tooling.md) | Editor-time preview suite (the Storybook equivalent): `[VelvetPreview]` stories, `[VelvetPreviewSetup]` environments, the Controls / Viewport / Theme / Backgrounds / Zoom / Outline / Measure addons, and registry-driven screenshot capture |
| [fonts.md](fonts.md) | Font utilities (`font-<family>` / weight scale / `italic`), the `VelvetFonts` registry (representation-agnostic), Addressables fonts, and CJK fallback |

The React API quick reference (Hooks / Zustand / JSX → V.* / lifecycle / styling) is consolidated in
[react-migration.md](react-migration.md).
