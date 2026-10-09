### Added

- Nine-slice background utilities over UI Toolkit's `-unity-slice-*` properties: `slice-[N]` (and
  `border-image-slice`'s two-, three- and four-value forms, `slice-[12_8_4_2]`) and its `slice-x-` /
  `slice-y-` / `slice-t-` / `slice-r-` / `slice-b-` / `slice-l-` edges set the insets, `slice-scale-[N]` the
  scale, and `slice-sliced` / `slice-tiled` the fill mode. They take variants and the `!` modifier, and on a
  field control they go to the input box with the other background utilities. The new
  `styling-backgrounds.md` guide covers them.
- Tailwind's `bg-repeat`, `bg-no-repeat`, `bg-repeat-x`, `bg-repeat-y`, `bg-repeat-round` and
  `bg-repeat-space` utilities.
- `StyleOverrides.BackgroundRepeat`, `UnitySliceTop`, `UnitySliceRight`, `UnitySliceBottom`,
  `UnitySliceLeft`, `UnitySliceScale` and `UnitySliceType`, for the same properties computed at runtime. The
  slice insets and the scale win over a `slice-*` utility writing the same property.

### Fixed

- A pooled element no longer carries `-unity-slice-*` values onto its next consumer.
- When an `animate-gradient` or `animate-shimmer` pan stops, the element's inline `background-repeat` comes
  back instead of being cleared.
