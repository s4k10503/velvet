### Added

- Tailwind's per-side border colors resolve: `border-t-*`, `border-r-*`, `border-b-*`, `border-l-*`, `border-x-*`,
  `border-y-*` and the logical `border-s-*`, `border-e-*`, `border-bs-*`, `border-be-*`, each with a palette name, a
  bracketed color or either with an opacity modifier (`border-b-red-500`, `border-x-[#1e293b]`,
  `border-s-white/40`), under variants and `!`. A side's color wins on that side over a `border-{color}` class of the
  same variant rank, whichever is written first, and two inline colors on one side at the same variant rank order
  by Tailwind's property order. They were left in the class list and resolved nothing.
