### Fixed

- A corner radius that overflows its box and comes in through a class of your own that a re-render adds or removes,
  with no size change, is scaled down the way CSS does it (Backgrounds 3 §5.5) in the same update, where it kept its
  unfitted value until the box next resized. It applies to a class whose rule writes a radius, including the first time
  the class is used on a panel. It holds on the elements `V.Div`, `V.Button`, `V.Custom<T>` and the other element
  factories build, and needs the bundled stylesheet attached. A class change your code makes outside Velvet still waits
  for the next size change, as does a write to `style.border*Radius` after layout. A rule of your own keyed on an
  ancestor's class or on a pseudo-class Velvet does not toggle is seen only on an element that receives style
  events: one with a bundled `rounded-*` class, a Label or a Button, or one a class that writes a radius has marked.
