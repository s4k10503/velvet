### Fixed

- An added or removed `contrast-*` under a whole-property transition (`transition-all`, a bare
  `duration-*`) fades from contrast's identity, `1`, as CSS pads it. UI Toolkit ramped it from `0`, a
  flat grey, because its own contrast definition declares that as the value to pad from. Velvet corrects
  that declaration whenever it composes a `contrast-*`, so once one has been composed a `contrast()` a
  hand-written stylesheet transitions pads from `1` too.

- A hand-authored `transition-property` naming `filter` together with `background-size` or
  `-unity-background-scale-mode` animates a filter change once, by UI Toolkit's own animation. Velvet's
  `transition-filter` tween ran on the same property at the same time.
