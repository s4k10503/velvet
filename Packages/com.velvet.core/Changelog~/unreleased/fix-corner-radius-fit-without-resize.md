### Fixed

- A corner radius that overflows its box and comes in with no size change is scaled down the way CSS does it
  (Backgrounds 3 §5.5) in the same update, where it kept its unfitted value until the box next resized. This holds
  when a class changes, whether your code or Velvet changed it, when a class on an ancestor that a rule of yours keys
  on changes, and when a pseudo-class like `:hover` begins to match. Velvet reads the stylesheets above a mount, the
  panel's own and an element inside the tree, with what they import, and attaches a runtime-only sheet beside each one
  that has a radius rule; your own sheets are not edited. A sheet attached later to an ancestor of an element Velvet
  built is read within 16 ms; one attached to a leaf that held none at its last size change waits for the next. A
  write to `style.border*Radius` after layout is fitted at the element's next size change, the next time a radius rule
  starts or stops matching it, or when Velvet adds or removes a class on it.
