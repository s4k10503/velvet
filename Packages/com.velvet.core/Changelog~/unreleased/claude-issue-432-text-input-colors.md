### Added

- `caret-*`, `selection:bg-*` and `selection:text-*` colour a text field's caret and selection, as
  Tailwind's `caret-color` and `selection:` utilities do:
  - each takes a palette colour, `white`, `black`, `transparent`, a bracketed value or an opacity
    modifier, which multiplies the colour's own alpha as `bg-`'s does, and `caret-current` follows the
    text colour, `caret-current/50` at half its alpha;
  - variants apply, a field takes the nearest ancestor's `caret-*`, and `-inherit` passes to the
    parent;
  - competing utilities resolve in the order Tailwind emits them, not the order the classes were
    added in, and a `selection:*` on the field competes with every ancestor's, as Tailwind's
    `& *::selection, &::selection` does;
  - a colour goes back to the theme's, or the field's own, when no utility asks for one;
  - while a selection utility applies and the field holds focus and a non-empty selection, the
    selected text is drawn again above UI Toolkit's selection highlight, so an opaque highlight reads
    as a background.
- `shadow-<colour>` and `shadow-[<colour>]`, with an opacity modifier (`shadow-red-500/50`), colour the
  `shadow-*` beside them, as Tailwind's `--tw-shadow-color` does. `shadow-current` and `shadow-inherit` are
  read, and a size takes an opacity modifier (`shadow-lg/20`, `shadow/20`).
- `ring-offset-<colour>` colours the offset fill, and a ring, ring-offset or outline colour takes an opacity
  modifier (`ring-red-500/50`).
- `ring-<n>`, `outline-<n>`, `ring-offset-<n>` and `outline-offset-<n>` take any integer Tailwind generates
  (`ring-3`, `outline-12`), where only 0, 1, 2, 4 and 8 were read, and `-outline-offset-<n>` and
  `outline-offset-[-2px]` draw the outline inside the element's edge.

### Fixed

- A bracketed length's unit is read in any case, as CSS reads it, so `w-[10PX]`, `top-[1IN]`, `w-[2EM]`
  and `leading-[1.5EM]` apply where they used to leave the class inert. `border-*`, `text-*`, `ring-*`,
  `outline-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` still take a length only in Tailwind's case,
  as Tailwind does, except inside a math function or behind `text-[length:…]`.
- An angle or a time in a bracket is read in any case, as CSS reads it: `rotate-[45DEG]`, `hue-rotate-[.5TURN]`,
  `duration-[150MS]`, `animate-spin-[2S]`. `skew-x-[…]` takes any angle unit, not only `deg`, and a bare `0`.
- A math function is spaced as Tailwind spaces it before it is read, so an operator spaced on one side,
  `calc(1px_-2px)`, subtracts where it was declined, and a function name is read in any case: `CALC(1px_+_2px)`
  is 3px, while `CALC(1px+2px)`, which Tailwind leaves unspaced, is declined as CSS declines it. A space at
  either end of a length (`top-[_calc(1px)]`, `w-[1px_]`) is trimmed as the browser trims it.
- `border-[thin]`, `border-[medium]` and `border-[thick]` are 1px, 3px and 5px, and `text-[…]` takes the
  font-size keywords (`text-[x-large]`, `text-[larger]`, …), as Tailwind's type tests hand them to
  `border-width` and `font-size`.
- Tailwind's bracket type hint (`[length:…]`, `[color:…]`) is read on every utility, not only `text-[length:…]`:
  a utility that writes its bracket through drops it (`w-[length:10px]`, `gap-[length:8px]`), and `border-*`,
  `text-*`, `ring-*`, `outline-*`, `ring-offset-*` and `from-*` / `via-*` / `to-*` take the branch it names
  (`border-[length:2PX]` is 2px, `text-[color:#f00]` a colour, `bg-[color:#f00]/50` a half-transparent red).
