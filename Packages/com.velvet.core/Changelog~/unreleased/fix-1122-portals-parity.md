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

- A portal whose id is registered from a `refCallback` written as an inline lambda, a new delegate at every
  render, mounts its children. The old callback's cleanup ran where the element was patched, unregistering
  the id ahead of the portal's patch in that render, and the registration that followed asked for another
  render, which did the same again. A callback whose identity changes now has its old cleanup run at the end
  of the pass, ahead of every setup the pass runs, as React detaches and attaches refs in its commit.

- A ref cleanup that throws into an error boundary when its callback's identity changes leaves the rest of
  that render to commit. It ran inside the render, where the boundary's catch stopped the components after it
  from rendering.
