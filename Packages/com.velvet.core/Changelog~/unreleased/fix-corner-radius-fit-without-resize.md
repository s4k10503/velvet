### Fixed

- A corner radius that overflows its box and comes in through a class of your own that a re-render adds or removes,
  with no size change, is scaled down the way CSS does it (Backgrounds 3 §5.5) in the same update, where it kept its
  unfitted value until the box next resized. A class of your own is one Velvet does not resolve itself, so the bundled
  utilities, `ring-2` and `uppercase` do not count. It holds on the elements `V.Div`, `V.Button`, `V.Custom<T>` and the
  other element factories build, for a radius from a stylesheet of your own, and needs the bundled stylesheet
  attached. A class change your code makes outside Velvet still waits for the next size change, as does a write to
  `style.border*Radius` after layout. A rule of your own keyed on an ancestor's class or on a pseudo-class Velvet does
  not toggle is seen only where a style event reaches the fit: on an element with a bundled `rounded-*` class, on a
  Label or a Button the fit already tracks, or on one a class of your own has marked.
