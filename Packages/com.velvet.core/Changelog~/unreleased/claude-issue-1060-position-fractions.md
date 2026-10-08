### Added

- The position utilities take Tailwind's fractions: `top-1/2`, `right-1/4`, `bottom-3/4`, `left-1/2`,
  `inset-1/2`, `inset-x-1/3` and `inset-y-2/3` resolve to a percent of the containing block, and so do their
  negated forms (`-left-1/2`). `absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2` centres an
  element in its parent. Each was previously added to the element's class list as a plain class, where it
  positioned nothing and logged no warning. They apply under variants (`hover:left-1/2`) and drive a
  `V.Motion` length channel the way the sizing fractions do.
- The position utilities take `full` and `-full` (`left-full`, `-top-full`, `inset-x-full`), which resolve to
  100% and -100%.
- Fractions take any `a/b` of whole numbers with a non-zero denominator, as in Tailwind: `left-3/2` is 150%,
  `w-1/7` is a seventh. `min-w-`, `min-h-`, `max-w-`, `max-h-` and `basis-` take them too; each was previously
  added to the class list as a plain class and did nothing. A denominator of zero stays unrecognised.
- `translate-x-` and `translate-y-` (and their negated forms) take any `a/b` of whole numbers with a non-zero
  denominator, where they previously took only halves, thirds and quarters.
- `top-auto`, `right-auto`, `bottom-auto`, `left-auto`, `inset-auto`, `inset-x-auto` and `inset-y-auto` write an
  inline `auto` offset, which overrides a value the stylesheet supplies. Each was previously added to the class
  list as a plain class and did nothing.
