### Added

- The position utilities take Tailwind's fractions: `top-1/2`, `right-1/4`, `bottom-3/4`, `left-1/2`,
  `inset-1/2`, `inset-x-1/3` and `inset-y-2/3` resolve to a percent of the containing block, and so do their
  negated forms (`-left-1/2`). They accept the same fractions as `w-*` / `h-*` / `size-*`, so
  `absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2` centres an element in its parent. Each was
  previously added to the element's class list as a plain class, where it positioned nothing and logged no
  warning. They apply under variants (`hover:left-1/2`) and drive a `V.Motion` length
  channel the way the sizing fractions do.
