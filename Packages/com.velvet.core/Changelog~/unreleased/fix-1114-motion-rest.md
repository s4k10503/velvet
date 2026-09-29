### Fixed

- A keyed `V.Fragment` is accepted as a `V.AnimatePresence` child, as Framer Motion's `AnimatePresence`
  accepts one: its first Motion is its anchor, and every element it places is held until its Motions' exits
  finish. It used to be dropped with an error.
