### Fixed

- A layer or world-space host whose `UIDocument` was disabled long enough for the engine to drop its panel and then
  enabled again came back without Velvet's focus navigation on the new panel, until a portal into it patched, or for
  good when the portal's focus order was `Isolated`. The new panel now gets it as soon as the document builds its root.
