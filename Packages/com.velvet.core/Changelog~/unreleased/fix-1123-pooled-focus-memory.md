### Fixed

- A pooled element such as a `Button` that held focus when it was unmounted, or was taking it as it was
  unmounted, behaves as a new one when it is mounted again. Mounted again on the same panel, it was already that
  panel's focused element without anything focusing it. Mounted on another panel, its first `Focus()` was undone
  when the panel it left was the one UI Toolkit's runtime event system had focused last, and focus returning to
  that panel raised a `FocusInEvent` on the Button in its new panel. A composite field such as a `Toggle` whose
  focused child is unmounted no longer keeps its focus state.
