### Added

- Nine-slice background utilities over UI Toolkit's `-unity-slice-*` properties: `slice-[N]` (and
  `border-image-slice`'s two-, three- and four-value forms, `slice-[12_8_4_2]`, with an optional `fill`) and
  its `slice-x-` / `slice-y-` / `slice-t-` / `slice-r-` / `slice-b-` / `slice-l-` edges set the insets — in
  pixels or as a percentage of the background image's size on that axis (`slice-[25%]`, read as at most
  100%), re-resolved when the image changes — `slice-scale-[N]` the scale, and `slice-sliced` / `slice-tiled` the fill mode (UI Toolkit tiles only a
  Sprite whose Mesh Type is Full Rect). They take
  variants and the `!` modifier, and on a field control they go to the input box with the other background
  utilities. The new `styling-backgrounds.md` guide covers them, including that UI Toolkit paints the centre
  with or without `fill`.
- Tailwind's `bg-repeat`, `bg-no-repeat`, `bg-repeat-x`, `bg-repeat-y`, `bg-repeat-round` and
  `bg-repeat-space` utilities.
- `StyleOverrides.BackgroundRepeat`, `UnitySliceTop`, `UnitySliceRight`, `UnitySliceBottom`,
  `UnitySliceLeft`, `UnitySliceScale` and `UnitySliceType`, for the same properties computed at runtime. Each
  wins over a utility writing the same property and loses to an important one.

### Fixed

- A pooled element no longer carries `-unity-slice-*` values onto its next consumer.
- When an `animate-gradient` or `animate-shimmer` pan stops, the element's inline `background-size`,
  `background-position` and `background-repeat` come back instead of being reset, and a value written into
  one of them from outside while the pan ran stands.
