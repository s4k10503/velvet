### Changed

- Two bundled utilities that a stylesheet class resolves, and that write the same property, are ordered as Tailwind
  emits them rather than by where the stylesheet declared them: the one Tailwind emits later wins, whichever is written first. `w-px w-4` and
  `w-4 w-px` both give 1px (they gave 16px), `p-1 p-px` gives 1px, `bg-red-500 bg-blue-500` gives red (it gave
  blue), `text-sm text-lg` gives the small size, `items-center items-start` gives start, `flex-wrap flex-nowrap`
  wraps, `rounded-md rounded-lg` gives `rounded-md`, `transition-colors transition-opacity` transitions opacity,
  and `transition-none` follows every other `transition-*`. `truncate` now yields to `overflow-*`, `whitespace-*`
  and `text-ellipsis` where it used to outrank them. The `anim-*` presets keep their place after the base
  utilities, since the animation scheduler applies them to outrank those. Utilities Velvet resolves inline in C# are
  not part of this: the font facets (`font-light font-bold`, `not-italic italic`) still go to the last class written.
  A label with `truncate overflow-visible` (or `overflow-hidden md:overflow-visible` above `md`) is no longer treated as
  clipped by the text item's automatic minimum size.
- The bundled partials were rearranged to declare that order: the semantic colours (`bg-primary`, `text-subtle`,
  `border-default`, …) are in `_palette.uss` beside the palette colours of their property, `truncate` is in
  `_layout.uss`, the `transition-*` rules are in `_effects.uss`, and the `font-*` weight rules are one rule each.
  `StyleUtilities.uss` imports the same partials as before.
- The gap polyfill, the shadow's corner radius and the ranking of several `text-<size>` classes on one element follow
  the same order, so they agree with the box: `flex-wrap flex-nowrap gap-4` takes the wrap path, `rounded-sm rounded-lg`
  casts the 2px shadow, and the size whose font size shows is the one Tailwind emits last.
