### Changed

- A value Tailwind composes into an element's box-shadow and the browser rejects now discards the box-shadow,
  as the browser does: a ring colour or offset colour that is no colour (`ring ring-[5PX]`), a ring width that
  is no length (`ring-[length:2]`) and a `shadow-[…]` that is no box-shadow (`shadow-[0_4_8_#000]`,
  `shadow-[4px]`) take the ring and every `shadow-*` on the element with them, where the ring used to draw. A
  `from-` / `via-` / `to-` colour that is no colour leaves its stop transparent, and a position that is no length
  or percentage returns the stop to its initial one. A function, `var()`, `currentColor`, a colour name or a unit
  Velvet does not read is not taken for a rejected value.
- A value the browser rejects in one utility of a family Tailwind composes through one declaration — translate,
  per-axis and uniform scale, skew, the filters — drops the family's other utilities, as the browser drops the
  shared declaration: `translate-x-4 translate-y-[10]` and `!translate-y-[10] translate-x-4` no longer translate
  along x, and `drop-shadow-lg blur-[2]` no longer draws its drop shadow. Only the value each property holds
  counts, so `blur-[2] blur-md` still blurs. Under a variant the family is off while the variant applies:
  `translate-x-4 hover:translate-y-[10]` stops translating while hovered. `blur-[-2px]` is declined.
