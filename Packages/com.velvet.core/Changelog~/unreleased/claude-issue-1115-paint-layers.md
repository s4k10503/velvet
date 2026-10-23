### Added

- Spring and Bezier Motion plays now animate the paint layers their poses name: `shadow-*` layer by layer, `skew-*`
  per axis and `bg-gradient-*` stop by stop (stepping at one half where the two gradients share no stop list), and
  `z-*` swaps at the end of the play. A skew, shadow or gradient exit no longer lands with the swap.
