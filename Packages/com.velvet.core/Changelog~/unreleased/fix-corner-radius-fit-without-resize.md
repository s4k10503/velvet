### Fixed

- A corner radius that overflows its box and comes in with no size change is scaled down the way CSS does it
  (Backgrounds 3 §5.5) in the same update, where it kept its unfitted value until the box next resized. This holds
  whatever brought the radius: a class your code or Velvet adds or removes, a class on an ancestor that a rule of
  yours keys on, or a pseudo-class like `:hover`. Velvet reads the stylesheets attached to the mounted element or one of
  its ancestors, and what they import, and writes a custom property into each rule that declares a radius in memory
  (in the editor it is taken back before an assembly reload and when play mode ends). A stylesheet attached to an
  element inside the tree, or the panel's theme sheet, is not read. A write to `style.border*Radius` after layout is
  still fitted at the element's next size change or next style pass.
