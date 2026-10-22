### Fixed

- A `layoutId` Motion whose bundled `scale-*`, `rotate-*` or `origin-*` class changes in the render that hands it
  the lead, or while its move runs, is drawn at its new scale and scaled about its new origin from the frame of that
  render, and a lead moving from another holder's box mixes towards its new rotate from that frame. This holds where
  the bundled stylesheet is attached at or above the Motion and no rule other than an ungated bundled utility could
  give it that property; a pseudo-class-gated variant, an `anim-*` class or a rule of another stylesheet is taken
  once the styles have. That frame drew it with the classes it had before the render.
