### Changed

- A math function whose result falls below the range its property takes is raised into it, as CSS clamps a
  calculation: `w-[calc(-5px)]`, `p-[calc(-5px)]`, `rounded-[…]`, `border-[…]`, `text-[…]`, `basis-[…]` and
  `blur-[…]` stop at 0 where they wrote the negative value, `opacity-[calc(2)]` is 1 and `opacity-[calc(-1)]` 0
  where both were declined, and a negative `duration-[…]`, `grow-[…]` or filter amount is 0.
