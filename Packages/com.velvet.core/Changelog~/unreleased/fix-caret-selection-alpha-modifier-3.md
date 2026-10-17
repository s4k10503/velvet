### Fixed

- Tailwind's bracket type hint (`[length:…]`, `[color:…]`) is read on every utility, not only `text-[length:…]`:
  a utility that writes its bracket through drops it (`w-[length:10px]`, `gap-[length:8px]`), and `border-*`,
  `text-*`, `ring-*`, `outline-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` take the branch it names
  (`border-[length:2PX]` is 2px, `text-[color:#f00]` a colour, `bg-[color:#f00]/50` a half-transparent red).
