# Velvet documentation

Design and usage guides for **Velvet**, a React-style declarative UI framework for Unity UI Toolkit.
For the package overview and installation instructions, see the repository root README.

## Guides

| File | Contents |
|------|----------|
| [react-migration.md](react-migration.md) | Naming alignment and API mapping for developers coming from React |
| [memoization.md](memoization.md) | The `[Memoize]` attribute — usage, constraints, and diagnostic IDs (Source Generator-driven partial-method memoization) |
| [deep-nest-mitigation.md](deep-nest-mitigation.md) | DX patterns for deeply nested `V.*` trees (component splitting + `StyleSlotClasses` typo prevention) |
| [styling-flexbox-and-gap.md](styling-flexbox-and-gap.md) | Flexbox direction (`flex` defaults to column, not row) and `gap-*` gotchas (single-axis, trailing margin) vs Tailwind |
| [styling-variants.md](styling-variants.md) | The variant set (state / theme `dark:` / responsive `sm:`…`2xl:` / relational `group-`·`peer-` / stacked) and container queries (`@container`, the `container-type: inline-size` equivalent) |
| [preview-tooling.md](preview-tooling.md) | Editor-time preview suite (the Storybook equivalent): `[VelvetPreview]` stories, `[VelvetPreviewSetup]` environments, the Controls / Viewport / Theme / Backgrounds / Zoom / Outline / Measure addons, and registry-driven screenshot capture |
| [fonts.md](fonts.md) | Font utilities (`font-<family>` / weight scale / `italic`), the `VelvetFonts` registry (representation-agnostic), Addressables fonts, and CJK fallback |

The React API quick reference (Hooks / Zustand / JSX → V.* / lifecycle / styling) is consolidated in
[react-migration.md](react-migration.md).
