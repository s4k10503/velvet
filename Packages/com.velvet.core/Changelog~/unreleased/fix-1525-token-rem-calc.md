### Fixed

- A theme token written in `rem` or in a CSS math function (`--spacing: 0.25rem`, `--breakpoint-3xl: 120rem`,
  `--radius-card: calc(...)`) now resolves as Tailwind v4 reads it, with 1rem as 16px. UI Toolkit's importer used to reject a `rem` in a custom property and leave the whole file empty, and to drop a
  math function, so tokens had to be written in `px`. Velvet's importer, which `Velvet.Editor` selects in the meta file of
  a USS file that needs it, stores those custom properties as the text they were written with.
