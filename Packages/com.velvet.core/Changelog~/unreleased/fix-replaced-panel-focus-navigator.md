### Fixed

- A layer or world-space host whose `UIDocument` was disabled long enough for the engine to drop its panel and then
  enabled again showed nothing until a render of the component that declared its portal patched the portal, and came
  back without what its first root carried: Velvet's focus navigation, the chained focus ring, the event bridge to the
  portal's logical ancestors, the default font family and the declaring panel's viewport. Some of that returned on the
  next patch and some never did, as the navigator for an `Isolated` portal. The portal's children now mount into the
  new root and the rest is applied to it as soon as the document builds the root. Components inside the host mount
  again, so their state starts over.
