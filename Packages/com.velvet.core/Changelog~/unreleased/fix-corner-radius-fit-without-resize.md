### Fixed

- A corner radius that overflows its box and comes in through a class swap, with no size change, is scaled down the
  way CSS does it (Backgrounds 3 §5.5) in the same update, where it kept its unfitted value until the box next resized.
  It holds on `V.Div`, `V.Button`, `V.Label` and `V.ScrollView`, for a radius from a stylesheet of your own, and needs
  the bundled stylesheet attached. A write to `style.border*Radius` after layout is still fitted at the element's next
  size change or style pass.
