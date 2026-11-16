### Fixed

- A root `V.Mount` created from a callback of another root's render, an `onCreated` for example, rendered inside that
  root's pass, and a discrete event's handler fired by that root's apply rendered its own root inside the apply. The
  mount returns its handle at once now, and the roots render once the commit that follows the pass has ended, the
  commit's layout effects and ref setups included: the passive effects of each root whose flush was held, then its
  flush, then the first render of a mounted root.
