### Changed

- Two base utilities that both resolve inline (a bracket value, a fraction, a negated step, a colour with an
  opacity, a logical side, a per-side border colour) and write the same property are ordered as Tailwind emits them,
  where the one written last used to win: `w-[2px] w-[1px]` gives 2px (it gave 1px), `w-[10px] w-1/2` gives 10px (it
  gave 50%), `bg-red-500/50 bg-[#ff0000]` gives the half-transparent red (it gave `#ff0000`),
  `border-b-red-500 border-b-[#0000ff]` leaves the bottom border red, and the same holds for `!w-[2px] !w-[1px]`,
  the transforms (`scale-[2] scale-[1.5]`, `translate-x-[8px] translate-x-[4px]`), `opacity-[.7] opacity-[.3]`, the
  filters (`blur-[4px] blur-[2px]`) and the other properties an inline token writes. The earlier token is no longer
  written, and taking the later one away brings it back. A custom filter is ordered per registered name. A slash text
  size (`text-[14px]/6`) is kept beside a later font size, since its line height is read off the token.
- An inline border width that later inline tokens write in part is cut down to the sides they leave:
  `border-t-[4px] border-[3px]` gives the top 4px and the other sides 3px, where the bracket gave all four 3px.
- A stylesheet class written inline beside a narrower inline utility is ordered by the name it was written with, not
  by its inline form: `size-[10px] w-[10px] w-4` gives a 10px width (it gave 16px), `pt-[10px] pt-4 p-[3px]` gives a
  10px top padding (it gave 16px), and `pt-4 pt-2 p-[3px]` gives a 16px one (it gave 8px). The same applies behind a
  variant and under `!`.
