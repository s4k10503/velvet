### Changed

- An `AnimatePresence` child removed with nothing to animate — no `V.Motion` in it, or a `StyleTransitionConfig.None`
  or zero-duration exit — stays on screen for the render that removed it and leaves on the render its exit's
  completion asks for, as Framer Motion's `AnimatePresence` drops a child only on a render after its exit completes.
  That exit completes in the passive-effect flush after the removing render. It used to leave in the removing
  render itself. Under `AnimatePresenceMode.Wait` such a child now holds the new children back until that later
  render, as an animated exit does; they used to mount in the removing render.

- Under `AnimatePresenceMode.Wait`, while a removed child is exiting the presence renders the exiting children
  alone, as Framer Motion's `mode="wait"` does: a child that stays is not rendered until the exits complete, so one
  already mounted unmounts with no exit and mounts again with its enter. It used to stay mounted, and only a key
  new to the presence was held back.
