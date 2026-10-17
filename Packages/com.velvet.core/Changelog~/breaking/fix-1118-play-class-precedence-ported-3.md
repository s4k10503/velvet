### Changed

- A `V.Motion` draws `shadow-*`, `skew-*` and `border-dashed` / `border-dotted`, from its className, its
  variants and the `while*Class` and drag channels, as any element does. It used to draw none of them, and
  logged a warning for a shadow.

### Fixed

- A skewed or `border-dashed` element kept painting the background and border colours it had when its skew or
  dashed border attached after a variant, gesture or drag class, a theme change or a play changed them, until
  an unrelated re-render.
