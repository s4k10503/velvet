# Bracketed lengths

A bracketed value such as `w-[120px]`, `top-[1in]` or `translate-x-[25%]` is resolved by Velvet rather
than by a USS rule, because no stylesheet can declare every value a bracket can hold. This guide owns which
lengths a bracket reads.

## What every length bracket reads

| Written | Read as |
|---------|---------|
| `120px`, or a bare `120` | pixels |
| `1.5rem` | pixels, at 1rem = 16px: UI Toolkit has no root font size to take a rem from |
| `1in`, `2.54cm`, `25.4mm`, `72pt`, `6pc`, `101.6Q` | pixels, at CSS's fixed ratio of 96px to the inch |
| `25%` | a percentage, where the utility takes one; a border width takes none, as in CSS |
| `calc()`, `min()`, `max()`, `clamp()` whose terms all come to pixels, or all to percentages | that pixel length or that percentage |

Unit names match case-sensitively, so `Q` is upper case and the rest lower.

A math function follows CSS's grammar with Tailwind's spelling. `_` stands for a space. CSS wants a space
on both sides of a binary `+` or `-`; Tailwind writes `calc(2rem-4px)` and adds the spaces itself, so a
space on neither side reads as one on both, and `calc(2rem-4px)` and `calc(2rem_-_4px)` are the same
length. A space on one side only is declined: `calc(1px_-2px)` is `1px` followed by a second value `-2px`.
A sign belongs to a number (`-2px`, `+.5rem`, `1e3px`), never to a parenthesis or a function, so `-(1px)`
and `-calc(1px)` are declined, and so is a space before the function. Functions nest, `(` groups, and `*`
and `/` take a plain number on one side. `clamp(low, value, high)` is `max(low, min(value, high))`, so a
`low` above `high` wins. What CSS rejects is declined and leaves the class inert: adding a number to a length
(`calc(1px+2)`), multiplying two lengths, dividing by a length, a result that is a plain number (`calc(4)`)
and `clamp()` without three arguments. Valid CSS that is not read here is declined too: any function other
than these four (`var()`, `env()`, `round()`, …), and, as a deliberate deviation, a division by zero and a
length too large for a float, both of which CSS reads by clamping an infinite result to the range of lengths
it supports.

So `top-[calc(1rem+4px)]`, `gap-[min(2rem,24px)]`, `blur-[calc(2px*3)]`,
`shadow-[calc(1rem+4px)_4px_8px_#101820]` and `w-[calc(100%/3)]` resolve as the plain length would.

## Where they are not read

- A math function that mixes a percentage with a pixel length (`w-[calc(50%-1rem)]`) is declined everywhere,
  and leaves the class inert.
- The units only an element or the panel can measure (`em`, `ex`, `ch`, `vw`, `vh`, …) are declined
  everywhere. `leading-[…]` reads `em` itself ([fonts.md](fonts.md)).
- A gradient stop list, a shadow and a `clip-path-[…]` split their bracket into words on `_`, so a math
  function inside one is written without spaces: `bg-linear-[to_right,#000000_0px,#ffffff_calc(1rem+4px)]`.

