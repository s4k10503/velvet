### Changed

- `flex-1` sets a basis of `0%`, as Tailwind's `flex: 1` does, where it set `0px`. A `flex-1` child of a
  column that sizes to its content now keeps its content height.

- Two inline values of one property under the same variants resolve as Tailwind orders their names,
  the later one winning: `w-[2px] w-[1px]` is 2px wide, where the value the className listed last won.
  A repeated `filter-[name:…]` still takes the one listed last.

- `border border-0` has no border, as in Tailwind, whose `border-0` sorts after `border`; `border` used to
  win there.
