### Fixed

- `caret-*`, `selection:bg-*` and `selection:text-*` multiply a colour's own alpha by an opacity modifier, as
  `bg-`, `text-` and `border-` do and as Tailwind's `color-mix` with `transparent` does, so
  `caret-[#ff000080]/50` is a quarter opaque rather than half.
