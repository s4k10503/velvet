### Changed

- The classes a play shows — a `V.Motion` pose's, an `AnimatePresence` enter or exit class — apply bracket
  values and the utilities Velvet realises itself (`gap-*`, `shadow-*`, `ring-*`, `skew-*`, gradients,
  `animate-*`, `border-dashed`, `uppercase`, …) while the play shows them, and one of the latter that the
  element's resting pose carries stops applying while the play has the pose's class off. A play's class used
  to apply only a utility backed by a plain USS rule.
