### Fixed

- On an element that carries a `rounded-*` class or an arbitrary radius, the radius fit scales the radius a rule of your
  own gives a corner that outranks the class there, and scales a radius your code writes to `style` with the others,
  as CSS does. Both used to be left unscaled, and the class's radius replaced the rule's once it was fitted.
