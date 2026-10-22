### Fixed

- A `layoutId` Motion whose bundled `scale-*`, `rotate-*` or `origin-*` class changes in the render that hands it
  the lead, or while its move runs, is drawn at its new scale, mixed towards its new rotate and scaled about its
  new origin from the frame of that render, where no rule other than the bundled utilities could give it that
  property. That frame drew it with the classes it had before the render.

- A `layoutId` Motion drawn at a rotate other than a quarter turn inside one stretching by different factors on its
  two axes is drawn turned and unsheared at its own size, as Framer Motion draws it, on a screen panel where nothing
  around it clips. It was drawn sheared by the stretch at an angle to its axes. While it is drawn that way the panel
  picks it anywhere in the box bounding it.
