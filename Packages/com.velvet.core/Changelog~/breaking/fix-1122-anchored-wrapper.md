### Changed

- `V.Anchored` renders a wrapper element, as drei's `<Html>` does, and puts the element carrying its `className`,
  `name`, `events`, ref, children and `props` inside it. The wrapper owns `position: absolute`, the projected
  `left`/`top`, the hide for a target behind the camera or occluded, and the `distanceFactor` scale; `z-*` classes
  move to it. A `scale-*` class or a Motion scale variant on the element no longer fights `distanceFactor` for
  one style slot. A query for the named element now finds the inner one, so what read its position, display or
  scale through that query reads `.parent` instead, and a selector that assumed the element was the positioned
  one has to allow for the extra level.
