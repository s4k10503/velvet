### Added

- A margin, `w-*`, `h-*`, `size-*`, `basis-*` or inset takes `[auto]` (`mx-[auto]`); padding and the minimum and
  maximum sizes take none.

### Fixed

- A shorthand and a longhand of one family in one `className` (`p-[52px] pt-2`, `m-[52px] mt-2`,
  `m-[52px] mx-auto`, `size-[52px] w-full`) now resolve as Tailwind orders them, the longhand holding its side
  whichever is written first, where the one written last or the inline one held it. It covers padding, margin,
  the insets and `size-*` / `w-*` / `h-*`; a logical utility gives way to the physical one of its side; a
  `V.Motion` pose overrides the node's own spacing; and a spring or bezier play reads the same order. A
  stylesheet class that takes part is written inline, so it leaves the element's class list.
