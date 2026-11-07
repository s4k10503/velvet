### Added

- A custom property a stylesheet declares in a Tailwind v4 theme namespace (`--color-brand`, `--radius-pill`,
  `--text-*`, `--font-*`, `--font-weight-*`, `--tracking-*`, `--leading-*`, `--shadow-*`, `--inset-shadow-*`,
  `--drop-shadow-*`, `--blur-*`, `--aspect-*`, `--spacing`, `--spacing-*`, `--container-*`, `--breakpoint-*`,
  `--animate-*`) generates the utilities of that namespace: `bg-brand`, `border-b-brand`, `ring-brand/50`,
  `rounded-pill`, `3xl:`, `@wide:`. `--color-*: initial` and `--*: initial` remove the bundled utilities of a
  namespace. The bundled semantic colours take the families the palette names, so `border-b-primary`, `ring-primary`
  and `bg-primary/50` resolve, and `max-md:`, `min-3xl:`, `max-[600px]:` and `min-[600px]:` are variants ranked as
  Tailwind orders them. See `Documentation~/styling-colors.md`.
