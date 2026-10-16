### Changed

- A utility Velvet resolves inline (a bracket value, a fraction, `auto`, a negated step, a colour with an opacity, a
  logical side) and a bundled class that write the same longhand are ordered as Tailwind emits them, where the inline
  one always won: the one Tailwind emits later holds it, whichever is written first. `w-px w-[10px]` and
  `w-[10px] w-px` give 1px (they gave 10px), `w-1/2 w-4` gives 16px (it gave 50%), `mt-[10px] mt-auto` and
  `-mt-2 mt-1` leave the margin to the class, `bg-[#ff0000] bg-red-500` gives red, `border-s-2 border-l-4` gives
  a 4px left border (it gave 2px) and `rounded-lg rounded-[2px]` gives `rounded-lg` (it gave 2px). Where the inline
  token is the one emitted later it still wins (`w-4 w-[10px]` gives 10px). An inline token that the later classes
  write in full is no longer written; the class stays on the element. A token they write only in part stays whole, as it
  did: `border-[3px] border-l-4` still gives every side the bracket. Variants, the `!` modifier and two utilities that
  both resolve inline are unchanged.
