### Changed

- A `V.TextField`'s or `V.IntegerField`'s `className` background (`bg-*`), border (`border`, `border-*`),
  radius (`rounded`, `rounded-*`) and padding (`p-*`) utilities, with the state, theme and responsive variants
  written on them, now paint the box the text is typed into, as the same classes do on an `<input>`. They used
  to land on the outer control and leave the theme's own background and border on the box. A field that relied
  on the outer control carrying them (a padded or bordered frame around a labelled field) now has them on the
  box and nothing on the frame. Gradient backgrounds, the `border-solid` / `border-dashed` / `border-dotted`
  line style, `shadow-*`, `ring-*` and every layout utility stay on the outer control.
