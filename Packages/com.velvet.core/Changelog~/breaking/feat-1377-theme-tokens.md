### Added

- A custom property a stylesheet declares in a Tailwind v4 theme namespace (`--color-brand`, `--radius-pill`,
  `--text-*`, `--font-*`, `--font-weight-*`, `--tracking-*`, `--leading-*`, `--shadow-*`, `--blur-*`,
  `--spacing-*`, `--inset-shadow-*`, `--drop-shadow-*`, `--aspect-*`) generates the utilities of that namespace: `bg-brand`, `border-b-brand`, `ring-brand/50`,
  `rounded-pill`. A project token with a built-in's name overrides it for the per-side, ring, shadow and gradient
  forms the palette resolved from a fixed table. The bundled semantic colours take the same families, so
  `border-b-primary`, `ring-primary` and `bg-primary/50` resolve. A project that declares `--spacing` makes numeric spacing
  utilities its multiples, and `--velvet-initial-<namespace>: 1` removes the bundled utilities of a namespace. Each
  mount reads the sheets at or above its target on every render pass. See `Documentation~/styling-colors.md`.
