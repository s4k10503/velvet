### Changed

- The opacity modifier on `bg-` / `text-` / `border-` multiplies the colour's own alpha instead of replacing
  it, as Tailwind v4's `color-mix(in oklab, <colour> N%, transparent)` does. Only a colour that already has
  alpha changes: `bg-[#ff000080]/50` is now a quarter opaque where it was half, and `bg-transparent/50`, which
  resolved to black at half alpha, now stays fully transparent, as it does in Tailwind. `bg-red-500/50` and
  `bg-[#ff0000]/50` are unchanged.
