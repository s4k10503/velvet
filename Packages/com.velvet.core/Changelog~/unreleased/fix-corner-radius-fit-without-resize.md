### Fixed

- A corner radius that overflows its box and comes in through a class of your own that a re-render adds or removes,
  with no size change, is scaled down the way CSS does it (Backgrounds 3 §5.5) in the same update, where it kept its
  unfitted value until the box next resized. It holds on the elements `V.Div`, `V.Button`, `V.Custom<T>` and the other
  element factories build, for a radius from a stylesheet of your own, and needs the bundled stylesheet attached. A
  class change your code makes outside Velvet, and a rule of your own keyed on an ancestor's class or on a
  pseudo-class Velvet does not toggle, still wait for the next size change, as does a write to `style.border*Radius`
  after layout.
