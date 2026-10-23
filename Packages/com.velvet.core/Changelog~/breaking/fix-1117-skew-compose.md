### Changed

- `skew-*` composes with `rotate-*`, `scale-*`, an `animate-*` preset and a `V.Motion` as CSS does, so an element
  that carries any of them is drawn as its own transform too: the individual `rotate` and `scale`, and then
  `skewX() skewY()`, are written together into the element's rotate and scale slots, and written again when a
  class, a variant, a play or a `layoutId` move changes either. The caster's text and descendants shear with it,
  `origin-*` moves the pivot, a pointer picks the drawn shape, and the angles a `V.Motion` animates are written into
  the same slots. A rule of your own stylesheet that sets `rotate` or `scale` on a skewed element is not composed.

### Removed

- The painted skew layer: the sheared silhouette, the descendant `translate` seats and the gradient silhouette
  shader, which no element reaches now. A `shadow-*`, a gradient and a `border-dashed` on a skewed element are drawn under the element's
  transform with the rest of its face. Project Settings ▸ Velvet lists one shader fewer.
