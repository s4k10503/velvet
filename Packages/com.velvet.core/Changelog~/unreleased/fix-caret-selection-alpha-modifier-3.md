### Fixed

- Tailwind's bracket type hint (`[length:…]`, `[color:…]`) is read beyond `text-[length:…]`: a utility that writes
  its bracket through drops it (`w-[length:10px]`, `gap-[length:8px]`; an empty hint, `w-[:10px]`, is none), and
  `border-*`, `text-*`, `ring-*`, `outline-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` take the branch it
  names (`border-[length:2PX]` is 2px, `text-[size:16px]` a font size, `text-[color:#f00]` a colour,
  `bg-[color:#f00]/50` a half-transparent red). `drop-shadow-*` and `font-[…]` take no hint, and `filter-[name:args]`
  and `bg-[addr:key]` are Velvet's own colon forms.
- `text-[length:red]/50` is no longer red text at half alpha: the hint names a length, which `red` is not, so the
  class is inert.
