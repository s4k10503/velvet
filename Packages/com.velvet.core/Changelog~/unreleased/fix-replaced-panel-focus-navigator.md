### Fixed

- A layer or world-space host whose `UIDocument` was disabled long enough for the engine to drop its panel and then
  enabled again came back without what its first root carried: Velvet's focus navigation, the chained focus ring, the
  event bridge to the portal's logical ancestors, the default font family and the declaring panel's viewport. Some of it
  returned on the next patch of a portal into the host and some never did, as the navigator for an `Isolated` portal.
  The new root and panel now get all of it as soon as the document builds the root.
