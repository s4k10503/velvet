### Fixed

- The missing-stylesheet warning looks when the target's panel next updates rather than at the `V.Mount`
  call, so a sheet attached right after `V.Mount` no longer draws the warning.
- Disposing one of two mounts on the same target no longer stops the missing-stylesheet warning for the
  other.
- `VelvetStyleUtilities.AttachTo` and `BindThemeTo` no longer keep a panel's root alive after the panel is
  disposed; the static theme event held it for the rest of the domain. A bound root now follows
  `VelvetTheme.IsDark` whether or not it is on a panel.
