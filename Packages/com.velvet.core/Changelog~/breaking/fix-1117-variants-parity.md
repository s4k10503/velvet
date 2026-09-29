### Changed

- A `ring-*` / `outline-*` band moves with its element's own transform, as a CSS outline or box-shadow
  does. A `translate-*`, `scale-*` or `rotate-*` on the ringed element, about any `origin-*`, and a
  transition or animation driving one, are copied onto the band once per frame; the band used to stay on
  the element's untransformed box.

- A `ring-*` / `outline-*` band takes its element's own opacity, `invisible` / `visible` and `hidden`, as
  an outline or box-shadow does. The band used to stay opaque, visible and displayed whatever the element
  set.

- A `ring-*` / `outline-*` on a `V.Motion` renders, and follows the Motion's animated transform and
  `layoutId` plays. It used to be ignored with a warning.
