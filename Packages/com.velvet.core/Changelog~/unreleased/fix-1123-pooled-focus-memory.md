### Fixed

- Unmounting an element from a live panel no longer leaves focus behind in that panel, so a pooled element such
  as a `Button` that held focus behaves as a new one when it is mounted again. Mounted again on the same panel,
  it was already that panel's focused element without anything focusing it. Mounted on another panel, its first
  `Focus()` was undone when the panel it left was the one UI Toolkit's runtime event system had focused last,
  and focus returning to that panel raised a `FocusInEvent` on the Button in its new panel.
