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

### Added

- The `mauve`, `olive`, `mist` and `taupe` palette hues, in the eleven shades the others have.
