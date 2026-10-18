### Fixed

- `caret-*`, `selection:bg-*` and `selection:text-*` multiply a colour's own alpha by an opacity modifier, as
  `bg-`, `text-` and `border-` do and as Tailwind's `color-mix` with `transparent` does, so
  `caret-[#ff000080]/50` is a quarter opaque rather than half.
- A bracketed length's unit is read in any case, as CSS reads it, so `w-[10PX]`, `top-[1IN]`, `w-[2EM]`
  and `leading-[1.5EM]` apply where they used to leave the class inert. `border-*`, `text-*`, `ring-*`,
  `outline-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` still take a length only in Tailwind's case,
  as Tailwind does, except inside a math function or behind `text-[length:…]`.
- An angle or a time in a bracket is read in any case, as CSS reads it: `rotate-[45DEG]`, `hue-rotate-[.5TURN]`,
  `duration-[150MS]`, `animate-spin-[2S]`. `skew-x-[…]` takes any angle unit, not only `deg`, and a bare `0`.
- A math function is spaced as Tailwind spaces it before it is read, so an operator spaced on one side,
  `calc(1px_-2px)`, subtracts where it was declined, and a function name is read in any case: `cALC(1px_+_2px)`
  is 3px, while `cALC(1px+2px)`, which Tailwind leaves unspaced, is declined as CSS declines it. A space at
  either end of a length (`top-[_calc(1px)]`, `w-[1px_]`) is trimmed as the browser trims it.
