### Changed

- A rejected value in a family Tailwind composes through one declaration — translate, per-axis and uniform scale,
  skew, the filters — leaves the family at its identity only where it is the value its property takes, after
  importance, variants and Tailwind's order, as the browser discards the shared declaration there. One that an
  important class, a class behind a variant or a class Tailwind emits later outranks takes nothing
  (`blur-[-2px] blur-sm` blurs), `translate-y-[10] hover:translate-y-4` translates on hover alone, and
  `translate-y-[10] hover:translate-x-4` does not translate on hover either, nor under a `V.Motion` pose or a
  `whileHoverClass` naming the family, unless a pose outranks the rejected value (a `translate-y-4` pose over
  `translate-y-[10]` translates).
