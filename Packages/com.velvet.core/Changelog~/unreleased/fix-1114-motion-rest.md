### Fixed

- A keyed `V.Fragment` is accepted as a `V.AnimatePresence` child, as Framer Motion's `AnimatePresence`
  accepts one: its first Motion is its anchor, and every element it places is held until its Motions' exits
  finish. Under `AnimatePresenceMode.PopLayout` it exits in flow, unpinned. It used to be dropped with an error.
