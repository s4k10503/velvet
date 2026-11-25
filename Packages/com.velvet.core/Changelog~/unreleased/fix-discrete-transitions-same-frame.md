### Fixed

- On an element Velvet renders, a transition of a property UI Toolkit can only switch, such as `justify-content`,
  that a style pass starts, as a class change or a `V.Motion` `Tween` variant swap does, is cancelled in that pass
  where a `transition-*` or `duration-*` utility, a `duration-[…]` value or a `Tween` gave the element its
  transition, or where the element has run a transition before. The frame the pass paints shows the new value, a
  child with no transition of its own that inherits the property takes it in the same pass, and no transition event
  is sent for it, as CSS sends none under `transition-behavior: normal`.
