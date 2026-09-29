### Fixed

- A portal into a container Velvet renders keeps patching its own children when a render of the same tree
  adds or removes the container's own children ahead of them — the element's own, or a component's among
  them rendering again. The next patch wrote over the container's own child and left the portal's behind.

- `V.Anchored` among a `V.WorldSpace` panel's children sits where the camera's ray to its target crosses that
  panel's plane, and hides where the ray does not cross it. It was placed by a screen-space projection,
  which misplaces it on a world-space panel.

- A portal declared among the children of the container it targets keeps patching its own children when the
  container renders its children again. Its patch logged that the target must not be the element being
  reconciled, and a change to the container's children ahead of it in that render put its children over
  theirs.

- A logical ancestor's `events:` handler fires once for an event raised inside a portal whose target sits in
  another portal's content. The event reached it through both portals.
