### Changed

- Two base utilities that both resolve inline (a bracket value, a fraction, a negated step, a colour with an
  opacity, a logical side, a per-side border colour) and write the same property are ordered as Tailwind emits them,
  where the one written last used to win: `w-[2px] w-[1px]` gives 2px (it gave 1px), `w-[10px] w-1/2` gives 10px (it
  gave 50%), `bg-red-500/50 bg-[#ff0000]` gives the half-transparent red (it gave `#ff0000`),
  `border-b-red-500 border-b-[#0000ff]` leaves the bottom border red, and the same holds for `!w-[2px] !w-[1px]`,
  the transforms (`scale-[2] scale-[1.5]`, `translate-x-[8px] translate-x-[4px]`), `opacity-[.7] opacity-[.3]`, the
  filters (`blur-[4px] blur-[2px]`) and the other properties an inline token writes. The earlier token is no longer
  written, and taking the later one away brings it back. A custom filter is ordered per registered name.
- An inline border width that later inline tokens write in part is cut down to the sides they leave:
  `border-t-[4px] border-[3px]` gives the top 4px and the other sides 3px, where the bracket gave all four 3px.
- A stylesheet class written inline beside a narrower inline utility is ordered by the name it was written with, not
  by its inline form: `size-[10px] w-[10px] w-4` gives a 10px width (it gave 16px), `pt-[10px] pt-4 p-[3px]` gives a
  10px top padding (it gave 16px), and `pt-4 pt-2 p-[3px]` gives a 16px one (it gave 8px). The same applies behind a
  variant and under `!`.
- A slash text size (`text-[14px]/6`) beside a plain font size (`text-[16px]`) gives the plain size's font size and
  the slash token's line height in either order, as Tailwind emits the slash form first: `text-[16px] text-[14px]/6`
  gives 16px (it gave 14px).
- The utilities a C# resolver folds from the whole class list are put in the order Tailwind emits them, so the one
  Tailwind emits last wins whichever is written first, where the last token written used to: the font facets
  (`font-light font-bold` and `font-bold font-light` give light, `not-italic italic` gives not italic,
  `font-[700] font-[600]` gives 700), the text effects (`uppercase lowercase` gives upper case, `underline overline`
  underline, `text-pretty text-balance` pretty, `leading-6 leading-4` the 6 step), `z-*`, `gap-*` / `space-*`,
  `grid-cols-*`, `divide-*`, `shadow-*` (`shadow-sm shadow-lg` gives the small shadow), `ring-*` (an `outline-*`
  outranks it: `ring-0 outline-2` gives a 2px outline), `clip-path-*`, `skew-*`,
  the gradient shape and stops (a keyword or integer `bg-linear-*` loses to every other shape: `bg-linear-to-r bg-conic`
  gives conic), `pointer-events-*`, `bg-[addr:…]`, `border-solid` and the other line styles, and `animate-*`
  (`animate-spin animate-none` spins). A class added to an element in a later render takes the place a fresh mount
  gives it, so a clip written as `clip-path-[inset(10%)]` and re-rendered with `clip-path-[circle(40%)]` added
  resolves as it does when mounted that way. The nine-slice
  insets, which Tailwind has no utility for, follow the sort key: `slice-t-[2] slice-y-[8]` gives the top 2 and the
  bottom 8, where the top was 8.
