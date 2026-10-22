### Changed

- A value Tailwind composes into an element's box-shadow and the browser rejects now discards the box-shadow,
  as the browser does: a ring colour or offset colour that is no colour (`ring ring-[5PX]`), a ring width that
  is no length (`ring-[length:2]`, `ring-[length:0]`, `ring-[length:50%]`), a `shadow-[…]` that is no box-shadow
  (`shadow-[0_4_8_#000]`, `shadow-[4px]`), a shadow colour that is no colour where a shadow reads it
  (`shadow-md shadow-[#ggg]`), and the same values written through `inset-ring-*` and `inset-shadow-*`, take the
  ring and every `shadow-*` on the element with them, where the ring used to draw. Each value is the one Tailwind
  emits last, whatever order the classes are written in, so `ring-[length:2] ring-4` discards while
  `ring-2 ring-red-500 ring-[5PX]` draws a red ring. A
  `from-` / `via-` / `to-` colour that is no colour leaves its stop transparent, and a position that is no length
  or percentage returns the stop to its initial one. A function, `var()`, `currentColor`, a colour name or a unit
  Velvet does not read is not taken for a rejected value.
- A value the browser rejects in one utility of a family Tailwind composes through one declaration — the
  per-axis translate and scale, the per-axis skew, the filters — now draws nothing for the whole family wherever
  it holds its property, as the browser drops the shared declaration: `translate-x-4 translate-y-[10]` and
  `!translate-y-[10] translate-x-4` no longer translate along x, and `drop-shadow-lg blur-[2]` no longer draws its
  drop shadow. Which value holds is decided as for any value, by importance, then variant rank, then Tailwind's
  order, so `blur-[2] blur-md` still blurs, `translate-x-4 hover:translate-y-[10]` stops translating only while
  hovered, and `translate-x-4 hover:translate-y-[10] focus:translate-y-2` translates while hovered and focused.
  `blur-[-2px]` is such a value too.
