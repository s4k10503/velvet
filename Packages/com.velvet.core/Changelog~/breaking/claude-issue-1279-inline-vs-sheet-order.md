### Changed

- A plain utility Velvet resolves inline (a bracket value, a fraction, `auto`, a negated step, a colour with an
  opacity, a logical side) is ordered against the bundled classes that write one of its longhands as Tailwind emits
  them, where the inline one always won: the one Tailwind emits later holds it, whichever is written first.
  `w-px w-[10px]` and `w-[10px] w-px` give 1px (they gave 10px), `w-1/2 w-4` gives 16px (it gave 50%),
  `mt-[10px] mt-auto` and `-mt-2 mt-1` leave the margin to the class, `bg-[#ff0000] bg-red-500` gives red,
  `border-s-2 border-l-4` gives a 4px left border (it gave 2px), `rounded-lg rounded-[2px]` gives `rounded-lg` (it
  gave 2px) and `text-lg/7 text-sm` gives the `text-sm` size (it gave the slash size). Where the inline token is the
  one emitted later it still wins (`w-4 w-[10px]` gives 10px, `scale-100 -scale-[0.5]` the inline scale). An inline
  token that the later classes write in full is no longer written; the class stays on the element. One they write in
  part keeps the rest: `border-[3px] border-l-4` gives the left border 4px and the other sides 3px, where the
  bracket gave all four 3px. Variants and the `!` modifier are unchanged.
- The bundled classes that carry Velvet's own name for a Tailwind utility are declared where Tailwind's name sorts:
  `rotate-n6` and the other `rotate-n<N>` classes ahead of `rotate-0`, as `-rotate-6` is emitted ahead of `rotate-6`, and
  `duration-fast`, `duration-normal` and `duration-slow` beside `duration-100`, `duration-150` and `duration-200`.
  `rotate-6 rotate-n6` and `rotate-n6 rotate-6` now give 6 degrees (they gave -6), and the same ranking applies against
  an inline utility (`rotate-[10deg] rotate-n6` keeps the inline rotation).
