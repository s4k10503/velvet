### Changed

- A tween's `PropertyOverrides` leave every property they do not name on the config's own
  `DurationSec`, `Easing` and `DelaySec`, as a value missing from Framer Motion's per-value transition map
  takes the default one. The overrides used to replace the swap's `transition-property: all`, so a property
  they did not name landed at once.

- A `V.AnimatePresence` keyed child whose Motion is created again under the same key, as an `elementType`
  change does, enters again, as Framer mounts the new child of a kept `PresenceChild` with its enter. It used
  to appear at rest, and one whose key returned mid-exit did too. A child the presence's first render created
  under `initial: false` still enters nothing.

- A presence child's Motion that inherits its labels from the Motion above the presence rests at its pose when
  its key returns mid-exit, or when it is added with no `initial` label to enter from, as one naming its own
  labels does. It used to start its transition preset's enter, such as `StyleTransition.Fade`'s, over that pose.
