### Fixed

- `origin-[…]` takes CSS `transform-origin`'s grammar inside the brackets: the `left` / `center` / `right` /
  `top` / `bottom` keywords, alone or mixed with a length (`origin-[left_20px]`), a keyword-only pair in
  either order (`origin-[bottom_left]`), and a third component as the z length (`origin-[50%_50%_10px]`).
  Each of those was refused. A pair CSS declares invalid, such as `origin-[top_20px]`, is still refused.

- Adding or removing `@container` in an element's `className` re-points the responsive variants of
  descendants that are already attached, as a CSS container query re-evaluates when an ancestor gains or
  loses `container-type`. They used to keep the width source they bound when they attached.

- `has-[.foo]:` matches a descendant whose `className` holds `foo` while a variant or an important utility
  keeps `foo` off that descendant's class list, as `:has(.foo)` tests the class attribute rather than which
  declarations won. It used to stop matching.
