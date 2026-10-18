### Fixed

- `border-[thin]`, `border-[medium]` and `border-[thick]` are 1px, 3px and 5px, and `text-[…]` takes the
  font-size keywords (`text-[x-large]`, `text-[larger]`, …), as Tailwind's type tests hand them to
  `border-width` and `font-size`.
