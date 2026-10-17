### Changed

- A value Tailwind composes into an element's box-shadow and the browser rejects now discards the box-shadow,
  as the browser does: a ring colour or offset colour that is no colour (`ring ring-[5PX]`), a ring width that
  is no length (`ring-[length:2]`) and a `shadow-[…]` with a bare non-zero number take the ring and every
  `shadow-*` on the element with them, where the ring used to draw. A `from-` / `via-` / `to-` colour that is
  no colour leaves its stop transparent, and a rejected position returns the stop to its initial one.
- A value the browser rejects in one utility of a family Tailwind composes through one declaration — translate,
  per-axis and uniform scale, skew, the filters — drops the family's other utilities with the same variants and
  importance, as the browser drops the shared declaration: `translate-x-4 translate-y-[10]` no longer translates
  along x, and `drop-shadow-lg blur-[2]` no longer draws its drop shadow. `blur-[-2px]` is declined.
