### Fixed

- A hand-authored `transition-property` naming `filter` together with `background-size` or
  `-unity-background-scale-mode` animates a filter change once, by UI Toolkit's own animation. Velvet's
  `transition-filter` tween ran on the same property at the same time.
