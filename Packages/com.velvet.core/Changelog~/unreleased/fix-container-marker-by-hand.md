### Fixed

- A container variant (`@md:`, `@container/name`'s `@md/name:`) follows a container marker written straight to
  a class list, with `AddToClassList("@container")` or `RemoveFromClassList`, within a frame. It re-pointed at
  the marked element's next render, which never came for an element that renders nothing of its own.
