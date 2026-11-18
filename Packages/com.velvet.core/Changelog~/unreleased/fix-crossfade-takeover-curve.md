### Fixed

- A layoutId crossfade, or a rotate, translate, scale or corner-radius take-over, that took over a transition in the
  frame it started carried it on UI Toolkit's own curve where the element's classes or an `ease-[...]` class give a
  different one, such as Tailwind's `ease-in-out` on a `transition-*` utility. It now carries it on the curve the
  classes give, as the transition itself runs once its run event arrives. A transition that started before the element's
  timing classes were watched keeps UI Toolkit's curve.
