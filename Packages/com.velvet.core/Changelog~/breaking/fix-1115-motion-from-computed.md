### Changed

- A `Spring` or `Bezier` slot whose swapped-out classes name no value now starts from its current inline
  value, including a running Spring or Bezier frame, otherwise from its resolved value. A native
  transition on that slot supplies its displayed value before the custom play takes over. A color or length
  named only by the swapped-in classes used to land instantly, as did a pair in different units
  (`w-1/2` against `w-[60px]`); opacity, translate, scale and rotate started from their identity values.
- A `Spring` or `Bezier` variant swap leaves alone a slot the element's own classes hold through the swap. An
  inline-resolved class of its own (`pt-[2px]` beside a `p-0` → `p-8` swap) used to be driven toward the
  swap's value and jump back when the play ended.
- A nonuniform sampled scale or set of border colors is not collapsed into one target-only uniform channel.
- Computed plans retain resting important holders, including inline holders that have no class-list token;
  an important inline token the swap itself replaces still yields to its new target.

- Native transitions on a clip wrapper no longer intercept a custom length channel, including when
  the element wraps or unwraps during a play.
