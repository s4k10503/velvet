### Added

- Every spacing utility takes a bare number the way Tailwind v4 does: any non-negative multiple of 0.25 written
  in canonical decimal form is that many 4px steps, so `p-13`, `gap-4.5`, `w-0.25` and `-top-4.5` resolve where
  only the `--space-*` steps did. The utilities are padding, margin, `gap-*`, `space-x-*` / `space-y-*`, `w-*`,
  `h-*`, `size-*`, `min-*` / `max-*` width and height, `basis-*`, the insets and logical insets, and
  `translate-x-*` / `translate-y-*`, with a minus sign on the margin, inset, translate and space ones. A step the
  bundled stylesheets already declare stays a class; the others resolve inline, as a bracket value does.
- `leading-<n>` takes the same steps (`leading-13` is an absolute 52px line height), and a named leading is read
  first.
- `z-<n>` takes any whole number (`z-15`, `-z-15`), not only 0, 10, 20, 30, 40 and 50.
