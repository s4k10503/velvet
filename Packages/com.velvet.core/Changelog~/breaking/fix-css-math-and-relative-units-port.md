### Changed

- A math function whose result falls below the range its property takes is raised into it, as CSS clamps a
  calculation: `w-[calc(-5px)]`, `p-[calc(-5px)]`, `rounded-[…]`, `border-[…]`, `text-[…]`, `basis-[…]` and
  `blur-[…]` stop at 0 where they wrote the negative value, `opacity-[calc(2)]` is 1 and `opacity-[calc(-1)]` 0
  where both were declined, and a negative `duration-[…]`, `grow-[…]` or filter amount is 0.
- A negative literal where the CSS property takes none leaves the class inert, as the browser rejects it:
  `w-[-5px]`, `p-[-5px]`, `rounded-[-2px]`, `border-[-2px]`, `text-[-4px]`, `basis-[-4px]` and `w-[-2em]` used to
  write the negative value. `opacity-[2]` is 1 and `opacity-[-1]` 0, where both were declined.
- A math function of a type the browser does not take in a family Tailwind composes through one declaration
  discards that declaration, as a rejected literal does: `translate-x-4 translate-y-[calc(45deg)]` translates
  nowhere, where the x translation used to apply.
- A math function of a type the browser does not take in a family Tailwind composes through one declaration
  discards that declaration, as a rejected literal does: `translate-x-4 translate-y-[calc(45deg)]` translates
  nowhere, where the x translation used to apply.
