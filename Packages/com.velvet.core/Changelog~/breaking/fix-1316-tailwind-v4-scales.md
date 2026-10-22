### Changed

- The palette is Tailwind v4's, where it was v3's: `bg-red-500` is `#fb2c36` rather than `#ef4444`, and a palette
  name takes its v4 colour wherever a colour is taken (`text-*`, `border-*`, `ring-*`, `divide-*`, a gradient stop,
  …). Each OKLCH colour is converted to sRGB with a channel outside 0–1 clipped. 185 of the 242 colours v3 had
  change; write the old value in brackets (`bg-[#ef4444]`) to keep one.
- `ring-<n>`, `outline-<n>`, `ring-offset-<n>` and `outline-offset-<n>` take any whole number of pixels, as Tailwind v4
  does. `ring-3` drew no ring and `ring-offset-3` no offset; both now draw 3px.
- `inset-shadow-2xs`, `inset-shadow-xs`, `inset-shadow-sm` and `inset-shadow-[…]` draw their inset layers, beside any
  `shadow-*`, and `inset-shadow-none` removes them. They drew nothing. On a text field they style the input box, as
  `shadow-*` does.
- `shadow-<color>`, `inset-shadow-<color>` and their `/N` colour the layers of their own family, and a size's `/N`
  (`shadow-lg/50`) sets its layers' alpha, as Tailwind v4 does. They drew nothing, and `shadow-[#f00]` now colours the
  shadow rather than reading as no shadow.
- `shadow-inherit`, `inset-shadow-inherit` and `inset-ring-inherit` take their family's colour from the parent
  element, as Tailwind v4's `inherit` does for the colour variable. They were ignored.
- `inset-ring`, `inset-ring-<n>`, `inset-ring-[…]` and `inset-ring-<color>` draw an inset band, in the element's text
  colour where no colour class names one. They drew nothing.
- `-outline-offset-<n>`, `-outline-offset-[…]` and a negative `outline-offset-[…]` draw the outline inside the
  element's edge. They drew it at the edge.
- The role colours of the default theme that were Tailwind v3 palette colours (`--color-primary`, `--color-danger`,
  `--color-text`, the light border and surface ladders, the dark background, …) take the matching v4 palette colour:
  `bg-primary` is `rgb(21, 93, 252)` rather than `rgb(37, 99, 235)`.

### Added

- The `mauve`, `olive`, `mist` and `taupe` palette hues, in the eleven shades the others have.
